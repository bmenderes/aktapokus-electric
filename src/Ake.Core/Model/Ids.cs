namespace Ake.Core.Model;

// Kararlı kimlikler. İnsan-okunur tag'lerden bağımsızdır; tag değişse de kimlik değişmez.
// Yeni kimlikler GUID v7'dir (zamana göre sıralı). Her tür ayrı tiptir, karıştırılamaz.

public readonly record struct ProjectId(Guid Value)
{
    public static ProjectId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct StructureNodeId(Guid Value)
{
    public static StructureNodeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct DeviceId(Guid Value)
{
    public static DeviceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct FunctionId(Guid Value)
{
    public static FunctionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ConnectionPointId(Guid Value)
{
    public static ConnectionPointId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ConnectionId(Guid Value)
{
    public static ConnectionId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct WireId(Guid Value)
{
    public static WireId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CableId(Guid Value)
{
    public static CableId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct CableCoreId(Guid Value)
{
    public static CableCoreId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PotentialSourceId(Guid Value)
{
    public static PotentialSourceId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ControllerId(Guid Value)
{
    public static ControllerId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct NetworkId(Guid Value)
{
    public static NetworkId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct NodeId(Guid Value)
{
    public static NodeId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PortId(Guid Value)
{
    public static PortId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PortLinkId(Guid Value)
{
    public static PortLinkId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ModuleId(Guid Value)
{
    public static ModuleId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct ChannelId(Guid Value)
{
    public static ChannelId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PageId(Guid Value)
{
    public static PageId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}

public readonly record struct PlacementId(Guid Value)
{
    public static PlacementId New() => new(Guid.CreateVersion7());

    public override string ToString() => Value.ToString();
}
