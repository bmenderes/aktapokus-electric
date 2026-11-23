using System.Globalization;
using System.Text.RegularExpressions;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Numbering;

namespace Ake.Core.Macros;

/// <summary>Makrodaki bir sembolün yeri, makronun sol üst köşesine göre (mm).</summary>
public sealed record MacroPlacement(FunctionId FunctionId, double X, double Y, int RotationDeg = 0);

/// <summary>Makro yerleştirilirken seçilebilen hazır değer kümesi (ör. güç/akım seçenekleri; ADR-0058).</summary>
public sealed record MacroValueSet(string Name, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Tekrar kullanılabilir devre parçası (ADR-0025): cihazlar, aralarındaki bağlantılar, sembollerin göreli
/// yerleri ve kullandıkları kullanıcı sembolleri. Cihazlar yapı düğümü taşımaz; yerleştirildiği sayfanınkini
/// alır. Kimlikler yalnızca makronun içindedir; her yerleştirmede yenileri üretilir.
/// </summary>
public sealed record Macro(
    string Name,
    IReadOnlyList<Device> Devices,
    IReadOnlyList<Connection> Connections,
    IReadOnlyList<MacroPlacement> Placements,
    IReadOnlyList<SymbolDefinition> Symbols,
    string? Description = null,
    IReadOnlyList<MacroValueSet>? ValueSets = null)
{
    /// <summary>Yerleşim noktalarının kapladığı genişlik ve yükseklik (mm).</summary>
    public (double Width, double Height) Extent =>
        Placements.Count == 0 ? (0, 0) : (Placements.Max(p => p.X), Placements.Max(p => p.Y));

    /// <summary>Makronun kendi içinde tutarlı olup olmadığı: bağlantı ve yerleşimler makrodaki cihazlara gider.</summary>
    public Error? Check()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return new Error("makro.ad_yok", "Makronun adı boş olamaz.");
        }

        if (Devices.Count == 0)
        {
            return new Error("makro.bos", "Makroda cihaz yok.");
        }

        var points = Devices.SelectMany(d => d.Functions).SelectMany(f => f.ConnectionPoints).Select(p => p.Id).ToHashSet();
        if (Connections.FirstOrDefault(c => !points.Contains(c.From) || !points.Contains(c.To)) is { } loose)
        {
            return new Error("makro.baglanti_disarida", $"Bağlantı makro dışındaki bir uca gidiyor ({loose.Id}).");
        }

        var functions = Devices.SelectMany(d => d.Functions).ToDictionary(f => f.Id);
        if (Placements.FirstOrDefault(p => !functions.ContainsKey(p.FunctionId)) is { } stray)
        {
            return new Error("makro.yerlesim_disarida", $"Yerleşim makroda olmayan bir function'ı gösteriyor ({stray.FunctionId}).");
        }

        var symbols = Symbols.Select(s => s.Id).ToHashSet();
        if (functions.Values.FirstOrDefault(f => f.SymbolId is { } id && !symbols.Contains(id)) is { } missing)
        {
            return new Error("makro.sembol_yok", $"Function'ın kullanıcı sembolü makroda yok ({missing.SymbolId}).");
        }

        return null;
    }
}

/// <summary>Makro yakalama ve yerleştirme. Modeli değiştirmez; yerleştirme çalıştırılacak bir işlem üretir.</summary>
public static partial class MacroService
{
    [GeneratedRegex(@"^(?<lead>.*?)(?<number>\d+)$")]
    private static partial Regex TrailingNumber();

    /// <summary>
    /// Sayfadan makro yakalar. <paramref name="area"/> verilirse yalnızca ana görünümü bu dikdörtgende
    /// (sayfa mm) duran cihazlar alınır; verilmezse sayfadaki bütün cihazlar. Bir cihazın sayfa dışındaki
    /// function'ları makroya girer ama yerleşimsiz kalır. Bağlantılar iki ucu da makrodaysa alınır; tel
    /// numarası ve kablo damarı bağı düşer.
    /// </summary>
    public static Result<Macro> Capture(
        Project project, PageId pageId, string name, (double X, double Y, double Width, double Height)? area = null) =>
        CaptureWhere(project, pageId, name, p =>
            area is not { } box || (p.X >= box.X && p.X <= box.X + box.Width && p.Y >= box.Y && p.Y <= box.Y + box.Height));

    /// <summary>Seçili yerleşimlerden makro (kopyalama); kurallar alanla yakalamadakiyle aynı.</summary>
    public static Result<Macro> Capture(Project project, PageId pageId, string name, IReadOnlyCollection<PlacementId> placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var wanted = placements.ToHashSet();
        return CaptureWhere(project, pageId, name, p => wanted.Contains(p.Id));
    }

    private static Result<Macro> CaptureWhere(Project project, PageId pageId, string name, Func<Placement, bool> include)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(name);

        if (project.Pages.All(page => page.Id != pageId))
        {
            return Result.Failure<Macro>(new Error("sayfa.yok", $"Sayfa modelde yok ({pageId})."));
        }

        var placed = project.Placements
            .Where(p => p.PageId == pageId && p.Role == PlacementRole.Main && include(p))
            .ToList();
        var functionToDevice = project.Devices
            .SelectMany(d => d.Functions.Select(f => (Function: f.Id, Device: d)))
            .ToDictionary(entry => entry.Function, entry => entry.Device);
        var devices = project.Devices.Where(d => placed.Any(p => functionToDevice[p.FunctionId] == d)).ToList();
        if (devices.Count == 0)
        {
            return Result.Failure<Macro>(new Error("makro.bos", "Seçilen yerde makroya alınacak sembol yok."));
        }

        var points = devices.SelectMany(d => d.Functions).SelectMany(f => f.ConnectionPoints).Select(p => p.Id).ToHashSet();
        var connections = project.Connections
            .Where(c => points.Contains(c.From) && points.Contains(c.To))
            .ToList();

        var originX = placed.Min(p => p.X);
        var originY = placed.Min(p => p.Y);
        // Köşe noktaları makronun sol üst köşesine göre tutulur (yerleştirirken yeni yere kayar).
        connections = [.. connections.Select(c => new Connection(
            c.Id,
            c.From,
            c.To,
            c.Wire is { } wire ? new Wire(wire.Id, null, wire.CrossSectionMm2, wire.Color) : null,
            null,
            c.Route.Select(point => new WirePoint(point.X - originX, point.Y - originY))))];
        var placements = placed
            .Select(p => new MacroPlacement(p.FunctionId, p.X - originX, p.Y - originY, p.RotationDeg))
            .ToList();

        var symbolIds = devices.SelectMany(d => d.Functions).Select(f => f.SymbolId).OfType<SymbolId>().ToHashSet();
        var symbols = project.Symbols.Where(s => symbolIds.Contains(s.Id)).ToList();

        // Cihazlar yapı düğümü taşımaz: yerleştirildiği sayfanınkini alır.
        // Klemens şeridinden yalnızca alandaki klemensler alınır; diğer cihazlar bütün function'larıyla gelir.
        var placedFunctions = placed.Select(p => p.FunctionId).ToHashSet();
        var templates = devices
            .Select(d => Copy(
                d,
                d.Id,
                d.ProductTag,
                null,
                null,
                id => id,
                (f, _) => f.Id,
                p => p.Id,
                d.Kind == DeviceKind.TerminalStrip ? f => placedFunctions.Contains(f.Id) : null))
            .ToList();
        var kept = templates.SelectMany(d => d.Functions).SelectMany(f => f.ConnectionPoints).Select(p => p.Id).ToHashSet();
        connections.RemoveAll(c => !kept.Contains(c.From) || !kept.Contains(c.To));
        var macro = new Macro(name.Trim(), templates, connections, placements, symbols);
        return macro.Check() is { } error ? Result.Failure<Macro>(error) : Result.Success(macro);
    }

    /// <summary>
    /// Makroyu sayfaya yerleştiren işlem: kullanıcı sembolleri (projede yoksa), cihazlar, bağlantılar ve
    /// yerleşimler; hepsi tek adımda geri alınır. Kimlikler yenidir. Cihazlar sayfanın yapı düğümlerini
    /// alır; tag'ler projenin tag şemasına göre o harfteki en büyük numaradan devam eder.
    /// </summary>
    public static Result<Transaction> Insert(Project project, Macro macro, PageId pageId, double x, double y, string? valueSetName = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(macro);

        if (macro.Check() is { } invalid)
        {
            return Result.Failure<Transaction>(invalid);
        }

        if (project.Pages.FirstOrDefault(p => p.Id == pageId) is not { } page)
        {
            return Result.Failure<Transaction>(new Error("sayfa.yok", $"Sayfa modelde yok ({pageId})."));
        }

        MacroValueSet? selectedSet = null;
        if (!string.IsNullOrWhiteSpace(valueSetName))
        {
            selectedSet = macro.ValueSets?.FirstOrDefault(set => string.Equals(set.Name, valueSetName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (selectedSet is null)
            {
                return Result.Failure<Transaction>(new Error("makro.deger_kumesi_yok", $"Makroda '{valueSetName}' değer kümesi yok."));
            }
        }

        var commands = new List<ICommand>();
        foreach (var symbol in macro.Symbols.Where(s => project.FindSymbol(s.Id) is null))
        {
            commands.Add(new PutSymbol(symbol));
        }

        var tags = new TagAllocator(project, page);
        var functionIds = new Dictionary<FunctionId, FunctionId>();
        var pointIds = new Dictionary<ConnectionPointId, ConnectionPointId>();
        foreach (var template in macro.Devices)
        {
            var device = Copy(
                template,
                DeviceId.New(),
                tags.Next(template.ProductTag),
                page.FunctionNodeId,
                page.LocationNodeId,
                symbol => symbol,
                (function, _) => functionIds[function.Id] = FunctionId.New(),
                point => pointIds[point.Id] = ConnectionPointId.New());
            if (selectedSet is not null)
            {
                ApplyValueSet(device, template.ProductTag, selectedSet.Values);
            }

            commands.Add(new AddDevice(device));
        }

        foreach (var connection in macro.Connections)
        {
            commands.Add(new Connect(new Connection(
                ConnectionId.New(),
                pointIds[connection.From],
                pointIds[connection.To],
                connection.Wire is { } wire ? new Wire(WireId.New(), null, wire.CrossSectionMm2, wire.Color) : null,
                null,
                connection.Route.Select(point => new WirePoint(x + point.X, y + point.Y)))));
        }

        foreach (var placement in macro.Placements)
        {
            commands.Add(new AddPlacement(new Placement(
                PlacementId.New(),
                pageId,
                functionIds[placement.FunctionId],
                PlacementRole.Main,
                x + placement.X,
                y + placement.Y,
                placement.RotationDeg)));
        }

        return Result.Success(new Transaction($"Makro yerleştirildi: {macro.Name}", commands));
    }

    /// <summary>
    /// Konum verilmediğinde makronun sol üst köşesi: sayfadaki sembollerin en solunun hizasında, en alttakinin
    /// 40 mm altında; boş sayfada (30, 40). Kullanıcı sonra taşır.
    /// </summary>
    public static (double X, double Y) DefaultSpot(Project project, PageId pageId)
    {
        ArgumentNullException.ThrowIfNull(project);

        var onPage = project.Placements.Where(p => p.PageId == pageId).ToList();
        return onPage.Count == 0 ? (30, 40) : (onPage.Min(p => p.X), onPage.Max(p => p.Y) + 40);
    }

    private static Device Copy(
        Device source,
        DeviceId id,
        string tag,
        StructureNodeId? functionNode,
        StructureNodeId? locationNode,
        Func<SymbolId?, SymbolId?> symbol,
        Func<Function, int, FunctionId> functionId,
        Func<ConnectionPoint, ConnectionPointId> pointId,
        Func<Function, bool>? keep = null) =>
        new(
            id,
            tag,
            source.Functions.Where(function => keep?.Invoke(function) ?? true).Select((function, index) => new Function(
                functionId(function, index),
                function.Kind,
                function.ConnectionPoints.Select(point => new ConnectionPoint(pointId(point), point.Name, point.Attributes)).ToList(),
                function.PassThrough,
                function.Description,
                function.RatedCurrentA,
                symbol(function.SymbolId),
                function.Level,
                function.Role)).ToList(),
            source.Kind,
            functionNode,
            locationNode,
            source.Description,
            source.Part,
            source.Mounting,
            source.Address,
            source.Properties);

    private static void ApplyValueSet(Device device, string templateTag, IReadOnlyDictionary<string, string> values)
    {
        var prefix = templateTag + ".";
        foreach (var (key, value) in values)
        {
            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var field = key[prefix.Length..];
            if (field.Equals("description", StringComparison.OrdinalIgnoreCase))
            {
                device.Description = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
            else if (field.Equals("manufacturer", StringComparison.OrdinalIgnoreCase))
            {
                device.Part = (device.Part ?? new PartRef(value.Trim(), string.Empty)) with { Manufacturer = value.Trim() };
            }
            else if (field.Equals("orderCode", StringComparison.OrdinalIgnoreCase))
            {
                device.Part = (device.Part ?? new PartRef(string.Empty, value.Trim())) with { OrderCode = value.Trim() };
            }
            else if (field.Equals("typeDesignation", StringComparison.OrdinalIgnoreCase))
            {
                if (device.Part is { } part)
                {
                    device.Part = part with { TypeDesignation = string.IsNullOrWhiteSpace(value) ? null : value.Trim() };
                }
            }
            else if (field.Equals("ratedCurrentA", StringComparison.OrdinalIgnoreCase)
                && decimal.TryParse(value.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var current))
            {
                foreach (var fn in device.Functions)
                {
                    fn.RatedCurrentA = current;
                }
            }
            else if (field.StartsWith("prop.", StringComparison.OrdinalIgnoreCase))
            {
                var propKey = field["prop.".Length..].Trim();
                if (propKey.Length > 0 && !string.IsNullOrWhiteSpace(value))
                {
                    device.PropertyMap[propKey] = value.Trim();
                }
            }
        }
    }

    /// <summary>
    /// Yeni tag üretir: harfi (K, F…) tag'den okunur; numara, hedef kapsamda (aynı yapı düğümleri; sayfa
    /// şemasında aynı sayfa) o harfteki en büyük numaranın bir fazlası. Harfi okunamayan tag (PE) korunur.
    /// </summary>
    private sealed class TagAllocator(Project project, Page page)
    {
        private readonly List<string> _pageNumbers = [.. project.Pages.Select(p => p.Number)];
        private readonly HashSet<string> _used = [.. project.Devices
            .Where(d => d.FunctionNodeId == page.FunctionNodeId && d.LocationNodeId == page.LocationNodeId)
            .Select(d => d.ProductTag)];

        public string Next(string templateTag)
        {
            if (Renumbering.PrefixOf(templateTag, _pageNumbers) is not { } prefix)
            {
                return templateTag;
            }

            var lead = project.TagScheme == TagScheme.PageLetterNumber ? page.Number + prefix : prefix;
            var highest = _used
                .Select(tag => TrailingNumber().Match(tag))
                .Where(match => match.Success && match.Groups["lead"].Value == lead)
                .Select(match => int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .Max();
            var tag = lead + (highest + 1).ToString(CultureInfo.InvariantCulture);
            _used.Add(tag);
            return tag;
        }
    }
}
