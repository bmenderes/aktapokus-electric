using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Tele tıklayıp seçme ve silme: oturum tarafı (Faz 6.2).</summary>
public sealed class WireSelectionTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-wires-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;

    public WireSelectionTests()
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

    // Çizilen en uzun parçanın ortası: başka bir telin ya da sembolün üstüne düşme ihtimali en düşük nokta.
    private static (double X, double Y) MidOfLongestSegment(Ake.Render.WirePath wire) =>
        Enumerable.Range(1, wire.Path.Count - 1)
            .Select(i => (A: wire.Path[i - 1], B: wire.Path[i]))
            .OrderByDescending(s => Math.Abs(s.B.X - s.A.X) + Math.Abs(s.B.Y - s.A.Y))
            .Select(s => ((s.A.X + s.B.X) / 2, (s.A.Y + s.B.Y) / 2))
            .First();

    [Fact]
    public void Every_wire_drawn_on_the_page_has_a_path_and_can_be_found_by_clicking_on_it()
    {
        var wires = _session.WiresFor(1);

        Assert.NotEmpty(wires);
        Assert.All(wires, wire => Assert.True(wire.Path.Count >= 2));
        var wire = wires[0];
        var (x, y) = MidOfLongestSegment(wire);

        // Başka bir telle çakışan noktada en yakın tel kazanır; bu yüzden yalnızca tele yakınlık denetlenir.
        var found = _session.WireAt(1, x, y);
        Assert.NotNull(found);
        Assert.Contains(wires, w => w.Connection == found!.Connection);
        Assert.Null(_session.WireAt(1, -50, -50));
        Assert.Empty(_session.WiresFor(99));
    }

    [Fact]
    public void Deleting_a_wire_removes_only_that_connection_and_undo_restores_the_file_exactly()
    {
        var wire = _session.WiresFor(1)[0];
        var before = _session.Project!.Connections.Count;

        Assert.True(_session.DeleteSelected([], [], [wire.Connection]).IsSuccess);

        Assert.Equal(before - 1, _session.Project.Connections.Count);
        Assert.DoesNotContain(_session.Project.Connections, c => c.Id == wire.Connection);
        Assert.DoesNotContain(_session.WiresFor(1), w => w.Connection == wire.Connection);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project));
        Assert.Contains(_session.WiresFor(1), w => w.Connection == wire.Connection);
    }

    [Fact]
    public void Wires_and_symbols_are_deleted_together_in_one_undoable_step()
    {
        var wire = _session.WiresFor(1)[0];
        var symbol = _session.BoxesFor(1).First();

        Assert.True(_session.DeleteSelected([symbol.Placement], [], [wire.Connection]).IsSuccess);
        Assert.True(_session.Undo().IsSuccess);

        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Deleting_an_unknown_wire_changes_nothing()
    {
        Assert.False(_session.DeleteSelected([], [], [ConnectionId.New()]).IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }
}
