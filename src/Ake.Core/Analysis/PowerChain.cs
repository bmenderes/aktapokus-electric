using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Analysis;

/// <summary>Güç zincirindeki bir elemanın rolü (ADR-0039).</summary>
public enum PowerElementKind
{
    /// <summary>Besleme: potansiyel işaretinin konduğu klemens ya da kaynak.</summary>
    Source,

    /// <summary>Koruma: sigorta, motor koruma şalteri, devre kesici (<see cref="FunctionKind.Protection"/>).</summary>
    Protection,

    /// <summary>Anahtarlama: şalter, kontaktör gücü (<see cref="FunctionKind.PowerContact"/>).</summary>
    Switch,

    /// <summary>Yük: motor, güç kaynağı gibi zincirin sonu.</summary>
    Load,
}

/// <summary>
/// Güç zincirinin bir düğümü: cihaz, zincirdeki function'ı, rolü, anma akımı ve kendisine gelen bağlantının (telin ya da kablonun) bilgisi.
/// </summary>
/// <param name="Cable">Kendisine gelen bağlantı bir kablo damarıysa kablonun adı ve tipi (örn. "-W1 4G1.5").</param>
/// <param name="CrossSectionMm2">Kendisine gelen telin kesiti; yoksa <c>null</c>.</param>
/// <param name="Potentials">Yalnızca kaynakta: beslemenin potansiyel adları (L1, L2, L3, N, PE).</param>
public sealed record PowerNode(
    Device Device,
    Function Function,
    PowerElementKind Kind,
    decimal? RatedCurrentA,
    string? Cable,
    decimal? CrossSectionMm2,
    IReadOnlyList<string> Potentials,
    IReadOnlyList<PowerNode> Children);

/// <summary>
/// Güç zinciri (ADR-0039): beslemeden başlayıp koruma ve anahtarlama elemanlarından geçerek yüklere inen ağaç. Faz potansiyel işaretinden
/// (yoksa DC artı) başlanır; iki uçlu <c>Protection</c> ve <c>PowerContact</c> function'ları zincir elemanıdır, klemensler şeffaf geçilir
/// (tel kesiti ve kablo kenara yazılır), <c>Motor</c>, <c>PowerSupply</c> ve diğer güç function'ları yüktür. Bobin, yardımcı kontak ve
/// PLC kanalı gibi kumanda elemanları zincire girmez. Model değişmez; her çağrıda yeniden hesaplanır.
/// </summary>
public static class PowerChain
{
    private static readonly FunctionKind[] Loads = [FunctionKind.Motor, FunctionKind.PowerSupply];

    /// <summary>Besleme başına bir kök olmak üzere güç zincirleri; besleme yoksa boş.</summary>
    public static IReadOnlyList<PowerNode> Build(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var neighbours = new Dictionary<ConnectionPointId, List<(Connection Connection, ConnectionPointId Other)>>();
        foreach (var connection in project.Connections)
        {
            Add(neighbours, connection.From, connection, connection.To);
            Add(neighbours, connection.To, connection, connection.From);
        }

        var cableOfCore = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (Core: core.Id, Cable: cable)))
            .ToDictionary(item => item.Core, item => item.Cable);

        // Her besleme cihazı için tek başlangıç: faz işaretlerinden adı en küçük olan; faz yoksa DC artı.
        var sources = ChooseSources(project);
        var roots = new List<PowerNode>();
        foreach (var (source, potentials) in sources)
        {
            if (project.Locate(source.At) is not { } location)
            {
                continue;
            }

            var visited = new HashSet<Device> { location.Device };
            var children = ThroughTerminal(location)
                .SelectMany(start => Walk(project, neighbours, cableOfCore, start, [], visited, cameBy: null))
                .ToList();
            roots.Add(new PowerNode(location.Device, location.Function, PowerElementKind.Source, null, null, null, potentials, children));
        }

        return roots;
    }

    private static List<(PotentialSource Source, IReadOnlyList<string> Potentials)> ChooseSources(Project project)
    {
        var chosen = new List<(PotentialSource, IReadOnlyList<string>)>();
        foreach (var kind in new[] { PotentialKind.Phase, PotentialKind.DcPositive })
        {
            var ofKind = project.PotentialSources.Where(source => source.Kind == kind).ToList();
            foreach (var group in ofKind.GroupBy(source => project.Locate(source.At)?.Device))
            {
                if (group.Key is null)
                {
                    continue;
                }

                var all = project.PotentialSources.Where(source => project.Locate(source.At)?.Device == group.Key).Select(source => source.Name).Order(StringComparer.Ordinal).ToList();
                chosen.Add((group.OrderBy(source => source.Name, StringComparer.Ordinal).First(), all));
            }

            if (chosen.Count > 0)
            {
                break;
            }
        }

        return chosen;
    }

    // Klemens function'ında bir uca varıldığında zincir öbür taraftan sürer (çok bağlantılı klemenste öbür tarafın bütün noktalarından).
    private static IEnumerable<ConnectionPointId> ThroughTerminal(ConnectionPointLocation location)
    {
        var function = location.Function;
        if (function.Kind == FunctionKind.Terminal && function.Ways > 0)
        {
            var side = function.ConnectionPoints.ToList().FindIndex(point => point.Id == location.Point.Id) < function.Ways ? 1 : 0;
            return TerminalNames.OnSide(function, side).Select(point => point.Id);
        }

        return function.ConnectionPoints.Count == 2
            ? [function.ConnectionPoints.First(point => point.Id != location.Point.Id).Id]
            : [location.Point.Id];
    }

    private static List<PowerNode> Walk(
        Project project,
        Dictionary<ConnectionPointId, List<(Connection Connection, ConnectionPointId Other)>> neighbours,
        Dictionary<CableCoreId, Cable> cableOfCore,
        ConnectionPointId from,
        List<Connection> carried,
        HashSet<Device> visited,
        Connection? cameBy)
    {
        var nodes = new List<PowerNode>();
        if (!neighbours.TryGetValue(from, out var next))
        {
            return nodes;
        }

        foreach (var (connection, other) in next)
        {
            if (connection == cameBy || project.Locate(other) is not { } location)
            {
                continue;
            }

            var path = new List<Connection>(carried) { connection };
            var kind = location.Function.Kind;

            if (location.Function.PassThrough && location.Function.Kind == FunctionKind.Terminal)
            {
                // Klemens: şeffaf geçilir.
                foreach (var start in ThroughTerminal(location))
                {
                    nodes.AddRange(Walk(project, neighbours, cableOfCore, start, path, visited, connection));
                }

                continue;
            }

            if (visited.Contains(location.Device))
            {
                continue;
            }

            if (kind is FunctionKind.Protection or FunctionKind.PowerContact && location.Function.ConnectionPoints.Count == 2)
            {
                var children = ThroughTerminal(location)
                    .SelectMany(start => Walk(project, neighbours, cableOfCore, start, [], [.. visited, location.Device], cameBy: null))
                    .ToList();
                nodes.Add(Node(location, kind == FunctionKind.Protection ? PowerElementKind.Protection : PowerElementKind.Switch, path, cableOfCore, children));
            }
            else if (Array.IndexOf(Loads, kind) >= 0)
            {
                nodes.Add(Node(location, PowerElementKind.Load, path, cableOfCore, []));
            }
        }

        // Aynı elemana giden birden çok bağlantı (çok kutuplu cihaz) tek düğümdür.
        return [.. nodes.GroupBy(node => node.Device).Select(group => group.First())];
    }

    private static PowerNode Node(
        ConnectionPointLocation location,
        PowerElementKind kind,
        List<Connection> path,
        Dictionary<CableCoreId, Cable> cableOfCore,
        IReadOnlyList<PowerNode> children)
    {
        var cable = path
            .Select(connection => connection.CoreId is { } core && cableOfCore.TryGetValue(core, out var found) ? found : null)
            .FirstOrDefault(found => found is not null);
        var section = path.Select(connection => connection.Wire?.CrossSectionMm2).FirstOrDefault(value => value is not null);
        return new PowerNode(
            location.Device,
            location.Function,
            kind,
            location.Function.RatedCurrentA,
            cable is null ? null : $"-{cable.ProductTag}{(string.IsNullOrWhiteSpace(cable.Type) ? string.Empty : " " + cable.Type)}",
            section,
            [],
            children);
    }

    private static void Add(
        Dictionary<ConnectionPointId, List<(Connection, ConnectionPointId)>> map, ConnectionPointId point, Connection connection, ConnectionPointId other)
    {
        if (!map.TryGetValue(point, out var list))
        {
            list = [];
            map[point] = list;
        }

        list.Add((connection, other));
    }
}
