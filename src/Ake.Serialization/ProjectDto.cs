using System.Text.Json.Serialization;
using Ake.Core.Model;
using Ake.Core.Numbering;

namespace Ake.Serialization;

// Proje dosyasının şeması (schemaVersion 1). Model sınıflarından ayrıdır: model değişse de dosya
// biçimi ancak şema versiyonu artırılarak değişir. Parametre sırası JSON'daki alan sırasıdır.

internal sealed record ProjectDto(
    int SchemaVersion,
    Guid Id,
    string Name,
    TagScheme? TagScheme = null,
    SortedDictionary<string, string>? Properties = null,
    List<StructureNodeDto>? Structure = null,
    List<DeviceDto>? Devices = null,
    List<ConnectionDto>? Connections = null,
    List<CableDto>? Cables = null,
    List<PotentialSourceDto>? PotentialSources = null,
    List<ControllerDto>? Controllers = null,
    List<SheetTemplateDto>? SheetTemplates = null,
    List<SymbolDto>? Symbols = null,
    List<PageDto>? Pages = null,
    List<PlacementDto>? Placements = null,
    List<GraphicDto>? Graphics = null,
    List<ConnectionKindDto>? ConnectionKinds = null,
    WireNumberingDto? WireNumbering = null,
    List<RevisionDto>? Revisions = null);

internal sealed record RevisionDto(string Mark, string Date, string Author, string Description);

internal sealed record StructureNodeDto(
    Guid Id,
    Aspect Aspect,
    string Name,
    Guid? ParentId = null,
    string? Description = null);

internal sealed record DeviceDto(
    Guid Id,
    string Tag,
    DeviceKind Kind = DeviceKind.Generic,
    Guid? FunctionNodeId = null,
    Guid? LocationNodeId = null,
    string? Description = null,
    PartRefDto? Part = null,
    MountingDto? Mounting = null,
    List<FunctionDto>? Functions = null,
    string? Address = null,
    SortedDictionary<string, string>? Properties = null);

internal sealed record PartRefDto(string Manufacturer, string OrderCode, string? TypeDesignation = null);

internal sealed record MountingDto(
    MountingType Type,
    double WidthMm,
    double HeightMm,
    double DepthMm,
    double ClearanceMm = 0,
    double HeatLossW = 0,
    string? StepRef = null);

internal sealed record FunctionDto(
    Guid Id,
    FunctionKind Kind,
    bool PassThrough = false,
    string? Description = null,
    decimal? RatedCurrentA = null,
    Guid? SymbolId = null,
    List<ConnectionPointDto>? ConnectionPoints = null,
    int? Level = null,
    TerminalRole? Role = null);

/// <summary>Uç özellikleri (ADR-0033) yalnızca belirtilmişse yazılır; eski dosyalarda yoktur.</summary>
internal sealed record ConnectionPointDto(
    Guid Id,
    string Name,
    PinKind? Kind = null,
    ConnectorType? Connector = null,
    ContactType? Contact = null,
    string? ConnectorCode = null);

/// <summary><c>Route</c> köşe noktalarıdır, tek metin: <c>x,y x,y…</c> (yoksa tel otomatik yönlendirilir; ADR-0029).</summary>
internal sealed record ConnectionDto(
    Guid Id, Guid From, Guid To, WireDto? Wire = null, Guid? CoreId = null, string? Route = null, Guid? Kind = null);

/// <summary>Bağlantı türü (ADR-0036): renk #RRGGBB, çizgi biçimi ve kategori yalnızca varsayılandan farklıysa yazılır.</summary>
/// <summary>Tel numaralandırma ayarları (ADR-0041); varsayılandan farklıysa yazılır, alanlar da yalnızca değişmişse.</summary>
internal sealed record WireNumberingDto(
    string? Prefix = null, int? Start = null, int? Step = null, int? Width = null, WireNumberingScheme? Scheme = null);

internal sealed record ConnectionKindDto(Guid Id, string Name, string Color, LineStyle? Style = null, PinKind? Category = null);

internal sealed record WireDto(Guid Id, string? Number = null, decimal? CrossSectionMm2 = null, string? Color = null);

internal sealed record CableDto(Guid Id, string Tag, string? Type = null, List<CableCoreDto>? Cores = null);

internal sealed record CableCoreDto(
    Guid Id,
    string Designation,
    decimal? CrossSectionMm2 = null,
    string? Color = null);

internal sealed record PotentialSourceDto(Guid Id, string Name, PotentialKind Kind, Guid At);

internal sealed record ControllerDto(Guid Id, Guid DeviceId, string Name, List<NetworkDto>? Networks = null);

internal sealed record NetworkDto(
    Guid Id,
    NetworkKind Kind,
    string Name,
    List<NodeDto>? Nodes = null,
    List<PortLinkDto>? PortLinks = null);

internal sealed record NodeDto(
    Guid Id,
    Guid DeviceId,
    string Name,
    int Position,
    List<PortDto>? Ports = null,
    List<ModuleDto>? Modules = null);

internal sealed record PortDto(Guid Id, string Name);

internal sealed record PortLinkDto(Guid Id, Guid From, Guid To);

internal sealed record ModuleDto(Guid Id, Guid DeviceId, int Position, List<ChannelDto>? Channels = null);

internal sealed record ChannelDto(
    Guid Id,
    int Index,
    SignalType SignalType,
    Guid? FunctionId = null,
    string? Address = null,
    string? SymbolName = null,
    string? Description = null,
    List<ProcessDataEntryDto>? ProcessData = null);

internal sealed record ProcessDataEntryDto(
    string Name,
    string DataType,
    int BitOffset,
    int BitLength,
    DataDirection Direction);

internal sealed record PageDto(
    Guid Id,
    string Number,
    string Title,
    Guid? FunctionNodeId = null,
    Guid? LocationNodeId = null,
    Guid? TemplateId = null,
    PageKind? Kind = null,
    int? Part = null);

internal sealed record SheetTemplateDto(
    Guid Id,
    string Name,
    double WidthMm,
    double HeightMm,
    double MarginMm,
    int Columns,
    int Rows,
    double GridBandMm,
    double TitleBlockHeightMm,
    string FontFamily,
    int FirstColumnNumber = 0,
    List<TitleBlockCellDto>? Cells = null);

internal sealed record TitleBlockCellDto(
    double XMm,
    double YMm,
    double WidthMm,
    double HeightMm,
    string? Label = null,
    string? Content = null,
    double FontSizeMm = 3,
    TextAlign Align = TextAlign.Left,
    bool Border = true,
    string? ImageRef = null);

internal sealed record PlacementDto(
    Guid Id,
    Guid PageId,
    Guid FunctionId,
    PlacementRole Role,
    double X,
    double Y,
    int RotationDeg = 0);

/// <summary>Serbest grafik (şema 6). Çoklu çizgi noktaları tek metindir: <c>x,y x,y…</c>.</summary>
internal sealed record GraphicDto(Guid Id, Guid PageId, GraphicShapeDto Shape);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GraphicPolylineDto), "polyline")]
[JsonDerivedType(typeof(GraphicCircleDto), "circle")]
[JsonDerivedType(typeof(GraphicTextDto), "text")]
internal abstract record GraphicShapeDto(string? Color = null);

internal sealed record GraphicPolylineDto(string Points, double StrokeMm = 0.25, bool? Closed = null, bool? Filled = null, string? Color = null)
    : GraphicShapeDto(Color);

internal sealed record GraphicCircleDto(double X, double Y, double Radius, double StrokeMm = 0.25, bool? Filled = null, string? Color = null)
    : GraphicShapeDto(Color);

internal sealed record GraphicTextDto(
    double X, double Y, string Text, double SizeMm = 2.5, TextAlign Align = TextAlign.Left, double? RotationDeg = null, string? Color = null)
    : GraphicShapeDto(Color);

internal sealed record SymbolDto(
    Guid Id,
    string Name,
    List<SymbolShapeDto> Shapes,
    List<SymbolPinDto> Pins,
    string? Source = null,
    string? License = null,
    string? DeviceName = null,
    string? TagLetter = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SymbolLineDto), "line")]
[JsonDerivedType(typeof(SymbolRectDto), "rect")]
[JsonDerivedType(typeof(SymbolCircleDto), "circle")]
[JsonDerivedType(typeof(SymbolTextDto), "text")]
internal abstract record SymbolShapeDto;

internal sealed record SymbolLineDto(double X1, double Y1, double X2, double Y2, double StrokeMm = 0.25) : SymbolShapeDto;

internal sealed record SymbolRectDto(double X, double Y, double Width, double Height, double StrokeMm = 0.25)
    : SymbolShapeDto;

internal sealed record SymbolCircleDto(double X, double Y, double Radius, double StrokeMm = 0.25, bool Filled = false)
    : SymbolShapeDto;

internal sealed record SymbolTextDto(double X, double Y, string Text, double SizeMm = 2.5, TextAlign Align = TextAlign.Left, double? RotationDeg = null)
    : SymbolShapeDto;

internal sealed record SymbolPinDto(
    double X,
    double Y,
    int DirectionX,
    int DirectionY,
    string? Name = null,
    PinKind? Kind = null,
    ConnectorType? Connector = null,
    ContactType? Contact = null,
    string? ConnectorCode = null);
