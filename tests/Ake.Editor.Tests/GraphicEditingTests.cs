using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Serbest grafiğin editörde seçilmesi, taşınması, silinmesi ve yazısının düzenlenmesi (ADR-0028).</summary>
public sealed class GraphicEditingTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-graphics-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;
    private readonly GraphicId _line = GraphicId.New();
    private readonly GraphicId _circle = GraphicId.New();
    private readonly GraphicId _note = GraphicId.New();
    private readonly GraphicId _frame = GraphicId.New();

    public GraphicEditingTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);

        // Boş bir bölgeye: yatay çizgi, daire, yazı ve büyük bir çerçeve (ortası boş).
        var page = _session.Project!.Pages[0].Id;
        Assert.True(new CommandStack(_session.Project).Execute(
            new AddGraphics(
            [
                new PageGraphic(_line, page, new GraphicPolyline([300, 40, 340, 40])),
                new PageGraphic(_circle, page, new GraphicCircle(320, 70, 5)),
                new PageGraphic(_note, page, new GraphicText(300, 100, "Eski not", 3)),
                new PageGraphic(_frame, page, new GraphicPolyline([290, 30, 390, 30, 390, 120, 290, 120], Closed: true)),
            ]),
            ChangeSource.User,
            "hazırlık").IsSuccess);
        _original = ProjectJson.Serialize(_session.Project);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private PageGraphic Find(GraphicId id) => _session.Project!.Graphics.Single(graphic => graphic.Id == id);

    private void AssertUnchanged() => Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));

    [Fact]
    public void A_click_selects_a_line_a_circle_outline_or_text_but_not_empty_space_inside_a_frame()
    {
        Assert.Equal(_line, _session.GraphicAt(0, 320, 40.6)!.Graphic);
        Assert.Equal(_circle, _session.GraphicAt(0, 325, 70)!.Graphic);
        Assert.Equal(_note, _session.GraphicAt(0, 310, 99)!.Graphic);
        Assert.Equal(_frame, _session.GraphicAt(0, 290.5, 80)!.Graphic);

        Assert.Null(_session.GraphicAt(0, 340, 80));
        Assert.Null(_session.GraphicAt(0, 320, 70));
        Assert.Null(_session.GraphicAt(1, 320, 40));
    }

    [Fact]
    public void Window_selection_takes_only_graphics_entirely_inside()
    {
        var inside = _session.GraphicsIn(0, new Box(295, 35, 60, 70));
        var cut = _session.GraphicsIn(0, new Box(295, 35, 10, 70));

        Assert.Equal([_line, _circle, _note], inside.Select(box => box.Graphic));
        Assert.Empty(cut);
    }

    [Fact]
    public void Moving_symbols_and_graphics_together_is_one_undoable_step()
    {
        var motor = _session.BoxesFor(0).First();

        Assert.True(_session.MoveSelected([motor.Placement], [_line, _note], 10.4, -5.2).IsSuccess);

        Assert.Equal([310.0, 35.0, 350.0, 35.0], ((GraphicPolyline)Find(_line).Shape).Points);
        Assert.Equal((310.0, 95.0), (((GraphicText)Find(_note).Shape).X, ((GraphicText)Find(_note).Shape).Y));
        Assert.Equal(motor.Bounds.X + 10, _session.BoxesFor(0).Single(b => b.Placement == motor.Placement).Bounds.X, 3);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void A_move_shorter_than_the_snap_step_changes_nothing()
    {
        var before = _session.CanUndo;

        Assert.True(_session.MoveSelected([], [_line], 1, 1).IsSuccess);

        Assert.Equal(before, _session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void Deleting_graphics_with_a_symbol_is_one_step_and_the_model_is_restored_by_undo()
    {
        var coil = _session.BoxesFor(0).First();

        Assert.True(_session.DeleteSelected([coil.Placement], [_circle, _frame]).IsSuccess);

        Assert.DoesNotContain(_session.Project!.Graphics, graphic => graphic.Id == _circle || graphic.Id == _frame);
        Assert.Equal(2, _session.Project.Graphics.Count);
        Assert.DoesNotContain(_session.Project.Placements, p => p.Id == coil.Placement);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
        Assert.True(_session.DeleteSelected([], []).IsSuccess);
    }

    [Fact]
    public void Editing_text_changes_only_the_text_and_refuses_empty_or_non_text()
    {
        Assert.Equal("Eski not", _session.TextOf(_note));
        Assert.Null(_session.TextOf(_line));

        Assert.True(_session.SetGraphicText(_note, "  Yeni not  ").IsSuccess);
        Assert.Equal("Yeni not", _session.TextOf(_note));
        Assert.Equal(3, ((GraphicText)Find(_note).Shape).SizeMm);

        Assert.Equal("grafik.gecersiz", _session.SetGraphicText(_note, "  ").Error!.Code);
        Assert.Equal("grafik.yazi_degil", _session.SetGraphicText(_line, "x").Error!.Code);
        Assert.True(_session.SetGraphicText(_note, "Yeni not").IsSuccess);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
        Assert.False(_session.CanUndo);
    }
}
