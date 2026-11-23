using Ake.Core.Model;

namespace Ake.Core.Catalog;

/// <summary>Bir ürünün function şablonu: türü ve bağlantı noktası adları (bobin A1/A2, NO 13/14).</summary>
public sealed record FunctionTemplate(
    FunctionKind Kind,
    IReadOnlyList<string> ConnectionPoints,
    bool PassThrough = false,
    string? Description = null,
    decimal? RatedCurrentA = null,
    SymbolId? SymbolId = null);

/// <summary>
/// Bir ürünün, ondan Device üretmek için gereken tanımı. Kütüphanenin nerede tutulduğundan
/// bağımsızdır; Core yalnızca bu tanımı bilir.
/// </summary>
public sealed record ProductDefinition(
    string Manufacturer,
    string OrderCode,
    IReadOnlyList<FunctionTemplate> Functions,
    string? TypeDesignation = null,
    string? Description = null,
    Mounting? Mounting = null)
{
    /// <summary>
    /// Şablondan yeni bir Device üretir: function'lar ve bağlantı noktaları yeni kimliklerle kurulur.
    /// Device modele eklenmez; bunun için <c>AddDevice</c> command'ı kullanılır.
    /// </summary>
    public Device CreateDevice(
        string productTag,
        StructureNodeId? functionNodeId = null,
        StructureNodeId? locationNodeId = null,
        string? description = null,
        DeviceId? id = null)
    {
        var functions = Functions.Select(template => new Function(
            FunctionId.New(),
            template.Kind,
            template.ConnectionPoints.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)),
            template.PassThrough,
            template.Description,
            template.RatedCurrentA));

        return new Device(
            id ?? DeviceId.New(),
            productTag,
            functions,
            Functions.Count > 0 && Functions.All(template => template.Kind == FunctionKind.Terminal)
                ? DeviceKind.TerminalStrip
                : DeviceKind.Generic,
            functionNodeId,
            locationNodeId,
            description ?? Description,
            new PartRef(Manufacturer, OrderCode, TypeDesignation),
            Mounting);
    }
}

/// <summary>Ürün tanımlarının kaynağı (komponent kütüphanesi). Core kaynağın ne olduğunu bilmez.</summary>
public interface IProductCatalog
{
    /// <summary>
    /// Sipariş koduyla ürün bulur. Üretici verilmezse ve kod birden çok üreticide varsa başarısız olur.
    /// </summary>
    Result<ProductDefinition> Find(string orderCode, string? manufacturer = null);
}

/// <summary>
/// Projeden ayrı tutulan sembollerin kaynağı (paylaşılan sembol kütüphanesi). Bir patch projede olmayan
/// bir sembolü buradan alır ve projeye kopyalar.
/// </summary>
public interface ISymbolCatalog
{
    /// <summary>
    /// Sembolü kimliğiyle, tek ise adıyla ya da <c>kategori/ad</c> yoluyla bulur. Ad birden çok sembolde
    /// varsa başarısız olur.
    /// </summary>
    Result<SymbolDefinition> FindSymbol(string reference);
}
