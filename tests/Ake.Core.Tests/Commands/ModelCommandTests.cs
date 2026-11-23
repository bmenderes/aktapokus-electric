using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Yapı düğümü, kablo, potansiyel işareti ve PLC denetleyicisi ekleyen komutlar (ADR-0031).</summary>
public class ModelCommandTests
{
    [Fact]
    public void A_structure_node_joins_its_parent_and_a_missing_or_foreign_parent_is_refused()
    {
        var project = TestData.RefMachine();
        var parent = project.Structure.First(node => node.Aspect == Aspect.Function);
        var node = new StructureNode(StructureNodeId.New(), Aspect.Function, "ALT", parent.Id);

        CommandAssert.RoundTrips(project, new AddStructureNode(node));

        Assert.Equal($"{parent.Name}.ALT", project.StructurePath(node.Id));
        CommandAssert.Rejected(
            project, new AddStructureNode(new StructureNode(StructureNodeId.New(), Aspect.Function, "X", StructureNodeId.New())), "structure.ust_yok");
        var location = project.Structure.First(n => n.Aspect == Aspect.Location);
        CommandAssert.Rejected(
            project, new AddStructureNode(new StructureNode(StructureNodeId.New(), Aspect.Function, "X", location.Id)), "structure.aspect_uyumsuz");
        CommandAssert.Rejected(project, new AddStructureNode(new StructureNode(parent.Id, Aspect.Function, "Y")), "id.tekrar");
    }

    [Fact]
    public void A_cable_with_its_cores_is_added_and_duplicate_ids_are_refused()
    {
        var project = TestData.RefMachine();
        var cable = new Cable(CableId.New(), "W9", [new CableCore(CableCoreId.New(), "1", 1.5m, "BN"), new CableCore(CableCoreId.New(), "2", 1.5m, "BU")], "2x1.5");

        CommandAssert.RoundTrips(project, new AddCable(cable));

        Assert.Contains(cable, project.Cables);
        var existing = project.Cables[0];
        CommandAssert.Rejected(project, new AddCable(new Cable(existing.Id, "W8", [])), "id.tekrar");
        CommandAssert.Rejected(
            project, new AddCable(new Cable(CableId.New(), "W8", [new CableCore(existing.Cores[0].Id, "1")])), "id.tekrar");
    }

    [Fact]
    public void A_potential_marker_needs_an_existing_point()
    {
        var project = TestData.RefMachine();
        var point = project.Point("K1", "A1");
        var marker = new PotentialSource(PotentialSourceId.New(), "TEST24", PotentialKind.DcPositive, point);

        CommandAssert.RoundTrips(project, new AddPotentialSource(marker));

        Assert.Contains(marker, project.PotentialSources);
        CommandAssert.Rejected(
            project,
            new AddPotentialSource(new PotentialSource(PotentialSourceId.New(), "X", PotentialKind.Other, ConnectionPointId.New())),
            "potential.uc_yok");
        CommandAssert.Rejected(project, new AddPotentialSource(new PotentialSource(project.PotentialSources[0].Id, "Y", PotentialKind.Other, point)), "id.tekrar");
    }

    [Fact]
    public void A_controller_comes_with_its_whole_tree_and_needs_devices_that_exist()
    {
        var controllerDevice = new Device(DeviceId.New(), "A1", [new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "1")])]);
        var moduleDevice = new Device(DeviceId.New(), "A2", [new Function(FunctionId.New(), FunctionKind.PlcChannel, [new ConnectionPoint(ConnectionPointId.New(), "1")])]);
        var project = Project.Create(ProjectId.New(), "plc", devices: [controllerDevice, moduleDevice]).Value;
        var channel = new Channel(ChannelId.New(), 1, SignalType.DI, moduleDevice.Functions[0].Id, "%I0.0", "bStart");
        var module = new Module(ModuleId.New(), moduleDevice.Id, 1, [channel]);
        var node = new Node(NodeId.New(), controllerDevice.Id, "Düğüm", 1, [module]);
        var controller = new Controller(ControllerId.New(), controllerDevice.Id, "PLC", [new Network(NetworkId.New(), NetworkKind.EtherCat, "Ağ", [node])]);

        CommandAssert.RoundTrips(project, new AddController(controller));

        Assert.Same(channel, project.Channels.Single());
        var stray = new Controller(ControllerId.New(), DeviceId.New(), "Yok");
        CommandAssert.Rejected(project, new AddController(stray), "plc.device_yok");
        CommandAssert.Rejected(project, new AddController(new Controller(controller.Id, controllerDevice.Id, "Aynı")), "id.tekrar");
    }

    [Fact]
    public void Structure_node_can_be_updated_and_removed_only_when_unused()
    {
        var project = TestData.RefMachine();
        var unused = new StructureNode(StructureNodeId.New(), Aspect.Function, "YEDEK", description: "İlk");
        Assert.True(new AddStructureNode(unused).Execute(project).IsSuccess);

        CommandAssert.RoundTrips(project, new SetStructureNode(unused.Id, "YEDEK2", "Güncel"));
        Assert.Equal(("YEDEK2", "Güncel"), (unused.Name, unused.Description));

        var usedNode = project.Structure.First(node => node.Aspect == Aspect.Function && project.Pages.Any(page => page.FunctionNodeId == node.Id));
        CommandAssert.Rejected(project, new RemoveStructureNode(usedNode.Id), "structure.kullaniliyor");
        CommandAssert.Rejected(project, new SetStructureNode(unused.Id, "A.B", null), "structure.ad_gecersiz");

        CommandAssert.RoundTrips(project, new RemoveStructureNode(unused.Id));
        Assert.DoesNotContain(unused, project.Structure);
    }

    [Fact]
    public void Cable_and_potential_marker_can_be_updated_and_removed_with_undo()
    {
        var project = TestData.RefMachine();
        var cable = project.Cables[0];
        var updated = new Cable(
            cable.Id,
            cable.ProductTag,
            [.. cable.Cores.Select(core => new CableCore(core.Id, core.Designation, 2.5m, core.Color))],
            "4G2.5");

        CommandAssert.RoundTrips(project, new SetCable(updated));
        Assert.Equal("4G2.5", project.Cables[0].Type);
        CommandAssert.Rejected(project, new SetCable(new Cable(cable.Id, "BAD TAG", updated.Cores)), "tag.gecersiz");
        CommandAssert.Rejected(project, new SetCable(new Cable(cable.Id, cable.ProductTag, [new CableCore(CableCoreId.New(), "1"), new CableCore(CableCoreId.New(), "1")])), "cable.damar_gecersiz");

        CommandAssert.RoundTrips(project, new RemoveCable(cable.Id));
        Assert.DoesNotContain(project.Cables, candidate => candidate.Id == cable.Id);
        Assert.DoesNotContain(project.Connections, connection => cable.Cores.Any(core => core.Id == connection.CoreId));

        var marker = project.PotentialSources[0];
        CommandAssert.RoundTrips(project, new RemovePotentialSource(marker.Id));
        Assert.DoesNotContain(marker, project.PotentialSources);
        CommandAssert.Rejected(project, new RemovePotentialSource(PotentialSourceId.New()), "potential.yok");
    }
}
