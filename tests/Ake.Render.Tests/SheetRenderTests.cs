using System.Text;
using Ake.Core.Model;
using Ake.Serialization;
using SkiaSharp;

namespace Ake.Render.Tests;

public class SheetRenderTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static Project Machine()
    {
        var result = ProjectFile.Load(Path.Combine(RepositoryRoot(), "examples", "ref-machine", "ref-machine.json"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static SheetTemplate ExampleTemplate()
    {
        var result = SheetTemplateJson.Load(Path.Combine(RepositoryRoot(), "examples", "templates", "a3-yatay.sablon.json"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static List<Scene> Scenes(Project project) =>
        [.. project.Pages.Select(page => SheetRenderer.Render(project, page, Today).Value)];

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }

    // ---- snapshot

    [Fact]
    public Task First_page_of_the_reference_machine_matches_its_snapshot()
    {
        var machine = Machine();

        var svg = SvgExporter.Write(SheetRenderer.Render(machine, machine.Pages[0], Today).Value);

        return Verifier.Verify(svg, extension: "svg");
    }

    // ---- geometri

    [Fact]
    public void Geometry_splits_the_sheet_into_frame_grid_bands_drawing_area_and_title_block()
    {
        var geometry = SheetGeometry.For(ExampleTemplate());

        Assert.Equal(new Box(10, 10, 400, 277), geometry.Frame);
        Assert.Equal(new Box(10, 267, 400, 20), geometry.TitleBlock);
        Assert.Equal(new Box(15, 15, 390, 252), geometry.DrawingArea);
        Assert.Equal(39, geometry.ColumnWidth, 6);
        Assert.Equal(42, geometry.RowHeight, 6);
    }

    [Fact]
    public void Grid_labels_follow_the_template()
    {
        var template = ExampleTemplate() with { Columns = 8, Rows = 4, FirstColumnNumber = 1, Cells = [] };
        var project = Project.Create(
            ProjectId.New(), "p", pages: [new Page(PageId.New(), "1", "Sayfa")], sheetTemplates: [template]).Value;

        var labels = SheetRenderer.Render(project, project.Pages[0], Today).Value.Items
            .OfType<TextItem>()
            .Select(text => text.Text)
            .ToList();

        Assert.Equal(["1", "2", "3", "4", "5", "6", "7", "8", "A", "A", "B", "B", "C", "C", "D", "D"], labels);
    }

    // ---- yer tutucular

    [Theory]
    [InlineData("{project.name}", "Referans makine: iki motorlu konveyör")]
    [InlineData("{project.drawingNo} / {project.revision}", "REF-001 / A")]
    [InlineData("{project.tanimsiz}", "")]
    [InlineData("{page.number}/{page.count}", "2/5")]
    [InlineData("{page.title}", "Motor 1")]
    [InlineData("{page.next}", "3")]
    [InlineData("{page.function} {page.location}", "=M1 +P1")]
    [InlineData("{today}", "15.11.2026")]
    [InlineData("{page.uydurma} {baska.sey} {duz}", "{page.uydurma} {baska.sey} {duz}")]
    [InlineData("yer tutucu yok", "yer tutucu yok")]
    public void Placeholders_are_filled_from_the_project_and_the_page(string text, string expected)
    {
        var machine = Machine();

        Assert.Equal(expected, SheetRenderer.Resolve(text, machine, machine.Pages[1], Today));
    }

    [Fact]
    public void Last_page_has_no_next_page()
    {
        var machine = Machine();

        Assert.Equal(string.Empty, SheetRenderer.Resolve("{page.next}", machine, machine.Pages[^1], Today));
    }

    [Fact]
    public void Multi_line_content_becomes_one_text_item_per_line()
    {
        var template = ExampleTemplate() with
        {
            Cells = [new TitleBlockCell(0, 0, 100, 20, Content: "{page.title}\nikinci satır", FontSizeMm: 3.3)],
        };
        var project = Project.Create(
            ProjectId.New(), "p", pages: [new Page(PageId.New(), "1", "Birinci")], sheetTemplates: [template]).Value;

        var lines = SheetRenderer.Render(project, project.Pages[0], Today).Value.Items
            .OfType<TextItem>()
            .Where(text => text.SizeMm == 3.3)
            .ToList();

        Assert.Equal(["Birinci", "ikinci satır"], lines.Select(line => line.Text));
        Assert.True(lines[1].Y > lines[0].Y);
    }

    // ---- şablon seçimi

    [Fact]
    public void Page_without_any_template_cannot_be_rendered()
    {
        var project = Project.Create(ProjectId.New(), "p", pages: [new Page(PageId.New(), "1", "Sayfa")]).Value;

        Assert.Equal("render.sablon_yok", SheetRenderer.Render(project, project.Pages[0], Today).Error!.Code);
    }

    [Fact]
    public void Page_uses_its_own_template_when_it_has_one()
    {
        var first = ExampleTemplate();
        var a4 = first with { Id = SheetTemplateId.New(), Name = "A4", WidthMm = 297, HeightMm = 210, Cells = [] };
        var project = Project.Create(
            ProjectId.New(),
            "p",
            pages: [new Page(PageId.New(), "1", "Varsayılan"), new Page(PageId.New(), "2", "A4", templateId: a4.Id)],
            sheetTemplates: [first, a4]).Value;

        var scenes = Scenes(project);

        Assert.Equal((420, 297), (scenes[0].WidthMm, scenes[0].HeightMm));
        Assert.Equal((297, 210), (scenes[1].WidthMm, scenes[1].HeightMm));
    }

    // ---- çıktı biçimleri

    [Fact]
    public void Svg_escapes_text_and_uses_millimetres()
    {
        var scene = new Scene(100, 50, [new TextItem(1, 2, "A & B < C", 3, "Arial", TextAlign.Right)]);

        var svg = SvgExporter.Write(scene);

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"100mm\" height=\"50mm\" viewBox=\"0 0 100 50\">", svg);
        Assert.Contains("text-anchor=\"end\">A &amp; B &lt; C</text>", svg);
    }

    [Fact]
    public void Pdf_has_one_page_per_scene()
    {
        var machine = Machine();
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "cikti.pdf");

            var saved = PdfExporter.Save(Scenes(machine), path, directory.FullName, machine.Name);

            Assert.True(saved.IsSuccess, saved.Error?.Message);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5));
            var text = Encoding.Latin1.GetString(bytes);
            Assert.Equal(5, CountPages(text));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Png_has_the_pixel_size_of_the_sheet_at_the_requested_resolution()
    {
        var machine = Machine();
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "sayfa.png");

            var saved = PngExporter.Save(Scenes(machine)[0], path, directory.FullName, dpi: 50);

            Assert.True(saved.IsSuccess, saved.Error?.Message);
            using var image = SKBitmap.Decode(path);
            Assert.Equal((827, 585), (image.Width, image.Height));
            Assert.Equal(SKColors.White, image.GetPixel(400, 250));
            Assert.NotEqual(SKColors.White, image.GetPixel(20, 300));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Logo_is_drawn_when_the_file_exists_and_skipped_when_it_does_not()
    {
        var machine = Machine();
        var scene = Scenes(machine)[0];
        var logoBox = Assert.Single(scene.Items.OfType<ImageItem>());
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var without = Path.Combine(directory.FullName, "logosuz.png");
            Assert.True(PngExporter.Save(scene, without, directory.FullName, dpi: 50).IsSuccess);

            using (var logo = new SKBitmap(40, 20))
            {
                logo.Erase(SKColors.Red);
                using var data = logo.Encode(SKEncodedImageFormat.Png, 100);
                File.WriteAllBytes(Path.Combine(directory.FullName, logoBox.Path), data.ToArray());
            }

            var with = Path.Combine(directory.FullName, "logolu.png");
            Assert.True(PngExporter.Save(scene, with, directory.FullName, dpi: 50).IsSuccess);

            var x = (int)((logoBox.X + (logoBox.Width / 2)) * 50 / 25.4);
            var y = (int)((logoBox.Y + (logoBox.Height / 2)) * 50 / 25.4);
            using var plain = SKBitmap.Decode(without);
            using var branded = SKBitmap.Decode(with);
            Assert.Equal(SKColors.White, plain.GetPixel(x, y));
            Assert.Equal(SKColors.Red, branded.GetPixel(x, y));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Writing_to_an_unwritable_path_fails_without_throwing()
    {
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var blocker = Path.Combine(directory.FullName, "engel");
            File.WriteAllText(blocker, string.Empty);
            var scene = new Scene(10, 10, []);

            Assert.Equal("render.dosya_yazilamadi", PdfExporter.Save([scene], Path.Combine(blocker, "a.pdf"), null, "t").Error?.Code);
            Assert.Equal("render.dosya_yazilamadi", PngExporter.Save(scene, Path.Combine(blocker, "a.png"), null).Error?.Code);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static int CountPages(string pdf)
    {
        var count = 0;
        var index = 0;
        while ((index = pdf.IndexOf("/Type /Page", index, StringComparison.Ordinal)) >= 0)
        {
            index += "/Type /Page".Length;
            if (index >= pdf.Length || pdf[index] != 's')
            {
                count++;
            }
        }

        return count;
    }
}
