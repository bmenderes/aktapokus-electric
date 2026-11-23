namespace Ake.Core.Model;

public enum DeviceKind
{
    Generic,
    TerminalStrip,

    /// <summary>Telin ortasında iki ya da daha çok telin buluştuğu nokta (ADR-0032); raporlarda ve listelerde cihaz sayılmaz.</summary>
    Junction,
}

public enum FunctionKind
{
    Generic,
    Coil,
    NoContact,
    NcContact,
    PowerContact,
    Terminal,
    Protection,
    Motor,
    PowerSupply,
    PlcChannel,
    Junction,
}

/// <summary>Klemensin türü (ADR-0040).</summary>
public enum TerminalRole
{
    /// <summary>Sıradan klemens.</summary>
    Normal,

    /// <summary>Koruma topraklaması (PE) klemensi.</summary>
    ProtectiveEarth,

    /// <summary>Sonlandırıcı (uç levha): elektriksel bağlantısı yoktur, yalnızca şeridin sonunu kapatır; bağlantı noktası taşımaz.</summary>
    EndCover,
}

public enum MountingType
{
    DinRail35,
    Plate,
    Door,
}

/// <summary>Üretici ürün bilgisi.</summary>
public sealed record PartRef(string Manufacturer, string OrderCode, string? TypeDesignation = null);

/// <summary>Pano yerleşimi ve 3D için montaj bilgisi (Faz 8 kullanır).</summary>
public sealed record Mounting(
    MountingType Type,
    double WidthMm,
    double HeightMm,
    double DepthMm,
    double ClearanceMm = 0,
    double HeatLossW = 0,
    string? StepRef = null);

/// <summary>Function'a ait, isimli bağlantı noktası (A1, 13, U1…).</summary>
public sealed class ConnectionPoint(ConnectionPointId id, string name, PointAttributes attributes = default)
{
    public ConnectionPointId Id { get; } = id;

    public string Name { get; internal set; } = name;

    /// <summary>Uç türü, bağlantı tipi, kontak türü (ADR-0033); verilmediyse hepsi "belirtilmemiş".</summary>
    public PointAttributes Attributes { get; internal set; } = attributes;
}

/// <summary>
/// Device'ın elektriksel bir parçası: bobin, kontak, klemens. Şemada çizilen ve bağlanan şey budur.
/// </summary>
public sealed class Function(
    FunctionId id,
    FunctionKind kind,
    IEnumerable<ConnectionPoint> connectionPoints,
    bool passThrough = false,
    string? description = null,
    decimal? ratedCurrentA = null,
    SymbolId? symbolId = null,
    int level = 1,
    TerminalRole role = TerminalRole.Normal)
{
    public const int MaxLevel = 3;

    /// <summary>Bir klemensin tarafı başına en çok bağlantı sayısı (çok bağlantılı klemens, ADR-0040).</summary>
    public const int MaxWays = 4;

    /// <summary>Klemens türü (PE, sonlandırıcı); klemens dışındaki function'larda her zaman <see cref="TerminalRole.Normal"/>.</summary>
    public TerminalRole Role { get; internal set; } = role;

    /// <summary>
    /// Klemensin taraf başına bağlantı sayısı: noktalar önce iç taraf, sonra dış taraf olarak sıralıdır, yani nokta sayısının yarısı.
    /// Sonlandırıcıda ve klemens olmayan function'larda 0.
    /// </summary>
    public int Ways => Kind == FunctionKind.Terminal && Role != TerminalRole.EndCover ? _connectionPoints.Count / 2 : 0;

    private readonly List<ConnectionPoint> _connectionPoints = [.. connectionPoints];

    /// <summary>
    /// Çok katlı klemenste klemensin katı (1–3; ADR-0034). Aynı ada sahip farklı katlar elektriksel olarak ayrı function'lardır.
    /// Klemens dışındaki function'larda her zaman 1.
    /// </summary>
    public int Level { get; internal set; } = level;

    /// <summary>
    /// Function'ın çizileceği kullanıcı sembolü. Boşsa türüne göre yerleşik parametrik sembol kullanılır.
    /// </summary>
    public SymbolId? SymbolId { get; internal set; } = symbolId;

    /// <summary>Koruma elemanının anma akımı (A). ERC kesit/koruma kuralı kullanır.</summary>
    public decimal? RatedCurrentA { get; internal set; } = ratedCurrentA;

    public FunctionId Id { get; } = id;

    public FunctionKind Kind { get; internal set; } = kind;

    /// <summary>
    /// Doğruysa bağlantı noktaları elektriksel olarak aynıdır (klemens, bara, köprü) ve potansiyel
    /// içinden geçer. Kontak, sigorta ve şalterde yanlıştır.
    /// </summary>
    public bool PassThrough { get; internal set; } = passThrough;

    public string? Description { get; internal set; } = description;

    public IReadOnlyList<ConnectionPoint> ConnectionPoints => _connectionPoints;
}

/// <summary>Fiziksel ürün (-K1). BOM'da sayılan şey budur.</summary>
public sealed class Device(
    DeviceId id,
    string productTag,
    IEnumerable<Function> functions,
    DeviceKind kind = DeviceKind.Generic,
    StructureNodeId? functionNodeId = null,
    StructureNodeId? locationNodeId = null,
    string? description = null,
    PartRef? part = null,
    Mounting? mounting = null,
    string? address = null,
    IReadOnlyDictionary<string, string>? properties = null)
{
    private readonly List<Function> _functions = [.. functions];
    private readonly SortedDictionary<string, string> _properties = InitializeProperties(properties);

    /// <summary>Ağ adresi (IP, ör. 192.168.0.50); haberleşme şemasında cihazın altında görünür (ADR-0037). Boşsa yok.</summary>
    public string? Address { get; internal set; } = address;

    public DeviceId Id { get; } = id;

    /// <summary>Ürün aspect'i, <c>-</c> işareti olmadan (K1).</summary>
    public string ProductTag { get; internal set; } = productTag;

    public DeviceKind Kind { get; internal set; } = kind;

    public StructureNodeId? FunctionNodeId { get; internal set; } = functionNodeId;

    public StructureNodeId? LocationNodeId { get; internal set; } = locationNodeId;

    public string? Description { get; internal set; } = description;

    public PartRef? Part { get; internal set; } = part;

    public Mounting? Mounting { get; internal set; } = mounting;

    /// <summary>Kullanıcı tanımlı veya katalog/liste içe aktarmadan gelen özel cihaz özellikleri (ADR-0058).</summary>
    public IReadOnlyDictionary<string, string> Properties => _properties;

    internal SortedDictionary<string, string> PropertyMap => _properties;

    public IReadOnlyList<Function> Functions => _functions;

    internal List<Function> FunctionList => _functions;

    private static SortedDictionary<string, string> InitializeProperties(IReadOnlyDictionary<string, string>? source)
    {
        var map = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (source is null)
        {
            return map;
        }

        foreach (var (key, value) in source)
        {
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                map[key.Trim()] = value.Trim();
            }
        }

        return map;
    }
}
