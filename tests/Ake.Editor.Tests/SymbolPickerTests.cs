using Ake.Core.Model;
using Ake.Library;

namespace Ake.Editor.Tests;

/// <summary>Editörün sembol seçicisi, oturum üzerinden; geçici proje ve kütüphaneyle.</summary>
public sealed class SymbolPickerTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-picker-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _libraryPath;
    private readonly SymbolDefinition _lamp;
    private readonly SymbolDefinition _breaker;

    public SymbolPickerTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var project = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), project);
        Assert.True(_session.Open(project).IsSuccess);

        _lamp = new SymbolDefinition(
            SymbolId.New(),
            "Lamba",
            [new SymbolCircle(0, 7.5, 4)],
            [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)],
            "test",
            "CC0");
        _breaker = new SymbolDefinition(
            SymbolId.New(),
            "Şalter 3p",
            [new SymbolRect(-2, 2, 24, 10)],
            [.. Enumerable.Range(0, 6).Select(i => new SymbolPin((i / 2) * 10, i % 2 == 0 ? 0 : 15, 0, i % 2 == 0 ? -1 : 1))],
            "test",
            "CC0");

        _libraryPath = Path.Combine(_directory.FullName, "library.db");
        using var library = ComponentLibrary.Open(_libraryPath).Value;
        library.ImportSymbols([new LibrarySymbol(_lamp, "sinyal"), new LibrarySymbol(_breaker, "koruma")]);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private PlacementId CoilPlacement()
    {
        var coil = _session.Project!.Devices.Single(device => device.ProductTag == "K1").Functions.Single(f => f.Kind == FunctionKind.Coil);
        return _session.Project.Placements.Single(placement => placement.FunctionId == coil.Id).Id;
    }

    [Fact]
    public void Without_a_library_the_picker_is_empty_and_assigning_fails_cleanly()
    {
        Assert.Null(_session.LibraryPath);
        Assert.Empty(_session.SearchLibrary(""));
        Assert.Null(_session.PreviewOf(_lamp.Id));
        Assert.Equal("kutuphane.acik_degil", _session.AssignFromLibrary(CoilPlacement(), _lamp.Id).Error!.Code);
    }

    [Fact]
    public void Opening_a_missing_library_keeps_the_picker_closed()
    {
        Assert.Equal("kutuphane.yok", _session.OpenLibrary(Path.Combine(_directory.FullName, "yok.db")).Error!.Code);
        Assert.Null(_session.LibraryPath);
        Assert.False(File.Exists(Path.Combine(_directory.FullName, "yok.db")));
    }

    [Fact]
    public void Search_lists_symbols_and_can_keep_only_those_that_fit_the_selected_function()
    {
        Assert.True(_session.OpenLibrary(_libraryPath).IsSuccess);

        Assert.Equal(2, _session.LibrarySymbolCount);
        Assert.Equal(2, _session.SearchLibrary("").Count);
        Assert.Equal(["Lamba  ·  2 uç  ·  sinyal"], _session.SearchLibrary("", pins: 2).Select(row => row.Label));
        Assert.Equal([_breaker.Id], _session.SearchLibrary("şalter").Select(row => row.Id));
    }

    [Fact]
    public void Preview_shows_the_library_symbol()
    {
        _session.OpenLibrary(_libraryPath);

        var preview = _session.PreviewOf(_lamp.Id)!;

        Assert.Contains(preview.Items.OfType<Ake.Render.CircleItem>(), circle => circle.Radius == 4);

        // Kutu: sembol 15 mm yüksek, 8 mm geniş; her yanda 5 mm pay.
        Assert.Equal((18.0, 25.0), (preview.WidthMm, preview.HeightMm));
        Assert.All(preview.Items.OfType<Ake.Render.LineItem>(), line => Assert.InRange(line.Y2, 0, preview.HeightMm));
    }

    [Fact]
    public void Assigning_from_the_library_copies_the_symbol_into_the_project_in_one_undoable_step()
    {
        _session.OpenLibrary(_libraryPath);
        var placement = CoilPlacement();
        var coil = _session.FunctionOf(placement)!;

        Assert.True(_session.AssignFromLibrary(placement, _lamp.Id).IsSuccess);
        Assert.Equal(_lamp.Id, coil.SymbolId);
        Assert.Equal("Lamba", Assert.Single(_session.Project!.Symbols).Name);
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Null(coil.SymbolId);
        Assert.Empty(_session.Project.Symbols);
    }

    [Fact]
    public void A_library_symbol_that_does_not_fit_the_function_changes_nothing()
    {
        _session.OpenLibrary(_libraryPath);

        var result = _session.AssignFromLibrary(CoilPlacement(), _breaker.Id);

        Assert.Equal("sembol.uc_uyumsuz", result.Error!.Code);
        Assert.Empty(_session.Project!.Symbols);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void Placing_a_library_symbol_with_rotation_and_adding_free_text_both_undo_cleanly()
    {
        Assert.True(_session.OpenLibrary(_libraryPath).IsSuccess);

        var placed = _session.PlaceLibrarySymbol(_lamp.Id, 1, 100, 80, rotationDeg: 90);
        Assert.True(placed.IsSuccess, placed.Error?.Message);
        var placement = _session.Project!.Placements.Single(p => p.Id == placed.Value);
        Assert.Equal(90, placement.RotationDeg);

        var text = _session.AddFreeText(1, 120, 90, "  Motor besleme  ");
        Assert.True(text.IsSuccess, text.Error?.Message);
        Assert.Contains(_session.Project.Graphics, g => g.Id == text.Value && ((GraphicText)g.Shape).Text == "Motor besleme");

        Assert.True(_session.Undo().IsSuccess);
        Assert.DoesNotContain(_session.Project.Graphics, g => g.Id == text.Value);
        Assert.True(_session.Undo().IsSuccess);
        Assert.DoesNotContain(_session.Project.Placements, p => p.Id == placed.Value);
    }

    [Fact]
    public void Preview_supports_rotation_variants_for_symbol_selection()
    {
        _session.OpenLibrary(_libraryPath);

        var rot0 = _session.PreviewOf(_lamp.Id, 0)!;
        var rot90 = _session.PreviewOf(_lamp.Id, 90)!;

        Assert.Equal((18.0, 25.0), (rot0.WidthMm, rot0.HeightMm));
        Assert.Equal((25.0, 18.0), (rot90.WidthMm, rot90.HeightMm));
        Assert.Contains(rot90.Items.OfType<Ake.Render.CircleItem>(), c => c.Radius == 4);
    }

    [Fact]
    public void Custom_symbol_categories_and_moving_symbols_persist_across_reopen_and_standard_reinstall()
    {
        Assert.True(_session.OpenLibrary(_libraryPath).IsSuccess);

        const string customFolder = "Özel Panom/Motorlar";
        Assert.True(_session.CreateSymbolCategory(customFolder).IsSuccess);
        Assert.Contains(customFolder, _session.SymbolCategories());

        var treeWithEmptyFolder = SymbolPalette.Build(_session.SearchLibrary(""), expanded: false, _session.SymbolCategories());
        var rootFolder = Assert.Single(treeWithEmptyFolder, g => g.Name == "Özel Panom");
        Assert.Contains(rootFolder.Items.OfType<SymbolGroup>(), g => g.Name == "Motorlar" && g.CategoryPath == customFolder);

        Assert.True(_session.MoveSymbolToCategory(_lamp.Id, customFolder).IsSuccess);
        Assert.Contains(_session.SearchLibrary(""), row => row.Id == _lamp.Id && row.Category == customFolder);

        // Aynı kimlikle eski kategoride yeniden içe aktarılsa bile kullanıcı taşıdığı kategori korunur.
        using (var direct = ComponentLibrary.Open(_libraryPath).Value)
        {
            Assert.True(direct.ImportSymbols([new LibrarySymbol(_lamp, "sinyal")]).IsSuccess);
        }

        Assert.True(_session.OpenLibrary(_libraryPath).IsSuccess);
        Assert.Contains(_session.SearchLibrary(""), row => row.Id == _lamp.Id && row.Category == customFolder);
    }
}
