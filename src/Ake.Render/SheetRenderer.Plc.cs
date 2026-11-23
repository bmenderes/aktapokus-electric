using System.Globalization;
using Ake.Core.Model;
using Ake.Reports;

namespace Ake.Render;

/// <summary>
/// PLC şeması sayfası (ADR-0053): her PLC modülü, kanallarını satır satır gösteren başlıklı bir kutudur. Satırda kanal, adres, sembolik ad, açıklama,
/// modül ucu, klemens ve saha ucu (kanalın bağlı olduğu uç) yer alır. İçerik G/Ç raporundan (<see cref="ReportCatalog.Io"/>) gelir; sayfaya sığmayan
/// modüller devam sayfalarıyla sürer ve devam eden modülün başlığı yeniden yazılır.
/// </summary>
public static partial class SheetRenderer
{
    private static readonly string[] PlcColumns = ["Kanal", "Adres", "Sembolik ad", "Açıklama", "Modül ucu", "Klemens", "Saha ucu"];
    private static readonly double[] PlcFractions = [0.07, 0.11, 0.17, 0.20, 0.15, 0.13, 0.17];

    // Sayfadaki bir satır: modül başlığı, kanal ya da modüller arası boşluk.
    private sealed record PlcLine(PlcLineKind Kind, string Heading, string[] Cells);

    private enum PlcLineKind
    {
        Header,
        Channel,
        Spacer,
    }

    /// <summary>PLC şemasının sayfalara bölünmüş satırları (her sayfa en çok <paramref name="rowsPerPage"/> satır); PLC yoksa tek boş sayfa.</summary>
    private static List<List<PlcLine>> PlcPages(Project project, int rowsPerPage)
    {
        var table = ReportCatalog.Io(project);
        int Index(string key) => table.Columns.ToList().FindIndex(column => column.Key == key);
        var (controller, network, node, module) = (Index("controller"), Index("network"), Index("node"), Index("module"));
        var cells = new[] { "channel", "address", "symbol", "description", "point", "terminal", "target" }.Select(Index).ToArray();
        string Text(IReadOnlyList<object?> row, int column) => Convert.ToString(row[column], CultureInfo.InvariantCulture) ?? string.Empty;

        var devices = project.Devices.ToDictionary(device => project.DesignationOf(device).ToString(), device => device);
        var pages = new List<List<PlcLine>> { new() };
        List<PlcLine> Current() => pages[^1];

        string HeadingOf(IReadOnlyList<object?> row, bool continued)
        {
            var tag = Text(row, module);
            var device = devices.GetValueOrDefault(tag);
            var detail = string.Join("  ·  ", new[] { device?.Description, device?.Part?.OrderCode }.Where(part => !string.IsNullOrWhiteSpace(part)));
            return $"{tag}{(detail.Length > 0 ? "  ·  " + detail : string.Empty)}   ({Text(row, controller)} › {Text(row, network)} › {Text(row, node)}){(continued ? "   devam" : string.Empty)}";
        }

        string? lastModule = null;
        foreach (var row in table.Rows)
        {
            var tag = Text(row, module);
            if (tag != lastModule)
            {
                // Yeni modül: başlık ve en az bir kanal sığmıyorsa yeni sayfa; önceki modülden sonra boşluk.
                if (lastModule is not null && Current().Count + 1 < rowsPerPage)
                {
                    Current().Add(new PlcLine(PlcLineKind.Spacer, string.Empty, []));
                }

                if (Current().Count + 2 > rowsPerPage)
                {
                    pages.Add([]);
                }

                Current().Add(new PlcLine(PlcLineKind.Header, HeadingOf(row, continued: false), []));
                lastModule = tag;
            }
            else if (Current().Count + 1 > rowsPerPage)
            {
                pages.Add([]);
                Current().Add(new PlcLine(PlcLineKind.Header, HeadingOf(row, continued: true), []));
            }

            Current().Add(new PlcLine(PlcLineKind.Channel, string.Empty, [.. cells.Select(column => Text(row, column))]));
        }

        return pages;
    }

    private static int PlcPartCount(Project project, SheetTemplate template) => PlcPages(project, RowsPerPage(template)).Count;

    private static void AddPlcDiagram(List<SceneItem> items, List<string> warnings, Project project, Page page, SheetTemplate template, Box area)
    {
        var rowsPerPage = RowsPerPage(template);
        var pages = PlcPages(project, rowsPerPage);
        var left = area.X + 6;
        var width = area.Width - 12;
        var top = area.Y + TableHeadingMm + 8;

        items.Add(new TextItem(left, area.Y + TableHeadingMm + 3, page.Part > 0 ? $"{page.Title} (devam {page.Part + 1})" : page.Title, TableHeadingMm, template.FontFamily));

        var widths = PlcFractions.Select(fraction => fraction * width).ToArray();
        var starts = new double[widths.Length];
        var x = left;
        for (var c = 0; c < widths.Length; c++)
        {
            starts[c] = x;
            x += widths[c];
        }

        var y = top;
        for (var c = 0; c < PlcColumns.Length; c++)
        {
            items.Add(new TextItem(starts[c] + 2, y + TableRowMm - 1.8, PlcColumns[c], TableTextMm, template.FontFamily));
        }

        y += TableRowMm;
        items.Add(new LineItem(left, y, left + width, y, 0.35));

        if (page.Part < pages.Count)
        {
            double? blockTop = null;

            void CloseBlock(double bottom)
            {
                if (blockTop is { } start)
                {
                    items.Add(new RectItem(left, start, width, bottom - start, 0.35));
                    foreach (var boundary in starts.Skip(1))
                    {
                        items.Add(new LineItem(boundary, start + TableRowMm, boundary, bottom, 0.13));
                    }

                    blockTop = null;
                }
            }

            foreach (var line in pages[page.Part])
            {
                switch (line.Kind)
                {
                    case PlcLineKind.Header:
                        CloseBlock(y);
                        blockTop = y;
                        items.Add(new TextItem(left + 2, y + TableRowMm - 1.8, Fit(line.Heading, width - 4), TableTextMm + 0.3, template.FontFamily));
                        y += TableRowMm;
                        items.Add(new LineItem(left, y, left + width, y, 0.25));
                        break;
                    case PlcLineKind.Channel:
                        for (var c = 0; c < line.Cells.Length; c++)
                        {
                            if (line.Cells[c].Length > 0)
                            {
                                items.Add(new TextItem(starts[c] + 2, y + TableRowMm - 1.8, Fit(line.Cells[c], widths[c]), TableTextMm, template.FontFamily));
                            }
                        }

                        y += TableRowMm;
                        items.Add(new LineItem(left, y, left + width, y, 0.13));
                        break;
                    default:
                        CloseBlock(y);
                        y += TableRowMm;
                        break;
                }
            }

            CloseBlock(y);
        }

        if (pages.Count == 1 && pages[0].Count == 0)
        {
            items.Add(new TextItem(left + 2, y + TableRowMm - 1.8, "(PLC modülü yok)", TableTextMm, template.FontFamily));
        }

        var continued = project.Pages.Any(other => other.Kind == page.Kind && other.Part > page.Part);
        if (page.Part < pages.Count - 1 && !continued)
        {
            warnings.Add("PLC şeması sürüyor: kalan modül ve kanallar bu sayfaya sığmadı; devam sayfası ekleyin.");
        }
        else if (page.Part > 0 && page.Part >= pages.Count)
        {
            warnings.Add("Bu devam sayfasında gösterilecek satır kalmadı; sayfayı silebilirsiniz.");
        }
    }
}
