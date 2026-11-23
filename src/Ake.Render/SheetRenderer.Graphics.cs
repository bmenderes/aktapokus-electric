using Ake.Core.Model;

namespace Ake.Render;

/// <summary>Sayfadaki bir serbest grafiğin kutusu, sayfa koordinatlarında (seçim ve vurgu için).</summary>
public sealed record GraphicBox(GraphicId Graphic, Box Bounds);

public static partial class SheetRenderer
{
    /// <summary>
    /// Sayfanın serbest grafiklerini sahne öğelerine çevirir: çoklu çizgi çizgi parçalarına, daire ve yazı
    /// kendi öğelerine (ADR-0028). Dolgu şimdilik yalnızca dairede çizilir.
    /// </summary>
    internal static void AddGraphics(List<SceneItem> items, Project project, Page page, SheetTemplate template)
    {
        foreach (var graphic in project.Graphics)
        {
            if (graphic.PageId != page.Id)
            {
                continue;
            }

            switch (graphic.Shape)
            {
                case GraphicPolyline polyline:
                    var points = polyline.Points;
                    for (var i = 2; i + 1 < points.Count; i += 2)
                    {
                        items.Add(new LineItem(points[i - 2], points[i - 1], points[i], points[i + 1], polyline.StrokeMm, polyline.Color));
                    }

                    if (polyline.Closed && points.Count >= 6)
                    {
                        items.Add(new LineItem(points[^2], points[^1], points[0], points[1], polyline.StrokeMm, polyline.Color));
                    }

                    break;

                case GraphicCircle circle:
                    items.Add(new CircleItem(circle.X, circle.Y, circle.Radius, circle.StrokeMm, circle.Filled));
                    break;

                case GraphicText text:
                    var resolved = MultilingualText.Resolve(text.Text, project.Properties.GetValueOrDefault("language"));
                    items.Add(new TextItem(text.X, text.Y, resolved, text.SizeMm, template.FontFamily, text.Align, text.RotationDeg));
                    break;
            }
        }
    }

    /// <summary>Sayfadaki serbest grafiklerin kutuları; çizim sırasıyla. Şablonu olmayan sayfada da çalışır.</summary>
    public static IReadOnlyList<GraphicBox> GraphicBoxes(Project project, Page page)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        return
        [
            .. project.Graphics
                .Where(graphic => graphic.PageId == page.Id)
                .Select(graphic =>
                {
                    var (x, y, width, height) = graphic.Shape.Bounds;
                    return new GraphicBox(graphic.Id, new Box(x, y, width, height));
                }),
        ];
    }
}
