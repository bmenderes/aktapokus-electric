using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Render.Tests;

public class UserSymbolRenderTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private static SymbolDefinition ExampleLamp()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var result = SymbolJson.Load(Path.Combine(directory!.FullName, "examples", "symbols", "sinyal-lambasi.sembol.json"));
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static (Project Project, Device Lamp) LampOnAPage(SymbolDefinition? symbol)
    {
        var lamp = new Device(
            DeviceId.New(),
            "H1",
            [
                new Function(
                    FunctionId.New(),
                    FunctionKind.Generic,
                    [new ConnectionPoint(ConnectionPointId.New(), "X1"), new ConnectionPoint(ConnectionPointId.New(), "X2")],
                    symbolId: symbol?.Id),
            ]);
        var page = new Page(PageId.New(), "1", "Sayfa");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [lamp],
            pages: [page],
            placements: [new Placement(PlacementId.New(), page.Id, lamp.Functions[0].Id, PlacementRole.Main, 50, 40)],
            sheetTemplates: [Template],
            symbols: symbol is null ? null : [symbol]).Value;
        return (project, lamp);
    }

    [Fact]
    public void Example_symbol_file_loads_and_round_trips()
    {
        var lamp = ExampleLamp();

        Assert.Equal("Sinyal lambası", lamp.Name);
        Assert.Equal(2, lamp.Pins.Count);
        Assert.Equal("Apache-2.0", lamp.License);
        Assert.Equal(SymbolJson.Serialize(lamp), SymbolJson.Serialize(SymbolJson.Deserialize(SymbolJson.Serialize(lamp)).Value));
    }

    [Fact]
    public void Function_with_a_user_symbol_is_drawn_with_that_symbol()
    {
        var (withSymbol, _) = LampOnAPage(ExampleLamp());
        var (builtIn, _) = LampOnAPage(null);

        var custom = SheetRenderer.Render(withSymbol, withSymbol.Pages[0], Today).Value;
        var plain = SheetRenderer.Render(builtIn, builtIn.Pages[0], Today).Value;

        // Çizim alanı (15, 15)'te başlar; lambanın dairesi (65, 62.5) merkezli, 4 mm yarıçaplı.
        Assert.Contains(custom.Items.OfType<CircleItem>(), circle => (circle.X, circle.Y, circle.Radius) == (65, 62.5, 4));
        Assert.DoesNotContain(plain.Items.OfType<CircleItem>(), circle => circle.Radius == 4);
    }

    [Fact]
    public void User_symbol_box_covers_every_shape_and_pin()
    {
        var symbol = SymbolLibrary.FromDefinition(new SymbolDefinition(
            SymbolId.New(),
            "Geniş",
            [new SymbolRect(-6, -3, 12, 4), new SymbolText(8, 2, "AB", 2)],
            [new SymbolPin(0, -5, 0, -1), new SymbolPin(0, 6, 0, 1)]));

        Assert.Equal(-6, symbol.MinX);
        Assert.True(symbol.MaxX >= 8 + (2 * 2 * 0.6) - 0.001);
        Assert.Equal(-5, symbol.MinY);
        Assert.Equal(6, symbol.Height);
    }

    [Fact]
    public void Very_thin_symbol_still_gets_a_box_that_can_be_clicked()
    {
        var symbol = SymbolLibrary.FromDefinition(new SymbolDefinition(
            SymbolId.New(), "Tel", [new SymbolLine(0, 0, 0, 10)], [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 10, 0, 1)]));

        Assert.Equal(2, symbol.MaxX - symbol.MinX, 6);
    }

    [Fact]
    public void Project_with_symbols_round_trips_through_the_project_file()
    {
        var (project, _) = LampOnAPage(ExampleLamp());

        var json = ProjectJson.Serialize(project);
        var reloaded = ProjectJson.Deserialize(json);

        Assert.True(reloaded.IsSuccess, reloaded.Error?.Message);
        Assert.Equal(json, ProjectJson.Serialize(reloaded.Value));
        Assert.Contains("\"type\": \"circle\"", json);
        Assert.Contains("\"symbolId\":", json);
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("""{ "id": "0193c000-0000-7000-8000-00000000c001", "name": "x", "shapes": [ { "type": "yildiz" } ], "pins": [] }""")]
    public void Malformed_symbol_files_are_rejected(string json)
    {
        Assert.Equal("sembol.json_gecersiz", SymbolJson.Deserialize(json).Error!.Code);
    }

    [Fact]
    public void Symbol_file_that_parses_but_cannot_be_drawn_is_rejected()
    {
        var json = """{ "id": "0193c000-0000-7000-8000-00000000c001", "name": "x", "shapes": [], "pins": [] }""";

        Assert.Equal("sembol.gecersiz", SymbolJson.Deserialize(json).Error!.Code);
    }
}
