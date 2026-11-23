using System.Globalization;
using System.Text.RegularExpressions;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Library;

/// <summary>Serbest grafikten tespit edilen bir bağlantı ucu (tel ucu).</summary>
public sealed record RecognizedPin(
    string Name,
    double SheetX,
    double SheetY,
    int DirectionX,
    int DirectionY,
    PointAttributes Attributes = default);

/// <summary>Serbest grafikten tespit edilen bir cihaz fonksiyonu, sembolü ve uçları.</summary>
public sealed record RecognizedDevice(
    string Key,
    PageId PageId,
    string Tag,
    FunctionKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    IReadOnlyList<RecognizedPin> Pins,
    SymbolDefinition Symbol,
    IReadOnlyList<GraphicId> ConsumedGraphics,
    PartRef? Part = null,
    string? Description = null,
    IReadOnlyDictionary<string, string>? Properties = null,
    bool FromBookmark = false);

/// <summary>Serbest grafikten tespit edilen iki cihaz ucu arasındaki tel bağlantısı.</summary>
public sealed record RecognizedWire(
    string Key,
    PageId PageId,
    string FromDeviceKey,
    string FromPinName,
    string ToDeviceKey,
    string ToPinName,
    IReadOnlyList<WirePoint> Route,
    string? WireNumber,
    string? WireColor,
    IReadOnlyList<GraphicId> ConsumedGraphics);

/// <summary>Tek bir sayfadan tanınan cihaz ve tel bağlantılarının sonucu.</summary>
public sealed record PageRecognitionResult(
    PageId PageId,
    IReadOnlyList<RecognizedDevice> Devices,
    IReadOnlyList<RecognizedWire> Wires);

/// <summary>Tanınan grafiklerin elektriksel modele dönüştürülme özeti.</summary>
public sealed record RecognitionConversionSummary(
    int DevicesAdded,
    int FunctionsAdded,
    int SymbolsAdded,
    int PlacementsAdded,
    int ConnectionsAdded,
    int GraphicsRemoved);

/// <summary>Tanınan cihazların sembol ve ürün kütüphanesine aktarılma özeti.</summary>
public sealed record RecognitionLibrarySummary(
    int SymbolsAdded,
    int SymbolsUpdated,
    int ProductsAdded,
    int ProductsUpdated);

/// <summary>
/// PDF veya DXF'ten içe aktarılmış serbest grafik (<see cref="PageGraphic"/>) sayfalarından cihazları,
/// bağlantı uçlarını (tel uçlarını), sembol geometrisini ve aralarındaki telleri tespit eder; kullanıcı
/// onayıyla elektriksel modele ve/veya <see cref="ComponentLibrary"/> kütüphanesine aktarır.
/// </summary>
public static partial class GraphicRecognition
{
    private const double PinStubToleranceMm = 2.6;
    private const double PinMergeDistanceMm = 1.4;
    private const double WireJoinToleranceMm = 0.85;
    private const double MinSegmentMm = 0.05;

    [GeneratedRegex(@"^(?:\d{1,3})?[A-Z]{1,5}\d+(?:[./-][A-Z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeuristicTagRegex();

    [GeneratedRegex(@"^(?=.*[A-Z])(?=.*\d)[A-Z0-9][A-Z0-9./-]{3,}$", RegexOptions.CultureInvariant)]
    private static partial Regex OrderCodeRegex();

    [GeneratedRegex(@"^\d+(\.\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericOrCrossRefRegex();

    [GeneratedRegex(@"^(?:\d+[.,]?\d*\s*(?:kW|A|V|VAC|VDC|Hz|W)|C\d+A|K\d+)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ElectricalValueRegex();

    [GeneratedRegex(@"^(?:CN\d+(?:/[A-Z0-9.]+)?|[UVW]\d?|BR[+-]|PE|GND|COM|0V\d*|24V\d*|L[123]?|N|A[12]|T[+-]|TX[+-]|RX[+-]|DI\d+|D[OQ]\d+|AI\d+|AO\d+|R[12][ABC]|P\d+|X\d+(?:/\d+)?|COS|SIN|REFCOS|REFSIN|P10V|ENCODER|STO|CAN[12]?|ETH\d?|USB\d?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PinNamePatternRegex();

    private static readonly (string Prefix, string Manufacturer)[] KnownManufacturers =
    [
        ("LXM", "Schneider Electric"),
        ("BMH", "Schneider Electric"),
        ("ATV", "Schneider Electric"),
        ("LMC", "Schneider Electric"),
        ("A9C", "Schneider Electric"),
        ("A9R", "Schneider Electric"),
        ("RM35", "Schneider Electric"),
        ("VCF", "Schneider Electric"),
        ("PLC-RPT", "Phoenix Contact"),
        ("QUINT", "Phoenix Contact"),
        ("STEP", "Phoenix Contact"),
        ("EL", "Beckhoff"),
        ("EK", "Beckhoff"),
        ("CP", "Beckhoff"),
        ("6ES7", "Siemens"),
        ("3RT", "Siemens"),
        ("3RV", "Siemens"),
    ];

    private static readonly string[] ManufacturerKeywords =
    [
        "SCHNEIDER", "OMRON", "PHOENIX", "SIEMENS", "ABB", "FESTO", "EATON", "WEIDMULLER", "BECKHOFF", "SMC", "ALLEN-BRADLEY",
    ];

    /// <summary>Projedeki grafik içeren tüm şema sayfalarında tanıma çalıştırır.</summary>
    public static IReadOnlyList<PageRecognitionResult> RecognizeProject(
        Project project,
        IReadOnlyDictionary<PageId, IReadOnlyList<PdfBookmarkComponent>>? bookmarkHints = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var results = new List<PageRecognitionResult>();
        var byPage = project.Graphics.GroupBy(g => g.PageId).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var page in project.Pages)
        {
            if (page.Kind != PageKind.Schematic || !byPage.TryGetValue(page.Id, out var pageGraphics) || pageGraphics.Count == 0)
            {
                continue;
            }

            var hints = bookmarkHints?.GetValueOrDefault(page.Id);
            var recognized = RecognizePage(project, page, hints, pageGraphics);
            if (recognized.Devices.Count > 0 || recognized.Wires.Count > 0)
            {
                results.Add(recognized);
            }
        }

        return results;
    }

    /// <summary>Tek bir sayfadaki serbest grafiklerden cihazları, tel uçlarını ve bağlantıları tanır.</summary>
    public static PageRecognitionResult RecognizePage(
        Project project,
        Page page,
        IReadOnlyList<PdfBookmarkComponent>? bookmarkHints = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(page);

        var graphics = project.Graphics.Where(g => g.PageId == page.Id).ToList();
        return RecognizePage(project, page, bookmarkHints, graphics);
    }

    private static PageRecognitionResult RecognizePage(
        Project project,
        Page page,
        IReadOnlyList<PdfBookmarkComponent>? bookmarkHints,
        List<PageGraphic> pageGraphics)
    {
        if (pageGraphics.Count == 0)
        {
            return new PageRecognitionResult(page.Id, [], []);
        }

        var ctx = BuildPageContext(pageGraphics);
        var devices = DetectDevices(page.Id, ctx, bookmarkHints);
        var consumedByDevices = devices.SelectMany(d => d.ConsumedGraphics).ToHashSet();
        var wires = DetectWires(page.Id, ctx, devices, consumedByDevices);

        return new PageRecognitionResult(page.Id, devices, wires);
    }

    /// <summary>
    /// Onaylanan cihazları ve telleri projeye ekler; tek adımda geri alınabilir <see cref="Transaction"/> üretir.
    /// Aynı cihaz tag'ine sahip birden çok fonksiyon aynı <see cref="Device"/> altında toplanır.
    /// </summary>
    public static Result<(Transaction Transaction, RecognitionConversionSummary Summary)> PlanConversion(
        Project project,
        IReadOnlyList<RecognizedDevice> devices,
        IReadOnlyList<RecognizedWire> wires,
        Func<Page, (double X, double Y)> drawingAreaOriginFor,
        bool removeConsumedGraphics = true)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(wires);
        ArgumentNullException.ThrowIfNull(drawingAreaOriginFor);

        if (devices.Count == 0 && wires.Count == 0)
        {
            return Result.Failure<(Transaction, RecognitionConversionSummary)>(
                new Error("tani.secim_yok", "Dönüştürülecek cihaz veya bağlantı seçilmedi."));
        }

        var pageById = project.Pages.ToDictionary(p => p.Id);
        var commands = new List<ICommand>();

        var symbolCount = 0;
        var symbolsByKey = new Dictionary<string, SymbolDefinition>(StringComparer.Ordinal);
        var assignedSymbolIds = new Dictionary<string, SymbolId>(StringComparer.Ordinal);

        foreach (var dev in devices)
        {
            var sig = SymbolSignature(dev.Symbol);
            if (!symbolsByKey.TryGetValue(sig, out var storedSymbol))
            {
                var check = dev.Symbol.Check();
                if (check is not null)
                {
                    return Result.Failure<(Transaction, RecognitionConversionSummary)>(check);
                }

                storedSymbol = dev.Symbol with
                {
                    Source = string.IsNullOrWhiteSpace(dev.Symbol.Source) ? "Grafikten tanındı" : dev.Symbol.Source,
                    License = string.IsNullOrWhiteSpace(dev.Symbol.License) ? "Kullanıcı çizimi" : dev.Symbol.License,
                };
                symbolsByKey[sig] = storedSymbol;
                commands.Add(new PutSymbol(storedSymbol));
                symbolCount++;
            }

            assignedSymbolIds[dev.Key] = storedSymbol.Id;
        }

        var existingDevices = project.Devices
            .GroupBy(d => d.ProductTag, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var pinPointMap = new Dictionary<(string DeviceKey, string PinName), ConnectionPointId>();
        var deviceCount = 0;
        var functionCount = 0;
        var placementCount = 0;

        foreach (var group in devices.GroupBy(d => d.Tag, StringComparer.OrdinalIgnoreCase))
        {
            var representative = group.FirstOrDefault(d => d.Part is not null)
                ?? group.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Description))
                ?? group.First();

            var builtFunctions = new List<(RecognizedDevice Source, Function Function)>();
            foreach (var item in group)
            {
                var points = new List<ConnectionPoint>(item.Pins.Count);
                foreach (var pin in item.Pins)
                {
                    var cp = new ConnectionPoint(ConnectionPointId.New(), pin.Name, pin.Attributes);
                    points.Add(cp);
                    pinPointMap[(item.Key, pin.Name)] = cp.Id;
                }

                var fn = new Function(
                    FunctionId.New(),
                    item.Kind,
                    points,
                    passThrough: item.Kind == FunctionKind.Terminal,
                    description: item.Description,
                    symbolId: assignedSymbolIds[item.Key]);
                builtFunctions.Add((item, fn));
            }

            if (existingDevices.TryGetValue(representative.Tag, out var existing))
            {
                foreach (var (_, fn) in builtFunctions)
                {
                    commands.Add(new AddFunction(existing.Id, fn));
                    functionCount++;
                }
            }
            else
            {
                var mergedProps = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var item in group)
                {
                    if (item.Properties is not null)
                    {
                        foreach (var (k, v) in item.Properties)
                        {
                            mergedProps.TryAdd(k, v);
                        }
                    }
                }

                var device = new Device(
                    DeviceId.New(),
                    representative.Tag,
                    builtFunctions.Select(f => f.Function),
                    representative.Kind == FunctionKind.Terminal ? DeviceKind.TerminalStrip : DeviceKind.Generic,
                    description: representative.Description,
                    part: representative.Part,
                    properties: mergedProps.Count > 0 ? mergedProps : null);
                commands.Add(new AddDevice(device));
                existingDevices[representative.Tag] = device;
                deviceCount++;
                functionCount += builtFunctions.Count;
            }

            foreach (var (item, fn) in builtFunctions)
            {
                if (!pageById.TryGetValue(item.PageId, out var page) || item.Pins.Count == 0)
                {
                    continue;
                }

                var origin = drawingAreaOriginFor(page);
                var pin0 = item.Pins[0];
                commands.Add(new AddPlacement(new Placement(
                    PlacementId.New(),
                    page.Id,
                    fn.Id,
                    PlacementRole.Main,
                    Math.Round(pin0.SheetX - origin.X, 3),
                    Math.Round(pin0.SheetY - origin.Y, 3))));
                placementCount++;
            }
        }

        var connectedPairs = new HashSet<(ConnectionPointId A, ConnectionPointId B)>();
        foreach (var existingConn in project.Connections)
        {
            connectedPairs.Add(OrderedPair(existingConn.From, existingConn.To));
        }

        var connectionCount = 0;
        foreach (var wire in wires)
        {
            if (!pinPointMap.TryGetValue((wire.FromDeviceKey, wire.FromPinName), out var fromId)
                || !pinPointMap.TryGetValue((wire.ToDeviceKey, wire.ToPinName), out var toId)
                || fromId == toId)
            {
                continue;
            }

            var pair = OrderedPair(fromId, toId);
            if (!connectedPairs.Add(pair))
            {
                continue;
            }

            var route = wire.Route.Count > Project.MaxRoutePoints ? [.. wire.Route.Take(Project.MaxRoutePoints)] : wire.Route;
            var physicalWire = new Wire(WireId.New(), wire.WireNumber, null, wire.WireColor);
            commands.Add(new Connect(new Connection(ConnectionId.New(), fromId, toId, physicalWire, null, route)));
            connectionCount++;
        }

        var graphicsRemoved = 0;
        if (removeConsumedGraphics)
        {
            var projectGraphicIds = project.Graphics.Select(g => g.Id).ToHashSet();
            var toRemove = devices.SelectMany(d => d.ConsumedGraphics)
                .Concat(wires.Where(w =>
                    pinPointMap.ContainsKey((w.FromDeviceKey, w.FromPinName))
                    && pinPointMap.ContainsKey((w.ToDeviceKey, w.ToPinName)))
                    .SelectMany(w => w.ConsumedGraphics))
                .Where(projectGraphicIds.Contains)
                .Distinct()
                .ToList();
            if (toRemove.Count > 0)
            {
                commands.Add(new RemoveGraphics(toRemove));
                graphicsRemoved = toRemove.Count;
            }
        }

        if (commands.Count == 0)
        {
            return Result.Failure<(Transaction, RecognitionConversionSummary)>(
                new Error("tani.degisiklik_yok", "Uygulanabilir cihaz veya bağlantı değişikliği bulunamadı."));
        }

        var summary = new RecognitionConversionSummary(
            deviceCount, functionCount, symbolCount, placementCount, connectionCount, graphicsRemoved);
        var description = $"Grafikten modele dönüştürüldü: {deviceCount} cihaz, {connectionCount} bağlantı";
        return Result.Success((new Transaction(description, commands), summary));
    }

    /// <summary>
    /// Tespit edilen cihazları sembol imzasına göre tekilleştirip açık <see cref="ComponentLibrary"/> kütüphanesine
    /// hem kullanıcı sembolü hem (sipariş kodu/parça bilgisi varsa) ürün kaydı olarak aktarır.
    /// </summary>
    public static Result<RecognitionLibrarySummary> ExportToLibrary(
        ComponentLibrary library,
        IReadOnlyList<RecognizedDevice> devices,
        string symbolCategory,
        string source,
        string license)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(symbolCategory);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(license);

        if (devices.Count == 0)
        {
            return Result.Failure<RecognitionLibrarySummary>(
                new Error("tani.secim_yok", "Kütüphaneye aktarılacak cihaz seçilmedi."));
        }

        var effectiveSource = string.IsNullOrWhiteSpace(source) ? "Grafikten aktarıldı" : source.Trim();
        var effectiveLicense = string.IsNullOrWhiteSpace(license) ? "Kullanıcı çizimi" : license.Trim();

        var librarySymbols = new List<LibrarySymbol>();
        var seenSymbols = new HashSet<string>(StringComparer.Ordinal);
        var symbolIdBySignature = new Dictionary<string, SymbolId>(StringComparer.Ordinal);
        foreach (var dev in devices)
        {
            var sig = SymbolSignature(dev.Symbol);
            if (!seenSymbols.Add(sig))
            {
                continue;
            }

            var displayName = BuildLibrarySymbolName(dev);
            var stored = dev.Symbol with
            {
                Name = displayName,
                Source = effectiveSource,
                License = effectiveLicense,
                DeviceName = dev.Description ?? dev.Part?.OrderCode ?? dev.Tag,
                TagLetter = ExtractTagLetter(dev.Tag),
            };
            if (stored.Check() is null)
            {
                librarySymbols.Add(new LibrarySymbol(stored, symbolCategory));
                symbolIdBySignature[sig] = stored.Id;
            }
        }

        var symResult = librarySymbols.Count > 0
            ? library.ImportSymbols(librarySymbols)
            : Result.Success(new ImportSummary(0, 0));
        if (symResult.IsFailure)
        {
            return Result.Failure<RecognitionLibrarySummary>(symResult.Error!);
        }

        var products = new List<LibraryProduct>();
        var seenProducts = new HashSet<(string Manufacturer, string OrderCode)>();
        foreach (var tagGroup in devices.GroupBy(d => d.Tag, StringComparer.OrdinalIgnoreCase))
        {
            var representative = tagGroup.FirstOrDefault(d => d.Part is not null)
                ?? tagGroup.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Description))
                ?? tagGroup.First();
            var mfg = string.IsNullOrWhiteSpace(representative.Part?.Manufacturer)
                ? "Belirtilmedi"
                : representative.Part.Manufacturer.Trim();
            var code = string.IsNullOrWhiteSpace(representative.Part?.OrderCode)
                ? representative.Tag.Trim()
                : representative.Part.OrderCode.Trim();
            if (!seenProducts.Add((mfg.ToUpperInvariant(), code.ToUpperInvariant())))
            {
                continue;
            }

            var fnTemplates = tagGroup
                .Where(d => d.Pins.Count > 0)
                .Select(d =>
                {
                    var linkedId = symbolIdBySignature.TryGetValue(SymbolSignature(d.Symbol), out var foundId)
                        ? foundId
                        : (SymbolId?)null;
                    return new FunctionTemplate(
                        d.Kind,
                        [.. d.Pins.Select(p => p.Name)],
                        PassThrough: d.Kind == FunctionKind.Terminal,
                        Description: d.Description,
                        SymbolId: linkedId);
                })
                .ToList();

            if (fnTemplates.Count == 0)
            {
                continue;
            }

            var props = representative.Properties?
                .Select(kv => new ProductProperty(kv.Key, kv.Value))
                .ToList();
            var category = CategoryFor(representative);
            var definition = new ProductDefinition(
                mfg,
                code,
                fnTemplates,
                representative.Part?.TypeDesignation,
                representative.Description ?? $"{code} ({representative.Pins.Count} uç)");
            products.Add(new LibraryProduct(definition, effectiveSource, effectiveLicense, category, props));
        }

        var prodResult = products.Count > 0
            ? library.Import(products)
            : Result.Success(new ImportSummary(0, 0));
        if (prodResult.IsFailure)
        {
            return Result.Failure<RecognitionLibrarySummary>(prodResult.Error!);
        }

        return Result.Success(new RecognitionLibrarySummary(
            symResult.Value.Added,
            symResult.Value.Updated,
            prodResult.Value.Added,
            prodResult.Value.Updated));
    }

    private sealed record CircleCandidate(
        GraphicId GraphicId,
        double CenterX,
        double CenterY,
        double Radius,
        double StrokeMm,
        string? Color,
        GraphicPolyline? Polyline);

    private sealed record Seg(
        GraphicId? SourceId,
        double X1,
        double Y1,
        double X2,
        double Y2,
        double StrokeMm,
        string? Color,
        bool IsVirtualDash = false)
    {
        public bool Horizontal => Math.Abs(Y1 - Y2) < 0.25 && Math.Abs(X1 - X2) >= MinSegmentMm;
        public bool Vertical => Math.Abs(X1 - X2) < 0.25 && Math.Abs(Y1 - Y2) >= MinSegmentMm;
        public double MinX => Math.Min(X1, X2);
        public double MaxX => Math.Max(X1, X2);
        public double MinY => Math.Min(Y1, Y2);
        public double MaxY => Math.Max(Y1, Y2);
        public double Length => Math.Sqrt(((X2 - X1) * (X2 - X1)) + ((Y2 - Y1) * (Y2 - Y1)));
    }

    private sealed record RectCandidate(
        double X,
        double Y,
        double Width,
        double Height,
        IReadOnlyList<GraphicId> EdgeIds)
    {
        public double Right => X + Width;
        public double Bottom => Y + Height;
    }

    private sealed class TextEntry(GraphicId id, GraphicText text)
    {
        public GraphicId Id { get; } = id;
        public GraphicText Text { get; } = text;
        public bool Claimed { get; set; }
    }

    private sealed class PageContext(
        List<PageGraphic> all,
        List<(GraphicId Id, GraphicPolyline Poly)> polylines,
        List<CircleCandidate> circles,
        List<TextEntry> texts,
        List<Seg> axisSegments,
        List<(Seg Seg, IReadOnlyList<GraphicId> Ids)> virtualDashed,
        List<RectCandidate> rectangles)
    {
        public List<PageGraphic> All { get; } = all;
        public List<(GraphicId Id, GraphicPolyline Poly)> Polylines { get; } = polylines;
        public List<CircleCandidate> Circles { get; } = circles;
        public List<TextEntry> Texts { get; } = texts;
        public List<Seg> AxisSegments { get; } = axisSegments;
        public List<(Seg Seg, IReadOnlyList<GraphicId> Ids)> VirtualDashed { get; } = virtualDashed;
        public List<RectCandidate> Rectangles { get; } = rectangles;
    }

    private static PageContext BuildPageContext(List<PageGraphic> graphics)
    {
        var polylines = new List<(GraphicId, GraphicPolyline)>();
        var circles = new List<CircleCandidate>();
        var texts = new List<TextEntry>();
        var allAxisSegs = new List<Seg>();
        var dashShort = new List<Seg>();

        foreach (var g in graphics)
        {
            switch (g.Shape)
            {
                case GraphicText t:
                    texts.Add(new TextEntry(g.Id, t));
                    break;
                case GraphicCircle c:
                    circles.Add(new CircleCandidate(g.Id, c.X, c.Y, c.Radius, c.StrokeMm, c.Color, null));
                    break;
                case GraphicPolyline p:
                    polylines.Add((g.Id, p));
                    if (TryAsCircle(p, out var cx, out var cy, out var r))
                    {
                        circles.Add(new CircleCandidate(g.Id, cx, cy, r, p.StrokeMm, p.Color, p));
                    }

                    if (!p.Filled)
                    {
                        var count = p.Points.Count / 2;
                        for (var i = 1; i < count; i++)
                        {
                            AddSeg(g.Id, p.Points[(i - 1) * 2], p.Points[((i - 1) * 2) + 1], p.Points[i * 2], p.Points[(i * 2) + 1], p.StrokeMm, p.Color);
                        }

                        if (p.Closed && count >= 3)
                        {
                            AddSeg(g.Id, p.Points[(count - 1) * 2], p.Points[((count - 1) * 2) + 1], p.Points[0], p.Points[1], p.StrokeMm, p.Color);
                        }
                    }

                    break;
            }
        }

        void AddSeg(GraphicId id, double x1, double y1, double x2, double y2, double stroke, string? color)
        {
            var s = new Seg(id, x1, y1, x2, y2, stroke, color);
            if (!s.Horizontal && !s.Vertical)
            {
                return;
            }

            if (color is not null && s.Length <= 4.0)
            {
                dashShort.Add(s);
            }
            else
            {
                allAxisSegs.Add(s);
            }
        }

        var virtualDashed = MergeColoredDashedSegments(dashShort);
        var rectangles = DetectRectangles(polylines, allAxisSegs);
        return new PageContext(graphics, polylines, circles, texts, allAxisSegs, virtualDashed, rectangles);
    }

    private static bool TryAsCircle(GraphicPolyline poly, out double cx, out double cy, out double radius)
    {
        cx = cy = radius = 0;
        var count = poly.Points.Count / 2;
        if (!poly.Closed || count < 12)
        {
            return false;
        }

        double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
        for (var i = 0; i < count; i++)
        {
            var x = poly.Points[i * 2];
            var y = poly.Points[(i * 2) + 1];
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        var w = maxX - minX;
        var h = maxY - minY;
        if (w < 0.6 || h < 0.6 || Math.Abs(w - h) > Math.Max(w, h) * 0.2)
        {
            return false;
        }

        cx = (minX + maxX) / 2;
        cy = (minY + maxY) / 2;
        radius = (w + h) / 4;
        for (var i = 0; i < count; i++)
        {
            var dx = poly.Points[i * 2] - cx;
            var dy = poly.Points[(i * 2) + 1] - cy;
            var dist = Math.Sqrt((dx * dx) + (dy * dy));
            if (Math.Abs(dist - radius) > radius * 0.22 + 0.15)
            {
                return false;
            }
        }

        return true;
    }

    private static List<(Seg Seg, IReadOnlyList<GraphicId> Ids)> MergeColoredDashedSegments(List<Seg> dashes)
    {
        var result = new List<(Seg Seg, IReadOnlyList<GraphicId> Ids)>();
        if (dashes.Count == 0)
        {
            return result;
        }

        foreach (var colorGroup in dashes.GroupBy(d => d.Color ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var mergedHoriz = new List<(Seg Seg, IReadOnlyList<GraphicId> Ids)>();
            var mergedVert = new List<(Seg Seg, IReadOnlyList<GraphicId> Ids)>();

            var horiz = colorGroup.Where(s => s.Horizontal).ToList();
            foreach (var band in ClusterByValue(horiz, s => (s.Y1 + s.Y2) / 2, 0.35))
            {
                var ordered = band.OrderBy(s => s.MinX).ToList();
                var start = ordered[0].MinX;
                var end = ordered[0].MaxX;
                var y = (ordered[0].Y1 + ordered[0].Y2) / 2;
                var ids = new List<GraphicId>();
                if (ordered[0].SourceId is { } id0)
                {
                    ids.Add(id0);
                }

                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].MinX <= end + 4.2)
                    {
                        end = Math.Max(end, ordered[i].MaxX);
                        if (ordered[i].SourceId is { } id)
                        {
                            ids.Add(id);
                        }
                    }
                    else
                    {
                        if (end - start >= 3.0)
                        {
                            mergedHoriz.Add((new Seg(null, start, y, end, y, ordered[0].StrokeMm, ordered[0].Color, true), [.. ids.Distinct()]));
                        }

                        start = ordered[i].MinX;
                        end = ordered[i].MaxX;
                        ids = ordered[i].SourceId is { } idNew ? [idNew] : [];
                    }
                }

                if (end - start >= 3.0)
                {
                    mergedHoriz.Add((new Seg(null, start, y, end, y, ordered[0].StrokeMm, ordered[0].Color, true), [.. ids.Distinct()]));
                }
            }

            var vert = colorGroup.Where(s => s.Vertical).ToList();
            foreach (var band in ClusterByValue(vert, s => (s.X1 + s.X2) / 2, 0.35))
            {
                var ordered = band.OrderBy(s => s.MinY).ToList();
                var start = ordered[0].MinY;
                var end = ordered[0].MaxY;
                var x = (ordered[0].X1 + ordered[0].X2) / 2;
                var ids = new List<GraphicId>();
                if (ordered[0].SourceId is { } id0)
                {
                    ids.Add(id0);
                }

                for (var i = 1; i < ordered.Count; i++)
                {
                    if (ordered[i].MinY <= end + 4.2)
                    {
                        end = Math.Max(end, ordered[i].MaxY);
                        if (ordered[i].SourceId is { } id)
                        {
                            ids.Add(id);
                        }
                    }
                    else
                    {
                        if (end - start >= 3.0)
                        {
                            mergedVert.Add((new Seg(null, x, start, x, end, ordered[0].StrokeMm, ordered[0].Color, true), [.. ids.Distinct()]));
                        }

                        start = ordered[i].MinY;
                        end = ordered[i].MaxY;
                        ids = ordered[i].SourceId is { } idNew ? [idNew] : [];
                    }
                }

                if (end - start >= 3.0)
                {
                    mergedVert.Add((new Seg(null, x, start, x, end, ordered[0].StrokeMm, ordered[0].Color, true), [.. ids.Distinct()]));
                }
            }

            // Köşe dönüşlerindeki 1-2 mm kesik boşluklarını aynı renkteki dik kolları uzatarak birleştir
            const double cornerGapMm = 2.8;
            for (var hi = 0; hi < mergedHoriz.Count; hi++)
            {
                for (var vj = 0; vj < mergedVert.Count; vj++)
                {
                    var h = mergedHoriz[hi].Seg;
                    var v = mergedVert[vj].Seg;
                    var vx = v.X1;
                    var hy = h.Y1;
                    var hGap = vx < h.X1 ? h.X1 - vx : vx > h.X2 ? vx - h.X2 : 0.0;
                    var vGap = hy < v.Y1 ? v.Y1 - hy : hy > v.Y2 ? hy - v.Y2 : 0.0;
                    if (hGap <= cornerGapMm && vGap <= cornerGapMm)
                    {
                        mergedHoriz[hi] = (h with { X1 = Math.Min(h.X1, vx), X2 = Math.Max(h.X2, vx) }, mergedHoriz[hi].Ids);
                        mergedVert[vj] = (v with { Y1 = Math.Min(v.Y1, hy), Y2 = Math.Max(v.Y2, hy) }, mergedVert[vj].Ids);
                    }
                }
            }

            result.AddRange(mergedHoriz);
            result.AddRange(mergedVert);
        }

        return result;
    }

    private static List<RectCandidate> DetectRectangles(
        List<(GraphicId Id, GraphicPolyline Poly)> polylines,
        List<Seg> axisSegments)
    {
        var rects = new List<RectCandidate>();

        foreach (var (id, p) in polylines)
        {
            if (p.Filled || p.Points.Count is < 8 or > 10)
            {
                continue;
            }

            var count = p.Points.Count / 2;
            var isRectLoop = p.Closed || (Math.Abs(p.Points[0] - p.Points[^2]) < 0.2 && Math.Abs(p.Points[1] - p.Points[^1]) < 0.2);
            if (!isRectLoop || count < 4)
            {
                continue;
            }

            var (bx, by, bw, bh) = p.Bounds;
            if (bw is >= 8 and <= 385 && bh is >= 8 and <= 230)
            {
                rects.Add(new RectCandidate(bx, by, bw, bh, [id]));
            }
        }

        var horizontals = axisSegments.Where(s => s.Horizontal && s.Length >= 8).ToList();
        var verticals = axisSegments.Where(s => s.Vertical && s.Length >= 8).ToList();
        const double tol = 1.2;

        for (var i = 0; i < horizontals.Count; i++)
        {
            var top = horizontals[i];
            for (var j = i + 1; j < horizontals.Count; j++)
            {
                var bottom = horizontals[j];
                var (t, b) = top.Y1 <= bottom.Y1 ? (top, bottom) : (bottom, top);
                var h = b.Y1 - t.Y1;
                if (h is < 8 or > 230)
                {
                    continue;
                }

                var leftX = Math.Max(t.MinX, b.MinX);
                var rightX = Math.Min(t.MaxX, b.MaxX);
                if (rightX - leftX is < 8 or > 385)
                {
                    continue;
                }

                var leftSeg = verticals.FirstOrDefault(v =>
                    Math.Abs(v.X1 - leftX) <= tol && v.MinY <= t.Y1 + tol && v.MaxY >= b.Y1 - tol);
                var rightSeg = verticals.FirstOrDefault(v =>
                    Math.Abs(v.X1 - rightX) <= tol && v.MinY <= t.Y1 + tol && v.MaxY >= b.Y1 - tol);
                if (leftSeg is null)
                {
                    continue;
                }

                // Bazı sürücü şablonlarında (ör. LXM62 sağ kenarı testere kesikli) sağ kenar açık ya da 10 mm içeride olabilir.
                if (rightSeg is null)
                {
                    rightSeg = verticals.FirstOrDefault(v =>
                        v.X1 <= rightX + 0.5 && v.X1 >= rightX - 15.0 && v.MinY <= t.Y1 + tol && v.MaxY >= b.Y1 - tol);
                    if (rightSeg is null)
                    {
                        continue;
                    }
                }

                var ids = new[] { t.SourceId, b.SourceId, leftSeg.SourceId, rightSeg.SourceId }
                    .OfType<GraphicId>()
                    .Distinct()
                    .ToList();
                var candidate = new RectCandidate(leftX, t.Y1, rightX - leftX, h, ids);
                if (!rects.Any(existing =>
                    Math.Abs(existing.X - candidate.X) < 1.5
                    && Math.Abs(existing.Y - candidate.Y) < 1.5
                    && Math.Abs(existing.Width - candidate.Width) < 12.0
                    && Math.Abs(existing.Height - candidate.Height) < 1.5))
                {
                    rects.Add(candidate);
                }
            }
        }

        // Aynı gövde için daha büyük dış dikdörtgenler önceliklidir.
        return [.. rects.OrderByDescending(r => r.Width * r.Height)];
    }

    private static List<RecognizedDevice> DetectDevices(
        PageId pageId,
        PageContext ctx,
        IReadOnlyList<PdfBookmarkComponent>? bookmarkHints)
    {
        var devices = new List<RecognizedDevice>();
        var claimedBodies = new List<RectCandidate>();
        var seq = 0;

        if (bookmarkHints is { Count: > 0 })
        {
            foreach (var hint in bookmarkHints)
            {
                if (!TryResolveBookmarkDevice(pageId, ctx, hint, claimedBodies, ++seq, out var dev))
                {
                    continue;
                }

                devices.Add(dev);
                claimedBodies.Add(new RectCandidate(dev.X, dev.Y, dev.Width, dev.Height, []));
            }
        }

        foreach (var entry in ctx.Texts)
        {
            if (entry.Claimed)
            {
                continue;
            }

            if (!LooksLikeHeuristicTag(entry.Text.Text, ctx, entry))
            {
                continue;
            }

            if (!TryBuildDeviceFromAnchor(pageId, ctx, entry, null, false, claimedBodies, ++seq, out var dev))
            {
                continue;
            }

            devices.Add(dev);
            claimedBodies.Add(new RectCandidate(dev.X, dev.Y, dev.Width, dev.Height, []));
        }

        return devices;
    }

    private static bool TryResolveBookmarkDevice(
        PageId pageId,
        PageContext ctx,
        PdfBookmarkComponent hint,
        List<RectCandidate> claimedBodies,
        int seq,
        out RecognizedDevice device)
    {
        device = null!;
        var (normalizedTag, _) = NormalizeTag(hint.Tag);
        if (!ReferenceDesignation.IsValidProductTag(normalizedTag))
        {
            return false;
        }

        var rawNoDash = hint.Tag.Trim().TrimStart('-');
        var afterDash = rawNoDash.Contains('-', StringComparison.Ordinal)
            ? rawNoDash[(rawNoDash.IndexOf('-', StringComparison.Ordinal) + 1)..]
            : rawNoDash;

        var candidates = ctx.Texts
            .Where(t => !t.Claimed && MatchesTagText(t.Text.Text, rawNoDash, normalizedTag, afterDash))
            .ToList();
        if (candidates.Count == 0)
        {
            return false;
        }

        TextEntry bestAnchor;
        if (candidates.Count == 1 || hint.PinNames.Count == 0)
        {
            bestAnchor = candidates[0];
        }
        else
        {
            var expectedPins = hint.PinNames.Select(CleanPinName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            bestAnchor = candidates
                .OrderByDescending(c =>
                {
                    var rightMatches = ctx.Texts.Count(t =>
                        t != c
                        && !t.Claimed
                        && t.Text.X >= c.Text.X - 5
                        && t.Text.X <= c.Text.X + 40
                        && Math.Abs(t.Text.Y - c.Text.Y) <= 45
                        && expectedPins.Contains(CleanPinName(t.Text.Text)));
                    var anyMatches = ctx.Texts.Count(t =>
                        t != c
                        && !t.Claimed
                        && Math.Abs(t.Text.X - c.Text.X) <= 60
                        && Math.Abs(t.Text.Y - c.Text.Y) <= 45
                        && expectedPins.Contains(CleanPinName(t.Text.Text)));
                    return (rightMatches * 10) + anyMatches;
                })
                .First();
        }

        return TryBuildDeviceFromAnchor(pageId, ctx, bestAnchor, hint, true, claimedBodies, seq, out device);
    }

    private static bool MatchesTagText(string text, string rawNoDash, string normalizedTag, string afterDash)
    {
        var clean = text.Trim().TrimStart('-');
        if (clean.Equals(rawNoDash, StringComparison.OrdinalIgnoreCase)
            || clean.Equals(normalizedTag, StringComparison.OrdinalIgnoreCase)
            || clean.Equals(afterDash, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // "3G1  OMRON" gibi tag + üretici bitişik satırlar
        var space = clean.IndexOf(' ', StringComparison.Ordinal);
        return space > 0 && clean[..space].Equals(normalizedTag, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryBuildDeviceFromAnchor(
        PageId pageId,
        PageContext ctx,
        TextEntry anchor,
        PdfBookmarkComponent? hint,
        bool fromBookmark,
        List<RectCandidate> claimedBodies,
        int seq,
        out RecognizedDevice device)
    {
        device = null!;
        var (tag, inlineMfg) = NormalizeTag(hint?.Tag ?? anchor.Text.Text);
        if (!ReferenceDesignation.IsValidProductTag(tag))
        {
            return false;
        }

        // Büyük / orta dikdörtgen gövde ya da motor çemberi bul
        var body = FindBodyForAnchor(ctx, anchor, hint, claimedBodies, out var motorCircle);
        if (body is null)
        {
            return false;
        }

        // Aynı dikdörtgen gövde başka bir cihaz tarafından zaten sahiplenildiyse atla (büyük kutular için)
        if (body.Width >= 25 && body.Height >= 20 && claimedBodies.Any(c => OverlapRatio(c, body) > 0.75))
        {
            return false;
        }

        anchor.Claimed = true;
        var metadata = ExtractMetadata(ctx, anchor, body, inlineMfg);
        var pins = ExtractPins(ctx, body, motorCircle, hint?.PinNames, out var pinTextIds);
        if (pins.Count == 0)
        {
            anchor.Claimed = false;
            return false;
        }

        var kind = InferFunctionKind(tag, pins, motorCircle is not null);
        var (symbol, bodyGraphicIds) = BuildSymbolDefinition(
            ctx, body, motorCircle, pins, tag, anchor.Id, pinTextIds, metadata.ConsumedIds);

        if (symbol.Check() is not null)
        {
            anchor.Claimed = false;
            return false;
        }

        foreach (var id in pinTextIds.Concat(metadata.ConsumedIds))
        {
            if (ctx.Texts.FirstOrDefault(t => t.Id == id) is { } txt)
            {
                txt.Claimed = true;
            }
        }

        var consumed = bodyGraphicIds
            .Append(anchor.Id)
            .Concat(pinTextIds)
            .Concat(metadata.ConsumedIds)
            .Distinct()
            .ToList();

        var key = $"P{pageId.Value:N}:{tag}#{seq}";
        device = new RecognizedDevice(
            key,
            pageId,
            tag,
            kind,
            body.X,
            body.Y,
            body.Width,
            body.Height,
            pins,
            symbol,
            consumed,
            metadata.Part,
            metadata.Description,
            metadata.Properties,
            fromBookmark);
        return true;
    }

    private static RectCandidate? FindBodyForAnchor(
        PageContext ctx,
        TextEntry anchor,
        PdfBookmarkComponent? hint,
        List<RectCandidate> claimedBodies,
        out CircleCandidate? motorCircle)
    {
        var ax = anchor.Text.X;
        var ay = anchor.Text.Y;

        // 1. Etiketin içinde veya hemen sağında/etrafında olan dikdörtgen gövde
        var rect = ctx.Rectangles
            .Where(r =>
                !claimedBodies.Any(c => OverlapRatio(c, r) > 0.75)
                && ax >= r.X - 35 && ax <= r.Right + 25
                && ay >= r.Y - 14 && ay <= r.Bottom + 14)
            .OrderByDescending(r => ax >= r.X - 2 && ax <= r.Right + 2 && ay >= r.Y - 2 && ay <= r.Bottom + 2 ? 1 : 0)
            .ThenByDescending(r => r.Width * r.Height)
            .FirstOrDefault();

        // 2. Motor / döner makine çemberi
        motorCircle = ctx.Circles
            .Where(c => c.Radius is >= 4.0 and <= 12.0
                && Math.Abs(c.CenterX - ax) <= 35
                && Math.Abs(c.CenterY - ay) <= 25)
            .OrderBy(c => Distance(c.CenterX, c.CenterY, ax, ay))
            .FirstOrDefault();

        if (rect is not null && (motorCircle is null || rect.Width >= 25))
        {
            return rect;
        }

        if (motorCircle is not null)
        {
            var r = motorCircle.Radius;
            var x0 = motorCircle.CenterX - r - 2.0;
            var x1 = motorCircle.CenterX + r + 3.0;
            var y0 = motorCircle.CenterY - r - 3.5;
            var y1 = motorCircle.CenterY + r + 3.5;
            return new RectCandidate(x0, y0, x1 - x0, y1 - y0, [motorCircle.GraphicId]);
        }

        // 3. Yer imi uç listesi olan ama dış dikdörtgeni çizilmemiş standart semboller (ör. bobin, kontak, sigorta)
        if (hint is { PinNames.Count: > 0 })
        {
            var expected = hint.PinNames.Select(CleanPinName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var nearbyPins = ctx.Texts
                .Where(t => !t.Claimed
                    && t != anchor
                    && expected.Contains(CleanPinName(t.Text.Text))
                    && t.Text.X >= ax - 5
                    && t.Text.X <= ax + 35
                    && Math.Abs(t.Text.Y - ay) <= 30)
                .ToList();
            if (nearbyPins.Count == 0)
            {
                nearbyPins = ctx.Texts
                    .Where(t => !t.Claimed
                        && t != anchor
                        && expected.Contains(CleanPinName(t.Text.Text))
                        && Math.Abs(t.Text.X - ax) <= 35
                        && Math.Abs(t.Text.Y - ay) <= 30)
                    .ToList();
            }

            if (nearbyPins.Count > 0)
            {
                var minX = nearbyPins.Min(t => t.Text.X) - 2.0;
                var maxX = nearbyPins.Max(t => t.Text.X) + 5.0;
                var minY = nearbyPins.Min(t => t.Text.Y) - 2.0;
                var maxY = nearbyPins.Max(t => t.Text.Y) + 2.0;
                if (maxX - minX < 6)
                {
                    var cx = (minX + maxX) / 2;
                    (minX, maxX) = (cx - 4, cx + 4);
                }

                if (maxY - minY < 10)
                {
                    var cy = (minY + maxY) / 2;
                    (minY, maxY) = (cy - 6, cy + 6);
                }

                return new RectCandidate(minX, minY, maxX - minX, maxY - minY, []);
            }
        }

        return null;
    }

    private sealed record DeviceMetadata(
        PartRef? Part,
        string? Description,
        IReadOnlyDictionary<string, string>? Properties,
        List<GraphicId> ConsumedIds);

    private static DeviceMetadata ExtractMetadata(
        PageContext ctx,
        TextEntry anchor,
        RectCandidate body,
        string? inlineManufacturer)
    {
        var consumed = new List<GraphicId>();
        var props = new SortedDictionary<string, string>(StringComparer.Ordinal);
        string? orderCode = null;
        string? manufacturer = inlineManufacturer;
        string? description = null;

        var ax = anchor.Text.X;
        var ay = anchor.Text.Y;
        var searchMinX = Math.Min(ax, body.X) - 25;
        var searchMaxX = Math.Max(ax + 30, body.Right + 10);
        var searchMinY = Math.Min(ay, body.Y) - 8;
        var searchMaxY = Math.Max(ay, body.Bottom) + 32;

        foreach (var entry in ctx.Texts)
        {
            if (entry == anchor || entry.Claimed)
            {
                continue;
            }

            var t = entry.Text;
            if (t.X < searchMinX || t.X > searchMaxX || t.Y < searchMinY || t.Y > searchMaxY)
            {
                continue;
            }

            var value = t.Text.Trim();
            if (value.Length == 0 || PinNamePatternRegex().IsMatch(value) || NumericOrCrossRefRegex().IsMatch(value))
            {
                continue;
            }

            if (ManufacturerKeywords.Any(k => value.Contains(k, StringComparison.OrdinalIgnoreCase)) && value.Length <= 40)
            {
                manufacturer = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.ToLowerInvariant());
                consumed.Add(entry.Id);
                continue;
            }

            var nearTag = Math.Abs(t.Y - ay) <= 14 && t.X <= body.X + 40 && Math.Abs(t.X - ax) <= 30;
            var knownPrefix = KnownManufacturers.Any(m => value.StartsWith(m.Prefix, StringComparison.OrdinalIgnoreCase));
            if (orderCode is null && nearTag && (OrderCodeRegex().IsMatch(value) || knownPrefix)
                && (!LooksLikeGenericTag(value) || (knownPrefix && t.Y > ay)))
            {
                orderCode = value;
                consumed.Add(entry.Id);
                continue;
            }

            if (nearTag && ElectricalValueRegex().IsMatch(value))
            {
                props["Anma değeri"] = value;
                consumed.Add(entry.Id);
                continue;
            }

            // Motor veya küçük cihazın altındaki büyük açıklama yazısı ("Ürün Dizme_Sol" gibi)
            if (description is null
                && t.SizeMm >= 4.2
                && Math.Abs(t.RotationDeg) < 1
                && Math.Abs(t.X - body.X) <= 35
                && t.Y > body.Bottom
                && t.Y <= body.Bottom + 32
                && !value.Contains("BUSS BAR", StringComparison.OrdinalIgnoreCase))
            {
                description = value;
            }
        }

        if (manufacturer is null && orderCode is not null)
        {
            foreach (var (prefix, known) in KnownManufacturers)
            {
                if (orderCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    manufacturer = known;
                    break;
                }
            }
        }

        PartRef? part = orderCode is not null
            ? new PartRef(manufacturer ?? "Belirtilmedi", orderCode)
            : manufacturer is not null
                ? new PartRef(manufacturer, anchor.Text.Text.Trim())
                : null;
        return new DeviceMetadata(part, description, props.Count > 0 ? props : null, consumed);
    }

    private static List<RecognizedPin> ExtractPins(
        PageContext ctx,
        RectCandidate body,
        CircleCandidate? motorCircle,
        IReadOnlyList<string>? bookmarkPins,
        out List<GraphicId> consumedPinTextIds)
    {
        var raw = new List<(string? Name, double X, double Y, int Dx, int Dy, int Priority, GraphicId? TextId)>();
        var usedTextIds = new HashSet<GraphicId>();
        var x0 = body.X;
        var y0 = body.Y;
        var x1 = body.Right;
        var y1 = body.Bottom;

        (double Px, double Py, int Dx, int Dy)? BoundaryContact(double px, double py)
        {
            var candidates = new List<(double Dist, double Bx, double By, int Dx, int Dy)>();
            if (px >= x0 - 2.5 && px <= x1 + 2.5)
            {
                if (Math.Abs(py - y0) <= PinStubToleranceMm)
                {
                    candidates.Add((Math.Abs(py - y0), Math.Clamp(px, x0, x1), y0, 0, -1));
                }

                if (Math.Abs(py - y1) <= PinStubToleranceMm)
                {
                    candidates.Add((Math.Abs(py - y1), Math.Clamp(px, x0, x1), y1, 0, 1));
                }
            }

            if (py >= y0 - 2.5 && py <= y1 + 2.5)
            {
                if (Math.Abs(px - x0) <= PinStubToleranceMm)
                {
                    candidates.Add((Math.Abs(px - x0), x0, Math.Clamp(py, y0, y1), -1, 0));
                }

                if (Math.Abs(px - x1) <= PinStubToleranceMm)
                {
                    candidates.Add((Math.Abs(px - x1), x1, Math.Clamp(py, y0, y1), 1, 0));
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            var best = candidates.OrderBy(c => c.Dist).First();
            return (best.Bx, best.By, best.Dx, best.Dy);
        }

        // Katman 1: Dışarıdan gövde sınırına değen eksene paralel gerçek tel uçları
        var isSmallSymbol = body.Width < 25 && body.Height < 25 && bookmarkPins is { Count: > 0 };
        var minWireLen = isSmallSymbol ? 11.0 : 6.0;
        foreach (var (id, p) in ctx.Polylines)
        {
            if (p.Filled || p.Closed || body.EdgeIds.Contains(id) || p.Points.Count < 4)
            {
                continue;
            }

            var count = p.Points.Count / 2;
            for (var i = 1; i < count; i++)
            {
                var (ax, ay) = (p.Points[(i - 1) * 2], p.Points[(i - 1) * 2 + 1]);
                var (bx, by) = (p.Points[i * 2], p.Points[i * 2 + 1]);
                if (Math.Abs(ax - bx) >= 0.35 && Math.Abs(ay - by) >= 0.35)
                {
                    continue;
                }

                var len = Distance(ax, ay, bx, by);
                if (len < minWireLen)
                {
                    continue;
                }

                var aIn = InsideOrOn(ax, ay, x0, y0, x1, y1, -0.5);
                var bIn = InsideOrOn(bx, by, x0, y0, x1, y1, -0.5);
                if (aIn && bIn)
                {
                    continue;
                }

                if (BoundaryContact(ax, ay) is { } ca && !bIn && OutwardConsistent(ca.Dx, ca.Dy, bx - ax, by - ay))
                {
                    raw.Add((null, ca.Px, ca.Py, ca.Dx, ca.Dy, 3, null));
                }

                if (BoundaryContact(bx, by) is { } cb && !aIn && OutwardConsistent(cb.Dx, cb.Dy, ax - bx, ay - by))
                {
                    raw.Add((null, cb.Px, cb.Py, cb.Dx, cb.Dy, 3, null));
                }
            }
        }

        // Kesikli yeşil PE / renkli zincir uçları da tel ucu olarak eklenir
        foreach (var (s, _) in ctx.VirtualDashed)
        {
            if (BoundaryContact(s.X1, s.Y1) is { } ca && OutwardConsistent(ca.Dx, ca.Dy, s.X2 - s.X1, s.Y2 - s.Y1))
            {
                raw.Add((null, ca.Px, ca.Py, ca.Dx, ca.Dy, 3, null));
            }

            if (BoundaryContact(s.X2, s.Y2) is { } cb && OutwardConsistent(cb.Dx, cb.Dy, s.X1 - s.X2, s.Y1 - s.Y2))
            {
                raw.Add((null, cb.Px, cb.Py, cb.Dx, cb.Dy, 3, null));
            }
        }

        // Katman 2: Gövde kenarlarındaki küçük terminal halkaları (10D1/D2 CN1, CN4, CN8 gibi)
        foreach (var c in ctx.Circles)
        {
            if (c.Radius is < 0.4 or > 1.7)
            {
                continue;
            }

            if (c.CenterX < x0 - 1.5 || c.CenterX > x1 + 1.5 || c.CenterY < y0 - 1.5 || c.CenterY > y1 + 1.5)
            {
                continue;
            }

            var topDist = Math.Abs(c.CenterY - y0);
            var botDist = Math.Abs(c.CenterY - y1);
            var leftDist = Math.Abs(c.CenterX - x0);
            var rightDist = Math.Abs(c.CenterX - x1);
            var minDist = Math.Min(Math.Min(topDist, botDist), Math.Min(leftDist, rightDist));
            if (minDist > 3.0)
            {
                continue;
            }

            if (minDist == topDist)
            {
                raw.Add((null, c.CenterX, y0, 0, -1, 2, null));
            }
            else if (minDist == botDist)
            {
                raw.Add((null, c.CenterX, y1, 0, 1, 2, null));
            }
            else if (minDist == leftDist)
            {
                raw.Add((null, x0, c.CenterY, -1, 0, 2, null));
            }
            else
            {
                raw.Add((null, x1, c.CenterY, 1, 0, 2, null));
            }
        }

        // Pin uç noktalarına en yakın pin metnini eşle
        var expectedSet = bookmarkPins?
            .Select(CleanPinName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < raw.Count; i++)
        {
            var pin = raw[i];
            var match = FindPinTextNear(ctx, expectedSet, pin.X, pin.Y, pin.Dx, pin.Dy, usedTextIds);
            if (match is not null)
            {
                usedTextIds.Add(match.Id);
                raw[i] = pin with { Name = match.Text.Text, TextId = match.Id };
            }
        }

        if (motorCircle is not null && bookmarkPins is { Count: > 0 })
        {
            raw.RemoveAll(r => r.Priority == 3 && r.Name is null && r.Dx != 0);
        }

        // Katman 3: Gövde çevresinde kalmış pin adlarından ek uçlar (ör. halka çizilmemiş RJ45/encoder pinleri: CN2/1..6, CN7/1..A)
        foreach (var entry in ctx.Texts)
        {
            if (entry.Claimed || usedTextIds.Contains(entry.Id))
            {
                continue;
            }

            var clean = CleanPinName(entry.Text.Text);
            var isPinLabel = (expectedSet is not null && expectedSet.Contains(clean))
                || (expectedSet is null && IsPlausiblePinLabel(entry.Text.Text) && Math.Abs(entry.Text.RotationDeg) > 45);
            if (!isPinLabel)
            {
                continue;
            }

            if (!ProjectTextToBoundary(entry.Text, x0, y0, x1, y1, out var px, out var py, out var dx, out var dy))
            {
                continue;
            }

            if (raw.Any(existing => Distance(existing.X, existing.Y, px, py) <= PinMergeDistanceMm))
            {
                continue;
            }

            usedTextIds.Add(entry.Id);
            raw.Add((entry.Text.Text, px, py, dx, dy, 1, entry.Id));
        }

        // Yakın pinleri birleştir (yüksek öncelik kazanır)
        var merged = new List<(string? Name, double X, double Y, int Dx, int Dy, int Priority, GraphicId? TextId)>();
        foreach (var item in raw.OrderByDescending(r => r.Priority).ThenByDescending(r => r.Name is not null))
        {
            var idx = merged.FindIndex(m => Distance(m.X, m.Y, item.X, item.Y) <= PinMergeDistanceMm && m.Dx == item.Dx && m.Dy == item.Dy);
            if (idx < 0)
            {
                merged.Add(item);
            }
            else if (merged[idx].Name is null && item.Name is not null)
            {
                merged[idx] = merged[idx] with { Name = item.Name, TextId = item.TextId };
            }
        }

        // Düzenli sırala (üst/alt soldan sağa, yanlar yukarıdan aşağıya) ve benzersiz geçerli uç adları üret
        var ordered = merged
            .OrderBy(p => p.Dy != 0 ? (p.Dy < 0 ? 0 : 1) : (p.Dx < 0 ? 2 : 3))
            .ThenBy(p => p.Dy != 0 ? p.X : p.Y)
            .ToList();

        var pins = new List<RecognizedPin>(ordered.Count);
        var usedNames = new HashSet<string>(StringComparer.Ordinal);
        var autoIdx = 1;
        foreach (var p in ordered)
        {
            var baseName = CleanPinName(p.Name);
            if (baseName.Length == 0)
            {
                baseName = autoIdx.ToString(CultureInfo.InvariantCulture);
                autoIdx++;
            }

            var finalName = baseName;
            var suffix = 2;
            while (!usedNames.Add(finalName))
            {
                finalName = $"{baseName}_{suffix}";
                suffix++;
            }

            var attrs = InferPinAttributes(finalName, motorCircle is not null);
            pins.Add(new RecognizedPin(
                finalName,
                Math.Round(p.X, 3),
                Math.Round(p.Y, 3),
                p.Dx,
                p.Dy,
                attrs));
        }

        consumedPinTextIds = [.. usedTextIds];
        return pins;
    }

    private static TextEntry? FindPinTextNear(
        PageContext ctx,
        HashSet<string>? expectedSet,
        double px,
        double py,
        int dx,
        int dy,
        HashSet<GraphicId> usedTextIds)
    {
        return ctx.Texts
            .Where(t => !t.Claimed && !usedTextIds.Contains(t.Id))
            .Select(t =>
            {
                var cx = Math.Abs(t.Text.RotationDeg) > 45 ? t.Text.X + 0.9 : t.Text.X;
                var cy = t.Text.Y;
                var alongOutward = ((cx - px) * dx) + ((cy - py) * dy);
                var crossDist = Math.Abs(((cx - px) * dy) - ((cy - py) * dx));
                var totalDist = Distance(cx, cy, px, py);
                return (Entry: t, Along: alongOutward, Cross: crossDist, Dist: totalDist);
            })
            .Where(item =>
            {
                if (item.Dist > 11.5 || item.Cross > 4.2)
                {
                    return false;
                }

                var raw = item.Entry.Text.Text.Trim();
                if (NumericOrCrossRefRegex().IsMatch(raw) && raw.Contains('.', StringComparison.Ordinal))
                {
                    return false;
                }

                var clean = CleanPinName(raw);
                if (clean.Length == 0 || clean.Length > 14)
                {
                    return false;
                }

                return (expectedSet is not null && expectedSet.Contains(clean))
                    || IsPlausiblePinLabel(raw);
            })
            .OrderByDescending(item => expectedSet is not null && expectedSet.Contains(CleanPinName(item.Entry.Text.Text)) ? 1 : 0)
            .ThenBy(item => item.Cross + (Math.Abs(item.Along) * 0.35))
            .Select(item => item.Entry)
            .FirstOrDefault();
    }

    private static bool ProjectTextToBoundary(
        GraphicText text,
        double x0,
        double y0,
        double x1,
        double y1,
        out double px,
        out double py,
        out int dx,
        out int dy)
    {
        px = py = 0;
        dx = dy = 0;
        var tx = Math.Abs(text.RotationDeg) > 45 ? Math.Round((text.X + 0.9) * 2) / 2 : text.X;
        var ty = text.Y;
        const double maxBand = 12.0;

        if (tx >= x0 - 1.5 && tx <= x1 + 1.5)
        {
            if (ty <= y0 + 2.0 && ty >= y0 - maxBand)
            {
                (px, py, dx, dy) = (Math.Clamp(tx, x0, x1), y0, 0, -1);
                return true;
            }

            if (ty >= y1 - 2.0 && ty <= y1 + maxBand)
            {
                (px, py, dx, dy) = (Math.Clamp(tx, x0, x1), y1, 0, 1);
                return true;
            }
        }

        if (ty >= y0 - 1.5 && ty <= y1 + 1.5)
        {
            if (tx <= x0 + 2.0 && tx >= x0 - maxBand)
            {
                (px, py, dx, dy) = (x0, Math.Clamp(ty, y0, y1), -1, 0);
                return true;
            }

            if (tx >= x1 - 2.0 && tx <= x1 + maxBand)
            {
                (px, py, dx, dy) = (x1, Math.Clamp(ty, y0, y1), 1, 0);
                return true;
            }
        }

        return false;
    }

    private static (SymbolDefinition Symbol, List<GraphicId> ConsumedIds) BuildSymbolDefinition(
        PageContext ctx,
        RectCandidate body,
        CircleCandidate? motorCircle,
        List<RecognizedPin> pins,
        string tag,
        GraphicId anchorTextId,
        List<GraphicId> pinTextIds,
        List<GraphicId> metaTextIds)
    {
        var ox = pins[0].SheetX;
        var oy = pins[0].SheetY;
        var shapes = new List<SymbolShape>();
        var consumed = new List<GraphicId>();

        var pinTextSet = pinTextIds.ToHashSet();
        var metaSet = metaTextIds.ToHashSet();
        var consumedPolySet = new HashSet<GraphicId>();

        const double bodyPad = 3.0;
        var bx0 = body.X - bodyPad;
        var by0 = body.Y - bodyPad;
        var bx1 = body.Right + (body.Width > 50 ? 12.0 : bodyPad);
        var by1 = body.Bottom + bodyPad;

        if (motorCircle is not null)
        {
            shapes.Add(new SymbolCircle(
                Round(motorCircle.CenterX - ox),
                Round(motorCircle.CenterY - oy),
                Round(motorCircle.Radius),
                Math.Clamp(motorCircle.StrokeMm, 0.15, 0.8)));
            consumed.Add(motorCircle.GraphicId);
            consumedPolySet.Add(motorCircle.GraphicId);

            // Motor sembolündeki "M", "3~" yazıları ve gövde içi kısa bağlantılar
            foreach (var entry in ctx.Texts)
            {
                if (entry.Id == anchorTextId || pinTextSet.Contains(entry.Id) || metaSet.Contains(entry.Id))
                {
                    continue;
                }

                if (Distance(entry.Text.X, entry.Text.Y, motorCircle.CenterX, motorCircle.CenterY) <= motorCircle.Radius + 1.5)
                {
                    shapes.Add(new SymbolText(
                        Round(entry.Text.X - ox),
                        Round(entry.Text.Y - oy),
                        entry.Text.Text,
                        Math.Clamp(entry.Text.SizeMm, 1.2, 6.0),
                        entry.Text.Align,
                        entry.Text.RotationDeg));
                    consumed.Add(entry.Id);
                }
            }

            foreach (var (id, p) in ctx.Polylines)
            {
                if (consumedPolySet.Contains(id) || p.Filled || p.Points.Count < 4)
                {
                    continue;
                }

                var bounds = p.Bounds;
                var allNearMotor = bounds.X >= motorCircle.CenterX - motorCircle.Radius - 4.0
                    && bounds.X + bounds.Width <= motorCircle.CenterX + motorCircle.Radius + 4.5
                    && bounds.Y >= motorCircle.CenterY - motorCircle.Radius - 4.5
                    && bounds.Y + bounds.Height <= motorCircle.CenterY + motorCircle.Radius + 5.0;
                if (allNearMotor)
                {
                    AddPolylineToSymbol(shapes, p, ox, oy);
                    consumed.Add(id);
                    consumedPolySet.Add(id);
                }
            }
        }
        else
        {
            // Gövde içi çemberler (terminal halkaları dahil)
            foreach (var c in ctx.Circles)
            {
                if (c.CenterX >= bx0 && c.CenterX <= bx1 && c.CenterY >= by0 && c.CenterY <= by1)
                {
                    shapes.Add(new SymbolCircle(
                        Round(c.CenterX - ox),
                        Round(c.CenterY - oy),
                        Round(c.Radius),
                        Math.Clamp(c.StrokeMm, 0.15, 0.8),
                        c.Polyline?.Filled ?? false));
                    consumed.Add(c.GraphicId);
                    consumedPolySet.Add(c.GraphicId);
                }
            }

            // Gövdenin kendi çerçeve kenarları
            foreach (var edgeId in body.EdgeIds)
            {
                if (consumedPolySet.Add(edgeId) && ctx.Polylines.FirstOrDefault(p => p.Id == edgeId) is { Poly: not null } found)
                {
                    AddPolylineToSymbol(shapes, found.Poly, ox, oy);
                    consumed.Add(edgeId);
                }
            }

            // Gövdenin tamamen içinde kalan bölmeler, RJ45 ikonları ve kısa diyagonal/yatay çizgiler
            foreach (var (id, p) in ctx.Polylines)
            {
                if (consumedPolySet.Contains(id) || p.Filled)
                {
                    continue;
                }

                var (px, py, pw, ph) = p.Bounds;
                var strictlyInside = px >= body.X - 0.8 && px + pw <= bx1 && py >= body.Y - 0.8 && py + ph <= body.Bottom + 0.8;
                if (strictlyInside)
                {
                    AddPolylineToSymbol(shapes, p, ox, oy);
                    consumed.Add(id);
                    consumedPolySet.Add(id);
                }
            }

            // Gövde içi metinler (port adları: BUS BAR MODULE, DIGITAL I/O, SERCOS 3, TX+, A_DI1...)
            foreach (var entry in ctx.Texts)
            {
                if (entry.Id == anchorTextId || pinTextSet.Contains(entry.Id) || metaSet.Contains(entry.Id))
                {
                    continue;
                }

                var t = entry.Text;
                if (t.X >= body.X - 0.5 && t.X <= bx1 && t.Y >= body.Y - 0.5 && t.Y <= body.Bottom + 0.5)
                {
                    shapes.Add(new SymbolText(
                        Round(t.X - ox),
                        Round(t.Y - oy),
                        t.Text,
                        Math.Clamp(t.SizeMm, 1.0, 8.0),
                        t.Align,
                        t.RotationDeg));
                    consumed.Add(entry.Id);
                }
            }
        }

        // Çok ince/boş kalma durumunda minimal bir çerçeve ekle ki sembol her zaman çizilebilir olsun
        if (shapes.Count == 0)
        {
            shapes.Add(new SymbolRect(
                Round(body.X - ox),
                Round(body.Y - oy),
                Round(Math.Max(2.0, body.Width)),
                Round(Math.Max(2.0, body.Height))));
        }

        var symbolPins = pins
            .Select(pin => new SymbolPin(
                Round(pin.SheetX - ox),
                Round(pin.SheetY - oy),
                pin.DirectionX,
                pin.DirectionY,
                pin.Name,
                pin.Attributes))
            .ToList();

        var def = new SymbolDefinition(
            SymbolId.New(),
            $"{tag} ({pins.Count} uç)",
            shapes,
            symbolPins,
            Source: "Grafikten tanındı",
            License: "Kullanıcı çizimi",
            TagLetter: ExtractTagLetter(tag));

        return (def, consumed);
    }

    private static void AddPolylineToSymbol(List<SymbolShape> shapes, GraphicPolyline poly, double ox, double oy)
    {
        var count = poly.Points.Count / 2;
        if (count < 2)
        {
            return;
        }

        for (var i = 1; i < count; i++)
        {
            var (x1, y1) = (Round(poly.Points[(i - 1) * 2] - ox), Round(poly.Points[((i - 1) * 2) + 1] - oy));
            var (x2, y2) = (Round(poly.Points[i * 2] - ox), Round(poly.Points[(i * 2) + 1] - oy));
            if (Math.Abs(x2 - x1) >= 0.01 || Math.Abs(y2 - y1) >= 0.01)
            {
                shapes.Add(new SymbolLine(x1, y1, x2, y2, Math.Clamp(poly.StrokeMm, 0.12, 1.0)));
            }
        }

        if (poly.Closed && count >= 3)
        {
            var (x1, y1) = (Round(poly.Points[(count - 1) * 2] - ox), Round(poly.Points[((count - 1) * 2) + 1] - oy));
            var (x2, y2) = (Round(poly.Points[0] - ox), Round(poly.Points[1] - oy));
            if (Math.Abs(x2 - x1) >= 0.01 || Math.Abs(y2 - y1) >= 0.01)
            {
                shapes.Add(new SymbolLine(x1, y1, x2, y2, Math.Clamp(poly.StrokeMm, 0.12, 1.0)));
            }
        }
    }

    private sealed class WireGraphNode(double x, double y)
    {
        public double X { get; } = x;
        public double Y { get; } = y;
        public List<(WireGraphNode Next, double Length, GraphicId[] Ids, string? Color)> Edges { get; } = [];
        public (RecognizedDevice Device, RecognizedPin Pin)? Pin { get; set; }
    }

    private static List<RecognizedWire> DetectWires(
        PageId pageId,
        PageContext ctx,
        List<RecognizedDevice> devices,
        HashSet<GraphicId> consumedByDevices)
    {
        if (devices.Count == 0)
        {
            return [];
        }

        // Tüketilmemiş tel parçalarını topla
        var rawSegments = new List<(double X1, double Y1, double X2, double Y2, string? Color, GraphicId[] Ids)>();

        foreach (var (id, p) in ctx.Polylines)
        {
            if (consumedByDevices.Contains(id) || p.Filled || p.Closed)
            {
                continue;
            }

            var count = p.Points.Count / 2;
            for (var i = 1; i < count; i++)
            {
                var (x1, y1) = (p.Points[(i - 1) * 2], p.Points[((i - 1) * 2) + 1]);
                var (x2, y2) = (p.Points[i * 2], p.Points[(i * 2) + 1]);
                if (Distance(x1, y1, x2, y2) < 0.8)
                {
                    continue;
                }

                // Çoklu küçük renkli dash zaten virtualDashed içinde işlendi
                if (p.Color is not null && Distance(x1, y1, x2, y2) <= 4.0)
                {
                    continue;
                }

                if (Math.Abs(x1 - x2) < 0.35 || Math.Abs(y1 - y2) < 0.35 || Distance(x1, y1, x2, y2) >= 2.0)
                {
                    rawSegments.Add((x1, y1, x2, y2, p.Color, [id]));
                }
            }
        }

        foreach (var (v, ids) in ctx.VirtualDashed)
        {
            if (!ids.All(consumedByDevices.Contains))
            {
                rawSegments.Add((v.X1, v.Y1, v.X2, v.Y2, v.Color, [.. ids]));
            }
        }

        if (rawSegments.Count == 0)
        {
            return [];
        }

        // Kesişme / T-kavşağı bölmeleri: her uç nokta başka bir segmentin içine düşüyorsa segmenti böl
        var endpoints = rawSegments
            .SelectMany(s => new[] { (s.X1, s.Y1), (s.X2, s.Y2) })
            .Concat(devices.SelectMany(d => d.Pins.Select(p => (p.SheetX, p.SheetY))))
            .ToList();

        var splitSegments = new List<(double X1, double Y1, double X2, double Y2, string? Color, GraphicId[] Ids)>();
        foreach (var seg in rawSegments)
        {
            var len = Distance(seg.X1, seg.Y1, seg.X2, seg.Y2);
            if (len < 0.5)
            {
                continue;
            }

            var cuts = new List<double> { 0.0, 1.0 };
            var dx = seg.X2 - seg.X1;
            var dy = seg.Y2 - seg.Y1;
            var len2 = (dx * dx) + (dy * dy);
            foreach (var (px, py) in endpoints)
            {
                var t = (((px - seg.X1) * dx) + ((py - seg.Y1) * dy)) / len2;
                if (t <= 0.02 || t >= 0.98)
                {
                    continue;
                }

                var projX = seg.X1 + (t * dx);
                var projY = seg.Y1 + (t * dy);
                if (Distance(px, py, projX, projY) <= 0.75)
                {
                    cuts.Add(t);
                }
            }

            var distinct = cuts.Order().ToList();
            var lastT = 0.0;
            for (var i = 1; i < distinct.Count; i++)
            {
                if (distinct[i] - lastT < 0.01 && i < distinct.Count - 1)
                {
                    continue;
                }

                var ax = seg.X1 + (lastT * dx);
                var ay = seg.Y1 + (lastT * dy);
                var bx = seg.X1 + (distinct[i] * dx);
                var by = seg.Y1 + (distinct[i] * dy);
                if (Distance(ax, ay, bx, by) >= 0.4)
                {
                    splitSegments.Add((ax, ay, bx, by, seg.Color, seg.Ids));
                }

                lastT = distinct[i];
            }
        }

        // Graf düğümleri oluştur (aynı renkte ve WireJoinToleranceMm içinde uçları birleştir)
        var nodes = new List<WireGraphNode>();
        WireGraphNode GetOrCreateNode(double x, double y)
        {
            var existing = nodes.FirstOrDefault(n => Distance(n.X, n.Y, x, y) <= WireJoinToleranceMm);
            if (existing is not null)
            {
                return existing;
            }

            var created = new WireGraphNode(x, y);
            nodes.Add(created);
            return created;
        }

        foreach (var seg in splitSegments)
        {
            var na = GetOrCreateNode(seg.X1, seg.Y1);
            var nb = GetOrCreateNode(seg.X2, seg.Y2);
            if (na == nb)
            {
                continue;
            }

            var len = Distance(na.X, na.Y, nb.X, nb.Y);
            na.Edges.Add((nb, len, seg.Ids, seg.Color));
            nb.Edges.Add((na, len, seg.Ids, seg.Color));
        }

        // Her tespit edilen cihaz pinini graf düğümleriyle eşleştir
        var pinNodes = new List<(WireGraphNode Node, RecognizedDevice Device, RecognizedPin Pin)>();
        foreach (var dev in devices)
        {
            foreach (var pin in dev.Pins)
            {
                var best = nodes
                    .Select(n => (Node: n, Dist: Distance(n.X, n.Y, pin.SheetX, pin.SheetY)))
                    .Where(x => x.Dist <= PinStubToleranceMm && x.Node.Pin is null)
                    .OrderBy(x => x.Dist)
                    .FirstOrDefault();
                if (best.Node is not null)
                {
                    best.Node.Pin = (dev, pin);
                    pinNodes.Add((best.Node, dev, pin));
                }
            }
        }

        // Bağlı bileşenler üzerinde pin çiftlerini çıkar ve en kısa yolları oluştur
        var wires = new List<RecognizedWire>();
        var visited = new HashSet<WireGraphNode>();
        var claimedWireTextIds = new HashSet<GraphicId>();
        var wireSeq = 0;

        foreach (var start in pinNodes.Select(p => p.Node))
        {
            if (!visited.Add(start))
            {
                continue;
            }

            var componentNodes = new List<WireGraphNode>();
            var queue = new Queue<WireGraphNode>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                componentNodes.Add(cur);
                foreach (var edge in cur.Edges)
                {
                    if (visited.Add(edge.Next))
                    {
                        queue.Enqueue(edge.Next);
                    }
                }
            }

            var componentPins = componentNodes
                .Where(n => n.Pin is not null)
                .ToList();
            if (componentPins.Count < 2)
            {
                continue;
            }

            // En kısa ağacı (spanning tree) kurarak bu ağdaki tüm pinleri birbirine bağla
            var inTree = new HashSet<WireGraphNode> { componentPins[0] };
            var remaining = componentPins.Skip(1).ToHashSet();
            while (remaining.Count > 0)
            {
                (WireGraphNode A, WireGraphNode B, List<(double X, double Y)> Path, double Cost, GraphicId[] Ids, string? Color)? bestLink = null;
                foreach (var a in inTree)
                {
                    var found = ShortestPaths(a, remaining);
                    foreach (var (target, info) in found)
                    {
                        var sameDevice = a.Pin!.Value.Device.Key == target.Pin!.Value.Device.Key;
                        if (sameDevice && (a.Pin!.Value.Pin.Name == target.Pin!.Value.Pin.Name
                            || !PathLeavesDevice(a.Pin!.Value.Device, info.Path, 6.0)))
                        {
                            continue;
                        }

                        if (bestLink is null || info.Cost < bestLink.Value.Cost)
                        {
                            bestLink = (a, target, info.Path, info.Cost, info.Ids, info.Color);
                        }
                    }
                }

                if (bestLink is null)
                {
                    break;
                }

                inTree.Add(bestLink.Value.B);
                remaining.Remove(bestLink.Value.B);

                var fromPin = bestLink.Value.A.Pin!.Value;
                var toPin = bestLink.Value.B.Pin!.Value;
                var route = BuildWireRoute(fromPin.Pin, toPin.Pin, bestLink.Value.Path);
                var (wireNum, numTextId) = FindWireNumber(ctx, bestLink.Value.Path, consumedByDevices, claimedWireTextIds);
                if (numTextId is { } textId)
                {
                    claimedWireTextIds.Add(textId);
                }

                var colorCode = MapWireColor(bestLink.Value.Color, fromPin.Pin.Name, toPin.Pin.Name);
                var consumedWireIds = bestLink.Value.Ids
                    .Concat(numTextId is { } tid ? [tid] : Array.Empty<GraphicId>())
                    .Distinct()
                    .ToList();

                wires.Add(new RecognizedWire(
                    $"W:{pageId.Value:N}:{++wireSeq}",
                    pageId,
                    fromPin.Device.Key,
                    fromPin.Pin.Name,
                    toPin.Device.Key,
                    toPin.Pin.Name,
                    route,
                    wireNum,
                    colorCode,
                    consumedWireIds));
            }
        }

        return wires;
    }

    private static bool PathLeavesDevice(RecognizedDevice device, List<(double X, double Y)> path, double marginMm) =>
        path.Any(pt =>
            pt.X < device.X - marginMm
            || pt.X > device.X + device.Width + marginMm
            || pt.Y < device.Y - marginMm
            || pt.Y > device.Y + device.Height + marginMm);

    private static Dictionary<WireGraphNode, (List<(double X, double Y)> Path, double Cost, GraphicId[] Ids, string? Color)> ShortestPaths(
        WireGraphNode start,
        HashSet<WireGraphNode> targets)
    {
        var dist = new Dictionary<WireGraphNode, double> { [start] = 0 };
        var prev = new Dictionary<WireGraphNode, (WireGraphNode Prev, GraphicId[] Ids, string? Color)>();
        var pq = new PriorityQueue<WireGraphNode, double>();
        pq.Enqueue(start, 0);

        while (pq.TryDequeue(out var u, out var d))
        {
            if (d > dist.GetValueOrDefault(u, double.MaxValue))
            {
                continue;
            }

            foreach (var edge in u.Edges)
            {
                var nd = d + edge.Length;
                if (nd < dist.GetValueOrDefault(edge.Next, double.MaxValue))
                {
                    dist[edge.Next] = nd;
                    prev[edge.Next] = (u, edge.Ids, edge.Color);
                    pq.Enqueue(edge.Next, nd);
                }
            }
        }

        var result = new Dictionary<WireGraphNode, (List<(double, double)>, double, GraphicId[], string?)>();
        foreach (var target in targets)
        {
            if (!dist.TryGetValue(target, out var cost))
            {
                continue;
            }

            var path = new List<(double X, double Y)>();
            var ids = new List<GraphicId>();
            string? color = null;
            for (var cur = target; cur != start; cur = prev[cur].Prev)
            {
                path.Add((cur.X, cur.Y));
                var step = prev[cur];
                ids.AddRange(step.Ids);
                color ??= step.Color;
            }

            path.Add((start.X, start.Y));
            path.Reverse();
            result[target] = (path, cost, [.. ids.Distinct()], color);
        }

        return result;
    }

    private static IReadOnlyList<WirePoint> BuildWireRoute(
        RecognizedPin from,
        RecognizedPin to,
        List<(double X, double Y)> rawPath)
    {
        // Uçlara çok yakın ilk/son ara noktaları sadeleştir ki ManualPath (5 mm çıkış ucu) geriye kıvrılmasın
        var simplified = new List<(double X, double Y)> { (from.SheetX, from.SheetY) };
        for (var i = 1; i < rawPath.Count - 1; i++)
        {
            var prev = simplified[^1];
            var cur = rawPath[i];
            var next = rawPath[i + 1];
            var collinearH = Math.Abs(prev.Y - cur.Y) < 0.25 && Math.Abs(cur.Y - next.Y) < 0.25;
            var collinearV = Math.Abs(prev.X - cur.X) < 0.25 && Math.Abs(cur.X - next.X) < 0.25;
            if (!collinearH && !collinearV)
            {
                simplified.Add(cur);
            }
        }

        simplified.Add((to.SheetX, to.SheetY));

        var interior = new List<WirePoint>();
        for (var i = 1; i < simplified.Count - 1; i++)
        {
            var pt = simplified[i];
            // İlk köşe başlangıç çıkış ucunun gerisindeyse atla
            if (i == 1 && (((pt.X - from.SheetX) * from.DirectionX) + ((pt.Y - from.SheetY) * from.DirectionY) < -0.5))
            {
                continue;
            }

            if (i == simplified.Count - 2 && (((pt.X - to.SheetX) * to.DirectionX) + ((pt.Y - to.SheetY) * to.DirectionY) < -0.5))
            {
                continue;
            }

            interior.Add(new WirePoint(Round(pt.X), Round(pt.Y)));
        }

        // Doğrudan hizalı düz tellerde tek orta nokta verilir ki ManualPath otomatik yönlendiriciye düşmeden düz çizsin
        if (interior.Count == 0)
        {
            interior.Add(new WirePoint(Round((from.SheetX + to.SheetX) / 2), Round((from.SheetY + to.SheetY) / 2)));
        }

        return interior.Count > Project.MaxRoutePoints ? [.. interior.Take(Project.MaxRoutePoints)] : interior;
    }

    private static (string? Number, GraphicId? TextId) FindWireNumber(
        PageContext ctx,
        List<(double X, double Y)> path,
        HashSet<GraphicId> consumedByDevices,
        HashSet<GraphicId> claimedWireTextIds)
    {
        (string Number, GraphicId Id, double Dist)? best = null;
        foreach (var entry in ctx.Texts)
        {
            if (entry.Claimed || consumedByDevices.Contains(entry.Id) || claimedWireTextIds.Contains(entry.Id))
            {
                continue;
            }

            var t = entry.Text;
            var text = t.Text.Trim();
            if (t.SizeMm > 3.1 || text.Length == 0 || text.Length > 10 || text.Contains('.', StringComparison.Ordinal))
            {
                continue;
            }

            if (!text.All(c => char.IsDigit(c) || c == '-'))
            {
                continue;
            }

            var cx = Math.Abs(t.RotationDeg) > 45 ? t.X + (t.SizeMm * 0.35) : t.X + (text.Length * t.SizeMm * 0.25);
            var cy = Math.Abs(t.RotationDeg) > 45 ? t.Y - (text.Length * t.SizeMm * 0.25) : t.Y - (t.SizeMm * 0.35);
            var d = DistanceToPolyline(cx, cy, path);
            if (d <= 4.0 && (best is null || d < best.Value.Dist))
            {
                best = (text, entry.Id, d);
            }
        }

        return best is { } b ? (b.Number, b.Id) : (null, null);
    }

    private static string? MapWireColor(string? hex, string fromPin, string toPin)
    {
        if (fromPin.Equals("PE", StringComparison.OrdinalIgnoreCase) && toPin.Equals("PE", StringComparison.OrdinalIgnoreCase))
        {
            return "GNYE";
        }

        if (string.IsNullOrWhiteSpace(hex) || hex.Length != 7 || hex[0] != '#')
        {
            return null;
        }

        if (!byte.TryParse(hex[1..3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            || !byte.TryParse(hex[3..5], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            || !byte.TryParse(hex[5..7], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return null;
        }

        if (g > r + 40 && g > b + 40)
        {
            return "GNYE";
        }

        if (b > r + 40 && b > g + 20)
        {
            return "BU";
        }

        if (r > g + 50 && r > b + 50)
        {
            return "RD";
        }

        if (r > 180 && g is >= 70 and <= 170 && b < 60)
        {
            return "OG";
        }

        if (r > 90 && g < 110 && b < 70 && r > b + 40)
        {
            return "BN";
        }

        if (Math.Abs(r - g) < 25 && Math.Abs(g - b) < 25 && r is >= 80 and <= 180)
        {
            return "GY";
        }

        return null;
    }

    private static (string Tag, string? InlineManufacturer) NormalizeTag(string raw)
    {
        var trimmed = raw.Trim().TrimStart('-');
        string? inlineMfg = null;
        var spaceIdx = trimmed.IndexOf(' ', StringComparison.Ordinal);
        if (spaceIdx > 0)
        {
            var rest = trimmed[(spaceIdx + 1)..].Trim();
            trimmed = trimmed[..spaceIdx].Trim();
            if (ManufacturerKeywords.Any(k => rest.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                inlineMfg = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(rest.ToLowerInvariant());
            }
        }

        if (trimmed.Contains('+', StringComparison.Ordinal) || trimmed.Contains('=', StringComparison.Ordinal))
        {
            var dashIdx = trimmed.IndexOf('-', StringComparison.Ordinal);
            if (dashIdx >= 0 && dashIdx + 1 < trimmed.Length)
            {
                trimmed = trimmed[(dashIdx + 1)..];
            }
        }

        trimmed = new string([.. trimmed.Where(c => c is not ('=' or '+' or ':') && !char.IsWhiteSpace(c))]);
        return (trimmed, inlineMfg);
    }

    private static bool LooksLikeHeuristicTag(string raw, PageContext ctx, TextEntry entry)
    {
        var (tag, _) = NormalizeTag(raw);
        if (!HeuristicTagRegex().IsMatch(tag) || !LooksLikeGenericTag(tag))
        {
            return false;
        }

        if (OrderCodeRegex().IsMatch(tag) && ctx.Texts.Any(other =>
            other != entry
            && !other.Claimed
            && Math.Abs(other.Text.X - entry.Text.X) <= 30
            && other.Text.Y < entry.Text.Y
            && entry.Text.Y - other.Text.Y <= 12
            && LooksLikeGenericTag(other.Text.Text.Trim())))
        {
            return false;
        }

        return true;
    }

    private static bool LooksLikeGenericTag(string value)
    {
        var clean = value.Trim().TrimStart('-');
        if (clean.Length is < 2 or > 18)
        {
            return false;
        }

        if (PinNamePatternRegex().IsMatch(clean) || NumericOrCrossRefRegex().IsMatch(clean) || ElectricalValueRegex().IsMatch(clean))
        {
            return false;
        }

        return clean.Any(char.IsLetter) && clean.Any(char.IsDigit) && HeuristicTagRegex().IsMatch(clean);
    }

    private static bool IsPlausiblePinLabel(string text)
    {
        var raw = text.Trim();
        if (raw.Length is 0 or > 12 || raw.Contains(' ', StringComparison.Ordinal))
        {
            return false;
        }

        if (PinNamePatternRegex().IsMatch(raw))
        {
            return true;
        }

        // Kısa sayısal kontak/klemens uçları ("1", "2", "11", "14", "13", "21"...)
        return raw.All(char.IsDigit) && raw.Length <= 3;
    }

    private static string CleanPinName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var cleaned = new string([.. name.Trim().Where(c => !char.IsWhiteSpace(c) && c != ':')]);
        return cleaned;
    }

    private static FunctionKind InferFunctionKind(string tag, List<RecognizedPin> pins, bool isMotorCircle)
    {
        var names = pins.Select(p => p.Name.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
        if (isMotorCircle || (names.Contains("U1") && names.Contains("V1") && names.Contains("W1")))
        {
            return FunctionKind.Motor;
        }

        if (names.SetEquals(["A1", "A2"]))
        {
            return FunctionKind.Coil;
        }

        if (names.SetEquals(["11", "12"]) || names.SetEquals(["21", "22"]))
        {
            return FunctionKind.NcContact;
        }

        if (names.SetEquals(["13", "14"]) || names.SetEquals(["23", "24"]))
        {
            return FunctionKind.NoContact;
        }

        var letter = ExtractTagLetter(tag);
        if (letter is "F" or "Q" && pins.Count is >= 2 and <= 8)
        {
            return FunctionKind.Protection;
        }

        if (letter is "K" && pins.Count == 6)
        {
            return FunctionKind.PowerContact;
        }

        if (letter == "X" && pins.Count <= 2)
        {
            return FunctionKind.Terminal;
        }

        return FunctionKind.Generic;
    }

    private static PointAttributes InferPinAttributes(string pinName, bool isMotor)
    {
        var upper = pinName.ToUpperInvariant();
        if (upper == "PE")
        {
            return new PointAttributes(PinKind.ProtectiveEarth);
        }

        if (upper.StartsWith("CN", StringComparison.Ordinal) && upper.Contains('/', StringComparison.Ordinal))
        {
            return new PointAttributes(PinKind.Unspecified, ConnectorType.Unspecified, ContactType.Unspecified, upper[..upper.IndexOf('/', StringComparison.Ordinal)]);
        }

        if (upper.StartsWith("ETH", StringComparison.Ordinal) || upper == "RJ45")
        {
            return new PointAttributes(PinKind.Communication, ConnectorType.Rj45);
        }

        if (upper.StartsWith("TX", StringComparison.Ordinal) || upper.StartsWith("RX", StringComparison.Ordinal) || upper.StartsWith("CAN", StringComparison.Ordinal))
        {
            return new PointAttributes(PinKind.Communication);
        }

        if (upper is "DC+" or "DC-" or "24V" or "0V" or "GND" or "U" or "V" or "W" or "U1" or "V1" or "W1" or "L1" or "L2" or "L3" or "N")
        {
            return new PointAttributes(PinKind.Power);
        }

        return default;
    }

    private static string BuildLibrarySymbolName(RecognizedDevice dev)
    {
        var prefix = !string.IsNullOrWhiteSpace(dev.Part?.OrderCode) ? dev.Part.OrderCode : dev.Tag;
        return $"{prefix} ({dev.Pins.Count} uç)";
    }

    private static string? CategoryFor(RecognizedDevice dev) => dev.Kind switch
    {
        FunctionKind.Motor => "Motor ve aktüatör",
        FunctionKind.Coil or FunctionKind.PowerContact or FunctionKind.NoContact or FunctionKind.NcContact => "Röle ve kontaktör",
        FunctionKind.Protection => "Koruma",
        FunctionKind.Terminal => "Klemens ve konnektör",
        _ => dev.Pins.Count >= 16 ? "Otomasyon" : "Kumanda",
    };

    private static string? ExtractTagLetter(string tag)
    {
        var letters = new string([.. tag.Where(char.IsAsciiLetterUpper).Take(3)]);
        return letters.Length is >= 1 and <= 3 ? letters : null;
    }

    private static string SymbolSignature(SymbolDefinition s) =>
        string.Join('|', s.Pins.Select(p => $"{p.Name}@{p.X:0.#},{p.Y:0.#}")) + "#" + s.Shapes.Count + ":" + ShapeSpan(s);

    private static string ShapeSpan(SymbolDefinition s)
    {
        if (s.Shapes.Count == 0)
        {
            return "0";
        }

        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var shape in s.Shapes)
        {
            switch (shape)
            {
                case SymbolLine l:
                    minX = Math.Min(minX, Math.Min(l.X1, l.X2));
                    maxX = Math.Max(maxX, Math.Max(l.X1, l.X2));
                    minY = Math.Min(minY, Math.Min(l.Y1, l.Y2));
                    maxY = Math.Max(maxY, Math.Max(l.Y1, l.Y2));
                    break;
                case SymbolRect r:
                    minX = Math.Min(minX, r.X);
                    maxX = Math.Max(maxX, r.X + r.Width);
                    minY = Math.Min(minY, r.Y);
                    maxY = Math.Max(maxY, r.Y + r.Height);
                    break;
                case SymbolCircle c:
                    minX = Math.Min(minX, c.X - c.Radius);
                    maxX = Math.Max(maxX, c.X + c.Radius);
                    minY = Math.Min(minY, c.Y - c.Radius);
                    maxY = Math.Max(maxY, c.Y + c.Radius);
                    break;
            }
        }

        return minX > maxX ? "0" : $"{maxX - minX:0.#}x{maxY - minY:0.#}";
    }

    private static (ConnectionPointId A, ConnectionPointId B) OrderedPair(ConnectionPointId a, ConnectionPointId b) =>
        a.Value.CompareTo(b.Value) <= 0 ? (a, b) : (b, a);

    private static bool InsideOrOn(double x, double y, double x0, double y0, double x1, double y1, double pad) =>
        x >= x0 - pad && x <= x1 + pad && y >= y0 - pad && y <= y1 + pad;

    private static bool OutwardConsistent(int dx, int dy, double vx, double vy) =>
        ((dx * vx) + (dy * vy)) > 0.5;

    private static double OverlapRatio(RectCandidate a, RectCandidate b)
    {
        var ix0 = Math.Max(a.X, b.X);
        var iy0 = Math.Max(a.Y, b.Y);
        var ix1 = Math.Min(a.Right, b.Right);
        var iy1 = Math.Min(a.Bottom, b.Bottom);
        if (ix1 <= ix0 || iy1 <= iy0)
        {
            return 0;
        }

        var inter = (ix1 - ix0) * (iy1 - iy0);
        var minArea = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return minArea <= 0 ? 0 : inter / minArea;
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));

    private static double DistanceToPolyline(double px, double py, List<(double X, double Y)> path)
    {
        var best = double.MaxValue;
        for (var i = 1; i < path.Count; i++)
        {
            var (ax, ay) = path[i - 1];
            var (bx, by) = path[i];
            var (dx, dy) = (bx - ax, by - ay);
            var len2 = (dx * dx) + (dy * dy);
            var t = len2 < 1e-9 ? 0 : Math.Clamp((((px - ax) * dx) + ((py - ay) * dy)) / len2, 0, 1);
            best = Math.Min(best, Distance(px, py, ax + (t * dx), ay + (t * dy)));
        }

        return best;
    }

    private static double Round(double v) => Math.Round(v, 3) + 0.0;

    private static IEnumerable<List<Seg>> ClusterByValue(List<Seg> items, Func<Seg, double> keySelector, double tolerance)
    {
        var ordered = items.OrderBy(keySelector).ToList();
        if (ordered.Count == 0)
        {
            yield break;
        }

        var current = new List<Seg> { ordered[0] };
        var key = keySelector(ordered[0]);
        for (var i = 1; i < ordered.Count; i++)
        {
            var nextKey = keySelector(ordered[i]);
            if (nextKey - key <= tolerance)
            {
                current.Add(ordered[i]);
            }
            else
            {
                yield return current;
                current = [ordered[i]];
                key = nextKey;
            }
        }

        yield return current;
    }
}
