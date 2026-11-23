using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>Otomatik üretilen sayfalar (ADR-0046): kapak ve tablo sayfaları (içindekiler, malzeme listesi, klemens planı, kablo listesi).</summary>
public class AutoPageTests
{
    private static readonly DateOnly Today = new(2026, 11, 19);

    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private static Project Make(IEnumerable<Page> pages, IReadOnlyDictionary<string, string>? properties = null) =>
        Project.Create(ProjectId.New(), "Konveyör hattı", pages: pages, sheetTemplates: [Template], properties: properties).Value;

    private static Scene Render(Project project, int page) => SheetRenderer.Render(project, project.Pages[page], Today).Value;

    private static List<string> Texts(Scene scene) => [.. scene.Items.OfType<TextItem>().Select(text => text.Text)];

    [Fact]
    public void The_cover_shows_the_project_name_and_only_the_properties_that_have_a_value()
    {
        var project = Make(
            [new Page(PageId.New(), "1", "Kapak", kind: PageKind.Cover)],
            new Dictionary<string, string>
            {
                ["description"] = "İki motorlu konveyör",
                ["customer"] = "Örnek A.Ş.",
                ["drawingNo"] = "KNV-001",
                ["revision"] = "B",
            });

        var texts = Texts(Render(project, 0));

        Assert.Contains("Konveyör hattı", texts);
        Assert.Contains("İki motorlu konveyör", texts);
        Assert.Contains("Müşteri", texts);
        Assert.Contains("Örnek A.Ş.", texts);
        Assert.Contains("Çizim no", texts);
        Assert.Contains("KNV-001", texts);
        Assert.Contains("Revizyon", texts);
        Assert.Contains("Toplam sayfa", texts);
        Assert.Contains("Sayfa şablonu", texts);
        Assert.DoesNotContain("Çizen", texts); // değeri olmayan satır çizilmez
        Assert.DoesNotContain("Sipariş / iş no", texts);
    }

    [Fact]
    public void The_cover_keeps_the_frame_and_the_title_block_of_the_sheet()
    {
        var project = Make([new Page(PageId.New(), "1", "Kapak", kind: PageKind.Cover)]);

        var scene = Render(project, 0);

        Assert.Contains(scene.Items.OfType<RectItem>(), rect => rect.X == 10 && rect.Y == 10);
        Assert.Contains("Kapak", Texts(scene)); // sayfa başlığı kapakta da antette de
        Assert.Equal(SheetRenderer.BookmarkOf(project.Pages[0]), scene.Bookmark);
    }

    [Fact]
    public void The_contents_page_lists_every_page_and_each_row_links_to_its_page()
    {
        var project = Make(
        [
            new Page(PageId.New(), "1", "Kapak", kind: PageKind.Cover),
            new Page(PageId.New(), "2", "İçindekiler", kind: PageKind.Contents),
            new Page(PageId.New(), "3", "Güç"),
        ]);

        var scene = Render(project, 1);

        var texts = Texts(scene);
        Assert.Contains("Güç", texts);
        Assert.Contains("Kapak", texts);
        Assert.Equal(project.Pages.Select(SheetRenderer.AnchorOf), scene.Items.OfType<LinkItem>().Select(link => link.Target));
        Assert.Empty(scene.Warnings);
    }

    [Fact]
    public void A_table_that_does_not_fit_continues_on_the_next_part_and_warns_until_it_is_added()
    {
        var rows = SheetRenderer.RowsPerPage(Template);
        var many = Enumerable.Range(1, rows + 5).Select(i => new Page(PageId.New(), i.ToString(System.Globalization.CultureInfo.InvariantCulture), $"Sayfa {i}")).ToList();
        many.Insert(0, new Page(PageId.New(), "0", "İçindekiler", kind: PageKind.Contents));
        var withoutPart = Make(many);

        Assert.Equal(2, SheetRenderer.PartCount(withoutPart, PageKind.Contents, Template));
        var first = SheetRenderer.Render(withoutPart, withoutPart.Pages[0], Today).Value;
        Assert.Contains(first.Warnings, warning => warning.Contains("devam sayfası", StringComparison.Ordinal));
        Assert.Equal(rows, first.Items.OfType<LinkItem>().Count());

        many.Add(new Page(PageId.New(), "0.1", "İçindekiler", kind: PageKind.Contents, part: 1));
        var project = Make(many);
        var second = Render(project, project.Pages.Count - 1);

        Assert.Empty(second.Warnings);
        Assert.Contains("İçindekiler (devam 2)", Texts(second));
        Assert.Equal(project.Pages.Count - rows, second.Items.OfType<LinkItem>().Count());
        Assert.Empty(SheetRenderer.Render(project, project.Pages[0], Today).Value.Warnings);
    }

    [Fact]
    public void A_continuation_page_with_nothing_left_says_so()
    {
        var project = Make([new Page(PageId.New(), "1", "İçindekiler", kind: PageKind.Contents, part: 3)]);

        Assert.Contains(Render(project, 0).Warnings, warning => warning.Contains("satır kalmadı", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(PageKind.PartsList, "Malzeme listesi", "Adet")]
    [InlineData(PageKind.TerminalPlan, "Klemens planı", "Klemens")]
    [InlineData(PageKind.CableList, "Kablo listesi", "Damar")]
    public void The_report_pages_draw_the_report_table_with_its_headers(PageKind kind, string title, string header)
    {
        var project = Make([new Page(PageId.New(), "1", title, kind: kind)]);

        var texts = Texts(Render(project, 0));

        Assert.Contains(title, texts);
        Assert.Contains(header, texts);
        Assert.Contains("(kayıt yok)", texts); // boş projede tablo boştur
    }

    [Fact]
    public void The_report_pages_of_the_reference_machine_have_rows_and_fit_the_page_count()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var machine = Ake.Serialization.ProjectFile.Load(Path.Combine(directory!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
        var template = machine.SheetTemplates[0];

        foreach (var kind in new[] { PageKind.PartsList, PageKind.TerminalPlan, PageKind.CableList, PageKind.Contents })
        {
            var parts = SheetRenderer.PartCount(machine, kind, template);
            Assert.InRange(parts, 1, 3);
            var page = new Page(PageId.New(), "9", "Tablo", kind: kind);
            Assert.True(new Ake.Core.Commands.AddPage(page).Execute(machine).IsSuccess);
            var scene = SheetRenderer.Render(machine, page, Today).Value;
            Assert.True(scene.Items.OfType<TextItem>().Count() > 12, kind.ToString());
            new Ake.Core.Commands.AddPage(page).Undo(machine);
        }
    }

    [Fact]
    public void Only_cover_and_table_kinds_are_generated_and_only_table_kinds_split_into_parts()
    {
        Assert.True(SheetRenderer.IsGenerated(PageKind.Cover));
        Assert.False(SheetRenderer.IsGenerated(PageKind.Schematic));
        Assert.False(SheetRenderer.IsGenerated(PageKind.SingleLineDiagram));
        Assert.False(SheetRenderer.IsTable(PageKind.Cover));
        Assert.All([PageKind.Contents, PageKind.PartsList, PageKind.TerminalPlan, PageKind.CableList], kind => Assert.True(SheetRenderer.IsTable(kind)));
        Assert.Equal(1, SheetRenderer.PartCount(Make([new Page(PageId.New(), "1", "K", kind: PageKind.Cover)]), PageKind.Cover, Template));
    }

    [Fact]
    public void Every_standard_template_renders_a_cover_and_a_table_page_that_stay_inside_the_frame()
    {
        foreach (var template in StandardSheetTemplates.All)
        {
            var project = Project.Create(
                ProjectId.New(),
                "Çok uzun bir proje adı: iki motorlu konveyör hattı ve yardımcı sistemleri",
                pages:
                [
                    new Page(PageId.New(), "1", "Kapak", templateId: template.Id, kind: PageKind.Cover),
                    new Page(PageId.New(), "2", "İçindekiler", templateId: template.Id, kind: PageKind.Contents),
                ],
                sheetTemplates: [template]).Value;

            foreach (var page in project.Pages)
            {
                var scene = SheetRenderer.Render(project, page, Today).Value;
                var frame = SheetGeometry.For(template).Frame;
                Assert.Equal((template.WidthMm, template.HeightMm), (scene.WidthMm, scene.HeightMm));
                foreach (var text in scene.Items.OfType<TextItem>().Where(text => text.Align == TextAlign.Center))
                {
                    // Ortalanmış başlık (yaklaşık genişlik) çerçevenin içinde kalır.
                    Assert.InRange(text.X - (text.Text.Length * text.SizeMm * 0.5 / 2), frame.X, frame.Right);
                    Assert.InRange(text.X + (text.Text.Length * text.SizeMm * 0.5 / 2), frame.X, frame.Right);
                }
            }
        }
    }

    [Fact]
    public void Label_pdf_exporter_lays_out_cut_grid_pages_and_saves_pdf()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var machine = Ake.Serialization.ProjectFile.Load(Path.Combine(directory!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
        var scenes = LabelPdfExporter.BuildScenes(machine);
        Assert.True(scenes.Count >= 1);
        Assert.Contains(scenes[0].Items.OfType<LineItem>(), line => line.Style == LineStyle.Dashed);

        var tempDir = Directory.CreateTempSubdirectory("ake-label-pdf-");
        try
        {
            var pdfPath = Path.Combine(tempDir.FullName, "etiketler.pdf");
            Assert.True(LabelPdfExporter.Save(machine, pdfPath).IsSuccess);
            Assert.True(File.Exists(pdfPath));
            Assert.True(new FileInfo(pdfPath).Length > 1000);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }
}
