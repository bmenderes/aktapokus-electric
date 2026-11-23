using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>Tek hat şeması: güç zincirinden otomatik, antetli sayfa (ADR-0039).</summary>
public class SingleLineDiagramTests
{
    private static Project Machine() => Ake.Serialization.ProjectFile.Load(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json")).Value;

    private static readonly DateOnly Today = new(2026, 11, 18);

    [Fact]
    public void The_page_shows_the_whole_chain_with_ratings_wire_sections_the_cable_and_the_title_block()
    {
        var scene = SingleLineDiagram.Build(Machine(), Today).Value;
        var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();

        // Antet ve çerçeve (şablon).
        Assert.Contains("Tek hat şeması", texts);
        Assert.Contains("Referans makine: iki motorlu konveyör", texts);
        // Zincir: besleme, ana şalter, iki kol ve güç kaynağı kolu.
        foreach (var tag in new[] { "-X0", "-Q1", "-F1", "-F2", "-F3", "-K1", "-K2", "-M1", "-M2", "-T1" })
        {
            Assert.Contains(tag, texts);
        }

        Assert.Contains("L1  L2  L3  N  PE", texts);
        Assert.Contains("6,3 A", texts);
        Assert.Contains("2,5 mm²", texts);
        Assert.Contains("1,5 mm²", texts);
        Assert.Contains("-W1 4G1.5", texts);
        Assert.Contains("-W2 4G1.5", texts);
        // Kumanda elemanları şemada yok.
        Assert.DoesNotContain("-S0", texts);
        Assert.DoesNotContain("-K0", texts);
        Assert.Empty(scene.Warnings);
    }

    [Fact]
    public void The_chain_is_laid_out_as_a_tree_shared_parts_once_and_everything_inside_the_drawing_area()
    {
        var project = Machine();
        var template = project.SheetTemplates[0];
        var area = SheetGeometry.For(template).DrawingArea;
        var scene = SingleLineDiagram.Build(project, Today).Value;

        var boxes = scene.Items.OfType<RectItem>().Where(rect => rect.Width > 30 && rect.Width < 70 && rect.Height > 20 && rect.Height < 35).ToList();
        Assert.Equal(10, boxes.Count);
        Assert.All(boxes, box =>
        {
            Assert.InRange(box.X, area.X - 0.01, area.Right + 0.01);
            Assert.InRange(box.X + box.Width, area.X - 0.01, area.Right + 0.01);
            Assert.InRange(box.Y, area.Y - 0.01, area.Bottom + 0.01);
            Assert.InRange(box.Y + box.Height, area.Y - 0.01, area.Bottom + 0.01);
        });
        // Dört seviye aşağı: besleme, ana şalter, koruma, kontaktör, yük => beş farklı satır.
        Assert.Equal(5, boxes.Select(box => Math.Round(box.Y)).Distinct().Count());
        // Hiçbir kutu ötekiyle çakışmaz.
        for (var i = 0; i < boxes.Count; i++)
        {
            for (var j = i + 1; j < boxes.Count; j++)
            {
                var (a, b) = (boxes[i], boxes[j]);
                var overlap = a.X < b.X + b.Width && b.X < a.X + a.Width && a.Y < b.Y + b.Height && b.Y < a.Y + a.Height;
                Assert.False(overlap, $"Kutular çakışıyor: {a} / {b}");
            }
        }
    }

    [Fact]
    public void A_project_without_a_template_or_without_a_chain_is_refused_with_a_clear_error()
    {
        var project = Machine();
        var withoutTemplate = Project.Create(
            ProjectId.New(), "Şablonsuz", structure: project.Structure, devices: project.Devices, connections: project.Connections,
            cables: project.Cables, potentialSources: project.PotentialSources).Value;
        var withoutChain = Project.Create(
            ProjectId.New(), "Beslemesiz", structure: project.Structure, devices: project.Devices, connections: project.Connections,
            cables: project.Cables, sheetTemplates: project.SheetTemplates).Value;

        Assert.Equal("render.sablon_yok", SingleLineDiagram.Build(withoutTemplate).Error!.Code);
        Assert.Equal("tekhat.zincir_yok", SingleLineDiagram.Build(withoutChain).Error!.Code);
        Assert.False(SingleLineDiagram.HasChain(withoutChain));
        Assert.True(SingleLineDiagram.HasChain(project));
    }

    [Fact]
    public void The_diagram_renders_to_png_and_a_page_of_that_kind_renders_through_the_sheet_renderer()
    {
        var project = Machine();
        var page = new Page(PageId.New(), "9", "Tek hat", templateId: project.SheetTemplates[0].Id, kind: PageKind.SingleLineDiagram);
        Assert.True(new Ake.Core.Commands.AddPage(page).Execute(project).IsSuccess);

        var scene = SheetRenderer.Render(project, page, Today).Value;

        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "-M2");
        var path = Path.Combine(Path.GetTempPath(), $"ake-tekhat-{Guid.NewGuid():N}.png");
        try
        {
            Assert.True(PngExporter.Save(scene, path, null, 60).IsSuccess);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
