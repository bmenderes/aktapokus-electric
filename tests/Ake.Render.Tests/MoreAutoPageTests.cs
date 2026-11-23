using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>Potansiyel özeti, pin-bağlantı, PLC, revizyon özeti ve çerçeve dokümantasyonu sayfaları (ADR-0050).</summary>
public class MoreAutoPageTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static Project Machine()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return Ake.Serialization.ProjectFile.Load(Path.Combine(directory!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
    }

    private static List<string> Texts(Project project, PageKind kind)
    {
        var page = new Page(PageId.New(), "9", "Tablo", kind: kind);
        Assert.True(new AddPage(page).Execute(project).IsSuccess);
        return [.. SheetRenderer.Render(project, page, Today).Value.Items.OfType<TextItem>().Select(text => text.Text)];
    }

    [Fact]
    public void The_potential_overview_lists_potentials_with_their_wire_numbers_and_ends()
    {
        var texts = Texts(Machine(), PageKind.PotentialOverview);

        Assert.Contains("Potansiyel", texts);
        Assert.Contains("Tel no", texts);
        Assert.Contains("Uç sayısı", texts);
        Assert.Contains(texts, text => text.Contains("=G1+P1-Q1:1", StringComparison.Ordinal)); // ana şalter girişi bir potansiyelin ucu
        Assert.Contains("4, 7, 23", texts); // tel numaraları sayısal sırayla
        Assert.True(texts.Count > 20);
    }

    [Fact]
    public void The_pin_connection_page_lists_each_device_end_with_its_target_and_wire()
    {
        var texts = Texts(Machine(), PageKind.PinConnections);

        Assert.Contains("Cihaz", texts);
        Assert.Contains("Tel / damar", texts);
        Assert.Contains("=C1+P1-A1", texts); // sıralı listenin ilk cihazı
        Assert.Contains("=C1+P1-T1:+", texts); // A1:24V ucunun hedefi

        // Çift uçlu 65 bağlantı bir sayfaya sığmaz: devam sayfaları gerekir, devam sayfası sonraki cihazları gösterir.
        var machine = Machine();
        var parts = SheetRenderer.PartCount(machine, PageKind.PinConnections, machine.SheetTemplates[0]);
        Assert.InRange(parts, 2, 5);
        var later = new List<string>();
        for (var part = 1; part < parts; part++)
        {
            var next = new Page(PageId.New(), $"9.{part}", "Pin", kind: PageKind.PinConnections, part: part);
            Assert.True(new AddPage(next).Execute(machine).IsSuccess);
            later.AddRange(SheetRenderer.Render(machine, next, Today).Value.Items.OfType<TextItem>().Select(text => text.Text));
        }

        Assert.Contains("=M1+P1-K1", later); // devam sayfalarında sonraki cihazlar
        Assert.Contains("10", later); // F1:2 – K1:1 telinin numarası
    }

    [Fact]
    public void The_plc_page_draws_a_box_per_module_with_a_header_and_one_row_per_channel()
    {
        var machine = Machine();
        var page = new Page(PageId.New(), "9", "PLC şeması", kind: PageKind.PlcDiagram);
        Assert.True(new AddPage(page).Execute(machine).IsSuccess);

        var scene = SheetRenderer.Render(machine, page, Today).Value;
        var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();

        Assert.Empty(scene.Warnings);
        Assert.All(new[] { "Kanal", "Adres", "Sembolik ad", "Açıklama", "Modül ucu", "Klemens", "Saha ucu" }, header => Assert.Contains(header, texts));
        Assert.Contains(texts, text => text.StartsWith("=C1+P1-A11  ·  8 kanal dijital giriş  ·  EL1008", StringComparison.Ordinal));
        Assert.Contains(texts, text => text.StartsWith("=C1+P1-A12", StringComparison.Ordinal));
        Assert.All(Enumerable.Range(0, 8), i => Assert.Contains($"DI{i}", texts));
        Assert.All(Enumerable.Range(0, 8), i => Assert.Contains($"DO{i}", texts));
        Assert.Contains("bStartButton", texts);
        Assert.Contains("=C1+P1-S1:14", texts); // DI0'ın saha ucu
        // İki modül, iki dış kutu (kalın çerçeve).
        Assert.Equal(2, scene.Items.OfType<RectItem>().Count(rect => rect.StrokeMm == 0.35 && rect.Width > 200 && rect.X > 12));
    }

    [Fact]
    public void A_project_without_plc_modules_says_so_and_a_long_plc_continues_on_the_next_pages()
    {
        var empty = Project.Create(
            ProjectId.New(), "p", pages: [new Page(PageId.New(), "1", "PLC", kind: PageKind.PlcDiagram)],
            sheetTemplates: [StandardSheetTemplates.A3Landscape]).Value;
        Assert.Contains("(PLC modülü yok)", SheetRenderer.Render(empty, empty.Pages[0], Today).Value.Items.OfType<TextItem>().Select(text => text.Text));

        // Kısa bir sayfa şablonu: modüller birkaç sayfaya bölünür, devam eden modülün başlığı yeniden yazılır.
        var machine = Machine();
        var shortTemplate = machine.SheetTemplates[0] with { HeightMm = 110 };
        Assert.True(new PutSheetTemplate(shortTemplate).Execute(machine).IsSuccess);
        var parts = SheetRenderer.PartCount(machine, PageKind.PlcDiagram, shortTemplate);
        Assert.InRange(parts, 3, 8);

        var first = new Page(PageId.New(), "9", "PLC", kind: PageKind.PlcDiagram);
        Assert.True(new AddPage(first).Execute(machine).IsSuccess);
        Assert.Contains(SheetRenderer.Render(machine, first, Today).Value.Warnings, warning => warning.Contains("devam sayfası", StringComparison.Ordinal));

        var all = new List<Page> { first };
        for (var part = 1; part < parts; part++)
        {
            var next = new Page(PageId.New(), $"9.{part}", "PLC", kind: PageKind.PlcDiagram, part: part);
            Assert.True(new AddPage(next).Execute(machine).IsSuccess);
            all.Add(next);
        }

        var channels = new List<string>();
        var headers = new List<string>();
        foreach (var next in all)
        {
            var scene = SheetRenderer.Render(machine, next, Today).Value;
            var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();
            channels.AddRange(texts.Where(text => text.Length == 3 && (text.StartsWith("DI", StringComparison.Ordinal) || text.StartsWith("DO", StringComparison.Ordinal))));
            headers.AddRange(texts.Where(text => text.StartsWith("=C1+P1-A1", StringComparison.Ordinal)));
            Assert.Empty(scene.Warnings); // devam sayfaları eklendi
        }

        Assert.Equal(16, channels.Count);
        Assert.Equal(16, channels.Distinct().Count()); // her kanal tam bir kez
        Assert.Contains(headers, header => header.EndsWith("devam", StringComparison.Ordinal));
        Assert.True(headers.Count > 2);

        var beyond = new Page(PageId.New(), "9.9", "PLC", kind: PageKind.PlcDiagram, part: parts + 2);
        Assert.True(new AddPage(beyond).Execute(machine).IsSuccess);
        Assert.Contains(SheetRenderer.Render(machine, beyond, Today).Value.Warnings, warning => warning.Contains("satır kalmadı", StringComparison.Ordinal));
    }

    [Fact]
    public void The_revision_overview_lists_the_revision_history_or_says_there_is_none()
    {
        var empty = Machine();
        Assert.Contains("(kayıt yok)", Texts(empty, PageKind.RevisionOverview));

        var project = Machine();
        Assert.True(new AddRevision(new Revision("A", "15.11.2026", "AKE", "İlk yayın")).Execute(project).IsSuccess);
        Assert.True(new AddRevision(new Revision("B", "19.11.2026", "AKE", "K7 eklendi")).Execute(project).IsSuccess);

        var texts = Texts(project, PageKind.RevisionOverview);

        Assert.Contains("Rev.", texts);
        Assert.Contains("İlk yayın", texts);
        Assert.Contains("K7 eklendi", texts);
        Assert.Contains("19.11.2026", texts);
        Assert.DoesNotContain("(kayıt yok)", texts);
    }

    [Fact]
    public void The_frame_documentation_describes_each_template_and_how_many_pages_use_it()
    {
        var project = Machine();
        var template = project.SheetTemplates[0];

        var texts = Texts(project, PageKind.FrameDocumentation);

        Assert.Contains(template.Name, texts);
        Assert.Contains("420 x 297", texts);
        Assert.Contains("10 x 6", texts);
        Assert.Contains("20 mm", texts); // antet yüksekliği
        Assert.Contains((project.Pages.Count).ToString(System.Globalization.CultureInfo.InvariantCulture), texts); // şablonu kullanan sayfa sayısı (yeni sayfa dahil)
    }

    [Fact]
    public void The_new_table_kinds_are_generated_table_pages_and_the_plc_diagram_is_a_generated_drawing_page()
    {
        foreach (var kind in new[] { PageKind.PotentialOverview, PageKind.PinConnections, PageKind.RevisionOverview, PageKind.FrameDocumentation })
        {
            Assert.True(SheetRenderer.IsGenerated(kind), kind.ToString());
            Assert.True(SheetRenderer.IsTable(kind), kind.ToString());
            Assert.InRange(SheetRenderer.PartCount(Machine(), kind, Machine().SheetTemplates[0]), 1, 5);
        }

        Assert.True(SheetRenderer.IsGenerated(PageKind.PlcDiagram));
        Assert.False(SheetRenderer.IsTable(PageKind.PlcDiagram)); // tablo değil, modül kutuları çizilir
        Assert.Equal(1, SheetRenderer.PartCount(Machine(), PageKind.PlcDiagram, Machine().SheetTemplates[0]));
    }
}
