using Ake.Core.Model;

namespace Ake.Render.Tests;

public class SchematicRenderTests
{
    private static readonly DateOnly Today = new(2026, 11, 15);

    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private static Function Fn(FunctionKind kind, params string[] points) =>
        new(FunctionId.New(), kind, points.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)));

    private static ConnectionPointId Point(Device device, string name) =>
        device.Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    private static Placement Place(Page page, Device device, int function, double x, double y, int rotation = 0) =>
        new(PlacementId.New(), page.Id, device.Functions[function].Id, PlacementRole.Main, x, y, rotation);

    private static Scene Render(Project project, int page = 0) =>
        SheetRenderer.Render(project, project.Pages[page], Today).Value;

    // ---- semboller

    [Theory]
    [InlineData(FunctionKind.NoContact, 2)]
    [InlineData(FunctionKind.NcContact, 2)]
    [InlineData(FunctionKind.PowerContact, 2)]
    [InlineData(FunctionKind.Protection, 2)]
    [InlineData(FunctionKind.Coil, 2)]
    [InlineData(FunctionKind.Terminal, 2)]
    public void Two_pin_symbols_run_from_the_first_point_at_the_top_to_the_second_at_the_bottom(FunctionKind kind, int points)
    {
        var symbol = SymbolLibrary.For(Fn(kind, [.. Enumerable.Range(1, points).Select(i => i.ToString())]));

        Assert.Equal([new Pin(0, 0, 0, -1), new Pin(0, Symbol.Length, 0, 1)], symbol.Pins);
        Assert.NotEmpty(symbol.Items);
        Assert.True(symbol.MinX < 0 && symbol.MaxX > 0);
    }

    [Fact]
    public void Motor_puts_its_phases_on_top_at_pole_pitch()
    {
        var symbol = SymbolLibrary.For(Fn(FunctionKind.Motor, "U1", "V1", "W1", "PE"));

        Assert.Equal([0, 10, 20, 30], symbol.Pins.Select(pin => pin.X));
        Assert.All(symbol.Pins, pin => Assert.Equal((0.0, -1.0), (pin.Y, pin.DirectionY)));
        Assert.Contains(symbol.Items, item => item is CircleItem);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(2, 1, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(8, 4, 4)]
    public void Box_symbol_splits_its_points_between_top_and_bottom(int points, int top, int bottom)
    {
        var symbol = SymbolLibrary.For(Fn(FunctionKind.Generic, [.. Enumerable.Range(1, points).Select(i => i.ToString())]));

        Assert.Equal(points, symbol.Pins.Count);
        Assert.Equal(top, symbol.Pins.Count(pin => pin.DirectionY < 0));
        Assert.Equal(bottom, symbol.Pins.Count(pin => pin.DirectionY > 0));
    }

    [Fact]
    public void A_two_pin_kind_with_an_unusual_number_of_points_falls_back_to_the_box()
    {
        var symbol = SymbolLibrary.For(Fn(FunctionKind.Coil, "A1", "A2", "A3"));

        Assert.Equal(3, symbol.Pins.Count);
        Assert.Contains(symbol.Items, item => item is RectItem);
    }

    // ---- sayfa içeriği

    [Fact]
    public void Wire_between_two_symbols_on_the_same_page_is_drawn_with_its_number_and_colour()
    {
        var breaker = new Device(DeviceId.New(), "F1", [Fn(FunctionKind.Protection, "1", "2")]);
        var contactor = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.PowerContact, "1", "2")]);
        var page = new Page(PageId.New(), "1", "Güç");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [breaker, contactor],
            connections:
            [
                new Connection(
                    ConnectionId.New(), Point(breaker, "2"), Point(contactor, "1"), new Wire(WireId.New(), "10", 2.5m, "BU")),
            ],
            pages: [page],
            placements: [Place(page, breaker, 0, 50, 20), Place(page, contactor, 0, 50, 60)],
            sheetTemplates: [Template]).Value;

        var scene = Render(project);

        // Çizim alanı (15, 15)'te başlar: sigortanın alt ucu (65, 50), kontaktörün üst ucu (65, 75).
        var wire = scene.Items.OfType<LineItem>().Where(line => line.Color == "#1f4fd8").ToList();
        Assert.NotEmpty(wire);
        Assert.All(wire, line => Assert.Equal((65.0, 65.0), (line.X1, line.X2)));
        Assert.Equal(50, wire.Min(line => Math.Min(line.Y1, line.Y2)));
        Assert.Equal(75, wire.Max(line => Math.Max(line.Y1, line.Y2)));
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "10");
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "-F1");
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "-K1");
    }

    [Fact]
    public void Connection_to_another_page_becomes_a_cross_reference_with_page_and_column()
    {
        var supply = new Device(DeviceId.New(), "T1", [Fn(FunctionKind.Generic, "+")]);
        var coil = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var first = new Page(PageId.New(), "1", "Besleme");
        var second = new Page(PageId.New(), "7", "Kontrol");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [supply, coil],
            connections: [new Connection(ConnectionId.New(), Point(supply, "+"), Point(coil, "A1"))],
            potentialSources: [new PotentialSource(PotentialSourceId.New(), "24VDC", PotentialKind.DcPositive, Point(supply, "+"))],
            pages: [first, second],
            placements: [Place(first, supply, 0, 100, 40), Place(second, coil, 0, 250, 40)],
            sheetTemplates: [Template]).Value;

        var onFirst = Render(project, 0).Items.OfType<TextItem>().Single(text => text.Text.Contains(" / "));
        var onSecond = Render(project, 1).Items.OfType<TextItem>().Single(text => text.Text.Contains(" / "));

        // Kolon genişliği 39 mm: x=250 altıncı kolon (6), x=100 ikinci kolon (2).
        Assert.Equal("24VDC / 7.6", onFirst.Text);
        Assert.Equal("24VDC / 1.2", onSecond.Text);
        Assert.Equal(-90, onFirst.RotationDeg);
    }

    [Fact]
    public void Unnamed_potential_is_referenced_by_the_tag_of_the_far_end()
    {
        var button = new Device(DeviceId.New(), "S1", [Fn(FunctionKind.NoContact, "13", "14")]);
        var input = new Device(DeviceId.New(), "A11", [Fn(FunctionKind.PlcChannel, "1")]);
        var first = new Page(PageId.New(), "1", "Butonlar");
        var second = new Page(PageId.New(), "2", "PLC");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [button, input],
            connections: [new Connection(ConnectionId.New(), Point(button, "14"), Point(input, "1"))],
            pages: [first, second],
            placements: [Place(first, button, 0, 30, 40), Place(second, input, 0, 30, 40)],
            sheetTemplates: [Template]).Value;

        var note = Render(project, 0).Items.OfType<TextItem>().Single(text => text.Text.Contains(" / "));

        Assert.Equal("-A11:1 / 2.0", note.Text);
    }

    [Fact]
    public void Connection_to_a_function_that_is_on_no_page_draws_nothing()
    {
        var button = new Device(DeviceId.New(), "S1", [Fn(FunctionKind.NoContact, "13", "14")]);
        var lamp = new Device(DeviceId.New(), "H1", [Fn(FunctionKind.Generic, "X1")]);
        var page = new Page(PageId.New(), "1", "Butonlar");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [button, lamp],
            connections: [new Connection(ConnectionId.New(), Point(button, "14"), Point(lamp, "X1"))],
            pages: [page],
            placements: [Place(page, button, 0, 30, 40)],
            sheetTemplates: [Template]).Value;

        Assert.DoesNotContain(Render(project).Items.OfType<TextItem>(), text => text.Text.Contains(" / "));
    }

    [Fact]
    public void Adjacent_poles_of_one_device_share_a_single_tag()
    {
        var contactor = new Device(
            DeviceId.New(),
            "K1",
            [Fn(FunctionKind.PowerContact, "1", "2"), Fn(FunctionKind.PowerContact, "3", "4"), Fn(FunctionKind.PowerContact, "5", "6")]);
        var page = new Page(PageId.New(), "1", "Güç");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [contactor],
            pages: [page],
            placements: [Place(page, contactor, 0, 50, 20), Place(page, contactor, 1, 60, 20), Place(page, contactor, 2, 70, 20)],
            sheetTemplates: [Template]).Value;

        Assert.Single(Render(project).Items.OfType<TextItem>(), text => text.Text == "-K1");
    }

    [Fact]
    public void Rotated_symbol_keeps_its_first_pin_at_the_placement_point()
    {
        var contact = new Device(DeviceId.New(), "S1", [Fn(FunctionKind.NoContact, "13", "14")]);
        var page = new Page(PageId.New(), "1", "Sayfa");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [contact],
            pages: [page],
            placements: [Place(page, contact, 0, 100, 50, rotation: 90)],
            sheetTemplates: [Template]).Value;

        var lines = Render(project).Items.OfType<LineItem>().ToList();

        // 90 derece: sembol sola doğru yatar; ilk uç (115, 65), ikinci uç (100, 65).
        Assert.Contains(lines, line => (line.X1, line.Y1) == (115.0, 65.0));
        Assert.Contains(lines, line => (line.X2, line.Y2) == (100.0, 65.0));
    }

    [Fact]
    public void Wire_goes_around_a_symbol_standing_between_its_two_ends()
    {
        var top = new Device(DeviceId.New(), "F1", [Fn(FunctionKind.Protection, "1", "2")]);
        var middle = new Device(DeviceId.New(), "K9", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var bottom = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.PowerContact, "1", "2")]);
        var page = new Page(PageId.New(), "1", "Güç");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [top, middle, bottom],
            connections: [new Connection(ConnectionId.New(), Point(top, "2"), Point(bottom, "1"), new Wire(WireId.New(), "1", 2.5m, "RD"))],
            pages: [page],
            placements: [Place(page, top, 0, 50, 20), Place(page, middle, 0, 50, 60), Place(page, bottom, 0, 50, 110)],
            sheetTemplates: [Template]).Value;

        var wire = Render(project).Items.OfType<LineItem>().Where(line => line.Color == "#d62828").ToList();

        // Bobin (65, 75)-(65, 90) arasında durur; kutusu x 61..69. Tel o dikdörtgenin içinden geçmemeli.
        Assert.NotEmpty(wire);
        Assert.DoesNotContain(wire, line =>
            Math.Max(line.X1, line.X2) > 61 && Math.Min(line.X1, line.X2) < 69
            && Math.Max(line.Y1, line.Y2) > 75 && Math.Min(line.Y1, line.Y2) < 90);
        Assert.Contains(wire, line => Math.Abs(line.X1 - line.X2) > 0.001);
    }

    [Fact]
    public void Three_wires_meeting_on_one_net_get_a_junction_dot()
    {
        var supply = new Device(DeviceId.New(), "T1", [Fn(FunctionKind.Generic, "+")]);
        var first = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var second = new Device(DeviceId.New(), "K2", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var page = new Page(PageId.New(), "1", "Kontrol");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [supply, first, second],
            connections:
            [
                new Connection(ConnectionId.New(), Point(supply, "+"), Point(first, "A1")),
                new Connection(ConnectionId.New(), Point(supply, "+"), Point(second, "A1")),
            ],
            pages: [page],
            placements: [Place(page, supply, 0, 50, 70, 180), Place(page, first, 0, 50, 120), Place(page, second, 0, 90, 120)],
            sheetTemplates: [Template]).Value;

        var dots = Render(project).Items.OfType<CircleItem>().Where(circle => circle.Filled).ToList();

        Assert.Single(dots);
    }

    [Fact]
    public void Channels_of_one_plc_card_are_drawn_inside_a_single_box()
    {
        var card = new Device(
            DeviceId.New(), "A11", [.. Enumerable.Range(1, 4).Select(i => Fn(FunctionKind.PlcChannel, i.ToString()))]);
        var page = new Page(PageId.New(), "1", "PLC");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [card],
            pages: [page],
            placements: [.. Enumerable.Range(0, 4).Select(i => Place(page, card, i, 50 + (i * 10), 40))],
            sheetTemplates: [Template]).Value;

        var scene = Render(project);

        // Çerçeve dışında tek dikdörtgen: kartın gövdesi, ilk kanaldan son kanala.
        var body = Assert.Single(scene.Items.OfType<RectItem>(), rect => rect.StrokeMm < 0.5);
        Assert.Equal((61.0, 38.0), (body.X, body.Width));
        Assert.Single(scene.Items.OfType<TextItem>(), text => text.Text == "-A11");
    }

    [Fact]
    public void Several_connections_to_the_same_potential_share_one_note()
    {
        var supply = new Device(DeviceId.New(), "T1", [Fn(FunctionKind.Generic, "+")]);
        var a = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var b = new Device(DeviceId.New(), "K2", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var first = new Page(PageId.New(), "1", "Besleme");
        var second = new Page(PageId.New(), "2", "Kontrol");
        var third = new Page(PageId.New(), "3", "Kontrol 2");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [supply, a, b],
            connections:
            [
                new Connection(ConnectionId.New(), Point(supply, "+"), Point(a, "A1")),
                new Connection(ConnectionId.New(), Point(supply, "+"), Point(b, "A1")),
            ],
            potentialSources: [new PotentialSource(PotentialSourceId.New(), "24VDC", PotentialKind.DcPositive, Point(supply, "+"))],
            pages: [first, second, third],
            placements: [Place(first, supply, 0, 100, 40), Place(second, a, 0, 50, 40), Place(third, b, 0, 300, 40)],
            sheetTemplates: [Template]).Value;

        var note = Assert.Single(Render(project, 0).Items.OfType<TextItem>(), text => text.Text.Contains(" / "));

        Assert.Equal("24VDC / 2.1, 3.7", note.Text);
    }

    [Fact]
    public void Connection_through_a_cable_core_is_labelled_with_the_core()
    {
        var terminal = new Device(DeviceId.New(), "X1", [Fn(FunctionKind.Terminal, "1.a", "1.b")]);
        var motor = new Device(DeviceId.New(), "M1", [Fn(FunctionKind.Motor, "U1", "V1", "W1")]);
        var core = new CableCore(CableCoreId.New(), "2");
        var page = new Page(PageId.New(), "1", "Motor");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [terminal, motor],
            connections: [new Connection(ConnectionId.New(), Point(terminal, "1.b"), Point(motor, "U1"), coreId: core.Id)],
            cables: [new Cable(CableId.New(), "W7", [core])],
            pages: [page],
            placements: [Place(page, terminal, 0, 50, 30), Place(page, motor, 0, 50, 90)],
            sheetTemplates: [Template]).Value;

        Assert.Contains(Render(project).Items.OfType<TextItem>(), text => text.Text == "-W7:2");
    }

    [Fact]
    public void Page_without_placements_has_only_the_frame()
    {
        var page = new Page(PageId.New(), "1", "Boş");
        var project = Project.Create(ProjectId.New(), "p", pages: [page], sheetTemplates: [Template]).Value;

        Assert.DoesNotContain(Render(project).Items, item => item is CircleItem);
        Assert.All(Render(project).Items.OfType<LineItem>(), line => Assert.Null(line.Color));
    }

    [Fact]
    public void Junction_is_drawn_as_a_filled_dot_without_tag_or_pin_name()
    {
        var project = Ake.Serialization.ProjectFile.Load(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json")).Value;
        var wire = project.Connections.First(c => c.Wire?.Number is not null && c.CoreId is null && project.Locate(c.From) is not null);
        var page = project.Pages[0];
        var split = Ake.Core.Commands.Junctions.Split(project, wire.Id, page.Id, 50, 70).Value;
        Assert.True(new Ake.Core.Commands.CommandStack(project).Execute(split.Command, Ake.Core.Commands.ChangeSource.User, "test").IsSuccess);

        var scene = SheetRenderer.Render(project, page, new DateOnly(2026, 11, 15)).Value;

        // Yerleşim koordinatı çizim alanına göredir (alan 15, 15'te başlar): 50,70 → sayfada 65,85.
        Assert.Contains(scene.Items.OfType<CircleItem>(), circle => circle is { X: 65, Y: 85, Filled: true });
        Assert.DoesNotContain(scene.Items.OfType<TextItem>(), text => text.Text == "-J1");
        Assert.Contains(SheetRenderer.PinsOf(project, page), pin => pin.Point == split.Point && pin is { X: 65, Y: 85 });
    }

    [Fact]
    public void A_contact_type_on_a_point_is_written_next_to_the_pin_name()
    {
        var project = Ake.Serialization.ProjectFile.Load(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json")).Value;
        var point = project.Devices.Single(device => device.ProductTag == "K1").Functions
            .SelectMany(function => function.ConnectionPoints).First(p => p.Name == "A1");
        var page = project.Pages.First(p => SheetRenderer.PinsOf(project, p).Any(pin => pin.Point == point.Id));
        Assert.True(new Ake.Core.Commands.SetPointAttributes(point.Id, new Ake.Core.Model.PointAttributes(Contact: Ake.Core.Model.ContactType.NormallyClosed)).Execute(project).IsSuccess);

        var scene = SheetRenderer.Render(project, page, new DateOnly(2026, 11, 15)).Value;

        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text == "A1 NC");
    }

    [Fact]
    public void A_connection_kind_colors_and_styles_its_wires_and_adds_a_legend_only_when_used()
    {
        var project = Ake.Serialization.ProjectFile.Load(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json")).Value;
        var page = project.Pages[0];
        var before = SheetRenderer.Render(project, page, new DateOnly(2026, 11, 15)).Value;
        Assert.DoesNotContain(before.Items.OfType<TextItem>(), text => text.Text == "Bağlantı türleri");
        Assert.DoesNotContain(before.Items.OfType<LineItem>(), line => line.Style != Ake.Core.Model.LineStyle.Solid);

        var kind = new Ake.Core.Model.ConnectionKind(
            Ake.Core.Model.ConnectionKindId.New(), "DriveCLiQ", "#808080", Ake.Core.Model.LineStyle.Dashed, Ake.Core.Model.PinKind.Communication);
        Assert.True(new Ake.Core.Commands.PutConnectionKind(kind).Execute(project).IsSuccess);
        var pins = SheetRenderer.PinsOf(project, page).Select(pin => pin.Point).ToHashSet();
        var onPage = project.Connections.First(connection => pins.Contains(connection.From) && pins.Contains(connection.To));
        Assert.True(new Ake.Core.Commands.SetConnectionKind(onPage.Id, kind.Id).Execute(project).IsSuccess);

        var after = SheetRenderer.Render(project, page, new DateOnly(2026, 11, 15)).Value;

        Assert.Contains(after.Items.OfType<LineItem>(), line => line is { Color: "#808080", Style: Ake.Core.Model.LineStyle.Dashed });
        Assert.Contains(after.Items.OfType<TextItem>(), text => text.Text == "Bağlantı türleri");
        Assert.Contains(after.Items.OfType<TextItem>(), text => text.Text == "DriveCLiQ");
        Assert.Contains("stroke-dasharray", SvgExporter.Write(after), StringComparison.Ordinal);
        Assert.DoesNotContain("stroke-dasharray", SvgExporter.Write(before), StringComparison.Ordinal);
    }

    [Fact]
    public void Terminal_symbols_follow_the_role_and_the_number_of_ways()
    {
        var plain = Ake.Core.Commands.AddTerminal.Create("1");
        var pe = Ake.Core.Commands.AddTerminal.Create("PE", role: Ake.Core.Model.TerminalRole.ProtectiveEarth);
        var multi = Ake.Core.Commands.AddTerminal.Create("2", ways: 3);
        var cover = Ake.Core.Commands.AddTerminal.Create("SL", role: Ake.Core.Model.TerminalRole.EndCover);

        var plainSymbol = SymbolLibrary.For(plain);
        var peSymbol = SymbolLibrary.For(pe);
        var multiSymbol = SymbolLibrary.For(multi);
        var coverSymbol = SymbolLibrary.For(cover);

        Assert.Equal(2, plainSymbol.Pins.Count);
        Assert.DoesNotContain(plainSymbol.Items.OfType<TextItem>(), text => text.Text == "PE");
        Assert.Contains(peSymbol.Items.OfType<TextItem>(), text => text.Text == "PE");
        Assert.Equal(6, multiSymbol.Pins.Count);
        Assert.Equal([0.0, 5.0, 10.0], multiSymbol.Pins.Take(3).Select(pin => pin.X));
        Assert.All(multiSymbol.Pins.Take(3), pin => Assert.Equal(-1, pin.DirectionY));
        Assert.All(multiSymbol.Pins.Skip(3), pin => Assert.Equal(1, pin.DirectionY));
        Assert.Empty(coverSymbol.Pins);
        Assert.NotEmpty(coverSymbol.Items);
    }
}
