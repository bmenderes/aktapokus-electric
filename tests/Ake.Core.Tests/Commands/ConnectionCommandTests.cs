using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

public class ConnectionCommandTests
{
    private static CableCoreId Core(Project project, string cable, string core) =>
        project.Cables.Single(c => c.ProductTag == cable).Cores.Single(c => c.Designation == core).Id;

    [Fact]
    public void Connect_adds_a_wired_connection()
    {
        var project = TestData.RefMachine();
        var connection = new Connection(
            ConnectionId.New(),
            project.Point("K1", "21"),
            project.Point("A11", "8"),
            new Wire(WireId.New(), "57", 0.75m, "DBU"));

        CommandAssert.RoundTrips(project, new Connect(connection));

        Assert.Equal(66, project.Connections.Count);
        Assert.Same(connection, project.Connections[^1]);
    }

    [Fact]
    public void Connect_rejects_invalid_connections()
    {
        var project = TestData.RefMachine();
        var a = project.Point("K1", "21");
        var b = project.Point("A11", "8");
        var existing = project.Connections[0];

        CommandAssert.Rejected(
            project, new Connect(new Connection(ConnectionId.New(), a, ConnectionPointId.New())), "connection.uc_yok");
        CommandAssert.Rejected(project, new Connect(new Connection(ConnectionId.New(), a, a)), "connection.kendine");
        CommandAssert.Rejected(project, new Connect(new Connection(existing.Id, a, b)), "id.tekrar");
        CommandAssert.Rejected(
            project,
            new Connect(new Connection(ConnectionId.New(), a, b, new Wire(existing.Wire!.Id, "1"))),
            "id.tekrar");
        CommandAssert.Rejected(
            project,
            new Connect(new Connection(ConnectionId.New(), a, b, new Wire(WireId.New()), Core(project, "W1", "1"))),
            "connection.ortam_cift");
        CommandAssert.Rejected(
            project,
            new Connect(new Connection(ConnectionId.New(), a, b, coreId: Core(project, "W1", "1"))),
            "core.cift_atama");
        CommandAssert.Rejected(
            project,
            new Connect(new Connection(ConnectionId.New(), a, b, coreId: CableCoreId.New())),
            "connection.damar_yok");
    }

    [Fact]
    public void Disconnect_removes_the_connection_and_undo_puts_it_back_in_place()
    {
        var project = TestData.RefMachine();
        var middle = project.Connections[30];

        CommandAssert.RoundTrips(project, new Disconnect(middle.Id));

        Assert.Equal(64, project.Connections.Count);
        Assert.DoesNotContain(middle, project.Connections);
    }

    [Fact]
    public void Disconnect_rejects_an_unknown_connection()
    {
        CommandAssert.Rejected(TestData.RefMachine(), new Disconnect(ConnectionId.New()), "connection.yok");
    }

    [Fact]
    public void AssignWire_can_renumber_the_existing_wire()
    {
        var project = TestData.RefMachine();
        var connection = project.Connections[0];
        var wire = connection.Wire!;

        CommandAssert.RoundTrips(
            project,
            new AssignWire(connection.Id, new Wire(wire.Id, "L1-1", wire.CrossSectionMm2, wire.Color), null));

        Assert.Equal("L1-1", connection.Wire!.Number);
        Assert.Equal(wire.Id, connection.Wire.Id);
    }

    [Fact]
    public void AssignWire_can_clear_the_medium()
    {
        var project = TestData.RefMachine();
        var connection = project.Connections[0];

        CommandAssert.RoundTrips(project, new AssignWire(connection.Id, null, null));

        Assert.Null(connection.Wire);
        Assert.Null(connection.CoreId);
    }

    [Fact]
    public void AssignWire_can_move_a_freed_cable_core_to_another_connection()
    {
        var project = TestData.RefMachine();
        var core = Core(project, "W1", "1");
        var viaCable = project.Connection("X1", "1.b", "M1", "U1");
        var wired = project.Connections[0];
        new Disconnect(viaCable.Id).Execute(project);

        CommandAssert.RoundTrips(project, new AssignWire(wired.Id, null, core));

        Assert.Equal(core, wired.CoreId);
        Assert.Null(wired.Wire);
    }

    [Fact]
    public void AssignWire_rejects_invalid_media()
    {
        var project = TestData.RefMachine();
        var connection = project.Connections[0];
        var otherWire = project.Connections[1].Wire!;
        var usedCore = Core(project, "W1", "1");

        CommandAssert.Rejected(project, new AssignWire(ConnectionId.New(), null, null), "connection.yok");
        CommandAssert.Rejected(
            project, new AssignWire(connection.Id, new Wire(WireId.New()), usedCore), "connection.ortam_cift");
        CommandAssert.Rejected(project, new AssignWire(connection.Id, null, usedCore), "core.cift_atama");
        CommandAssert.Rejected(project, new AssignWire(connection.Id, null, CableCoreId.New()), "connection.damar_yok");
        CommandAssert.Rejected(project, new AssignWire(connection.Id, new Wire(otherWire.Id, "1"), null), "id.tekrar");
    }

    [Fact]
    public void Reassigning_the_same_core_to_its_own_connection_is_allowed()
    {
        var project = TestData.RefMachine();
        var viaCable = project.Connection("X1", "1.b", "M1", "U1");

        var result = new AssignWire(viaCable.Id, null, viaCable.CoreId).Execute(project);

        Assert.True(result.IsSuccess, result.Error?.Message);
    }
}
