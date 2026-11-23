using System.Text;
using Ake.Core.Model;

namespace Ake.Ai;

/// <summary>
/// Modelin istem bağlamı: projenin kısa, tag'li özeti. Tam JSON yerine yalnızca patch'in atıf yaptığı şeyleri verir: yapı düğümleri (yeni cihaz
/// bunlardan birine konur), cihazlar (tag, tür, açıklama, bağlantı noktaları) ve bağlantılar. Büyük projede istemde adı geçen cihazlar ve
/// bağlantı komşuları seçilir; böylece bağlam küçük kalır ve yanıt hızlanır.
/// </summary>
public static class ProjectSummary
{
    /// <summary>Bu kadar cihazdan küçük projelerin tamamı özete girer.</summary>
    public const int FullLimit = 40;

    private const int MaxConnections = 400;

    public static string Build(Project project, string? request = null, int fullLimit = FullLimit)
    {
        ArgumentNullException.ThrowIfNull(project);

        var devices = project.Devices.Where(device => device.Kind != DeviceKind.Junction).ToList();
        var shown = devices.Count <= fullLimit || string.IsNullOrWhiteSpace(request)
            ? devices.Take(Math.Max(devices.Count, 0)).ToList()
            : Relevant(project, devices, request);

        var text = new StringBuilder();
        text.AppendLine($"Sayfalar: {string.Join(", ", project.Pages.Select(page => $"{page.Number} {page.Title}"))}");
        text.AppendLine($"Fonksiyon düğümleri: {string.Join(", ", project.Structure.Where(node => node.Aspect == Aspect.Function).Select(node => '=' + project.StructurePath(node.Id)))}");
        text.AppendLine($"Yer düğümleri: {string.Join(", ", project.Structure.Where(node => node.Aspect == Aspect.Location).Select(node => '+' + project.StructurePath(node.Id)))}");
        text.AppendLine(shown.Count < devices.Count
            ? $"Cihazlar (istekle ilgili {shown.Count}/{devices.Count}; tag — tür — açıklama — bağlantı noktaları):"
            : "Cihazlar (tag — tür — açıklama — bağlantı noktaları):");
        foreach (var device in shown)
        {
            var points = string.Join(',', device.Functions.SelectMany(function => function.ConnectionPoints).Select(point => point.Name));
            text.AppendLine($"- {project.DesignationOf(device)} — {device.Kind} — {device.Description} — {points}");
        }

        var shownPoints = shown.SelectMany(device => device.Functions.SelectMany(function => function.ConnectionPoints.Select(point => point.Id))).ToHashSet();
        text.AppendLine("Bağlantılar:");
        foreach (var connection in project.Connections
            .Where(connection => shown.Count == devices.Count || shownPoints.Contains(connection.From) || shownPoints.Contains(connection.To))
            .Take(MaxConnections))
        {
            text.AppendLine($"- {project.DesignationOf(connection.From)} → {project.DesignationOf(connection.To)}"
                + (connection.Wire?.Number is { } number ? $" (tel {number})" : string.Empty));
        }

        return text.ToString();
    }

    // İstekte tag'i ya da açıklaması geçen cihazlar ve onlara bağlı komşular.
    private static List<Device> Relevant(Project project, List<Device> devices, string request)
    {
        var wanted = KnowledgeBase.Tokens(request);
        var direct = devices
            .Where(device => KnowledgeBase.Tokens($"{project.DesignationOf(device)} {device.Description}").Overlaps(wanted)
                || request.Contains(project.DesignationOf(device).ToString(), StringComparison.Ordinal)
                || request.Contains('-' + device.ProductTag, StringComparison.Ordinal))
            .ToList();

        var directPoints = direct.SelectMany(device => device.Functions.SelectMany(function => function.ConnectionPoints.Select(point => point.Id))).ToHashSet();
        var neighbours = project.Connections
            .Where(connection => directPoints.Contains(connection.From) || directPoints.Contains(connection.To))
            .SelectMany(connection => new[] { project.Locate(connection.From)?.Device, project.Locate(connection.To)?.Device })
            .OfType<Device>();

        return [.. direct.Concat(neighbours).Distinct().Where(device => device.Kind != DeviceKind.Junction)];
    }
}
