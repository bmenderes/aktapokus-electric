using System.Globalization;
using Ake.Core.Model;
using Ake.Library;

namespace Ake.Editor;

/// <summary>
/// Sayfa ağacındaki bir düğüm: yapı grubu (=A1, =A1.M1, +P1) ya da sayfa. <c>PageIndex</c> yalnızca sayfa düğümünde
/// doludur.
/// </summary>
public sealed record PageTreeNode(string Label, int? PageIndex, IReadOnlyList<PageTreeNode> Children);

/// <summary>Sayfa ağacı ile klemens, PLC ve kablo navigator'ları, oturum tarafı (Faz 6.1.5 ve 6.3.13).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// Sayfaların =/+ yapısına göre ağacı: önce fonksiyon yolu (=A1 > =A1.M1), onun altında yer yolu (+P1), en altta sayfalar.
    /// Yapısı olmayan sayfalar kökte durur. <paramref name="filter"/> sayfa numarası ve başlığında ya da grup adında aranır
    /// (Türkçe harflere ve büyük-küçük harfe duyarsız); boşsa hepsi gösterilir. Bir grup adı eşleşirse altındaki her şey kalır.
    /// </summary>
    public IReadOnlyList<PageTreeNode> PageTree(string? filter = null, IReadOnlySet<int>? only = null)
    {
        if (Project is not { } project)
        {
            return [];
        }

        var wanted = string.IsNullOrWhiteSpace(filter) ? null : ComponentLibrary.FoldText(filter.Trim());
        var root = new Group(string.Empty);
        for (var index = 0; index < project.Pages.Count; index++)
        {
            var page = project.Pages[index];
            var group = root;
            foreach (var label in Segments(project.StructurePath(page.FunctionNodeId), '=').Concat(Segments(project.StructurePath(page.LocationNodeId), '+')))
            {
                group = group.Child(label);
            }

            group.Pages.Add((index, $"{page.Number}  {page.Title}"));
        }

        return Build(root, wanted, parentMatches: false, only);
    }

    // "A1.M1" → "=A1", "=A1.M1" (her derinlikte tam yol); yol yoksa boş.
    private static IEnumerable<string> Segments(string? path, char prefix)
    {
        if (path is null)
        {
            yield break;
        }

        var parts = path.Split('.');
        for (var depth = 1; depth <= parts.Length; depth++)
        {
            yield return prefix + string.Join('.', parts.Take(depth));
        }
    }

    private static List<PageTreeNode> Build(Group group, string? wanted, bool parentMatches, IReadOnlySet<int>? only)
    {
        var nodes = new List<PageTreeNode>();
        foreach (var child in group.Children.OrderBy(child => child.Label, StringComparer.Ordinal))
        {
            var matches = parentMatches || (wanted is not null && ComponentLibrary.FoldText(child.Label).Contains(wanted, StringComparison.Ordinal));
            var children = Build(child, wanted, matches, only);
            if ((wanted is null && only is null) || matches || children.Count > 0)
            {
                nodes.Add(new PageTreeNode(child.Label, null, children));
            }
        }

        nodes.AddRange(group.Pages
            .Where(page => only is null || only.Contains(page.Index))
            .Where(page => wanted is null || parentMatches || ComponentLibrary.FoldText(page.Label).Contains(wanted, StringComparison.Ordinal))
            .Select(page => new PageTreeNode(page.Label, page.Index, [])));
        return nodes;
    }

    private sealed class Group(string label)
    {
        public string Label { get; } = label;

        public List<Group> Children { get; } = [];

        public List<(int Index, string Label)> Pages { get; } = [];

        public Group Child(string childLabel)
        {
            var found = Children.FirstOrDefault(child => child.Label == childLabel);
            if (found is null)
            {
                found = new Group(childLabel);
                Children.Add(found);
            }

            return found;
        }
    }

    /// <summary>
    /// Klemens navigator'ı: her klemens şeridi ve klemensleri (kaç bağlantı taşıdığı, hangi sayfada çizildiği). Çift tıklayınca
    /// çizildiği yere gidilir.
    /// </summary>
    public IReadOnlyList<NavigatorRow> TerminalRows()
    {
        if (Project is not { } project)
        {
            return [];
        }

        // Nokta → ona değen bağlantılar; her klemens için bütün bağlantıları taramak yüzlerce sayfada O(n²) olurdu.
        var touching = new Dictionary<ConnectionPointId, List<ConnectionId>>();
        foreach (var connection in project.Connections)
        {
            foreach (var point in new[] { connection.From, connection.To })
            {
                if (!touching.TryGetValue(point, out var list))
                {
                    touching[point] = list = [];
                }

                list.Add(connection.Id);
            }
        }

        var rows = new List<NavigatorRow>();
        foreach (var strip in project.Devices.Where(device => device.Kind == DeviceKind.TerminalStrip).OrderBy(device => project.DesignationOf(device).ToString(), StringComparer.Ordinal))
        {
            rows.Add(Row($"{project.DesignationOf(strip)}  ·  {strip.Functions.Count} klemens", strip.Id.Value, strip.Id));
            foreach (var terminal in strip.Functions)
            {
                var connections = terminal.ConnectionPoints
                    .SelectMany(point => touching.GetValueOrDefault(point.Id) ?? [])
                    .Distinct()
                    .Count();
                var name = string.Join("/", terminal.ConnectionPoints.Select(point => point.Name));
                rows.Add(Row($"    {name}  ·  {connections} bağlantı{Where(terminal.Id.Value)}", terminal.Id.Value, null));
            }
        }

        return rows;
    }

    /// <summary>
    /// PLC navigator'ı: denetleyici > ağ > düğüm > modül > kanal. Kanal satırında sinyal türü, adres, sembol adı ve bağlı
    /// function'ın yeri yazılır.
    /// </summary>
    public IReadOnlyList<NavigatorRow> PlcRows()
    {
        if (Project is not { } project)
        {
            return [];
        }

        var rows = new List<NavigatorRow>();
        foreach (var controller in project.Controllers)
        {
            var device = project.FindDevice(controller.DeviceId);
            rows.Add(Row($"{controller.Name}  ·  {(device is null ? "?" : project.DesignationOf(device).ToString())}", controller.DeviceId.Value, controller.DeviceId));
            foreach (var network in controller.Networks)
            {
                rows.Add(Row($"  {network.Kind} {network.Name}", network.Id.Value, null));
                foreach (var node in network.Nodes.OrderBy(node => node.Position))
                {
                    var nodeDevice = project.FindDevice(node.DeviceId);
                    rows.Add(Row($"    {node.Name}  ·  {(nodeDevice is null ? "?" : project.DesignationOf(nodeDevice).ToString())}", node.DeviceId.Value, node.DeviceId));
                    foreach (var module in node.Modules.OrderBy(module => module.Position))
                    {
                        var moduleDevice = project.FindDevice(module.DeviceId);
                        var assigned = module.Channels.Count(channel => channel.FunctionId is not null);
                        rows.Add(Row(
                            $"      Modül {module.Position}  ·  {(moduleDevice is null ? "?" : project.DesignationOf(moduleDevice).ToString())}  ·  {assigned}/{module.Channels.Count} kanal",
                            module.DeviceId.Value,
                            module.DeviceId));
                        foreach (var channel in module.Channels.OrderBy(channel => channel.Index))
                        {
                            var text = $"        {channel.Index.ToString(CultureInfo.InvariantCulture)}  {channel.SignalType}"
                                + $"  {channel.Address ?? "—"}  {channel.SymbolName ?? string.Empty}".TrimEnd()
                                + (channel.FunctionId is { } function ? Where(function.Value) : "  ·  atanmamış");
                            rows.Add(Row(text, channel.Id.Value, null));
                        }
                    }
                }
            }
        }

        return rows;
    }

    /// <summary>
    /// Kablo navigator'ı: her kablo ve damarları (kesit, renk, bağlandığı uçlar). Çift tıklayınca damarın çizildiği sayfaya
    /// gidilir.
    /// </summary>
    public IReadOnlyList<NavigatorRow> CableRows()
    {
        if (Project is not { } project)
        {
            return [];
        }

        string End(ConnectionPointId id) => project.Locate(id) is { } location
            ? $"{project.DesignationOf(location.Device)}:{location.Point.Name}"
            : "?";

        var rows = new List<NavigatorRow>();
        foreach (var cable in project.Cables.OrderBy(cable => cable.ProductTag, StringComparer.Ordinal))
        {
            rows.Add(Row($"-{cable.ProductTag}  ·  {cable.Type ?? "tip yok"}  ·  {cable.Cores.Count} damar", cable.Id.Value, null));
            foreach (var core in cable.Cores)
            {
                var wire = project.Connections.FirstOrDefault(connection => connection.CoreId == core.Id);
                var details = string.Join(
                    "  ",
                    new[]
                    {
                        core.CrossSectionMm2 is { } section ? $"{section.ToString(CultureInfo.InvariantCulture)} mm²" : null,
                        core.Color,
                    }.Where(part => part is not null));
                var ends = wire is null ? "  ·  bağlı değil" : $"  ·  {End(wire.From)} → {End(wire.To)}";
                rows.Add(Row($"    {core.Designation}  {details}{ends}".TrimEnd(), wire?.Id.Value ?? core.Id.Value, null));
            }
        }

        return rows;
    }

    // Satırın gidileceği yer Locate ile bulunur; bulunamazsa satır yine listelenir (gidilecek yeri yoktur).
    private NavigatorRow Row(string label, Guid target, DeviceId? device)
    {
        var found = Locate(target);
        return new NavigatorRow(label, found?.PageIndex, found?.Placement, device);
    }

    private string Where(Guid target) =>
        Locate(target) is { } found ? $"  ·  sayfa {Project!.Pages[found.PageIndex].Number}" : "  ·  yerleştirilmemiş";
}
