using Ake.Core.Commands;
using Ake.Core.Macros;
using Ake.Core.Model;

namespace Ake.Serialization.Tests;

public sealed class CustomPropertySerializationTests
{
    [Fact]
    public void Device_properties_round_trip_and_omit_when_empty()
    {
        var project = Project.Create(ProjectId.New(), "Seri Test").Value;
        var fn1 = new Function(FunctionId.New(), FunctionKind.Motor, [new ConnectionPoint(ConnectionPointId.New(), "U1")]);
        var d1 = new Device(
            DeviceId.New(),
            "-M1",
            [fn1],
            properties: new Dictionary<string, string> { ["Güç"] = "1.5 kW", ["Oran"] = "15" });
        var fn2 = new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")]);
        var d2 = new Device(DeviceId.New(), "-K1", [fn2]);

        Assert.True(new AddDevice(d1).Execute(project).IsSuccess);
        Assert.True(new AddDevice(d2).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        Assert.Contains("\"Güç\": \"1.5 kW\"", json, StringComparison.Ordinal);
        // Boş özel özellik sözlüğü serileştirmede yer kaplamamalı (geriye dönük uyumluluk).
        Assert.Equal(1, json.Split("\"properties\":").Length - 2); // proje root properties + yalnızca d1

        var back = ProjectJson.Deserialize(json);
        Assert.True(back.IsSuccess, back.Error?.Message);
        var loadedM1 = back.Value.Devices.First(d => d.ProductTag == "-M1");
        Assert.Equal("1.5 kW", loadedM1.Properties["Güç"]);
        Assert.Equal("15", loadedM1.Properties["Oran"]);
        Assert.Empty(back.Value.Devices.First(d => d.ProductTag == "-K1").Properties);
    }

    [Fact]
    public void SetDeviceProperty_patch_op_executes_and_removes()
    {
        var project = Project.Create(ProjectId.New(), "Patch Test").Value;
        var dev = new Device(
            DeviceId.New(),
            "K1",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")])]);
        Assert.True(new AddDevice(dev).Execute(project).IsSuccess);

        const string patchJson = """
            {
              "patchVersion": 1,
              "description": "Özel özellik ekle",
              "commands": [
                { "op": "setDeviceProperty", "device": "-K1", "key": "Tip", "value": "Emniyet" }
              ]
            }
            """;
        var parsed = PatchJson.Deserialize(patchJson);
        Assert.True(parsed.IsSuccess, parsed.Error?.Message);

        var res = parsed.Value.ToCommand().Execute(project);
        Assert.True(res.IsSuccess, res.Error?.Message);
        Assert.Equal("Emniyet", dev.Properties["Tip"]);
    }

    [Fact]
    public void Macro_value_sets_round_trip_through_macro_json()
    {
        var fn = new Function(FunctionId.New(), FunctionKind.Protection, [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "2")], passThrough: true);
        var dev = new Device(DeviceId.New(), "-Q1", [fn]);
        var macro = new Macro(
            "Besleme",
            [dev],
            [],
            [new MacroPlacement(fn.Id, 0, 0)],
            [],
            "Değer kümeli makro",
            [new MacroValueSet("16 A", new Dictionary<string, string> { ["-Q1.ratedCurrentA"] = "16" })]);

        var json = MacroJson.Serialize(macro);
        var loaded = MacroJson.Deserialize(json);
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        var set = Assert.Single(loaded.Value.ValueSets!);
        Assert.Equal("16 A", set.Name);
        Assert.Equal("16", set.Values["-Q1.ratedCurrentA"]);
    }
}
