using Ake.Core.Model;
using Ake.Library;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Yeni proje, Farklı kaydet ve kaydedilmemiş değişiklik durumu (Faz 6.2, madde 10).</summary>
public sealed class ProjectFileTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-files-");
    private readonly EditorSession _session = new("deneme");

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_directory.FullName, name);

    [Fact]
    public void New_project_has_one_page_on_the_bundled_template_and_no_file_yet()
    {
        Assert.True(_session.NewProject().IsSuccess);

        var project = _session.Project!;
        Assert.Equal("Yeni proje", project.Name);
        var page = Assert.Single(project.Pages);
        Assert.Equal(EditorSession.DefaultTemplate().Value.Id, page.TemplateId);
        Assert.NotNull(_session.DrawingAreaFor(0));
        Assert.True(_session.IsUntitled);
        Assert.False(_session.IsDirty);
        Assert.False(_session.CanUndo);
        Assert.Equal("editor.dosya_yok", _session.Save().Error!.Code);
        Assert.Equal("editor.ad_bos", _session.NewProject("  ").Error!.Code);
    }

    [Fact]
    public void Save_as_writes_a_loadable_file_and_later_saves_go_there()
    {
        Assert.True(_session.NewProject("Pano 7").IsSuccess);
        Assert.True(_session.ChangeTagScheme(TagScheme.PageLetterNumber).IsSuccess);
        Assert.True(_session.IsDirty);

        var path = PathOf("pano-7.json");
        Assert.True(_session.SaveAs(path).IsSuccess);

        Assert.Equal(path, _session.ProjectPath);
        Assert.False(_session.IsUntitled);
        Assert.False(_session.IsDirty);
        Assert.True(File.Exists(AuditLogPath(path)));
        var loaded = ProjectFile.Load(path).Value;
        Assert.Equal(("Pano 7", TagScheme.PageLetterNumber), (loaded.Name, loaded.TagScheme));

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Save().IsSuccess);
        Assert.Equal(TagScheme.LetterNumber, ProjectFile.Load(path).Value.TagScheme);
    }

    [Fact]
    public void Save_as_leaves_the_original_file_untouched()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var original = PathOf("makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), original);
        var before = File.ReadAllText(original);
        Assert.True(_session.Open(original).IsSuccess);
        Assert.True(_session.ChangeTagScheme(TagScheme.PageLetterNumber).IsSuccess);

        var copy = PathOf("kopya.json");
        Assert.True(_session.SaveAs(copy).IsSuccess);

        Assert.Equal(before, File.ReadAllText(original));
        Assert.Equal(TagScheme.PageLetterNumber, ProjectFile.Load(copy).Value.TagScheme);
        Assert.Equal("editor.yol_bos", _session.SaveAs(" ").Error!.Code);
    }

    [Fact]
    public void Window_title_marks_unsaved_changes_and_projects_without_a_file()
    {
        Assert.Equal("aktapokus-electric", MainWindow.WindowTitle(_session));

        Assert.True(_session.NewProject().IsSuccess);
        Assert.Equal("Yeni proje — kaydedilmedi — aktapokus-electric", MainWindow.WindowTitle(_session));

        Assert.True(_session.ChangeTagScheme(TagScheme.PageLetterNumber).IsSuccess);
        Assert.StartsWith("● Yeni proje", MainWindow.WindowTitle(_session), StringComparison.Ordinal);
    }

    [Fact]
    public void New_and_save_as_are_on_the_file_menu_with_their_usual_shortcuts()
    {
        Assert.Equal(
            [EditorCommands.NewProject, EditorCommands.Open, EditorCommands.Save, EditorCommands.SaveAs, EditorCommands.ExportProject, EditorCommands.ProjectProperties, EditorCommands.ImportPdf, EditorCommands.CloseProject, EditorCommands.Exit],
            EditorCommands.FileMenu);
        Assert.Equal("Ctrl+N", EditorCommands.Get(EditorCommands.NewProject).Gesture);
        Assert.Equal("Ctrl+Shift+S", EditorCommands.Get(EditorCommands.SaveAs).Gesture);
        Assert.Equal("Ctrl+W", EditorCommands.Get(EditorCommands.CloseProject).Gesture);
    }

    [Fact]
    public void CloseProject_clears_project_state_caches_and_returns_to_empty_title()
    {
        var libPath = PathOf("kutuphane.db");
        ComponentLibrary.Open(libPath).Value.Dispose();
        Assert.True(_session.OpenLibrary(libPath).IsSuccess);

        Assert.True(_session.NewProject("Kapanacak Proje").IsSuccess);
        Assert.True(_session.ChangeTagScheme(TagScheme.PageLetterNumber).IsSuccess);
        Assert.True(_session.IsDirty);
        Assert.True(_session.CanUndo);

        _session.CloseProject();

        Assert.Null(_session.Project);
        Assert.Null(_session.ProjectPath);
        Assert.Null(_session.Erc);
        Assert.False(_session.IsDirty);
        Assert.False(_session.CanUndo);
        Assert.False(_session.CanRedo);
        Assert.Empty(_session.PageLabels);
        Assert.Empty(_session.DeviceRows());
        Assert.Equal("Proje açık değil", _session.Status);
        Assert.Equal("aktapokus-electric", MainWindow.WindowTitle(_session));
        Assert.Equal(Path.GetFullPath(libPath), _session.LibraryPath);
    }

    [Fact]
    public void Imported_pdf_pages_join_the_project_as_free_graphics_in_one_undoable_step()
    {
        Assert.True(_session.NewProject().IsSuccess);
        var drawing = new PdfDrawing(
            [
                new PdfPageDrawing(1, "0001 Güç", 420, 297, [new GraphicPolyline([100, 100, 120, 100]), new GraphicText(100, 95, "-K1", 3.5)], (15, 27, 390, 240)),
                new PdfPageDrawing(2, null, 420, 297, [new GraphicCircle(50, 50, 4)], (15, 27, 390, 240)),
            ],
            0,
            0);

        var imported = _session.ImportPdf(drawing);

        Assert.True(imported.IsSuccess, imported.Error?.Message);
        Assert.Equal(2, imported.Value);
        Assert.Equal(["1  Sayfa 1", "2  0001 Güç", "3  Sayfa 2"], _session.PageLabels);
        Assert.Equal(3, _session.Project!.Graphics.Count);
        // Çerçeve atılmıştı: içerik şablonun çizim alanına (15, 15) kaydırılır.
        Assert.Equal([100.0, 88.0, 120.0, 88.0], ((GraphicPolyline)_session.Project.Graphics[0].Shape).Points);
        Assert.Equal(2, _session.GraphicBoxesFor(1).Count);
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Single(_session.Project.Pages);
        Assert.Empty(_session.Project.Graphics);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void Importing_a_pdf_needs_an_open_project()
    {
        Assert.Equal("editor.proje_yok", _session.ImportPdf(new PdfDrawing([], 0, 0)).Error!.Code);
        Assert.Contains(EditorCommands.ImportPdf, EditorCommands.FileMenu);
    }

    private static string AuditLogPath(string projectPath) => Ake.Serialization.AuditLog.PathFor(projectPath);
}
