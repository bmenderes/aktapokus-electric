using System.Globalization;
using System.Text.RegularExpressions;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Render;

/// <summary>Dikdörtgen, milimetre.</summary>
public readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>
/// Şablondan hesaplanan sayfa bölgeleri. Çerçevenin üstünde kolon bandı, iki yanında satır bandı,
/// altında antet vardır; kalan alan çizim alanıdır.
/// </summary>
public sealed record SheetGeometry(Box Frame, Box DrawingArea, Box TitleBlock, double ColumnWidth, double RowHeight)
{
    public static SheetGeometry For(SheetTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var m = template.MarginMm;
        var g = template.GridBandMm;
        var frame = new Box(m, m, template.WidthMm - (2 * m), template.HeightMm - (2 * m));
        var titleBlock = new Box(m, frame.Bottom - template.TitleBlockHeightMm, frame.Width, template.TitleBlockHeightMm);
        var drawing = new Box(m + g, m + g, frame.Width - (2 * g), titleBlock.Y - (m + g));

        return new SheetGeometry(
            frame, drawing, titleBlock, drawing.Width / template.Columns, drawing.Height / template.Rows);
    }
}

/// <summary>Bir yerleşimin sayfada kapladığı kutu.</summary>
public sealed record PlacedBox(PlacementId Placement, Box Bounds);

/// <summary>Sayfada çizilen bir telin yolu (köşe noktaları, mm); <c>Label</c> tel numarası ya da damar adıdır.</summary>
public sealed record WirePath(ConnectionId Connection, string? Label, IReadOnlyList<(double X, double Y)> Path);

/// <summary>Sayfadaki bir bağlantı noktası: kimliği, adı ve yeri (mm).</summary>
public sealed record PlacedPinInfo(
    ConnectionPointId Point, string Name, double X, double Y, double DirectionX = 0, double DirectionY = 0);

/// <summary>Bir sayfanın çerçevesini, ızgarasını ve antetini sahneye çevirir.</summary>
public static partial class SheetRenderer
{
    private const double LabelSizeMm = 1.8;
    private const double CellPaddingMm = 0.8;

    [GeneratedRegex(@"\{(?<scope>project|page)\.(?<key>[^{}\s]+)\}|\{(?<today>today)\}")]
    private static partial Regex Placeholder();

    /// <param name="today">
    /// <c>{today}</c> yer tutucusunun değeri. Dışarıdan verilir; böylece çıktı testte sabit kalır.
    /// </param>
    public static Result<Scene> Render(Project project, Page page, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        var template = project.TemplateFor(page);
        if (template is null)
        {
            return Result.Failure<Scene>(new Error(
                "render.sablon_yok",
                $"Sayfa {page.Number} için sayfa şablonu yok. Projeye bir şablon ekleyin (ake template set)."));
        }

        var geometry = SheetGeometry.For(template);
        var items = new List<SceneItem>();
        var warnings = new List<string>();

        AddFrameAndGrid(items, template, geometry);
        AddGraphics(items, project, page, template);
        if (IsGenerated(page.Kind))
        {
            // Kapak ve tablo sayfaları: içerik projeden üretilir (ADR-0046).
            AddGenerated(items, warnings, project, page, template, geometry);
        }
        else if (page.Kind == PageKind.CommunicationDiagram)
        {
            // Haberleşme şeması: içerik projeden üretilir, çerçeve ve antet normal şablondan gelir (ADR-0037).
            CommunicationDiagram.AddTo(items, warnings, project, geometry.DrawingArea);
        }
        else if (page.Kind == PageKind.SingleLineDiagram)
        {
            SingleLineDiagram.AddTo(items, warnings, project, geometry.DrawingArea);
        }
        else
        {
            SchematicRenderer.Add(items, warnings, project, page, template, geometry);
        }

        AddTitleBlock(items, template, geometry, text => Resolve(text, project, page, today));

        return Result.Success(new Scene(template.WidthMm, template.HeightMm, items, warnings)
        {
            Bookmark = BookmarkOf(page, project.Properties.GetValueOrDefault("language")),
            Anchor = AnchorOf(page),
        });
    }

    /// <summary>
    /// Bir sayfa şablonunu çerçeve, kolon/satır ızgarası ve antetle birlikte canlı önizleme sahnesine çevirir.
    /// Proje veya sayfa verilmezse yer tutucular örnek değerlerle doldurulur.
    /// </summary>
    public static Result<Scene> PreviewTemplate(SheetTemplate template, Project? project, Page? page, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (template.Check() is { } invalid)
        {
            return Result.Failure<Scene>(invalid);
        }

        var geometry = SheetGeometry.For(template);
        var items = new List<SceneItem>();
        AddFrameAndGrid(items, template, geometry);
        AddTitleBlock(
            items,
            template,
            geometry,
            text => project is not null && page is not null
                ? Resolve(text, project, page, today)
                : ResolveSample(text, project, today));

        return Result.Success(new Scene(template.WidthMm, template.HeightMm, items));
    }

    private static string ResolveSample(string text, Project? project, DateOnly today)
    {
        return Placeholder().Replace(text, match =>
        {
            if (match.Groups["today"].Success)
            {
                return today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            }

            var key = match.Groups["key"].Value;
            if (match.Groups["scope"].Value == "project")
            {
                if (project is not null)
                {
                    return key == "name" ? project.Name : project.Properties.GetValueOrDefault(key, string.Empty);
                }

                return key switch
                {
                    "name" => "Örnek Proje",
                    "drawingNo" => "DRW-001",
                    "drawnBy" => "AKE",
                    "date" => today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture),
                    "revision" => "0",
                    _ => string.Empty,
                };
            }

            return key switch
            {
                "number" => "1",
                "title" => "Örnek Sayfa",
                "count" => (project?.Pages.Count ?? 1).ToString(CultureInfo.InvariantCulture),
                "next" => "2",
                "function" => "=M1",
                "location" => "+P1",
                _ => match.Value,
            };
        });
    }

    /// <summary>Sayfanın PDF yer imi başlığı: numara ve başlık.</summary>
    public static string BookmarkOf(Page page, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        var title = MultilingualText.Resolve(page.Title, language);
        return string.IsNullOrWhiteSpace(title) ? page.Number : $"{page.Number}  {title}";
    }

    /// <summary>Sayfanın PDF içindeki hedef adı; içindekiler satırları buna bağlanır.</summary>
    public static string AnchorOf(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return AnchorOf(page.Id);
    }

    /// <summary>Sayfanın PDF hedef adı, kimliğinden.</summary>
    public static string AnchorOf(PageId page) => $"sayfa-{page}";

    /// <summary>
    /// Sayfadaki sembollerin kutuları, sayfa koordinatlarında. Şablonu olmayan sayfada boş döner.
    /// </summary>
    public static IReadOnlyList<PlacedBox> SymbolBoxes(Project project, Page page)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        return project.TemplateFor(page) is { } template
            ? SchematicRenderer.Boxes(project, page, SheetGeometry.For(template))
            : [];
    }

    /// <summary>
    /// Sayfada çizilen tellerin yolları (sayfa koordinatları, mm); tel seçimi içindir. Şablonu olmayan sayfada boş döner.
    /// Telin iki ucu da bu sayfada değilse (sayfa ucu notuyla gösterilen tel) yolu yoktur.
    /// </summary>
    public static IReadOnlyList<WirePath> WirePaths(Project project, Page page)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        if (project.TemplateFor(page) is not { } template)
        {
            return [];
        }

        var paths = new List<WirePath>();
        SchematicRenderer.Add([], [], project, page, template, SheetGeometry.For(template), paths);
        return paths;
    }

    /// <summary>
    /// Köşe noktalı telin çizeceği yol (ADR-0029): uçtan çıkış noktasına, oradan sırayla <paramref name="route"/> noktalarına
    /// dik parçalarla, sonunda <paramref name="end"/> noktasına. Tel çekerken önizleme için de kullanılır (son nokta imleç).
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> RoutePreview(
        PlacedPinInfo start, IReadOnlyList<WirePoint> route, (double X, double Y) end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(route);

        var stub = new RouterPin(start.X, start.Y, start.DirectionX, start.DirectionY);
        return SchematicRenderer.Orthogonal(
            [(start.X, start.Y), (stub.EndX, stub.EndY), .. route.Select(point => (point.X, point.Y)), end],
            start.DirectionX,
            start.DirectionY);
    }

    /// <summary>Sayfadaki bağlantı noktalarının yerleri. Şablonu olmayan sayfada boş döner.</summary>
    public static IReadOnlyList<PlacedPinInfo> PinsOf(Project project, Page page)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        return project.TemplateFor(page) is { } template
            ? SchematicRenderer.Pins(project, page, SheetGeometry.For(template))
            : [];
    }

    /// <summary>Metindeki yer tutucuları doldurur. Bilinmeyen yer tutucu olduğu gibi kalır ki fark edilsin.</summary>
    public static string Resolve(string text, Project project, Page page, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(text);

        return Placeholder().Replace(text, match =>
        {
            if (match.Groups["today"].Success)
            {
                return today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
            }

            var key = match.Groups["key"].Value;
            if (match.Groups["scope"].Value == "project")
            {
                return key == "name" ? project.Name : project.Properties.GetValueOrDefault(key, string.Empty);
            }

            var index = project.Pages.ToList().IndexOf(page);
            return key switch
            {
                "number" => page.Number,
                "title" => MultilingualText.Resolve(page.Title, project.Properties.GetValueOrDefault("language")),
                "count" => project.Pages.Count.ToString(CultureInfo.InvariantCulture),
                "next" => index >= 0 && index + 1 < project.Pages.Count ? project.Pages[index + 1].Number : string.Empty,
                "function" => project.StructurePath(page.FunctionNodeId) is { } function ? $"={function}" : string.Empty,
                "location" => project.StructurePath(page.LocationNodeId) is { } location ? $"+{location}" : string.Empty,
                _ => match.Value,
            };
        });
    }

    private static void AddFrameAndGrid(List<SceneItem> items, SheetTemplate template, SheetGeometry geometry)
    {
        var frame = geometry.Frame;
        var area = geometry.DrawingArea;
        var band = template.GridBandMm;

        items.Add(new RectItem(frame.X, frame.Y, frame.Width, frame.Height, 0.5));
        if (band <= 0)
        {
            return;
        }

        var labelSize = Math.Min(band * 0.6, 3);

        // Üst bant: kolon numaraları.
        items.Add(new LineItem(frame.X, area.Y, frame.Right, area.Y));
        for (var column = 0; column <= template.Columns; column++)
        {
            var x = area.X + (column * geometry.ColumnWidth);
            items.Add(new LineItem(x, frame.Y, x, area.Y));
            if (column < template.Columns)
            {
                items.Add(new TextItem(
                    x + (geometry.ColumnWidth / 2),
                    frame.Y + (band / 2) + (labelSize * 0.35),
                    (template.FirstColumnNumber + column).ToString(CultureInfo.InvariantCulture),
                    labelSize,
                    template.FontFamily,
                    TextAlign.Center));
            }
        }

        // Yan bantlar: satır harfleri, iki yanda.
        items.Add(new LineItem(area.X, area.Y, area.X, area.Bottom));
        items.Add(new LineItem(area.Right, area.Y, area.Right, area.Bottom));
        for (var row = 0; row <= template.Rows; row++)
        {
            var y = area.Y + (row * geometry.RowHeight);
            items.Add(new LineItem(frame.X, y, area.X, y));
            items.Add(new LineItem(area.Right, y, frame.Right, y));
            if (row < template.Rows)
            {
                var letter = ((char)('A' + row)).ToString();
                var baseline = y + (geometry.RowHeight / 2) + (labelSize * 0.35);
                items.Add(new TextItem(frame.X + (band / 2), baseline, letter, labelSize, template.FontFamily, TextAlign.Center));
                items.Add(new TextItem(frame.Right - (band / 2), baseline, letter, labelSize, template.FontFamily, TextAlign.Center));
            }
        }
    }

    private static void AddTitleBlock(
        List<SceneItem> items, SheetTemplate template, SheetGeometry geometry, Func<string, string> resolve)
    {
        var block = geometry.TitleBlock;
        if (block.Height <= 0)
        {
            return;
        }

        items.Add(new LineItem(block.X, block.Y, block.Right, block.Y, 0.5));

        foreach (var cell in template.Cells)
        {
            var box = new Box(block.X + cell.XMm, block.Y + cell.YMm, cell.WidthMm, cell.HeightMm);
            if (cell.Border)
            {
                items.Add(new RectItem(box.X, box.Y, box.Width, box.Height));
            }

            if (cell.ImageRef is not null)
            {
                var imagePath = resolve(cell.ImageRef).Trim();
                if (string.Equals(cell.ImageRef, "logo.png", StringComparison.OrdinalIgnoreCase))
                {
                    var companyLogo = resolve("{project.companyLogo}").Trim();
                    if (companyLogo.Length > 0)
                    {
                        imagePath = companyLogo;
                    }
                }

                if (imagePath.Length > 0)
                {
                    items.Add(new ImageItem(
                        box.X + CellPaddingMm,
                        box.Y + CellPaddingMm,
                        box.Width - (2 * CellPaddingMm),
                        box.Height - (2 * CellPaddingMm),
                        imagePath));
                }
            }

            var labelHeight = 0.0;
            if (!string.IsNullOrEmpty(cell.Label))
            {
                labelHeight = LabelSizeMm + 1.4;
                items.Add(new TextItem(
                    box.X + CellPaddingMm, box.Y + LabelSizeMm + 0.6, cell.Label, LabelSizeMm, template.FontFamily));
            }

            if (string.IsNullOrEmpty(cell.Content))
            {
                continue;
            }

            var lines = resolve(cell.Content).Split('\n');
            var lineHeight = cell.FontSizeMm * 1.25;
            var available = box.Height - labelHeight;
            var firstBaseline = box.Y + labelHeight
                + ((available - (lines.Length * lineHeight)) / 2)
                + (cell.FontSizeMm * 0.9);
            var x = cell.Align switch
            {
                TextAlign.Center => box.X + (box.Width / 2),
                TextAlign.Right => box.Right - CellPaddingMm,
                _ => box.X + CellPaddingMm,
            };

            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                {
                    items.Add(new TextItem(
                        x, firstBaseline + (i * lineHeight), lines[i], cell.FontSizeMm, template.FontFamily, cell.Align));
                }
            }
        }
    }
}
