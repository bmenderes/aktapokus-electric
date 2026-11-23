using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Klemens şeridi komutları: ekle, kaldır, taşı (ADR-0030).</summary>
public class TerminalCommandTests
{
    private static Device Strip(Project project) => project.Devices.First(device => device.Kind == DeviceKind.TerminalStrip && device.Functions.Count > 2);

    [Fact]
    public void A_new_terminal_gets_its_name_points_and_place_and_is_registered_with_the_model()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = AddTerminal.Create("9");

        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, 1, terminal));

        Assert.Equal(terminal, strip.Functions[1]);
        Assert.Equal((FunctionKind.Terminal, true, "9"), (terminal.Kind, terminal.PassThrough, terminal.Description));
        Assert.Equal(["9.a", "9.b"], terminal.ConnectionPoints.Select(point => point.Name));
        Assert.Same(strip, project.Locate(terminal.ConnectionPoints[0].Id)!.Device);
    }

    [Fact]
    public void Undoing_an_add_unregisters_the_points()
    {
        var project = TestData.RefMachine();
        var terminal = AddTerminal.Create("9");
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new AddTerminal(Strip(project).Id, 0, terminal), ChangeSource.User, "t").IsSuccess);

        Assert.True(stack.Undo().IsSuccess);

        Assert.Null(project.Locate(terminal.ConnectionPoints[0].Id));
        Assert.DoesNotContain(terminal, Strip(project).Functions);
    }

    [Fact]
    public void Terminal_names_must_be_valid_and_unique_inside_the_strip_and_the_place_must_exist()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var existing = strip.Functions[0].Description!;

        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create(existing)), "terminal.ad_tekrar");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("  ")), "terminal.ad_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("a b")), "terminal.ad_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("X:1")), "terminal.ad_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, strip.Functions.Count + 1, AddTerminal.Create("9")), "terminal.sira_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, -1, AddTerminal.Create("9")), "terminal.sira_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(project.Device("K1").Id, 0, AddTerminal.Create("9")), "device.serit_degil");
        CommandAssert.Rejected(project, new AddTerminal(DeviceId.New(), 0, AddTerminal.Create("9")), "device.yok");
    }

    [Fact]
    public void Removing_a_terminal_takes_its_jumpers_and_placement_along_and_undo_brings_everything_back()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = AddTerminal.Create("9");
        var page = project.Pages[0];
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new AddTerminal(strip.Id, 1, terminal), ChangeSource.User, "t").IsSuccess);
        var jumper = new Connection(ConnectionId.New(), terminal.ConnectionPoints[0].Id, strip.Functions[0].ConnectionPoints[0].Id);
        Assert.True(stack.Execute(new Connect(jumper), ChangeSource.User, "t").IsSuccess);
        Assert.True(stack.Execute(new AddPlacement(new Placement(PlacementId.New(), page.Id, terminal.Id, PlacementRole.Main, 200, 100)), ChangeSource.User, "t").IsSuccess);

        CommandAssert.RoundTrips(project, new RemoveTerminal(strip.Id, terminal.Id));

        Assert.DoesNotContain(terminal, strip.Functions);
        Assert.DoesNotContain(jumper, project.Connections);
        Assert.DoesNotContain(project.Placements, placement => placement.FunctionId == terminal.Id);
        Assert.Null(project.Locate(terminal.ConnectionPoints[0].Id));
    }

    [Fact]
    public void A_terminal_with_a_wire_or_core_on_it_and_the_last_terminal_stay()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var wired = strip.Functions.First(function => project.Connections.Any(c =>
            function.ConnectionPoints.Any(point => point.Id == c.From || point.Id == c.To) && (c.Wire is not null || c.CoreId is not null)));

        CommandAssert.Rejected(project, new RemoveTerminal(strip.Id, wired.Id), "terminal.bagli");
        CommandAssert.Rejected(project, new RemoveTerminal(strip.Id, FunctionId.New()), "function.yok");
        CommandAssert.Rejected(project, new RemoveTerminal(project.Device("K1").Id, wired.Id), "device.serit_degil");

        var single = new Device(DeviceId.New(), "X9", [AddTerminal.Create("1")], DeviceKind.TerminalStrip);
        var tiny = Project.Create(ProjectId.New(), "küçük", devices: [single]).Value;
        CommandAssert.Rejected(tiny, new RemoveTerminal(single.Id, single.Functions[0].Id), "terminal.son");
    }

    [Fact]
    public void Moving_a_terminal_changes_only_the_order_and_undo_restores_it()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var before = strip.Functions.Select(function => function.Id).ToList();

        CommandAssert.RoundTrips(project, new MoveTerminal(strip.Id, before[0], 2));

        Assert.Equal([before[1], before[2], before[0], .. before.Skip(3)], strip.Functions.Select(function => function.Id));
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new MoveTerminal(strip.Id, before[0], 0), ChangeSource.User, "t").IsSuccess);
        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal([before[1], before[2], before[0], .. before.Skip(3)], strip.Functions.Select(function => function.Id));
    }

    [Fact]
    public void Moving_with_a_bad_index_or_unknown_terminal_is_refused()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);

        CommandAssert.Rejected(project, new MoveTerminal(strip.Id, strip.Functions[0].Id, strip.Functions.Count), "terminal.sira_gecersiz");
        CommandAssert.Rejected(project, new MoveTerminal(strip.Id, strip.Functions[0].Id, -1), "terminal.sira_gecersiz");
        CommandAssert.Rejected(project, new MoveTerminal(strip.Id, FunctionId.New(), 0), "function.yok");
        CommandAssert.Rejected(project, new MoveTerminal(DeviceId.New(), strip.Functions[0].Id, 0), "device.yok");
    }

    [Fact]
    public void A_multi_level_terminal_shares_its_name_with_the_other_levels_and_names_its_points_by_level()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var name = strip.Functions[0].Description!;
        var second = AddTerminal.Create(name, 2);
        var third = AddTerminal.Create(name, 3);

        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, 1, second));
        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, 2, third));

        Assert.Equal([$"{name}.a2", $"{name}.b2"], second.ConnectionPoints.Select(point => point.Name));
        Assert.Equal([$"{name}.a3", $"{name}.b3"], third.ConnectionPoints.Select(point => point.Name));
        Assert.Equal((2, 3), (second.Level, third.Level));
        Assert.Equal(1, strip.Functions[0].Level);
    }

    [Fact]
    public void The_same_name_on_the_same_level_or_a_level_outside_one_to_three_is_rejected()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var name = strip.Functions[0].Description!;

        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create(name, 1)), "terminal.ad_tekrar");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("9", 4)), "terminal.kat_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("9", 0)), "terminal.kat_gecersiz");
    }

    [Fact]
    public void SetTerminalLevel_moves_a_terminal_to_another_level_renames_its_points_and_undoes()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = strip.Functions[0];
        var name = terminal.Description!;
        var connections = project.Connections.Count;

        CommandAssert.RoundTrips(project, new SetTerminalLevel(strip.Id, terminal.Id, 2));

        Assert.Equal(2, terminal.Level);
        Assert.Equal([$"{name}.a2", $"{name}.b2"], terminal.ConnectionPoints.Select(point => point.Name));
        Assert.Equal(connections, project.Connections.Count);
    }

    [Fact]
    public void SetTerminalLevel_rejects_a_taken_level_and_a_bad_level()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = strip.Functions[0];
        var second = AddTerminal.Create(terminal.Description!, 2);
        new AddTerminal(strip.Id, 1, second).Execute(project);

        CommandAssert.Rejected(project, new SetTerminalLevel(strip.Id, terminal.Id, 2), "terminal.ad_tekrar");
        CommandAssert.Rejected(project, new SetTerminalLevel(strip.Id, terminal.Id, 4), "terminal.kat_gecersiz");
        CommandAssert.Rejected(project, new SetTerminalLevel(strip.Id, FunctionId.New(), 2), "function.yok");
    }

    [Fact]
    public void A_level_above_one_on_a_function_that_is_not_a_terminal_is_invalid()
    {
        var project = TestData.RefMachine();
        var device = new Device(
            DeviceId.New(),
            "Z1",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")], level: 2)]);

        CommandAssert.Rejected(project, new AddDevice(device), "function.kat_gecersiz");
    }

    [Fact]
    public void A_multi_way_terminal_has_several_points_per_side_named_after_the_first()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = AddTerminal.Create("9", ways: 3);
        var level2 = AddTerminal.Create("9", level: 2, ways: 2);

        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, 1, terminal));
        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, 2, level2));

        Assert.Equal(["9.a", "9.a.2", "9.a.3", "9.b", "9.b.2", "9.b.3"], terminal.ConnectionPoints.Select(point => point.Name));
        Assert.Equal(["9.a2", "9.a2.2", "9.b2", "9.b2.2"], level2.ConnectionPoints.Select(point => point.Name));
        Assert.Equal((3, 2), (terminal.Ways, level2.Ways));
        Assert.Equal(["9.a", "9.a.2", "9.a.3"], TerminalNames.OnSide(terminal, 0).Select(point => point.Name));
        Assert.Equal(["9.b", "9.b.2", "9.b.3"], TerminalNames.OnSide(terminal, 1).Select(point => point.Name));
    }

    [Fact]
    public void Moving_a_multi_way_terminal_to_another_level_renames_all_its_points()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = AddTerminal.Create("9", ways: 2);
        new AddTerminal(strip.Id, 1, terminal).Execute(project);

        CommandAssert.RoundTrips(project, new SetTerminalLevel(strip.Id, terminal.Id, 3));

        Assert.Equal(["9.a3", "9.a3.2", "9.b3", "9.b3.2"], terminal.ConnectionPoints.Select(point => point.Name));
    }

    [Fact]
    public void An_end_cover_has_no_points_and_the_way_count_is_limited()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var cover = AddTerminal.Create("SL", role: TerminalRole.EndCover);

        CommandAssert.RoundTrips(project, new AddTerminal(strip.Id, strip.Functions.Count, cover));

        Assert.Empty(cover.ConnectionPoints);
        Assert.Equal((TerminalRole.EndCover, 0, false), (cover.Role, cover.Ways, cover.PassThrough));
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("8", ways: 5)), "terminal.baglanti_gecersiz");
        CommandAssert.Rejected(project, new AddTerminal(strip.Id, 0, AddTerminal.Create("8", ways: 0)), "terminal.baglanti_gecersiz");
    }

    [Fact]
    public void A_terminal_becomes_a_pe_terminal_and_back_but_never_an_end_cover()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        var terminal = strip.Functions[0];
        var cover = AddTerminal.Create("SL", role: TerminalRole.EndCover);
        new AddTerminal(strip.Id, strip.Functions.Count, cover).Execute(project);

        CommandAssert.RoundTrips(project, new SetTerminalRole(strip.Id, terminal.Id, TerminalRole.ProtectiveEarth));
        Assert.Equal(TerminalRole.ProtectiveEarth, terminal.Role);

        CommandAssert.Rejected(project, new SetTerminalRole(strip.Id, terminal.Id, TerminalRole.EndCover), "terminal.tur_degismez");
        CommandAssert.Rejected(project, new SetTerminalRole(strip.Id, cover.Id, TerminalRole.Normal), "terminal.tur_degismez");
        CommandAssert.Rejected(project, new SetTerminalRole(strip.Id, FunctionId.New(), TerminalRole.Normal), "function.yok");
    }

    [Fact]
    public void A_terminal_role_on_a_function_that_is_not_a_terminal_or_an_end_cover_with_points_is_invalid()
    {
        var project = TestData.RefMachine();
        var coil = new Device(
            DeviceId.New(), "Z1", [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")], role: TerminalRole.ProtectiveEarth)]);
        var cover = new Device(
            DeviceId.New(), "Z2", [new Function(FunctionId.New(), FunctionKind.Terminal, [new ConnectionPoint(ConnectionPointId.New(), "1")], role: TerminalRole.EndCover)]);

        CommandAssert.Rejected(project, new AddDevice(coil), "function.tur_gecersiz");
        CommandAssert.Rejected(project, new AddDevice(cover), "function.tur_gecersiz");
    }

    [Fact]
    public void An_end_cover_does_not_raise_the_function_without_points_warning()
    {
        var project = TestData.RefMachine();
        var strip = Strip(project);
        new AddTerminal(strip.Id, 0, AddTerminal.Create("SL", role: TerminalRole.EndCover)).Execute(project);

        var findings = Ake.Core.Erc.ErcEngine.Default.Run(project).Findings;

        Assert.DoesNotContain(findings, finding => finding.Message.Contains("bağlantı noktası olmayan", StringComparison.Ordinal));
    }
}
