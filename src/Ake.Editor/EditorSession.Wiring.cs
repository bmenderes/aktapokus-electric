using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Tel çekme, oturum tarafı (ADR-0026): uca yakalama ve iki ucu bağlama.</summary>
public sealed partial class EditorSession
{
    /// <summary>Uca yakalama mesafesi (mm).</summary>
    public const double PinToleranceMm = 2.5;

    private readonly Dictionary<int, (int Version, IReadOnlyList<PlacedPinInfo> Pins)> _pinCache = [];

    /// <summary>
    /// Sayfadaki bağlantı noktaları. Tel çekerken imleç her kıpırdadığında sorulur; sayfa bir kez yerleştirilir ve sonuç model
    /// değişene kadar saklanır.
    /// </summary>
    public IReadOnlyList<PlacedPinInfo> PinsFor(int pageIndex)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return [];
        }

        if (_pinCache.TryGetValue(pageIndex, out var cached) && cached.Version == _version)
        {
            return cached.Pins;
        }

        var pins = SheetRenderer.PinsOf(Project!, page.Value);
        _pinCache[pageIndex] = (_version, pins);
        return pins;
    }

    /// <summary>Noktaya en yakın uç; yakalama mesafesinde uç yoksa <c>null</c>.</summary>
    public PlacedPinInfo? PinAt(int pageIndex, double x, double y, double toleranceMm = PinToleranceMm) =>
        PinsFor(pageIndex)
            .Select(pin => (Pin: pin, Distance: Math.Sqrt(((pin.X - x) * (pin.X - x)) + ((pin.Y - y) * (pin.Y - y)))))
            .Where(entry => entry.Distance <= Math.Max(toleranceMm, PinToleranceMm))
            .OrderBy(entry => entry.Distance)
            .Select(entry => entry.Pin)
            .FirstOrDefault();

    /// <summary>
    /// İki ucu yeni bir telle bağlar (numarası boş; Tel numaraları komutu verir). Geçersiz bağlantıyı
    /// (aynı uç, zaten bağlı) model reddeder ve hiçbir şey değişmez.
    /// </summary>
    public Result ConnectPins(ConnectionPointId from, ConnectionPointId to, IReadOnlyList<WirePoint>? route = null) =>
        Project is null
            ? NoProject()
            : Execute(new Connect(new Connection(ConnectionId.New(), from, to, new Wire(WireId.New()), null, route)));

    /// <summary>
    /// Telin köşe noktalarını değiştirir (ADR-0029); boş liste otomatik yönlendirmeye döndürür. Köşe noktaları yapışma
    /// adımına oturtulur. Tek adımda geri alınır.
    /// </summary>
    public Result SetWireRoute(ConnectionId connection, IReadOnlyList<WirePoint> route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (Project is null)
        {
            return NoProject();
        }

        var snapped = route.Select(point => { var (x, y) = Snap(point.X, point.Y); return new WirePoint(x, y); }).ToList();
        return Project.FindConnection(connection) is { } current && current.Route.SequenceEqual(snapped)
            ? Result.Success()
            : Execute(new SetWireRoute(connection, snapped));
    }

    /// <summary>Telin köşe noktaları var mı (otomatik yönlendirilmiyor mu).</summary>
    public bool HasRoute(ConnectionId connection) => Project?.FindConnection(connection)?.Route.Count > 0;

    /// <summary>Bağlantının iki ucunun sayfadaki yeri; ikisi de bu sayfada değilse <c>null</c>.</summary>
    public (PlacedPinInfo From, PlacedPinInfo To)? EndsOf(int pageIndex, ConnectionId connection)
    {
        if (Project?.FindConnection(connection) is not { } wire)
        {
            return null;
        }

        var pins = PinsFor(pageIndex);
        var from = pins.FirstOrDefault(pin => pin.Point == wire.From);
        var to = pins.FirstOrDefault(pin => pin.Point == wire.To);
        return from is null || to is null ? null : (from, to);
    }

    /// <summary>
    /// Telin tıklanan yere en yakın noktasına kavşak koyar ve teli orada ikiye böler (ADR-0032). Kavşak, telin üzerinde kalır:
    /// yatay parçada yalnızca x, dikey parçada yalnızca y ızgaraya yakalanır. Dönen uç, kavşağın bağlantı noktasıdır; ona
    /// üçüncü tel bağlanabilir.
    /// </summary>
    public Result<PlacedPinInfo> SplitWire(int pageIndex, WirePath wire, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(wire);

        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<PlacedPinInfo>(page.Error!);
        }

        // Yerleşim koordinatı çizim alanına göredir; tel yolu sayfa koordinatındadır.
        var (jx, jy) = NearestOnPath(wire.Path, x, y);
        var origin = DrawingAreaFor(pageIndex) ?? new Box(0, 0, 0, 0);
        var split = Junctions.Split(Project!, wire.Connection, page.Value.Id, jx - origin.X, jy - origin.Y);
        if (split.IsFailure)
        {
            return Result.Failure<PlacedPinInfo>(split.Error!);
        }

        var executed = Execute(split.Value.Command);
        if (executed.IsFailure)
        {
            return Result.Failure<PlacedPinInfo>(executed.Error!);
        }

        return PinsFor(pageIndex).FirstOrDefault(pin => pin.Point == split.Value.Point) is { } found
            ? Result.Success(found)
            : Result.Failure<PlacedPinInfo>(new Error("kavsak.uc_yok", "Kavşağın ucu sayfada bulunamadı."));
    }

    // Yola en yakın nokta; yatay/dikey parçada parçanın yönündeki koordinat ızgaraya yakalanır ve parça içinde tutulur.
    private static (double X, double Y) NearestOnPath(IReadOnlyList<(double X, double Y)> path, double x, double y)
    {
        var best = path[0];
        var bestDistance = double.MaxValue;
        for (var i = 1; i < path.Count; i++)
        {
            var (a, b) = (path[i - 1], path[i]);
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var length2 = (dx * dx) + (dy * dy);
            var t = length2 < 1e-9 ? 0 : Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / length2, 0, 1);
            var point = (X: a.X + (t * dx), Y: a.Y + (t * dy));
            if (Math.Abs(dy) < 1e-6 && Math.Abs(dx) > 1e-6)
            {
                point.X = Math.Clamp(Snap(point.X, point.Y).X, Math.Min(a.X, b.X), Math.Max(a.X, b.X));
            }
            else if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) > 1e-6)
            {
                point.Y = Math.Clamp(Snap(point.X, point.Y).Y, Math.Min(a.Y, b.Y), Math.Max(a.Y, b.Y));
            }

            var distance = ((point.X - x) * (point.X - x)) + ((point.Y - y) * (point.Y - y));
            if (distance < bestDistance)
            {
                (best, bestDistance) = (point, distance);
            }
        }

        return best;
    }
}
