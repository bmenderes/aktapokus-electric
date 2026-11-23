using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Kavşak noktası (ADR-0032): telin ortasında teller buluşur.</summary>
public class JunctionTests
{
    private static (Project Project, Connection Wire) WiredProject()
    {
        var project = TestData.RefMachine();
        return (project, project.Connections.First(c => c.Wire?.Number is not null && c.CoreId is null));
    }

    [Fact]
    public void Split_replaces_a_wire_by_two_through_a_junction_and_keeps_the_potential()
    {
        var (project, wire) = WiredProject();
        var page = project.Pages[0].Id;
        var count = project.Connections.Count;
        var netBefore = Connectivity.Compute(project).NetOf(wire.From);
        Assert.Contains(wire.To, netBefore.Points);

        var split = Junctions.Split(project, wire.Id, page, 50, 70);
        Assert.True(split.IsSuccess, split.Error?.Message);
        CommandAssert.RoundTrips(project, split.Value.Command);

        var junction = project.Devices.Single(device => device.Kind == DeviceKind.Junction);
        Assert.Equal("J1", junction.ProductTag);
        Assert.Equal(FunctionKind.Junction, Assert.Single(junction.Functions).Kind);
        Assert.Equal(count + 1, project.Connections.Count);
        Assert.Null(project.FindConnection(wire.Id));
        var halves = project.Connections.Where(c => c.From == split.Value.Point || c.To == split.Value.Point).ToList();
        Assert.Equal(2, halves.Count);
        Assert.All(halves, half => Assert.Equal(wire.Wire!.Number, half.Wire!.Number));
        Assert.NotEqual(halves[0].Wire!.Id, halves[1].Wire!.Id);
        var net = Connectivity.Compute(project).NetOf(wire.From);
        Assert.Contains(wire.To, net.Points);
        Assert.Contains(split.Value.Point, net.Points);
        var placement = Assert.Single(project.Placements, p => p.FunctionId == junction.Functions[0].Id);
        Assert.Equal((page, 50.0, 70.0), (placement.PageId, placement.X, placement.Y));
    }

    [Fact]
    public void A_third_wire_to_the_junction_joins_the_same_potential_and_numbers_stay_consistent()
    {
        var (project, wire) = WiredProject();
        var other = project.Connections.Where(c => c != wire).SelectMany(c => new[] { c.From, c.To })
            .First(point => !Connectivity.Compute(project).NetOf(wire.From).Points.Contains(point));
        var split = Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        Assert.True(new CommandStack(project).Execute(split.Command, ChangeSource.User, "test").IsSuccess);

        CommandAssert.RoundTrips(project, new Connect(new Connection(ConnectionId.New(), split.Point, other, new Wire(WireId.New()))));

        var net = Connectivity.Compute(project).NetOf(wire.From);
        Assert.Contains(other, net.Points);
        Assert.Contains(wire.To, net.Points);
    }

    [Fact]
    public void Junction_adds_no_erc_finding()
    {
        var (project, wire) = WiredProject();
        var before = ErcEngine.Default.Run(project).Findings.Count;

        var split = Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        Assert.True(new CommandStack(project).Execute(split.Command, ChangeSource.User, "test").IsSuccess);

        Assert.Equal(before, ErcEngine.Default.Run(project).Findings.Count);
    }

    [Fact]
    public void Split_rejects_a_cable_core_connection_and_a_missing_connection()
    {
        var project = TestData.RefMachine();
        var core = project.Connections.First(c => c.CoreId is not null);

        Assert.Equal("kavsak.damar", Junctions.Split(project, core.Id, project.Pages[0].Id, 1, 1).Error?.Code);
        Assert.Equal("connection.yok", Junctions.Split(project, ConnectionId.New(), project.Pages[0].Id, 1, 1).Error?.Code);
    }

    [Fact]
    public void Junction_tags_use_the_smallest_free_number_and_removing_one_removes_its_wires()
    {
        var (project, wire) = WiredProject();
        var (first, _) = Junctions.Create(project, project.Pages[0].Id, 10, 10);
        Assert.True(new CommandStack(project).Execute(first, ChangeSource.User, "test").IsSuccess);
        var (second, _) = Junctions.Create(project, project.Pages[0].Id, 20, 10);
        CommandAssert.RoundTrips(project, second);
        Assert.Equal(["J1", "J2"], project.Devices.Where(d => d.Kind == DeviceKind.Junction).Select(d => d.ProductTag).Order());

        var split = Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        Assert.True(new CommandStack(project).Execute(split.Command, ChangeSource.User, "test").IsSuccess);
        var junction = project.Locate(split.Point)!.Device;
        CommandAssert.RoundTrips(project, new RemoveDevice(junction.Id));
        Assert.DoesNotContain(project.Connections, c => c.From == split.Point || c.To == split.Point);
    }
}
