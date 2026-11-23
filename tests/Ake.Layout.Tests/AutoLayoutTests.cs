using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Layout.Tests;

public class AutoLayoutTests
{
    private static readonly SheetTemplate Template = new(
        SheetTemplateId.New(), "A3", 420, 297, 10, 10, 6, 5, 20, "Arial", []);

    private static Function Fn(FunctionKind kind, params string[] points) =>
        new(FunctionId.New(), kind, points.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)));

    private static ConnectionPointId Point(Device device, string name) =>
        device.Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    private static Connection Wire(Device from, string a, Device to, string b) =>
        new(ConnectionId.New(), Point(from, a), Point(to, b));

    /// <summary>Besleme, sigorta, kontaktör ve motor: hepsi aynı fonksiyon düğümünde.</summary>
    private static (Project Project, Device Supply, Device Breaker, Device Contactor, Device Motor) MotorCircuit()
    {
        var node = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1");
        Device Make(string tag, params Function[] functions) =>
            new(DeviceId.New(), tag, functions, functionNodeId: node.Id);

        var supply = Make("X0", Fn(FunctionKind.Generic, "L1", "L2", "L3"));
        var breaker = Make(
            "F1",
            Fn(FunctionKind.NoContact, "13", "14"),
            Fn(FunctionKind.Protection, "1", "2"),
            Fn(FunctionKind.Protection, "3", "4"),
            Fn(FunctionKind.Protection, "5", "6"));
        var contactor = Make(
            "K1",
            Fn(FunctionKind.Coil, "A1", "A2"),
            Fn(FunctionKind.PowerContact, "1", "2"),
            Fn(FunctionKind.PowerContact, "3", "4"),
            Fn(FunctionKind.PowerContact, "5", "6"));
        var motor = Make("M1", Fn(FunctionKind.Motor, "U1", "V1", "W1"));

        var project = Project.Create(
            ProjectId.New(),
            "test",
            structure: [node],
            devices: [motor, contactor, breaker, supply],
            connections:
            [
                Wire(supply, "L1", breaker, "1"), Wire(supply, "L2", breaker, "3"), Wire(supply, "L3", breaker, "5"),
                Wire(breaker, "2", contactor, "1"), Wire(breaker, "4", contactor, "3"), Wire(breaker, "6", contactor, "5"),
                Wire(contactor, "2", motor, "U1"), Wire(contactor, "4", motor, "V1"), Wire(contactor, "6", motor, "W1"),
            ],
            potentialSources: [new PotentialSource(PotentialSourceId.New(), "L1", PotentialKind.Phase, Point(supply, "L1"))],
            pages: [new Page(PageId.New(), "1", "Motor", node.Id)],
            sheetTemplates: [Template]).Value;
        return (project, supply, breaker, contactor, motor);
    }

    private static Placement PlacementOf(Project project, Device device, string point) =>
        project.Placements.Single(placement => placement.FunctionId
            == device.Functions.Single(function => function.ConnectionPoints.Any(c => c.Name == point)).Id);

    private static void Layout(Project project)
    {
        var result = new CommandStack(project).Execute(AutoLayout.Build(project), ChangeSource.User, "test");
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public void Every_function_gets_exactly_one_main_placement()
    {
        var (project, _, _, _, _) = MotorCircuit();

        Layout(project);

        Assert.True(project.CheckIntegrity().IsSuccess);
        Assert.Equal(project.Devices.Sum(device => device.Functions.Count), project.Placements.Count);
        Assert.All(project.Placements, placement => Assert.Equal(PlacementRole.Main, placement.Role));
    }

    [Fact]
    public void Devices_are_stacked_from_the_supply_down_to_the_load_whatever_the_model_order_is()
    {
        var (project, supply, breaker, contactor, motor) = MotorCircuit();

        Layout(project);

        var rows = new[]
        {
            PlacementOf(project, supply, "L1").Y,
            PlacementOf(project, breaker, "1").Y,
            PlacementOf(project, contactor, "1").Y,
            PlacementOf(project, motor, "U1").Y,
        };
        Assert.Equal(rows.Order(), rows);
        Assert.Equal(4, rows.Distinct().Count());
    }

    [Fact]
    public void Poles_of_stacked_devices_share_columns_so_wires_run_straight()
    {
        var (project, _, breaker, contactor, motor) = MotorCircuit();

        Layout(project);

        foreach (var pole in new[] { "1", "3", "5" })
        {
            Assert.Equal(PlacementOf(project, breaker, pole).X, PlacementOf(project, contactor, pole).X);
        }

        Assert.Equal(PlacementOf(project, contactor, "1").X, PlacementOf(project, motor, "U1").X);
        Assert.Equal(Symbol.Pitch, PlacementOf(project, contactor, "3").X - PlacementOf(project, contactor, "1").X);
        Assert.True(PlacementOf(project, contactor, "A1").X > PlacementOf(project, contactor, "5").X);
    }

    [Fact]
    public void Two_pin_symbol_is_turned_upside_down_when_its_first_point_feeds_the_row_below()
    {
        var node = new StructureNode(StructureNodeId.New(), Aspect.Function, "G1");
        var terminal = new Device(DeviceId.New(), "X0", [Fn(FunctionKind.Terminal, "1.a", "1.b")], functionNodeId: node.Id);
        var breaker = new Device(DeviceId.New(), "Q1", [Fn(FunctionKind.PowerContact, "1", "2")], functionNodeId: node.Id);
        var project = Project.Create(
            ProjectId.New(),
            "test",
            structure: [node],
            devices: [terminal, breaker],
            connections: [Wire(terminal, "1.a", breaker, "1")],
            potentialSources: [new PotentialSource(PotentialSourceId.New(), "L1", PotentialKind.Phase, Point(terminal, "1.b"))],
            pages: [new Page(PageId.New(), "1", "Güç", node.Id)],
            sheetTemplates: [Template]).Value;

        Layout(project);

        var flipped = PlacementOf(project, terminal, "1.a");
        var upright = PlacementOf(project, breaker, "1");
        Assert.Equal(180, flipped.RotationDeg);
        Assert.Equal(0, upright.RotationDeg);

        // Baş aşağı sembolün başlangıç noktası (ilk ucu) alt ucudur; sembol yine kendi satırında durur.
        Assert.Equal(48 - Symbol.Length, upright.Y - flipped.Y, 6);
    }

    [Fact]
    public void Through_terminal_is_drawn_on_the_page_of_the_devices_it_joins()
    {
        var power = new StructureNode(StructureNodeId.New(), Aspect.Function, "G1");
        var drive = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1");
        var strip = new Device(
            DeviceId.New(),
            "X1",
            [
                new Function(FunctionId.New(), FunctionKind.Terminal, [new ConnectionPoint(ConnectionPointId.New(), "1.a"), new ConnectionPoint(ConnectionPointId.New(), "1.b")], passThrough: true),
                new Function(FunctionId.New(), FunctionKind.Terminal, [new ConnectionPoint(ConnectionPointId.New(), "9.a"), new ConnectionPoint(ConnectionPointId.New(), "9.b")], passThrough: true),
            ],
            DeviceKind.TerminalStrip,
            functionNodeId: power.Id);
        var contactor = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.PowerContact, "1", "2")], functionNodeId: drive.Id);
        var motor = new Device(DeviceId.New(), "M1", [Fn(FunctionKind.Motor, "U1", "V1", "W1")], functionNodeId: drive.Id);
        var powerPage = new Page(PageId.New(), "1", "Güç", power.Id);
        var drivePage = new Page(PageId.New(), "2", "Motor", drive.Id);
        var project = Project.Create(
            ProjectId.New(),
            "test",
            structure: [power, drive],
            devices: [strip, contactor, motor],
            connections: [Wire(contactor, "2", strip, "1.a"), Wire(strip, "1.b", motor, "U1"), Wire(contactor, "2", strip, "9.a")],
            potentialSources: [new PotentialSource(PotentialSourceId.New(), "L1", PotentialKind.Phase, Point(contactor, "1"))],
            pages: [powerPage, drivePage],
            sheetTemplates: [Template]).Value;

        Layout(project);

        // İki ucu da motor devresine giden klemens o sayfada, kontaktör ile motorun arasında ve aynı kolonda.
        var through = PlacementOf(project, strip, "1.a");
        Assert.Equal(drivePage.Id, through.PageId);
        Assert.Equal(PlacementOf(project, contactor, "1").X, through.X);
        Assert.InRange(through.Y, PlacementOf(project, contactor, "1").Y + 1, PlacementOf(project, motor, "U1").Y - 1);

        // Yalnızca bir ucu bağlı klemens kendi cihazının sayfasında kalır.
        Assert.Equal(powerPage.Id, PlacementOf(project, strip, "9.a").PageId);
    }

    [Fact]
    public void New_symbols_are_placed_below_the_ones_already_on_the_page()
    {
        var (project, supply, breaker, contactor, motor) = MotorCircuit();
        Layout(project);
        var lowest = project.Placements.Max(placement => placement.Y);
        var lamp = new Device(DeviceId.New(), "H1", [Fn(FunctionKind.Generic, "X1", "X2")], functionNodeId: supply.FunctionNodeId);
        Assert.True(new AddDevice(lamp).Execute(project).IsSuccess);

        Layout(project);

        Assert.True(PlacementOf(project, lamp, "X1").Y > lowest);
    }

    [Fact]
    public void Running_again_places_nothing_new_and_undo_removes_everything_at_once()
    {
        var (project, _, _, _, _) = MotorCircuit();
        var stack = new CommandStack(project);
        stack.Execute(AutoLayout.Build(project), ChangeSource.User, "test");

        Assert.Empty(AutoLayout.Build(project).Commands);

        stack.Undo();
        Assert.Empty(project.Placements);
    }

    [Fact]
    public void Symbols_stay_inside_the_drawing_area()
    {
        var (project, _, _, _, _) = MotorCircuit();
        var area = SheetGeometry.For(Template).DrawingArea;

        Layout(project);

        Assert.All(project.Placements, placement =>
        {
            Assert.InRange(placement.X, 0, area.Width);
            Assert.InRange(placement.Y, 0, area.Height - Symbol.Length);
        });
    }

    [Fact]
    public void Nothing_is_placed_without_a_page_for_the_function_node_or_without_a_template()
    {
        var loose = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.Coil, "A1", "A2")]);
        var withoutNode = Project.Create(
            ProjectId.New(),
            "p",
            devices: [loose],
            pages: [new Page(PageId.New(), "1", "Sayfa")],
            sheetTemplates: [Template]).Value;

        Assert.Empty(AutoLayout.Build(withoutNode).Commands);

        var node = new StructureNode(StructureNodeId.New(), Aspect.Function, "M1");
        var device = new Device(DeviceId.New(), "K1", [Fn(FunctionKind.Coil, "A1", "A2")], functionNodeId: node.Id);
        var withoutTemplate = Project.Create(
            ProjectId.New(),
            "p",
            structure: [node],
            devices: [device],
            pages: [new Page(PageId.New(), "1", "Sayfa", node.Id)]).Value;

        Assert.Empty(AutoLayout.Build(withoutTemplate).Commands);
    }

    [Fact]
    public void Reference_machine_is_already_fully_placed()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        var path = Path.Combine(directory!.FullName, "examples", "ref-machine", "ref-machine.json");
        var machine = ProjectFile.Load(path).Value;

        Assert.Empty(AutoLayout.Build(machine).Commands);
        Assert.Equal(68, machine.Placements.Count);
    }
}
