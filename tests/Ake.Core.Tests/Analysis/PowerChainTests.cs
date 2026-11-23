using Ake.Core.Analysis;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Tests.Commands;

namespace Ake.Core.Tests.Analysis;

/// <summary>Güç zinciri: beslemeden korumaya, anahtarlamaya ve yüke inen ağaç (ADR-0039).</summary>
public class PowerChainTests
{
    private static string Tags(PowerNode node) =>
        node.Children.Count == 0 ? node.Device.ProductTag : $"{node.Device.ProductTag}({string.Join(",", node.Children.Select(Tags))})";

    [Fact]
    public void The_reference_machine_chain_goes_from_the_mains_terminals_through_the_main_switch_to_both_motors_and_the_supply()
    {
        var project = TestData.RefMachine();

        var roots = PowerChain.Build(project);

        var root = Assert.Single(roots);
        Assert.Equal((PowerElementKind.Source, "X0"), (root.Kind, root.Device.ProductTag));
        Assert.Equal(["L1", "L2", "L3", "N", "PE"], root.Potentials);
        Assert.Equal("X0(Q1(F1(K1(M1)),F2(K2(M2)),F3(T1)))", Tags(root));
    }

    [Fact]
    public void Elements_carry_their_role_rating_wire_section_and_the_cable_of_the_last_leg()
    {
        var project = TestData.RefMachine();
        var q1 = PowerChain.Build(project)[0].Children.Single();
        var f1 = q1.Children.Single(node => node.Device.ProductTag == "F1");
        var k1 = f1.Children.Single();
        var m1 = k1.Children.Single();
        var t1 = q1.Children.Single(node => node.Device.ProductTag == "F3").Children.Single();

        Assert.Equal((PowerElementKind.Switch, 2.5m), (q1.Kind, q1.CrossSectionMm2));
        Assert.Equal((PowerElementKind.Protection, 6.3m), (f1.Kind, f1.RatedCurrentA));
        Assert.Equal(PowerElementKind.Switch, k1.Kind);
        Assert.Equal((PowerElementKind.Load, "-W1 4G1.5"), (m1.Kind, m1.Cable));
        Assert.Equal(PowerElementKind.Load, t1.Kind);
        Assert.Equal(1.5m, q1.Children.Single(node => node.Device.ProductTag == "F3").CrossSectionMm2);
    }

    [Fact]
    public void Control_devices_never_enter_the_chain_and_a_project_without_a_source_has_no_chain()
    {
        var project = TestData.RefMachine();
        var all = PowerChain.Build(project).SelectMany(Flatten).Select(node => node.Device.ProductTag).ToList();

        Assert.DoesNotContain("S0", all);
        Assert.DoesNotContain("K0", all);
        Assert.DoesNotContain("A11", all);

        var unfed = Project.Create(
            ProjectId.New(), "Beslemesiz", structure: project.Structure, devices: project.Devices, connections: project.Connections, cables: project.Cables).Value;

        Assert.Empty(PowerChain.Build(unfed));
    }

    private static IEnumerable<PowerNode> Flatten(PowerNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    [Fact]
    public void A_multi_way_terminal_passes_the_chain_on_from_every_point_of_the_other_side()
    {
        var project = TestData.RefMachine();
        var x1 = project.Devices.Single(device => device.ProductTag == "X1");
        var k1Out = project.Connections.Single(c => project.Locate(c.To)!.Device == x1 && project.Locate(c.From)!.Device.ProductTag == "K1" && project.Locate(c.From)!.Point.Name == "2");
        var wide = AddTerminal.Create("W", ways: 2);
        new AddTerminal(x1.Id, 0, wide).Execute(project);
        var m1 = project.Devices.Single(device => device.ProductTag == "M1");
        var m2 = project.Devices.Single(device => device.ProductTag == "M2");

        // K1:2 → geniş klemensin iç noktası; klemensin dış tarafındaki iki noktadan iki motora.
        new Disconnect(k1Out.Id).Execute(project);
        Assert.True(new Connect(new Connection(ConnectionId.New(), k1Out.From, wide.ConnectionPoints[0].Id)).Execute(project).IsSuccess);
        Assert.True(new Connect(new Connection(ConnectionId.New(), wide.ConnectionPoints[2].Id, m1.Functions[0].ConnectionPoints[0].Id)).Execute(project).IsSuccess);
        Assert.True(new Connect(new Connection(ConnectionId.New(), wide.ConnectionPoints[3].Id, m2.Functions[0].ConnectionPoints[1].Id)).Execute(project).IsSuccess);

        var k1 = PowerChain.Build(project)[0].Children.Single().Children.Single(node => node.Device.ProductTag == "F1").Children.Single();

        Assert.Equal(["M1", "M2"], k1.Children.Select(node => node.Device.ProductTag).Order());
    }
}
