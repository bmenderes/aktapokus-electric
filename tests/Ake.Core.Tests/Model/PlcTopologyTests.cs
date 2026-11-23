using Ake.Core.Model;

namespace Ake.Core.Tests.Model;

/// <summary>Aynı Controller → Network → Node → Module → Channel ağacı her platformu taşımalı.</summary>
public class PlcTopologyTests
{
    private static Device IoDevice(string tag, int channels) =>
        new(
            DeviceId.New(),
            tag,
            Enumerable.Range(1, channels).Select(i => new Function(
                FunctionId.New(),
                FunctionKind.PlcChannel,
                [new ConnectionPoint(ConnectionPointId.New(), i.ToString())])));

    private static Module IoModule(Device device, int position, SignalType signalType, Func<int, string> address) =>
        new(
            ModuleId.New(),
            device.Id,
            position,
            device.Functions.Select((function, i) => new Channel(
                ChannelId.New(), i, signalType, function.Id, address(i))));

    private static Result<Project> Create(IEnumerable<Device> devices, Controller controller) =>
        Project.Create(ProjectId.New(), "test", devices: devices, controllers: [controller]);

    [Fact]
    public void Beckhoff_EtherCat_coupler_with_terminals()
    {
        var ipc = new Device(DeviceId.New(), "A1", []);
        var coupler = new Device(DeviceId.New(), "A10", []);
        var inputs = IoDevice("A11", 8);
        var outputs = IoDevice("A12", 8);

        var node = new Node(
            NodeId.New(),
            coupler.Id,
            "EK1100",
            position: 1,
            modules:
            [
                IoModule(inputs, 1, SignalType.DI, i => $"%I*"),
                IoModule(outputs, 2, SignalType.DO, i => $"%Q*"),
            ]);
        var controller = new Controller(
            ControllerId.New(), ipc.Id, "CX5130", [new Network(NetworkId.New(), NetworkKind.EtherCat, "EtherCAT", [node])]);

        var result = Create([ipc, coupler, inputs, outputs], controller);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(16, result.Value.Controllers[0].Networks[0].Nodes[0].Modules.Sum(module => module.Channels.Count));
    }

    [Fact]
    public void Beckhoff_EtherCat_segments_are_chained_with_port_links()
    {
        var ipc = new Device(DeviceId.New(), "A1", []);
        var firstCoupler = new Device(DeviceId.New(), "A10", []);
        var secondCoupler = new Device(DeviceId.New(), "A20", []);

        var outPort = new Port(PortId.New(), "X2 OUT");
        var inPort = new Port(PortId.New(), "X1 IN");
        var network = new Network(
            NetworkId.New(),
            NetworkKind.EtherCat,
            "EtherCAT",
            [
                new Node(NodeId.New(), firstCoupler.Id, "EK1100-1", 1, ports: [outPort]),
                new Node(NodeId.New(), secondCoupler.Id, "EK1100-2", 2, ports: [inPort]),
            ],
            [new PortLink(PortLinkId.New(), outPort.Id, inPort.Id)]);

        var result = Create(
            [ipc, firstCoupler, secondCoupler],
            new Controller(ControllerId.New(), ipc.Id, "CX5130", [network]));

        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public void Rockwell_chassis_with_slots_and_remote_EtherNetIp_adapter()
    {
        var cpu = new Device(DeviceId.New(), "A1", []);
        var localInputs = IoDevice("A2", 16);
        var localOutputs = IoDevice("A3", 16);
        var adapter = new Device(DeviceId.New(), "A20", []);
        var remoteInputs = IoDevice("A21", 8);

        var chassis = new Node(
            NodeId.New(),
            cpu.Id,
            "Local",
            position: 0,
            modules:
            [
                IoModule(localInputs, 1, SignalType.DI, i => $"Local:1:I.Data.{i}"),
                IoModule(localOutputs, 2, SignalType.DO, i => $"Local:2:O.Data.{i}"),
            ]);
        var remote = new Node(
            NodeId.New(),
            adapter.Id,
            "Remote1",
            position: 1,
            modules: [IoModule(remoteInputs, 1, SignalType.DI, i => $"Remote1:1:I.Data.{i}")]);

        var controller = new Controller(
            ControllerId.New(),
            cpu.Id,
            "CompactLogix",
            [
                new Network(NetworkId.New(), NetworkKind.LocalBus, "Backplane", [chassis]),
                new Network(NetworkId.New(), NetworkKind.EtherNetIp, "EtherNet/IP", [remote]),
            ]);

        var result = Create([cpu, localInputs, localOutputs, adapter, remoteInputs], controller);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var slot2 = result.Value.Controllers[0].Networks[0].Nodes[0].Modules.Single(module => module.Position == 2);
        Assert.Equal("Local:2:O.Data.3", slot2.Channels[3].Address);
    }

    [Fact]
    public void Schneider_controller_with_expansion_modules_and_remote_Modbus_island()
    {
        var cpu = IoDevice("A1", 14);
        var expansion = IoDevice("A2", 16);
        var island = new Device(DeviceId.New(), "A30", []);
        var islandOutputs = IoDevice("A31", 8);

        var body = new Node(
            NodeId.New(),
            cpu.Id,
            "TM241",
            position: 0,
            modules:
            [
                IoModule(cpu, 0, SignalType.DI, i => $"%IX0.{i}"),
                IoModule(expansion, 1, SignalType.DI, i => $"%IX{2 + (i / 8)}.{i % 8}"),
            ]);
        var remote = new Node(
            NodeId.New(),
            island.Id,
            "TM3BC",
            position: 1,
            modules: [IoModule(islandOutputs, 1, SignalType.DO, i => $"%QX10.{i}")]);

        var controller = new Controller(
            ControllerId.New(),
            cpu.Id,
            "M241",
            [
                new Network(NetworkId.New(), NetworkKind.LocalBus, "TM3 bus", [body]),
                new Network(NetworkId.New(), NetworkKind.ModbusTcp, "Modbus TCP", [remote]),
            ]);

        var result = Create([cpu, expansion, island, islandOutputs], controller);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("%IX3.7", result.Value.Controllers[0].Networks[0].Nodes[0].Modules[1].Channels[15].Address);
    }

    [Fact]
    public void Module_pointing_to_a_missing_device_is_rejected()
    {
        var cpu = new Device(DeviceId.New(), "A1", []);
        var node = new Node(NodeId.New(), cpu.Id, "Local", 0, [new Module(ModuleId.New(), DeviceId.New(), 1)]);
        var controller = new Controller(
            ControllerId.New(), cpu.Id, "CPU", [new Network(NetworkId.New(), NetworkKind.LocalBus, "Bus", [node])]);

        Assert.Equal("plc.device_yok", Create([cpu], controller).Error!.Code);
    }

    [Fact]
    public void Channel_pointing_to_a_missing_function_is_rejected()
    {
        var cpu = new Device(DeviceId.New(), "A1", []);
        var module = new Module(
            ModuleId.New(), cpu.Id, 1, [new Channel(ChannelId.New(), 0, SignalType.DI, FunctionId.New())]);
        var node = new Node(NodeId.New(), cpu.Id, "Local", 0, [module]);
        var controller = new Controller(
            ControllerId.New(), cpu.Id, "CPU", [new Network(NetworkId.New(), NetworkKind.LocalBus, "Bus", [node])]);

        Assert.Equal("plc.function_yok", Create([cpu], controller).Error!.Code);
    }

    [Fact]
    public void Port_link_to_a_port_outside_the_network_is_rejected()
    {
        var cpu = new Device(DeviceId.New(), "A1", []);
        var port = new Port(PortId.New(), "X1");
        var network = new Network(
            NetworkId.New(),
            NetworkKind.EtherCat,
            "EtherCAT",
            [new Node(NodeId.New(), cpu.Id, "N1", 1, ports: [port])],
            [new PortLink(PortLinkId.New(), port.Id, PortId.New())]);

        Assert.Equal(
            "plc.port_yok",
            Create([cpu], new Controller(ControllerId.New(), cpu.Id, "CPU", [network])).Error!.Code);
    }
}
