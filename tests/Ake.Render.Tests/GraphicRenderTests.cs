using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Render.Tests;

/// <summary>Serbest grafik çizimi ve çıktıları (ADR-0028).</summary>
public sealed class GraphicRenderTests
{
    private static Project WithGraphics(out Page page)
    {
        var project = ProjectFile.Load(Path.Combine(Root(), "examples", "ref-machine", "ref-machine.json")).Value;
        page = project.Pages[0];
        var added = new CommandStack(project).Execute(
            new AddGraphics(
            [
                new PageGraphic(GraphicId.New(), page.Id, new GraphicPolyline([100, 100, 120, 100, 120, 110], StrokeMm: 0.5) { Color = "#00aa00" }),
                new PageGraphic(GraphicId.New(), page.Id, new GraphicPolyline([200, 100, 210, 100, 210, 110], Closed: true)),
                new PageGraphic(GraphicId.New(), page.Id, new GraphicCircle(150, 150, 5, 0.3, Filled: true)),
                new PageGraphic(GraphicId.New(), page.Id, new GraphicText(160, 160, "Not düşüldü", 3, TextAlign.Center)),
                new PageGraphic(GraphicId.New(), project.Pages[1].Id, new GraphicText(10, 10, "başka sayfa")),
            ]),
            ChangeSource.User,
            "test");
        Assert.True(added.IsSuccess, added.Error?.Message);
        return project;
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static Scene Render(Project project, Page page) => SheetRenderer.Render(project, page, new DateOnly(2026, 11, 17)).Value;

    [Fact]
    public void Graphics_become_scene_items_on_their_own_page_only()
    {
        var project = WithGraphics(out var page);

        var items = Render(project, page).Items;

        Assert.Contains(new LineItem(100, 100, 120, 100, 0.5, "#00aa00"), items);
        Assert.Contains(new LineItem(120, 100, 120, 110, 0.5, "#00aa00"), items);
        Assert.Contains(new CircleItem(150, 150, 5, 0.3, true), items);
        Assert.Contains(items.OfType<TextItem>(), text => text.Text == "Not düşüldü" && text.Align == TextAlign.Center);
        Assert.DoesNotContain(items.OfType<TextItem>(), text => text.Text == "başka sayfa");
        Assert.Contains(Render(project, project.Pages[1]).Items.OfType<TextItem>(), text => text.Text == "başka sayfa");
    }

    [Fact]
    public void Closed_polyline_gets_its_closing_segment()
    {
        var project = WithGraphics(out var page);

        Assert.Contains(new LineItem(210, 110, 200, 100, 0.25), Render(project, page).Items);
    }

    [Fact]
    public void Boxes_cover_each_graphic_of_the_page_for_selection()
    {
        var project = WithGraphics(out var page);

        var boxes = SheetRenderer.GraphicBoxes(project, page);

        Assert.Equal(4, boxes.Count);
        Assert.Equal(new Box(100, 100, 20, 10), boxes[0].Bounds);
        Assert.Equal(new Box(145, 145, 10, 10), boxes[2].Bounds);
    }

    [Fact]
    public void Graphics_reach_svg_pdf_dxf_and_dwg_outputs()
    {
        var project = WithGraphics(out var page);
        var scene = Render(project, page);
        var directory = Directory.CreateTempSubdirectory("ake-graphic-");
        try
        {
            var svg = SvgExporter.Write(scene);
            var pdf = Path.Combine(directory.FullName, "s.pdf");
            var dxf = Path.Combine(directory.FullName, "s.dxf");
            var dwg = Path.Combine(directory.FullName, "s.dwg");

            Assert.Contains("Not düşüldü", svg, StringComparison.Ordinal);
            Assert.True(PdfExporter.Save([scene], pdf, null, "deneme").IsSuccess);
            Assert.True(CadExporter.Save(scene, dxf, CadFormat.Dxf).IsSuccess);
            Assert.True(CadExporter.Save(scene, dwg, CadFormat.Dwg).IsSuccess);
            Assert.Contains("Not düşüldü", File.ReadAllText(dxf), StringComparison.Ordinal);
            Assert.True(new FileInfo(dwg).Length > 1000);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
