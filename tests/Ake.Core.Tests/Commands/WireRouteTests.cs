using Ake.Core.Commands;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Core.Tests.Commands;

/// <summary>Tel köşe noktaları: komut, dosya ve makro (ADR-0029).</summary>
public class WireRouteTests
{
    private static readonly WirePoint[] Corner = [new(100, 40), new(100, 80)];

    [Fact]
    public void Setting_and_clearing_the_route_round_trips_and_keeps_everything_else()
    {
        var project = TestData.RefMachine();
        var connection = project.Connections[0];
        var wire = connection.Wire;

        CommandAssert.RoundTrips(project, new SetWireRoute(connection.Id, Corner));

        Assert.Equal(Corner, connection.Route);
        Assert.Same(wire, connection.Wire);

        CommandAssert.RoundTrips(project, new SetWireRoute(connection.Id, null));
        Assert.Empty(connection.Route);
    }

    [Fact]
    public void Invalid_routes_and_unknown_connections_are_refused()
    {
        var project = TestData.RefMachine();
        var id = project.Connections[0].Id;

        CommandAssert.Rejected(project, new SetWireRoute(ConnectionId.New(), Corner), "connection.yok");
        CommandAssert.Rejected(project, new SetWireRoute(id, [new WirePoint(double.NaN, 1)]), "connection.yol_gecersiz");
        CommandAssert.Rejected(
            project,
            new SetWireRoute(id, [.. Enumerable.Range(0, Project.MaxRoutePoints + 1).Select(i => new WirePoint(i, i))]),
            "connection.yol_gecersiz");
    }

    [Fact]
    public void A_connection_can_be_made_with_a_route_and_a_disconnect_restores_it()
    {
        var project = TestData.RefMachine();
        var free = project.Device("K1").Functions.SelectMany(f => f.ConnectionPoints).First(p => project.Connections.All(c => c.From != p.Id && c.To != p.Id));
        var other = project.Device("K2").Functions.SelectMany(f => f.ConnectionPoints).First(p => project.Connections.All(c => c.From != p.Id && c.To != p.Id));
        var connection = new Connection(ConnectionId.New(), free.Id, other.Id, new Wire(WireId.New()), null, Corner);

        CommandAssert.RoundTrips(project, new Connect(connection));
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new Disconnect(connection.Id), ChangeSource.User, "t").IsSuccess);
        Assert.DoesNotContain(connection, project.Connections);
        Assert.True(stack.Undo().IsSuccess);

        Assert.Contains(connection, project.Connections);
        Assert.Equal(Corner, connection.Route);
    }

    [Fact]
    public void Route_is_saved_as_one_text_survives_a_load_and_is_left_out_when_empty()
    {
        var project = TestData.RefMachine();
        var connection = project.Connections[0];
        Assert.True(new CommandStack(project).Execute(new SetWireRoute(connection.Id, [new(100, 40), new(100.5, 80.25)]), ChangeSource.User, "t").IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Contains("\"route\": \"100,40 100.5,80.25\"", json, StringComparison.Ordinal);
        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal([new WirePoint(100, 40), new WirePoint(100.5, 80.25)], loaded.Connections.Single(c => c.Id == connection.Id).Route);
        Assert.DoesNotContain("\"route\"", ProjectJson.Serialize(TestData.RefMachine()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_with_broken_route_text_is_refused()
    {
        var project = TestData.RefMachine();
        Assert.True(new CommandStack(project).Execute(new SetWireRoute(project.Connections[0].Id, Corner), ChangeSource.User, "t").IsSuccess);
        var json = ProjectJson.Serialize(project);

        var broken = ProjectJson.Deserialize(json.Replace("100,40 100,80", "100;40 bir,80", StringComparison.Ordinal));

        Assert.Equal("connection.yol_gecersiz", broken.Error!.Code);
    }

    [Fact]
    public void A_macro_carries_the_route_relative_to_its_corner_and_places_it_at_the_new_spot()
    {
        var project = TestData.RefMachine();
        var page = project.Pages[1];
        var placed = project.Placements.Where(p => p.PageId == page.Id).ToList();
        var (originX, originY) = (placed.Min(p => p.X), placed.Min(p => p.Y));
        var inside = MacroService.Capture(project, page.Id, "m").Value.Connections.First();
        var wire = project.Connections.Single(c => c.Id == inside.Id);
        Assert.True(new CommandStack(project).Execute(new SetWireRoute(wire.Id, [new(originX + 10, originY + 20)]), ChangeSource.User, "t").IsSuccess);

        var macro = MacroService.Capture(project, page.Id, "m").Value;
        var captured = macro.Connections.Single(c => c.Id == wire.Id);
        var transaction = MacroService.Insert(project, macro, page.Id, 200, 300).Value;
        Assert.True(new CommandStack(project).Execute(transaction, ChangeSource.User, "t").IsSuccess);

        Assert.Equal([new WirePoint(10, 20)], captured.Route);
        Assert.Contains(project.Connections, c => c.Route.Count == 1 && c.Route[0] == new WirePoint(210, 320));
    }
}
