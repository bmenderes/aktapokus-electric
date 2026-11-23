using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Render.Tests;

public sealed class TitleBlockPreviewTests
{
    [Fact]
    public void Title_block_resolves_company_logo_placeholder_and_preserves_literal_image_ref()
    {
        var today = new DateOnly(2026, 11, 22);
        var template = new SheetTemplate(
            SheetTemplateId.New(),
            "Logolu Antet",
            420,
            297,
            10,
            10,
            6,
            5,
            20,
            "Arial",
            [
                new TitleBlockCell(0, 0, 60, 20, ImageRef: "{project.companyLogo}"),
                new TitleBlockCell(60, 0, 60, 20, ImageRef: "sabit-logo.png"),
                new TitleBlockCell(120, 0, 280, 20, "Proje:", "{project.name}"),
            ]);

        var project = Project.Create(ProjectId.New(), "Antet Projesi", sheetTemplates: [template]).Value;
        var page = new Page(PageId.New(), "1", "Güç Şeması", templateId: template.Id);
        Assert.True(new AddPage(page).Execute(project).IsSuccess);
        Assert.True(new SetProjectProperty("companyLogo", "gorsel/firma.png").Execute(project).IsSuccess);

        var preview = SheetRenderer.PreviewTemplate(template, project, page, today);
        Assert.True(preview.IsSuccess, preview.Error?.Message);
        Assert.Contains(preview.Value.Items.OfType<ImageItem>(), i => i.Path == "gorsel/firma.png");
        Assert.Contains(preview.Value.Items.OfType<ImageItem>(), i => i.Path == "sabit-logo.png");
        Assert.Contains(preview.Value.Items.OfType<TextItem>(), t => t.Text == "Antet Projesi");

        // companyLogo boşken yer tutucu hücre görüntü öğesi üretmez; sabit yol korunur.
        Assert.True(new SetProjectProperty("companyLogo", null).Execute(project).IsSuccess);
        var withoutLogo = SheetRenderer.PreviewTemplate(template, project, page, today);
        Assert.True(withoutLogo.IsSuccess);
        var onlyImg = Assert.Single(withoutLogo.Value.Items.OfType<ImageItem>());
        Assert.Equal("sabit-logo.png", onlyImg.Path);

        // Proje ve sayfa olmadan önizleme örnek metinlerle çalışır.
        var sample = SheetRenderer.PreviewTemplate(template, null, null, today);
        Assert.True(sample.IsSuccess, sample.Error?.Message);
        Assert.Contains(sample.Value.Items.OfType<TextItem>(), t => t.Text == "Örnek Proje");
    }

    [Fact]
    public void PreviewTemplate_rejects_out_of_bounds_cells()
    {
        var invalid = new SheetTemplate(
            SheetTemplateId.New(),
            "Hatalı",
            297,
            210,
            10,
            8,
            6,
            5,
            20,
            "Arial",
            [new TitleBlockCell(260, 0, 40, 20, "Taşan", "X")]);

        var result = SheetRenderer.PreviewTemplate(invalid, null, null, new DateOnly(2026, 11, 22));
        Assert.True(result.IsFailure);
    }
}
