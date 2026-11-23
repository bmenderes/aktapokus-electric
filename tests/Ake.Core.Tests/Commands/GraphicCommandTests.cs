using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Core.Tests.Commands;

/// <summary>Serbest grafik: ekleme, kaldırma, şekil değiştirme ve dosyaya yazılıp okunması (ADR-0028).</summary>
public class GraphicCommandTests
{
    private static PageGraphic Line(Project project, double x = 10, double y = 20) =>
        new(GraphicId.New(), project.Pages[0].Id, new GraphicPolyline([x, y, x + 30, y]));

    [Fact]
    public void Adding_graphics_is_one_undoable_step_and_changes_no_electrical_model()
    {
        var project = TestData.RefMachine();
        var devices = project.Devices.Count;
        var connections = project.Connections.Count;
        var graphics = new[]
        {
            Line(project),
            new PageGraphic(GraphicId.New(), project.Pages[0].Id, new GraphicCircle(50, 50, 4) { Color = "#ff0000" }),
            new PageGraphic(GraphicId.New(), project.Pages[0].Id, new GraphicText(60, 80, "Not", 3.5, TextAlign.Center)),
        };

        CommandAssert.RoundTrips(project, new AddGraphics(graphics));

        Assert.Equal(3, project.Graphics.Count);
        Assert.Equal((devices, connections), (project.Devices.Count, project.Connections.Count));
    }

    [Fact]
    public void Invalid_graphic_rejects_the_whole_batch()
    {
        var project = TestData.RefMachine();
        var page = project.Pages[0].Id;

        CommandAssert.Rejected(
            project,
            new AddGraphics([Line(project), new PageGraphic(GraphicId.New(), page, new GraphicCircle(0, 0, -1))]),
            "grafik.gecersiz");
        CommandAssert.Rejected(
            project, new AddGraphics([new PageGraphic(GraphicId.New(), PageId.New(), new GraphicText(0, 0, "x"))]), "grafik.sayfa_yok");
        CommandAssert.Rejected(
            project, new AddGraphics([new PageGraphic(GraphicId.New(), page, new GraphicPolyline([1, 2]))]), "grafik.gecersiz");
        CommandAssert.Rejected(
            project, new AddGraphics([new PageGraphic(GraphicId.New(), page, new GraphicText(0, 0, "x") { Color = "kirmizi" })]), "grafik.gecersiz");
        var same = Line(project);
        CommandAssert.Rejected(project, new AddGraphics([same, same]), "id.tekrar");
    }

    [Fact]
    public void Removing_graphics_restores_them_in_their_original_order()
    {
        var project = TestData.RefMachine();
        var all = new[] { Line(project, 0), Line(project, 10), Line(project, 20), Line(project, 30) };
        Assert.True(new CommandStack(project).Execute(new AddGraphics(all), ChangeSource.User, "test").IsSuccess);

        CommandAssert.RoundTrips(project, new RemoveGraphics([all[1].Id, all[3].Id]));

        Assert.Equal([all[0].Id, all[2].Id], project.Graphics.Select(g => g.Id));
        CommandAssert.Rejected(project, new RemoveGraphics([GraphicId.New()]), "grafik.yok");
    }

    [Fact]
    public void Replacing_shapes_moves_and_edits_in_one_step()
    {
        var project = TestData.RefMachine();
        var line = Line(project);
        var note = new PageGraphic(GraphicId.New(), project.Pages[0].Id, new GraphicText(5, 5, "eski"));
        Assert.True(new CommandStack(project).Execute(new AddGraphics([line, note]), ChangeSource.User, "test").IsSuccess);

        CommandAssert.RoundTrips(
            project,
            new ReplaceGraphicShapes(new Dictionary<GraphicId, GraphicShape>
            {
                [line.Id] = line.Shape.Translate(5, -5),
                [note.Id] = new GraphicText(5, 5, "yeni"),
            }));

        Assert.Equal([15.0, 15.0, 45.0, 15.0], ((GraphicPolyline)line.Shape).Points);
        Assert.Equal("yeni", ((GraphicText)note.Shape).Text);
        CommandAssert.Rejected(
            project, new ReplaceGraphicShapes(new Dictionary<GraphicId, GraphicShape> { [note.Id] = new GraphicText(0, 0, " ") }), "grafik.gecersiz");
        CommandAssert.Rejected(
            project, new ReplaceGraphicShapes(new Dictionary<GraphicId, GraphicShape> { [GraphicId.New()] = new GraphicText(0, 0, "x") }), "grafik.yok");
    }

    [Fact]
    public void Graphics_survive_a_save_and_load_and_old_files_still_open()
    {
        var project = TestData.RefMachine();
        var graphics = new[]
        {
            new PageGraphic(GraphicId.New(), project.Pages[1].Id, new GraphicPolyline([0, 0, 10.5, 0, 10.5, 7.25], Closed: true, StrokeMm: 0.5, Filled: true) { Color = "#00ff00" }),
            new PageGraphic(GraphicId.New(), project.Pages[1].Id, new GraphicCircle(3, 4, 1.5)),
            new PageGraphic(GraphicId.New(), project.Pages[1].Id, new GraphicText(1, 2, "Ğüşiöç", 2, TextAlign.Right)),
        };
        Assert.True(new CommandStack(project).Execute(new AddGraphics(graphics), ChangeSource.User, "test").IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json);

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(json, ProjectJson.Serialize(loaded.Value));
        Assert.Contains("\"points\": \"0,0 10.5,0 10.5,7.25\"", json);
        Assert.Equal(graphics.Select(g => g.Shape), loaded.Value.Graphics.Select(g => g.Shape), new ShapeComparer());
        Assert.Equal(6, ProjectJson.CurrentSchemaVersion);
        Assert.DoesNotContain("\"graphics\"", ProjectJson.Serialize(TestData.RefMachine()));
    }

    [Fact]
    public void File_with_a_graphic_on_a_missing_page_or_broken_points_is_refused()
    {
        var project = TestData.RefMachine();
        var graphic = new PageGraphic(GraphicId.New(), project.Pages[0].Id, new GraphicPolyline([0, 0, 1, 1]));
        Assert.True(new CommandStack(project).Execute(new AddGraphics([graphic]), ChangeSource.User, "test").IsSuccess);
        var json = ProjectJson.Serialize(project);

        var at = json.IndexOf("\"graphics\"", StringComparison.Ordinal);
        var missingPage = json[..at] + json[at..].Replace(project.Pages[0].Id.ToString(), PageId.New().ToString(), StringComparison.Ordinal);
        var broken = json.Replace("0,0 1,1", "0,0 bir,1", StringComparison.Ordinal);

        Assert.False(ProjectJson.Deserialize(missingPage).IsSuccess);
        Assert.Equal("grafik.gecersiz", ProjectJson.Deserialize(broken).Error!.Code);
    }

    [Fact]
    public void Bounds_and_translation_follow_the_shape()
    {
        Assert.Equal((2.0, 3.0, 8.0, 4.0), new GraphicPolyline([2, 3, 10, 7]).Bounds);
        Assert.Equal((8.0, 8.0, 4.0, 4.0), new GraphicCircle(10, 10, 2).Bounds);
        var text = new GraphicText(10, 20, "abcd", 4, TextAlign.Center);
        Assert.Equal(text.Bounds.X + 5, text.Translate(5, 0).Bounds.X, 6);
        Assert.Equal(new double[] { 1, 2, 3.5, 4 }, GraphicPolyline.Decode("1,2 3.5,4"));
        Assert.Null(GraphicPolyline.Decode("1;2"));
    }

    private sealed class ShapeComparer : IEqualityComparer<GraphicShape>
    {
        public bool Equals(GraphicShape? x, GraphicShape? y) => (x, y) switch
        {
            (GraphicPolyline a, GraphicPolyline b) => a.Points.SequenceEqual(b.Points)
                && (a.Closed, a.StrokeMm, a.Filled, a.Color) == (b.Closed, b.StrokeMm, b.Filled, b.Color),
            _ => object.Equals(x, y),
        };

        public int GetHashCode(GraphicShape obj) => 0;
    }
}
