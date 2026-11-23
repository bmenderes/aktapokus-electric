using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Tests.Model;

namespace Ake.Core.Tests.Commands;

public class DeviceCommandTests
{
    private static StructureNodeId Node(Project project, Aspect aspect, string name) =>
        project.Structure.Single(node => node.Aspect == aspect && node.Name == name).Id;

    [Fact]
    public void AddDevice_adds_the_device_with_its_structure()
    {
        var project = TestData.RefMachine();
        var device = new Device(
            DeviceId.New(),
            "K3",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")])],
            functionNodeId: Node(project, Aspect.Function, "M1"),
            locationNodeId: Node(project, Aspect.Location, "P1"));

        CommandAssert.RoundTrips(project, new AddDevice(device));

        Assert.Equal("=M1+P1-K3:A1", project.DesignationOf(device.Point("A1"))!.ToString());
    }

    [Fact]
    public void AddDevice_rejects_an_id_that_is_already_in_use()
    {
        var project = TestData.RefMachine();
        var clash = new Device(project.Device("K1").Id, "K9", []);

        CommandAssert.Rejected(project, new AddDevice(clash), "id.tekrar");
    }

    [Fact]
    public void AddDevice_rejects_a_missing_structure_node()
    {
        var project = TestData.RefMachine();
        var device = new Device(DeviceId.New(), "K3", [], functionNodeId: StructureNodeId.New());

        CommandAssert.Rejected(project, new AddDevice(device), "structure.dugum_yok");
    }

    [Fact]
    public void RemoveDevice_takes_connections_and_placements_with_it()
    {
        var project = TestData.RefMachine();
        var contactor = project.Device("K1");
        var points = contactor.Functions.SelectMany(function => function.ConnectionPoints).Select(point => point.Id).ToHashSet();

        CommandAssert.RoundTrips(project, new RemoveDevice(contactor.Id));

        Assert.Equal(18, project.Devices.Count);
        Assert.Equal(55, project.Connections.Count);
        Assert.Equal(62, project.Placements.Count);
        Assert.DoesNotContain(project.Placements, p => contactor.Functions.Any(f => f.Id == p.FunctionId));
        Assert.DoesNotContain(project.Connections, c => points.Contains(c.From) || points.Contains(c.To));
        Assert.Null(project.Locate(points.First()));
    }

    [Fact]
    public void RemoveDevice_takes_potential_sources_with_it()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new RemoveDevice(project.Device("X0").Id));

        Assert.Equal(["24VDC", "0V"], project.PotentialSources.Select(source => source.Name));
    }

    [Fact]
    public void RemoveDevice_clears_channels_assigned_to_its_functions()
    {
        var sensor = Build.Device("B1", "OUT");
        var cpu = new Device(DeviceId.New(), "A1", []);
        var channel = new Channel(ChannelId.New(), 0, SignalType.DI, sensor.Functions[0].Id, symbolName: "bSensor");
        var controller = new Controller(
            ControllerId.New(),
            cpu.Id,
            "CPU",
            [new Network(NetworkId.New(), NetworkKind.LocalBus, "Bus", [new Node(NodeId.New(), cpu.Id, "Local", 0, [new Module(ModuleId.New(), cpu.Id, 1, [channel])])])]);
        var project = Project.Create(ProjectId.New(), "test", devices: [sensor, cpu], controllers: [controller]).Value;

        CommandAssert.RoundTrips(project, new RemoveDevice(sensor.Id));

        Assert.Null(channel.FunctionId);
        Assert.Equal("bSensor", channel.SymbolName);
    }

    [Theory]
    [InlineData("A1")]
    [InlineData("A10")]
    [InlineData("A11")]
    public void RemoveDevice_rejects_devices_that_are_part_of_the_plc_topology(string tag)
    {
        var project = TestData.RefMachine();

        CommandAssert.Rejected(project, new RemoveDevice(project.Device(tag).Id), "device.plc_bagli");
    }

    [Fact]
    public void RemoveDevice_rejects_an_unknown_device()
    {
        CommandAssert.Rejected(TestData.RefMachine(), new RemoveDevice(DeviceId.New()), "device.yok");
    }

    [Fact]
    public void Retag_changes_the_product_tag_and_keeps_connections()
    {
        var project = TestData.RefMachine();
        var contactor = project.Device("K1");

        CommandAssert.RoundTrips(
            project, new Retag(contactor.Id, "K9", contactor.FunctionNodeId, contactor.LocationNodeId));

        Assert.Equal("=M1+P1-K9", project.DesignationOf(contactor).ToString());
        Assert.Equal(65, project.Connections.Count);
    }

    [Fact]
    public void Retag_can_move_a_device_to_another_structure_node()
    {
        var project = TestData.RefMachine();
        var contactor = project.Device("K1");

        CommandAssert.RoundTrips(
            project,
            new Retag(contactor.Id, "K1", Node(project, Aspect.Function, "E1"), contactor.LocationNodeId));

        Assert.Equal("=E1+P1-K1", project.DesignationOf(contactor).ToString());
    }

    [Fact]
    public void Retag_rejects_invalid_input()
    {
        var project = TestData.RefMachine();
        var contactor = project.Device("K1");
        var locationNode = Node(project, Aspect.Location, "P1");

        CommandAssert.Rejected(project, new Retag(contactor.Id, "K:1", null, null), "tag.gecersiz");
        CommandAssert.Rejected(project, new Retag(contactor.Id, "", null, null), "tag.gecersiz");
        CommandAssert.Rejected(project, new Retag(contactor.Id, "K1", StructureNodeId.New(), null), "structure.dugum_yok");
        CommandAssert.Rejected(project, new Retag(contactor.Id, "K1", locationNode, null), "structure.aspect_uyumsuz");
        CommandAssert.Rejected(project, new Retag(DeviceId.New(), "K1", null, null), "device.yok");
    }

    [Fact]
    public void AssignIo_names_a_free_channel()
    {
        var project = TestData.RefMachine();
        var channel = project.Channel("A11", "8");

        CommandAssert.RoundTrips(
            project, new AssignIo(channel.Id, channel.FunctionId, "bSpare", "%IX0.7", "Yedek giriş"));

        Assert.Equal("bSpare", channel.SymbolName);
        Assert.Equal("%IX0.7", channel.Address);
    }

    [Fact]
    public void AssignIo_rejects_unknown_channel_and_unknown_function()
    {
        var project = TestData.RefMachine();
        var channel = project.Channel("A11", "8");

        CommandAssert.Rejected(project, new AssignIo(ChannelId.New(), null, "x", null, null), "plc.kanal_yok");
        CommandAssert.Rejected(project, new AssignIo(channel.Id, FunctionId.New(), "x", null, null), "plc.function_yok");
    }
}
