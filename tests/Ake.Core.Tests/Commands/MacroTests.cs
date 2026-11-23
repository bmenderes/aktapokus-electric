using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Core.Tests.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Makro yakalama ve yerleştirme (ADR-0025).</summary>
public class MacroTests
{
    private static Page PageNumbered(Project project, string number) => project.Pages.Single(page => page.Number == number);

    private static Macro MotorMacro(Project project) =>
        MacroService.Capture(project, PageNumbered(project, "2").Id, "Motor çıkışı").Value;

    [Fact]
    public void Page_macro_takes_its_devices_connections_and_relative_positions()
    {
        var project = TestData.RefMachine();

        var macro = MotorMacro(project);

        Assert.Equal("Motor çıkışı", macro.Name);
        Assert.Contains(macro.Devices, device => device.ProductTag == "K1");
        Assert.Contains(macro.Devices, device => device.ProductTag == "M1");
        Assert.All(macro.Devices, device => Assert.Null(device.FunctionNodeId));
        Assert.Equal(0, macro.Placements.Min(p => p.X));
        Assert.Equal(0, macro.Placements.Min(p => p.Y));
        Assert.NotEmpty(macro.Connections);
        Assert.All(macro.Connections, connection => Assert.Null(connection.Wire?.Number));
        Assert.Null(macro.Check());
    }

    [Fact]
    public void Terminal_strip_brings_only_the_terminals_placed_in_the_macro()
    {
        var project = TestData.RefMachine();
        var page = PageNumbered(project, "2");
        var strip = project.Devices.Single(device => device.Kind == DeviceKind.TerminalStrip
            && device.Functions.Any(f => project.Placements.Any(p => p.PageId == page.Id && p.FunctionId == f.Id)));

        var macro = MotorMacro(project);

        var copied = macro.Devices.Single(device => device.Id == strip.Id);
        var onPage = strip.Functions.Count(f => project.Placements.Any(p => p.PageId == page.Id && p.FunctionId == f.Id));
        Assert.Equal(onPage, copied.Functions.Count);
        Assert.True(copied.Functions.Count < strip.Functions.Count);
    }

    [Fact]
    public void Area_macro_takes_only_the_devices_whose_symbol_stands_inside()
    {
        var project = TestData.RefMachine();
        var page = PageNumbered(project, "2");
        var breaker = project.Device("F1");
        var placement = project.Placements.First(p => p.PageId == page.Id && breaker.Functions.Any(f => f.Id == p.FunctionId));

        var macro = MacroService.Capture(project, page.Id, "Sigorta", (placement.X - 1, placement.Y - 1, 2, 2)).Value;

        Assert.Equal(["F1"], macro.Devices.Select(device => device.ProductTag));
        Assert.Empty(macro.Connections);
    }

    [Fact]
    public void Inserting_is_one_undoable_step_with_fresh_ids_and_the_next_free_tags()
    {
        var project = TestData.RefMachine();
        var macro = MotorMacro(project);
        var target = PageNumbered(project, "4");
        var before = project.Devices.Count;

        var transaction = MacroService.Insert(project, macro, target.Id, 250, 40).Value;
        CommandAssert.RoundTrips(project, transaction);

        var added = project.Devices.Skip(before).ToList();
        Assert.Equal(macro.Devices.Count, added.Count);
        Assert.All(added, device => Assert.DoesNotContain(macro.Devices, template => template.Id == device.Id));
        Assert.All(added, device => Assert.Equal(target.FunctionNodeId, device.FunctionNodeId));

        // =M2+P1 kapsamında F2 ve K2 var: yeniler F3 ve K3.
        Assert.Contains(added, device => device.ProductTag == "F3");
        Assert.Contains(added, device => device.ProductTag == "K3");
        Assert.DoesNotContain(ErcEngine.Default.Run(project).Findings, finding => finding.RuleId == "erc.tag_tekrar");

        var placed = project.Placements.Where(p => added.SelectMany(d => d.Functions).Any(f => f.Id == p.FunctionId)).ToList();
        Assert.Equal(macro.Placements.Count, placed.Count);
        Assert.All(placed, p => Assert.Equal(target.Id, p.PageId));
        Assert.Equal((250.0, 40.0), (placed.Min(p => p.X), placed.Min(p => p.Y)));
    }

    [Fact]
    public void Page_scheme_gives_the_inserted_devices_the_page_number()
    {
        var project = TestData.RefMachine();
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(Renumbering.ChangeTagScheme(project, TagScheme.PageLetterNumber), ChangeSource.User, "test").IsSuccess);
        var macro = MotorMacro(project);
        var before = project.Devices.Count;

        var transaction = MacroService.Insert(project, macro, PageNumbered(project, "4").Id, 250, 40).Value;
        Assert.True(stack.Execute(transaction, ChangeSource.User, "test").IsSuccess);

        var tags = project.Devices.Skip(before).Select(device => device.ProductTag).ToList();
        Assert.Contains("4K2", tags);
        Assert.Contains("4F2", tags);
    }

    [Fact]
    public void User_symbols_travel_with_the_macro_and_are_added_only_when_missing()
    {
        var symbol = new SymbolDefinition(
            SymbolId.New(), "Lamba", [new SymbolCircle(0, 7.5, 4)], [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)], "test", "CC0");
        var lamp = new Device(
            DeviceId.New(),
            "H1",
            [new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "X1"), new ConnectionPoint(ConnectionPointId.New(), "X2")], symbolId: symbol.Id)]);
        var page = new Page(PageId.New(), "1", "Sinyal");
        var project = Project.Create(
            ProjectId.New(),
            "p",
            devices: [lamp],
            pages: [page],
            placements: [new Placement(PlacementId.New(), page.Id, lamp.Functions[0].Id, PlacementRole.Main, 50, 50)],
            symbols: [symbol]).Value;
        var other = Project.Create(ProjectId.New(), "q", pages: [new Page(PageId.New(), "1", "Boş")]).Value;

        var macro = MacroService.Capture(project, page.Id, "Lamba").Value;

        Assert.Equal([symbol.Id], macro.Symbols.Select(s => s.Id));
        Assert.DoesNotContain(MacroService.Insert(project, macro, page.Id, 100, 50).Value.Commands, c => c is PutSymbol);

        var into = MacroService.Insert(other, macro, other.Pages[0].Id, 20, 20).Value;
        CommandAssert.RoundTrips(other, into);
        Assert.Equal(symbol.Id, Assert.Single(other.Symbols).Id);
        Assert.Equal("H1", Assert.Single(other.Devices).ProductTag);
    }

    [Fact]
    public void Capturing_nothing_or_an_unknown_page_fails_cleanly()
    {
        var project = TestData.RefMachine();

        Assert.Equal("makro.bos", MacroService.Capture(project, PageNumbered(project, "2").Id, "Boş", (400, 280, 1, 1)).Error!.Code);
        Assert.Equal("sayfa.yok", MacroService.Capture(project, PageId.New(), "x").Error!.Code);
        Assert.Equal("sayfa.yok", MacroService.Insert(project, MotorMacro(project), PageId.New(), 0, 0).Error!.Code);
    }

    [Fact]
    public void Macro_with_a_connection_leaving_it_is_rejected()
    {
        var inside = Build.Device("K1", "A1");
        var outside = Build.Device("K2", "A1");
        var macro = new Macro("Bozuk", [inside], [Build.Connect(inside, "A1", outside, "A1")], [], []);

        Assert.Equal("makro.baglanti_disarida", macro.Check()!.Code);
        Assert.Equal("makro.ad_yok", (macro with { Name = " " }).Check()!.Code);
    }
}
