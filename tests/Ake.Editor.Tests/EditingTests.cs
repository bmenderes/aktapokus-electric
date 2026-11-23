using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>
/// Düzenleme, oturum üzerinden. Her test referans makinenin geçici bir kopyasıyla çalışır; depodaki
/// örnek dosyaya yazılmaz.
/// </summary>
public sealed class EditingTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-test-");
    private readonly string _path;
    private readonly string _original;
    private readonly EditorSession _session = new("deneme");

    public EditingTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        _path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), _path);
        _original = File.ReadAllText(_path);
        Assert.True(_session.Open(_path).IsSuccess);
    }

    private string AuditPath => Path.Combine(_directory.FullName, "makine.audit.jsonl");

    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>İkinci sayfadaki (Motor 1) motor sembolü.</summary>
    private PlacedBox Motor()
    {
        var motor = _session.Project!.Devices.Single(device => device.ProductTag == "M1").Functions[0];
        var placement = _session.Project.Placements.Single(candidate => candidate.FunctionId == motor.Id);
        return _session.BoxesFor(1).Single(box => box.Placement == placement.Id);
    }

    private Placement PlacementOf(PlacedBox box) =>
        _session.Project!.Placements.Single(placement => placement.Id == box.Placement);

    // ---- seçim

    [Fact]
    public void Clicking_inside_a_symbol_selects_it_and_clicking_empty_space_selects_nothing()
    {
        var motor = Motor();
        var centreX = motor.Bounds.X + (motor.Bounds.Width / 2);
        var centreY = motor.Bounds.Y + (motor.Bounds.Height / 2);

        Assert.Equal(motor.Placement, _session.HitTest(1, centreX, centreY)!.Placement);
        Assert.Null(_session.HitTest(1, 380, 240));
        Assert.Null(_session.HitTest(0, centreX, centreY + 60));
    }

    [Fact]
    public void Every_placement_on_a_page_has_a_box_inside_the_sheet()
    {
        var boxes = _session.BoxesFor(1);
        var onPage = _session.Project!.Placements.Count(placement => placement.PageId == _session.Project.Pages[1].Id);

        Assert.Equal(onPage, boxes.Count);
        Assert.All(boxes, box => Assert.InRange(box.Bounds.X, 0, 420));
        Assert.Empty(_session.BoxesFor(99));
    }

    // ---- taşıma ve döndürme

    [Fact]
    public void Moving_a_symbol_snaps_the_distance_and_keeps_every_connection()
    {
        var motor = Motor();
        var before = PlacementOf(motor);
        var (x, y) = (before.X, before.Y);
        var connections = ProjectJsonConnections();

        var moved = _session.Move(motor.Placement, 58.6, -11.9);

        Assert.True(moved.IsSuccess, moved.Error?.Message);
        Assert.Equal((x + 60, y - 10), (before.X, before.Y));
        Assert.Equal(motor.Bounds.X + 60, Motor().Bounds.X, 6);
        Assert.Equal(connections, ProjectJsonConnections());
        Assert.True(_session.Project!.CheckIntegrity().IsSuccess);
        Assert.True(_session.IsDirty);
        Assert.EndsWith("kaydedilmedi", _session.Status);
    }

    [Fact]
    public void A_drag_shorter_than_half_a_snap_step_changes_nothing_and_leaves_no_history()
    {
        var moved = _session.Move(Motor().Placement, 2.4, -2.4);

        Assert.True(moved.IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.False(_session.IsDirty);
    }

    [Theory]
    [InlineData(2.4, 0, 0, 0)]
    [InlineData(2.6, -7.4, 5, -5)]
    [InlineData(-12.5, 12.5, -10, 10)]
    public void Snap_rounds_to_the_nearest_step(double dx, double dy, double expectedX, double expectedY)
    {
        Assert.Equal((expectedX, expectedY), EditorSession.Snap(dx, dy));
    }

    [Fact]
    public void Grid_area_is_the_drawing_area_of_the_page()
    {
        var area = _session.DrawingAreaFor(1);

        Assert.Equal(new Box(15, 15, 390, 252), area);
        Assert.Null(_session.DrawingAreaFor(99));
        Assert.Null(new EditorSession().DrawingAreaFor(0));
    }

    [Fact]
    public void Rotating_four_times_brings_a_symbol_back()
    {
        var motor = Motor();
        var placement = PlacementOf(motor);

        var angles = new List<int>();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(_session.Rotate(motor.Placement).IsSuccess);
            angles.Add(placement.RotationDeg);
        }

        Assert.Equal([90, 180, 270, 0], angles);
        Assert.Equal(motor.Bounds, Motor().Bounds);
    }

    [Fact]
    public void Scene_follows_the_edit()
    {
        var before = SvgExporter.Write(_session.SceneFor(1, Today).Value);

        _session.Move(Motor().Placement, 50, 0);

        Assert.NotEqual(before, SvgExporter.Write(_session.SceneFor(1, Today).Value));
    }

    [Fact]
    public void Editing_an_unknown_placement_fails_without_throwing()
    {
        Assert.Equal("placement.yok", _session.Move(PlacementId.New(), 10, 10).Error!.Code);
        Assert.Equal("placement.yok", _session.Rotate(PlacementId.New()).Error!.Code);
        Assert.Equal("editor.proje_yok", new EditorSession().Move(PlacementId.New(), 10, 10).Error!.Code);
    }

    // ---- geri alma

    [Fact]
    public void Undo_and_redo_walk_the_edit_history()
    {
        var motor = Motor();
        var start = ProjectJson.Serialize(_session.Project!);
        _session.Move(motor.Placement, 50, 0);
        var afterMove = ProjectJson.Serialize(_session.Project!);
        _session.Rotate(motor.Placement);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(afterMove, ProjectJson.Serialize(_session.Project!));
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(start, ProjectJson.Serialize(_session.Project!));
        Assert.False(_session.CanUndo);
        Assert.Equal("komut.geri_alinacak_yok", _session.Undo().Error!.Code);

        Assert.True(_session.Redo().IsSuccess);
        Assert.Equal(afterMove, ProjectJson.Serialize(_session.Project!));
        Assert.True(_session.CanRedo);
    }

    // ---- kaydetme

    [Fact]
    public void Saving_writes_the_project_and_appends_what_happened_to_the_audit_log()
    {
        var motor = Motor();
        _session.Move(motor.Placement, 50, 0);
        _session.Rotate(motor.Placement);
        _session.Undo();

        var saved = _session.Save();

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.False(_session.IsDirty);
        Assert.NotEqual(_original, File.ReadAllText(_path));
        Assert.Equal(ProjectJson.Serialize(_session.Project!), File.ReadAllText(_path));

        var lines = File.ReadAllLines(AuditPath);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => Assert.Contains("\"actor\":\"deneme\"", line));
        Assert.Contains("\"action\":\"Undo\"", lines[2]);
    }

    [Fact]
    public void Saving_twice_does_not_repeat_audit_entries()
    {
        _session.Move(Motor().Placement, 50, 0);
        _session.Save();
        _session.Save();
        _session.Move(Motor().Placement, 0, 20);
        _session.Save();

        Assert.Equal(2, File.ReadAllLines(AuditPath).Length);
    }

    [Fact]
    public void Saving_an_untouched_project_rewrites_the_same_file_and_creates_no_audit_log()
    {
        Assert.True(_session.Save().IsSuccess);

        Assert.Equal(_original.ReplaceLineEndings("\n"), File.ReadAllText(_path));
        Assert.False(File.Exists(AuditPath));
    }

    [Fact]
    public void Saved_project_reopens_with_the_edit_in_place()
    {
        var motor = Motor();
        _session.Move(motor.Placement, 50, 0);
        _session.Save();

        var reopened = new EditorSession();
        Assert.True(reopened.Open(_path).IsSuccess);

        Assert.Equal(
            PlacementOf(motor).X,
            reopened.Project!.Placements.Single(placement => placement.Id == motor.Placement).X);
        Assert.False(reopened.IsDirty);
    }

    private string ProjectJsonConnections() =>
        string.Join('|', _session.Project!.Connections.Select(connection => $"{connection.From}>{connection.To}"));

    [Fact]
    public void Changing_the_tag_scheme_relabels_the_drawing_and_undoes_in_one_step()
    {
        Assert.True(_session.ChangeTagScheme(TagScheme.PageLetterNumber).IsSuccess);

        Assert.Equal(TagScheme.PageLetterNumber, _session.Project!.TagScheme);
        Assert.Contains(_session.SceneFor(1, Today).Value.Items.OfType<TextItem>(), text => text.Text == "-2K1");
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(TagScheme.LetterNumber, _session.Project.TagScheme);
        Assert.Contains(_session.SceneFor(1, Today).Value.Items.OfType<TextItem>(), text => text.Text == "-K1");
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void Renumbering_wires_is_one_undoable_step()
    {
        Assert.True(_session.RenumberWires().IsSuccess);
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Renumbering_tags_that_are_already_in_order_leaves_nothing_to_undo()
    {
        Assert.True(_session.RenumberTags().IsSuccess);
        Assert.True(_session.RenumberTags().IsSuccess);

        // İkinci çağrı değişiklik üretmez; geri alınacak tek adım ilkidir.
        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Page_macro_inserted_on_another_page_is_one_undoable_step()
    {
        var macro = _session.MacroFromPage(1).Value;
        var devices = _session.Project!.Devices.Count;

        Assert.Equal("Motor 1", macro.Name);
        Assert.True(_session.InsertMacro(macro, 3).IsSuccess);
        Assert.Equal(devices + macro.Devices.Count, _session.Project.Devices.Count);
        Assert.Contains(_session.Project.Devices, device => device.ProductTag == "K3");
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project));
    }

    [Fact]
    public void Selection_macro_takes_the_selected_device_and_a_position_is_snapped()
    {
        var macro = _session.MacroFromSelection(Motor().Placement).Value;

        Assert.Equal("-M1", macro.Name);
        Assert.Equal(["M1"], macro.Devices.Select(device => device.ProductTag));

        Assert.True(_session.InsertMacro(macro, 0, 101.3, 52.2).IsSuccess);
        var added = _session.Project!.Devices[^1].Functions[0].Id;
        var placement = _session.Project.Placements.Single(p => p.FunctionId == added);
        Assert.Equal(EditorSession.Snap(101.3, 52.2), (placement.X, placement.Y));
    }

    [Fact]
    public void Macro_needs_an_open_page()
    {
        Assert.Equal("editor.sayfa_yok", _session.MacroFromPage(99).Error!.Code);
        Assert.Equal("editor.proje_yok", new EditorSession("x").MacroFromPage(0).Error!.Code);
    }
}
