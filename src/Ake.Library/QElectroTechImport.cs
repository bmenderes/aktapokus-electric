using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Ake.Core;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>Bir dosyanın içe aktarma sonucu: sembol ya da neden alınmadığı.</summary>
public sealed record ImportedSymbol(string File, SymbolDefinition? Symbol, Error? Error)
{
    /// <summary>Sembol alındı ama kullanıcının bilmesi gereken bir varsayım yapıldı (ör. uç konması).</summary>
    public string? Warning { get; init; }
}

/// <summary>
/// QElectroTech eleman dosyalarını (<c>.elmt</c>) kullanıcı sembolüne çevirir.
/// </summary>
/// <remarks>
/// <para>
/// Eleman XML'dir: <c>&lt;definition&gt;</c> içinde <c>&lt;names&gt;</c>, <c>&lt;informations&gt;</c>
/// ve çizimi taşıyan <c>&lt;description&gt;</c>. Koordinatlar piksel ve elemanın sıcak noktasına
/// göredir; ızgara 10 pikseldir. Varsayılan ölçek 0,5 mm/piksel: QElectroTech ızgarası 5 mm olur ve
/// uçlar sayfa ızgarasına oturur.
/// </para>
/// <para>
/// Sembol modelinde yay ve çoklu çizgi yoktur; çokgen çizgilere, yay ve elips kısa çizgi
/// parçalarına bölünür. Değişken yazılar (<c>dynamic_text</c>) alınmaz: etiketi uygulama kendisi yazar.
/// </para>
/// <para>
/// Lisans zorunludur (ADR-0001): <c>&lt;informations&gt;</c> içinde bir lisans adı (CC-BY, CC0…)
/// bulunamazsa ve çağıran bir lisans vermediyse eleman alınmaz.
/// </para>
/// </remarks>
public static partial class QElectroTechImport
{
    public const double DefaultScaleMmPerPixel = 0.5;

    /// <summary>
    /// Koleksiyonun lisansı: şemalarda kullanım serbest; şema dışında yeniden dağıtımda CC-BY 3.0. Koleksiyon
    /// makine öğrenmesi modeli eğitiminde kullanılamaz.
    /// </summary>
    public const string CollectionLicenseName =
        "CC-BY-3.0 (QElectroTech koleksiyonu; şemalarda serbest; makine öğrenmesi eğitiminde kullanılamaz)";

    private const int CurveSegments = 16;

    // Büyük-küçük harf duyarlıdır: lisans adları büyük harfle yazılır; Almanca "mit" ("ile") lisans değildir.
    [GeneratedRegex(@"\b(CC[- ]?BY(?:[- ]SA)?(?:[- ]?\d\.\d)?|CC0|Public Domain|Apache[- ]2\.0|MIT License|GPL[- ]?\d)\b")]
    private static partial Regex LicensePattern();

    /// <summary>Bir dosyayı ya da klasördeki bütün <c>.elmt</c> dosyalarını (alt klasörler dahil) okur.</summary>
    public static IReadOnlyList<ImportedSymbol> FromPath(
        string path, string? license = null, double scaleMmPerPixel = DefaultScaleMmPerPixel)
    {
        ArgumentNullException.ThrowIfNull(path);

        // Koleksiyon lisansı elemanlarda değil, koleksiyonun kökündeki ELEMENTS.LICENSE dosyasındadır.
        license ??= CollectionLicense(path);

        if (Directory.Exists(path))
        {
            return
            [
                .. Directory.EnumerateFiles(path, "*.elmt", SearchOption.AllDirectories)
                    .Order(StringComparer.Ordinal)
                    .Select(file => FromFile(file, Path.GetRelativePath(path, file), license, scaleMmPerPixel)),
            ];
        }

        return [FromFile(path, Path.GetFileName(path), license, scaleMmPerPixel)];
    }

    /// <summary>
    /// Verilen yolun kendisinde ya da üst klasörlerinde (en çok dört düzey) QElectroTech koleksiyon lisansı
    /// (<c>ELEMENTS.LICENSE</c>) arar. Dosya CC-BY 3.0'ı adlandırıyorsa o lisansı döner.
    /// </summary>
    public static string? CollectionLicense(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var directory = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path).Directory;
        for (var level = 0; directory is not null && level < 5; level++, directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "ELEMENTS.LICENSE");
            if (!File.Exists(file))
            {
                continue;
            }

            string text;
            try
            {
                text = File.ReadAllText(file, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }

            return text.Contains("Creative Commons Attribution 3.0", StringComparison.Ordinal)
                ? CollectionLicenseName
                : null;
        }

        return null;
    }

    /// <summary>Tek bir eleman metnini çevirir. <paramref name="name"/> kaynak ve kimlik içindir.</summary>
    public static Result<SymbolDefinition> FromXml(
        string xml, string name, string? license = null, double scaleMmPerPixel = DefaultScaleMmPerPixel)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(name);

        XElement definition;
        try
        {
            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                MaxCharactersFromEntities = 1024,
                MaxCharactersInDocument = 20_000_000,
            };
            using var xmlReader = XmlReader.Create(new StringReader(xml), settings);
            definition = XDocument.Load(xmlReader).Root
                ?? throw new XmlException("Kök eleman yok.");
        }
        catch (XmlException ex)
        {
            return Fail("qet.xml_gecersiz", $"{name}: XML okunamadı: {ex.Message}");
        }

        if (definition.Name.LocalName != "definition")
        {
            return Fail("qet.xml_gecersiz", $"{name}: QElectroTech elemanı değil (kök '{definition.Name.LocalName}').");
        }

        if (definition.Element("description") is not { } description)
        {
            return Fail("qet.xml_gecersiz", $"{name}: <description> yok.");
        }

        var information = definition.Element("informations")?.Value.Trim();
        var detected = information is null ? null : LicensePattern().Match(information) is { Success: true } match ? match.Value : null;
        var finalLicense = license ?? detected;
        if (finalLicense is null)
        {
            return Fail(
                "qet.lisans_yok",
                $"{name}: elemanın bilgisinde lisans bulunamadı; --license ile verin ya da elemanı almayın.");
        }

        var scale = scaleMmPerPixel;
        var shapes = new List<SymbolShape>();
        var pins = new List<SymbolPin>();
        foreach (var element in description.Elements())
        {
            switch (element.Name.LocalName)
            {
                case "line":
                    shapes.Add(new SymbolLine(
                        Number(element, "x1") * scale,
                        Number(element, "y1") * scale,
                        Number(element, "x2") * scale,
                        Number(element, "y2") * scale));
                    break;

                case "rect":
                    var width = Number(element, "width") * scale;
                    var height = Number(element, "height") * scale;
                    if (width > 0 && height > 0)
                    {
                        shapes.Add(new SymbolRect(Number(element, "x") * scale, Number(element, "y") * scale, width, height));
                    }

                    break;

                case "circle":
                    var diameter = Number(element, "diameter") * scale;
                    if (diameter > 0)
                    {
                        shapes.Add(new SymbolCircle(
                            (Number(element, "x") * scale) + (diameter / 2),
                            (Number(element, "y") * scale) + (diameter / 2),
                            diameter / 2));
                    }

                    break;

                case "ellipse":
                    AddEllipse(shapes, element, scale, 0, 360);
                    break;

                case "arc":
                    AddEllipse(shapes, element, scale, Number(element, "start"), Number(element, "angle"));
                    break;

                case "polygon":
                    AddPolygon(shapes, element, scale);
                    break;

                case "text":
                    // Yazı boyu punto cinsindendir. 0,55 katsayısı, varsayılan 9 puntoyu 0,5 mm/piksel ölçekte
                    // yaklaşık 2,5 mm yapar (şemadaki olağan yazı boyu). Çok satırlı yazı satırlara bölünür.
                    var text = (string?)element.Attribute("text");
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        var size = Math.Max(1, Number(element, "size", 9) * scale * 0.55);
                        var lines = text.ReplaceLineEndings("\n").Split('\n');
                        for (var line = 0; line < lines.Length; line++)
                        {
                            if (lines[line].Trim() is { Length: > 0 } content)
                            {
                                shapes.Add(new SymbolText(
                                    Number(element, "x") * scale,
                                    (Number(element, "y") * scale) + (line * size * 1.25),
                                    content,
                                    Math.Round(size, 2)));
                            }
                        }
                    }

                    break;

                case "terminal":
                    var (dx, dy) = ((string?)element.Attribute("orientation"))?.Trim().ToLowerInvariant() switch
                    {
                        "n" => (0, -1),
                        "s" => (0, 1),
                        "e" => (1, 0),
                        "w" => (-1, 0),
                        _ => (0, 0),
                    };
                    if ((dx, dy) == (0, 0))
                    {
                        return Fail("qet.uc_yonu", $"{name}: bilinmeyen uç yönü '{element.Attribute("orientation")?.Value}'.");
                    }

                    pins.Add(new SymbolPin(Number(element, "x") * scale, Number(element, "y") * scale, dx, dy));
                    break;
            }
        }

        if (pins.Count == 0)
        {
            return Fail("qet.uc_yok", $"{name}: elemanın bağlantı ucu yok (yalnızca çizim ya da antet olabilir).");
        }

        // Başlangıç noktası ilk uçtur (ADR-0013).
        var (ox, oy) = (pins[0].X, pins[0].Y);
        var symbol = new SymbolDefinition(
            DeterministicId(name),
            DisplayName(definition, name),
            [.. shapes.Select(shape => Shift(shape, -ox, -oy))],
            [.. pins.Select(pin => pin with { X = Round(pin.X - ox), Y = Round(pin.Y - oy) })],
            $"QElectroTech: {name}" + (string.IsNullOrEmpty(information) ? string.Empty : $" — {Shorten(information)}"),
            finalLicense);

        return symbol.Check() is { } error ? Result.Failure<SymbolDefinition>(error) : Result.Success(symbol);
    }

    private static ImportedSymbol FromFile(string file, string name, string? license, double scale)
    {
        string xml;
        try
        {
            xml = File.ReadAllText(file, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportedSymbol(name, null, new Error("qet.dosya_okunamadi", $"{name}: okunamadı: {ex.Message}"));
        }

        var result = FromXml(xml, name.Replace('\\', '/'), license, scale);
        return new ImportedSymbol(name, result.IsSuccess ? result.Value : null, result.Error);
    }

    /// <summary>Türkçe ad, yoksa İngilizce, yoksa ilk ad, yoksa dosya adı.</summary>
    private static string DisplayName(XElement definition, string file)
    {
        var names = definition.Element("names")?.Elements("name").ToList() ?? [];
        string? Lang(string lang) => names.FirstOrDefault(n => (string?)n.Attribute("lang") == lang)?.Value.Trim();
        var name = Lang("tr") ?? Lang("en") ?? names.FirstOrDefault()?.Value.Trim();
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileNameWithoutExtension(file) : name;
    }

    /// <summary>Aynı dosya yeniden alınınca aynı kimliği alır; böylece proje içinde güncellenir, çoğalmaz.</summary>
    private static SymbolId DeterministicId(string file)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes($"qelectrotech:{file.Replace('\\', '/')}"));
        return new SymbolId(new Guid(hash));
    }

    private static void AddEllipse(List<SymbolShape> shapes, XElement element, double scale, double startDeg, double sweepDeg)
    {
        var width = Number(element, "width") * scale;
        var height = Number(element, "height") * scale;
        if (width <= 0 || height <= 0 || sweepDeg == 0)
        {
            return;
        }

        var (cx, cy) = ((Number(element, "x") * scale) + (width / 2), (Number(element, "y") * scale) + (height / 2));
        if (Math.Abs(width - height) < 0.001 && Math.Abs(Math.Abs(sweepDeg) - 360) < 0.001)
        {
            shapes.Add(new SymbolCircle(cx, cy, width / 2));
            return;
        }

        // Açılar saat yönünün tersine ve doğudan ölçülür; ekranda y aşağı doğrudur.
        var segments = Math.Max(2, (int)Math.Ceiling(CurveSegments * Math.Abs(sweepDeg) / 360));
        (double X, double Y) At(double degrees)
        {
            var radians = degrees * Math.PI / 180;
            return (cx + (width / 2 * Math.Cos(radians)), cy - (height / 2 * Math.Sin(radians)));
        }

        var previous = At(startDeg);
        for (var i = 1; i <= segments; i++)
        {
            var next = At(startDeg + (sweepDeg * i / segments));
            shapes.Add(new SymbolLine(Round(previous.X), Round(previous.Y), Round(next.X), Round(next.Y)));
            previous = next;
        }
    }

    private static void AddPolygon(List<SymbolShape> shapes, XElement element, double scale)
    {
        var points = new List<(double X, double Y)>();
        for (var i = 1; element.Attribute($"x{i}") is not null && element.Attribute($"y{i}") is not null; i++)
        {
            points.Add((Number(element, $"x{i}") * scale, Number(element, $"y{i}") * scale));
        }

        for (var i = 1; i < points.Count; i++)
        {
            shapes.Add(new SymbolLine(points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y));
        }

        var closed = !string.Equals((string?)element.Attribute("closed"), "false", StringComparison.OrdinalIgnoreCase);
        if (closed && points.Count > 2)
        {
            shapes.Add(new SymbolLine(points[^1].X, points[^1].Y, points[0].X, points[0].Y));
        }
    }

    private static double Number(XElement element, string attribute, double fallback = 0) =>
        double.TryParse((string?)element.Attribute(attribute), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;

    private static double Round(double value) => Math.Round(value, 3) + 0.0;

    private static SymbolShape Shift(SymbolShape shape, double dx, double dy) => shape switch
    {
        SymbolLine l => l with { X1 = Round(l.X1 + dx), Y1 = Round(l.Y1 + dy), X2 = Round(l.X2 + dx), Y2 = Round(l.Y2 + dy) },
        SymbolRect r => r with { X = Round(r.X + dx), Y = Round(r.Y + dy) },
        SymbolCircle c => c with { X = Round(c.X + dx), Y = Round(c.Y + dy) },
        SymbolText t => t with { X = Round(t.X + dx), Y = Round(t.Y + dy) },
        _ => shape,
    };

    private static string Shorten(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= 160 ? line : line[..157] + "...";
    }

    private static Result<SymbolDefinition> Fail(string code, string message) =>
        Result.Failure<SymbolDefinition>(new Error(code, message));
}
