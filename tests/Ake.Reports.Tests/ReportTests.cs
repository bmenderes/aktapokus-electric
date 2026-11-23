using System.Globalization;
using Ake.Core.Commands;
using Ake.Core.Model;
using ClosedXML.Excel;

namespace Ake.Reports.Tests;

public class ReportTests
{
    private static ReportTable Table(params object?[][] rows) => new(
        "test",
        "Test",
        [new("a", "A"), new("b", "B"), new("c", "C")],
        [.. rows.Select(row => (IReadOnlyList<object?>)row)]);

    private static object? Cell(ReportTable table, int row, string key) =>
        table.Rows[row][table.Columns.Select((column, i) => (column, i)).Single(x => x.column.Key == key).i];

    // ---- CSV

    [Fact]
    public void Csv_quotes_only_what_needs_quoting()
    {
        var csv = CsvReport.Write(Table(["düz", "a, b", "tırnak \"içinde\""], [null, "iki\nsatır", 3]));

        Assert.Equal("A,B,C\ndüz,\"a, b\",\"tırnak \"\"içinde\"\"\"\n,\"iki\nsatır\",3\n", csv);
    }

    [Fact]
    public void Csv_writes_decimals_with_a_point_whatever_the_machine_culture_is()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            Assert.Equal("A,B,C\n2.5,0.75,10\n", CsvReport.Write(Table([2.5m, 0.75m, 10])));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Csv_separator_can_be_changed()
    {
        Assert.Equal("A;B;C\n1,5;\"x;y\";\n", CsvReport.Write(Table(["1,5", "x;y", null]), ';'));
    }

    // ---- şablon

    [Fact]
    public void Template_selects_reorders_and_renames_columns()
    {
        var template = ReportTemplate.Parse(
            """{ "title": "Özet", "columns": [ { "key": "c", "header": "Üçüncü" }, "a" ] }""").Value;

        var shaped = Table([1, 2, 3]).Apply(template).Value;

        Assert.Equal("Özet", shaped.Title);
        Assert.Equal(["Üçüncü", "A"], shaped.Columns.Select(column => column.Header));
        Assert.Equal([3, 1], shaped.Rows[0]);
    }

    [Fact]
    public void Template_with_an_unknown_column_is_rejected_and_names_the_valid_ones()
    {
        var result = Table().Apply(new ReportTemplate([new TemplateColumn("yok")]));

        Assert.Equal("rapor.kolon_yok", result.Error!.Code);
        Assert.Contains("a, b, c", result.Error.Message);
    }

    [Theory]
    [InlineData("bozuk")]
    [InlineData("""{ "title": "x" }""")]
    [InlineData("""{ "columns": [] }""")]
    [InlineData("""{ "columns": [ 5 ] }""")]
    [InlineData("""{ "columns": [ { "header": "anahtar yok" } ] }""")]
    public void Malformed_templates_are_rejected(string json)
    {
        Assert.Equal("rapor.sablon_gecersiz", ReportTemplate.Parse(json).Error!.Code);
    }

    [Fact]
    public void Example_io_template_produces_the_four_column_layout()
    {
        var machine = ReferenceMachineReportTests.Machine();
        var templatePath = Path.Combine(
            ReferenceMachineReportTests.RepositoryRoot(), "examples", "ref-machine", "templates", "io-klemens-nr-isim-kod.json");

        var shaped = ReportCatalog.Io(machine).Apply(ReportTemplate.Parse(File.ReadAllText(templatePath)).Value).Value;

        Assert.Equal(["Modül", "Klemens", "Nr", "İsim", "Kod"], shaped.Columns.Select(column => column.Header));
        Assert.Equal(16, shaped.Rows.Count);
    }

    // ---- içerik

    [Fact]
    public void Bom_groups_identical_parts_and_lists_devices_without_part_data_one_by_one()
    {
        var bom = ReportCatalog.Bom(ReferenceMachineReportTests.Machine());

        var contactors = bom.Rows.Single(row => (string?)row[3] == "LC1D09BD");
        Assert.Equal(2, contactors[1]);
        Assert.Equal("=M1+P1-K1, =M2+P1-K2", contactors[6]);

        var withoutPart = bom.Rows.Where(row => row[2] is null && (string?)row[5] != "Kablo").ToList();
        Assert.Equal(11, withoutPart.Count);
        Assert.All(withoutPart, row => Assert.Equal(1, row[1]));

        Assert.Equal(Enumerable.Range(1, bom.Rows.Count).Cast<object?>(), bom.Rows.Select(row => row[0]));
        Assert.Equal(21, bom.Rows.Sum(row => (int)row[1]!));
    }

    [Fact]
    public void Cable_list_shows_an_unused_core_with_empty_ends()
    {
        var machine = ReferenceMachineReportTests.Machine();
        var viaCable = machine.Connections.First(connection => connection.CoreId is not null);
        Assert.True(new Disconnect(viaCable.Id).Execute(machine).IsSuccess);

        var cables = ReportCatalog.Cables(machine);

        Assert.Equal(8, cables.Rows.Count);
        Assert.Null(Cell(cables, 0, "from"));
        Assert.Null(Cell(cables, 0, "to"));
        Assert.NotNull(Cell(cables, 1, "from"));
    }

    [Fact]
    public void Wire_list_is_sorted_by_number_as_numbers_not_as_text()
    {
        var wires = ReportCatalog.Wires(ReferenceMachineReportTests.Machine());

        var numbers = wires.Rows.Select(row => (string?)row[0]).TakeWhile(number => number is not null).ToList();

        Assert.Equal(Enumerable.Range(1, 56).Select(n => n.ToString(CultureInfo.InvariantCulture)), numbers);
        Assert.Equal(65, wires.Rows.Count);
        Assert.Equal("Köprü", wires.Rows.Single(row => (string?)row[1] == "Köprü")[1]);
    }

    [Fact]
    public void Terminal_plan_separates_inner_side_outer_side_and_jumpers()
    {
        var terminals = ReportCatalog.Terminals(ReferenceMachineReportTests.Machine());
        var pe = terminals.Rows.Single(row => (string?)row[0] == "=G1+P1-X1" && (string?)row[1] == "4");

        Assert.Equal("=G1+P1-X0:PE.a", pe[2]);
        Assert.Equal("=M1+S1-M1:PE", pe[4]);
        Assert.Equal("-W1:GNYE", pe[5]);
        Assert.Equal("8", pe[6]);
        Assert.Equal("PE", pe[7]);
        Assert.Equal(13, terminals.Rows.Count);
    }

    [Fact]
    public void Io_list_finds_the_terminal_a_signal_passes_and_the_field_device_it_reaches()
    {
        static Function Fn(FunctionKind kind, bool passThrough, string? description, params string[] points) => new(
            FunctionId.New(),
            kind,
            points.Select(name => new ConnectionPoint(ConnectionPointId.New(), name)),
            passThrough,
            description);

        var sensor = new Device(DeviceId.New(), "B1", [Fn(FunctionKind.Generic, false, null, "OUT")]);
        var strip = new Device(
            DeviceId.New(), "X2", [Fn(FunctionKind.Terminal, true, "3", "3.a", "3.b")], DeviceKind.TerminalStrip);
        var card = new Device(DeviceId.New(), "A5", [Fn(FunctionKind.PlcChannel, false, null, "1")]);
        var cpu = new Device(DeviceId.New(), "A1", []);

        ConnectionPointId Point(Device device, string name) =>
            device.Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

        var controller = new Controller(
            ControllerId.New(),
            cpu.Id,
            "LMC",
            [
                new Network(
                    NetworkId.New(),
                    NetworkKind.LocalBus,
                    "Yerel",
                    [
                        new Node(
                            NodeId.New(),
                            cpu.Id,
                            "Şasi",
                            0,
                            [new Module(ModuleId.New(), card.Id, 1, [new Channel(ChannelId.New(), 0, SignalType.DI, card.Functions[0].Id, symbolName: "Di_BoxSen1")])]),
                    ]),
            ]);
        var project = Project.Create(
            ProjectId.New(),
            "test",
            devices: [sensor, strip, card, cpu],
            connections:
            [
                new Connection(ConnectionId.New(), Point(card, "1"), Point(strip, "3.a")),
                new Connection(ConnectionId.New(), Point(strip, "3.b"), Point(sensor, "OUT")),
            ],
            controllers: [controller]).Value;

        var io = ReportCatalog.Io(project);

        Assert.Equal("DI0", Cell(io, 0, "channel"));
        Assert.Equal("Di_BoxSen1", Cell(io, 0, "symbol"));
        Assert.Equal("-X2:3", Cell(io, 0, "terminal"));
        Assert.Equal("-B1:OUT", Cell(io, 0, "target"));
        Assert.Equal("-A5", Cell(io, 0, "module"));
    }

    // ---- XLSX

    [Fact]
    public void Xlsx_holds_one_sheet_per_report_with_typed_cells()
    {
        var machine = ReferenceMachineReportTests.Machine();
        var tables = ReportCatalog.Names.Select(name => ReportCatalog.Build(name, machine).Value).ToList();
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "raporlar.xlsx");

            var saved = XlsxReport.Save(tables, path);
            Assert.True(saved.IsSuccess, saved.Error?.Message);

            using var workbook = new XLWorkbook(path);
            Assert.Equal(ReportCatalog.Names, workbook.Worksheets.Select(sheet => sheet.Name));

            var bom = workbook.Worksheet("bom");
            Assert.Equal("Poz", bom.Cell(1, 1).GetString());
            Assert.True(bom.Cell(1, 1).Style.Font.Bold);
            Assert.Equal(XLDataType.Number, bom.Cell(2, 2).DataType);
            Assert.Equal(tables[0].Rows.Count + 1, bom.LastRowUsed()!.RowNumber());

            var terminals = workbook.Worksheet("terminals");
            Assert.Equal(XLDataType.Text, terminals.Cell(7, 2).DataType);
            Assert.Equal("1", terminals.Cell(7, 2).GetString());

            var cables = workbook.Worksheet("cables");
            Assert.Equal(1.5, cables.Cell(2, 4).GetDouble());
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Saving_to_an_unwritable_path_fails_without_throwing()
    {
        // Hedef klasörün yerinde bir dosya var; klasör oluşturulamaz, dosya yazılamaz.
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var blocker = Path.Combine(directory.FullName, "engel");
            File.WriteAllText(blocker, string.Empty);

            var csv = CsvReport.Save(Table(), Path.Combine(blocker, "rapor.csv"));
            var xlsx = XlsxReport.Save([Table()], Path.Combine(blocker, "rapor.xlsx"));

            Assert.Equal("rapor.dosya_yazilamadi", csv.Error?.Code);
            Assert.Equal("rapor.dosya_yazilamadi", xlsx.Error?.Code);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_wire_junction_is_not_a_bill_of_materials_row()
    {
        var project = ReferenceMachineReportTests.Machine();
        var before = ReportCatalog.Bom(project).Rows.Count;
        var wire = project.Connections.First(c => c.Wire?.Number is not null && c.CoreId is null);
        var split = Ake.Core.Commands.Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        Assert.True(new Ake.Core.Commands.CommandStack(project).Execute(split.Command, Ake.Core.Commands.ChangeSource.User, "test").IsSuccess);

        Assert.Equal(before, ReportCatalog.Bom(project).Rows.Count);
    }

    [Fact]
    public void The_terminal_plan_names_the_level_of_a_multi_level_terminal()
    {
        var project = ReferenceMachineReportTests.Machine();
        var strip = project.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip);
        var name = strip.Functions[0].Description!;
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 1, Ake.Core.Commands.AddTerminal.Create(name, 2)).Execute(project).IsSuccess);

        var rows = ReportCatalog.Terminals(project).Rows;

        Assert.Contains(rows, row => (string?)row[1] == $"{name} (2. kat)");
        Assert.Contains(rows, row => (string?)row[1] == name);
    }

    [Fact]
    public void The_terminal_plan_marks_pe_and_end_covers_and_collects_every_connection_of_a_multi_way_side()
    {
        var project = ReferenceMachineReportTests.Machine();
        var strip = project.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip);
        var wide = Ake.Core.Commands.AddTerminal.Create("W", ways: 2, role: Ake.Core.Model.TerminalRole.ProtectiveEarth);
        var cover = Ake.Core.Commands.AddTerminal.Create("SL", role: Ake.Core.Model.TerminalRole.EndCover);
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 0, wide).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 1, cover).Execute(project).IsSuccess);
        var k1 = project.Devices.Single(device => device.ProductTag == "K1").Functions.First(function => function.Kind == Ake.Core.Model.FunctionKind.PowerContact);
        var m1 = project.Devices.Single(device => device.ProductTag == "M1").Functions[0];
        Assert.True(new Ake.Core.Commands.Connect(new Ake.Core.Model.Connection(
            Ake.Core.Model.ConnectionId.New(), wide.ConnectionPoints[0].Id, k1.ConnectionPoints[0].Id, new Ake.Core.Model.Wire(Ake.Core.Model.WireId.New(), "W1"))).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.Connect(new Ake.Core.Model.Connection(
            Ake.Core.Model.ConnectionId.New(), wide.ConnectionPoints[1].Id, m1.ConnectionPoints[0].Id, new Ake.Core.Model.Wire(Ake.Core.Model.WireId.New(), "W2"))).Execute(project).IsSuccess);

        var rows = ReportCatalog.Terminals(project).Rows;

        var peRow = rows.Single(row => (string?)row[1] == "W (PE)");
        Assert.Contains("W1", (string?)peRow[3]);
        Assert.Contains("W2", (string?)peRow[3]);
        Assert.Contains(rows, row => (string?)row[1] == "SL (sonlandırıcı)");
    }

    [Fact]
    public void Plc_cross_reference_report_joins_addresses_wires_terminals_and_sorts_both_ways()
    {
        var machine = ReferenceMachineReportTests.Machine();
        var ch0 = machine.Channels.ElementAt(0);
        var ch1 = machine.Channels.ElementAt(1);
        Assert.True(new Ake.Core.Commands.AssignIo(ch0.Id, ch0.FunctionId, ch0.SymbolName, "%IX1.0", ch0.Description).Execute(machine).IsSuccess);
        Assert.True(new Ake.Core.Commands.AssignIo(ch1.Id, ch1.FunctionId, ch1.SymbolName, "%IX0.0", ch1.Description).Execute(machine).IsSuccess);

        var byAddress = ReportCatalog.Build("plc-xref", machine).Value;
        var byWire = ReportCatalog.PlcCrossReference(machine, sortByWire: true);

        Assert.Equal(16, byAddress.Rows.Count);
        Assert.Equal(16, byWire.Rows.Count);
        Assert.Equal("%IX0.0", byAddress.Rows[0][0]);
        Assert.Equal("%IX1.0", byWire.Rows[0][0]);
        Assert.All(byAddress.Rows.Where(r => r[0] is not null), r => Assert.NotNull(r[3]));
        Assert.NotEqual(byAddress.Rows[0][0], byWire.Rows[0][0]);
    }
}
