using Ake.Render;

namespace Ake.Editor;

/// <summary>Tel seçimi, oturum tarafı: sayfada çizilen tellerin yolları ve tıkla seçme.</summary>
public sealed partial class EditorSession
{
    /// <summary>Tele bu kadar yakın bir tık teli seçer (mm).</summary>
    private const double WireHitMm = 1.2;

    private readonly Dictionary<int, (int Version, IReadOnlyList<WirePath> Paths)> _wireCache = [];

    /// <summary>
    /// Sayfada çizilen tellerin yolları. Hesap sayfanın tamamını yönlendirir; sonuç, model değişene kadar saklanır.
    /// </summary>
    public IReadOnlyList<WirePath> WiresFor(int pageIndex)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return [];
        }

        if (_wireCache.TryGetValue(pageIndex, out var cached) && cached.Version == _version)
        {
            return cached.Paths;
        }

        var paths = SheetRenderer.WirePaths(Project!, page.Value);
        _wireCache[pageIndex] = (_version, paths);
        return paths;
    }

    /// <summary>Verilen sayfa noktasına en yakın tel (<see cref="WireHitMm"/> içinde); yoksa <c>null</c>.</summary>
    public WirePath? WireAt(int pageIndex, double sheetX, double sheetY)
    {
        WirePath? best = null;
        var bestDistance = WireHitMm;
        foreach (var wire in WiresFor(pageIndex))
        {
            for (var i = 1; i < wire.Path.Count; i++)
            {
                var distance = DistanceToSegment(
                    sheetX, sheetY, wire.Path[i - 1].X, wire.Path[i - 1].Y, wire.Path[i].X, wire.Path[i].Y);
                if (distance <= bestDistance)
                {
                    (best, bestDistance) = (wire, distance);
                }
            }
        }

        return best;
    }
}
