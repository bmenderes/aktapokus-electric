using Ake.Core.Model;

namespace Ake.Core.Erc;

/// <summary>Aynı tam tag birden çok device ya da kabloda kullanılmış.</summary>
public sealed class DuplicateTagRule : IErcRule
{
    public string Id => "erc.tag_tekrar";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var project = context.Project;
        var tagged = project.Devices
            .Select(device => (Tag: project.DesignationOf(device).ToString(), Id: device.Id.Value))
            .Concat(project.Cables.Select(cable => (Tag: $"-{cable.ProductTag}", Id: cable.Id.Value)));

        foreach (var group in tagged.GroupBy(item => item.Tag, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            yield return new ErcFinding(
                Id,
                Severity.Error,
                $"{group.Key} tag'i {group.Count()} nesnede kullanılmış.",
                [.. group.Select(item => item.Id)]);
        }
    }
}

/// <summary>
/// Aynı tel numarası farklı potansiyellerde kullanılmış. Aynı potansiyeldeki teller aynı numarayı
/// taşıyabilir (potansiyel bazlı numaralandırma).
/// </summary>
public sealed class DuplicateWireNumberRule : IErcRule
{
    public string Id => "erc.tel_no_tekrar";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var numbered = context.Project.Connections
            .Where(connection => !string.IsNullOrEmpty(connection.Wire?.Number))
            .GroupBy(connection => connection.Wire!.Number!, StringComparer.Ordinal);

        foreach (var group in numbered)
        {
            var potentials = group
                .Select(connection => context.Connectivity.PotentialOf(connection.From))
                .Distinct(ReferenceEqualityComparer.Instance)
                .Count();
            if (potentials > 1)
            {
                yield return new ErcFinding(
                    Id,
                    Severity.Error,
                    $"Tel numarası {group.Key}, {potentials} farklı potansiyelde kullanılmış.",
                    [.. group.Select(connection => connection.Id.Value)]);
            }
        }
    }
}

/// <summary>Elektriksel ucu bağlı ama sembolik adı olmayan kanal, ya da tersi.</summary>
public sealed class IoUnassignedRule : IErcRule
{
    public string Id => "erc.io_atanmamis";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var project = context.Project;
        var wiredPoints = project.Connections
            .SelectMany(connection => new[] { connection.From, connection.To })
            .ToHashSet();
        var wiredFunctions = project.Devices
            .SelectMany(device => device.Functions)
            .Where(function => function.ConnectionPoints.Any(point => wiredPoints.Contains(point.Id)))
            .Select(function => function.Id)
            .ToHashSet();

        foreach (var (label, channel) in PlcChannels.WithLabels(project))
        {
            var wired = channel.FunctionId is { } functionId && wiredFunctions.Contains(functionId);
            var named = !string.IsNullOrEmpty(channel.SymbolName);
            if (wired == named)
            {
                continue;
            }

            yield return new ErcFinding(
                Id,
                Severity.Warning,
                wired
                    ? $"{label}: ucu bağlı ama sembolik adı yok."
                    : $"{label}: sembolik adı '{channel.SymbolName}' var ama ucu bağlı değil.",
                [channel.Id.Value]);
        }
    }
}

/// <summary>Aynı function, sembolik ad ya da adres birden çok kanalda.</summary>
public sealed class IoDoubleAssignmentRule : IErcRule
{
    public string Id => "erc.io_cift";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var channels = context.Project.Channels.ToList();

        var byFunction = channels
            .Where(channel => channel.FunctionId is not null)
            .GroupBy(channel => channel.FunctionId!.Value.ToString());
        var bySymbol = channels
            .Where(channel => !string.IsNullOrEmpty(channel.SymbolName))
            .GroupBy(channel => channel.SymbolName!, StringComparer.Ordinal);

        // '*' içeren adres otomatik adres yer tutucusudur (%I*), tekrar sayılmaz.
        var byAddress = channels
            .Where(channel => !string.IsNullOrEmpty(channel.Address) && !channel.Address.Contains('*'))
            .GroupBy(channel => channel.Address!, StringComparer.Ordinal);

        return Duplicates(byFunction, key => $"Aynı elektriksel uç birden çok kanala atanmış ({key}).")
            .Concat(Duplicates(bySymbol, key => $"Sembolik ad '{key}' birden çok kanalda kullanılmış."))
            .Concat(Duplicates(byAddress, key => $"Adres '{key}' birden çok kanalda kullanılmış."));
    }

    private IEnumerable<ErcFinding> Duplicates(
        IEnumerable<IGrouping<string, Channel>> groups, Func<string, string> message) =>
        groups
            .Where(group => group.Count() > 1)
            .Select(group => new ErcFinding(
                Id, Severity.Error, message(group.Key), [.. group.Select(channel => channel.Id.Value)]));
}

/// <summary>
/// İşlevsiz kalmış nesneler: function'ı olmayan device, bağlantı noktası olmayan function, hiçbir
/// bağlantıya atanmamış kablo damarı.
/// </summary>
public sealed class OrphanRule : IErcRule
{
    public string Id => "erc.yetim";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var project = context.Project;

        foreach (var device in project.Devices)
        {
            if (device.Functions.Count == 0)
            {
                yield return new ErcFinding(
                    Id, Severity.Warning, $"{project.DesignationOf(device)} device'ının function'ı yok.", [device.Id.Value]);
            }

            foreach (var function in device.Functions.Where(function => function.ConnectionPoints.Count == 0 && function.Role != TerminalRole.EndCover))
            {
                yield return new ErcFinding(
                    Id,
                    Severity.Warning,
                    $"{project.DesignationOf(device)} içinde bağlantı noktası olmayan function var.",
                    [function.Id.Value]);
            }
        }

        var usedCores = project.Connections.Select(connection => connection.CoreId).OfType<CableCoreId>().ToHashSet();
        foreach (var cable in project.Cables)
        {
            foreach (var core in cable.Cores.Where(core => !usedCores.Contains(core.Id)))
            {
                yield return new ErcFinding(
                    Id,
                    Severity.Warning,
                    $"-{cable.ProductTag} kablosunun {core.Designation} damarı hiçbir bağlantıya atanmamış.",
                    [core.Id.Value]);
            }
        }
    }
}

internal static class PlcChannels
{
    public static IEnumerable<(string Label, Channel Channel)> WithLabels(Project project) =>
        from controller in project.Controllers
        from network in controller.Networks
        from node in network.Nodes
        from module in node.Modules
        from channel in module.Channels
        select ($"{node.Name} modül {module.Position} kanal {channel.Index}", channel);
}
