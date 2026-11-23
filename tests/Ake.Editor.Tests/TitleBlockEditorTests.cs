using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor.Tests;

public sealed class TitleBlockEditorTests
{
    [Fact]
    public void SaveSheetTemplate_creates_applies_to_pages_and_supports_undo()
    {
        var session = new EditorSession();
        var today = new DateOnly(2026, 11, 22);
        Assert.True(session.NewProject(new NewProjectInfo("Antet Test"), today).IsSuccess);

        // Mevcut proje iki sayfayla (Kapak + 1 Şema) başlar.
        var baseTemplate = session.ActiveTemplateFor(1);
        Assert.NotNull(baseTemplate);

        var custom = new SheetTemplate(
            SheetTemplateId.New(),
            "Firma Anteti V1",
            420,
            297,
            10,
            10,
            6,
            5,
            24,
            "Arial",
            [
                new TitleBlockCell(0, 0, 70, 24, ImageRef: "assets/logo.png"),
                new TitleBlockCell(70, 0, 230, 24, "Başlık:", "{page.title}", 3.5),
                new TitleBlockCell(300, 0, 100, 24, "Sayfa:", "{page.number}", 3.5, TextAlign.Right),
            ]);

        var preview = session.PreviewTemplate(custom, 1, today);
        Assert.True(preview.IsSuccess, preview.Error?.Message);
        Assert.Contains(preview.Value.Items.OfType<ImageItem>(), i => i.Path == "assets/logo.png");

        // Etkin sayfaya uygula.
        var applied = session.SaveSheetTemplate(custom, applyToActivePage: true, activePageIndex: 1, applyToAllPages: false);
        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.Contains(session.ProjectTemplates, t => t.Id == custom.Id);
        Assert.Equal(custom.Id, session.Project!.Pages[1].TemplateId);

        // Geri alınınca şablon da sayfa ataması da geri alınır.
        Assert.True(session.Undo().IsSuccess);
        Assert.DoesNotContain(session.ProjectTemplates, t => t.Id == custom.Id);
        Assert.NotEqual(custom.Id, session.Project!.Pages[1].TemplateId);

        // Tüm sayfalara uygula.
        Assert.True(session.SaveSheetTemplate(custom, applyToAllPages: true).IsSuccess);
        Assert.All(session.Project!.Pages, p => Assert.Equal(custom.Id, p.TemplateId));

        // Taşan hücreli geçersiz şablon reddedilir.
        var invalid = custom with { Cells = [new TitleBlockCell(390, 0, 30, 24, "Taşan", "X")] };
        Assert.True(session.SaveSheetTemplate(invalid).IsFailure);

        // Komut Şerit ve Default KeyMap'te kayıtlı.
        Assert.Contains(EditorCommands.All, c => c.Id == EditorCommands.TitleBlockEditor);
    }
}
