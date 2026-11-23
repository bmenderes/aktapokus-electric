using Ake.Core.Model;

namespace Ake.Core.Tests.Model;

public class ProjectTests
{
    private static string? ErrorCode(Result<Project> result) => result.Error?.Code;

    private static Result<Project> Create(
        IEnumerable<StructureNode>? structure = null,
        IEnumerable<Device>? devices = null,
        IEnumerable<Connection>? connections = null,
        IEnumerable<Cable>? cables = null,
        IEnumerable<PotentialSource>? potentialSources = null,
        IEnumerable<Controller>? controllers = null,
        IEnumerable<Page>? pages = null,
        IEnumerable<Placement>? placements = null) =>
        Project.Create(
            ProjectId.New(), "test", structure, devices, connections, cables, potentialSources, controllers, pages, placements);

    [Fact]
    public void Empty_project_is_valid()
    {
        Assert.True(Create().IsSuccess);
    }

    [Fact]
    public void New_ids_are_unique_and_typed()
    {
        Assert.NotEqual(DeviceId.New(), DeviceId.New());
        Assert.Equal(7, DeviceId.New().Value.Version);
    }

    [Fact]
    public void Designation_combines_structure_path_and_product_tag()
    {
        var machine = new StructureNode(StructureNodeId.New(), Aspect.Function, "A1");
        var motor = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1", machine.Id);
        var panel = new StructureNode(StructureNodeId.New(), Aspect.Location, "P1");
        var contactor = new Device(
            DeviceId.New(),
            "K1",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")])],
            functionNodeId: motor.Id,
            locationNodeId: panel.Id);

        var project = Create(structure: [machine, motor, panel], devices: [contactor]).Value;

        Assert.Equal("=A1.M1+P1-K1", project.DesignationOf(contactor).ToString());
        Assert.Equal("=A1.M1+P1-K1:A1", project.DesignationOf(contactor.Point("A1"))!.ToString());
    }

    [Fact]
    public void Duplicate_id_is_rejected()
    {
        var shared = Guid.CreateVersion7();
        var first = new Device(new DeviceId(shared), "K1", []);
        var second = new Device(new DeviceId(shared), "K2", []);

        Assert.Equal("id.tekrar", ErrorCode(Create(devices: [first, second])));
    }

    [Fact]
    public void Duplicate_id_across_different_kinds_is_rejected()
    {
        var shared = Guid.CreateVersion7();
        var device = new Device(new DeviceId(shared), "K1", []);
        var page = new Page(new PageId(shared), "1", "Sayfa");

        Assert.Equal("id.tekrar", ErrorCode(Create(devices: [device], pages: [page])));
    }

    [Fact]
    public void Structure_node_with_missing_parent_is_rejected()
    {
        var orphan = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1", StructureNodeId.New());

        Assert.Equal("structure.ust_yok", ErrorCode(Create(structure: [orphan])));
    }

    [Fact]
    public void Structure_node_under_a_different_aspect_is_rejected()
    {
        var panel = new StructureNode(StructureNodeId.New(), Aspect.Location, "P1");
        var motor = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1", panel.Id);

        Assert.Equal("structure.aspect_uyumsuz", ErrorCode(Create(structure: [panel, motor])));
    }

    [Fact]
    public void Structure_cycle_is_rejected()
    {
        var firstId = StructureNodeId.New();
        var secondId = StructureNodeId.New();
        var first = new StructureNode(firstId, Aspect.Function, "A", secondId);
        var second = new StructureNode(secondId, Aspect.Function, "B", firstId);

        Assert.Equal("structure.dongu", ErrorCode(Create(structure: [first, second])));
    }

    [Fact]
    public void Device_pointing_to_a_missing_structure_node_is_rejected()
    {
        var device = new Device(DeviceId.New(), "K1", [], functionNodeId: StructureNodeId.New());

        Assert.Equal("structure.dugum_yok", ErrorCode(Create(devices: [device])));
    }

    [Fact]
    public void Device_using_a_location_node_as_function_is_rejected()
    {
        var panel = new StructureNode(StructureNodeId.New(), Aspect.Location, "P1");
        var device = new Device(DeviceId.New(), "K1", [], functionNodeId: panel.Id);

        Assert.Equal("structure.aspect_uyumsuz", ErrorCode(Create(structure: [panel], devices: [device])));
    }

    [Fact]
    public void Connection_to_a_missing_point_is_rejected()
    {
        var device = Build.Device("K1", "A1");
        var connection = new Connection(ConnectionId.New(), device.Point("A1"), ConnectionPointId.New());

        Assert.Equal("connection.uc_yok", ErrorCode(Create(devices: [device], connections: [connection])));
    }

    [Fact]
    public void Connection_from_a_point_to_itself_is_rejected()
    {
        var device = Build.Device("K1", "A1");
        var connection = new Connection(ConnectionId.New(), device.Point("A1"), device.Point("A1"));

        Assert.Equal("connection.kendine", ErrorCode(Create(devices: [device], connections: [connection])));
    }

    [Fact]
    public void Connection_with_both_wire_and_cable_core_is_rejected()
    {
        var a = Build.Device("K1", "1");
        var b = Build.Device("K2", "1");
        var core = new CableCore(CableCoreId.New(), "1");
        var cable = new Cable(CableId.New(), "W1", [core]);
        var connection = new Connection(
            ConnectionId.New(), a.Point("1"), b.Point("1"), new Wire(WireId.New(), "1"), core.Id);

        Assert.Equal(
            "connection.ortam_cift",
            ErrorCode(Create(devices: [a, b], connections: [connection], cables: [cable])));
    }

    [Fact]
    public void Connection_with_unknown_cable_core_is_rejected()
    {
        var a = Build.Device("K1", "1");
        var b = Build.Device("K2", "1");
        var connection = new Connection(ConnectionId.New(), a.Point("1"), b.Point("1"), coreId: CableCoreId.New());

        Assert.Equal("connection.damar_yok", ErrorCode(Create(devices: [a, b], connections: [connection])));
    }

    [Fact]
    public void Cable_core_assigned_to_two_connections_is_rejected()
    {
        var a = Build.Device("K1", "1", "2");
        var b = Build.Device("K2", "1", "2");
        var core = new CableCore(CableCoreId.New(), "1");
        var cable = new Cable(CableId.New(), "W1", [core]);
        Connection[] connections =
        [
            new(ConnectionId.New(), a.Point("1"), b.Point("1"), coreId: core.Id),
            new(ConnectionId.New(), a.Point("2"), b.Point("2"), coreId: core.Id),
        ];

        Assert.Equal(
            "core.cift_atama",
            ErrorCode(Create(devices: [a, b], connections: connections, cables: [cable])));
    }

    [Fact]
    public void Potential_source_on_a_missing_point_is_rejected()
    {
        var source = new PotentialSource(PotentialSourceId.New(), "L1", PotentialKind.Phase, ConnectionPointId.New());

        Assert.Equal("potential.uc_yok", ErrorCode(Create(potentialSources: [source])));
    }

    [Fact]
    public void Placement_on_a_missing_page_is_rejected()
    {
        var device = Build.Device("K1", "A1");
        var placement = new Placement(
            PlacementId.New(), PageId.New(), device.Functions[0].Id, PlacementRole.Main, 0, 0);

        Assert.Equal("placement.sayfa_yok", ErrorCode(Create(devices: [device], placements: [placement])));
    }

    [Fact]
    public void Placement_of_a_missing_function_is_rejected()
    {
        var page = new Page(PageId.New(), "1", "Güç");
        var placement = new Placement(PlacementId.New(), page.Id, FunctionId.New(), PlacementRole.Main, 0, 0);

        Assert.Equal("placement.function_yok", ErrorCode(Create(pages: [page], placements: [placement])));
    }

    [Fact]
    public void Placement_with_non_orthogonal_rotation_is_rejected()
    {
        var device = Build.Device("K1", "A1");
        var page = new Page(PageId.New(), "1", "Güç");
        var placement = new Placement(
            PlacementId.New(), page.Id, device.Functions[0].Id, PlacementRole.Main, 0, 0, rotationDeg: 45);

        Assert.Equal(
            "placement.donus_gecersiz",
            ErrorCode(Create(devices: [device], pages: [page], placements: [placement])));
    }

    [Fact]
    public void Function_may_have_one_main_and_several_reference_placements()
    {
        var device = Build.Device("K1", "A1");
        var functionId = device.Functions[0].Id;
        var page = new Page(PageId.New(), "1", "Güç");
        Placement[] placements =
        [
            new(PlacementId.New(), page.Id, functionId, PlacementRole.Main, 10, 10),
            new(PlacementId.New(), page.Id, functionId, PlacementRole.Reference, 50, 10),
            new(PlacementId.New(), page.Id, functionId, PlacementRole.Reference, 90, 10),
        ];

        Assert.True(Create(devices: [device], pages: [page], placements: placements).IsSuccess);
    }

    [Fact]
    public void Function_with_two_main_placements_is_rejected()
    {
        var device = Build.Device("K1", "A1");
        var functionId = device.Functions[0].Id;
        var page = new Page(PageId.New(), "1", "Güç");
        Placement[] placements =
        [
            new(PlacementId.New(), page.Id, functionId, PlacementRole.Main, 10, 10),
            new(PlacementId.New(), page.Id, functionId, PlacementRole.Main, 50, 10),
        ];

        Assert.Equal(
            "placement.ana_tekrar",
            ErrorCode(Create(devices: [device], pages: [page], placements: placements)));
    }

    [Fact]
    public void Function_without_any_placement_is_valid()
    {
        var device = Build.Device("K1", "A1");

        Assert.True(Create(devices: [device]).IsSuccess);
    }
}
