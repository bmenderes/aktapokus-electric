using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Render.Tests;

public sealed class CoverLogoAndMultilingualTests
{
    [Fact]
    public void Cover_places_company_logo_and_resolves_multilingual_strings_by_project_language()
    {
        var project = Project.Create(
            ProjectId.New(),
            "tr=Kahve Makinesi;en=Coffee Machine",
            sheetTemplates: [StandardSheetTemplates.A3Landscape]).Value;
        var tid = StandardSheetTemplates.A3Landscape.Id;
        var cover = new Page(PageId.New(), "0", "tr=Kapak;en=Cover", templateId: tid, kind: PageKind.Cover);
        var toc = new Page(PageId.New(), "0a", "tr=İçindekiler;en=Contents", templateId: tid, kind: PageKind.Contents);
        var schema = new Page(PageId.New(), "1", "tr=Güç dağıtımı;en=Power distribution", templateId: tid, kind: PageKind.Schematic);

        Assert.True(new AddPage(cover).Execute(project).IsSuccess);
        Assert.True(new AddPage(toc).Execute(project).IsSuccess);
        Assert.True(new AddPage(schema).Execute(project).IsSuccess);
        Assert.True(new AddGraphics([new PageGraphic(GraphicId.New(), schema.Id, new GraphicText(40, 60, "tr=Motor devresi;en=Motor circuit"))]).Execute(project).IsSuccess);
        Assert.True(new SetProjectProperty("companyLogo", "assets/logo.png").Execute(project).IsSuccess);
        Assert.True(new SetProjectProperty("description", "tr=Çift hat;en=Dual lane").Execute(project).IsSuccess);
        Assert.True(new SetProjectProperty("language", "en").Execute(project).IsSuccess);

        var today = new DateOnly(2026, 11, 21);

        var coverScene = SheetRenderer.Render(project, cover, today);
        Assert.True(coverScene.IsSuccess, coverScene.Error?.Message);
        Assert.Contains(coverScene.Value.Items.OfType<ImageItem>(), img => img.Path == "assets/logo.png");
        Assert.Contains(coverScene.Value.Items.OfType<TextItem>(), t => t.Text == "Coffee Machine");
        Assert.Contains(coverScene.Value.Items.OfType<TextItem>(), t => t.Text == "Cover");
        Assert.Contains(coverScene.Value.Items.OfType<TextItem>(), t => t.Text == "Dual lane");

        var tocScene = SheetRenderer.Render(project, toc, today);
        Assert.True(tocScene.IsSuccess, tocScene.Error?.Message);
        Assert.Contains(tocScene.Value.Items.OfType<TextItem>(), t => t.Text == "Power distribution");

        var schemaScene = SheetRenderer.Render(project, schema, today);
        Assert.True(schemaScene.IsSuccess, schemaScene.Error?.Message);
        Assert.Contains(schemaScene.Value.Items.OfType<TextItem>(), t => t.Text == "Motor circuit");
        Assert.Equal("1  Power distribution", schemaScene.Value.Bookmark);

        // Varsayılan (dil ayarı yok) Türkçe kolunu seçer.
        Assert.True(new SetProjectProperty("language", null).Execute(project).IsSuccess);
        var defaultCover = SheetRenderer.Render(project, cover, today);
        Assert.True(defaultCover.IsSuccess);
        Assert.Contains(defaultCover.Value.Items.OfType<TextItem>(), t => t.Text == "Kahve Makinesi");
    }

    [Fact]
    public void PdfExporter_ignores_unc_and_non_image_logo_paths_safely()
    {
        var dir = Directory.CreateTempSubdirectory("ake-pdf-sec-");
        try
        {
            var txtPath = Path.Combine(dir.FullName, "notes.txt");
            File.WriteAllText(txtPath, "not an image");

            var scenes = new[]
            {
                new Scene(
                    420,
                    297,
                    [
                        new ImageItem(10, 10, 40, 20, @"\\127.0.0.1\nonexistent\logo.png"),
                        new ImageItem(60, 10, 40, 20, "notes.txt"),
                    ]),
            };

            var outPdf = Path.Combine(dir.FullName, "out.pdf");
            var exported = PdfExporter.Save(scenes, outPdf, dir.FullName, "Güvenlik Testi");
            Assert.True(exported.IsSuccess, exported.Error?.Message);
            Assert.True(File.Exists(outPdf));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Standard_title_block_logo_cell_uses_project_companyLogo_when_set()
    {
        var project = Project.Create(
            ProjectId.New(),
            "Logo Testi",
            sheetTemplates: [StandardSheetTemplates.A3Landscape]).Value;
        var tid = StandardSheetTemplates.A3Landscape.Id;
        var schema = new Page(PageId.New(), "1", "Şema", templateId: tid, kind: PageKind.Schematic);
        Assert.True(new AddPage(schema).Execute(project).IsSuccess);

        var today = new DateOnly(2026, 11, 23);
        var defaultScene = SheetRenderer.Render(project, schema, today);
        Assert.True(defaultScene.IsSuccess);
        Assert.Contains(defaultScene.Value.Items.OfType<ImageItem>(), img => img.Path == "logo.png");

        Assert.True(new SetProjectProperty("companyLogo", "assets/firma-logo.png").Execute(project).IsSuccess);

        var customScene = SheetRenderer.Render(project, schema, today);
        Assert.True(customScene.IsSuccess);
        Assert.Contains(customScene.Value.Items.OfType<ImageItem>(), img => img.Path == "assets/firma-logo.png");

        var previewScene = SheetRenderer.PreviewTemplate(StandardSheetTemplates.A3Landscape, project, schema, today);
        Assert.True(previewScene.IsSuccess);
        Assert.Contains(previewScene.Value.Items.OfType<ImageItem>(), img => img.Path == "assets/firma-logo.png");
    }
}
