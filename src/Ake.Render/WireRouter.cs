namespace Ake.Render;

/// <summary>
/// Telleri sayfa üzerinde dik parçalarla, engellerden kaçarak yürütür (ızgara üzerinde A* araması).
/// </summary>
/// <remarks>
/// Kurallar, çizimin yanlış okunmasını önlemek içindir:
/// - Tel bir sembolün içinden geçmez.
/// - Farklı net'lerin telleri aynı hat üzerinde üst üste binmez; yalnızca dik açıyla kesişebilir.
/// - Aynı net'in telleri ortak hattı paylaşır; ayrıldıkları yer düğüm noktasıdır.
/// - Tel, başka bir net'in köşesinden ya da ucundan geçmez (bağlıymış gibi görünürdü).
/// </remarks>
internal sealed class WireRouter
{
    public const double StubMm = 5;

    private const double StepMm = 2.5;
    private const double MinGapMm = 1.2;
    private const double BendCost = 5;
    private const double CrossCost = 8;
    private const double SharedFactor = 0.4;

    private static readonly (int Dx, int Dy)[] Directions = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    private readonly double[] _xs;
    private readonly double[] _ys;
    private readonly Dictionary<double, int> _xIndex = [];
    private readonly Dictionary<double, int> _yIndex = [];
    private readonly bool[,] _blocked;

    // _horizontal[ix, iy]: (ix, iy) ile (ix + 1, iy) arasındaki kenarın sahibi net; 0 boş demektir.
    private readonly int[,] _horizontal;
    private readonly int[,] _vertical;

    public WireRouter(Box area, IReadOnlyList<Box> obstacles, IReadOnlyList<(RouterPin Pin, int Net)> pins)
    {
        _xs = Lines(area.X, area.Right, pins.SelectMany(p => new[] { p.Pin.X, p.Pin.EndX }));
        _ys = Lines(area.Y, area.Bottom, pins.SelectMany(p => new[] { p.Pin.Y, p.Pin.EndY }));
        for (var i = 0; i < _xs.Length; i++)
        {
            _xIndex[_xs[i]] = i;
        }

        for (var i = 0; i < _ys.Length; i++)
        {
            _yIndex[_ys[i]] = i;
        }

        _blocked = new bool[_xs.Length, _ys.Length];
        _horizontal = new int[_xs.Length, _ys.Length];
        _vertical = new int[_xs.Length, _ys.Length];

        foreach (var box in obstacles)
        {
            for (var ix = 0; ix < _xs.Length; ix++)
            {
                if (_xs[ix] < box.X - 1 || _xs[ix] > box.Right + 1)
                {
                    continue;
                }

                for (var iy = 0; iy < _ys.Length; iy++)
                {
                    if (_ys[iy] >= box.Y - 0.5 && _ys[iy] <= box.Bottom + 0.5)
                    {
                        _blocked[ix, iy] = true;
                    }
                }
            }
        }

        // Her ucun çıkış parçası baştan kendi net'ine ayrılır; başka net onun üzerinden yürüyemez.
        foreach (var (pin, net) in pins)
        {
            Mark(Node(pin.X, pin.Y), Node(pin.EndX, pin.EndY), net);
        }
    }

    /// <summary>
    /// İki uç arasındaki yol: uçtan uca köşe noktaları. Yol bulunamazsa <c>null</c>.
    /// </summary>
    public IReadOnlyList<(double X, double Y)>? Route(RouterPin from, RouterPin to, int net)
    {
        var start = Node(from.EndX, from.EndY);
        var goal = Node(to.EndX, to.EndY);
        var nodes = Search(start, goal, net);
        if (nodes is null)
        {
            return null;
        }

        for (var i = 1; i < nodes.Count; i++)
        {
            Mark(nodes[i - 1], nodes[i], net);
        }

        var points = new List<(double X, double Y)> { (from.X, from.Y) };
        points.AddRange(nodes.Select(node => (_xs[node.Ix], _ys[node.Iy])));
        points.Add((to.X, to.Y));
        return Simplify(points);
    }

    /// <summary>Aynı net'in üç ya da daha çok parçasının buluştuğu noktalar.</summary>
    public IEnumerable<(double X, double Y)> Junctions()
    {
        for (var ix = 0; ix < _xs.Length; ix++)
        {
            for (var iy = 0; iy < _ys.Length; iy++)
            {
                var owners = Enumerable.Range(0, 4).Select(d => Owner(ix, iy, d)).Where(owner => owner != 0).ToList();
                if (owners.Count >= 3 && owners.GroupBy(owner => owner).Any(group => group.Count() >= 3))
                {
                    yield return (_xs[ix], _ys[iy]);
                }
            }
        }
    }

    private List<(int Ix, int Iy)>? Search((int Ix, int Iy) start, (int Ix, int Iy) goal, int net)
    {
        var width = _xs.Length;
        var height = _ys.Length;

        // Durum: düğüm + o düğüme hangi yönden girildiği (köşe maliyeti için). Yön 4 = başlangıç.
        int State(int ix, int iy, int direction) => (((ix * height) + iy) * 5) + direction;

        var cost = new double[width * height * 5];
        Array.Fill(cost, double.PositiveInfinity);
        var previous = new int[cost.Length];
        Array.Fill(previous, -1);

        var queue = new PriorityQueue<int, double>();
        var first = State(start.Ix, start.Iy, 4);
        cost[first] = 0;
        queue.Enqueue(first, 0);

        while (queue.TryDequeue(out var state, out var priority))
        {
            var direction = state % 5;
            var iy = state / 5 % height;
            var ix = state / 5 / height;
            if (priority > cost[state] + Heuristic(ix, iy, goal) + 1e-9)
            {
                continue;
            }

            if ((ix, iy) == goal)
            {
                return Trace(previous, state, height);
            }

            for (var next = 0; next < 4; next++)
            {
                if (direction < 4 && (next ^ 1) == direction)
                {
                    continue;
                }

                var nx = ix + Directions[next].Dx;
                var ny = iy + Directions[next].Dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height || (_blocked[nx, ny] && (nx, ny) != goal))
                {
                    continue;
                }

                var owner = Owner(ix, iy, next);
                if (owner != 0 && owner != net)
                {
                    continue;
                }

                var crossing = Crossing(ix, iy, direction, next, net);
                if (crossing is null)
                {
                    continue;
                }

                var length = Math.Abs(_xs[nx] - _xs[ix]) + Math.Abs(_ys[ny] - _ys[iy]);
                var step = (length * (owner == net ? SharedFactor : 1))
                    + (direction < 4 && direction != next ? BendCost : 0)
                    + (crossing.Value ? CrossCost : 0);

                var target = State(nx, ny, next);
                if (cost[state] + step < cost[target] - 1e-9)
                {
                    cost[target] = cost[state] + step;
                    previous[target] = state;
                    queue.Enqueue(target, cost[target] + Heuristic(nx, ny, goal));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Düğümden çıkış kuralı. Düğümde başka net yoksa <c>false</c>; başka bir tel dik açıyla düz
    /// geçiliyorsa <c>true</c>; geçiş yasaksa (dönüş, ya da öbür telin ucu/köşesi) <c>null</c>.
    /// </summary>
    private bool? Crossing(int ix, int iy, int incoming, int outgoing, int net)
    {
        var foreign = false;
        for (var d = 0; d < 4; d++)
        {
            var owner = Owner(ix, iy, d);
            foreign |= owner != 0 && owner != net;
        }

        if (!foreign)
        {
            return false;
        }

        if (incoming != outgoing)
        {
            return null;
        }

        var a = Owner(ix, iy, outgoing < 2 ? 2 : 0);
        var b = Owner(ix, iy, outgoing < 2 ? 3 : 1);
        return a != 0 && a != net && a == b ? true : null;
    }

    private double Heuristic(int ix, int iy, (int Ix, int Iy) goal) =>
        (Math.Abs(_xs[ix] - _xs[goal.Ix]) + Math.Abs(_ys[iy] - _ys[goal.Iy])) * SharedFactor;

    private static List<(int Ix, int Iy)> Trace(int[] previous, int state, int height)
    {
        var path = new List<(int Ix, int Iy)>();
        for (var current = state; current >= 0; current = previous[current])
        {
            path.Add((current / 5 / height, current / 5 % height));
        }

        path.Reverse();
        return path;
    }

    private int Owner(int ix, int iy, int direction) => direction switch
    {
        0 => ix + 1 < _xs.Length ? _horizontal[ix, iy] : 0,
        1 => ix > 0 ? _horizontal[ix - 1, iy] : 0,
        2 => iy + 1 < _ys.Length ? _vertical[ix, iy] : 0,
        _ => iy > 0 ? _vertical[ix, iy - 1] : 0,
    };

    /// <summary>Aynı hat üzerindeki iki düğüm arasındaki bütün kenarları net'e ayırır.</summary>
    private void Mark((int Ix, int Iy) a, (int Ix, int Iy) b, int net)
    {
        if (a.Iy == b.Iy)
        {
            for (var ix = Math.Min(a.Ix, b.Ix); ix < Math.Max(a.Ix, b.Ix); ix++)
            {
                if (_horizontal[ix, a.Iy] == 0)
                {
                    _horizontal[ix, a.Iy] = net;
                }
            }
        }
        else if (a.Ix == b.Ix)
        {
            for (var iy = Math.Min(a.Iy, b.Iy); iy < Math.Max(a.Iy, b.Iy); iy++)
            {
                if (_vertical[a.Ix, iy] == 0)
                {
                    _vertical[a.Ix, iy] = net;
                }
            }
        }
    }

    private (int Ix, int Iy) Node(double x, double y) => (_xIndex[Key(x)], _yIndex[Key(y)]);

    private static double Key(double value) => Math.Round(value, 3);

    /// <summary>
    /// Izgara çizgileri: düzenli adımlar ve bütün uç koordinatları. Bir uca çok yakın düzenli çizgi
    /// atılır; yoksa iki tel neredeyse üst üste, ama ayrı hatlarda yürürdü.
    /// </summary>
    private static double[] Lines(double from, double to, IEnumerable<double> forced)
    {
        var lines = forced.Select(Key).Distinct().ToList();
        var forcedLines = lines.ToArray();
        for (var value = from; value <= to + 0.001; value += StepMm)
        {
            var line = Key(value);
            if (!forcedLines.Any(f => Math.Abs(f - line) < MinGapMm))
            {
                lines.Add(line);
            }
        }

        lines.Sort();
        return [.. lines];
    }

    private static List<(double X, double Y)> Simplify(List<(double X, double Y)> points)
    {
        var result = new List<(double X, double Y)>();
        foreach (var point in points)
        {
            if (result.Count > 0 && Same(result[^1], point))
            {
                continue;
            }

            if (result.Count >= 2
                && ((Math.Abs(result[^2].X - result[^1].X) < 0.001 && Math.Abs(result[^1].X - point.X) < 0.001)
                    || (Math.Abs(result[^2].Y - result[^1].Y) < 0.001 && Math.Abs(result[^1].Y - point.Y) < 0.001)))
            {
                result[^1] = point;
            }
            else
            {
                result.Add(point);
            }
        }

        return result;

        static bool Same((double X, double Y) a, (double X, double Y) b) =>
            Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001;
    }
}

/// <summary>Tel yolu için bir uç: konumu ve <see cref="WireRouter.StubMm"/> kadar dışarıdaki çıkış noktası.</summary>
internal readonly record struct RouterPin(double X, double Y, double DirectionX, double DirectionY)
{
    public double EndX => X + (DirectionX * WireRouter.StubMm);

    public double EndY => Y + (DirectionY * WireRouter.StubMm);
}
