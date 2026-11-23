namespace Ake.Core.Model;

public enum PlacementRole
{
    Main,
    Reference,
}

/// <summary>Sayfanın içeriği nasıl üretilir (ADR-0037).</summary>
public enum PageKind
{
    /// <summary>Elle çizilen şema sayfası: semboller, teller, serbest grafik.</summary>
    Schematic,

    /// <summary>Haberleşme şeması: içerik projeden (haberleşme uçları ve bağlantıları) otomatik üretilir; çerçeve ve antet şablondan.</summary>
    CommunicationDiagram,

    /// <summary>Tek hat şeması: içerik güç zincirinden (besleme → koruma → anahtarlama → yük) otomatik üretilir (ADR-0039).</summary>
    SingleLineDiagram,

    /// <summary>Kapak sayfası: proje adı ve proje özelliklerinden (açıklama, müşteri, sipariş no, çizen, tarih, revizyon) üretilir (ADR-0046).</summary>
    Cover,

    /// <summary>İçindekiler: projenin sayfa listesi; her satır PDF'te o sayfaya bağlantıdır. Sığmayan liste devam sayfalarıyla sürer (<see cref="Page.Part"/>).</summary>
    Contents,

    /// <summary>Malzeme listesi sayfası (rapor <c>bom</c>); devam sayfalarıyla sürer.</summary>
    PartsList,

    /// <summary>Klemens planı sayfası (rapor <c>terminals</c>); devam sayfalarıyla sürer.</summary>
    TerminalPlan,

    /// <summary>Kablo listesi sayfası (rapor <c>cables</c>); devam sayfalarıyla sürer.</summary>
    CableList,

    /// <summary>Potansiyel özeti (ADR-0050): her potansiyelin adı, tel numaraları ve uçları; devam sayfalarıyla sürer.</summary>
    PotentialOverview,

    /// <summary>Pin-bağlantı şeması (ADR-0050): her cihaz ucunun hedefi ve teli ya da kablo damarı; devam sayfalarıyla sürer.</summary>
    PinConnections,

    /// <summary>PLC şeması (ADR-0050): modül kanalları, adresleri, adları ve bağlı saha uçları (G/Ç listesi); devam sayfalarıyla sürer.</summary>
    PlcDiagram,

    /// <summary>Revizyon özeti (ADR-0050): revizyon geçmişi (işaret, tarih, hazırlayan, açıklama); devam sayfalarıyla sürer.</summary>
    RevisionOverview,

    /// <summary>Çerçeve dokümantasyonu (ADR-0050): projedeki sayfa şablonları (kâğıt, ızgara, antet) ve kaç sayfanın kullandığı; devam sayfalarıyla sürer.</summary>
    FrameDocumentation,
}

/// <summary>Şema sayfası. Çerçeve, antet ve grid Faz 5'te eklenir.</summary>
public sealed class Page(
    PageId id,
    string number,
    string title,
    StructureNodeId? functionNodeId = null,
    StructureNodeId? locationNodeId = null,
    SheetTemplateId? templateId = null,
    PageKind kind = PageKind.Schematic,
    int part = 0)
{
    public PageId Id { get; } = id;

    /// <summary>Sayfanın türü; elle çizilen şema ya da otomatik üretilen haberleşme şeması.</summary>
    public PageKind Kind { get; } = kind;

    /// <summary>
    /// Tablo türü sayfalarda (içindekiler, malzeme, klemens, kablo listesi) kaçıncı devam sayfası olduğu (0'dan): sayfa, tablonun
    /// <c>Part</c>. diliminin satırlarını gösterir. Diğer türlerde 0'dır.
    /// </summary>
    public int Part { get; } = part;

    /// <summary>Sayfanın şablonu; boşsa projenin ilk şablonu kullanılır.</summary>
    public SheetTemplateId? TemplateId { get; internal set; } = templateId;

    public string Number { get; internal set; } = number;

    public string Title { get; internal set; } = title;

    public StructureNodeId? FunctionNodeId { get; internal set; } = functionNodeId;

    public StructureNodeId? LocationNodeId { get; internal set; } = locationNodeId;
}

/// <summary>
/// Bir Function'ın bir sayfadaki grafik temsili. Elektriksel modelden bağımsızdır: taşınması ya da
/// silinmesi bağlantıları değiştirmez. Bir function'ın en çok bir ana görünümü olur.
/// </summary>
public sealed class Placement(
    PlacementId id,
    PageId pageId,
    FunctionId functionId,
    PlacementRole role,
    double x,
    double y,
    int rotationDeg = 0)
{
    public PlacementId Id { get; } = id;

    public PageId PageId { get; internal set; } = pageId;

    public FunctionId FunctionId { get; } = functionId;

    public PlacementRole Role { get; internal set; } = role;

    public double X { get; internal set; } = x;

    public double Y { get; internal set; } = y;

    public int RotationDeg { get; internal set; } = rotationDeg;
}
