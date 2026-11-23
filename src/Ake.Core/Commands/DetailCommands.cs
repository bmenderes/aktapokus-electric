using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Device'ın açıklamasını ve ürün bilgisini (üretici, sipariş kodu, tip) değiştirir. İki değer de yeni değerdir;
/// ürün bilgisi <c>null</c> ise ürün atamasıdır kalkar.
/// </summary>
public sealed class SetDeviceDetails(DeviceId deviceId, string? description, PartRef? part) : ICommand
{
    private Device? _device;
    private (string? Description, PartRef? Part) _previous;

    public string Description => "Cihaz bilgisi değiştirildi";

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        if (part is not null && (string.IsNullOrWhiteSpace(part.Manufacturer) || string.IsNullOrWhiteSpace(part.OrderCode)))
        {
            return Result.Failure(new Error(
                "device.urun_eksik", "Ürün bilgisi için üretici ve sipariş kodu birlikte girilmeli."));
        }

        _device = device;
        _previous = (device.Description, device.Part);
        device.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        device.Part = part;
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _device!.Description = _previous.Description;
        _device.Part = _previous.Part;
    }
}

/// <summary>Function'ın açıklamasını değiştirir; boş açıklama kaldırır.</summary>
public sealed class SetFunctionDescription(FunctionId functionId, string? description) : ICommand
{
    private Function? _function;
    private string? _previous;

    public string Description => "Function açıklaması değiştirildi";

    public Result Execute(Project project)
    {
        var function = project.Devices.SelectMany(device => device.Functions).FirstOrDefault(candidate => candidate.Id == functionId);
        if (function is null)
        {
            return Result.Failure(new Error("function.yok", $"Function modelde yok ({functionId})."));
        }

        _function = function;
        _previous = function.Description;
        function.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    public void Undo(Project project) => _function!.Description = _previous;
}

/// <summary>
/// Bağlantı noktasının adını (A1, 13, U1…) değiştirir. Bağlantılar kimlikle tutulduğu için değişmez. Ad boş
/// olamaz ve aynı function'ın başka bir noktasında bulunamaz.
/// </summary>
public sealed class RenameConnectionPoint(ConnectionPointId pointId, string name) : ICommand
{
    private ConnectionPoint? _point;
    private string _previous = string.Empty;
    private string _description = "Bağlantı noktası adı değiştirildi";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var location = project.Locate(pointId);
        if (location is null)
        {
            return Result.Failure(new Error("point.yok", $"Bağlantı noktası modelde yok ({pointId})."));
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace) || trimmed.Contains(':', StringComparison.Ordinal))
        {
            return Result.Failure(new Error(
                "point.ad_gecersiz", $"Bağlantı noktası adı boş olamaz, boşluk ya da ':' içeremez: '{name}'"));
        }

        if (location.Function.ConnectionPoints.Any(other => other.Id != pointId && other.Name == trimmed))
        {
            return Result.Failure(new Error(
                "point.ad_tekrar", $"-{location.Device.ProductTag} cihazının aynı function'ında '{trimmed}' adlı başka bir nokta var."));
        }

        _point = location.Point;
        _previous = location.Point.Name;
        _description = $"Bağlantı noktası adı değiştirildi: -{location.Device.ProductTag}:{_previous} → {trimmed}";
        location.Point.Name = trimmed;
        return Result.Success();
    }

    public void Undo(Project project) => _point!.Name = _previous;
}

/// <summary>
/// Bağlantı noktasının uç özelliklerini (tür, bağlantı tipi, kontak türü, konektör kodu) değiştirir (ADR-0033). Bağlantılara ve
/// adlara dokunmaz.
/// </summary>
public sealed class SetPointAttributes(ConnectionPointId pointId, PointAttributes attributes) : ICommand
{
    private ConnectionPoint? _point;
    private PointAttributes _previous;

    public string Description => "Uç özellikleri değiştirildi";

    public Result Execute(Project project)
    {
        var location = project.Locate(pointId);
        if (location is null)
        {
            return Result.Failure(new Error("point.yok", $"Bağlantı noktası modelde yok ({pointId})."));
        }

        var normalized = attributes with { ConnectorCode = string.IsNullOrWhiteSpace(attributes.ConnectorCode) ? null : attributes.ConnectorCode.Trim() };
        if (normalized.Check() is { } problem)
        {
            return Result.Failure(new Error("point.ozellik_gecersiz", $"Uç özellikleri geçersiz: {problem}."));
        }

        _point = location.Point;
        _previous = location.Point.Attributes;
        location.Point.Attributes = normalized;
        return Result.Success();
    }

    public void Undo(Project project) => _point!.Attributes = _previous;
}

/// <summary>Cihazın ağ adresini (IP) değiştirir; boş adres kaldırır. En çok 64 karakter, boşluk içermez (ADR-0037).</summary>
public sealed class SetDeviceAddress(DeviceId deviceId, string? address) : ICommand
{
    public const int MaxLength = 64;

    private Device? _device;
    private string? _previous;

    public string Description => "Cihaz ağ adresi değiştirildi";

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var cleaned = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (cleaned is not null && (cleaned.Length > MaxLength || cleaned.Any(char.IsWhiteSpace)))
        {
            return Result.Failure(new Error(
                "device.adres_gecersiz", $"Ağ adresi en çok {MaxLength} karakter olmalı ve boşluk içermemeli: '{address}'"));
        }

        _device = device;
        _previous = device.Address;
        device.Address = cleaned;
        return Result.Success();
    }

    public void Undo(Project project) => _device!.Address = _previous;
}

/// <summary>Cihazın kullanıcı tanımlı özel bir özelliğini ekler/günceller ya da boşsa siler (ADR-0058).</summary>
public sealed class SetDeviceProperty(DeviceId deviceId, string key, string? value) : ICommand
{
    private Device? _device;
    private string _key = string.Empty;
    private bool _hadPrevious;
    private string? _previous;

    public string Description => $"Cihaz özelliği değiştirildi: {key}";

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var trimmedKey = key?.Trim() ?? string.Empty;
        if (trimmedKey.Length == 0)
        {
            return Result.Failure(new Error("device.ozellik_gecersiz", "Cihaz özellik adı boş olamaz."));
        }

        _device = device;
        _key = trimmedKey;
        _hadPrevious = device.PropertyMap.TryGetValue(trimmedKey, out _previous);

        if (string.IsNullOrWhiteSpace(value))
        {
            device.PropertyMap.Remove(trimmedKey);
        }
        else
        {
            device.PropertyMap[trimmedKey] = value.Trim();
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        if (_hadPrevious && _previous is not null)
        {
            _device!.PropertyMap[_key] = _previous;
        }
        else
        {
            _device!.PropertyMap.Remove(_key);
        }
    }
}
