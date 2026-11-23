using Ake.Render;

namespace Ake.Editor.Tests;

public class EditorSessionTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly string RefMachine = Path.Combine(
        RepositoryRoot(), "examples", "ref-machine", "ref-machine.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    [Fact]
    public void New_session_has_no_project()
    {
        var session = new EditorSession();

        Assert.Null(session.Project);
        Assert.Empty(session.PageLabels);
        Assert.Equal("Proje açık değil", session.Status);
        Assert.Equal("editor.proje_yok", session.SceneFor(0, Today).Error!.Code);
    }

    [Fact]
    public void Opening_a_project_lists_its_pages_and_summarises_it_in_one_line()
    {
        var session = new EditorSession();

        var opened = session.Open(RefMachine);

        Assert.True(opened.IsSuccess, opened.Error?.Message);
        Assert.Equal(
            ["1  Güç dağıtımı", "2  Motor 1", "3  Kontrol", "4  Motor 2", "5  Acil stop"], session.PageLabels);
        Assert.Equal(
            "Referans makine: iki motorlu konveyör  ·  5 sayfa  ·  19 cihaz  ·  0 hata, 0 uyarı", session.Status);
        Assert.DoesNotContain('\n', session.Status);
        Assert.True(Path.IsPathFullyQualified(session.ProjectPath!));
    }

    [Fact]
    public void Path_may_be_pasted_with_quotes_and_spaces_around_it()
    {
        var session = new EditorSession();

        Assert.True(session.Open($"  \"{RefMachine}\"  ").IsSuccess);
    }

    [Fact]
    public void Scene_of_a_page_is_the_same_scene_the_exporters_draw()
    {
        var session = new EditorSession();
        session.Open(RefMachine);

        var scene = session.SceneFor(1, Today).Value;

        var expected = SheetRenderer.Render(session.Project!, session.Project!.Pages[1], Today).Value;
        Assert.Equal(SvgExporter.Write(expected), SvgExporter.Write(scene));
        Assert.Equal((420, 297), (scene.WidthMm, scene.HeightMm));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Asking_for_a_page_that_does_not_exist_fails_without_throwing(int pageIndex)
    {
        var session = new EditorSession();
        session.Open(RefMachine);

        Assert.Equal("editor.sayfa_yok", session.SceneFor(pageIndex, Today).Error!.Code);
    }

    [Fact]
    public void A_failed_open_keeps_the_project_that_was_already_open()
    {
        var session = new EditorSession();
        session.Open(RefMachine);
        var before = session.Project;

        Assert.Equal("proje.dosya_okunamadi", session.Open(Path.Combine(Path.GetTempPath(), $"yok-{Guid.NewGuid()}.json")).Error!.Code);
        Assert.Equal("editor.yol_bos", session.Open("   ").Error!.Code);

        Assert.Same(before, session.Project);
        Assert.Equal(5, session.PageLabels.Count);
    }

    // ---- görünüm dönüşümü

    [Fact]
    public void Fit_centres_the_sheet_and_leaves_a_margin_on_the_tighter_side()
    {
        var view = ViewTransform.Fit(420, 297, 1000, 600, margin: 16);

        Assert.Equal((600 - 32) / 297.0, view.Scale, 9);
        Assert.Equal(16, view.OffsetY, 9);
        Assert.Equal((1000 - (420 * view.Scale)) / 2, view.OffsetX, 9);
    }

    [Theory]
    [InlineData(0, 297, 1000, 600)]
    [InlineData(420, 297, 16, 600)]
    public void Fit_falls_back_to_identity_when_there_is_nothing_to_fit(double sheetW, double sheetH, double viewW, double viewH)
    {
        Assert.Equal(new ViewTransform(1, 0, 0), ViewTransform.Fit(sheetW, sheetH, viewW, viewH));
    }

    [Fact]
    public void Zoom_keeps_the_sheet_point_under_the_cursor_in_place()
    {
        var view = new ViewTransform(2, 50, 30);
        var before = view.ToSheet(400, 250);

        var zoomed = view.ZoomAt(400, 250, 1.2);
        var after = zoomed.ToSheet(400, 250);

        Assert.Equal(2.4, zoomed.Scale, 9);
        Assert.Equal(before.X, after.X, 9);
        Assert.Equal(before.Y, after.Y, 9);
    }

    [Fact]
    public void Zoom_stops_at_its_limits()
    {
        var view = new ViewTransform(1, 0, 0);

        Assert.Equal(ViewTransform.MaxScale, view.ZoomAt(0, 0, 1e6).Scale);
        Assert.Equal(ViewTransform.MinScale, view.ZoomAt(0, 0, 1e-6).Scale);
    }

    [Fact]
    public void Pan_moves_the_view_without_changing_the_scale()
    {
        var view = new ViewTransform(2, 50, 30).Pan(-20, 15);

        Assert.Equal(new ViewTransform(2, 30, 45), view);
        Assert.Equal((10.0, 5.0), view.ToSheet(50, 55));
    }
}
