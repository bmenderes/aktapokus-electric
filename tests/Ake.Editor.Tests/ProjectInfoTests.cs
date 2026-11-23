using Ake.Core.Model;

namespace Ake.Editor.Tests;

/// <summary>Proje özellikleri ve proje oluşturma, oturum tarafı (ADR-0047).</summary>
public sealed class ProjectInfoTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-info-");
    private readonly EditorSession _session = new("deneme");

    public ProjectInfoTests()
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

    [Fact]
    public void Name_and_fields_change_in_one_undoable_step_and_empty_values_delete_the_field()
    {
        var project = _session.Project!;
        var originalName = project.Name;
        var values = new Dictionary<string, string> { ["customer"] = "  Örnek A.Ş.  ", ["revision"] = "", ["drawingNo"] = "REF-001" };

        var result = _session.SetProjectInfo("  Yeni ad  ", values);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Yeni ad", project.Name);
        Assert.Equal("Örnek A.Ş.", project.Properties["customer"]);
        Assert.False(project.Properties.ContainsKey("revision")); // boş değer alanı siler
        Assert.Equal("REF-001", project.Properties["drawingNo"]); // aynı değer: dokunulmadı

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(originalName, project.Name);
        Assert.False(project.Properties.ContainsKey("customer"));
        Assert.Equal("A", project.Properties["revision"]);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void An_unchanged_form_writes_nothing_and_a_blank_name_is_rejected()
    {
        var project = _session.Project!;
        var same = EditorSession.ProjectFields.ToDictionary(field => field.Key, field => project.Properties.GetValueOrDefault(field.Key, string.Empty));

        Assert.True(_session.SetProjectInfo(project.Name, same).IsSuccess);
        Assert.False(_session.CanUndo);

        Assert.Equal("proje.ad_bos", _session.SetProjectInfo("   ", same).Error!.Code);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void The_fields_are_the_ones_the_cover_page_and_title_block_use()
    {
        var keys = EditorSession.ProjectFields.Select(field => field.Key).ToList();

        Assert.Equal(["description", "customer", "commission", "drawingNo", "company", "companyAddress", "drawnBy", "date", "revision"], keys);
        Assert.All(["customer", "commission", "drawingNo", "company", "drawnBy", "date", "revision", "description"], key => Assert.Contains(key, keys));
    }

    [Fact]
    public void Statistics_structure_and_status_describe_the_project()
    {
        var statistics = _session.ProjectStatistics().ToDictionary(line => line.Label.Trim(), line => line.Value);
        Assert.Equal("5", statistics["Sayfa"]);
        Assert.Equal("5", statistics["Şema sayfası"]);
        Assert.Equal("19", statistics["Cihaz"]);
        Assert.Equal("65", statistics["Bağlantı"]);

        var structure = _session.ProjectStructureLines();
        Assert.Contains("Fonksiyon yapısı", structure);
        Assert.Contains(structure, line => line.Contains("=M1", StringComparison.Ordinal) && line.Contains("1 sayfa", StringComparison.Ordinal));
        Assert.Contains(structure, line => line.Contains("+P1", StringComparison.Ordinal));

        var status = _session.ProjectStatus().ToDictionary(line => line.Label, line => line.Value);
        Assert.EndsWith("makine.json", status["Dosya"], StringComparison.Ordinal);
        Assert.Equal("Kaydedildi", status["Kayıt durumu"]);
        Assert.Equal("0 hata, 0 uyarı", status["ERC"]);

        Assert.True(_session.SetProjectInfo(_session.Project!.Name, new Dictionary<string, string> { ["customer"] = "X" }).IsSuccess);
        Assert.Equal("Kaydedilmemiş değişiklik var", _session.ProjectStatus().Single(line => line.Label == "Kayıt durumu").Value);
    }

    [Fact]
    public void A_new_project_gets_the_creator_and_the_date_as_properties_and_starts_unmodified()
    {
        var created = _session.NewProject(new NewProjectInfo("Hat 7", Creator: " Barış ", Date: ""), new DateOnly(2026, 11, 19));

        Assert.True(created.IsSuccess, created.Error?.Message);
        var project = _session.Project!;
        Assert.Equal("Hat 7", project.Name);
        Assert.Equal("Barış", project.Properties["drawnBy"]);
        Assert.Equal("19.11.2026", project.Properties["date"]);
        Assert.Equal([(PageKind.Cover, "1", "Kapak"), (PageKind.Schematic, "2", "Sayfa")], project.Pages.Select(page => (page.Kind, page.Number, page.Title)));
        Assert.Empty(_session.SceneFor(0, new DateOnly(2026, 11, 19)).Value.Warnings);
        Assert.True(_session.IsUntitled);
        Assert.False(_session.IsDirty);
        Assert.False(_session.CanUndo);

        var explicitDate = _session.NewProject(new NewProjectInfo("Hat 8", Date: "01.02.2027"), new DateOnly(2026, 11, 19));
        Assert.True(explicitDate.IsSuccess);
        Assert.Equal("01.02.2027", _session.Project!.Properties["date"]);
        Assert.False(_session.Project.Properties.ContainsKey("drawnBy"));

        Assert.Equal("editor.ad_bos", _session.NewProject(new NewProjectInfo("  "), new DateOnly(2026, 11, 19)).Error!.Code);
    }

    [Fact]
    public void The_project_properties_command_is_on_the_start_tab_and_in_the_file_menu()
    {
        var group = EditorCommands.Main.Tabs.Single(tab => tab.Label == "Başlangıç").Groups.Single(group => group.Label == "Proje");

        Assert.Contains(group.Items, item => item.CommandId == EditorCommands.ProjectProperties);
        Assert.Contains(EditorCommands.ProjectProperties, EditorCommands.FileMenu);
    }

    [Fact]
    public void A_new_project_uses_the_chosen_standard_template_for_all_its_pages()
    {
        var a4 = StandardSheetTemplates.All.Single(template => template.Name.StartsWith("A4 dikey", StringComparison.Ordinal));

        Assert.True(_session.NewProject(new NewProjectInfo("Pano", TemplateId: a4.Id), new DateOnly(2026, 11, 19)).IsSuccess);

        var project = _session.Project!;
        Assert.Equal([a4], project.SheetTemplates);
        Assert.All(project.Pages, page => Assert.Equal(a4.Id, page.TemplateId));
        Assert.Empty(_session.SceneFor(0, new DateOnly(2026, 11, 19)).Value.Warnings);

        // Bilinmeyen kimlik varsayılan şablona döner.
        Assert.True(_session.NewProject(new NewProjectInfo("Pano 2", TemplateId: SheetTemplateId.New()), new DateOnly(2026, 11, 19)).IsSuccess);
        Assert.Equal([StandardSheetTemplates.A3Landscape.Id], _session.Project!.SheetTemplates.Select(template => template.Id));
    }

    [Fact]
    public void A_page_can_use_a_standard_template_that_is_added_to_the_project_in_the_same_undoable_step()
    {
        var project = _session.Project!;
        var a2 = StandardSheetTemplates.All.Single(template => template.Name.StartsWith("A2 yatay", StringComparison.Ordinal));
        var templates = project.SheetTemplates.Count;
        var pages = project.Pages.Count;

        var added = _session.AddPages(PageKind.PartsList, "Malzeme listesi", afterIndex: null, a2.Id);

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal(templates + 1, project.SheetTemplates.Count);
        Assert.Contains(project.SheetTemplates, template => template.Id == a2.Id);
        Assert.Equal(a2.Id, project.Pages[added.Value].TemplateId);
        Assert.Empty(_session.SceneFor(added.Value, new DateOnly(2026, 11, 19)).Value.Warnings);

        // Projede olan bir şablon seçilirse yeniden eklenmez; bilinmeyen kimlik reddedilir.
        Assert.True(_session.AddPages(PageKind.Cover, "Kapak", null, a2.Id).IsSuccess);
        Assert.Equal(templates + 1, project.SheetTemplates.Count);
        Assert.Equal("editor.sablon_yok", _session.AddPages(PageKind.Cover, "Kapak", null, SheetTemplateId.New()).Error!.Code);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal((templates, pages), (project.SheetTemplates.Count, project.Pages.Count));
    }

    [Fact]
    public void A_revision_is_suggested_added_with_the_title_block_revision_in_one_step_and_undone()
    {
        var project = _session.Project!;
        var suggestion = _session.SuggestRevision(new DateOnly(2026, 11, 19));
        Assert.Equal(("A", "19.11.2026", "AKE", string.Empty), (suggestion.Mark, suggestion.Date, suggestion.Author, suggestion.Description));

        var added = _session.AddRevisionRecord(suggestion with { Description = "İlk yayın" });

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal(["A"], project.Revisions.Select(revision => revision.Mark));
        Assert.Equal("A", project.Properties["revision"]);
        Assert.Equal("B", _session.SuggestRevision(new DateOnly(2026, 11, 19)).Mark);

        Assert.Equal("revizyon.isaret_tekrar", _session.AddRevisionRecord(suggestion with { Description = "yine" }).Error!.Code);
        Assert.Equal("revizyon.aciklama_bos", _session.AddRevisionRecord(new Ake.Core.Model.Revision("C", "", "", "")).Error!.Code);
        Assert.Single(project.Revisions);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Empty(project.Revisions);
        Assert.Equal("A", project.Properties["revision"]); // referans makinenin kendi revizyonu geri gelir
    }

    [Fact]
    public void The_revision_command_is_on_the_start_tab_and_the_statistics_name_every_new_page_kind()
    {
        var group = EditorCommands.Main.Tabs.Single(tab => tab.Label == "Başlangıç").Groups.Single(group => group.Label == "Proje");
        Assert.Contains(group.Items, item => item.CommandId == EditorCommands.AddRevisionEntry);

        foreach (var kind in new[] { PageKind.PotentialOverview, PageKind.PinConnections, PageKind.PlcDiagram, PageKind.RevisionOverview, PageKind.FrameDocumentation })
        {
            Assert.True(_session.AddPages(kind, kind.ToString(), afterIndex: null).IsSuccess, kind.ToString());
        }

        var names = _session.ProjectStatistics().Select(line => line.Label.Trim()).ToList();
        Assert.All(["Potansiyel özeti", "Pin-bağlantı şeması", "PLC şeması", "Revizyon özeti", "Çerçeve dokümantasyonu"], name => Assert.Contains(name, names));
    }

    [Fact]
    public void Structure_cable_and_potential_forms_add_update_remove_and_undo()
    {
        var project = _session.Project!;

        var fn = _session.AddStructureNode(Aspect.Function, "=M9", description: "Ek hat");
        Assert.True(fn.IsSuccess, fn.Error?.Message);
        Assert.True(_session.UpdateStructureNode(fn.Value, "M10", "Yeni hat").IsSuccess);
        Assert.Equal("M10", project.Structure.Single(n => n.Id == fn.Value).Name);

        var cable = _session.AddCableItem("-W99", "3x1.5", [("1", 1.5m, "BN"), ("2", 1.5m, "BU"), ("PE", 1.5m, "GNYE")]);
        Assert.True(cable.IsSuccess, cable.Error?.Message);
        var addedCable = project.Cables.Single(c => c.Id == cable.Value);
        var connId = project.Connections[0].Id;
        Assert.True(_session.AssignConnectionMedium(connId, null, null, null, addedCable.Cores[0].Id).IsSuccess);
        Assert.Equal(addedCable.Cores[0].Id, project.FindConnection(connId)!.CoreId);
        Assert.True(_session.AssignConnectionMedium(connId, "W777", 2.5m, "BK", null).IsSuccess);
        Assert.Equal(("W777", 2.5m, "BK", null), (project.FindConnection(connId)!.Wire!.Number, project.FindConnection(connId)!.Wire!.CrossSectionMm2, project.FindConnection(connId)!.Wire!.Color, project.FindConnection(connId)!.CoreId));
        Assert.True(_session.RemoveCableItem(cable.Value).IsSuccess);
        Assert.DoesNotContain(project.Cables, c => c.Id == cable.Value);

        var point = project.Devices[0].Functions[0].ConnectionPoints[0].Id;
        var marker = _session.AddPotentialMarker(point, "400VAC", PotentialKind.Phase);
        Assert.True(marker.IsSuccess, marker.Error?.Message);
        Assert.True(_session.RemovePotentialMarker(marker.Value).IsSuccess);
        Assert.DoesNotContain(project.PotentialSources, s => s.Id == marker.Value);

        Assert.True(_session.RemoveStructureNode(fn.Value).IsSuccess);
        Assert.True(_session.Undo().IsSuccess); // RemoveStructureNode geri alındı
        Assert.Contains(project.Structure, n => n.Id == fn.Value);
    }

    [Fact]
    public void Product_catalog_exports_and_plc_cross_reference_session_features_work()
    {
        var libPath = Path.Combine(_directory.FullName, "lib.db");
        Assert.True(Ake.Library.StandardSymbols.EnsureInstalled(libPath).IsSuccess);
        Assert.True(_session.OpenLibrary(libPath).IsSuccess);
        Assert.True(_session.LibraryProductCount >= 24);
        Assert.NotEmpty(_session.ProductCategories());
        Assert.NotEmpty(_session.SearchProducts("M12", "Sensör"));

        var deviceCountBefore = _session.Project!.Devices.Count;
        var addedDevice = _session.CreateDeviceFromLibraryProduct("IME12-04BPSZC0S", "Sick");
        Assert.True(addedDevice.IsSuccess, addedDevice.Error?.Message);
        Assert.Equal(deviceCountBefore + 1, _session.Project.Devices.Count);
        Assert.Contains(_session.Project.Devices, d => d.Id == addedDevice.Value && d.ProductTag.StartsWith("B", StringComparison.Ordinal));

        var pdfPath = Path.Combine(_directory.FullName, "proje.pdf");
        var xlsxPath = Path.Combine(_directory.FullName, "raporlar.xlsx");
        var labelsPath = Path.Combine(_directory.FullName, "etiketler.pdf");
        Assert.True(_session.ExportPdf(pdfPath, new DateOnly(2026, 11, 20)).IsSuccess);
        Assert.True(_session.ExportReportsXlsx(xlsxPath).IsSuccess);
        Assert.True(_session.ExportLabelsPdf(labelsPath).IsSuccess);
        Assert.True(File.Exists(pdfPath));
        Assert.True(File.Exists(xlsxPath));
        Assert.True(File.Exists(labelsPath));

        var plcRows = _session.PlcCrossReferenceRows(sortByWire: false);
        Assert.Equal(16, plcRows.Count);
        Assert.NotEmpty(_session.PlcCrossReferenceRows(sortByWire: true, filter: "M1"));

        var importedCount = _session.ImportPlcAddressesCsv("Adres,SembolikAd,Açıklama\n%IX10.0,bTestSinyal,Test kanalı\n");
        Assert.True(importedCount.IsSuccess, importedCount.Error?.Message);
        Assert.Equal(1, importedCount.Value);
        Assert.Contains(_session.PlcCrossReferenceRows(), row => row.Address == "%IX10.0" && row.SymbolName == "bTestSinyal");
    }
}
