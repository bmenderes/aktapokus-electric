using Ake.Core.Macros;

namespace Ake.Serialization.Tests;

/// <summary>Makro dosyası (ADR-0025).</summary>
public class MacroJsonTests
{
    private static Macro MotorMacro()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var project = ProjectFile.Load(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
        return MacroService.Capture(project, project.Pages.Single(page => page.Number == "2").Id, "Motor çıkışı").Value;
    }

    [Fact]
    public void Macro_survives_a_round_trip_and_its_text_is_stable()
    {
        var json = MacroJson.Serialize(MotorMacro());

        var read = MacroJson.Deserialize(json);

        Assert.True(read.IsSuccess, read.Error?.Message);
        Assert.Equal(json, MacroJson.Serialize(read.Value));
        Assert.Contains("\"macroVersion\": 1", json);
        Assert.Contains("\"name\": \"Motor çıkışı\"", json);
        Assert.DoesNotContain("null", json);
    }

    [Fact]
    public void Unknown_field_and_newer_version_are_rejected()
    {
        Assert.Equal("makro.json_gecersiz", MacroJson.Deserialize("""{ "macroVersion": 1, "name": "m", "yeni": 1 }""").Error!.Code);
        Assert.Equal("makro.sema_yeni", MacroJson.Deserialize("""{ "macroVersion": 9, "name": "m" }""").Error!.Code);
        Assert.Equal("makro.sema_yok", MacroJson.Deserialize("""{ "name": "m" }""").Error!.Code);
    }

    [Fact]
    public void Macro_without_devices_is_rejected_with_the_model_reason()
    {
        Assert.Equal("makro.bos", MacroJson.Deserialize("""{ "macroVersion": 1, "name": "m" }""").Error!.Code);
    }

    [Fact]
    public void Save_and_load_use_a_file()
    {
        var directory = Directory.CreateTempSubdirectory("ake-macro-");
        try
        {
            var path = Path.Combine(directory.FullName, "motor.makro.json");
            Assert.True(MacroJson.Save(MotorMacro(), path).IsSuccess);

            Assert.Equal("Motor çıkışı", MacroJson.Load(path).Value.Name);
            Assert.Equal("makro.dosya_okunamadi", MacroJson.Load(Path.Combine(directory.FullName, "yok.json")).Error!.Code);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
