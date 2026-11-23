namespace Ake.Core.Model;

public enum PotentialKind
{
    Phase,
    Neutral,
    ProtectiveEarth,
    DcPositive,
    DcNegative,
    Other,
}

/// <summary>Fiziksel tel. Bir Connection'a aittir.</summary>
public sealed class Wire(WireId id, string? number = null, decimal? crossSectionMm2 = null, string? color = null)
{
    public WireId Id { get; } = id;

    public string? Number { get; internal set; } = number;

    public decimal? CrossSectionMm2 { get; internal set; } = crossSectionMm2;

    public string? Color { get; internal set; } = color;
}

/// <summary>Telin geçeceği köşe noktası: sayfa koordinatı, mm, y aşağı doğru (ADR-0029).</summary>
public readonly record struct WirePoint(double X, double Y);

/// <summary>
/// İki bağlantı noktası arasındaki mantıksal bağ. Ortamı ya <see cref="Wire"/>, ya bir kablo damarı
/// (<see cref="CoreId"/>), ya da hiçbiridir (köprü, bara); ikisi birden olamaz.
/// </summary>
public sealed class Connection(
    ConnectionId id,
    ConnectionPointId from,
    ConnectionPointId to,
    Wire? wire = null,
    CableCoreId? coreId = null,
    IEnumerable<WirePoint>? route = null,
    ConnectionKindId? kindId = null)
{
    public ConnectionId Id { get; } = id;

    /// <summary>Bağlantı türü (renk ve çizgi biçimi; ADR-0036); atanmamışsa tel renginden ya da siyah.</summary>
    public ConnectionKindId? KindId { get; internal set; } = kindId;

    public ConnectionPointId From { get; } = from;

    public ConnectionPointId To { get; } = to;

    public Wire? Wire { get; internal set; } = wire;

    public CableCoreId? CoreId { get; internal set; } = coreId;

    /// <summary>
    /// Kullanıcının verdiği köşe noktaları (ADR-0029). Boşsa tel otomatik yönlendirilir. Doluysa tel uçtan çıkıp sırayla bu
    /// noktalardan (dik parçalarla) geçerek diğer uca varır. Noktalar iki ucun çizildiği sayfanın koordinatıdır.
    /// </summary>
    public IReadOnlyList<WirePoint> Route { get; internal set; } = [.. route ?? []];
}

/// <summary>Çok damarlı kablonun bir damarı.</summary>
public sealed class CableCore(
    CableCoreId id,
    string designation,
    decimal? crossSectionMm2 = null,
    string? color = null)
{
    public CableCoreId Id { get; } = id;

    public string Designation { get; internal set; } = designation;

    public decimal? CrossSectionMm2 { get; internal set; } = crossSectionMm2;

    public string? Color { get; internal set; } = color;
}

/// <summary>Çok damarlı kablo (-W1).</summary>
public sealed class Cable(CableId id, string productTag, IEnumerable<CableCore> cores, string? type = null)
{
    private readonly List<CableCore> _cores = [.. cores];

    public CableId Id { get; } = id;

    public string ProductTag { get; internal set; } = productTag;

    public string? Type { get; internal set; } = type;

    public IReadOnlyList<CableCore> Cores => _cores;
}

/// <summary>Bir bağlantı noktasına konan potansiyel adı işareti (L1, 24VDC, 0V).</summary>
public sealed class PotentialSource(PotentialSourceId id, string name, PotentialKind kind, ConnectionPointId at)
{
    public PotentialSourceId Id { get; } = id;

    public string Name { get; internal set; } = name;

    public PotentialKind Kind { get; internal set; } = kind;

    public ConnectionPointId At { get; } = at;
}
