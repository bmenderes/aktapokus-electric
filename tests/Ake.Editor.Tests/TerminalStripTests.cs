using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Klemens şeridi düzenleyicisinin oturum tarafı (ADR-0030).</summary>
public sealed class TerminalStripTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-strips-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;

    public TerminalStripTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
        _original = ProjectJson.Serialize(_session.Project!);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private Device Strip => _session.Project!.Devices.Single(device => device.ProductTag == "X1");

    private TerminalStripData Data => _session.TerminalStripOf(Strip.Id)!;

    private TerminalStripEdits Unchanged => new(Strip.Id, Data.Terminals);

    private void AssertUnchanged() => Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));

    [Fact]
    public void The_strip_is_listed_in_order_with_names_wires_and_where_each_terminal_is_drawn()
    {
        var data = Data;

        Assert.Equal(Strip.Functions.Select(function => function.Description), data.Terminals.Select(row => row.Name));
        Assert.Equal(Strip.Functions.Select(function => function.Id), data.Terminals.Select(row => row.Id!.Value));
        Assert.Contains(data.Terminals, row => row.Connections > 0);
        Assert.All(data.Terminals, row => Assert.True(row.Where.StartsWith("sayfa", StringComparison.Ordinal) || row.Where == "yerleştirilmemiş"));
        Assert.Contains("X1", data.Designation, StringComparison.Ordinal);
        Assert.Null(_session.TerminalStripOf(_session.Project!.Devices.Single(device => device.ProductTag == "K1").Id));
        Assert.Null(_session.TerminalStripOf(DeviceId.New()));
    }

    [Fact]
    public void Add_rename_reorder_and_jumper_flags_are_applied_in_one_undoable_step()
    {
        var rows = Data.Terminals.ToList();
        var first = rows[0];
        List<TerminalRow> edited =
        [
            new(null, "A1", JumperInner: true, JumperOuter: false, 0, string.Empty),
            first with { Name = "01", JumperInner = true },
            .. rows.Skip(1),
        ];

        var result = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, edited));

        Assert.True(result.IsSuccess, result.Error?.Message);
        var strip = Strip;
        Assert.Equal(["A1", "01", .. rows.Skip(1).Select(row => row.Name)], strip.Functions.Select(function => function.Description));
        Assert.Equal(["A1.a", "A1.b"], strip.Functions[0].ConnectionPoints.Select(point => point.Name));
        Assert.Equal(["01.a", "01.b"], strip.Functions[1].ConnectionPoints.Select(point => point.Name));
        Assert.Equal(first.Id, strip.Functions[1].Id);
        // A1 → 01 iç köprü; 01 → sonraki iç köprü
        Assert.NotNull(_session.Project!.Connections.FirstOrDefault(c => c.Wire is null && c.CoreId is null && c.From == strip.Functions[0].ConnectionPoints[0].Id && c.To == strip.Functions[1].ConnectionPoints[0].Id));
        Assert.NotNull(_session.Project.Connections.FirstOrDefault(c => c.Wire is null && c.CoreId is null && c.From == strip.Functions[1].ConnectionPoints[0].Id && c.To == strip.Functions[2].ConnectionPoints[0].Id));

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void Reordering_moves_terminals_without_touching_their_wires()
    {
        var rows = Data.Terminals.ToList();
        var connections = _session.Project!.Connections.Count;
        var reversed = rows.AsEnumerable().Reverse().Select(row => row with { JumperInner = false, JumperOuter = false }).ToList();

        var result = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, reversed));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(reversed.Select(row => row.Id!.Value), Strip.Functions.Select(function => function.Id));
        Assert.True(_session.Project.Connections.Count <= connections);
        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void A_terminal_without_wires_can_be_added_and_removed_again_but_a_wired_one_cannot()
    {
        var rows = Data.Terminals.ToList();
        var withNew = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [.. rows, new TerminalRow(null, "99", false, false, 0, string.Empty)]));
        Assert.True(withNew.IsSuccess, withNew.Error?.Message);
        Assert.Equal("99", Strip.Functions[^1].Description);

        var removedAgain = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, rows));
        Assert.True(removedAgain.IsSuccess, removedAgain.Error?.Message);

        var wired = rows.First(row => row.Connections > 0);
        var refused = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [.. rows.Where(row => row != wired)]));
        Assert.Equal("terminal.bagli", refused.Error!.Code);
        Assert.Equal(rows.Count, Strip.Functions.Count);
    }

    [Fact]
    public void Invalid_edits_change_nothing()
    {
        var rows = Data.Terminals.ToList();

        Assert.Equal("terminal.ad_tekrar", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [rows[0], rows[1] with { Name = rows[0].Name }, .. rows.Skip(2)])).Error!.Code);
        Assert.Equal("terminal.ad_gecersiz", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [rows[0] with { Name = "a b" }, .. rows.Skip(1)])).Error!.Code);
        Assert.Equal("terminal.ad_gecersiz", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [rows[0] with { Name = " " }, .. rows.Skip(1)])).Error!.Code);
        Assert.Equal("terminal.son", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [])).Error!.Code);
        Assert.Equal("device.serit_degil", _session.ApplyTerminalStrip(new TerminalStripEdits(DeviceId.New(), rows)).Error!.Code);

        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void Unchanged_edits_leave_no_history()
    {
        Assert.True(_session.ApplyTerminalStrip(Unchanged).IsSuccess);

        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    private Connection? Jumper(Function a, Function b, int side = 0) => _session.Project!.Connections.FirstOrDefault(c =>
        c.Wire is null && c.CoreId is null && c.From == a.ConnectionPoints[side].Id && c.To == b.ConnectionPoints[side].Id);

    [Fact]
    public void A_jumper_between_terminals_that_were_not_neighbours_survives_when_they_become_neighbours()
    {
        List<TerminalRow> three =
        [
            .. Data.Terminals,
            new(null, "91", false, false, 0, string.Empty),
            new(null, "92", false, false, 0, string.Empty),
            new(null, "93", false, false, 0, string.Empty),
        ];
        Assert.True(_session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, three)).IsSuccess);
        var (t91, t93) = (Strip.Functions[^3], Strip.Functions[^1]);
        var jumper = new Connection(ConnectionId.New(), t91.ConnectionPoints[0].Id, t93.ConnectionPoints[0].Id);
        Assert.True(new CommandStack(_session.Project!).Execute(new Connect(jumper), ChangeSource.User, "t").IsSuccess);

        var withoutMiddle = Data.Terminals.Where(row => row.Name != "92").ToList();
        Assert.True(_session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, withoutMiddle)).IsSuccess);

        Assert.Contains(jumper, _session.Project!.Connections);
    }

    [Fact]
    public void Unchecking_a_flag_removes_only_a_jumper_that_the_dialog_showed()
    {
        List<TerminalRow> rows = [.. Data.Terminals, new(null, "91", JumperInner: true, JumperOuter: true, 0, string.Empty), new(null, "92", false, false, 0, string.Empty)];
        Assert.True(_session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, rows)).IsSuccess);
        var (t91, t92) = (Strip.Functions[^2], Strip.Functions[^1]);
        Assert.NotNull(Jumper(t91, t92, 0));
        Assert.NotNull(Jumper(t91, t92, 1));

        var shown = Data.Terminals.ToList();
        Assert.True(shown[^2].JumperInner && shown[^2].JumperOuter);
        Assert.True(_session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [.. shown[..^2], shown[^2] with { JumperInner = false }, shown[^1]])).IsSuccess);

        Assert.Null(Jumper(Strip.Functions[^2], Strip.Functions[^1], 0));
        Assert.NotNull(Jumper(Strip.Functions[^2], Strip.Functions[^1], 1));
    }

    [Fact]
    public void A_strip_terminal_symbol_leads_to_its_strip_and_other_symbols_do_not()
    {
        var project = _session.Project!;
        var terminal = Strip.Functions.First(function => project.Placements.Any(p => p.FunctionId == function.Id));
        var placement = project.Placements.First(p => p.FunctionId == terminal.Id);
        var k1 = project.Devices.Single(device => device.ProductTag == "K1");
        var contactor = project.Placements.First(p => k1.Functions.Any(function => function.Id == p.FunctionId));

        Assert.Equal(Strip.Id, _session.StripOfPlacement(placement.Id));
        Assert.Null(_session.StripOfPlacement(contactor.Id));
        Assert.Null(_session.StripOfPlacement(PlacementId.New()));
    }

    [Theory]
    [InlineData(new string[0], "1")]
    [InlineData(new[] { "1", "2", "3" }, "4")]
    [InlineData(new[] { "L1", "L2", "N" }, "4")]
    [InlineData(new[] { "1", "7" }, "8")]
    [InlineData(new[] { "PE" }, "2")]
    public void The_next_terminal_name_continues_numbers_or_counts_and_stays_unique(string[] names, string expected) =>
        Assert.Equal(expected, EditorSession.NextTerminalName(names));

    [Fact]
    public void Levels_set_in_the_strip_window_are_applied_in_one_undoable_step()
    {
        var rows = Data.Terminals.ToList();
        var first = rows[0];
        List<TerminalRow> edited =
        [
            first,
            first with { Id = null, Level = 2, JumperInner = false, JumperOuter = false },
            first with { Id = null, Level = 3, JumperInner = false, JumperOuter = false },
            .. rows.Skip(1),
        ];

        var applied = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, edited));

        Assert.True(applied.IsSuccess, applied.Error?.Message);
        Assert.Equal([1, 2, 3], Strip.Functions.Take(3).Select(function => function.Level));
        Assert.Equal([1, 2, 3], Data.Terminals.Take(3).Select(row => row.Level));
        Assert.Equal($"{first.Name}.a3", Strip.Functions[2].ConnectionPoints[0].Name);

        // İlk klemensi 2. kata almak, o katta aynı ad olduğu için reddedilir ve hiçbir şey değişmez.
        var clash = Data.Terminals.ToList();
        clash[0] = clash[0] with { Level = 2 };
        Assert.Equal("terminal.ad_tekrar", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, clash)).Error!.Code);
        Assert.Equal(1, Strip.Functions[0].Level);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void Multi_way_pe_and_end_cover_rows_are_added_in_one_step_and_the_way_count_of_an_existing_terminal_is_fixed()
    {
        var rows = Data.Terminals.ToList();
        List<TerminalRow> edited =
        [
            .. rows,
            new TerminalRow(null, "90", false, false, 0, string.Empty, 1, 3, TerminalRole.Normal),
            new TerminalRow(null, "PE9", false, false, 0, string.Empty, 1, 2, TerminalRole.ProtectiveEarth),
            new TerminalRow(null, "SL", false, false, 0, string.Empty, 1, 1, TerminalRole.EndCover),
        ];

        var applied = _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, edited));

        Assert.True(applied.IsSuccess, applied.Error?.Message);
        var terminals = Strip.Functions.TakeLast(3).ToList();
        Assert.Equal([6, 4, 0], terminals.Select(function => function.ConnectionPoints.Count));
        Assert.Equal([TerminalRole.Normal, TerminalRole.ProtectiveEarth, TerminalRole.EndCover], terminals.Select(function => function.Role));
        Assert.Equal([3, 2, 1], Data.Terminals.TakeLast(3).Select(row => row.Ways));

        // Var olan klemensin bağlantı sayısı değişmez; PE olarak işaretlemek serbest.
        var again = Data.Terminals.ToList();
        again[0] = again[0] with { Ways = 2 };
        Assert.Equal("terminal.baglanti_degismez", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, again)).Error!.Code);
        var marked = Data.Terminals.ToList();
        marked[0] = marked[0] with { Role = TerminalRole.ProtectiveEarth };
        Assert.True(_session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, marked)).IsSuccess);
        Assert.Equal(TerminalRole.ProtectiveEarth, Strip.Functions[0].Role);
        Assert.Equal("terminal.baglanti_gecersiz", _session.ApplyTerminalStrip(new TerminalStripEdits(Strip.Id, [.. Data.Terminals, new TerminalRow(null, "77", false, false, 0, string.Empty, 1, 5)])).Error!.Code);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }
}
