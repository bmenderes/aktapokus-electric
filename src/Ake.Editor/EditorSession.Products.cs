using System.Globalization;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Ürün kataloğu yönetimi, ürün seçimi ve cihaza atama, oturum tarafı (Faz 6b-4, ADR-0056).</summary>
public sealed partial class EditorSession
{
    /// <summary>Açık kütüphanedeki tanımlı ürün sayısı.</summary>
    public int LibraryProductCount => _library?.Count ?? 0;

    /// <summary>Kütüphanedeki ürün kategorileri (alfabetik, tekrarsız).</summary>
    public IReadOnlyList<string> ProductCategories()
    {
        if (_library is null)
        {
            return [];
        }

        return
        [
            .. _library.Search(string.Empty, limit: 2000)
                .Select(product => product.Category)
                .Where(category => !string.IsNullOrWhiteSpace(category))
                .Select(category => category!)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(category => category, StringComparer.Ordinal),
        ];
    }

    /// <summary>Ürün kataloğunda metin ve isteğe bağlı kategori ile arar.</summary>
    public IReadOnlyList<ProductSummary> SearchProducts(string text = "", string? category = null, int limit = 500)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (_library is null)
        {
            return [];
        }

        var matches = _library.Search(text.Trim(), limit);
        return string.IsNullOrWhiteSpace(category)
            ? matches
            : [.. matches.Where(product => string.Equals(product.Category, category, StringComparison.Ordinal))];
    }

    /// <summary>Sipariş kodu ve isteğe bağlı üretici adıyla tam ürün kaydını getirir.</summary>
    public LibraryProduct? FindLibraryProduct(string orderCode, string? manufacturer = null)
    {
        ArgumentNullException.ThrowIfNull(orderCode);
        if (_library is null)
        {
            return null;
        }

        var found = _library.FindProduct(orderCode, manufacturer);
        return found.IsSuccess ? found.Value : null;
    }

    /// <summary>
    /// Ürün kataloğundaki seçili ürünün tüm fonksiyonlarını (kütüphane sembolü varsa tüm iç yazıları ve çerçeveleriyle,
    /// yoksa parametrik sembolüyle) şematik önizleme sahnesine yan yana çizer; fonksiyon yoksa <c>null</c> döner.
    /// </summary>
    public Scene? PreviewOfProduct(ProductDefinition product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return BuildProductPreview(
            product,
            (template, fn) => ResolveLibrarySymbolFor(product, template) is { } custom
                ? (SymbolLibrary.FromDefinition(custom), custom.Shapes.OfType<SymbolText>().Any())
                : (SymbolLibrary.For(fn), false));
    }

    /// <summary>
    /// Ürün kataloğundaki seçili ürünün tüm fonksiyonlarını (bobin, kontaklar, pin uç adları) tek bir şematik önizleme
    /// sahnesine yan yana çizer; fonksiyon yoksa <c>null</c> döner.
    /// </summary>
    public static Scene? PreviewOf(ProductDefinition product)
    {
        ArgumentNullException.ThrowIfNull(product);
        return BuildProductPreview(product, null);
    }

    private static Scene? BuildProductPreview(
        ProductDefinition product,
        Func<FunctionTemplate, Function, (Symbol Symbol, bool HasInternalText)>? selector)
    {
        if (product.Functions.Count == 0)
        {
            return null;
        }

        const int maxShown = 6;
        const double gap = 12;
        const double margin = 6;
        const double captionSpace = 6;

        var shown = product.Functions.Take(maxShown).ToList();
        var entries = new List<(FunctionTemplate Template, Symbol Symbol, bool HasInternalText, double Width, double Height)>(shown.Count);
        foreach (var template in shown)
        {
            var fn = new Function(
                FunctionId.New(),
                template.Kind,
                template.ConnectionPoints.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)),
                template.PassThrough,
                template.Description,
                template.RatedCurrentA);
            var (symbol, hasInternalText) = selector is null
                ? (SymbolLibrary.For(fn), false)
                : selector(template, fn);
            var width = Math.Max(4.0, symbol.MaxX - symbol.MinX);
            var height = Math.Max(4.0, symbol.Height - symbol.MinY);
            entries.Add((template, symbol, hasInternalText, width, height));
        }

        var maxSymHeight = entries.Max(entry => entry.Height);
        var items = new List<SceneItem>();
        var cursorX = margin;

        for (var e = 0; e < entries.Count; e++)
        {
            var (template, sym, hasInternalText, symW, symH) = entries[e];
            var dx = cursorX - sym.MinX;
            var dy = margin + ((maxSymHeight - symH) / 2.0) - sym.MinY;

            foreach (var item in sym.Items)
            {
                items.Add(item switch
                {
                    LineItem line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
                    RectItem rect => rect with { X = rect.X + dx, Y = rect.Y + dy },
                    CircleItem circle => circle with { X = circle.X + dx, Y = circle.Y + dy },
                    TextItem text => text with { X = text.X + dx, Y = text.Y + dy, FontFamily = "Arial" },
                    _ => item,
                });
            }

            for (var i = 0; i < sym.Pins.Count; i++)
            {
                var pin = sym.Pins[i];
                var (px, py) = (pin.X + dx, pin.Y + dy);
                if (hasInternalText)
                {
                    items.Add(new LineItem(px, py, px + (pin.DirectionX * 1.4), py + (pin.DirectionY * 1.4), 0.28, "#C77800"));
                    continue;
                }

                items.Add(new LineItem(px, py, px + (pin.DirectionX * 2.5), py + (pin.DirectionY * 2.5), 0.3, "#C77800"));

                var pinLabel = i < template.ConnectionPoints.Count
                    ? template.ConnectionPoints[i]
                    : (i + 1).ToString(CultureInfo.InvariantCulture);
                var labelY = pin.DirectionY > 0 ? py + 2.2 : py - 0.8;
                var labelX = px + (pin.DirectionX < 0 ? -1.0 : 1.0);
                var align = pin.DirectionX < 0 ? TextAlign.Right : TextAlign.Left;
                items.Add(new TextItem(labelX, labelY, pinLabel, 1.7, "Arial", align, 0, "#2B2619"));
            }

            var rawCaption = template.Description ?? template.Kind.ToString();
            var caption = rawCaption.Length > 24 ? rawCaption[..24] + "…" : rawCaption;
            items.Add(new TextItem(
                cursorX + (symW / 2.0),
                margin + maxSymHeight + 4.2,
                caption,
                1.9,
                "Arial",
                TextAlign.Center,
                0,
                "#6B6455"));

            cursorX += symW + (e < entries.Count - 1 ? gap : 0);
        }

        var extraWidth = 0.0;
        if (product.Functions.Count > maxShown)
        {
            extraWidth = 12.0;
            items.Add(new TextItem(
                cursorX + 4.0,
                margin + (maxSymHeight / 2.0) + 1.0,
                $"+{product.Functions.Count - maxShown}",
                2.6,
                "Arial",
                TextAlign.Left,
                0,
                "#6B6455"));
        }

        var totalWidth = cursorX + margin + extraWidth;
        var totalHeight = margin + maxSymHeight + captionSpace + margin;
        return new Scene(totalWidth, totalHeight, items);
    }

    private SymbolDefinition? ResolveLibrarySymbolFor(ProductDefinition product, FunctionTemplate template)
    {
        if (_library is null)
        {
            return null;
        }

        if (template.SymbolId is { } symId && _library.FindSymbol(symId.ToString()) is { IsSuccess: true } byId)
        {
            return byId.Value;
        }

        var pinCount = template.ConnectionPoints.Count;
        foreach (var query in new[] { product.OrderCode, product.TypeDesignation })
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                continue;
            }

            foreach (var summary in _library.SearchSymbols(query.Trim(), pins: pinCount, limit: 20))
            {
                if (_library.FindSymbol(summary.Id.ToString()) is { IsSuccess: true } loaded)
                {
                    return loaded.Value;
                }
            }
        }

        if (pinCount > 0)
        {
            var orderFold = ComponentLibrary.FoldText(product.OrderCode);
            var typeFold = ComponentLibrary.FoldText(product.TypeDesignation ?? string.Empty);
            var descFold = ComponentLibrary.FoldText(template.Description ?? product.Description ?? string.Empty);

            SymbolDefinition? fallback = null;
            foreach (var summary in _library.SearchSymbols(string.Empty, pins: pinCount, limit: 60))
            {
                if (_library.FindSymbol(summary.Id.ToString()) is not { IsSuccess: true } loaded)
                {
                    continue;
                }

                var sym = loaded.Value;
                var devFold = ComponentLibrary.FoldText(sym.DeviceName ?? string.Empty);
                var nameFold = ComponentLibrary.FoldText(sym.Name);
                var matchesOrder = orderFold.Length > 0
                    && (devFold.Contains(orderFold, StringComparison.Ordinal) || nameFold.Contains(orderFold, StringComparison.Ordinal));
                var matchesType = typeFold.Length > 0
                    && (devFold.Contains(typeFold, StringComparison.Ordinal) || nameFold.Contains(typeFold, StringComparison.Ordinal));
                var matchesDesc = descFold.Length > 2
                    && !string.IsNullOrEmpty(devFold)
                    && string.Equals(devFold, descFold, StringComparison.Ordinal);
                if (matchesOrder || matchesType || matchesDesc)
                {
                    if (sym.Shapes.OfType<SymbolText>().Any())
                    {
                        return sym;
                    }

                    fallback ??= sym;
                }
            }

            if (fallback is not null)
            {
                return fallback;
            }
        }

        return null;
    }

    private SymbolDefinition? ResolveValidLibrarySymbol(ProductDefinition product, FunctionTemplate template, Function fn)
    {
        var symbol = ResolveLibrarySymbolFor(product, template);
        return symbol is not null && symbol.Check() is null && symbol.Pins.Count == fn.ConnectionPoints.Count
            ? symbol
            : null;
    }

    /// <summary>Kullanıcının JSON veya CSV ürün dosyasını açık kütüphaneye alır.</summary>
    public Result<ImportSummary> ImportProductsFromFile(string path, string? source = null, string? license = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_library is null)
        {
            return Result.Failure<ImportSummary>(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        var loaded = LibraryImport.FromFile(
            path,
            string.IsNullOrWhiteSpace(source) ? $"Dosyadan içe aktarıldı: {Path.GetFileName(path)}" : source.Trim(),
            string.IsNullOrWhiteSpace(license) ? "Kullanıcı verisi" : license.Trim());
        if (loaded.IsFailure)
        {
            return Result.Failure<ImportSummary>(loaded.Error!);
        }

        return _library.Import(loaded.Value);
    }

    /// <summary>Tek bir ürün tanımını açık kütüphaneye ekler veya günceller.</summary>
    public Result SaveProductToLibrary(LibraryProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);
        if (_library is null)
        {
            return Result.Failure(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        var imported = _library.Import([product]);
        return imported.IsFailure ? Result.Failure(imported.Error!) : Result.Success();
    }

    /// <summary>Kütüphanedeki bir ürünün üstverisini günceller.</summary>
    public Result UpdateLibraryProduct(string originalManufacturer, string originalOrderCode, LibraryProduct updated)
    {
        ArgumentNullException.ThrowIfNull(updated);
        if (_library is null)
        {
            return Result.Failure(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        return _library.UpdateProduct(originalManufacturer, originalOrderCode, updated);
    }

    /// <summary>Kütüphanedeki bir ürünün sembolünü Blok editöründe düzenlemek için başlangıç sembolünü döndürür.</summary>
    public (SymbolDefinition Start, bool OverwriteLibrary) StartingSymbolForProduct(LibraryProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        var def = product.Definition;
        var first = def.Functions.Count > 0 ? def.Functions[0] : null;
        if (first is not null && ResolveLibrarySymbolFor(def, first) is { } existing)
        {
            var isUserEditable = existing.License == "Kullanıcı çizimi"
                || (existing.Source?.StartsWith("Düzenlendi", StringComparison.Ordinal) == true);
            return (existing, isUserEditable);
        }

        if (first is not null)
        {
            var fn = new Function(
                FunctionId.New(),
                first.Kind,
                [.. first.ConnectionPoints.Select(name => new ConnectionPoint(ConnectionPointId.New(), name))],
                first.PassThrough,
                first.Description,
                first.RatedCurrentA);
            var builtIn = SymbolLibrary.For(fn);
            var pins = builtIn.Pins
                .Select((pin, i) => new SymbolPin(
                    pin.X,
                    pin.Y,
                    (int)pin.DirectionX,
                    (int)pin.DirectionY,
                    i < first.ConnectionPoints.Count ? first.ConnectionPoints[i] : null))
                .ToList();
            var created = new SymbolDefinition(
                SymbolId.New(),
                $"{def.Manufacturer} {def.OrderCode}",
                SymbolShapesFrom(builtIn),
                pins,
                "Kütüphane ürün editörü",
                "Kullanıcı çizimi",
                def.Description ?? def.OrderCode,
                TagLetterFor(def.Description ?? def.OrderCode));
            return (created, false);
        }

        var starter = new SymbolDefinition(
            SymbolId.New(),
            $"{def.Manufacturer} {def.OrderCode}",
            [
                new SymbolRect(0, 2.5, 20, 10, 0.35),
                new SymbolText(10, 8.5, def.OrderCode, 2.2, TextAlign.Center),
            ],
            [
                new SymbolPin(10, 0, 0, -1, "1"),
                new SymbolPin(10, 15, 0, 1, "2"),
            ],
            "Kütüphane ürün editörü",
            "Kullanıcı çizimi",
            def.Description ?? def.OrderCode,
            TagLetterFor(def.Description ?? def.OrderCode));
        return (starter, false);
    }

    /// <summary>Kütüphanedeki bir sembolü açık kütüphanedeki ürünün ilk fonksiyonuna bağlar.</summary>
    public Result LinkSymbolToProduct(string manufacturer, string orderCode, SymbolId symbolId)
    {
        if (_library is null)
        {
            return Result.Failure(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        var foundProduct = _library.FindProduct(orderCode, manufacturer);
        if (foundProduct.IsFailure)
        {
            return Result.Failure(foundProduct.Error!);
        }

        var foundSymbol = _library.FindSymbol(symbolId.ToString());
        if (foundSymbol.IsFailure)
        {
            return Result.Failure(foundSymbol.Error!);
        }

        var product = foundProduct.Value;
        var def = product.Definition;
        var sym = foundSymbol.Value;

        var newFunctions = def.Functions.ToList();
        var pinNames = sym.Pins
            .Select((p, i) => !string.IsNullOrWhiteSpace(p.Name)
                ? p.Name!
                : (newFunctions.Count > 0 && i < newFunctions[0].ConnectionPoints.Count
                    ? newFunctions[0].ConnectionPoints[i]
                    : (i + 1).ToString(CultureInfo.InvariantCulture)))
            .ToList();

        if (newFunctions.Count == 0)
        {
            if (pinNames.Count == 0)
            {
                pinNames = ["1", "2"];
            }

            newFunctions.Add(new FunctionTemplate(
                FunctionKind.Generic,
                pinNames,
                false,
                def.Description,
                null,
                symbolId));
        }
        else
        {
            var first = newFunctions[0];
            var effectivePoints = pinNames.Count > 0 ? pinNames : first.ConnectionPoints;
            newFunctions[0] = first with
            {
                ConnectionPoints = effectivePoints,
                SymbolId = symbolId,
            };
        }

        var updated = product with { Definition = def with { Functions = newFunctions } };
        return _library.UpdateProduct(manufacturer, orderCode, updated);
    }

    /// <summary>
    /// Kütüphanedeki bir üründen yeni cihaz oluşturur, varsa kütüphanedeki zengin sembollerini projeye kopyalayıp
    /// fonksiyonlara atar ve etkin (veya ilk) şematik sayfaya yerleştirir. Tek adımda geri alınır.
    /// </summary>
    public Result<IReadOnlyList<PlacementId>> PlaceLibraryProduct(
        string orderCode,
        string? manufacturer = null,
        int? pageIndex = null,
        double? x = null,
        double? y = null)
    {
        ArgumentNullException.ThrowIfNull(orderCode);
        if (Project is null)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(NoProject().Error!);
        }

        if (_library is null)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        var found = _library.FindProduct(orderCode, manufacturer);
        if (found.IsFailure)
        {
            return Result.Failure<IReadOnlyList<PlacementId>>(found.Error!);
        }

        int targetPageIndex;
        Page targetPage;
        if (pageIndex is { } explicitIndex)
        {
            var pageRes = PageAt(explicitIndex);
            if (pageRes.IsFailure)
            {
                return Result.Failure<IReadOnlyList<PlacementId>>(pageRes.Error!);
            }

            if (pageRes.Value.Kind != PageKind.Schematic)
            {
                return Result.Failure<IReadOnlyList<PlacementId>>(new Error(
                    "sayfa.sema_degil",
                    "Cihaz yalnızca şema sayfasına yerleştirilebilir; lütfen bir şema sayfası açın."));
            }

            targetPageIndex = explicitIndex;
            targetPage = pageRes.Value;
        }
        else
        {
            targetPageIndex = FirstSchematicPageIndex(Project);
            if (targetPageIndex < 0)
            {
                return Result.Failure<IReadOnlyList<PlacementId>>(new Error(
                    "sayfa.sema_yok",
                    "Projede yerleştirme yapılacak şema sayfası yok."));
            }

            targetPage = Project.Pages[targetPageIndex];
        }

        var product = found.Value;
        var def = product.Definition;
        var cleanedTag = NextProductTagFor(Project, product);
        var device = def.CreateDevice(cleanedTag);
        IReadOnlyList<FunctionTemplate> templates = def.Functions;

        if (device.Functions.Count == 0)
        {
            var fallbackFunction = new Function(
                FunctionId.New(),
                FunctionKind.Generic,
                [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "2")],
                description: def.Description);
            device = new Device(
                device.Id,
                device.ProductTag,
                [fallbackFunction],
                device.Kind,
                device.FunctionNodeId,
                device.LocationNodeId,
                device.Description,
                device.Part,
                device.Mounting);
            templates = [new FunctionTemplate(FunctionKind.Generic, ["1", "2"], false, def.Description)];
        }

        var commands = new List<ICommand> { new AddDevice(device) };
        var addedSymbols = new HashSet<SymbolId>();
        var placements = new List<Placement>(device.Functions.Count);
        var area = DrawingAreaFor(targetPageIndex) ?? new Box(0, 0, 297, 210);
        var existingCount = Project.Placements.Count(p => p.PageId == targetPage.Id);

        double baseX;
        double baseY;
        var firstSymbol = ResolveValidLibrarySymbol(def, templates[0], device.Functions[0]);
        if (x is { } dropX && y is { } dropY)
        {
            (baseX, baseY) = SnapPoint(dropX, dropY, targetPageIndex);
        }
        else
        {
            var minSymX = firstSymbol is null ? 0.0 : SymbolLibrary.FromDefinition(firstSymbol).MinX;
            var minSymY = firstSymbol is null ? 0.0 : SymbolLibrary.FromDefinition(firstSymbol).MinY;
            var col = existingCount % 3;
            var row = (existingCount / 3) % 4;
            var rawX = Math.Max(15.0, 25.0 - minSymX) + (col * 45.0);
            var rawY = Math.Max(20.0, 30.0 - minSymY) + (row * 35.0);
            if (rawX > Math.Max(20.0, area.Width - 60.0))
            {
                rawX = Math.Max(15.0, 25.0 - minSymX);
            }

            if (rawY > Math.Max(25.0, area.Height - 50.0))
            {
                rawY = Math.Max(20.0, 30.0 - minSymY);
            }

            (baseX, baseY) = Snap(rawX, rawY);
        }

        var cursorX = baseX;
        for (var i = 0; i < device.Functions.Count; i++)
        {
            var fn = device.Functions[i];
            var tpl = i < templates.Count ? templates[i] : null;
            var libSymbol = tpl is null ? null : ResolveValidLibrarySymbol(def, tpl, fn);

            double widthMm;
            if (libSymbol is not null)
            {
                if (Project.FindSymbol(libSymbol.Id) is null && addedSymbols.Add(libSymbol.Id))
                {
                    commands.Add(new PutSymbol(libSymbol));
                }

                commands.Add(new AssignSymbol(fn.Id, libSymbol.Id));
                var sym = SymbolLibrary.FromDefinition(libSymbol);
                widthMm = Math.Max(10.0, sym.MaxX - sym.MinX);
            }
            else
            {
                var sym = SymbolLibrary.For(fn);
                widthMm = Math.Max(10.0, sym.MaxX - sym.MinX);
            }

            if (i > 0 && cursorX > Math.Max(40.0, area.Width - 30.0))
            {
                cursorX = baseX;
                baseY += 35.0;
            }

            var (px, py) = Snap(cursorX, baseY);
            var placement = new Placement(PlacementId.New(), targetPage.Id, fn.Id, PlacementRole.Main, px, py);
            commands.Add(new AddPlacement(placement));
            placements.Add(placement);
            cursorX += Math.Max(20.0, widthMm + 10.0);
        }

        var executed = Execute(new Transaction($"Sayfaya cihaz eklendi: -{cleanedTag}", commands));
        return executed.IsFailure
            ? Result.Failure<IReadOnlyList<PlacementId>>(executed.Error!)
            : Result.Success<IReadOnlyList<PlacementId>>([.. placements.Select(p => p.Id)]);
    }

    internal static int FirstSchematicPageIndex(Project project)
    {
        for (var i = 0; i < project.Pages.Count; i++)
        {
            if (project.Pages[i].Kind == PageKind.Schematic)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Kütüphanedeki bir üründen tüm fonksiyon ve bağlantı noktalarıyla yeni bir cihaz oluşturup projeye ekler.
    /// Tag boşsa kategorisine uygun sıradaki boş tag atanır.
    /// </summary>
    public Result<DeviceId> CreateDeviceFromLibraryProduct(
        string orderCode, string? manufacturer = null, string? productTag = null)
    {
        ArgumentNullException.ThrowIfNull(orderCode);
        if (Project is null)
        {
            return Result.Failure<DeviceId>(NoProject().Error!);
        }

        if (_library is null)
        {
            return Result.Failure<DeviceId>(new Error("kutuphane.acik_degil", "Kütüphane açık değil."));
        }

        var found = _library.FindProduct(orderCode, manufacturer);
        if (found.IsFailure)
        {
            return Result.Failure<DeviceId>(found.Error!);
        }

        var product = found.Value;
        var cleanedTag = string.IsNullOrWhiteSpace(productTag)
            ? NextProductTagFor(Project, product)
            : productTag.Trim().TrimStart('-');
        var device = product.Definition.CreateDevice(cleanedTag);
        var added = Execute(new AddDevice(device));
        return added.IsFailure ? Result.Failure<DeviceId>(added.Error!) : Result.Success(device.Id);
    }

    private static string NextProductTagFor(Project project, LibraryProduct product)
    {
        var category = ComponentLibrary.FoldText(product.Category ?? string.Empty);
        var letter = category.Contains("koruma", StringComparison.Ordinal) || category.Contains("sigorta", StringComparison.Ordinal) ? "F"
            : category.Contains("kontaktor", StringComparison.Ordinal) || category.Contains("role", StringComparison.Ordinal) ? "K"
            : category.Contains("sensor", StringComparison.Ordinal) ? "B"
            : category.Contains("guc kaynagi", StringComparison.Ordinal) ? "G"
            : category.Contains("klemens", StringComparison.Ordinal) ? "X"
            : category.Contains("kumanda", StringComparison.Ordinal) ? "S"
            : category.Contains("sinyal", StringComparison.Ordinal) ? "P"
            : category.Contains("plc", StringComparison.Ordinal) || category.Contains("haberlesme", StringComparison.Ordinal) ? "A"
            : category.Contains("pnomatik", StringComparison.Ordinal) ? "Y"
            : TagLetterFor(product.Definition.Description ?? product.Definition.OrderCode);

        var index = 1;
        while (project.Devices.Any(device => device.ProductTag == $"{letter}{index.ToString(CultureInfo.InvariantCulture)}"))
        {
            index++;
        }

        return $"{letter}{index.ToString(CultureInfo.InvariantCulture)}";
    }
}
