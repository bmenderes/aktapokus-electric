using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Ake.Core;
using Ake.Core.Model;
using CSMath;

namespace Ake.Library;

/// <summary>
/// DXF ve DWG çizimlerini (ör. üreticinin ürün sayfasından indirilen ön görünüş) kullanıcı sembolüne çevirir
/// (ADR-0027).
/// </summary>
/// <remarks>
/// <para>
/// Varsayılan olarak çizimin model alanı tek sembol olur; <c>blocks</c> istenirse adlandırılmış her blok ayrı
/// sembol olur (sembol kütüphanesi biçiminde dağıtılan DXF'ler için). Blok ekleri (INSERT) ve ölçülerin
/// çizimleri açılarak alınır; kapalı ya da dondurulmuş katmanlar alınmaz.
/// </para>
/// <para>
/// Birim, dosyanın <c>$INSUNITS</c> değerinden milimetreye çevrilir (birimsiz çizim milimetre sayılır), sonra
/// <c>scale</c> ile çarpılır. Sembol modelinde yay ve çoklu çizgi yoktur; yay, elips, eğri ve çıkıntılı
/// (bulge) çoklu çizgi kısa çizgi parçalarına bölünür. Tarama (hatch), görsel ve öznitelikler alınmaz.
/// Döndürülmüş yazılar alınmaz.
/// </para>
/// <para>
/// Bağlantı uçları çizimdeki POINT nesneleridir; yönleri çizimin en yakın kenarına doğrudur. Çizimde nokta
/// yoksa tek bir uç konur: blokta blok taban noktasına, model alanında çizimin sol üst köşesine. Böylece
/// sembol yerleştirilebilir; uçlar sonra blok editöründe düzeltilir.
/// </para>
/// <para>
/// Lisans zorunludur (ADR-0001). Üretici çizimleri lisans taşımaz; çağıran, kullanım koşulunu (ör. "üretici
/// çizimi, yalnızca bu projede") verir.
/// </para>
/// </remarks>
/// <summary>DXF/DWG dosyasının içeriğine bakış (<see cref="DxfImport.Inspect"/>).</summary>
/// <param name="Version">AutoCAD sürümü (ör. "AC1027 · AutoCAD 2013").</param>
/// <param name="Units">Dosyanın çizim birimi ($INSUNITS); birimsiz dosya milimetre sayılır.</param>
/// <param name="Extents">Alınabilen geometrinin sınır kutusu, çizim biriminde; geometri yoksa <c>null</c>.</param>
/// <param name="Entities">Model alanındaki öğe türleri ve sayıları.</param>
/// <param name="Ignored">Alınmayan öğe türleri ve sayıları (tarama, görsel, 3B).</param>
public sealed record DxfInfo(
    string File,
    string Version,
    string Units,
    (double X, double Y, double Width, double Height)? Extents,
    int Points,
    IReadOnlyList<(string Type, int Count)> Entities,
    IReadOnlyList<(string Type, int Count)> Ignored,
    IReadOnlyList<DxfLayerInfo> Layers,
    IReadOnlyList<(string Name, int Entities)> Blocks);

public sealed record DxfLayerInfo(string Name, bool Visible, int Entities);

public static class DxfImport
{
    // Sembole girmeyen (çizgi olarak okunamayan) öğe türleri: bilgi için sayılır ve kullanıcıya söylenir.
    private static readonly HashSet<string> IgnoredTypes =
    [
        "Hatch", "RasterImage", "Wipeout", "Solid3D", "Mesh", "Face3D", "PolyfaceMesh", "Body", "Region", "Ole2Frame", "Underlay",
        "PdfUnderlay", "DwfUnderlay", "DgnUnderlay", "Shape", "Viewport",
    ];

    private const int CurveSegments = 32;
    private const int MaxDepth = 16;

    // Çıktıda yazı yüksekliği büyük harf yüksekliğidir (CadExporter.CapHeightRatio).
    private const double CapHeightRatio = 0.7;

    /// <summary>Uzantı DXF, DWG ya da sıkıştırılmış .dxf.zip mi.</summary>
    public static bool IsCadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetExtension(path).ToLowerInvariant() is ".dxf" or ".dwg"
            || path.EndsWith(".dxf.zip", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Bir dosyayı ya da klasördeki bütün DXF/DWG dosyalarını (alt klasörler dahil) okur.</summary>
    /// <param name="area">
    /// Verilirse yalnızca bu dikdörtgenin (x, y, genişlik, yükseklik; <b>dosyanın çizim biriminde</b>, y yukarı) tamamen içinde kalan
    /// öğeler alınır. Üretici çizimleri tek dosyada birden çok görünüm (ön, üst, yan) taşır; bir görünümü seçmek içindir.
    /// Sınırlar <see cref="Inspect"/> ile görülür.
    /// </param>
    public static IReadOnlyList<ImportedSymbol> FromPath(
        string path, string? license, double scale = 1, bool blocks = false, (double X, double Y, double Width, double Height)? area = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!(scale > 0) || !double.IsFinite(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Ölçek sıfırdan büyük olmalı.");
        }

        if (Directory.Exists(path))
        {
            return
            [
                .. Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Where(IsCadFile)
                    .Order(StringComparer.Ordinal)
                    .SelectMany(file => FromFile(file, Path.GetRelativePath(path, file).Replace('\\', '/'), license, scale, blocks, area)),
            ];
        }

        return FromFile(path, Path.GetFileName(path), license, scale, blocks, area);
    }

    /// <summary>
    /// Dosyanın içine bakar, hiçbir şey almadan: sürüm, birim, geometrinin sınırları, öğe türleri, katmanlar, bloklar, uç noktaları ve
    /// alınmayacak öğeler. İçe aktarmadan önce çizimin hangi parçasının (görünümünün) seçileceğini bulmak içindir.
    /// </summary>
    public static Result<DxfInfo> Inspect(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var name = Path.GetFileName(path);
        var opened = ReadDocument(path, name);
        if (opened.IsFailure)
        {
            return Result.Failure<DxfInfo>(opened.Error!);
        }

        var document = opened.Value.Document;

        var model = document.BlockRecords[BlockRecord.ModelSpaceName];
        var entities = model.Entities.ToList();
        var drawing = new Drawing();
        foreach (var entity in entities)
        {
            drawing.Visit(entity, 0);
        }

        (double, double, double, double)? extents = null;
        if (drawing.Segments.Count + drawing.Circles.Count + drawing.Texts.Count + drawing.Points.Count > 0)
        {
            var (minX, minY, maxX, maxY) = drawing.Bounds();
            extents = (Math.Round(minX, 3), Math.Round(minY, 3), Math.Round(maxX - minX, 3), Math.Round(maxY - minY, 3));
        }

        List<(string, int)> Count(IEnumerable<string> names) =>
            [.. names.GroupBy(type => type).Select(group => (group.Key, group.Count())).OrderByDescending(item => item.Item2).ThenBy(item => item.Item1, StringComparer.Ordinal)];

        var types = entities.Select(entity => entity.GetType().Name).ToList();
        var layers = entities
            .GroupBy(entity => entity.Layer?.Name ?? "0")
            .Select(group => new DxfLayerInfo(
                group.Key,
                group.First().Layer is not { } layer || (layer.IsOn && !layer.Flags.HasFlag(LayerFlags.Frozen)),
                group.Count()))
            .OrderBy(layer => layer.Name, StringComparer.Ordinal)
            .ToList();
        var blocks = document.BlockRecords
            .Where(block => !block.Name.StartsWith('*') && !block.Name.StartsWith('_') && !block.IsAnonymous
                && !block.Name.Equals(BlockRecord.ModelSpaceName, StringComparison.OrdinalIgnoreCase)
                && !block.Name.StartsWith(BlockRecord.PaperSpaceName, StringComparison.OrdinalIgnoreCase))
            .Select(block => (block.Name, block.Entities.Count))
            .OrderBy(block => block.Name, StringComparer.Ordinal)
            .ToList();

        return Result.Success(new DxfInfo(
            name,
            $"{document.Header.Version} · {VersionName(document.Header.Version)}",
            document.Header.InsUnits.ToString(),
            extents,
            drawing.Points.Count,
            Count(types),
            Count(types.Where(IgnoredTypes.Contains)),
            layers,
            blocks));
    }

    private static string VersionName(ACadVersion version) => version switch
    {
        ACadVersion.AC1009 => "AutoCAD R11/R12",
        ACadVersion.AC1012 => "AutoCAD R13",
        ACadVersion.AC1014 => "AutoCAD R14",
        ACadVersion.AC1015 => "AutoCAD 2000",
        ACadVersion.AC1018 => "AutoCAD 2004",
        ACadVersion.AC1021 => "AutoCAD 2007",
        ACadVersion.AC1024 => "AutoCAD 2010",
        ACadVersion.AC1027 => "AutoCAD 2013",
        ACadVersion.AC1032 => "AutoCAD 2018",
        _ => "bilinmeyen sürüm",
    };

    private static IReadOnlyList<ImportedSymbol> FromFile(
        string file, string name, string? license, double scale, bool blocks, (double X, double Y, double Width, double Height)? area)
    {
        if (string.IsNullOrWhiteSpace(license))
        {
            return [Failed(name, "dxf.lisans_yok", $"{name}: lisans ya da kullanım koşulu verilmedi (--license).")];
        }

        var opened = ReadDocument(file, name);
        if (opened.IsFailure)
        {
            return [Failed(name, opened.Error!.Code, opened.Error.Message)];
        }

        var (document, kind) = opened.Value;

        var k = UnitsToMm(document.Header.InsUnits) * scale;
        if (!blocks)
        {
            var model = document.BlockRecords[BlockRecord.ModelSpaceName];
            var baseName = name.EndsWith(".dxf.zip", StringComparison.OrdinalIgnoreCase)
                ? name[..^".dxf.zip".Length]
                : Path.GetFileNameWithoutExtension(name);
            return [Convert(model.Entities, name, baseName, $"{kind}: {name}", license, k, null, area)];
        }

        var results = new List<ImportedSymbol>();
        foreach (var block in document.BlockRecords.OrderBy(b => b.Name, StringComparer.Ordinal))
        {
            if (block.Name.StartsWith('*') || block.Name.StartsWith('_') || block.IsAnonymous || block.Entities.Count == 0
                || block.Name.Equals(BlockRecord.ModelSpaceName, StringComparison.OrdinalIgnoreCase)
                || block.Name.StartsWith(BlockRecord.PaperSpaceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var origin = block.BlockEntity.BasePoint;
            results.Add(Convert(
                block.Entities, $"{name}#{block.Name}", block.Name, $"{kind}: {name}, blok {block.Name}", license, k, (origin.X, origin.Y), area));
        }

        return results.Count > 0 ? results : [Failed(name, "dxf.blok_yok", $"{name}: adlandırılmış blok yok.")];
    }

    private static ImportedSymbol Convert(
        IEnumerable<Entity> entities,
        string key,
        string displayName,
        string source,
        string license,
        double k,
        (double X, double Y)? anchor,
        (double X, double Y, double Width, double Height)? area)
    {
        var drawing = new Drawing();
        foreach (var entity in entities)
        {
            drawing.Visit(entity, 0);
        }

        if (area is { } window)
        {
            drawing.Crop(window);
        }

        if (drawing.Segments.Count == 0 && drawing.Circles.Count == 0 && drawing.Texts.Count == 0)
        {
            return Failed(
                key,
                "dxf.bos",
                area is null
                    ? $"{key}: alınabilecek çizim öğesi yok."
                    : $"{key}: verilen alanda alınabilecek çizim öğesi yok (alan çizim biriminde; sınırlar için 'ake symbol inspect').");
        }

        var (minX, minY, maxX, maxY) = drawing.Bounds();
        var pins = new List<(double X, double Y, int Dx, int Dy)>();
        foreach (var (x, y) in drawing.Points)
        {
            // En yakın kenara doğru; DXF'te y yukarı doğrudur, sembolde aşağı.
            var sides = new (double Distance, int Dx, int Dy)[]
            {
                (x - minX, -1, 0), (maxX - x, 1, 0), (maxY - y, 0, -1), (y - minY, 0, 1),
            };
            var nearest = sides.MinBy(side => side.Distance);
            pins.Add((x, y, nearest.Dx, nearest.Dy));
        }

        // Üretici kütüphaneleri uçları POINT ile işaretlemez; sembolün alt ve üst kenarına uzanan serbest çizgi uçları uç sayılır.
        if (pins.Count == 0)
        {
            pins.AddRange(InferPins(drawing));
        }

        string? warning = null;
        if (pins.Count == 0)
        {
            var (ax, ay) = anchor ?? (minX, maxY);
            pins.Add((ax, ay, 0, -1));
            warning = anchor is null
                ? "çizimde uç (POINT) yok; sol üst köşeye tek uç kondu."
                : "blokta uç (POINT) yok; taban noktasına tek uç kondu.";
        }

        // Başlangıç noktası ilk uçtur (ADR-0013); y ekseni çevrilir.
        var (ox, oy) = (pins[0].X, pins[0].Y);
        double X(double x) => Round((x - ox) * k);
        double Y(double y) => Round(-(y - oy) * k);

        var shapes = new List<SymbolShape>();
        foreach (var (x1, y1, x2, y2) in drawing.Segments)
        {
            var line = new SymbolLine(X(x1), Y(y1), X(x2), Y(y2));
            if (line.X1 != line.X2 || line.Y1 != line.Y2)
            {
                shapes.Add(line);
            }
        }

        shapes.AddRange(drawing.Circles
            .Select(c => new SymbolCircle(X(c.X), Y(c.Y), Round(c.Radius * k)))
            .Where(c => c.Radius > 0));
        shapes.AddRange(drawing.Texts
            .Select(t => new SymbolText(X(t.X), Y(t.Y), t.Text, Math.Max(0.5, Round(t.Height / CapHeightRatio * k)), t.Align)));

        var symbol = new SymbolDefinition(
            DeterministicId(key),
            displayName,
            shapes,
            [.. pins.Select(pin => new SymbolPin(X(pin.X), Y(pin.Y), pin.Dx, pin.Dy)).DistinctBy(pin => (pin.X, pin.Y))],
            source,
            license);

        return symbol.Check() is { } error
            ? new ImportedSymbol(key, null, error)
            : new ImportedSymbol(key, symbol, null) { Warning = warning };
    }

    // POINT'siz sembol için uç çıkarımı: yalnızca küçük çizimlerde, hiçbir başka çizgiye bağlanmayan (serbest) bir çizgi ucu sembolün en üst ya da
    // en alt kenarındaysa uçtur. En az iki uç bulunamazsa hiçbiri alınmaz (çağıran tek varsayılan uç koyar). Sıra: üsttekiler soldan sağa, sonra
    // alttakiler soldan sağa. DXF'te y yukarı doğrudur: en büyük y yukarıya bakar.
    private static List<(double X, double Y, int Dx, int Dy)> InferPins(Drawing drawing)
    {
        const double tolerance = 0.01;
        if (drawing.Segments.Count is 0 or > 80)
        {
            return [];
        }

        // Uç olacak çizgi dikey olmalıdır (telin geldiği yön); köşegen bir çizgi uç sayılmaz.
        var ends = drawing.Segments
            .SelectMany(segment => new[] { (X: segment.X1, Y: segment.Y1), (X: segment.X2, Y: segment.Y2) })
            .ToList();
        var vertical = drawing.Segments
            .Where(segment => Math.Abs(segment.X1 - segment.X2) < tolerance)
            .SelectMany(segment => new[] { (X: segment.X1, Y: segment.Y1), (X: segment.X2, Y: segment.Y2) })
            .ToList();
        var top = ends.Max(end => end.Y);
        var bottom = ends.Min(end => end.Y);
        if (top - bottom < 3)
        {
            return [];
        }

        bool Same((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) < tolerance && Math.Abs(a.Y - b.Y) < tolerance;
        var free = vertical.Where(end => ends.Count(other => Same(end, other)) == 1).ToList();
        var pins = new List<(double X, double Y, int Dx, int Dy)>();
        pins.AddRange(free.Where(end => Math.Abs(end.Y - top) < tolerance).OrderBy(end => end.X).Select(end => (end.X, end.Y, 0, -1)));
        pins.AddRange(free.Where(end => Math.Abs(end.Y - bottom) < tolerance).OrderBy(end => end.X).Select(end => (end.X, end.Y, 0, 1)));
        return pins.Count >= 2 ? pins : [];
    }

    /// <summary>Aynı dosya (ve blok) yeniden alınınca aynı kimliği alır; böylece güncellenir, çoğalmaz.</summary>
    private static SymbolId DeterministicId(string key) =>
        new(new Guid(MD5.HashData(Encoding.UTF8.GetBytes($"dxf:{key.Replace('\\', '/')}"))));

    private static double UnitsToMm(UnitsType units) => units switch
    {
        UnitsType.Inches => 25.4,
        UnitsType.Feet => 304.8,
        UnitsType.Yards => 914.4,
        UnitsType.Mils => 0.0254,
        UnitsType.Microns => 0.001,
        UnitsType.Centimeters => 10,
        UnitsType.Decimeters => 100,
        UnitsType.Meters => 1000,
        _ => 1,
    };

    private static double Round(double value) => Math.Round(value, 3) + 0.0;

    private static ImportedSymbol Failed(string name, string code, string message) => new(name, null, new Error(code, message));

    internal static Result<(CadDocument Document, string Kind)> ReadDocument(string path, string? displayName = null)
    {
        var name = displayName ?? Path.GetFileName(path);
        if (path.EndsWith(".dxf.zip", StringComparison.OrdinalIgnoreCase))
        {
            string? tempFile = null;
            try
            {
                using var zip = ZipFile.OpenRead(path);
                var entry = zip.Entries.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.Name)
                    && (e.Name.EndsWith(".dxf", StringComparison.OrdinalIgnoreCase)
                        || e.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase)));
                if (entry is null)
                {
                    return Result.Failure<(CadDocument, string)>(
                        new Error("dxf.okunamadi", $"{name}: ZIP arşivinde DXF/DWG dosyası bulunamadı."));
                }

                var isDwg = entry.Name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase);
                tempFile = Path.Combine(Path.GetTempPath(), $"ake-cad-{Guid.NewGuid():N}{(isDwg ? ".dwg" : ".dxf")}");
                CopyEntryBounded(entry, tempFile, 100L * 1024 * 1024);
                var doc = isDwg ? DwgReader.Read(tempFile) : DxfReader.Read(tempFile);
                return Result.Success((doc, isDwg ? "DWG" : "DXF"));
            }
#pragma warning disable CA1031
            catch (Exception ex)
#pragma warning restore CA1031
            {
                return Result.Failure<(CadDocument, string)>(
                    new Error("dxf.okunamadi", $"{name}: ZIP içinden DXF okunamadı: {ex.Message}"));
            }
            finally
            {
                if (tempFile is not null && File.Exists(tempFile))
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
        }

        var kind = Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase) ? "DWG" : "DXF";
        try
        {
            var document = kind == "DWG" ? DwgReader.Read(path) : DxfReader.Read(path);
            return Result.Success((document, kind));
        }
#pragma warning disable CA1031 // Okuyucu bozuk dosyada farklı türde istisnalar fırlatır; hepsi "okunamadı"dır.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            return Result.Failure<(CadDocument, string)>(new Error("dxf.okunamadi", $"{name}: {kind} okunamadı: {ex.Message}"));
        }
    }

    private static void CopyEntryBounded(ZipArchiveEntry entry, string destinationPath, long maxBytes)
    {
        using var src = entry.Open();
        using var dst = File.Create(destinationPath);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException($"ZIP girdisi boyut sınırını ({maxBytes / (1024 * 1024)} MB) aşıyor: '{entry.Name}'.");
            }

            dst.Write(buffer, 0, read);
        }
    }

    /// <summary>Çizim nesnelerinden toplanan düz geometri (DXF koordinatlarında, y yukarı).</summary>
    private sealed class Drawing
    {
        public List<(double X1, double Y1, double X2, double Y2)> Segments { get; } = [];

        public List<(double X, double Y, double Radius)> Circles { get; } = [];

        public List<(double X, double Y, string Text, double Height, TextAlign Align)> Texts { get; } = [];

        public List<(double X, double Y)> Points { get; } = [];

        public void Visit(Entity entity, int depth)
        {
            if (depth > MaxDepth || entity.IsInvisible || (entity.Layer is { } layer && (!layer.IsOn || layer.Flags.HasFlag(LayerFlags.Frozen))))
            {
                return;
            }

            switch (entity)
            {
                case Line line:
                    Segments.Add((line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y));
                    break;

                case Arc arc:
                    var steps = Math.Max(2, (int)Math.Ceiling(CurveSegments * Math.Abs(arc.Sweep) / (2 * Math.PI)));
                    Chain(Ocs(arc.PolygonalVertexes(steps + 1), arc.Normal), closed: false);
                    break;

                case Circle circle:
                    Circles.Add((circle.Normal.Z < 0 ? -circle.Center.X : circle.Center.X, circle.Center.Y, circle.Radius));
                    break;

                case Ellipse ellipse:
                    Chain([.. ellipse.PolygonalVertexes((CurveSegments * 2) + 1).Select(p => (p.X, p.Y))], closed: false);
                    break;

                case LwPolyline polyline:
                    Polyline([.. polyline.Vertices.Select(v => (v.Location.X, v.Location.Y, v.Bulge))], polyline.IsClosed, polyline.Normal);
                    break;

                case IPolyline polyline:
                    Polyline([.. polyline.Vertices.Select(v => (Coordinate(v.Location, 0), Coordinate(v.Location, 1), v.Bulge))], polyline.IsClosed, XYZ.AxisZ);
                    break;

                case Spline spline:
                    if (spline.TryPolygonalVertexes(CurveSegments * 2, out var vertices) && vertices.Count > 1)
                    {
                        Chain([.. vertices.Select(p => (p.X, p.Y))], closed: false);
                    }
                    else
                    {
                        Chain([.. spline.ControlPoints.Select(p => (p.X, p.Y))], closed: false);
                    }

                    break;

                case Solid solid:
                    Chain(
                        [
                            (solid.FirstCorner.X, solid.FirstCorner.Y), (solid.SecondCorner.X, solid.SecondCorner.Y),
                            (solid.FourthCorner.X, solid.FourthCorner.Y), (solid.ThirdCorner.X, solid.ThirdCorner.Y),
                        ],
                        closed: true);
                    break;

                // Öznitelik tanımı ve değeri değişken yazıdır; etiketi uygulama kendisi yazar.
                case AttributeDefinition:
                case AttributeEntity:
                    break;

                case TextEntity text when Upright(text.Rotation):
                    var align = text.HorizontalAlignment switch
                    {
                        TextHorizontalAlignment.Center or TextHorizontalAlignment.Middle => TextAlign.Center,
                        TextHorizontalAlignment.Right => TextAlign.Right,
                        _ => TextAlign.Left,
                    };
                    var at = align == TextAlign.Left ? text.InsertPoint : text.AlignmentPoint;
                    AddText(at.X, at.Y, text.Value, text.Height, align);
                    break;

                case MText mtext when Upright(mtext.Rotation):
                    AddMText(mtext);
                    break;

                case Point point:
                    Points.Add((point.Location.X, point.Location.Y));
                    break;

                case Insert insert:
                    foreach (var child in insert.Explode())
                    {
                        Visit(child, depth + 1);
                    }

                    break;

                case Dimension dimension when dimension.Block is { } block:
                    foreach (var child in block.Entities)
                    {
                        Visit(child, depth + 1);
                    }

                    break;
            }
        }

        /// <summary>Tamamen dikdörtgenin içinde kalmayan öğeleri atar (pencere seçimi).</summary>
        public void Crop((double X, double Y, double Width, double Height) window)
        {
            var (x0, y0, x1, y1) = (window.X, window.Y, window.X + window.Width, window.Y + window.Height);
            bool In(double x, double y) => x >= x0 && x <= x1 && y >= y0 && y <= y1;
            Segments.RemoveAll(segment => !In(segment.X1, segment.Y1) || !In(segment.X2, segment.Y2));
            Circles.RemoveAll(circle => !In(circle.X - circle.Radius, circle.Y - circle.Radius) || !In(circle.X + circle.Radius, circle.Y + circle.Radius));
            Texts.RemoveAll(text => !In(text.X, text.Y));
            Points.RemoveAll(point => !In(point.X, point.Y));
        }

        public (double MinX, double MinY, double MaxX, double MaxY) Bounds()
        {
            var xs = Segments.SelectMany(s => new[] { s.X1, s.X2 })
                .Concat(Circles.SelectMany(c => new[] { c.X - c.Radius, c.X + c.Radius }))
                .Concat(Texts.Select(t => t.X)).Concat(Points.Select(p => p.X)).ToList();
            var ys = Segments.SelectMany(s => new[] { s.Y1, s.Y2 })
                .Concat(Circles.SelectMany(c => new[] { c.Y - c.Radius, c.Y + c.Radius }))
                .Concat(Texts.Select(t => t.Y)).Concat(Points.Select(p => p.Y)).ToList();
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        private static bool Upright(double rotation) => Math.Abs(Math.Sin(rotation)) < 1e-6 && Math.Cos(rotation) > 0;

        private static double Coordinate(IVector vector, int index) =>
            vector switch
            {
                XY xy => index == 0 ? xy.X : xy.Y,
                XYZ xyz => index == 0 ? xyz.X : xyz.Y,
                _ => 0,
            };

        private static List<(double X, double Y)> Ocs(IEnumerable<XYZ> points, XYZ normal) =>
            [.. points.Select(p => normal.Z < 0 ? (-p.X, p.Y) : (p.X, p.Y))];

        private void Polyline(List<(double X, double Y, double Bulge)> vertices, bool closed, XYZ normal)
        {
            if (vertices.Count < 2)
            {
                return;
            }

            var count = closed ? vertices.Count : vertices.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var (a, b) = (vertices[i], vertices[(i + 1) % vertices.Count]);
                if (Math.Abs(a.Bulge) < 1e-9)
                {
                    Chain(Ocs([new XYZ(a.X, a.Y, 0), new XYZ(b.X, b.Y, 0)], normal), closed: false);
                    continue;
                }

                // Çıkıntı (bulge), yay açısının dörtte birinin tanjantıdır.
                var sweep = 4 * Math.Atan(Math.Abs(a.Bulge));
                var steps = Math.Max(2, (int)Math.Ceiling(CurveSegments * sweep / (2 * Math.PI)));
                var arc = Arc.CreateFromBulge(new XY(a.X, a.Y), new XY(b.X, b.Y), a.Bulge);
                Chain(Ocs(arc.PolygonalVertexes(steps + 1), normal), closed: false);
            }
        }

        private void Chain(List<(double X, double Y)> points, bool closed)
        {
            for (var i = 1; i < points.Count; i++)
            {
                Segments.Add((points[i - 1].X, points[i - 1].Y, points[i].X, points[i].Y));
            }

            if (closed && points.Count > 2)
            {
                Segments.Add((points[^1].X, points[^1].Y, points[0].X, points[0].Y));
            }
        }

        private void AddMText(MText mtext)
        {
            var lines = mtext.GetPlainTextLines().Where(line => line.Trim().Length > 0).ToList();
            var (h, step) = (mtext.Height, mtext.Height * 1.25 / CapHeightRatio);
            var align = mtext.AttachmentPoint switch
            {
                AttachmentPointType.TopCenter or AttachmentPointType.MiddleCenter or AttachmentPointType.BottomCenter => TextAlign.Center,
                AttachmentPointType.TopRight or AttachmentPointType.MiddleRight or AttachmentPointType.BottomRight => TextAlign.Right,
                _ => TextAlign.Left,
            };

            // İlk satırın taban çizgisi: üstten bağlıysa bir yazı boyu aşağıda.
            var height = h + ((lines.Count - 1) * step);
            var first = mtext.AttachmentPoint switch
            {
                AttachmentPointType.MiddleLeft or AttachmentPointType.MiddleCenter or AttachmentPointType.MiddleRight => mtext.InsertPoint.Y + (height / 2) - h,
                AttachmentPointType.BottomLeft or AttachmentPointType.BottomCenter or AttachmentPointType.BottomRight => mtext.InsertPoint.Y + height - h,
                _ => mtext.InsertPoint.Y - h,
            };

            for (var i = 0; i < lines.Count; i++)
            {
                AddText(mtext.InsertPoint.X, first - (i * step), lines[i], h, align);
            }
        }

        private void AddText(double x, double y, string value, double height, TextAlign align)
        {
            var text = value
                .Replace("%%c", "Ø", StringComparison.OrdinalIgnoreCase)
                .Replace("%%d", "°", StringComparison.OrdinalIgnoreCase)
                .Replace("%%p", "±", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (text.Length > 0 && height > 0)
            {
                Texts.Add((x, y, text, height, align));
            }
        }
    }
}
