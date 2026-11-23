using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Core.Tests.Commands;
using Ake.Core.Tests.Model;

namespace Ake.Core.Tests.Erc;

public class ErcTests
{
    private static List<ErcFinding> Run(Project project, string ruleId) =>
        [.. ErcEngine.Default.Run(project).Findings.Where(finding => finding.RuleId == ruleId)];

    private static void Apply(Project project, ICommand command)
    {
        var result = command.Execute(project);
        Assert.True(result.IsSuccess, result.Error?.Message);
    }

    [Fact]
    public void Reference_machine_is_clean()
    {
        var report = ErcEngine.Default.Run(TestData.RefMachine());

        Assert.Empty(report.Findings);
        Assert.False(report.HasErrors);
    }

    [Fact]
    public void Findings_are_deterministic()
    {
        var project = TestData.RefMachine();
        Apply(project, new RemoveDevice(project.Device("K1").Id));

        var first = ErcEngine.Default.Run(project).Findings.Select(finding => finding.Message).ToList();
        var second = ErcEngine.Default.Run(project).Findings.Select(finding => finding.Message).ToList();

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Engine_runs_exactly_the_rules_it_is_given()
    {
        var engine = new ErcEngine([new AlwaysComplains()]);

        var finding = Assert.Single(engine.Run(TestData.RefMachine()).Findings);

        Assert.Equal("test.kural", finding.RuleId);
    }

    // ---- açık uç

    [Fact]
    public void Half_connected_function_is_reported_at_its_open_end()
    {
        var supply = Build.Device("T1", "+");
        var contact = Build.Device("K1", "13", "14");
        var project = Build.Project([supply, contact], [Build.Connect(supply, "+", contact, "13")]);

        var finding = Assert.Single(Run(project, "erc.acik_uc"));

        Assert.Equal(Severity.Warning, finding.Severity);
        Assert.Equal([contact.Point("14").Value], finding.Targets);
        Assert.Contains("-K1:14", finding.Message);
    }

    [Fact]
    public void Fully_unused_function_is_not_reported()
    {
        var project = Build.Project([Build.Device("K1", "13", "14")]);

        Assert.Empty(Run(project, "erc.acik_uc"));
    }

    [Fact]
    public void Point_carrying_a_potential_source_counts_as_connected()
    {
        var terminal = Build.Device("X0", passThrough: true, "L1.a", "L1.b");
        var load = Build.Device("Q1", "1");
        var project = Build.Project(
            [terminal, load],
            [Build.Connect(terminal, "L1.b", load, "1")],
            [Build.Source("L1", terminal, "L1.a")]);

        Assert.Empty(Run(project, "erc.acik_uc"));
    }

    // ---- kısa devre

    [Fact]
    public void Joining_two_named_potentials_is_a_short_circuit()
    {
        var project = TestData.RefMachine();
        Apply(project, new Connect(new Connection(ConnectionId.New(), project.Point("T1", "+"), project.Point("T1", "-"))));

        var finding = Assert.Single(Run(project, "erc.kisa_devre"));

        Assert.Equal(Severity.Error, finding.Severity);
        Assert.Contains("0V, 24VDC", finding.Message);
        Assert.Equal(2, finding.Targets.Count);
    }

    // ---- tag

    [Fact]
    public void Same_product_tag_under_different_structure_nodes_is_not_a_duplicate()
    {
        var project = TestData.RefMachine();
        var second = project.Device("K2");
        Apply(project, new Retag(second.Id, "K1", second.FunctionNodeId, second.LocationNodeId));

        Assert.Empty(Run(project, "erc.tag_tekrar"));
    }

    [Fact]
    public void Same_full_tag_on_two_devices_is_a_duplicate()
    {
        var project = TestData.RefMachine();
        var first = project.Device("K1");
        var second = project.Device("K2");
        Apply(project, new Retag(second.Id, "K1", first.FunctionNodeId, first.LocationNodeId));

        var finding = Assert.Single(Run(project, "erc.tag_tekrar"));

        Assert.Contains("=M1+P1-K1", finding.Message);
        Assert.Equal([first.Id.Value, second.Id.Value], finding.Targets);
    }

    // ---- tel numarası

    [Fact]
    public void Same_wire_number_on_different_potentials_is_a_duplicate()
    {
        var project = TestData.RefMachine();
        var phase = project.Connection("X0", "L1.a", "Q1", "1");
        var control = project.Connection("S1", "14", "A11", "1");
        Apply(project, new AssignWire(control.Id, new Wire(control.Wire!.Id, phase.Wire!.Number), null));

        var finding = Assert.Single(Run(project, "erc.tel_no_tekrar"));

        Assert.Equal(2, finding.Targets.Count);
    }

    [Fact]
    public void Same_wire_number_within_one_potential_is_allowed()
    {
        var project = TestData.RefMachine();
        var first = project.Connection("T1", "+", "A1", "24V");
        var second = project.Connection("T1", "+", "A10", "24V");
        Apply(project, new AssignWire(second.Id, new Wire(second.Wire!.Id, first.Wire!.Number), null));

        Assert.Empty(Run(project, "erc.tel_no_tekrar"));
    }

    // ---- kesit / koruma

    [Theory]
    [InlineData("0.5", null)]
    [InlineData("0.75", "8.6")]
    [InlineData("2", "13.5")]
    [InlineData("2.5", "18.3")]
    [InlineData("50", "96")]
    public void Capacity_table_rounds_down_to_the_nearest_listed_cross_section(string crossSection, string? expected)
    {
        var capacity = CrossSectionRule.CapacityOf(decimal.Parse(crossSection, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(expected, capacity?.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("16", "1.0", 1)]
    [InlineData("10", "1.0", 0)]
    [InlineData("16", "2.5", 0)]
    [InlineData(null, "1.0", 0)]
    [InlineData("16", null, 0)]
    [InlineData("16", "0.5", 0)]
    public void Wire_too_thin_for_its_protection_is_reported(string? ratedA, string? crossSection, int expectedFindings)
    {
        static decimal? Parse(string? text) =>
            text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);

        var breaker = new Device(
            DeviceId.New(),
            "F1",
            [
                new Function(
                    FunctionId.New(),
                    FunctionKind.Protection,
                    [new ConnectionPoint(ConnectionPointId.New(), "1"), new ConnectionPoint(ConnectionPointId.New(), "2")],
                    ratedCurrentA: Parse(ratedA)),
            ]);
        var load = Build.Device("H1", "X1");
        var connection = new Connection(
            ConnectionId.New(), breaker.Point("2"), load.Point("X1"), new Wire(WireId.New(), "1", Parse(crossSection)));
        var project = Build.Project([breaker, load], [connection]);

        var findings = Run(project, "erc.kesit_koruma");

        Assert.Equal(expectedFindings, findings.Count);
        if (expectedFindings == 1)
        {
            Assert.Equal([connection.Id.Value], findings[0].Targets);
            Assert.Contains("1.0 mm² tel 10.3 A taşır, koruma 16 A", findings[0].Message);
        }
    }

    // ---- I/O

    [Fact]
    public void Wired_channel_without_a_symbol_name_is_reported()
    {
        var project = TestData.RefMachine();
        var channel = project.Channel("A11", "1");
        Apply(project, new AssignIo(channel.Id, channel.FunctionId, null, null, null));

        var finding = Assert.Single(Run(project, "erc.io_atanmamis"));

        Assert.Contains("ucu bağlı ama sembolik adı yok", finding.Message);
        Assert.Equal([channel.Id.Value], finding.Targets);
    }

    [Fact]
    public void Named_channel_that_is_not_wired_is_reported()
    {
        var project = TestData.RefMachine();
        var channel = project.Channel("A11", "8");
        Apply(project, new AssignIo(channel.Id, channel.FunctionId, "bSpare", null, null));

        var finding = Assert.Single(Run(project, "erc.io_atanmamis"));

        Assert.Contains("'bSpare'", finding.Message);
    }

    [Fact]
    public void Same_symbol_name_on_two_channels_is_a_double_assignment()
    {
        var project = TestData.RefMachine();
        var spare = project.Channel("A11", "8");
        Apply(project, new AssignIo(spare.Id, spare.FunctionId, "bStartButton", null, null));

        var findings = Run(project, "erc.io_cift");

        Assert.Contains(findings, finding => finding.Message.Contains("'bStartButton'") && finding.Targets.Count == 2);
    }

    [Fact]
    public void Same_electrical_end_on_two_channels_is_a_double_assignment()
    {
        var project = TestData.RefMachine();
        var first = project.Channel("A11", "1");
        var spare = project.Channel("A11", "8");
        Apply(project, new AssignIo(spare.Id, first.FunctionId, null, null, null));

        Assert.Contains(Run(project, "erc.io_cift"), finding => finding.Message.Contains("elektriksel uç"));
    }

    [Fact]
    public void Same_address_twice_is_a_double_assignment_unless_it_is_an_auto_address()
    {
        var project = TestData.RefMachine();
        var first = project.Channel("A12", "7");
        var second = project.Channel("A12", "8");

        Apply(project, new AssignIo(first.Id, first.FunctionId, null, "%Q*", null));
        Apply(project, new AssignIo(second.Id, second.FunctionId, null, "%Q*", null));
        Assert.Empty(Run(project, "erc.io_cift"));

        Apply(project, new AssignIo(first.Id, first.FunctionId, null, "%QX0.6", null));
        Apply(project, new AssignIo(second.Id, second.FunctionId, null, "%QX0.6", null));
        Assert.Contains(Run(project, "erc.io_cift"), finding => finding.Message.Contains("%QX0.6"));
    }

    // ---- yetim

    [Fact]
    public void Device_without_functions_and_function_without_points_are_orphans()
    {
        var empty = new Device(DeviceId.New(), "U1", []);
        var hollow = new Device(DeviceId.New(), "U2", [new Function(FunctionId.New(), FunctionKind.Generic, [])]);
        var project = Build.Project([empty, hollow]);

        var findings = Run(project, "erc.yetim");

        Assert.Equal(2, findings.Count);
        Assert.Equal([empty.Id.Value], findings[0].Targets);
        Assert.Equal([hollow.Functions[0].Id.Value], findings[1].Targets);
    }

    [Fact]
    public void Cable_core_that_no_connection_uses_is_an_orphan()
    {
        var project = TestData.RefMachine();
        Apply(project, new Disconnect(project.Connection("X1", "1.b", "M1", "U1").Id));

        var finding = Assert.Single(Run(project, "erc.yetim"));

        Assert.Contains("-W1 kablosunun 1 damarı", finding.Message);
    }

    private sealed class AlwaysComplains : IErcRule
    {
        public string Id => "test.kural";

        public IEnumerable<ErcFinding> Check(ErcContext context) =>
            [new ErcFinding(Id, Severity.Info, "her zaman", [])];
    }
}
