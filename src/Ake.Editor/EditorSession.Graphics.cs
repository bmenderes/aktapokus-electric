using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Editing;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Serbest grafiğin seçilmesi, taşınması, silinmesi ve yazısının düzenlenmesi, oturum tarafı (ADR-0028).</summary>
public sealed partial class EditorSession
{
    /// <summary>Çizgiye bu kadar yakın bir tık grafiği seçer (mm).</summary>
    private const double GraphicHitMm = 1;

    /// <summary>Sayfadaki serbest grafiklerin kutuları.</summary>
    public IReadOnlyList<GraphicBox> GraphicBoxesFor(int pageIndex)
    {
        var page = PageAt(pageIndex);
        return page.IsFailure ? [] : SheetRenderer.GraphicBoxes(Project!, page.Value);
    }

    /// <summary>
    /// Verilen sayfa noktasındaki grafik: çizgiye ya da daire çizgisine <see cref="GraphicHitMm"/> içinde ya da yazının
    /// kutusunda. Üst üste gelenlerde en küçük kutu, eşitse en üstte çizilen kazanır. Seçilecek grafik yoksa <c>null</c>.
    /// </summary>
    public GraphicBox? GraphicAt(int pageIndex, double sheetX, double sheetY)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return null;
        }

        GraphicBox? best = null;
        var bestArea = double.MaxValue;
        foreach (var graphic in Project!.Graphics.Where(graphic => graphic.PageId == page.Value.Id))
        {
            if (!Hits(graphic.Shape, sheetX, sheetY))
            {
                continue;
            }

            var (x, y, width, height) = graphic.Shape.Bounds;
            var area = width * height;
            if (area <= bestArea)
            {
                (best, bestArea) = (new GraphicBox(graphic.Id, new Box(x, y, width, height)), area);
            }
        }

        return best;
    }

    /// <summary>Kutusu tamamen alanın içinde kalan grafikler (pencere seçimi).</summary>
    public IReadOnlyList<GraphicBox> GraphicsIn(int pageIndex, Box area) =>
        [.. GraphicBoxesFor(pageIndex).Where(box =>
            box.Bounds.X >= area.X && box.Bounds.Right <= area.Right
            && box.Bounds.Y >= area.Y && box.Bounds.Bottom <= area.Bottom)];

    /// <summary>
    /// Seçili sembolleri ve grafikleri yapışma adımına yuvarlanmış mesafe kadar birlikte taşır; tek adım.
    /// </summary>
    public Result MoveSelected(
        IReadOnlyCollection<PlacementId> placements, IReadOnlyCollection<GraphicId> graphics, double deltaX, double deltaY)
    {
        if (Project is null)
        {
            return NoProject();
        }

        var (dx, dy) = Snap(deltaX, deltaY);
        if ((placements.Count == 0 && graphics.Count == 0) || (dx == 0 && dy == 0))
        {
            return Result.Success();
        }

        var commands = new List<ICommand>(Selection.Move(Project, placements, dx, dy).Commands);
        if (graphics.Count > 0)
        {
            var wanted = graphics.ToHashSet();
            commands.Add(new ReplaceGraphicShapes(Project.Graphics
                .Where(graphic => wanted.Contains(graphic.Id))
                .ToDictionary(graphic => graphic.Id, graphic => graphic.Shape.Translate(dx, dy))));
        }

        return ExecuteIfAny(new Transaction("Seçim taşındı", commands));
    }

    /// <summary>
    /// Seçili sembolleri ve grafikleri siler; sembolü kalmayan cihaz bağlantılarıyla birlikte silinir
    /// (<see cref="Selection.Delete"/>). Tek adımda geri alınır.
    /// </summary>
    public Result DeleteSelected(
        IReadOnlyCollection<PlacementId> placements,
        IReadOnlyCollection<GraphicId> graphics,
        IReadOnlyCollection<ConnectionId>? wires = null)
    {
        if (Project is null)
        {
            return NoProject();
        }

        // Teller önce kalkar: cihaz silinirse kalan bağlantıları onunla gider.
        var commands = (wires ?? []).Select(id => (ICommand)new Disconnect(id)).ToList();
        if (placements.Count > 0)
        {
            commands.AddRange(Selection.Delete(Project, placements).Commands);
        }

        if (graphics.Count > 0)
        {
            commands.Add(new RemoveGraphics(graphics));
        }

        return ExecuteIfAny(new Transaction("Seçim silindi", commands));
    }

    /// <summary>Grafik bir yazıysa metni; değilse <c>null</c>.</summary>
    public string? TextOf(GraphicId id) =>
        Project?.Graphics.FirstOrDefault(graphic => graphic.Id == id)?.Shape is GraphicText text ? text.Text : null;

    /// <summary>Sayfaya verilen noktada serbest metin ekler (sağ tık kolaylığı, ADR-0054).</summary>
    public Result<GraphicId> AddFreeText(int pageIndex, double sheetX, double sheetY, string text, double sizeMm = 2.5)
    {
        ArgumentNullException.ThrowIfNull(text);
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<GraphicId>(page.Error!);
        }

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return Result.Failure<GraphicId>(new Error("grafik.gecersiz", "Yazı boş olamaz."));
        }

        var (sx, sy) = Snap(sheetX, sheetY);
        var graphic = new PageGraphic(GraphicId.New(), page.Value.Id, new GraphicText(sx, sy, trimmed, sizeMm));
        var added = Execute(new AddGraphics([graphic]));
        return added.IsFailure ? Result.Failure<GraphicId>(added.Error!) : Result.Success(graphic.Id);
    }

    /// <summary>Yazı grafiğinin metnini değiştirir. Boş ya da çok satırlı metin reddedilir.</summary>
    public Result SetGraphicText(GraphicId id, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (Project is null)
        {
            return NoProject();
        }

        if (Project.Graphics.FirstOrDefault(graphic => graphic.Id == id)?.Shape is not GraphicText current)
        {
            return Result.Failure(new Error("grafik.yazi_degil", "Seçili grafik bir yazı değil."));
        }

        var trimmed = text.Trim();
        return trimmed == current.Text
            ? Result.Success()
            : Execute(new ReplaceGraphicShapes(new Dictionary<GraphicId, GraphicShape> { [id] = current with { Text = trimmed } }));
    }

    private static bool Hits(GraphicShape shape, double x, double y)
    {
        switch (shape)
        {
            case GraphicPolyline polyline:
                var points = polyline.Points;
                for (var i = 2; i + 1 < points.Count; i += 2)
                {
                    if (DistanceToSegment(x, y, points[i - 2], points[i - 1], points[i], points[i + 1]) <= GraphicHitMm)
                    {
                        return true;
                    }
                }

                return polyline.Closed && points.Count >= 6
                    && DistanceToSegment(x, y, points[^2], points[^1], points[0], points[1]) <= GraphicHitMm;

            case GraphicCircle circle:
                var distance = Math.Sqrt(Math.Pow(x - circle.X, 2) + Math.Pow(y - circle.Y, 2));
                return circle.Filled ? distance <= circle.Radius + GraphicHitMm : Math.Abs(distance - circle.Radius) <= GraphicHitMm;

            default:
                var (bx, by, width, height) = shape.Bounds;
                return x >= bx - GraphicHitMm && x <= bx + width + GraphicHitMm
                    && y >= by - GraphicHitMm && y <= by + height + GraphicHitMm;
        }
    }

    private static double DistanceToSegment(double px, double py, double x1, double y1, double x2, double y2)
    {
        var (dx, dy) = (x2 - x1, y2 - y1);
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared == 0 ? 0 : Math.Clamp((((px - x1) * dx) + ((py - y1) * dy)) / lengthSquared, 0, 1);
        return Math.Sqrt(Math.Pow(px - (x1 + (t * dx)), 2) + Math.Pow(py - (y1 + (t * dy)), 2));
    }
}
