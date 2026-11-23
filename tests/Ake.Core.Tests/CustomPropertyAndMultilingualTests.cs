using Ake.Core.Commands;
using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Core.Tests;

public sealed class CustomPropertyAndMultilingualTests
{
    [Fact]
    public void SetDeviceProperty_sets_updates_and_removes_with_undo()
    {
        var project = Project.Create(ProjectId.New(), "P").Value;
        var fn = new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")]);
        var dev = new Device(DeviceId.New(), "-K1", [fn]);
        Assert.True(new AddDevice(dev).Execute(project).IsSuccess);

        var add = new SetDeviceProperty(dev.Id, "Güç", "1.5 kW");
        Assert.True(add.Execute(project).IsSuccess);
        Assert.Equal("1.5 kW", dev.Properties["Güç"]);

        var update = new SetDeviceProperty(dev.Id, "Güç", "2.2 kW");
        Assert.True(update.Execute(project).IsSuccess);
        Assert.Equal("2.2 kW", dev.Properties["Güç"]);

        update.Undo(project);
        Assert.Equal("1.5 kW", dev.Properties["Güç"]);

        var remove = new SetDeviceProperty(dev.Id, "Güç", null);
        Assert.True(remove.Execute(project).IsSuccess);
        Assert.False(dev.Properties.ContainsKey("Güç"));

        remove.Undo(project);
        Assert.Equal("1.5 kW", dev.Properties["Güç"]);

        add.Undo(project);
        Assert.Empty(dev.Properties);
    }

    [Theory]
    [InlineData("tr=Motor çıkışı;en=Motor output;de=Motorabgang", "en", "Motor output")]
    [InlineData("tr=Motor çıkışı;en=Motor output;de=Motorabgang", "de-DE", "Motorabgang")]
    [InlineData("tr=Motor çıkışı;en=Motor output", "fr", "Motor çıkışı")]
    [InlineData("tr=Motor çıkışı;en=Motor output", null, "Motor çıkışı")]
    [InlineData("Sade metin = bozulmaz", "en", "Sade metin = bozulmaz")]
    public void MultilingualText_resolves_requested_language_or_leaves_plain_text(string raw, string? language, string expected)
    {
        Assert.Equal(expected, MultilingualText.Resolve(raw, language));
    }

    [Fact]
    public void MacroService_insert_applies_selected_value_set()
    {
        var project = Project.Create(ProjectId.New(), "M").Value;
        var page = new Page(PageId.New(), "1", "Şema");
        Assert.True(new AddPage(page).Execute(project).IsSuccess);

        var qFn = new Function(FunctionId.New(), FunctionKind.Protection, [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "2")], passThrough: true, ratedCurrentA: 2m);
        var qDev = new Device(DeviceId.New(), "-Q1", [qFn], part: new PartRef("Schneider", "GV2ME07"));
        var macro = new Macro(
            "Koruma",
            [qDev],
            [],
            [new MacroPlacement(qFn.Id, 0, 0)],
            [],
            "Koruma",
            [
                new MacroValueSet("10 A", new Dictionary<string, string>
                {
                    ["-Q1.ratedCurrentA"] = "10",
                    ["-Q1.orderCode"] = "GV2ME14",
                    ["-Q1.description"] = "10 A şalter",
                    ["-Q1.prop.Kutu"] = "IP20",
                }),
            ]);

        var planned = MacroService.Insert(project, macro, page.Id, 20, 30, valueSetName: "10 A");
        Assert.True(planned.IsSuccess, planned.Error?.Message);
        Assert.True(planned.Value.Execute(project).IsSuccess);

        var inserted = Assert.Single(project.Devices);
        Assert.Equal("GV2ME14", inserted.Part?.OrderCode);
        Assert.Equal("10 A şalter", inserted.Description);
        Assert.Equal(10m, Assert.Single(inserted.Functions).RatedCurrentA);
        Assert.Equal("IP20", inserted.Properties["Kutu"]);
    }
}
