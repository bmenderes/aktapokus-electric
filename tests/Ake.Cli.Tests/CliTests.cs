using System.Text.Json;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Cli.Tests;

/// <summary>
/// Komutlar gerçek dosyalarla, ama geçici bir klasördeki kopya üzerinde çalıştırılır. Depodaki
/// örnek dosyalara ve kullanıcının profiline yazılmaz.
/// </summary>
public sealed class CliTests : IDisposable
{
    private static readonly string Examples = Path.Combine(FindRepositoryRoot(), "examples", "ref-machine");

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-cli-test-");
    private readonly string _project;
    private readonly string _original;

    public CliTests()
    {
        _project = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(Examples, "ref-machine.json"), _project);
        _original = File.ReadAllText(_project);
    }

    private string AuditPath => Path.Combine(_directory.FullName, "makine.audit.jsonl");

    public void Dispose() => _directory.Delete(recursive: true);

    private static string PatchPath(string name) => Path.Combine(Examples, "patches", name);

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = CliApp.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    // ---- validate

    [Fact]
    public void Validate_reports_a_clean_project()
    {
        var (exit, output, _) = Run("validate", _project);

        Assert.Equal(0, exit);
        Assert.Contains("19 device, 65 bağlantı; 0 hata, 0 uyarı", output);
    }

    [Fact]
    public void Validate_can_write_json()
    {
        var (exit, output, _) = Run("validate", _project, "--json");

        using var document = JsonDocument.Parse(output);
        Assert.Equal(0, exit);
        Assert.Equal(0, document.RootElement.GetProperty("errors").GetInt32());
        Assert.Equal(0, document.RootElement.GetProperty("findings").GetArrayLength());
    }

    [Fact]
    public void Validate_fails_cleanly_on_a_missing_file()
    {
        var (exit, _, error) = Run("validate", Path.Combine(_directory.FullName, "yok.json"));

        Assert.Equal(1, exit);
        Assert.Contains("proje.dosya_okunamadi", error);
    }

    [Fact]
    public void Validate_fails_cleanly_on_a_broken_file()
    {
        File.WriteAllText(_project, "{ bozuk");

        var (exit, _, error) = Run("validate", _project);

        Assert.Equal(1, exit);
        Assert.Contains("proje.json_gecersiz", error);
    }

    // ---- apply

    [Fact]
    public void Apply_writes_the_project_and_the_audit_log()
    {
        var (exit, output, _) = Run("apply", _project, PatchPath("01-yedek-kontaktor.json"), "--actor", "deneme");

        Assert.Equal(0, exit);
        Assert.Contains("Patch uygulandı: 7 komut", output);
        Assert.Contains("20 device, 69 bağlantı; 0 hata, 0 uyarı", output);
        Assert.Contains("\"tag\": \"K3\"", File.ReadAllText(_project));

        var line = Assert.Single(File.ReadAllLines(AuditPath));
        Assert.Contains("\"actor\":\"deneme\"", line);
        Assert.Contains("\"action\":\"Execute\"", line);
        Assert.Contains("yedek kontaktör eklendi", line);
    }

    [Fact]
    public void Applied_project_validates_afterwards()
    {
        Run("apply", _project, PatchPath("01-yedek-kontaktor.json"));

        Assert.Equal(0, Run("validate", _project).Exit);
    }

    [Fact]
    public void Apply_dry_run_touches_nothing()
    {
        var (exit, output, _) = Run("apply", _project, PatchPath("01-yedek-kontaktor.json"), "--dry-run");

        Assert.Equal(0, exit);
        Assert.Contains("dosya yazılmadı", output);
        Assert.Equal(_original, File.ReadAllText(_project));
        Assert.False(File.Exists(AuditPath));
    }

    [Fact]
    public void Apply_out_writes_elsewhere_and_leaves_the_source_alone()
    {
        var target = Path.Combine(_directory.FullName, "sonuc.json");

        var (exit, _, _) = Run("apply", _project, PatchPath("01-yedek-kontaktor.json"), "--out", target);

        Assert.Equal(0, exit);
        Assert.Equal(_original, File.ReadAllText(_project));
        Assert.Contains("\"tag\": \"K3\"", File.ReadAllText(target));
        Assert.True(File.Exists(Path.Combine(_directory.FullName, "sonuc.audit.jsonl")));
        Assert.False(File.Exists(AuditPath));
    }

    [Fact]
    public void Apply_with_erc_errors_still_writes_but_exits_with_failure()
    {
        var (exit, output, _) = Run("apply", _project, PatchPath("02-kisa-devre.json"));

        Assert.Equal(1, exit);
        Assert.Contains("erc.kisa_devre", output);
        Assert.NotEqual(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Apply_strict_refuses_to_write_when_erc_has_errors()
    {
        var (exit, _, error) = Run("apply", _project, PatchPath("02-kisa-devre.json"), "--strict");

        Assert.Equal(1, exit);
        Assert.Contains("dosya yazılmadı", error);
        Assert.Equal(_original, File.ReadAllText(_project));
        Assert.False(File.Exists(AuditPath));
    }

    [Fact]
    public void Apply_rejects_a_patch_that_cannot_be_resolved_and_changes_nothing()
    {
        var patch = Path.Combine(_directory.FullName, "bozuk-patch.json");
        File.WriteAllText(
            patch,
            """{ "patchVersion": 1, "commands": [ { "op": "connect", "from": "-K1:A1", "to": "-YOK:1" } ] }""");

        var (exit, _, error) = Run("apply", _project, patch);

        Assert.Equal(1, exit);
        Assert.Contains("patch.bulunamadi", error);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    // ---- renumber

    [Fact]
    public void Renumber_requires_exactly_one_target()
    {
        Assert.Equal(1, Run("renumber", _project).Exit);
        Assert.Equal(1, Run("renumber", _project, "--tags", "--wires").Exit);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Renumber_reports_when_there_is_nothing_to_change()
    {
        var (exit, output, _) = Run("renumber", _project, "--wires", "--scheme", "Sequential");

        Assert.Equal(0, exit);
        Assert.Contains("Değişiklik yok", output);
        Assert.False(File.Exists(AuditPath));
    }

    [Fact]
    public void Renumber_wires_per_potential_writes_a_project_that_still_validates()
    {
        var (exit, output, _) = Run("renumber", _project, "--wires");

        Assert.Equal(0, exit);
        Assert.Contains("Tel atandı", output);
        Assert.NotEqual(_original, File.ReadAllText(_project));
        Assert.Single(File.ReadAllLines(AuditPath));
        Assert.Equal(0, Run("validate", _project).Exit);
    }

    [Fact]
    public void Renumber_dry_run_lists_changes_without_writing()
    {
        var (exit, output, _) = Run("renumber", _project, "--tags", "--dry-run");

        Assert.Equal(0, exit);
        Assert.Contains("=M2+P1-K2 → =M2+P1-K1", output);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Tag_scheme_by_page_renumbers_saves_the_setting_and_renumber_keeps_it()
    {
        var (exit, output, _) = Run("tag-scheme", _project, "sayfa");

        Assert.Equal(0, exit);
        Assert.Contains("=M2+P1-K2 → =M2+P1-4K1", output);
        Assert.Contains("\"tagScheme\": \"PageLetterNumber\"", File.ReadAllText(_project));
        Assert.Single(File.ReadAllLines(AuditPath));
        Assert.Equal(0, Run("validate", _project).Exit);
        Assert.Contains("Değişiklik yok.", Run("renumber", _project, "--tags").Output);
    }

    [Fact]
    public void Tag_scheme_rejects_an_unknown_name_and_dry_run_writes_nothing()
    {
        Assert.Contains("tag.sema_gecersiz", Run("tag-scheme", _project, "rakam").Error);
        Assert.Equal(0, Run("tag-scheme", _project, "sayfa", "--dry-run").Exit);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Macro_created_from_a_page_can_be_inserted_on_another_page()
    {
        var macro = Path.Combine(_directory.FullName, "motor.makro.json");

        var (created, text, _) = Run("macro", "create", _project, "2", "--out", macro, "--ad", "Motor çıkışı");
        Assert.Equal(0, created);
        Assert.Contains("4 cihaz", text);
        Assert.Equal(_original, File.ReadAllText(_project));

        var (inserted, output, _) = Run("macro", "insert", _project, macro, "4", "--x", "250", "--y", "40");
        Assert.Equal(0, inserted);
        Assert.Contains("Device eklendi: -K3", output);
        Assert.Single(File.ReadAllLines(AuditPath));
        Assert.Equal(0, Run("validate", _project).Exit);
    }

    [Fact]
    public void Macro_commands_fail_cleanly()
    {
        var macro = Path.Combine(_directory.FullName, "m.makro.json");

        Assert.Contains("sayfa.yok", Run("macro", "create", _project, "99", "--out", macro).Error);
        Assert.Contains("makro.alan_gecersiz", Run("macro", "create", _project, "2", "--out", macro, "--alan", "1,2,3").Error);
        Assert.Contains("makro.dosya_okunamadi", Run("macro", "insert", _project, macro, "2").Error);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    // ---- report

    [Fact]
    public void Report_writes_csv_to_the_screen_by_default()
    {
        var (exit, output, _) = Run("report", "cables", _project);

        Assert.Equal(0, exit);
        Assert.StartsWith("Kablo,Tip,Damar,Kesit (mm²),Renk,Kaynak,Hedef\n", output);
        Assert.Equal(9, output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    [Fact]
    public void Report_applies_a_column_template_and_a_custom_separator()
    {
        var template = Path.Combine(Examples, "templates", "io-klemens-nr-isim-kod.json");

        var (exit, output, _) = Run("report", "io", _project, "--template", template, "--separator", ";");

        Assert.Equal(0, exit);
        Assert.StartsWith("Modül;Klemens;Nr;İsim;Kod\n", output);
        Assert.Contains("=C1+P1-A11;;DI0;bStartButton;=C1+P1-S1:14\n", output);
    }

    [Fact]
    public void Report_writes_a_csv_file_with_a_byte_order_mark_for_excel()
    {
        var target = Path.Combine(_directory.FullName, "bom.csv");

        var (exit, output, _) = Run("report", "bom", _project, "--out", target);

        Assert.Equal(0, exit);
        Assert.Contains("bom: 18 satır", output);
        Assert.Equal([0xEF, 0xBB, 0xBF], File.ReadAllBytes(target)[..3]);
    }

    [Fact]
    public void Report_all_writes_every_report_into_one_workbook()
    {
        var target = Path.Combine(_directory.FullName, "raporlar.xlsx");

        var (exit, output, _) = Run("report", "all", _project, "--format", "xlsx", "--out", target);

        Assert.Equal(0, exit);
        Assert.Contains("bom: 18 satır, cables: 8 satır, wires: 65 satır, terminals: 13 satır, io: 16 satır", output);
        Assert.Equal([(byte)'P', (byte)'K'], File.ReadAllBytes(target)[..2]);
    }

    [Fact]
    public void Report_rejects_invalid_requests_without_writing()
    {
        var target = Path.Combine(_directory.FullName, "x.xlsx");

        Assert.Contains("rapor.yok", Run("report", "uydurma", _project).Error);
        Assert.Contains("rapor.cikti_yok", Run("report", "bom", _project, "--format", "xlsx").Error);
        Assert.Contains("rapor.all_gecersiz", Run("report", "all", _project).Error);
        Assert.Contains("rapor.bicim_yok", Run("report", "bom", _project, "--format", "pdf", "--out", target).Error);
        Assert.Contains("rapor.ayrac_gecersiz", Run("report", "bom", _project, "--separator", "||").Error);
        Assert.Equal(1, Run("report", "uydurma", _project).Exit);
        Assert.False(File.Exists(target));
    }

    // ---- lib

    private string LibraryPath => Path.Combine(_directory.FullName, "library.db");

    private static string StarterSet => Path.Combine(FindRepositoryRoot(), "examples", "library", "baslangic-seti.json");

    [Fact]
    public void Lib_import_search_and_show_work_on_a_library_file()
    {
        var imported = Run("lib", "import", StarterSet, "--library", LibraryPath);
        Assert.Equal(0, imported.Exit);
        Assert.Contains("6 ürün eklendi, 0 ürün güncellendi", imported.Output);

        var again = Run("lib", "import", StarterSet, "--library", LibraryPath);
        Assert.Contains("0 ürün eklendi, 6 ürün güncellendi", again.Output);

        var search = Run("lib", "search", "beckhoff", "--library", LibraryPath);
        Assert.Contains("Beckhoff  EL1008", search.Output);
        Assert.Contains("4 ürün bulundu", search.Output);

        var show = Run("lib", "show", "LC1D09BD", "--library", LibraryPath);
        Assert.Equal(0, show.Exit);
        Assert.Contains("Coil: A1, A2", show.Output);
        Assert.Contains("NcContact: 21, 22", show.Output);
        Assert.Contains("coilVoltage = 24 VDC", show.Output);
        Assert.Contains("Lisans:", show.Output);
    }

    [Fact]
    public void Lib_commands_fail_cleanly()
    {
        Assert.Contains("kutuphane.yol_yok", Run("lib", "search", "x").Error);
        Assert.Contains("kutuphane.urun_yok", Run("lib", "show", "YOK", "--library", LibraryPath).Error);
        Assert.Contains(
            "kutuphane.dosya_okunamadi",
            Run("lib", "import", Path.Combine(_directory.FullName, "yok.json"), "--library", LibraryPath).Error);
    }

    [Fact]
    public void Lib_import_of_csv_needs_source_and_license()
    {
        var csv = Path.Combine(_directory.FullName, "motorlar.csv");
        File.WriteAllText(csv, "manufacturer,orderCode,powerKw\nSchneider Electric,BMH0702P06A2A,1.159\n");

        Assert.Contains("kutuphane.kaynak_yok", Run("lib", "import", csv, "--library", LibraryPath).Error);

        var imported = Run(
            "lib", "import", csv, "--library", LibraryPath, "--source", "müşteri listesi", "--license", "dahili");
        Assert.Equal(0, imported.Exit);
        Assert.Contains("powerKw = 1.159", Run("lib", "show", "BMH0702P06A2A", "--library", LibraryPath).Output);
    }

    [Fact]
    public void Apply_builds_a_device_from_a_library_product()
    {
        Run("lib", "import", StarterSet, "--library", LibraryPath);

        var (exit, output, _) = Run(
            "apply", _project, PatchPath("03-kutuphaneden-kontaktor.json"), "--library", LibraryPath);

        Assert.Equal(0, exit);
        Assert.Contains("20 device, 67 bağlantı; 0 hata, 0 uyarı", output);
        var saved = File.ReadAllText(_project);
        Assert.Contains("\"tag\": \"K3\"", saved);
        Assert.Contains("\"description\": \"Yedek kontaktör\"", saved);
    }

    [Fact]
    public void Apply_of_a_library_patch_without_a_library_changes_nothing()
    {
        var (exit, _, error) = Run("apply", _project, PatchPath("03-kutuphaneden-kontaktor.json"));

        Assert.Equal(1, exit);
        Assert.Contains("patch.kutuphane_yok", error);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    // ---- template ve render

    [Fact]
    public void Template_list_shows_the_standard_templates_and_add_puts_one_into_the_project_by_name()
    {
        var (listExit, listOutput, _) = Run("template", "list");
        Assert.Equal(0, listExit);
        Assert.Contains("A4 dikey, 6 kolon", listOutput, StringComparison.Ordinal);
        Assert.Contains("A2 yatay, 14 kolon", listOutput, StringComparison.Ordinal);

        var (exit, output, _) = Run("template", "add", _project, "A4 dikey");
        Assert.Equal(0, exit);
        Assert.Contains("A4 dikey, 6 kolon", output, StringComparison.Ordinal);
        Assert.Contains(ProjectFile.Load(_project).Value.SheetTemplates, template => template.Name == "A4 dikey, 6 kolon");

        var (missingExit, _, missingError) = Run("template", "add", _project, "A9");
        Assert.NotEqual(0, missingExit);
        Assert.Contains("sablon.hazir_yok", missingError, StringComparison.Ordinal);
        var (ambiguousExit, _, ambiguousError) = Run("template", "add", _project, "A4");
        Assert.NotEqual(0, ambiguousExit);
        Assert.Contains("birden çok", ambiguousError, StringComparison.Ordinal);
    }

    private static string ExampleTemplate => Path.Combine(FindRepositoryRoot(), "examples", "templates", "a3-yatay.sablon.json");

    private string BareProject()
    {
        var path = Path.Combine(_directory.FullName, "bos.json");
        File.WriteAllText(
            path,
            """
            {
              "schemaVersion": 3,
              "id": "0193a000-0000-7000-8000-000000000001",
              "name": "Boş proje",
              "pages": [ { "id": "0193a000-0000-7000-8000-000000000002", "number": "1", "title": "Kapak" } ]
            }
            """);
        return path;
    }

    [Fact]
    public void Render_needs_a_sheet_template_and_template_set_provides_it()
    {
        var project = BareProject();
        var pdf = Path.Combine(_directory.FullName, "cikti.pdf");

        var before = Run("render", project, "--pdf", pdf);
        Assert.Equal(1, before.Exit);
        Assert.Contains("render.sablon_yok", before.Error);
        Assert.False(File.Exists(pdf));

        var set = Run("template", "set", project, ExampleTemplate);
        Assert.Equal(0, set.Exit);
        Assert.Contains("A3 yatay, 10 kolon (1 şablon)", set.Output);

        var again = Run("template", "set", project, ExampleTemplate);
        Assert.Contains("(1 şablon)", again.Output);

        var after = Run("render", project, "--pdf", pdf);
        Assert.Equal(0, after.Exit);
        Assert.Contains("(1 sayfa)", after.Output);
        Assert.Equal("%PDF-"u8.ToArray(), File.ReadAllBytes(pdf)[..5]);
    }

    [Fact]
    public void Render_writes_one_svg_and_one_png_per_page()
    {
        var output = Path.Combine(_directory.FullName, "sayfalar");

        var (exit, text, _) = Run("render", _project, "--svg", output, "--png", output);

        Assert.Equal(0, exit);
        Assert.Contains("(5 SVG)", text);
        Assert.Contains("(5 PNG)", text);
        Assert.Equal(
            Enumerable.Range(1, 5).SelectMany(n => new[] { $"sayfa-{n}.png", $"sayfa-{n}.svg" }),
            Directory.GetFiles(output).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Contains(">Motor 1</text>", File.ReadAllText(Path.Combine(output, "sayfa-2.svg")));
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Render_with_contents_puts_the_contents_sheet_first()
    {
        var output = Path.Combine(_directory.FullName, "sayfalar");
        var pdf = Path.Combine(_directory.FullName, "cikti.pdf");

        var (exit, text, _) = Run("render", _project, "--png", output, "--pdf", pdf, "--icindekiler");

        Assert.Equal(0, exit);
        Assert.Contains("(6 sayfa)", text);
        Assert.Contains("(6 PNG)", text);
        Assert.True(File.Exists(Path.Combine(output, "icindekiler-1.png")));
        Assert.Contains("/Outlines", System.Text.Encoding.Latin1.GetString(File.ReadAllBytes(pdf)));
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Render_writes_one_dxf_and_one_dwg_per_page()
    {
        var output = Path.Combine(_directory.FullName, "cad");

        var (exit, text, _) = Run("render", _project, "--dxf", output, "--dwg", output);

        Assert.Equal(0, exit);
        Assert.Contains("(5 DXF)", text);
        Assert.Contains("(5 DWG)", text);
        Assert.Equal(10, Directory.GetFiles(output).Length);
        Assert.StartsWith("  0\r\nSECTION", File.ReadAllText(Path.Combine(output, "sayfa-1.dxf")), StringComparison.Ordinal);
        Assert.Equal("AC1027", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(output, "sayfa-1.dwg")), 0, 6));
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Dwg_drawing_is_imported_as_a_symbol_into_the_project_and_the_library()
    {
        var output = Path.Combine(_directory.FullName, "cad");
        Assert.Equal(0, Run("render", _project, "--dwg", output).Exit);
        var drawing = Path.Combine(output, "sayfa-2.dwg");

        var refused = Run("symbol", "import", _project, drawing);
        var imported = Run("symbol", "import", _project, drawing, "--license", "deneme çizimi", "--scale", "0.25");
        var library = Run("lib", "symbol-import", output, "--license", "deneme çizimi", "--library", LibraryPath);

        Assert.Equal(1, refused.Exit);
        Assert.Contains("ATLANDI dxf.lisans_yok", refused.Error);
        Assert.Equal(0, imported.Exit);
        Assert.Contains("sayfa-2  ·  1 uç  ·  deneme çizimi", imported.Output);
        Assert.Contains("UYARI sayfa-2.dwg: çizimde uç (POINT) yok", imported.Error);
        Assert.Contains("\"source\": \"DWG: sayfa-2.dwg\"", File.ReadAllText(_project));
        Assert.Equal(0, library.Exit);
        Assert.Contains("5 sembol eklendi", library.Output);
        Assert.Contains("ölçek", Run("symbol", "import", _project, drawing, "--license", "x", "--scale", "0").Error, StringComparison.OrdinalIgnoreCase);
    }

    private Project Load() => ProjectFile.Load(_project).Value;

    [Fact]
    public void Our_own_pdf_output_can_be_imported_back_as_editable_pages()
    {
        var pdf = Path.Combine(_directory.FullName, "cikti.pdf");
        Assert.Equal(0, Run("render", _project, "--pdf", pdf).Exit);
        var before = Load().Pages.Count;

        var preview = Run("import", "pdf", _project, pdf, "--dry-run");
        var imported = Run("import", "pdf", _project, pdf);

        Assert.Equal(0, preview.Exit);
        Assert.Contains("Deneme çalıştırması", preview.Output);
        Assert.Equal(0, imported.Exit);
        Assert.Contains("5 sayfa", imported.Output);
        var project = Load();
        Assert.Equal(before + 5, project.Pages.Count);
        Assert.Contains(project.Graphics, graphic => graphic.Shape is GraphicText { Text: "-K1" });
        // Başlıklar PDF yer imlerinden gelir; sayfa numaraları projenin sonundan devam eder.
        Assert.Equal(before + 1, int.Parse(project.Pages[before].Number, System.Globalization.CultureInfo.InvariantCulture));
        Assert.StartsWith("1  ", project.Pages[before].Title, StringComparison.Ordinal);
        Assert.Contains("\"graphics\"", File.ReadAllText(_project));
    }

    [Fact]
    public void Pdf_import_reports_unreadable_files_and_missing_projects()
    {
        var broken = Path.Combine(_directory.FullName, "bozuk.pdf");
        File.WriteAllText(broken, "PDF değil");

        Assert.Contains("pdf.okunamadi", Run("import", "pdf", _project, broken).Error);
        Assert.Equal(1, Run("import", "pdf", Path.Combine(_directory.FullName, "yok.json"), broken).Exit);
    }

    [Fact]
    public void Render_and_template_fail_cleanly()
    {
        var notATemplate = Path.Combine(_directory.FullName, "bozuk.json");
        File.WriteAllText(notATemplate, """{ "name": "eksik" }""");

        Assert.Contains("render.cikti_yok", Run("render", _project).Error);
        Assert.Contains("sablon.json_gecersiz", Run("template", "set", _project, notATemplate).Error);
        Assert.Contains("sablon.dosya_okunamadi", Run("template", "set", _project, Path.Combine(_directory.FullName, "yok.json")).Error);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Symbol_set_adds_a_user_symbol_that_a_patch_can_assign()
    {
        var symbol = Path.Combine(FindRepositoryRoot(), "examples", "symbols", "sinyal-lambasi.sembol.json");

        var set = Run("symbol", "set", _project, symbol);
        var applied = Run("apply", _project, PatchPath("06-sinyal-lambasi.json"));

        Assert.Equal(0, set.Exit);
        Assert.Contains("Sinyal lambası (1 sembol)", set.Output);
        Assert.Equal(0, applied.Exit);
        Assert.Contains("\"symbolId\": \"0193c000-0000-7000-8000-00000000c001\"", File.ReadAllText(_project));
    }

    [Fact]
    public void Symbol_import_brings_in_qelectrotech_elements_and_reimporting_does_not_duplicate()
    {
        var folder = Path.Combine(FindRepositoryRoot(), "examples", "qelectrotech");

        var first = Run("symbol", "import", _project, folder);
        var second = Run("symbol", "import", _project, folder);

        Assert.Equal(0, first.Exit);
        Assert.Contains("Start butonu (NO)  ·  2 uç  ·  Apache-2.0", first.Output);
        Assert.Contains("3 sembol alındı, 0 eleman atlandı", second.Output);
        var saved = File.ReadAllText(_project);
        Assert.Equal(3, saved.Split("\"name\": \"Kontaktör bobini\"").Length - 1 + saved.Split("\"name\": \"Sinyal lambası\"").Length - 1 + saved.Split("\"name\": \"Start butonu (NO)\"").Length - 1);
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_directory.FullName, "makine.audit.jsonl")).Length);
    }

    [Fact]
    public void Symbol_import_reports_what_it_skipped_and_fails_when_nothing_is_left()
    {
        var element = Path.Combine(_directory.FullName, "lisanssiz.elmt");
        File.WriteAllText(element, """<definition><informations>yok</informations><description><terminal x="0" y="0" orientation="n"/></description></definition>""");

        var refused = Run("symbol", "import", _project, element);
        var licensed = Run("symbol", "import", _project, element, "--license", "CC0");

        Assert.Equal(1, refused.Exit);
        Assert.Contains("ATLANDI qet.lisans_yok", refused.Error);
        Assert.Contains("qet.eleman_yok", refused.Error);
        Assert.Equal(0, licensed.Exit);
        Assert.Contains("qet.yol_yok", Run("symbol", "import", _project, Path.Combine(_directory.FullName, "yok")).Error);
    }

    [Fact]
    public void Symbols_go_into_the_shared_library_and_only_the_used_one_is_copied_into_the_project()
    {
        var folder = Path.Combine(FindRepositoryRoot(), "examples", "qelectrotech");
        var patch = Path.Combine(_directory.FullName, "ata.json");
        File.WriteAllText(
            patch,
            """{ "patchVersion": 1, "commands": [ { "op": "assignSymbol", "function": "-S1:13", "symbol": "Start butonu (NO)" } ] }""");

        var imported = Run("lib", "symbol-import", folder, "--library", LibraryPath);
        var search = Run("lib", "symbols", "lamba", "--library", LibraryPath);
        var applied = Run("apply", _project, patch, "--library", LibraryPath);

        Assert.Equal(0, imported.Exit);
        Assert.Contains("3 sembol eklendi, 0 güncellendi, 0 eleman atlandı", imported.Output);
        Assert.Contains("qelectrotech/kontrol/Sinyal lambası  ·  2 uç", search.Output);
        Assert.Equal(0, applied.Exit);
        var saved = File.ReadAllText(_project);
        Assert.Contains("\"name\": \"Start butonu (NO)\"", saved);
        Assert.DoesNotContain("Sinyal lambası", saved);
    }

    [Fact]
    public void Library_symbol_import_reports_skipped_elements_grouped_by_reason()
    {
        var element = Path.Combine(_directory.FullName, "lisanssiz.elmt");
        File.WriteAllText(element, """<definition><informations>yok</informations><description><terminal x="0" y="0" orientation="n"/></description></definition>""");

        var refused = Run("lib", "symbol-import", element, "--library", LibraryPath);

        Assert.Equal(1, refused.Exit);
        Assert.Contains("ATLANDI qet.lisans_yok: 1 eleman", refused.Error);
    }

    [Fact]
    public void Patch_assigning_a_symbol_the_project_does_not_have_changes_nothing()
    {
        var (exit, _, error) = Run("apply", _project, PatchPath("06-sinyal-lambasi.json"));

        Assert.Equal(1, exit);
        Assert.Contains("patch.bulunamadi", error);
        Assert.Equal(_original, File.ReadAllText(_project));
    }

    [Fact]
    public void Unknown_command_exits_with_failure()
    {
        Assert.NotEqual(0, Run("uydurma").Exit);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    [Fact]
    public void Symbol_inspect_shows_the_drawing_and_area_picks_one_view_of_it()
    {
        var output = Path.Combine(_directory.FullName, "cad");
        Assert.Equal(0, Run("render", _project, "--dxf", output).Exit);
        var drawing = Path.Combine(output, "sayfa-2.dxf");

        var inspect = Run("symbol", "inspect", drawing);
        var area = Run("symbol", "import", _project, drawing, "--license", "deneme", "--alan", "-1000000,-1000000,2000000,2000000");
        var empty = Run("symbol", "import", _project, drawing, "--license", "deneme", "--alan", "5000000,5000000,1,1");
        var invalid = Run("symbol", "import", _project, drawing, "--license", "deneme", "--alan", "1,2,3");

        Assert.Equal(0, inspect.Exit);
        Assert.Contains("sayfa-2.dxf", inspect.Output);
        Assert.Contains("--alan \"", inspect.Output);
        Assert.Contains("Katmanlar:", inspect.Output);
        Assert.Equal(0, area.Exit);
        Assert.Contains("sayfa-2", area.Output);
        Assert.Contains("dxf.bos", empty.Error);
        Assert.Equal(1, invalid.Exit);
        Assert.Contains("sembol.alan_gecersiz", invalid.Error);
        Assert.Equal(1, Run("symbol", "inspect", Path.Combine(output, "yok.dxf")).Exit);
    }

    [Fact]
    public void Communication_diagram_is_written_from_communication_pins_and_refused_without_any()
    {
        var output = Path.Combine(_directory.FullName, "haberlesme.png");
        var refused = Run("haberlesme", _project, "--cikti", output);

        var project = Load();
        var connection = project.Connections.First(c => project.Locate(c.From)!.Device != project.Locate(c.To)!.Device);
        var communication = new Ake.Core.Model.PointAttributes(Ake.Core.Model.PinKind.Communication, Ake.Core.Model.ConnectorType.Rj45);
        Assert.True(new Ake.Core.Commands.SetPointAttributes(connection.From, communication).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.SetPointAttributes(connection.To, communication).Execute(project).IsSuccess);
        Assert.True(ProjectFile.Save(project, _project).IsSuccess);

        var png = Run("haberlesme", _project, "--cikti", output);
        var pdf = Run("haberlesme", _project, "--cikti", Path.Combine(_directory.FullName, "haberlesme.pdf"));
        var svg = Run("haberlesme", _project, "--cikti", Path.Combine(_directory.FullName, "haberlesme.svg"));
        var wrong = Run("haberlesme", _project, "--cikti", Path.Combine(_directory.FullName, "haberlesme.txt"));

        Assert.Equal(1, refused.Exit);
        Assert.Contains("haberlesme.yok", refused.Error);
        Assert.Equal(0, png.Exit);
        Assert.True(new FileInfo(output).Length > 1000);
        Assert.Equal(0, pdf.Exit);
        Assert.Equal(0, svg.Exit);
        Assert.Contains("Haberleşme şeması", File.ReadAllText(Path.Combine(_directory.FullName, "haberlesme.svg")), StringComparison.Ordinal);
        Assert.Contains("haberlesme.bicim", wrong.Error);
    }

    [Fact]
    public void Single_line_diagram_is_written_for_png_pdf_and_svg_and_other_formats_are_refused()
    {
        var png = Path.Combine(_directory.FullName, "tekhat.png");

        var written = Run("tekhat", _project, "--cikti", png);
        var pdf = Run("tekhat", _project, "--cikti", Path.Combine(_directory.FullName, "tekhat.pdf"));
        var svg = Run("tekhat", _project, "--cikti", Path.Combine(_directory.FullName, "tekhat.svg"));
        var wrong = Run("tekhat", _project, "--cikti", Path.Combine(_directory.FullName, "tekhat.txt"));

        Assert.Equal(0, written.Exit);
        Assert.True(new FileInfo(png).Length > 1000);
        Assert.Equal(0, pdf.Exit);
        Assert.Equal(0, svg.Exit);
        Assert.Contains("-Q1", File.ReadAllText(Path.Combine(_directory.FullName, "tekhat.svg")), StringComparison.Ordinal);
        Assert.Contains("tekhat.bicim", wrong.Error);
    }

    [Fact]
    public void Renumber_wires_takes_code_start_step_width_and_new_continues_where_it_left_off()
    {
        var all = Run("renumber", _project, "--wires", "--prefix", "W", "--start", "100", "--width", "3", "--scheme", "Sequential");
        var project = Load();
        var numbers = project.Connections.Where(c => c.Wire is not null).Select(c => c.Wire!.Number).ToList();

        Assert.Equal(0, all.Exit);
        // Kod, başlangıç ve basamak kaydedilir; --scheme yalnızca o çalıştırma içindir.
        Assert.Equal(new Ake.Core.Model.WireNumberSettings("W", 100, 1, 3), project.WireNumbering);
        Assert.Equal("W100", numbers[0]);
        Assert.Equal($"W{99 + numbers.Count}", numbers[^1]);

        // Yeni numarasız tel eklenir; --new yalnızca onu numaralar, mevcutlar aynı kalır.
        var a = project.Devices.Single(d => d.ProductTag == "K1").Functions.First().ConnectionPoints[0].Id;
        var b = project.Devices.Single(d => d.ProductTag == "K2").Functions.First().ConnectionPoints[0].Id;
        Assert.True(new Ake.Core.Commands.Connect(new Ake.Core.Model.Connection(Ake.Core.Model.ConnectionId.New(), a, b, new Ake.Core.Model.Wire(Ake.Core.Model.WireId.New()))).Execute(project).IsSuccess);
        Assert.True(ProjectFile.Save(project, _project).IsSuccess);

        var next = Run("renumber", _project, "--wires", "--new", "--scheme", "Sequential");
        var after = Load().Connections.Where(c => c.Wire is not null).Select(c => c.Wire!.Number).ToList();

        Assert.Equal(0, next.Exit);
        Assert.Equal(numbers, after.Take(numbers.Count));
        Assert.Equal($"W{100 + numbers.Count}", after[^1]);
        Assert.Equal(1, Run("renumber", _project, "--wires", "--prefix", "boşluk var").Exit);
    }
}
