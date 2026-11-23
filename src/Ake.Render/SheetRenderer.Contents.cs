using System.Globalization;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Render;

/// <summary>İçindekiler sayfaları: projenin sayfa listesi, her satır PDF'te o sayfaya bağlantıdır.</summary>
public static partial class SheetRenderer
{
    private const double ContentsHeadingMm = 5;
    private const double ContentsTextMm = 2.5;
    private const double ContentsRowMm = 6;

    /// <summary>
    /// Projenin sayfalarını listeleyen içindekiler sayfaları. Liste bir sayfaya sığmazsa sürer; sayfalar
    /// "İ1", "İ2" diye numaralanır. Çerçeve ve antet ilk sayfanın şablonundan gelir.
    /// </summary>
    public static Result<IReadOnlyList<Scene>> Contents(Project project, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.Pages.Count == 0)
        {
            return Result.Failure<IReadOnlyList<Scene>>(new Error("render.sayfa_yok", "Projede sayfa yok."));
        }

        var template = project.TemplateFor(project.Pages[0]);
        if (template is null)
        {
            return Result.Failure<IReadOnlyList<Scene>>(new Error(
                "render.sablon_yok",
                "İçindekiler için sayfa şablonu yok. Projeye bir şablon ekleyin (ake template set)."));
        }

        var geometry = SheetGeometry.For(template);
        var area = geometry.DrawingArea;
        var tableTop = area.Y + ContentsHeadingMm + 8;
        var rowsPerPage = Math.Max(1, (int)((area.Bottom - tableTop - ContentsRowMm - 4) / ContentsRowMm));
        var chunks = project.Pages.Chunk(rowsPerPage).ToList();

        var scenes = new List<Scene>();
        for (var index = 0; index < chunks.Count; index++)
        {
            var sheet = new Page(PageId.New(), $"İ{(index + 1).ToString(CultureInfo.InvariantCulture)}", "İçindekiler");
            var items = new List<SceneItem>();
            AddFrameAndGrid(items, template, geometry);

            var left = area.X + 6;
            var width = area.Width - 12;
            var columns = new[] { left + 2, left + 22, left + (width * 0.55), left + (width * 0.78) };
            items.Add(new TextItem(left, area.Y + ContentsHeadingMm + 3, "İçindekiler", ContentsHeadingMm, template.FontFamily));

            var y = tableTop;
            string[] header = ["Sayfa", "Başlık", "Fonksiyon", "Yer"];
            for (var c = 0; c < header.Length; c++)
            {
                items.Add(new TextItem(columns[c], y + ContentsRowMm - 1.8, header[c], ContentsTextMm, template.FontFamily));
            }

            y += ContentsRowMm;
            items.Add(new LineItem(left, y, left + width, y, 0.35));

            foreach (var page in chunks[index])
            {
                string[] cells =
                [
                    page.Number,
                    MultilingualText.Resolve(page.Title, project.Properties.GetValueOrDefault("language")),
                    project.StructurePath(page.FunctionNodeId) is { } function ? $"={function}" : string.Empty,
                    project.StructurePath(page.LocationNodeId) is { } location ? $"+{location}" : string.Empty,
                ];
                for (var c = 0; c < cells.Length; c++)
                {
                    if (cells[c].Length > 0)
                    {
                        items.Add(new TextItem(columns[c], y + ContentsRowMm - 1.8, cells[c], ContentsTextMm, template.FontFamily));
                    }
                }

                items.Add(new LinkItem(left, y, width, ContentsRowMm, AnchorOf(page)));
                y += ContentsRowMm;
                items.Add(new LineItem(left, y, left + width, y, 0.13));
            }

            AddTitleBlock(items, template, geometry, text => Resolve(text, project, sheet, today));
            scenes.Add(new Scene(template.WidthMm, template.HeightMm, items)
            {
                Bookmark = index == 0 ? "İçindekiler" : null,
            });
        }

        return Result.Success<IReadOnlyList<Scene>>(scenes);
    }
}
