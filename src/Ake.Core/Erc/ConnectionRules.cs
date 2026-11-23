using Ake.Core.Model;

namespace Ake.Core.Erc;

/// <summary>
/// Yarım bağlı function: bir ucu bağlı, diğeri boş. Hiç bağlanmamış function (yedek kontak,
/// kullanılmayan kanal) bulgu üretmez. Potansiyel işareti taşıyan nokta bağlı sayılır.
/// </summary>
public sealed class OpenEndRule : IErcRule
{
    public string Id => "erc.acik_uc";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var project = context.Project;
        var connected = new HashSet<ConnectionPointId>();
        foreach (var connection in project.Connections)
        {
            connected.Add(connection.From);
            connected.Add(connection.To);
        }

        foreach (var source in project.PotentialSources)
        {
            connected.Add(source.At);
        }

        foreach (var function in project.Devices.SelectMany(device => device.Functions))
        {
            var open = function.ConnectionPoints.Where(point => !connected.Contains(point.Id)).ToList();
            if (open.Count == 0 || open.Count == function.ConnectionPoints.Count)
            {
                continue;
            }

            foreach (var point in open)
            {
                yield return new ErcFinding(
                    Id,
                    Severity.Warning,
                    $"{project.DesignationOf(point.Id)} bağlı değil; aynı function'ın diğer ucu bağlı.",
                    [point.Id.Value]);
            }
        }
    }
}

/// <summary>Farklı adlı potansiyel işaretleri aynı potansiyele düşmüş.</summary>
public sealed class ShortCircuitRule : IErcRule
{
    public string Id => "erc.kisa_devre";

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        foreach (var potential in context.Connectivity.Potentials.Where(potential => potential.HasConflict))
        {
            var points = potential.Points.ToHashSet();
            var sources = context.Project.PotentialSources.Where(source => points.Contains(source.At));

            yield return new ErcFinding(
                Id,
                Severity.Error,
                $"Kısa devre: {string.Join(", ", potential.SourceNames)} potansiyelleri birbirine bağlı.",
                [.. sources.Select(source => source.Id.Value)]);
        }
    }
}

/// <summary>
/// Telin kesiti, potansiyeline dokunan en küçük koruma elemanının anma akımını taşıyamıyor.
/// Anma akımı ya da kesit girilmemişse, ya da kesit tabloda yoksa kural sessiz kalır.
/// </summary>
public sealed class CrossSectionRule : IErcRule
{
    // Bakır, PVC yalıtımlı tel, kanal içinde döşeme için basit taşıma tablosu (EN 60204-1 Tablo 6,
    // B1 yöntemi değerlerine dayanır). Ortam sıcaklığı ve gruplama düzeltmesi uygulanmaz.
    private static readonly (decimal CrossSectionMm2, decimal CapacityA)[] Capacity =
    [
        (0.75m, 8.6m),
        (1.0m, 10.3m),
        (1.5m, 13.5m),
        (2.5m, 18.3m),
        (4m, 24m),
        (6m, 31m),
        (10m, 44m),
        (16m, 59m),
        (25m, 77m),
        (35m, 96m),
    ];

    public string Id => "erc.kesit_koruma";

    /// <summary>Kesitin taşıyabildiği akım; tablodaki en küçük kesitten inceyse <c>null</c>.</summary>
    public static decimal? CapacityOf(decimal crossSectionMm2)
    {
        decimal? capacity = null;
        foreach (var (size, amps) in Capacity)
        {
            if (size <= crossSectionMm2)
            {
                capacity = amps;
            }
        }

        return capacity;
    }

    public IEnumerable<ErcFinding> Check(ErcContext context)
    {
        var project = context.Project;

        var ratedByPotential = new Dictionary<Potential, decimal>(ReferenceEqualityComparer.Instance);
        var protections = project.Devices
            .SelectMany(device => device.Functions)
            .Where(function => function.Kind == FunctionKind.Protection && function.RatedCurrentA is not null);
        foreach (var function in protections)
        {
            foreach (var point in function.ConnectionPoints)
            {
                var potential = context.Connectivity.PotentialOf(point.Id);
                var rated = function.RatedCurrentA!.Value;
                ratedByPotential[potential] = ratedByPotential.TryGetValue(potential, out var current)
                    ? Math.Min(current, rated)
                    : rated;
            }
        }

        var cores = project.Cables.SelectMany(cable => cable.Cores).ToDictionary(core => core.Id);

        foreach (var connection in project.Connections)
        {
            var crossSection = connection.Wire?.CrossSectionMm2
                ?? (connection.CoreId is { } coreId ? cores[coreId].CrossSectionMm2 : null);
            if (crossSection is not { } size
                || !ratedByPotential.TryGetValue(context.Connectivity.PotentialOf(connection.From), out var rated)
                || CapacityOf(size) is not { } capacity
                || capacity >= rated)
            {
                continue;
            }

            yield return new ErcFinding(
                Id,
                Severity.Error,
                FormattableString.Invariant(
                    $"{project.DesignationOf(connection.From)} – {project.DesignationOf(connection.To)}: {size} mm² tel {capacity} A taşır, koruma {rated} A."),
                [connection.Id.Value]);
        }
    }
}
