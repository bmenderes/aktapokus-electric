using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Editing;

/// <summary>
/// Birden çok yerleşim üzerinde düzenleme (ADR-0026). Modeli değiştirmez; tek adımda geri alınan bir
/// işlem üretir.
/// </summary>
public static class Selection
{
    /// <summary>Yerleşimleri aynı mesafe kadar taşır.</summary>
    public static Transaction Move(Project project, IReadOnlyCollection<PlacementId> placements, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(placements);

        var commands = Placed(project, placements)
            .Select(p => (ICommand)new MovePlacement(p.Id, p.X + dx, p.Y + dy, p.RotationDeg))
            .ToList();
        return new Transaction($"{commands.Count} sembol taşındı", commands);
    }

    /// <summary>Her yerleşimi kendi ilk ucu çevresinde saat yönünde 90 derece döndürür.</summary>
    public static Transaction Rotate(Project project, IReadOnlyCollection<PlacementId> placements)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(placements);

        var commands = Placed(project, placements)
            .Select(p => (ICommand)new MovePlacement(p.Id, p.X, p.Y, (p.RotationDeg + 90) % 360))
            .ToList();
        return new Transaction($"{commands.Count} sembol döndürüldü", commands);
    }

    /// <summary>
    /// Yerleşimleri siler. Sayfalarda hiç sembolü kalmayan cihaz bağlantıları ve potansiyel işaretleriyle
    /// birlikte silinir; sembolü kalan cihazın yalnızca seçilen sembolleri kalkar (function yerleşimsiz
    /// kalır, bağlantıları durur). PLC ağacının parçası olan cihaz silinmez, yalnızca sembolleri kalkar.
    /// </summary>
    public static Transaction Delete(Project project, IReadOnlyCollection<PlacementId> placements)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(placements);

        var removed = Placed(project, placements).ToList();
        var removedIds = removed.Select(p => p.Id).ToHashSet();
        var deviceOf = project.Devices
            .SelectMany(d => d.Functions.Select(f => (f.Id, Device: d)))
            .ToDictionary(entry => entry.Id, entry => entry.Device);

        var commands = new List<ICommand>();
        var deletedDevices = new HashSet<DeviceId>();
        foreach (var device in removed.Select(p => deviceOf[p.FunctionId]).Distinct())
        {
            var functions = device.Functions.Select(f => f.Id).ToHashSet();
            var remaining = project.Placements.Any(p => functions.Contains(p.FunctionId) && !removedIds.Contains(p.Id));
            if (!remaining && !IsPlcBound(project, device.Id))
            {
                commands.Add(new RemoveDevice(device.Id));
                deletedDevices.Add(device.Id);
            }
        }

        // Cihazı silinenlerin yerleşimlerini RemoveDevice kaldırır; kalanlar tek tek.
        commands.InsertRange(0, removed
            .Where(p => !deletedDevices.Contains(deviceOf[p.FunctionId].Id))
            .Select(p => (ICommand)new RemovePlacement(p.Id)));
        return new Transaction($"{removed.Count} sembol silindi", commands);
    }

    private static IEnumerable<Placement> Placed(Project project, IReadOnlyCollection<PlacementId> placements)
    {
        var wanted = placements.ToHashSet();
        return project.Placements.Where(p => wanted.Contains(p.Id));
    }

    private static bool IsPlcBound(Project project, DeviceId device)
    {
        var nodes = project.Controllers.SelectMany(c => c.Networks).SelectMany(n => n.Nodes);
        return project.Controllers.Any(c => c.DeviceId == device)
            || nodes.Any(n => n.DeviceId == device || n.Modules.Any(m => m.DeviceId == device));
    }
}
