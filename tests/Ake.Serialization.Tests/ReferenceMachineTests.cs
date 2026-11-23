using Ake.Core.Model;

namespace Ake.Serialization.Tests;

/// <summary>
/// Faz 1 DoD: examples/ref-machine yüklenip kaydedilebiliyor ve net hesabı doğru.
/// Beklenen değerler devreden elle çıkarılmıştır, dosyadan türetilmemiştir.
/// </summary>
public class ReferenceMachineTests
{
    private static readonly string FilePath = Path.Combine(
        FindRepositoryRoot(), "examples", "ref-machine", "ref-machine.json");

    private static readonly Project Machine = Load();

    private static readonly Connectivity Connectivity = Connectivity.Compute(Machine);

    [Fact]
    public void Contains_the_whole_machine()
    {
        Assert.Equal(19, Machine.Devices.Count);
        Assert.Equal(68, Machine.Devices.Sum(device => device.Functions.Count));
        Assert.Equal(127, AllPoints().Count());
        Assert.Equal(65, Machine.Connections.Count);
        Assert.Equal(2, Machine.Cables.Count);
        Assert.Equal(7, Machine.PotentialSources.Count);
        Assert.Equal(5, Machine.Pages.Count);
        Assert.Equal(68, Machine.Placements.Count);
    }

    [Fact]
    public void Has_two_motors_each_behind_a_breaker_and_a_contactor()
    {
        foreach (var n in new[] { 1, 2 })
        {
            Assert.Equal($"=M{n}+S1-M{n}", Designation($"M{n}"));
            Assert.Equal($"=M{n}+P1-K{n}", Designation($"K{n}"));
            Assert.Equal($"=M{n}+P1-F{n}", Designation($"F{n}"));
            Assert.Equal("LC1D09BD", Device($"K{n}").Part!.OrderCode);
        }
    }

    [Fact]
    public void Contactor_has_coil_power_and_auxiliary_contacts()
    {
        var kinds = Device("K1").Functions.Select(function => function.Kind).ToList();

        Assert.Equal(
            [
                FunctionKind.Coil,
                FunctionKind.PowerContact,
                FunctionKind.PowerContact,
                FunctionKind.PowerContact,
                FunctionKind.NoContact,
                FunctionKind.NcContact,
            ],
            kinds);
        Assert.Equal(MountingType.DinRail35, Device("K1").Mounting!.Type);
    }

    [Fact]
    public void Plc_has_sixteen_channels_and_nine_are_assigned()
    {
        var controller = Assert.Single(Machine.Controllers);
        var network = Assert.Single(controller.Networks);
        var channels = network.Nodes.SelectMany(node => node.Modules).SelectMany(module => module.Channels).ToList();

        Assert.Equal(NetworkKind.EtherCat, network.Kind);
        Assert.Equal(16, channels.Count);
        Assert.Equal(8, channels.Count(channel => channel.SignalType == SignalType.DI));
        Assert.Equal(8, channels.Count(channel => channel.SignalType == SignalType.DO));
        Assert.Equal(9, channels.Count(channel => channel.SymbolName is not null));
        Assert.All(channels, channel => Assert.NotNull(channel.FunctionId));
    }

    [Fact]
    public void Named_potentials_are_exactly_the_seven_supplies()
    {
        var names = Connectivity.Potentials
            .Where(potential => potential.Name is not null)
            .Select(potential => potential.Name!)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["0V", "24VDC", "L1", "L2", "L3", "N", "PE"], names);
        Assert.DoesNotContain(Connectivity.Potentials, potential => potential.HasConflict);
    }

    [Fact]
    public void Phase_potential_ends_at_the_main_switch()
    {
        var l1 = Connectivity.PotentialOf(Point("X0", "L1.a"));

        Assert.Equal("L1", l1.Name);
        AssertPoints(l1, "-X0:L1.a", "-X0:L1.b", "-Q1:1");
        Assert.Equal(2, l1.Nets.Count);

        var afterSwitch = Connectivity.PotentialOf(Point("Q1", "2"));
        Assert.Null(afterSwitch.Name);
        AssertPoints(afterSwitch, "-Q1:2", "-F1:1", "-F2:1", "-F3:1");
    }

    [Fact]
    public void Control_voltage_reaches_every_consumer_as_one_net()
    {
        var plus = Connectivity.PotentialOf(Point("T1", "+"));

        Assert.Equal("24VDC", plus.Name);
        Assert.Single(plus.Nets);
        AssertPoints(
            plus,
            "-T1:+", "-A1:24V", "-A10:24V", "-K0:A1", "-S1:13", "-S2:11", "-K1:13", "-K2:13", "-F1:13", "-F2:13", "-K0:33");

        var minus = Connectivity.PotentialOf(Point("T1", "-"));
        Assert.Equal("0V", minus.Name);
        AssertPoints(minus, "-T1:-", "-A1:0V", "-A10:0V", "-K0:A2", "-K1:A2", "-K2:A2");
    }

    [Fact]
    public void Protective_earth_passes_through_terminals_to_both_motors()
    {
        var pe = Connectivity.PotentialOf(Point("M2", "PE"));

        Assert.Equal("PE", pe.Name);
        Assert.Equal(4, pe.Nets.Count);
        AssertPoints(
            pe,
            "-X0:PE.a", "-X0:PE.b", "-X1:4.a", "-X1:4.b", "-X1:8.a", "-X1:8.b", "-M1:PE", "-M2:PE", "-T1:PE");
    }

    [Fact]
    public void Motor_phase_passes_through_its_terminal_but_stops_at_the_contactor()
    {
        var motorPhase = Connectivity.PotentialOf(Point("M1", "U1"));

        Assert.Null(motorPhase.Name);
        Assert.Equal(2, motorPhase.Nets.Count);
        AssertPoints(motorPhase, "-K1:2", "-X1:1.a", "-X1:1.b", "-M1:U1");
    }

    [Fact]
    public void Contactor_coil_is_switched_by_the_safety_relay_and_not_tied_to_the_supply()
    {
        var coil = Connectivity.PotentialOf(Point("K1", "A1"));

        AssertPoints(coil, "-K0:14", "-K1:A1");
        Assert.NotSame(coil, Connectivity.PotentialOf(Point("T1", "+")));
        AssertPoints(Connectivity.NetOf(Point("A12", "1")), "-A12:1", "-K0:13");
    }

    [Fact]
    public void Unused_points_stay_unconnected()
    {
        foreach (var (tag, point) in new[] { ("A11", "8"), ("A12", "3"), ("A12", "8"), ("K1", "21"), ("K2", "22") })
        {
            Assert.Single(Connectivity.NetOf(Point(tag, point)).Points);
        }
    }

    [Fact]
    public void Motor_cables_carry_one_connection_per_core()
    {
        var coreIds = Machine.Cables.SelectMany(cable => cable.Cores).Select(core => core.Id).ToHashSet();
        var viaCable = Machine.Connections.Where(connection => connection.CoreId is not null).ToList();

        Assert.Equal(8, coreIds.Count);
        Assert.Equal(8, viaCable.Count);
        Assert.All(viaCable, connection => Assert.Null(connection.Wire));
        Assert.Equal(56, Machine.Connections.Count(connection => connection.Wire is not null));
    }

    [Fact]
    public void Saving_reproduces_the_file_exactly()
    {
        var onDisk = File.ReadAllText(FilePath).ReplaceLineEndings("\n");

        Assert.Equal(onDisk, ProjectJson.Serialize(Machine));
    }

    [Fact]
    public void Saved_copy_loads_back_to_the_same_content()
    {
        var directory = Directory.CreateTempSubdirectory("ake-test-");
        try
        {
            var copyPath = Path.Combine(directory.FullName, "kopya.json");

            var saved = ProjectFile.Save(Machine, copyPath);
            Assert.True(saved.IsSuccess, saved.Error?.Message);

            var reloaded = ProjectFile.Load(copyPath);
            Assert.True(reloaded.IsSuccess, reloaded.Error?.Message);
            Assert.Equal(ProjectJson.Serialize(Machine), ProjectJson.Serialize(reloaded.Value));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Loading_a_missing_file_fails_without_throwing()
    {
        var result = ProjectFile.Load(Path.Combine(Path.GetTempPath(), $"ake-yok-{Guid.NewGuid()}.json"));

        Assert.Equal("proje.dosya_okunamadi", result.Error!.Code);
    }

    private static Project Load()
    {
        var result = ProjectFile.Load(FilePath);
        Assert.True(result.IsSuccess, result.Error?.Message);
        return result.Value;
    }

    private static IEnumerable<ConnectionPoint> AllPoints() =>
        Machine.Devices.SelectMany(device => device.Functions).SelectMany(function => function.ConnectionPoints);

    private static Device Device(string tag) => Machine.Devices.Single(device => device.ProductTag == tag);

    private static string Designation(string tag) => Machine.DesignationOf(Device(tag)).ToString();

    private static ConnectionPointId Point(string tag, string name) =>
        Device(tag).Functions.SelectMany(function => function.ConnectionPoints).Single(point => point.Name == name).Id;

    /// <summary>Noktaları yapı düğümleri olmadan, <c>-Tag:Nokta</c> biçiminde karşılaştırır.</summary>
    private static void AssertPoints(Potential potential, params string[] expected) =>
        AssertPoints(potential.Points, expected);

    private static void AssertPoints(Net net, params string[] expected) => AssertPoints(net.Points, expected);

    private static void AssertPoints(IEnumerable<ConnectionPointId> points, string[] expected)
    {
        var actual = points
            .Select(point => Machine.Locate(point)!)
            .Select(location => $"-{location.Device.ProductTag}:{location.Point.Name}")
            .Order(StringComparer.Ordinal);

        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Depo kökü bulunamadı (Directory.Build.props yok).");
    }

    [Fact]
    public void A_wire_junction_survives_save_and_load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        var wire = project.Connections.First(c => c.Wire?.Number is not null && c.CoreId is null);
        var split = Ake.Core.Commands.Junctions.Split(project, wire.Id, project.Pages[0].Id, 50, 70).Value;
        Assert.True(new Ake.Core.Commands.CommandStack(project).Execute(split.Command, Ake.Core.Commands.ChangeSource.User, "test").IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json);

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(json, ProjectJson.Serialize(loaded.Value));
        Assert.Equal(Ake.Core.Model.DeviceKind.Junction, loaded.Value.Devices.Single(device => device.ProductTag == "J1").Kind);
        Assert.Equal(project.Connections.Count, loaded.Value.Connections.Count);
    }

    [Fact]
    public void Point_and_symbol_pin_attributes_survive_save_and_load_and_default_values_are_not_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        var plain = ProjectJson.Serialize(project);
        Assert.DoesNotContain("connector", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("deviceName", plain, StringComparison.Ordinal);

        var attributes = new Ake.Core.Model.PointAttributes(
            Ake.Core.Model.PinKind.Communication, Ake.Core.Model.ConnectorType.Rj45, Ake.Core.Model.ContactType.Changeover, "Cat6");
        var point = project.Devices[0].Functions[0].ConnectionPoints[0];
        Assert.True(new Ake.Core.Commands.SetPointAttributes(point.Id, attributes).Execute(project).IsSuccess);
        var symbol = new Ake.Core.Model.SymbolDefinition(
            Ake.Core.Model.SymbolId.New(),
            "Sensör",
            [new Ake.Core.Model.SymbolLine(0, 0, 0, 10)],
            [new Ake.Core.Model.SymbolPin(0, 0, 0, -1, "OUT", attributes), new Ake.Core.Model.SymbolPin(0, 10, 0, 1)],
            DeviceName: "Endüktif sensör",
            TagLetter: "B");
        Assert.True(new Ake.Core.Commands.PutSymbol(symbol).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal(attributes, loaded.Devices[0].Functions[0].ConnectionPoints[0].Attributes);
        var back = Assert.Single(loaded.Symbols);
        Assert.Equal(symbol.Pins, back.Pins);
        Assert.Equal(("Endüktif sensör", "B"), (back.DeviceName, back.TagLetter));
    }

    [Fact]
    public void Terminal_levels_survive_save_and_load_and_level_one_is_not_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        var plain = ProjectJson.Serialize(project);
        Assert.DoesNotContain("\"level\"", plain, StringComparison.Ordinal);

        var strip = project.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip);
        var second = Ake.Core.Commands.AddTerminal.Create(strip.Functions[0].Description!, 2);
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 1, second).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        var back = loaded.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip).Functions[1];
        Assert.Equal(2, back.Level);
        Assert.Equal(second.ConnectionPoints.Select(point => point.Name), back.ConnectionPoints.Select(point => point.Name));
    }

    [Fact]
    public void Connection_kinds_and_assignments_survive_save_and_load_and_defaults_are_not_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        Assert.DoesNotContain("connectionKinds", ProjectJson.Serialize(project), StringComparison.Ordinal);

        var kind = new Ake.Core.Model.ConnectionKind(
            Ake.Core.Model.ConnectionKindId.New(), "DriveCLiQ", "#808080", Ake.Core.Model.LineStyle.Dashed, Ake.Core.Model.PinKind.Communication);
        var plain = new Ake.Core.Model.ConnectionKind(Ake.Core.Model.ConnectionKindId.New(), "Düz", "#112233");
        Assert.True(new Ake.Core.Commands.PutConnectionKind(kind).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.PutConnectionKind(plain).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.SetConnectionKind(project.Connections[0].Id, kind.Id).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal([kind, plain], loaded.ConnectionKinds);
        Assert.Equal(kind.Id, loaded.Connections[0].KindId);
        Assert.Null(loaded.Connections[1].KindId);
        Assert.Contains("\"style\": \"Dashed\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"style\": \"Solid\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_device_address_survives_save_and_load_and_is_not_written_when_empty()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        Assert.DoesNotContain("\"address\"", ProjectJson.Serialize(project), StringComparison.Ordinal);

        Assert.True(new Ake.Core.Commands.SetDeviceAddress(project.Devices[0].Id, "192.168.0.50").Execute(project).IsSuccess);
        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal("192.168.0.50", loaded.Devices[0].Address);
        Assert.Contains("\"address\": \"192.168.0.50\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void A_communication_page_kind_survives_save_and_load_and_schematic_pages_do_not_write_it()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        Assert.DoesNotContain("\"kind\": \"CommunicationDiagram\"", ProjectJson.Serialize(project), StringComparison.Ordinal);

        var page = new Ake.Core.Model.Page(
            Ake.Core.Model.PageId.New(), "9", "Haberleşme şeması", templateId: project.SheetTemplates[0].Id, kind: Ake.Core.Model.PageKind.CommunicationDiagram);
        Assert.True(new Ake.Core.Commands.AddPage(page).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal(Ake.Core.Model.PageKind.CommunicationDiagram, loaded.Pages[^1].Kind);
        Assert.Equal(Ake.Core.Model.PageKind.Schematic, loaded.Pages[0].Kind);
    }

    [Fact]
    public void A_single_line_page_kind_survives_save_and_load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        var page = new Ake.Core.Model.Page(
            Ake.Core.Model.PageId.New(), "9", "Tek hat şeması", templateId: project.SheetTemplates[0].Id, kind: Ake.Core.Model.PageKind.SingleLineDiagram);
        Assert.True(new Ake.Core.Commands.AddPage(page).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal(Ake.Core.Model.PageKind.SingleLineDiagram, loaded.Pages[^1].Kind);
        Assert.Contains("\"kind\": \"SingleLineDiagram\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Terminal_role_and_multi_way_points_survive_save_and_load_and_defaults_are_not_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        Assert.DoesNotContain("\"role\": \"ProtectiveEarth\"", ProjectJson.Serialize(project), StringComparison.Ordinal);
        Assert.DoesNotContain("\"role\": \"EndCover\"", ProjectJson.Serialize(project), StringComparison.Ordinal);

        var strip = project.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip);
        var pe = Ake.Core.Commands.AddTerminal.Create("PE9", ways: 2, role: Ake.Core.Model.TerminalRole.ProtectiveEarth);
        var cover = Ake.Core.Commands.AddTerminal.Create("SL", role: Ake.Core.Model.TerminalRole.EndCover);
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 0, pe).Execute(project).IsSuccess);
        Assert.True(new Ake.Core.Commands.AddTerminal(strip.Id, 1, cover).Execute(project).IsSuccess);

        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Contains("\"role\": \"ProtectiveEarth\"", json, StringComparison.Ordinal);
        var back = loaded.Devices.First(device => device.Kind == Ake.Core.Model.DeviceKind.TerminalStrip).Functions;
        Assert.Equal((Ake.Core.Model.TerminalRole.ProtectiveEarth, 2, 4), (back[0].Role, back[0].Ways, back[0].ConnectionPoints.Count));
        Assert.Equal((Ake.Core.Model.TerminalRole.EndCover, 0), (back[1].Role, back[1].ConnectionPoints.Count));
    }

    [Fact]
    public void Wire_numbering_settings_survive_save_and_load_and_defaults_are_not_written()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json");
        var project = ProjectFile.Load(path).Value;
        Assert.DoesNotContain("wireNumbering", ProjectJson.Serialize(project), StringComparison.Ordinal);

        var settings = new Ake.Core.Model.WireNumberSettings("W", 100, 2, 4, Ake.Core.Numbering.WireNumberingScheme.Sequential);
        Assert.True(new Ake.Core.Commands.SetWireNumbering(settings).Execute(project).IsSuccess);
        var json = ProjectJson.Serialize(project);
        var loaded = ProjectJson.Deserialize(json).Value;

        Assert.Equal(json, ProjectJson.Serialize(loaded));
        Assert.Equal(settings, loaded.WireNumbering);
        Assert.Contains("\"prefix\": \"W\"", json, StringComparison.Ordinal);
        // Yalnızca değişen alanlar yazılır.
        Assert.True(new Ake.Core.Commands.SetWireNumbering(new Ake.Core.Model.WireNumberSettings("X")).Execute(project).IsSuccess);
        var onlyPrefix = ProjectJson.Serialize(project);
        Assert.DoesNotContain("\"start\"", onlyPrefix, StringComparison.Ordinal);
        Assert.Equal("X", ProjectJson.Deserialize(onlyPrefix).Value.WireNumbering.Prefix);
    }
}
