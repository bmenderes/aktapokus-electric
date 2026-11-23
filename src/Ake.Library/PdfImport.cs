using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Outline;

namespace Ake.Library;

/// <summary>PDF yer imlerinden okunan sayfa-içi cihaz ve uç listesi ipucu (ör. SEE Electrical "Komponentler" ağacı).</summary>
public sealed record PdfBookmarkComponent(string Tag, IReadOnlyList<string> PinNames);

/// <summary>PDF sayfasından okunan çizim: serbest grafik şekilleri, kaynak (PDF) koordinatında, mm, y aşağı.</summary>
/// <param name="Number">PDF sayfa numarası (1'den).</param>
/// <param name="Title">Yer iminden sayfa başlığı; yer imi yoksa boş.</param>
/// <param name="Frame">Kaynağın iç çerçevesi (çizim alanı); tespit edilemediyse <c>null</c>.</param>
/// <param name="BookmarkComponents">Varsa PDF yer imi ağacından bu sayfaya düşen cihaz ve uç ipuçları.</param>
public sealed record PdfPageDrawing(
    int Number,
    string? Title,
    double WidthMm,
    double HeightMm,
    IReadOnlyList<GraphicShape> Shapes,
    (double X, double Y, double Width, double Height)? Frame,
    IReadOnlyList<PdfBookmarkComponent>? BookmarkComponents = null);

/// <summary>Bir PDF'ten okunan bütün sayfalar ve atılan öğelerin özeti.</summary>
public sealed record PdfDrawing(IReadOnlyList<PdfPageDrawing> Pages, int IgnoredImages, int DroppedAsFrame);

/// <summary>
/// Vektör PDF'i (ör. başka bir E-CAD aracının PDF çıktısı) serbest grafiğe çevirir (ADR-0028). Çizgiler, eğriler
/// ve yazılar konumlarıyla alınır; görseller ve beyaz (görünmez) çizimler alınmaz. Taranmış (görüntü) PDF'ten
/// çizim çıkmaz.
/// </summary>
/// <remarks>
/// <para>
/// <b>Çerçeve.</b> Kaynağın çerçevesi, kolon/satır harfleri ve anteti kendi şablonumuzla çakışacağından
/// varsayılan olarak atılır. Çerçeve sayfalardan tespit edilir: sayfa boyunun %70'inden uzun yatay ve dikey
/// çizgilerin ayrık koordinatlarından, dıştan ikincisi iç çerçeve (çizim alanı) olur. Sayfaların çoğunda
/// uyuşan çerçeve belgenin çerçevesi sayılır. Çerçeveye değmeyen öğeler (alanın içindeki her şey) kalır.
/// </para>
/// <para>
/// <b>Eğriler</b> kısa çizgi parçalarına bölünür. <b>Yazı</b> aynı satırdaki ardışık harflerden kurulur;
/// dönüş açısı korunur. <b>Çizgi kalınlığı</b> kaynaktaki değerden alınır (0,1–2 mm).
/// </para>
/// </remarks>
public static class PdfImport
{
    private const double PointToMm = 25.4 / 72;
    private const int CurveSteps = 8;
    private const double MinStrokeMm = 0.1;
    private const double MaxStrokeMm = 1.5;
    private const double TypicalStrokeMm = 0.25;
    private const double FrameShrinkMm = 0.4;

    /// <summary>PDF'i okur; <paramref name="keepFrame"/> doğruysa kaynağın çerçeve ve anteti de alınır.</summary>
    public static Result<PdfDrawing> Read(string path, bool keepFrame = false)
    {
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            using var document = PdfDocument.Open(path);
            var titles = PageTitles(document);
            var components = ExtractBookmarkComponents(document);
            var pages = new List<PdfPageDrawing>();
            var ignoredImages = 0;
            for (var number = 1; number <= document.NumberOfPages; number++)
            {
                var page = document.GetPage(number);
                ignoredImages += page.NumberOfImages;
                var (shapes, longLines) = ReadPage(page);
                pages.Add(new PdfPageDrawing(
                    number,
                    titles.GetValueOrDefault(number),
                    page.Width * PointToMm,
                    page.Height * PointToMm,
                    shapes,
                    FrameOf(longLines, page.Width * PointToMm, page.Height * PointToMm),
                    components.GetValueOrDefault(number)));
            }

            var drawing = keepFrame ? new PdfDrawing(pages, ignoredImages, 0) : DropFrame(pages, ignoredImages);
            return Result.Success(drawing with { Pages = NormalizeStrokes(drawing.Pages) });
        }
#pragma warning disable CA1031 // Bozuk ya da şifreli PDF farklı istisnalar fırlatır; hepsi "okunamadı"dır.
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return Result.Failure<PdfDrawing>(new Error("pdf.okunamadi", $"'{Path.GetFileName(path)}' PDF olarak okunamadı: {ex.Message}"));
        }
    }

    /// <summary>
    /// Okunan çizimi projeye ekleyen tek işlem: her PDF sayfası, projenin sonuna yeni bir sayfa olur ve şekilleri
    /// serbest grafik olarak yerleşir (tek adımda geri alınır). Çerçeve atıldıysa içerik, kaynağın çizim alanından
    /// <paramref name="drawingAreaOrigin"/> (hedef şablonun çizim alanının sol üst köşesi, mm) noktasına kaydırılır; böylece
    /// kolon ve satırlar aynı yere düşer.
    /// </summary>
    /// <param name="template">Yeni sayfaların şablonu; boşsa projenin ilk şablonu kullanılır.</param>
    public static Result<Transaction> Plan(
        Project project, PdfDrawing drawing, (double X, double Y)? drawingAreaOrigin = null, SheetTemplateId? template = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(drawing);

        if (drawing.Pages.Count == 0)
        {
            return Result.Failure<Transaction>(new Error("pdf.bos", "PDF'te sayfa yok."));
        }

        var templateId = template ?? project.SheetTemplates.FirstOrDefault()?.Id;
        if (templateId is null)
        {
            return Result.Failure<Transaction>(new Error(
                "pdf.sablon_yok", "Projede sayfa şablonu yok; önce bir şablon ekleyin (ake template set)."));
        }

        var commands = new List<ICommand>();
        var start = project.Pages.Count;
        for (var i = 0; i < drawing.Pages.Count; i++)
        {
            var source = drawing.Pages[i];
            var number = (start + i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var page = new Page(PageId.New(), number, source.Title ?? $"Sayfa {source.Number}", templateId: templateId);
            commands.Add(new AddPage(page));

            var (dx, dy) = source.Frame is { } frame && drawingAreaOrigin is { } origin
                ? (origin.X - frame.X, origin.Y - frame.Y)
                : (0.0, 0.0);
            if (source.Shapes.Count > 0)
            {
                commands.Add(new AddGraphics(source.Shapes.Select(
                    shape => new PageGraphic(GraphicId.New(), page.Id, dx == 0 && dy == 0 ? shape : shape.Translate(dx, dy)))));
            }
        }

        return Result.Success(new Transaction($"PDF içe aktarıldı: {drawing.Pages.Count} sayfa", commands));
    }

    // Belgedeki ortanca çizgi kalınlığı 0,25 mm olur; diğerleri oranını korur (0,1–1,5 mm).
    private static List<PdfPageDrawing> NormalizeStrokes(IReadOnlyList<PdfPageDrawing> pages)
    {
        var widths = pages.SelectMany(page => page.Shapes.OfType<GraphicPolyline>()).Where(line => !line.Filled).Select(line => line.StrokeMm).Order().ToList();
        if (widths.Count == 0)
        {
            return [.. pages];
        }

        var factor = TypicalStrokeMm / widths[widths.Count / 2];
        return
        [
            .. pages.Select(page => page with
            {
                Shapes =
                [
                    .. page.Shapes.Select(shape => shape is GraphicPolyline line
                        ? line with { StrokeMm = Math.Clamp(Math.Round(line.StrokeMm * factor, 2), MinStrokeMm, MaxStrokeMm) }
                        : shape),
                ],
            }),
        ];
    }

    // Yer imlerinden sayfa başlıkları; bir sayfa için ilk eşleşen kazanır (kök sırası belgenin sırasıdır).
    // Daha derin düğümler (cihaz listeleri) alınmaz.
    private static Dictionary<int, string> PageTitles(PdfDocument document)
    {
        var titles = new Dictionary<int, string>();
        if (!document.TryGetBookmarks(out var bookmarks))
        {
            return titles;
        }

        // Kategorili yer imi ağacında (kök = kategori) sayfalar kökün çocuklarıdır; düz listede (çocuksuz kök) kökün kendisidir.
        foreach (var root in bookmarks.Roots)
        {
            var pages = root.Children.Count > 0 ? root.Children : [root];
            foreach (var node in pages.OfType<DocumentBookmarkNode>())
            {
                if (node.PageNumber > 0 && !string.IsNullOrWhiteSpace(node.Title))
                {
                    titles.TryAdd(node.PageNumber, node.Title.Trim());
                }
            }
        }

        return titles;
    }

    private static Dictionary<int, IReadOnlyList<PdfBookmarkComponent>> ExtractBookmarkComponents(PdfDocument document)
    {
        var result = new Dictionary<int, List<PdfBookmarkComponent>>();
        if (!document.TryGetBookmarks(out var bookmarks))
        {
            return [];
        }

        foreach (var root in bookmarks.Roots)
        {
            var isComponentRoot =
                root.Title.Contains("Komponent", StringComparison.OrdinalIgnoreCase)
                || root.Title.Contains("Component", StringComparison.OrdinalIgnoreCase)
                || root.Title.Contains("Device", StringComparison.OrdinalIgnoreCase)
                || root.Title.Contains("Cihaz", StringComparison.OrdinalIgnoreCase)
                || root.Children.Any(c => c.Children.OfType<DocumentBookmarkNode>().Any(g => g.Title.Contains(" : ", StringComparison.Ordinal)));
            if (!isComponentRoot)
            {
                continue;
            }

            foreach (var compNode in root.Children)
            {
                var tag = compNode.Title?.Trim();
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                foreach (var child in compNode.Children.OfType<DocumentBookmarkNode>())
                {
                    if (child.PageNumber <= 0)
                    {
                        continue;
                    }

                    var title = child.Title ?? string.Empty;
                    var sep = title.IndexOf(" : ", StringComparison.Ordinal);
                    IReadOnlyList<string> pins = sep > 0
                        ? [.. title[..sep]
                            .Split(';', StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .Where(p => p.Length > 0 && !p.Equals("(Empty)", StringComparison.OrdinalIgnoreCase))]
                        : [];
                    if (!result.TryGetValue(child.PageNumber, out var list))
                    {
                        list = [];
                        result[child.PageNumber] = list;
                    }

                    list.Add(new PdfBookmarkComponent(tag, pins));
                }
            }
        }

        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<PdfBookmarkComponent>)pair.Value);
    }

    private static (List<GraphicShape> Shapes, List<(double X1, double Y1, double X2, double Y2)> Long) ReadPage(
        UglyToad.PdfPig.Content.Page page)
    {
        var height = page.Height;
        var shapes = new List<GraphicShape>();
        var longSegments = new List<(double, double, double, double)>();
        var (minLong, minLongV) = (page.Width * 0.7, page.Height * 0.7);

        // Ardışık çizgiler aynı stilde uç uca geliyorsa tek çoklu çizgi olur (yüzlerce iki noktalı yol yerine).
        List<double>? chain = null;
        (double Width, string? Color) chainStyle = default;

        void Flush()
        {
            if (chain is { Count: >= 4 })
            {
                shapes.Add(new GraphicPolyline(chain, StrokeMm: chainStyle.Width) { Color = chainStyle.Color });
            }

            chain = null;
        }

        foreach (var path in page.Paths)
        {
            if (!path.IsStroked && !path.IsFilled)
            {
                continue;
            }

            var color = ColorOf(path.IsStroked ? path.StrokeColor : path.FillColor);
            if (color == White)
            {
                continue;
            }

            // Ham kalınlık: PdfPig, çizim matrisinin ölçeğini kalınlığa uygulamaz; oranlar Read'de ortancayla normalize edilir.
            var style = (Math.Max(path.LineWidth, 0.0001), color);
            foreach (var subpath in path)
            {
                var points = Points(subpath, height);
                if (points.Count < 4)
                {
                    continue;
                }

                var closed = subpath.IsClosed() || (points.Count >= 6 && points[0] == points[^2] && points[1] == points[^1]);
                if (closed && points.Count >= 6 && points[0] == points[^2] && points[1] == points[^1])
                {
                    points.RemoveRange(points.Count - 2, 2);
                }

                // Uzun, eksene paralel çizgiler çerçeve adayıdır (çizgi 2 noktalı ve açık).
                if (!closed && points.Count == 4)
                {
                    var (dx, dy) = (Math.Abs(points[2] - points[0]), Math.Abs(points[3] - points[1]));
                    if ((dy < 0.3 * PointToMm && dx >= minLong * PointToMm) || (dx < 0.3 * PointToMm && dy >= minLongV * PointToMm))
                    {
                        longSegments.Add((points[0], points[1], points[2], points[3]));
                    }
                }

                // Zincirleme: önceki çizginin son noktası bunun ilk noktasıysa ve stil aynıysa uzat.
                if (!closed && !path.IsFilled && chain is not null && chainStyle == style
                    && chain[^2] == points[0] && chain[^1] == points[1])
                {
                    chain.AddRange(points.Skip(2));
                    continue;
                }

                Flush();
                if (closed || path.IsFilled)
                {
                    shapes.Add(new GraphicPolyline(points, closed || path.IsFilled, style.Item1, path.IsFilled) { Color = color });
                }
                else
                {
                    chain = points;
                    chainStyle = style;
                }
            }
        }

        Flush();
        shapes.AddRange(Texts(page));
        return (shapes, longSegments);
    }

    private static List<double> Points(PdfSubpath subpath, double pageHeight)
    {
        var points = new List<double>();

        void Add(PdfPoint point)
        {
            var (x, y) = (Math.Round(point.X * PointToMm, 3), Math.Round((pageHeight - point.Y) * PointToMm, 3));
            if (points.Count >= 2 && points[^2] == x && points[^1] == y)
            {
                return;
            }

            points.Add(x);
            points.Add(y);
        }

        void Curve(PdfPoint p0, PdfPoint c1, PdfPoint c2, PdfPoint p3)
        {
            if (points.Count == 0)
            {
                Add(p0);
            }

            for (var i = 1; i <= CurveSteps; i++)
            {
                var t = (double)i / CurveSteps;
                var u = 1 - t;
                Add(new PdfPoint(
                    (u * u * u * p0.X) + (3 * u * u * t * c1.X) + (3 * u * t * t * c2.X) + (t * t * t * p3.X),
                    (u * u * u * p0.Y) + (3 * u * u * t * c1.Y) + (3 * u * t * t * c2.Y) + (t * t * t * p3.Y)));
            }
        }

        foreach (var command in subpath.Commands)
        {
            switch (command)
            {
                case PdfSubpath.Move move:
                    Add(move.Location);
                    break;
                case PdfSubpath.Line line:
                    if (points.Count == 0)
                    {
                        Add(line.From);
                    }

                    Add(line.To);
                    break;
                case PdfSubpath.CubicBezierCurve cubic:
                    Curve(cubic.StartPoint, cubic.FirstControlPoint, cubic.SecondControlPoint, cubic.EndPoint);
                    break;
                case PdfSubpath.QuadraticBezierCurve quadratic:
                    // İkinci derece eğri, eşdeğer üçüncü derece eğriye çevrilir.
                    var (a, c, b) = (quadratic.StartPoint, quadratic.ControlPoint, quadratic.EndPoint);
                    Curve(
                        a,
                        new PdfPoint(a.X + (2.0 / 3 * (c.X - a.X)), a.Y + (2.0 / 3 * (c.Y - a.Y))),
                        new PdfPoint(b.X + (2.0 / 3 * (c.X - b.X)), b.Y + (2.0 / 3 * (c.Y - b.Y))),
                        b);
                    break;
            }
        }

        return points;
    }

    private const string White = "#ffffff";

    // Siyah çizgi varsayılandır (renk yazılmaz); beyaz çizgi kâğıdı maskeleyen görünmez çizgidir.
    private static string? ColorOf(IColor? color)
    {
        if (color is null)
        {
            return null;
        }

        var (r, g, b) = color.ToRGBValues();
        var hex = $"#{ToByte(r):x2}{ToByte(g):x2}{ToByte(b):x2}";
        return hex == "#000000" ? null : hex;

        static int ToByte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    // Harfler içerik sırasıyla gelir; aynı satırda, aynı boyda, aynı yönde ve birbirine yakın olanlar bir yazı olur.
    private static IEnumerable<GraphicText> Texts(UglyToad.PdfPig.Content.Page page)
    {
        var height = page.Height;
        var run = new List<UglyToad.PdfPig.Content.Letter>();

        IEnumerable<GraphicText> Emit()
        {
            if (run.Count == 0)
            {
                yield break;
            }

            var first = run.First(letter => !string.IsNullOrWhiteSpace(letter.Value));
            var text = string.Concat(run.Select(letter => letter.Value)).Trim();
            run = [];
            var (dx, dy) = (first.EndBaseLine.X - first.StartBaseLine.X, first.EndBaseLine.Y - first.StartBaseLine.Y);
            var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
            if (text.Length > 0 && first.PointSize > 0)
            {
                yield return new GraphicText(
                    Math.Round(first.StartBaseLine.X * PointToMm, 3),
                    Math.Round((height - first.StartBaseLine.Y) * PointToMm, 3),
                    text.Replace('\n', ' ').Replace('\r', ' '),
                    Math.Round(first.PointSize * PointToMm, 2),
                    TextAlign.Left,
                    Math.Abs(angle) < 0.5 ? 0 : Math.Round(-angle, 1) + 0.0);
            }
        }

        foreach (var letter in page.Letters)
        {
            if (run.Count > 0 && !Continues(run[^1], letter))
            {
                foreach (var text in Emit())
                {
                    yield return text;
                }
            }

            if (run.Count > 0 || !string.IsNullOrWhiteSpace(letter.Value))
            {
                run.Add(letter);
            }
        }

        foreach (var text in Emit())
        {
            yield return text;
        }
    }

    private static bool Continues(UglyToad.PdfPig.Content.Letter previous, UglyToad.PdfPig.Content.Letter next)
    {
        if (Math.Abs(previous.PointSize - next.PointSize) > 0.05)
        {
            return false;
        }

        var (ux, uy) = (previous.EndBaseLine.X - previous.StartBaseLine.X, previous.EndBaseLine.Y - previous.StartBaseLine.Y);
        var length = Math.Sqrt((ux * ux) + (uy * uy));
        var (nx, ny) = (next.EndBaseLine.X - next.StartBaseLine.X, next.EndBaseLine.Y - next.StartBaseLine.Y);
        var nextLength = Math.Sqrt((nx * nx) + (ny * ny));
        if (length < 1e-6 || nextLength < 1e-6)
        {
            // Genişliksiz harf (boşluk): yalnızca konumuna bakılır.
            (ux, uy, length) = length < 1e-6 ? (1, 0, 1) : (ux, uy, length);
        }

        (ux, uy) = (ux / length, uy / length);
        if (nextLength > 1e-6 && Math.Abs((ux * (ny / nextLength)) - (uy * (nx / nextLength))) > 0.02)
        {
            return false;
        }

        var (rx, ry) = (next.StartBaseLine.X - previous.EndBaseLine.X, next.StartBaseLine.Y - previous.EndBaseLine.Y);
        var along = (rx * ux) + (ry * uy);
        var across = (rx * -uy) + (ry * ux);
        // Ardışık harfler birbirine değer; boşluk harfi ya da ayrı yazılmış sözcük arasında bundan büyük aralık kalır.
        return Math.Abs(across) < 0.3 && along > -0.5 * previous.PointSize && along < 0.35 * previous.PointSize;
    }

    // Uzun yatay ve dikey çizgilerin ayrık koordinatlarından (dıştan ikincisi) iç çerçeve.
    private static (double X, double Y, double Width, double Height)? FrameOf(
        List<(double X1, double Y1, double X2, double Y2)> longSegments, double width, double height)
    {
        _ = (width, height);
        var xs = longSegments.Where(s => Math.Abs(s.X1 - s.X2) < 0.3).Select(s => Math.Round(s.X1 * 2) / 2).Distinct().Order().ToList();
        var ys = longSegments.Where(s => Math.Abs(s.Y1 - s.Y2) < 0.3).Select(s => Math.Round(s.Y1 * 2) / 2).Distinct().Order().ToList();
        return xs.Count >= 4 && ys.Count >= 4
            ? (xs[1], ys[1], xs[^2] - xs[1], ys[^2] - ys[1])
            : null;
    }

    // Çerçeve belge düzeyindedir: sayfaların en az üçte birinde aynı (0,5 mm içinde) çıkan çerçeve.
    private static PdfDrawing DropFrame(List<PdfPageDrawing> pages, int ignoredImages)
    {
        var frames = pages.Where(page => page.Frame is not null).Select(page => page.Frame!.Value).ToList();
        var common = frames
            .GroupBy(frame => (Math.Round(frame.X * 2), Math.Round(frame.Y * 2), Math.Round(frame.Width * 2), Math.Round(frame.Height * 2)))
            .OrderByDescending(group => group.Count())
            .FirstOrDefault();
        if (common is null || common.Count() * 3 < pages.Count)
        {
            return new PdfDrawing(pages, ignoredImages, 0);
        }

        var frame = common.First();
        var (x0, y0, x1, y1) = (frame.X + FrameShrinkMm, frame.Y + FrameShrinkMm, frame.X + frame.Width - FrameShrinkMm, frame.Y + frame.Height - FrameShrinkMm);
        var dropped = 0;
        var result = new List<PdfPageDrawing>();
        foreach (var page in pages)
        {
            var kept = new List<GraphicShape>();
            foreach (var shape in page.Shapes)
            {
                var (x, y, width, height) = shape.Bounds;
                var inside = shape is GraphicText
                    ? x >= x0 && x <= x1 && y >= y0 && y <= y1
                    : x + width >= x0 && x <= x1 && y + height >= y0 && y <= y1;
                if (inside)
                {
                    kept.Add(shape);
                }
                else
                {
                    dropped++;
                }
            }

            result.Add(page with { Shapes = kept, Frame = frame });
        }

        return new PdfDrawing(result, ignoredImages, dropped);
    }
}
