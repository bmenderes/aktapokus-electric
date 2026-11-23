namespace Ake.Core.Model;

/// <summary>Tellerle doğrudan birbirine bağlı bağlantı noktaları kümesi.</summary>
public sealed record Net(IReadOnlyList<ConnectionPointId> Points);

/// <summary>
/// Geçirgen function'lar (klemens, bara) üzerinden birleşen net'ler kümesi. Adını içindeki
/// <see cref="PotentialSource"/> işaretlerinden alır.
/// </summary>
public sealed record Potential(IReadOnlyList<string> SourceNames, IReadOnlyList<Net> Nets)
{
    /// <summary>Tek işaret adı varsa o ad; işaret yoksa ya da adlar çelişiyorsa <c>null</c>.</summary>
    public string? Name => SourceNames.Count == 1 ? SourceNames[0] : null;

    /// <summary>Farklı adlı işaretler aynı potansiyele düşmüş (ERC'de kısa devre).</summary>
    public bool HasConflict => SourceNames.Count > 1;

    public IEnumerable<ConnectionPointId> Points => Nets.SelectMany(net => net.Points);
}

/// <summary>
/// Net ve potansiyeller saklanmaz; bağlantılardan hesaplanır. Hesap modeli değiştirmez.
/// </summary>
public sealed class Connectivity
{
    private readonly Dictionary<ConnectionPointId, Net> _netByPoint = [];
    private readonly Dictionary<ConnectionPointId, Potential> _potentialByPoint = [];

    private Connectivity(IReadOnlyList<Net> nets, IReadOnlyList<Potential> potentials)
    {
        Nets = nets;
        Potentials = potentials;

        foreach (var net in nets)
        {
            foreach (var point in net.Points)
            {
                _netByPoint[point] = net;
            }
        }

        foreach (var potential in potentials)
        {
            foreach (var point in potential.Points)
            {
                _potentialByPoint[point] = potential;
            }
        }
    }

    public IReadOnlyList<Net> Nets { get; }

    public IReadOnlyList<Potential> Potentials { get; }

    public Net NetOf(ConnectionPointId point) => _netByPoint[point];

    public Potential PotentialOf(ConnectionPointId point) => _potentialByPoint[point];

    public static Connectivity Compute(Project project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var functions = project.Devices.SelectMany(device => device.Functions).ToList();
        var points = functions.SelectMany(function => function.ConnectionPoints).Select(point => point.Id).ToList();

        var indexOf = new Dictionary<ConnectionPointId, int>(points.Count);
        for (var i = 0; i < points.Count; i++)
        {
            indexOf[points[i]] = i;
        }

        var netSets = new DisjointSets(points.Count);
        foreach (var connection in project.Connections)
        {
            netSets.Union(indexOf[connection.From], indexOf[connection.To]);
        }

        var potentialSets = netSets.Clone();
        foreach (var function in functions.Where(function => function.PassThrough))
        {
            for (var i = 1; i < function.ConnectionPoints.Count; i++)
            {
                potentialSets.Union(
                    indexOf[function.ConnectionPoints[0].Id],
                    indexOf[function.ConnectionPoints[i].Id]);
            }
        }

        var nets = points
            .GroupBy(point => netSets.Find(indexOf[point]))
            .Select(group => new Net([.. group]))
            .ToList();

        var namesByRoot = project.PotentialSources
            .GroupBy(source => potentialSets.Find(indexOf[source.At]))
            .ToDictionary(
                group => group.Key,
                group => group.Select(source => source.Name).Distinct().Order(StringComparer.Ordinal).ToList());

        var potentials = nets
            .GroupBy(net => potentialSets.Find(indexOf[net.Points[0]]))
            .Select(group => new Potential(namesByRoot.GetValueOrDefault(group.Key) ?? [], [.. group]))
            .ToList();

        return new Connectivity(nets, potentials);
    }

    private sealed class DisjointSets
    {
        private readonly int[] _parent;

        public DisjointSets(int count)
        {
            _parent = new int[count];
            for (var i = 0; i < count; i++)
            {
                _parent[i] = i;
            }
        }

        private DisjointSets(int[] parent) => _parent = parent;

        public DisjointSets Clone() => new((int[])_parent.Clone());

        public int Find(int item)
        {
            while (_parent[item] != item)
            {
                _parent[item] = _parent[_parent[item]];
                item = _parent[item];
            }

            return item;
        }

        public void Union(int a, int b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB)
            {
                _parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
            }
        }
    }
}
