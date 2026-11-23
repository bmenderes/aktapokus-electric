using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Core.Tests.Commands;

namespace Ake.Core.Tests.Erc;

/// <summary>Esnek tel numaralandırma: kod, başlangıç, adım, basamak; yeni telleri kaldığı yerden numaralama (ADR-0041).</summary>
public class WireNumberingTests
{
    private static List<string?> Numbers(Project project) =>
        [.. project.Connections.Where(connection => connection.Wire is not null).Select(connection => connection.Wire!.Number)];

    private static void Apply(Project project, Transaction transaction) =>
        Assert.True(new CommandStack(project).Execute(transaction, ChangeSource.User, "test").IsSuccess);

    [Fact]
    public void Settings_format_read_back_and_validate()
    {
        var settings = new WireNumberSettings("W", 100, 5, 4);

        Assert.Equal("W0100", settings.Format(100));
        Assert.Equal("W0105", settings.Format(105));
        Assert.True(settings.TryRead("W0105", out var value));
        Assert.Equal(105, value);
        Assert.False(settings.TryRead("X0105", out _));
        Assert.False(settings.TryRead("W12a", out _));
        Assert.False(settings.TryRead("W", out _));
        Assert.False(settings.TryRead(null, out _));
        Assert.Equal("7", new WireNumberSettings().Format(7));
        Assert.True(new WireNumberSettings("1", 0).TryRead("12", out var shifted) && shifted == 2);
        Assert.Equal("numara.kod_gecersiz", new WireNumberSettings("W 1").Check()!.Code);
        Assert.Equal("numara.kod_gecersiz", new WireNumberSettings(new string('x', 13)).Check()!.Code);
        Assert.Equal("numara.baslangic_gecersiz", new WireNumberSettings(Start: -1).Check()!.Code);
        Assert.Equal("numara.adim_gecersiz", new WireNumberSettings(Step: 0).Check()!.Code);
        Assert.Equal("numara.basamak_gecersiz", new WireNumberSettings(Width: 9).Check()!.Code);
        Assert.Null(settings.Check());
    }

    [Fact]
    public void The_settings_are_a_project_command_and_invalid_ones_are_refused()
    {
        var project = TestData.RefMachine();
        Assert.Equal(new WireNumberSettings(), project.WireNumbering);

        CommandAssert.RoundTrips(project, new SetWireNumbering(new WireNumberSettings("W", 100, 1, 3)));

        Assert.Equal(new WireNumberSettings("W", 100, 1, 3), project.WireNumbering);
        CommandAssert.Rejected(project, new SetWireNumbering(new WireNumberSettings(Step: 0)), "numara.adim_gecersiz");
    }

    [Fact]
    public void Renumbering_from_scratch_uses_the_code_start_step_and_width()
    {
        var project = TestData.RefMachine();
        new SetWireNumbering(new WireNumberSettings("W", 100, 2, 4, WireNumberingScheme.Sequential)).Execute(project);

        Apply(project, Renumbering.Wires(project, project.WireNumbering));

        var numbers = Numbers(project);
        Assert.Equal("W0100", numbers[0]);
        Assert.Equal("W0102", numbers[1]);
        Assert.Equal($"W{100 + (2 * (numbers.Count - 1)):0000}", numbers[^1]);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());
    }

    [Fact]
    public void New_wires_continue_from_the_highest_existing_number_and_leave_existing_numbers_alone()
    {
        var project = TestData.RefMachine();
        var before = Numbers(project);
        var highest = before.Where(number => number is not null).Select(number => int.Parse(number!)).Max();
        var blank = project.Connections.Where(connection => connection.Wire is not null).Take(3).ToList();
        var blankNumbers = blank.Select(connection => connection.Wire!.Number).ToList();
        foreach (var connection in blank)
        {
            new AssignWire(connection.Id, new Wire(connection.Wire!.Id, null, connection.Wire.CrossSectionMm2, connection.Wire.Color), null).Execute(project);
        }

        var transaction = Renumbering.NewWires(project);
        Apply(project, transaction);

        // Numarasız üç tel numaralandı; kalanlar aynı.
        var after = Numbers(project);
        Assert.DoesNotContain(null, after);
        Assert.Equal(before.Count, after.Count);
        foreach (var (connection, index) in project.Connections.Where(connection => connection.Wire is not null).Select((connection, index) => (connection, index)))
        {
            if (!blank.Contains(connection))
            {
                Assert.Equal(before[index], connection.Wire!.Number);
            }
        }

        // Yeni numaralar en büyükten sonra gelir (potansiyel bazlı: numarası olan potansiyelin numarasını alanlar hariç).
        var fresh = blank.Select(connection => int.Parse(connection.Wire!.Number!)).ToList();
        Assert.All(fresh, number => Assert.True(number >= 1));
        Assert.True(blankNumbers.Count == 3);
        Assert.True(highest >= 1);
    }

    [Fact]
    public void Wires_added_after_the_last_number_get_the_next_numbers_and_a_second_run_does_nothing()
    {
        var project = TestData.RefMachine();
        new SetWireNumbering(new WireNumberSettings("W", 100, 1, 3, WireNumberingScheme.Sequential)).Execute(project);
        Apply(project, Renumbering.Wires(project, project.WireNumbering));
        var existing = Numbers(project);

        // Araya yeni bir tel eklenir (numarasız): numaralar en yüksekten sürer, eskiler değişmez.
        var a = project.Point("K1", "A1");
        var b = project.Point("K2", "A1");
        Assert.True(new Connect(new Connection(ConnectionId.New(), a, b, new Wire(WireId.New()))).Execute(project).IsSuccess);
        Apply(project, Renumbering.NewWires(project));

        var after = Numbers(project);
        Assert.Equal(existing, after.Take(existing.Count));
        Assert.Equal($"W{100 + existing.Count:000}", after[^1]);
        Assert.Empty(Renumbering.NewWires(project).Commands);
    }

    [Fact]
    public void Numbers_without_the_projects_code_are_ignored_when_finding_where_to_continue_and_start_applies_when_none_match()
    {
        var project = TestData.RefMachine();
        new SetWireNumbering(new WireNumberSettings("K", 500, 10, 0, WireNumberingScheme.Sequential)).Execute(project);
        var blank = project.Connections.First(connection => connection.Wire is not null);
        foreach (var connection in project.Connections.Where(connection => connection.Wire is not null && connection != blank))
        {
            // Mevcut numaralar kodsuz (1, 2, 3…): "K" kodlu numara yok.
            Assert.False(project.WireNumbering.TryRead(connection.Wire!.Number, out _));
        }

        new AssignWire(blank.Id, new Wire(blank.Wire!.Id, null, blank.Wire.CrossSectionMm2, blank.Wire.Color), null).Execute(project);
        Apply(project, Renumbering.NewWires(project));

        Assert.Equal("K500", blank.Wire!.Number);
    }

    [Fact]
    public void In_the_per_potential_scheme_a_new_wire_on_a_numbered_potential_takes_its_number()
    {
        var project = TestData.RefMachine();
        var connectivity = Connectivity.Compute(project);
        var group = project.Connections
            .Where(connection => connection.Wire?.Number is not null)
            .GroupBy(connection => connectivity.PotentialOf(connection.From))
            .First(candidate => candidate.Count() >= 2);
        var (wire, other) = (group.First(), group.Skip(1).First());
        var potentialNumber = wire.Wire!.Number;
        new AssignWire(other.Id, new Wire(other.Wire!.Id, null, other.Wire.CrossSectionMm2, other.Wire.Color), null).Execute(project);

        Apply(project, Renumbering.NewWires(project));

        Assert.Equal(potentialNumber, other.Wire!.Number);
    }
}
