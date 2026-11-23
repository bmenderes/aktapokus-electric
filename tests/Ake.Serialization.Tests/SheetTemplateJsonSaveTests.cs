using Ake.Core.Model;

namespace Ake.Serialization.Tests;

public sealed class SheetTemplateJsonSaveTests
{
    [Fact]
    public void Save_and_load_round_trip_custom_template_with_logo()
    {
        var temp = Directory.CreateTempSubdirectory("ake-tpl-");
        try
        {
            var path = Path.Combine(temp.FullName, "ozel.sablon.json");
            var template = new SheetTemplate(
                SheetTemplateId.New(),
                "Özel Firma Anteti",
                420,
                297,
                10,
                10,
                6,
                5,
                22,
                "Arial",
                [
                    new TitleBlockCell(0, 0, 75, 22, ImageRef: "firma-logo.png"),
                    new TitleBlockCell(75, 0, 200, 22, "Başlık:", "{page.title}", 3.5),
                    new TitleBlockCell(275, 0, 125, 22, "Sayfa:", "{page.number}", 3, TextAlign.Right),
                ]);

            var saved = SheetTemplateJson.Save(template, path);
            Assert.True(saved.IsSuccess, saved.Error?.Message);

            var loaded = SheetTemplateJson.Load(path);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);
            Assert.Equal(template.Id, loaded.Value.Id);
            Assert.Equal(template.Name, loaded.Value.Name);
            Assert.Equal("firma-logo.png", loaded.Value.Cells[0].ImageRef);
            Assert.Equal(3, loaded.Value.Cells.Count);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
