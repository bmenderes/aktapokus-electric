using Ake.Core.Model;

namespace Ake.Core.Tests.Model;

public class ConnectivityTests
{
    [Fact]
    public void Unconnected_point_is_its_own_net()
    {
        var device = Build.Device("K1", "A1", "A2");

        var connectivity = Connectivity.Compute(Build.Project([device]));

        Assert.Equal(2, connectivity.Nets.Count);
        Assert.Equal([device.Point("A1")], connectivity.NetOf(device.Point("A1")).Points);
    }

    [Fact]
    public void Chained_connections_form_one_net()
    {
        var a = Build.Device("K1", "1");
        var b = Build.Device("K2", "1");
        var c = Build.Device("K3", "1");

        var connectivity = Connectivity.Compute(Build.Project(
            [a, b, c],
            [Build.Connect(a, "1", b, "1"), Build.Connect(b, "1", c, "1")]));

        var net = Assert.Single(connectivity.Nets);
        Assert.Equal(3, net.Points.Count);
    }

    [Fact]
    public void Contact_separates_both_net_and_potential()
    {
        var supply = Build.Device("T1", "+");
        var contact = Build.Device("K1", "13", "14");
        var load = Build.Device("H1", "X1");

        var connectivity = Connectivity.Compute(Build.Project(
            [supply, contact, load],
            [Build.Connect(supply, "+", contact, "13"), Build.Connect(contact, "14", load, "X1")],
            [Build.Source("24VDC", supply, "+")]));

        Assert.Equal(2, connectivity.Potentials.Count);
        Assert.Equal("24VDC", connectivity.PotentialOf(contact.Point("13")).Name);
        Assert.Null(connectivity.PotentialOf(contact.Point("14")).Name);
        Assert.NotSame(connectivity.NetOf(contact.Point("13")), connectivity.NetOf(contact.Point("14")));
    }

    [Fact]
    public void Terminal_separates_nets_but_carries_the_potential_through()
    {
        var supply = Build.Device("T1", "+");
        var terminal = Build.Device("X1", passThrough: true, "1.a", "1.b");
        var load = Build.Device("H1", "X1");

        var connectivity = Connectivity.Compute(Build.Project(
            [supply, terminal, load],
            [Build.Connect(supply, "+", terminal, "1.a"), Build.Connect(terminal, "1.b", load, "X1")],
            [Build.Source("24VDC", supply, "+")]));

        Assert.Equal(2, connectivity.Nets.Count);
        var potential = Assert.Single(connectivity.Potentials);
        Assert.Equal("24VDC", potential.Name);
        Assert.Equal(4, potential.Points.Count());
        Assert.Same(potential, connectivity.PotentialOf(load.Point("X1")));
    }

    [Fact]
    public void Potential_without_a_source_has_no_name()
    {
        var a = Build.Device("K1", "1");
        var b = Build.Device("K2", "1");

        var connectivity = Connectivity.Compute(Build.Project([a, b], [Build.Connect(a, "1", b, "1")]));

        var potential = Assert.Single(connectivity.Potentials);
        Assert.Null(potential.Name);
        Assert.False(potential.HasConflict);
    }

    [Fact]
    public void Two_differently_named_sources_on_one_potential_are_a_conflict()
    {
        var plus = Build.Device("T1", "+");
        var minus = Build.Device("T2", "-");

        var connectivity = Connectivity.Compute(Build.Project(
            [plus, minus],
            [Build.Connect(plus, "+", minus, "-")],
            [Build.Source("24VDC", plus, "+"), Build.Source("0V", minus, "-")]));

        var potential = Assert.Single(connectivity.Potentials);
        Assert.True(potential.HasConflict);
        Assert.Null(potential.Name);
        Assert.Equal(["0V", "24VDC"], potential.SourceNames);
    }

    [Fact]
    public void Same_name_on_two_sources_is_not_a_conflict()
    {
        var a = Build.Device("X1", "1");
        var b = Build.Device("X2", "1");

        var connectivity = Connectivity.Compute(Build.Project(
            [a, b],
            [Build.Connect(a, "1", b, "1")],
            [Build.Source("PE", a, "1"), Build.Source("PE", b, "1")]));

        Assert.Equal("PE", Assert.Single(connectivity.Potentials).Name);
    }

    [Fact]
    public void Compute_does_not_change_the_model()
    {
        var a = Build.Device("K1", "1");
        var b = Build.Device("K2", "1");
        var project = Build.Project([a, b], [Build.Connect(a, "1", b, "1")]);

        Connectivity.Compute(project);
        Connectivity.Compute(project);

        Assert.Single(project.Connections);
        Assert.Equal(2, project.Devices.Count);
    }
}
