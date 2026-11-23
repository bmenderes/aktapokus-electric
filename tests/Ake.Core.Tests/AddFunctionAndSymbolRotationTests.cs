using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Core.Tests;

public sealed class AddFunctionAndSymbolRotationTests
{
    [Fact]
    public void AddFunction_adds_function_to_existing_device_validates_and_undoes()
    {
        var coilPt1 = new ConnectionPoint(ConnectionPointId.New(), "A1");
        var coilPt2 = new ConnectionPoint(ConnectionPointId.New(), "A2");
        var coil = new Function(FunctionId.New(), FunctionKind.Coil, [coilPt1, coilPt2]);
        var device = new Device(DeviceId.New(), "K1", [coil]);
        var project = Project.Create(ProjectId.New(), "P", devices: [device]).Value;
        var before = ProjectJson.Serialize(project);

        var contactPt1 = new ConnectionPoint(ConnectionPointId.New(), "13");
        var contactPt2 = new ConnectionPoint(ConnectionPointId.New(), "14");
        var contact = new Function(FunctionId.New(), FunctionKind.NoContact, [contactPt1, contactPt2]);

        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new AddFunction(device.Id, contact), ChangeSource.User, "test").IsSuccess);
        Assert.Equal(2, device.Functions.Count);
        Assert.NotNull(project.Locate(contactPt1.Id));
        Assert.True(project.CheckIntegrity().IsSuccess);

        // Yinelenen kimlik reddedilir
        var duplicate = new Function(FunctionId.New(), FunctionKind.NoContact, [new ConnectionPoint(contactPt1.Id, "23")]);
        Assert.Equal("id.tekrar", new AddFunction(device.Id, duplicate).Execute(project).Error!.Code);

        // Olmayan cihaz reddedilir
        var orphan = new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")]);
        Assert.Equal("device.yok", new AddFunction(DeviceId.New(), orphan).Execute(project).Error!.Code);

        // Uyumsuz sembol reddedilir
        var badSym = new Function(
            FunctionId.New(),
            FunctionKind.Generic,
            [new ConnectionPoint(ConnectionPointId.New(), "1")],
            symbolId: SymbolId.New());
        Assert.Equal("sembol.yok", new AddFunction(device.Id, badSym).Execute(project).Error!.Code);

        // Undo modeli bayt düzeyinde geri getirir
        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal(before, ProjectJson.Serialize(project));
    }

    [Fact]
    public void SymbolText_rotation_validates_finite_and_preserves_json_round_trip()
    {
        var unrotated = new SymbolDefinition(
            SymbolId.New(),
            "Düz",
            [new SymbolText(1, 2, "AB")],
            [new SymbolPin(0, 0, 0, -1, "1")]);
        var jsonZero = SymbolJson.Serialize(unrotated);
        Assert.DoesNotContain("rotationDeg", jsonZero, StringComparison.OrdinalIgnoreCase);

        var rotated = new SymbolDefinition(
            SymbolId.New(),
            "Dik",
            [new SymbolText(1, 2, "DI1", 2.0, TextAlign.Left, -90)],
            [new SymbolPin(0, 0, 0, -1, "1")]);
        var jsonRot = SymbolJson.Serialize(rotated);
        Assert.Contains("-90", jsonRot, StringComparison.Ordinal);

        var loaded = SymbolJson.Deserialize(jsonRot);
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        var loadedText = Assert.IsType<SymbolText>(Assert.Single(loaded.Value.Shapes));
        Assert.Equal(-90, loadedText.RotationDeg);

        var invalid = rotated with { Shapes = [new SymbolText(1, 2, "X", 2.0, TextAlign.Left, double.NaN)] };
        Assert.NotNull(invalid.Check());
    }
}
