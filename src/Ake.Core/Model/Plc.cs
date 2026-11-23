namespace Ake.Core.Model;

// PLC I/O, rack/slot olarak değil bus topolojisi olarak modellenir:
// Controller → Network → Node → Module → Channel → ProcessDataEntry.
// Bu ağaç Device'ların üzerine kurulmuş ikinci bir görünümdür; fiziksel ürün DeviceId ile bağlanır.
// Model üreticiden bağımsızdır; aynı ağaç her platformu taşır:
// - Beckhoff: EtherCat ağı, Node = coupler (EK1100), Module = terminal (EL1008).
// - Rockwell: LocalBus ağı, Node = şasi, Module.Position = slot; uzak I/O için EtherNetIp ağı.
// - Schneider: LocalBus ağı, Node = kontrolör gövdesi, Module = genişleme modülü (TM3);
//   uzak I/O için ModbusTcp, CanOpen ya da Sercos ağı.

public enum NetworkKind
{
    /// <summary>Kontrolörün kendi şasisi ya da genişleme veri yolu (rack/slot).</summary>
    LocalBus,
    EtherCat,
    EtherNetIp,
    Profinet,
    ModbusTcp,
    CanOpen,
    Sercos,
    Other,
}

public enum SignalType
{
    DI,
    DO,
    AI,
    AO,
}

public enum DataDirection
{
    Input,
    Output,
}

public sealed record ProcessDataEntry(
    string Name,
    string DataType,
    int BitOffset,
    int BitLength,
    DataDirection Direction);

public sealed class Channel(
    ChannelId id,
    int index,
    SignalType signalType,
    FunctionId? functionId = null,
    string? address = null,
    string? symbolName = null,
    string? description = null,
    IEnumerable<ProcessDataEntry>? processData = null)
{
    private readonly List<ProcessDataEntry> _processData = [.. processData ?? []];

    public ChannelId Id { get; } = id;

    public int Index { get; } = index;

    public SignalType SignalType { get; internal set; } = signalType;

    /// <summary>Kanalın elektriksel ucu: modül Device'ının bir Function'ı.</summary>
    public FunctionId? FunctionId { get; internal set; } = functionId;

    public string? Address { get; internal set; } = address;

    public string? SymbolName { get; internal set; } = symbolName;

    public string? Description { get; internal set; } = description;

    public IReadOnlyList<ProcessDataEntry> ProcessData => _processData;
}

public sealed class Module(ModuleId id, DeviceId deviceId, int position, IEnumerable<Channel>? channels = null)
{
    private readonly List<Channel> _channels = [.. channels ?? []];

    public ModuleId Id { get; } = id;

    public DeviceId DeviceId { get; } = deviceId;

    public int Position { get; internal set; } = position;

    public IReadOnlyList<Channel> Channels => _channels;
}

public sealed class Port(PortId id, string name)
{
    public PortId Id { get; } = id;

    public string Name { get; internal set; } = name;
}

/// <summary>Aynı ağdaki iki node portu arasındaki bus bağlantısı.</summary>
public sealed class PortLink(PortLinkId id, PortId from, PortId to)
{
    public PortLinkId Id { get; } = id;

    public PortId From { get; } = from;

    public PortId To { get; } = to;
}

public sealed class Node(
    NodeId id,
    DeviceId deviceId,
    string name,
    int position,
    IEnumerable<Module>? modules = null,
    IEnumerable<Port>? ports = null)
{
    private readonly List<Module> _modules = [.. modules ?? []];
    private readonly List<Port> _ports = [.. ports ?? []];

    public NodeId Id { get; } = id;

    public DeviceId DeviceId { get; } = deviceId;

    public string Name { get; internal set; } = name;

    public int Position { get; internal set; } = position;

    public IReadOnlyList<Module> Modules => _modules;

    public IReadOnlyList<Port> Ports => _ports;
}

public sealed class Network(
    NetworkId id,
    NetworkKind kind,
    string name,
    IEnumerable<Node>? nodes = null,
    IEnumerable<PortLink>? portLinks = null)
{
    private readonly List<Node> _nodes = [.. nodes ?? []];
    private readonly List<PortLink> _portLinks = [.. portLinks ?? []];

    public NetworkId Id { get; } = id;

    public NetworkKind Kind { get; internal set; } = kind;

    public string Name { get; internal set; } = name;

    public IReadOnlyList<Node> Nodes => _nodes;

    public IReadOnlyList<PortLink> PortLinks => _portLinks;
}

public sealed class Controller(
    ControllerId id,
    DeviceId deviceId,
    string name,
    IEnumerable<Network>? networks = null)
{
    private readonly List<Network> _networks = [.. networks ?? []];

    public ControllerId Id { get; } = id;

    public DeviceId DeviceId { get; } = deviceId;

    public string Name { get; internal set; } = name;

    public IReadOnlyList<Network> Networks => _networks;
}
