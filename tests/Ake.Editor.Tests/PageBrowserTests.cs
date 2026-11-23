using Ake.Core.Catalog;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor.Tests;

/// <summary>Sayfa gezgini (ADR-0051): alan bazlı süzgeç, düz liste görünümü.</summary>
public sealed class PageBrowserTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-browser-");
    private readonly EditorSession _session = new("deneme");

    public PageBrowserTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private IReadOnlyList<string> Numbers(IEnumerable<int> pages) => [.. pages.Select(index => _session.Project!.Pages[index].Number)];

    [Fact]
    public void Each_field_filters_on_its_own_property_and_an_empty_text_keeps_every_page()
    {
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(_session.PagesMatching(PageFilterField.All, "")));
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(_session.PagesMatching(PageFilterField.Title, null)));

        Assert.Equal(["2", "4"], Numbers(_session.PagesMatching(PageFilterField.Title, "MOTOR")));
        Assert.Equal(["3"], Numbers(_session.PagesMatching(PageFilterField.Number, "3")));
        Assert.Equal(["2"], Numbers(_session.PagesMatching(PageFilterField.Function, "=m1")));
        Assert.Equal(["2"], Numbers(_session.PagesMatching(PageFilterField.Function, "m1")));
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(_session.PagesMatching(PageFilterField.Location, "p1")));
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(_session.PagesMatching(PageFilterField.Kind, "şema")));
        Assert.Equal(["1", "2", "3", "4", "5"], Numbers(_session.PagesMatching(PageFilterField.Template, "A3")));
        Assert.Empty(_session.PagesMatching(PageFilterField.Template, "A4"));
        Assert.Empty(_session.PagesMatching(PageFilterField.Number, "motor"));
    }

    [Fact]
    public void The_all_field_looks_in_every_property_and_the_kind_field_finds_generated_pages()
    {
        Assert.Equal(["2"], Numbers(_session.PagesMatching(PageFilterField.All, "=M1")));
        Assert.Equal(["5"], Numbers(_session.PagesMatching(PageFilterField.All, "acil")));

        Assert.True(_session.AddPages(PageKind.Cover, "Kapak", afterIndex: null).IsSuccess);
        Assert.True(_session.AddPages(PageKind.PartsList, "Malzeme listesi", afterIndex: null).IsSuccess);

        Assert.Equal(["6"], Numbers(_session.PagesMatching(PageFilterField.Kind, "kapak")));
        Assert.Equal(["7"], Numbers(_session.PagesMatching(PageFilterField.Kind, "malzeme")));
        Assert.Equal(["6"], Numbers(_session.PagesMatching(PageFilterField.All, "Kapak")));
        Assert.True(_session.IsSchematicPage(0));
        Assert.False(_session.IsSchematicPage(5));
        Assert.False(_session.IsSchematicPage(6));
    }

    [Fact]
    public void The_list_view_is_flat_in_project_order_with_the_page_kind_and_follows_the_filter()
    {
        Assert.True(_session.AddPages(PageKind.Cover, "Kapak", afterIndex: null).IsSuccess);

        var rows = _session.PageListRows(null, PageFilterField.All);

        Assert.Equal(6, rows.Count);
        Assert.All(rows, row => Assert.False(row.IsGroup));
        Assert.All(rows, row => Assert.Equal(0, row.Depth));
        Assert.Equal([0, 1, 2, 3, 4, 5], rows.Select(row => row.PageIndex!.Value));
        Assert.Equal("1  Güç dağıtımı", rows[0].Label); // şema sayfasında tür yazılmaz
        Assert.Equal("6  Kapak  ·  Kapak", rows[5].Label);

        var filtered = _session.PageListRows("motor", PageFilterField.Title);
        Assert.Equal([1, 3], filtered.Select(row => row.PageIndex!.Value));
    }

    [Fact]
    public void The_tree_keeps_only_the_matching_pages_and_the_groups_leading_to_them_for_a_field_filter()
    {
        var rows = _session.PageTreeRows("m1", new HashSet<string>(), PageFilterField.Function);

        Assert.Equal(["=M1", "+P1", "2  Motor 1"], rows.Select(row => row.Label));
        Assert.All(rows.Where(row => row.IsGroup), row => Assert.True(row.Expanded));

        // Tümü alanı eski davranıştır: grup adı eşleşirse altındaki her şey kalır.
        var all = _session.PageTreeRows("m1", new HashSet<string>(), PageFilterField.All);
        Assert.Contains(all, row => row.PageIndex == 1);
        Assert.DoesNotContain(all, row => row.PageIndex == 0);

        Assert.Empty(_session.PageTreeRows("yok yok", new HashSet<string>(), PageFilterField.Title));
        Assert.Equal(_session.PageTreeRows(null, new HashSet<string>()).Count, _session.PageTreeRows("", new HashSet<string>(), PageFilterField.Kind).Count);
    }

    [Fact]
    public void The_filter_fields_are_listed_with_their_names_and_the_page_kind_names_are_public()
    {
        Assert.Equal(
            ["Tümü", "Numara", "Başlık", "Tür", "Fonksiyon", "Yer", "Şablon"],
            EditorSession.PageFilterFields.Select(field => field.Label));
        Assert.Equal(Enum.GetValues<PageFilterField>(), EditorSession.PageFilterFields.Select(field => field.Field));
        Assert.Equal("Kapak", EditorSession.KindName(PageKind.Cover));
        Assert.Equal("Şema sayfası", EditorSession.KindName(PageKind.Schematic));
    }

    [Fact]
    public void Full_page_name_template_summary_and_page_properties_work_with_undo()
    {
        Assert.Equal("=M1+P1/2", EditorSession.FullPageName("M1", "P1", "2"));
        Assert.Equal("=M1/3.1", EditorSession.FullPageName("=M1", "—", "3.1"));
        Assert.Contains("420 × 297 mm", EditorSession.TemplateSummary(StandardSheetTemplates.A3Landscape), StringComparison.Ordinal);

        var m1 = _session.Project!.Structure.First(n => n.Aspect == Aspect.Function && n.Name == "M1").Id;
        var a4 = StandardSheetTemplates.All[0];
        var added = _session.AddPages(PageKind.Schematic, "Özel Sayfa", afterIndex: null, templateId: a4.Id, functionNodeId: m1, locationNodeId: null, customNumber: "99", explicitStructure: true);
        Assert.True(added.IsSuccess, added.Error?.Message);
        var page = _session.Project.Pages[added.Value];
        Assert.Equal(("99", "Özel Sayfa", m1, null, a4.Id), (page.Number, page.Title, page.FunctionNodeId, page.LocationNodeId, page.TemplateId));

        var data = _session.PageDataOf(added.Value)!;
        Assert.Equal("99", data.Number);
        Assert.True(_session.ApplyPageEdits(added.Value, new PageEdits("99.1", "Düzenli Sayfa", null, null, a4.Id)).IsSuccess);
        Assert.Equal(("99.1", "Düzenli Sayfa", null), (page.Number, page.Title, page.FunctionNodeId));
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(("99", "Özel Sayfa", m1), (page.Number, page.Title, page.FunctionNodeId));
    }

    [Fact]
    public void Page_number_change_and_custom_new_page_move_into_natural_numeric_order_with_single_undo()
    {
        var unsorted = new[] { "91", "1.10", "2", "1.1", "0a", "6A", "1", "10", "0", "1.2", "6" };
        var sorted = unsorted.OrderBy(n => n, Comparer<string>.Create(PageNumbering.CompareNumbers)).ToArray();
        Assert.Equal(["0", "0a", "1", "1.1", "1.2", "1.10", "2", "6", "6A", "10", "91"], sorted);

        var added6 = _session.AppendPage();
        Assert.True(added6.IsSuccess);
        Assert.Equal(5, added6.Value);

        Assert.True(_session.SetPageNumberAt(5, "1.1").IsSuccess);
        Assert.Equal(["1", "1.1", "2", "3", "4", "5"], _session.Project!.Pages.Select(p => p.Number));

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], _session.Project.Pages.Select(p => p.Number));

        var data4 = _session.PageDataOf(4)!;
        Assert.True(_session.ApplyPageEdits(4, new PageEdits("1.2", data4.Title, data4.FunctionNode, data4.LocationNode, data4.TemplateId)).IsSuccess);
        Assert.Equal(["1", "1.2", "2", "3", "4", "6"], _session.Project.Pages.Select(p => p.Number));

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(["1", "2", "3", "4", "5", "6"], _session.Project.Pages.Select(p => p.Number));

        var inserted = _session.AddPages(PageKind.Schematic, "Ara", afterIndex: null, customNumber: "1.1");
        Assert.True(inserted.IsSuccess);
        Assert.Equal(1, inserted.Value);
        Assert.Equal("1.1", _session.Project.Pages[1].Number);
        Assert.Equal(["1", "1.1", "2", "3", "4", "5", "6"], _session.Project.Pages.Select(p => p.Number));
    }

    [Fact]
    public void Product_preview_renders_schematic_symbols_for_every_standard_product_and_null_when_empty()
    {
        foreach (var product in StandardProducts.All)
        {
            var scene = EditorSession.PreviewOf(product.Definition);
            Assert.NotNull(scene);
            Assert.True(scene.WidthMm > 0);
            Assert.True(scene.HeightMm > 0);
            Assert.NotEmpty(scene.Items);
        }

        var contactor = StandardProducts.All.First(p => p.Definition.OrderCode == "LC1D09BD");
        var contactorScene = EditorSession.PreviewOf(contactor.Definition)!;
        var texts = contactorScene.Items.OfType<TextItem>().Select(t => t.Text).ToList();
        Assert.Contains("A1", texts);
        Assert.Contains("A2", texts);
        Assert.Contains("13", texts);
        Assert.Contains("14", texts);

        var empty = new ProductDefinition("Acme", "EMPTY-1", []);
        Assert.Null(EditorSession.PreviewOf(empty));
    }

    [Fact]
    public void Place_library_product_places_device_with_rich_library_symbol_on_schematic_page_and_previews_internal_labels()
    {
        var dbPath = Path.Combine(_directory.FullName, "kutuphane.db");
        var richSymbol = new SymbolDefinition(
            SymbolId.New(),
            "LXM62 (2 uç)",
            [
                new SymbolRect(0, 0, 40, 20),
                new SymbolText(10, 8, "BUS BAR MODULE", 2.0, TextAlign.Left),
                new SymbolText(10, 14, "DC-", 2.0, TextAlign.Left),
            ],
            [new SymbolPin(10, 0, 0, -1, "CN1/1"), new SymbolPin(30, 0, 0, -1, "CN1/2")],
            "PDF",
            "Kullanıcı",
            DeviceName: "DRV-LXM",
            TagLetter: "D");
        var libProduct = new LibraryProduct(
            new ProductDefinition(
                "Schneider",
                "DRV-LXM",
                [new FunctionTemplate(FunctionKind.Generic, ["CN1/1", "CN1/2"], Description: "Servo", SymbolId: richSymbol.Id)],
                Description: "Servo sürücü"),
            "PDF",
            "Kullanıcı",
            "Otomasyon");

        using (var lib = ComponentLibrary.Open(dbPath).Value)
        {
            Assert.True(lib.ImportSymbols([new LibrarySymbol(richSymbol, "PDF/Servo")]).IsSuccess);
            Assert.True(lib.Import([libProduct]).IsSuccess);
        }

        Assert.True(_session.OpenLibrary(dbPath).IsSuccess);

        var preview = _session.PreviewOfProduct(libProduct.Definition);
        Assert.NotNull(preview);
        var previewTexts = preview.Items.OfType<TextItem>().Select(t => t.Text).ToList();
        Assert.Contains("BUS BAR MODULE", previewTexts);
        Assert.Contains("DC-", previewTexts);

        var devicesBefore = _session.Project!.Devices.Count;
        var placementsBefore = _session.Project.Placements.Count;
        var placed = _session.PlaceLibraryProduct("DRV-LXM", "Schneider", pageIndex: 0);
        Assert.True(placed.IsSuccess, placed.Error?.Message);
        Assert.Single(placed.Value);
        Assert.Equal(devicesBefore + 1, _session.Project.Devices.Count);
        Assert.Equal(placementsBefore + 1, _session.Project.Placements.Count);
        Assert.True(_session.Project.CheckIntegrity().IsSuccess);

        var placement = _session.Project.Placements.First(p => p.Id == placed.Value[0]);
        var fn = _session.Project.Devices.SelectMany(d => d.Functions).First(f => f.Id == placement.FunctionId);
        Assert.Equal(richSymbol.Id, fn.SymbolId);
        Assert.NotNull(_session.Project.FindSymbol(richSymbol.Id));

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(devicesBefore, _session.Project.Devices.Count);
        Assert.Equal(placementsBefore, _session.Project.Placements.Count);

        var editedSymbol = new SymbolDefinition(
            SymbolId.New(),
            "LXM62 Düzenli",
            [new SymbolRect(0, 0, 40, 20), new SymbolText(20, 10, "SERCOS 3", 2.0, TextAlign.Center)],
            [new SymbolPin(10, 0, 0, -1, "CN1/1"), new SymbolPin(30, 0, 0, -1, "CN1/2")],
            "Düzenlendi",
            "Kullanıcı çizimi");
        var saved = _session.SaveSymbolToLibrary(editedSymbol, overwrite: false);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.True(_session.LinkSymbolToProduct("Schneider", "DRV-LXM", saved.Value).IsSuccess);

        var updatedProduct = _session.FindLibraryProduct("DRV-LXM", "Schneider")!;
        var updatedPreview = _session.PreviewOfProduct(updatedProduct.Definition);
        Assert.NotNull(updatedPreview);
        Assert.Contains(updatedPreview.Items.OfType<TextItem>(), t => t.Text == "SERCOS 3");
    }
}
