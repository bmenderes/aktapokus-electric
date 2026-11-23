using System.Globalization;
using Ake.Core;
using Ake.Core.Model;
using Ake.Reports;

namespace Ake.Render;

/// <summary>
/// Baskıya hazır kablo ve tel etiketi PDF çıktısı (Faz 6c-3, ADR-0056):
/// <see cref="ReportCatalog.Labels"/> verisini kesim kılavuz çizgili A4 etiket tabakalarına yerleştirir.
/// </summary>
public static class LabelPdfExporter
{
    private const double PageWidthMm = 210;
    private const double PageHeightMm = 297;
    private const double MarginMm = 10;
    private const double HeaderMm = 12;
    private const double GapMm = 3;
    private const string FontFamily = "Arial";

    /// <summary>Projeden baskıya hazır A4 etiket sayfalarını (sahnelerini) üretir.</summary>
    public static IReadOnlyList<Scene> BuildScenes(Project project, int columns = 3, int rowsPerPage = 10)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rowsPerPage, 1);

        var table = ReportCatalog.Labels(project);
        if (table.Rows.Count == 0)
        {
            return
            [
                new Scene(
                    PageWidthMm,
                    PageHeightMm,
                    [
                        new TextItem(MarginMm, MarginMm + 6, $"{project.Name} — Kablo ve Tel Etiketleri", 4, FontFamily),
                        new RectItem(MarginMm, MarginMm + HeaderMm, PageWidthMm - (2 * MarginMm), 16, 0.3),
                        new TextItem(MarginMm + 4, MarginMm + HeaderMm + 10, "Basılacak numaralı tel veya kablo damarı etiketi yok.", 3, FontFamily),
                    ])
                {
                    Bookmark = "Kablo ve Tel Etiketleri",
                },
            ];
        }

        var perPage = columns * rowsPerPage;
        var pageCount = (int)Math.Ceiling(table.Rows.Count / (double)perPage);
        var usableW = PageWidthMm - (2 * MarginMm);
        var usableH = PageHeightMm - (2 * MarginMm) - HeaderMm;
        var cellW = (usableW - ((columns - 1) * GapMm)) / columns;
        var cellH = (usableH - ((rowsPerPage - 1) * GapMm)) / rowsPerPage;

        var scenes = new List<Scene>(pageCount);
        for (var p = 0; p < pageCount; p++)
        {
            var items = new List<SceneItem>
            {
                new TextItem(MarginMm, MarginMm + 6, $"{project.Name} — Kablo ve Tel Etiketleri (Sayfa {p + 1}/{pageCount})", 3.6, FontFamily),
                new LineItem(MarginMm, MarginMm + 9, PageWidthMm - MarginMm, MarginMm + 9, 0.3, "#90897A"),
            };

            var slice = table.Rows.Skip(p * perPage).Take(perPage).ToList();
            for (var i = 0; i < slice.Count; i++)
            {
                var row = slice[i];
                var col = i % columns;
                var r = i / columns;
                var x = MarginMm + (col * (cellW + GapMm));
                var y = MarginMm + HeaderMm + (r * (cellH + GapMm));

                // Kesim kılavuz çizgisi (kesikli)
                items.Add(new LineItem(x, y, x + cellW, y, 0.2, "#9A9385", LineStyle.Dashed));
                items.Add(new LineItem(x + cellW, y, x + cellW, y + cellH, 0.2, "#9A9385", LineStyle.Dashed));
                items.Add(new LineItem(x + cellW, y + cellH, x, y + cellH, 0.2, "#9A9385", LineStyle.Dashed));
                items.Add(new LineItem(x, y + cellH, x, y, 0.2, "#9A9385", LineStyle.Dashed));

                // Etiket başlık ayırıcı çizgisi
                var barH = Math.Min(7.5, cellH * 0.34);
                items.Add(new LineItem(x + 1.5, y + barH, x + cellW - 1.5, y + barH, 0.35, "#D49B1A"));

                var label = Text(row[0]);
                var end = Text(row[1]);
                var here = Shorten(Text(row[2]), 34);
                var there = Shorten(Text(row[3]), 34);
                var color = Text(row[4]);
                var section = row[5] is null ? string.Empty : $"{Convert.ToString(row[5], CultureInfo.InvariantCulture)} mm²";
                var spec = string.Join(" · ", new[] { color, section }.Where(part => !string.IsNullOrWhiteSpace(part)));

                items.Add(new TextItem(x + 2.5, y + barH - 1.8, label, 3.3, FontFamily));
                items.Add(new TextItem(x + cellW - 2.5, y + barH - 1.8, $"[{end}]", 2.4, FontFamily, TextAlign.Right, Color: "#5F6670"));
                items.Add(new TextItem(x + 2.5, y + barH + 5.2, $"Bu uç: {here}", 2.2, FontFamily));
                items.Add(new TextItem(x + 2.5, y + barH + 10.2, $"Karşı: {there}", 2.2, FontFamily, Color: "#3B4048"));
                if (!string.IsNullOrEmpty(spec))
                {
                    items.Add(new TextItem(x + 2.5, y + cellH - 2.2, spec, 2.0, FontFamily, Color: "#5F6670"));
                }
            }

            scenes.Add(new Scene(PageWidthMm, PageHeightMm, items)
            {
                Bookmark = p == 0 ? "Kablo ve Tel Etiketleri" : $"Etiketler ({p + 1})",
            });
        }

        return scenes;

        static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
    }

    /// <summary>Baskıya hazır kablo ve tel etiketlerini çok sayfalı PDF dosyasına yazar.</summary>
    public static Result Save(Project project, string path, int columns = 3, int rowsPerPage = 10)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(path);

        var scenes = BuildScenes(project, columns, rowsPerPage);
        return PdfExporter.Save(scenes, path, null, $"{project.Name} - Etiketler");
    }
}
