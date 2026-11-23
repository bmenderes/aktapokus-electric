using Ake.Core;
using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>Makro oluşturma ve yerleştirme, oturum tarafı (ADR-0025).</summary>
public sealed partial class EditorSession
{
    /// <summary>Sayfanın tamamından makro; adı verilmezse sayfa başlığı.</summary>
    public Result<Macro> MacroFromPage(int pageIndex, string? name = null)
    {
        var page = PageAt(pageIndex);
        return page.IsFailure
            ? Result.Failure<Macro>(page.Error!)
            : MacroService.Capture(Project!, page.Value.Id, string.IsNullOrWhiteSpace(name) ? page.Value.Title : name);
    }

    /// <summary>
    /// Seçili sembolün cihazından makro: cihazın o sayfadaki ana görünümü alınır; adı cihazın tag'i.
    /// </summary>
    public Result<Macro> MacroFromSelection(PlacementId placementId)
    {
        var found = Find(placementId);
        if (found.IsFailure)
        {
            return Result.Failure<Macro>(found.Error!);
        }

        var placement = found.Value;
        var device = Project!.Devices.First(d => d.Functions.Any(f => f.Id == placement.FunctionId));
        var functions = device.Functions.Select(f => f.Id).ToHashSet();
        var points = Project.Placements
            .Where(p => p.PageId == placement.PageId && p.Role == PlacementRole.Main && functions.Contains(p.FunctionId))
            .ToList();
        var left = points.Min(p => p.X);
        var top = points.Min(p => p.Y);
        var area = (left, top, points.Max(p => p.X) - left, points.Max(p => p.Y) - top);
        return MacroService.Capture(Project, placement.PageId, $"-{device.ProductTag}", area);
    }

    /// <summary>
    /// Makroyu sayfaya yerleştirir; konum verilmezse sayfadaki sembollerin altına. Tek adımda geri alınır.
    /// İsteğe bağlı olarak seçilen değer kümesini (<paramref name="valueSetName"/>) uygular.
    /// </summary>
    public Result InsertMacro(Macro macro, int pageIndex, double? x = null, double? y = null, string? valueSetName = null)
    {
        ArgumentNullException.ThrowIfNull(macro);

        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure(page.Error!);
        }

        var spot = MacroService.DefaultSpot(Project!, page.Value.Id);
        var (sx, sy) = Snap(x ?? spot.X, y ?? spot.Y);
        var insert = MacroService.Insert(Project!, macro, page.Value.Id, sx, sy, valueSetName: valueSetName);
        return insert.IsFailure ? Result.Failure(insert.Error!) : Execute(insert.Value);
    }
}
