using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

public class SymbolCommandTests
{
    private static SymbolDefinition Lamp(SymbolId? id = null, int pins = 2) => new(
        id ?? SymbolId.New(),
        "Lamba",
        [new SymbolCircle(0, 7.5, 4), new SymbolLine(0, 0, 0, 3.5), new SymbolLine(0, 11.5, 0, 15)],
        [.. new[] { new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1), new SymbolPin(5, 7.5, 1, 0) }.Take(pins)]);

    [Fact]
    public void PutSymbol_adds_and_replaces_in_place()
    {
        var project = TestData.RefMachine();
        var lamp = Lamp();

        CommandAssert.RoundTrips(project, new PutSymbol(lamp));
        CommandAssert.RoundTrips(project, new PutSymbol(lamp with { Name = "Lamba (yeni)" }));

        Assert.Equal("Lamba (yeni)", Assert.Single(project.Symbols).Name);
    }

    [Fact]
    public void AssignSymbol_changes_only_how_a_function_is_drawn()
    {
        var project = TestData.RefMachine();
        var lamp = Lamp();
        new PutSymbol(lamp).Execute(project);
        var coil = project.Function("K1", "A1");

        CommandAssert.RoundTrips(project, new AssignSymbol(coil.Id, lamp.Id));
        Assert.Equal(lamp.Id, coil.SymbolId);
        Assert.Equal(65, project.Connections.Count);

        CommandAssert.RoundTrips(project, new AssignSymbol(coil.Id, null));
        Assert.Null(coil.SymbolId);
    }

    [Fact]
    public void A_symbol_must_give_every_connection_point_a_pin()
    {
        var project = TestData.RefMachine();
        var threePins = Lamp(pins: 3);
        new PutSymbol(threePins).Execute(project);

        CommandAssert.Rejected(
            project, new AssignSymbol(project.Function("K1", "A1").Id, threePins.Id), "sembol.uc_uyumsuz");
        CommandAssert.Rejected(
            project, new AssignSymbol(project.Function("K1", "A1").Id, SymbolId.New()), "sembol.yok");
        CommandAssert.Rejected(project, new AssignSymbol(FunctionId.New(), threePins.Id), "function.yok");
    }

    [Fact]
    public void Replacing_a_symbol_in_use_with_one_that_no_longer_fits_is_rejected()
    {
        var project = TestData.RefMachine();
        var lamp = Lamp();
        new PutSymbol(lamp).Execute(project);
        new AssignSymbol(project.Function("K1", "A1").Id, lamp.Id).Execute(project);

        CommandAssert.Rejected(project, new PutSymbol(Lamp(lamp.Id, pins: 3)), "sembol.uc_uyumsuz");
        Assert.Equal(2, project.Symbols[0].Pins.Count);
    }

    [Fact]
    public void Invalid_symbols_are_rejected()
    {
        var project = TestData.RefMachine();

        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Name = " " }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Pins = [] }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Pins = [new SymbolPin(0, 0, 1, 1)] }), "sembol.gecersiz");
        CommandAssert.Rejected(
            project,
            new PutSymbol(Lamp() with { Pins = [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 0, 0, 1)] }),
            "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Shapes = [new SymbolCircle(0, 0, 0)] }), "sembol.gecersiz");
        CommandAssert.Rejected(
            project, new PutSymbol(Lamp() with { Shapes = [new SymbolLine(0, 0, double.NaN, 1)] }), "sembol.gecersiz");
        CommandAssert.Rejected(
            project, new PutSymbol(Lamp(new SymbolId(project.Device("K1").Id.Value))), "id.tekrar");
    }

    [Fact]
    public void Project_with_a_function_pointing_to_a_missing_symbol_is_rejected()
    {
        var device = new Device(
            DeviceId.New(),
            "H1",
            [new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "X1")], symbolId: SymbolId.New())]);

        Assert.Equal("sembol.yok", Project.Create(ProjectId.New(), "p", devices: [device]).Error!.Code);
    }

    private static readonly PointAttributes NcSensor = new(
        PinKind.Signal, ConnectorType.M12, ContactType.NormallyClosed, "A-kod 4 pin");

    [Fact]
    public void SetPointAttributes_changes_kind_connector_and_contact_and_undoes()
    {
        var project = TestData.RefMachine();
        var point = project.Function("K1", "A1").ConnectionPoints[0];
        Assert.True(point.Attributes.IsEmpty);

        CommandAssert.RoundTrips(project, new SetPointAttributes(point.Id, NcSensor with { ConnectorCode = "  A-kod 4 pin " }));

        Assert.Equal(NcSensor, point.Attributes);
    }

    [Fact]
    public void SetPointAttributes_rejects_a_missing_point_an_unknown_value_and_a_long_code()
    {
        var project = TestData.RefMachine();
        var point = project.Function("K1", "A1").ConnectionPoints[0].Id;

        CommandAssert.Rejected(project, new SetPointAttributes(ConnectionPointId.New(), NcSensor), "point.yok");
        CommandAssert.Rejected(project, new SetPointAttributes(point, new PointAttributes((PinKind)99)), "point.ozellik_gecersiz");
        CommandAssert.Rejected(project, new SetPointAttributes(point, new PointAttributes(ConnectorCode: new string('x', 41))), "point.ozellik_gecersiz");
    }

    [Fact]
    public void AssignSymbol_hands_pin_attributes_to_points_that_have_none_and_undo_gives_them_back()
    {
        var project = TestData.RefMachine();
        var symbol = Lamp() with
        {
            Pins = [new SymbolPin(0, 0, 0, -1, "A1", NcSensor), new SymbolPin(0, 15, 0, 1, "A2")],
        };
        new PutSymbol(symbol).Execute(project);
        var coil = project.Function("K1", "A1");
        var already = new PointAttributes(PinKind.Power);
        new SetPointAttributes(coil.ConnectionPoints[1].Id, already).Execute(project);

        CommandAssert.RoundTrips(project, new AssignSymbol(coil.Id, symbol.Id));

        Assert.Equal(NcSensor, coil.ConnectionPoints[0].Attributes);
        Assert.Equal(["A1", "A2"], coil.ConnectionPoints.Select(point => point.Name));
        Assert.Equal(already, coil.ConnectionPoints[1].Attributes);
    }

    [Fact]
    public void A_symbol_with_a_duplicate_pin_name_a_bad_tag_letter_or_bad_attributes_is_invalid()
    {
        var project = TestData.RefMachine();
        var pins = new[] { new SymbolPin(0, 0, 0, -1, "A"), new SymbolPin(0, 15, 0, 1, "A") };

        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Pins = pins }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Pins = [new SymbolPin(0, 0, 0, -1, "a b"), new SymbolPin(0, 15, 0, 1)] }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { TagLetter = "kk" }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { TagLetter = "ABCD" }), "sembol.gecersiz");
        CommandAssert.Rejected(project, new PutSymbol(Lamp() with { Pins = [new SymbolPin(0, 0, 0, -1, null, new PointAttributes(Contact: (ContactType)9)), new SymbolPin(0, 15, 0, 1)] }), "sembol.gecersiz");
        CommandAssert.RoundTrips(project, new PutSymbol(Lamp() with { TagLetter = "BQ", DeviceName = "Endüktif sensör" }));
    }
}
