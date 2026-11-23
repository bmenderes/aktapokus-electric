using System.Text.Json.Serialization;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Serialization;

// Patch dosyasının şeması (patchVersion 1). Patch bir transaction'ın dosyadaki halidir.
// Nesnelere kimlikle (GUID) ya da tag'le (-K1, =M1+P1-K1:A1) atıfta bulunulur.

internal sealed record PatchDto(
    int PatchVersion,
    List<PatchOpDto> Commands,
    ChangeSource Source = ChangeSource.User,
    string? Actor = null,
    string? Description = null);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "op")]
[JsonDerivedType(typeof(AddDeviceOp), "addDevice")]
[JsonDerivedType(typeof(RemoveDeviceOp), "removeDevice")]
[JsonDerivedType(typeof(ConnectOp), "connect")]
[JsonDerivedType(typeof(DisconnectOp), "disconnect")]
[JsonDerivedType(typeof(AssignWireOp), "assignWire")]
[JsonDerivedType(typeof(RetagOp), "retag")]
[JsonDerivedType(typeof(AssignIoOp), "assignIo")]
[JsonDerivedType(typeof(SetPropertyOp), "setProperty")]
[JsonDerivedType(typeof(SetDevicePropertyOp), "setDeviceProperty")]
[JsonDerivedType(typeof(AddPageOp), "addPage")]
[JsonDerivedType(typeof(AssignSymbolOp), "assignSymbol")]
internal abstract record PatchOpDto;

/// <summary>Proje alanını ayarlar (antet yer tutucuları için). <c>Value</c> yoksa alan silinir.</summary>
internal sealed record SetPropertyOp(string Key, string? Value = null) : PatchOpDto;

/// <summary>Cihaza kullanıcı tanımlı özel özellik ekler/günceller ya da siler.</summary>
internal sealed record SetDevicePropertyOp(string Device, string Key, string? Value = null) : PatchOpDto;

/// <summary><c>Function</c> ve <c>Location</c> yapı düğümü yollarıdır (M1, P1); düğümler var olmalıdır.</summary>
/// <summary>
/// <c>Function</c> function'ın bir bağlantı noktasıdır (-K1:A1). <c>Symbol</c> sembolün adı ya da kimliğidir;
/// yazılmazsa function yerleşik sembole döner.
/// </summary>
internal sealed record AssignSymbolOp(string Function, string? Symbol = null) : PatchOpDto;

internal sealed record AddPageOp(
    string Number,
    string Title,
    string? Function = null,
    string? Location = null,
    Guid? Id = null) : PatchOpDto;

/// <summary>
/// <c>Tag</c> tam tag'dir (=M1+P1-K3); yapı düğümleri modelde var olmalıdır. <c>Product</c> (sipariş
/// kodu) verilirse function'lar, ürün bilgisi ve montaj kütüphaneden gelir; bunlar patch'te ayrıca
/// yazılamaz.
/// </summary>
internal sealed record AddDeviceOp(
    string Tag,
    Guid? Id = null,
    DeviceKind Kind = DeviceKind.Generic,
    string? Description = null,
    PartRefDto? Part = null,
    MountingDto? Mounting = null,
    List<NewFunctionDto>? Functions = null,
    string? Product = null,
    string? Manufacturer = null) : PatchOpDto;

internal sealed record NewFunctionDto(
    FunctionKind Kind,
    bool PassThrough = false,
    string? Description = null,
    decimal? RatedCurrentA = null,
    List<string>? ConnectionPoints = null);

internal sealed record RemoveDeviceOp(string Device) : PatchOpDto;

/// <summary><c>Core</c> bir kablo damarıdır: -W1:GNYE.</summary>
internal sealed record ConnectOp(
    string From,
    string To,
    Guid? Id = null,
    NewWireDto? Wire = null,
    string? Core = null) : PatchOpDto;

internal sealed record NewWireDto(string? Number = null, decimal? CrossSectionMm2 = null, string? Color = null);

internal sealed record DisconnectOp(string From, string To) : PatchOpDto;

/// <summary>Ne <c>Wire</c> ne <c>Core</c> verilirse bağlantının ortamı kaldırılır.</summary>
internal sealed record AssignWireOp(string From, string To, NewWireDto? Wire = null, string? Core = null) : PatchOpDto;

/// <summary><c>Tag</c> içinde yazılmayan aspect değişmez: "-K7" yalnızca ürün tag'ini değiştirir.</summary>
internal sealed record RetagOp(string Device, string Tag) : PatchOpDto;

/// <summary>
/// <c>Channel</c> kanalın kimliği ya da elektriksel ucudur (-A12:3). Yazılmayan alan değişmez.
/// </summary>
internal sealed record AssignIoOp(
    string Channel,
    string? Function = null,
    string? SymbolName = null,
    string? Address = null,
    string? Description = null) : PatchOpDto;

internal sealed record AuditEntryDto(
    DateTimeOffset At,
    ChangeSource Source,
    string Actor,
    AuditAction Action,
    string Summary);
