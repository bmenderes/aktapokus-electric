using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>Device'ı function ve bağlantı noktalarıyla modele ekler.</summary>
public sealed class AddDevice(Device device) : ICommand
{
    public string Description => $"Device eklendi: -{device.ProductTag}";

    public Result Execute(Project project)
    {
        if (!ReferenceDesignation.IsValidProductTag(device.ProductTag))
        {
            return Result.Failure(new Error("tag.gecersiz", $"Geçersiz ürün tag'i: '{device.ProductTag}'"));
        }

        var existing = project.IdSet();
        var newIds = device.Functions
            .SelectMany(function => function.ConnectionPoints.Select(point => point.Id.Value).Prepend(function.Id.Value))
            .Prepend(device.Id.Value);
        foreach (var id in newIds)
        {
            if (!existing.Add(id))
            {
                return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor: {id}"));
            }
        }

        if (device.Functions.FirstOrDefault(function => function.Level is < 1 or > Function.MaxLevel
            || (function.Level > 1 && function.Kind != FunctionKind.Terminal)) is { } badLevel)
        {
            return Result.Failure(new Error(
                "function.kat_gecersiz",
                $"-{device.ProductTag}: kat 1–{Function.MaxLevel} olmalı ve yalnızca klemenste 1'den büyük olabilir ({badLevel.Level})."));
        }

        if (device.Functions.FirstOrDefault(function => function.Role != TerminalRole.Normal
            && (function.Kind != FunctionKind.Terminal || (function.Role == TerminalRole.EndCover && function.ConnectionPoints.Count > 0))) is { } badRole)
        {
            return Result.Failure(new Error(
                "function.tur_gecersiz",
                $"-{device.ProductTag}: klemens türü ({badRole.Role}) yalnızca klemens function'ında olabilir; sonlandırıcının bağlantı noktası olmaz."));
        }

        var owner = $"-{device.ProductTag}";
        var nodeError = project.CheckNode(device.FunctionNodeId, Aspect.Function, owner)
            ?? project.CheckNode(device.LocationNodeId, Aspect.Location, owner);
        if (nodeError is not null)
        {
            return Result.Failure(nodeError);
        }

        project.InsertDevice(project.Devices.Count, device);
        return Result.Success();
    }

    public void Undo(Project project) => project.RemoveDevice(device);
}

/// <summary>Mevcut bir cihazın altına yeni bir function (ör. başka sayfadaki blok veya ek kontak) ekler.</summary>
public sealed class AddFunction(DeviceId deviceId, Function function) : ICommand
{
    private Device? _device;

    public string Description => _device is null ? "Function eklendi" : $"Function eklendi: -{_device.ProductTag}";

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var existing = project.IdSet();
        var newIds = function.ConnectionPoints.Select(point => point.Id.Value).Prepend(function.Id.Value);
        foreach (var id in newIds)
        {
            if (!existing.Add(id))
            {
                return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor: {id}"));
            }
        }

        var owner = $"-{device.ProductTag}";
        if (function.Level is < 1 or > Function.MaxLevel || (function.Level > 1 && function.Kind != FunctionKind.Terminal))
        {
            return Result.Failure(new Error(
                "function.kat_gecersiz",
                $"{owner}: kat 1–{Function.MaxLevel} olmalı ve yalnızca klemenste 1'den büyük olabilir ({function.Level})."));
        }

        if (function.Role != TerminalRole.Normal
            && (function.Kind != FunctionKind.Terminal || (function.Role == TerminalRole.EndCover && function.ConnectionPoints.Count > 0)))
        {
            return Result.Failure(new Error(
                "function.tur_gecersiz",
                $"{owner}: klemens türü ({function.Role}) yalnızca klemens function'ında olabilir; sonlandırıcının bağlantı noktası olmaz."));
        }

        if (project.CheckSymbolFits(function, owner) is { } symbolError)
        {
            return Result.Failure(symbolError);
        }

        _device = device;
        project.InsertFunction(device, device.Functions.Count, function);
        return Result.Success();
    }

    public void Undo(Project project) => project.RemoveFunction(_device!, function);
}

/// <summary>
/// Device'ı ve ona bağlı her şeyi kaldırır: bağlantıları, üzerindeki potansiyel işaretleri,
/// yerleşimleri; function'larına atanmış PLC kanallarını boşaltır. PLC ağacının parçası olan
/// device (controller, node, modül) silinemez.
/// </summary>
public sealed class RemoveDevice(DeviceId deviceId) : ICommand
{
    private Device? _device;
    private int _index;
    private List<(int Index, Connection Item)> _connections = [];
    private List<(int Index, PotentialSource Item)> _sources = [];
    private List<(int Index, Placement Item)> _placements = [];
    private List<(Channel Channel, FunctionId FunctionId)> _channels = [];

    public string Description => _device is null ? "Device silindi" : $"Device silindi: -{_device.ProductTag}";

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var nodes = project.Controllers.SelectMany(controller => controller.Networks).SelectMany(network => network.Nodes);
        var plcBound = project.Controllers.Any(controller => controller.DeviceId == deviceId)
            || nodes.Any(node => node.DeviceId == deviceId || node.Modules.Any(module => module.DeviceId == deviceId));
        if (plcBound)
        {
            return Result.Failure(new Error(
                "device.plc_bagli",
                $"-{device.ProductTag} PLC topolojisinin parçası (controller, node ya da modül); silinemez."));
        }

        var functions = device.Functions.Select(function => function.Id).ToHashSet();
        var points = device.Functions
            .SelectMany(function => function.ConnectionPoints)
            .Select(point => point.Id)
            .ToHashSet();

        _device = device;
        _connections = ListEdit.RemoveWhere(
            project.ConnectionList, connection => points.Contains(connection.From) || points.Contains(connection.To));
        _sources = ListEdit.RemoveWhere(project.PotentialSourceList, source => points.Contains(source.At));
        _placements = ListEdit.RemoveWhere(project.PlacementList, placement => functions.Contains(placement.FunctionId));

        _channels = [];
        foreach (var channel in project.Channels)
        {
            if (channel.FunctionId is { } functionId && functions.Contains(functionId))
            {
                _channels.Add((channel, functionId));
                channel.FunctionId = null;
            }
        }

        _index = project.RemoveDevice(device);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        project.InsertDevice(_index, _device!);
        ListEdit.Restore(project.ConnectionList, _connections);
        ListEdit.Restore(project.PotentialSourceList, _sources);
        ListEdit.Restore(project.PlacementList, _placements);
        foreach (var (channel, functionId) in _channels)
        {
            channel.FunctionId = functionId;
        }
    }
}

/// <summary>
/// Device'ın tam tag'ini değiştirir: ürün tag'i ve iki yapı düğümü. Üç değer de yeni değerdir;
/// değişmeyecek olan için mevcut değer verilir.
/// </summary>
public sealed class Retag(
    DeviceId deviceId,
    string productTag,
    StructureNodeId? functionNodeId,
    StructureNodeId? locationNodeId) : ICommand
{
    private Device? _device;
    private (string Tag, StructureNodeId? Function, StructureNodeId? Location) _previous;
    private string _description = "Tag değiştirildi";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var device = project.FindDevice(deviceId);
        if (device is null)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        if (!ReferenceDesignation.IsValidProductTag(productTag))
        {
            return Result.Failure(new Error("tag.gecersiz", $"Geçersiz ürün tag'i: '{productTag}'"));
        }

        var owner = $"-{productTag}";
        var nodeError = project.CheckNode(functionNodeId, Aspect.Function, owner)
            ?? project.CheckNode(locationNodeId, Aspect.Location, owner);
        if (nodeError is not null)
        {
            return Result.Failure(nodeError);
        }

        var before = project.DesignationOf(device);
        _device = device;
        _previous = (device.ProductTag, device.FunctionNodeId, device.LocationNodeId);

        device.ProductTag = productTag;
        device.FunctionNodeId = functionNodeId;
        device.LocationNodeId = locationNodeId;

        _description = $"Tag değiştirildi: {before} → {project.DesignationOf(device)}";
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _device!.ProductTag = _previous.Tag;
        _device.FunctionNodeId = _previous.Function;
        _device.LocationNodeId = _previous.Location;
    }
}

/// <summary>
/// PLC kanalının atamasını değiştirir. Dört değer de yeni değerdir; değişmeyecek olan için
/// mevcut değer verilir.
/// </summary>
public sealed class AssignIo(
    ChannelId channelId,
    FunctionId? functionId,
    string? symbolName,
    string? address,
    string? description) : ICommand
{
    private Channel? _channel;
    private (FunctionId? Function, string? Symbol, string? Address, string? Description) _previous;

    public string Description => symbolName is null ? "I/O ataması kaldırıldı" : $"I/O atandı: {symbolName}";

    public Result Execute(Project project)
    {
        var channel = project.Channels.FirstOrDefault(candidate => candidate.Id == channelId);
        if (channel is null)
        {
            return Result.Failure(new Error("plc.kanal_yok", $"PLC kanalı modelde yok ({channelId})."));
        }

        if (functionId is { } id
            && !project.Devices.SelectMany(device => device.Functions).Any(function => function.Id == id))
        {
            return Result.Failure(new Error("plc.function_yok", $"Kanala atanacak function modelde yok ({id})."));
        }

        _channel = channel;
        _previous = (channel.FunctionId, channel.SymbolName, channel.Address, channel.Description);

        channel.FunctionId = functionId;
        channel.SymbolName = symbolName;
        channel.Address = address;
        channel.Description = description;
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _channel!.FunctionId = _previous.Function;
        _channel.SymbolName = _previous.Symbol;
        _channel.Address = _previous.Address;
        _channel.Description = _previous.Description;
    }
}
