using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Tel köşe noktaları: oturum tarafı (ADR-0029).</summary>
public sealed class WireRoutingTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-routing-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;

    public WireRoutingTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
        _original = ProjectJson.Serialize(_session.Project!);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private Ake.Render.WirePath FirstWire() => _session.WiresFor(2).First(wire => wire.Path.Count >= 3);

    [Fact]
    public void Corner_points_are_snapped_to_the_grid_and_the_wire_follows_them()
    {
        var wire = FirstWire();

        Assert.True(_session.SetWireRoute(wire.Connection, [new WirePoint(wire.Path[0].X + 33.3, wire.Path[0].Y + 41.9)]).IsSuccess);

        var route = _session.Project!.Connections.Single(c => c.Id == wire.Connection).Route;
        var corner = Assert.Single(route);
        Assert.Equal(0, corner.X % EditorSession.SnapMm);
        Assert.Equal(0, corner.Y % EditorSession.SnapMm);
        Assert.True(_session.HasRoute(wire.Connection));
        Assert.Contains((corner.X, corner.Y), _session.WiresFor(2).Single(w => w.Connection == wire.Connection).Path);
    }

    [Fact]
    public void The_same_route_again_changes_nothing_and_undo_restores_the_file_exactly()
    {
        var wire = FirstWire();
        var corner = new WirePoint(wire.Path[0].X + 35, wire.Path[0].Y + 35);
        Assert.True(_session.SetWireRoute(wire.Connection, [corner]).IsSuccess);
        var audit = _session.CanUndo;

        Assert.True(_session.SetWireRoute(wire.Connection, [corner]).IsSuccess);
        Assert.True(_session.Undo().IsSuccess);

        Assert.True(audit);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void An_empty_route_gives_the_automatic_path_back_and_clearing_a_clear_wire_is_a_no_op()
    {
        var wire = FirstWire();
        Assert.True(_session.SetWireRoute(wire.Connection, []).IsSuccess);
        Assert.False(_session.CanUndo);

        Assert.True(_session.SetWireRoute(wire.Connection, [new WirePoint(wire.Path[0].X + 35, wire.Path[0].Y + 35)]).IsSuccess);
        Assert.True(_session.SetWireRoute(wire.Connection, []).IsSuccess);

        Assert.False(_session.HasRoute(wire.Connection));
        Assert.Equal(wire.Path, _session.WiresFor(2).Single(w => w.Connection == wire.Connection).Path);
    }

    [Fact]
    public void A_new_wire_can_be_drawn_with_corner_points_in_one_step()
    {
        var pins = _session.PinsFor(2);
        var points = _session.Project!.Devices.SelectMany(d => d.Functions).SelectMany(f => f.ConnectionPoints).Select(p => p.Id).ToHashSet();
        var free = pins.Where(pin => _session.Project.Connections.All(c => c.From != pin.Point && c.To != pin.Point) && points.Contains(pin.Point)).Take(2).ToList();
        Assert.Equal(2, free.Count);
        var before = _session.Project.Connections.Count;

        var result = _session.ConnectPins(free[0].Point, free[1].Point, [new WirePoint(250, 150), new WirePoint(250, 200)]);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var added = _session.Project.Connections[^1];
        Assert.Equal(before + 1, _session.Project.Connections.Count);
        Assert.Equal([new WirePoint(250, 150), new WirePoint(250, 200)], added.Route);
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project));
    }

    [Fact]
    public void Ends_of_a_wire_are_known_only_when_both_are_on_the_page_and_the_pin_cache_follows_changes()
    {
        var wire = FirstWire();

        var ends = _session.EndsOf(2, wire.Connection);

        Assert.NotNull(ends);
        Assert.Equal(_session.Project!.Connections.Single(c => c.Id == wire.Connection).From, ends!.Value.From.Point);
        Assert.Null(_session.EndsOf(0, wire.Connection));
        Assert.Null(_session.EndsOf(2, ConnectionId.New()));

        var symbol = _session.BoxesFor(2).First();
        var pinsBefore = _session.PinsFor(2).Select(p => (p.X, p.Y)).ToList();
        Assert.True(_session.MoveMany([symbol.Placement], 10, 0).IsSuccess);
        Assert.NotEqual(pinsBefore, _session.PinsFor(2).Select(p => (p.X, p.Y)).ToList());
    }

    [Fact]
    public void Pins_carry_the_direction_the_wire_leaves_them()
    {
        var pins = _session.PinsFor(2);

        Assert.All(pins, pin => Assert.Equal(1.0, Math.Abs(pin.DirectionX) + Math.Abs(pin.DirectionY)));
    }
}
