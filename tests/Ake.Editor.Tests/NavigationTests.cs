using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Editor.Tests;

/// <summary>Navigator'lar, mesaj paneli ve özellik penceresinin oturum tarafı; geçici kopya üzerinde.</summary>
public sealed class NavigationTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-nav-");
    private readonly EditorSession _session = new("deneme");

    public NavigationTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private Device Device(string tag) => _session.Project!.Devices.Single(device => device.ProductTag == tag);

    [Fact]
    public void Device_navigator_lists_every_device_by_full_tag_with_its_page()
    {
        var rows = _session.DeviceRows();

        Assert.Equal(19, rows.Count);
        Assert.Equal(rows.Select(row => row.Label).Order(StringComparer.Ordinal), rows.Select(row => row.Label));
        var motor = Assert.Single(rows, row => row.Label.StartsWith("=M1+S1-M1", StringComparison.Ordinal));
        Assert.EndsWith("sayfa 2", motor.Label);
        Assert.Equal(1, motor.PageIndex);
        Assert.NotNull(motor.Placement);
    }

    [Fact]
    public void Device_without_any_placement_is_marked_as_not_placed()
    {
        var spare = new Device(
            DeviceId.New(),
            "K9",
            [new Function(FunctionId.New(), FunctionKind.Coil, [new ConnectionPoint(ConnectionPointId.New(), "A1")])]);
        Assert.True(new AddDevice(spare).Execute(_session.Project!).IsSuccess);

        var row = Assert.Single(_session.DeviceRows(), row => row.Label.StartsWith("-K9", StringComparison.Ordinal));

        Assert.EndsWith("yerleştirilmemiş", row.Label);
        Assert.Null(row.PageIndex);
    }

    [Fact]
    public void Potential_navigator_lists_the_named_potentials()
    {
        var names = _session.PotentialRows().Select(row => row.Label.Split("  ·  ")[0]).ToList();

        Assert.Equal(["0V", "24VDC", "L1", "L2", "L3", "N", "PE"], names);
        Assert.All(_session.PotentialRows(), row => Assert.NotNull(row.PageIndex));
    }

    [Fact]
    public void Clean_project_has_no_messages_and_a_finding_points_to_where_it_is_drawn()
    {
        Assert.Empty(_session.FindingRows());

        var plus = Device("T1").Functions[0].ConnectionPoints.Single(point => point.Name == "+");
        var minus = Device("T1").Functions[0].ConnectionPoints.Single(point => point.Name == "-");
        Assert.True(_session.Project!.CheckIntegrity().IsSuccess);
        new Connect(new Connection(ConnectionId.New(), plus.Id, minus.Id)).Execute(_session.Project!);
        var reopened = new EditorSession();
        var path = Path.Combine(_directory.FullName, "kisa-devre.json");
        Ake.Serialization.ProjectFile.Save(_session.Project!, path);
        reopened.Open(path);

        var finding = Assert.Single(reopened.FindingRows());

        Assert.StartsWith("Hata  ·  Kısa devre", finding.Label);
        Assert.Equal(2, finding.PageIndex);
    }

    [Fact]
    public void Locate_finds_the_page_of_devices_points_connections_and_channels()
    {
        var project = _session.Project!;
        var coil = Device("K1").Functions.Single(function => function.Kind == FunctionKind.Coil);
        var wire = project.Connections.First(connection => connection.From == coil.ConnectionPoints[0].Id || connection.To == coil.ConnectionPoints[0].Id);
        var channel = project.Channels.First(candidate => candidate.SymbolName == "bStartButton");

        Assert.Equal(1, _session.Locate(Device("K1").Id.Value)!.Value.PageIndex);
        Assert.Equal(1, _session.Locate(coil.ConnectionPoints[1].Id.Value)!.Value.PageIndex);
        Assert.NotNull(_session.Locate(wire.Id.Value));
        Assert.Equal(2, _session.Locate(channel.Id.Value)!.Value.PageIndex);
        Assert.Null(_session.Locate(Guid.NewGuid()));
    }

    [Fact]
    public void A_cross_reference_anchor_leads_to_its_page_and_a_foreign_one_to_nothing()
    {
        for (var i = 0; i < _session.Project!.Pages.Count; i++)
        {
            Assert.Equal(i, _session.PageIndexOfAnchor(Ake.Render.SheetRenderer.AnchorOf(_session.Project.Pages[i])));
        }

        Assert.Null(_session.PageIndexOfAnchor("sayfa-yok"));
        var scene = _session.SceneFor(0, new DateOnly(2026, 11, 17)).Value;
        Assert.All(
            scene.Items.OfType<Ake.Render.LinkItem>(),
            link => Assert.NotNull(_session.PageIndexOfAnchor(link.Target)));
    }

    [Fact]
    public void Properties_show_the_device_behind_a_symbol()
    {
        var coil = Device("K1").Functions.Single(function => function.Kind == FunctionKind.Coil);
        var placement = _session.Project!.Placements.Single(candidate => candidate.FunctionId == coil.Id);

        var properties = _session.PropertiesOf(placement.Id)!;

        Assert.Equal("=M1+P1-K1", properties.Designation);
        Assert.Equal("K1", properties.ProductTag);
        Assert.Equal("Schneider Electric LC1D09BD", properties.Part);
        Assert.Contains("Coil: A1, A2  (Bobin)", properties.Functions);
        Assert.Null(_session.PropertiesOf(PlacementId.New()));
    }

    [Fact]
    public void Renaming_from_the_properties_window_is_one_undoable_step()
    {
        var device = Device("K1");

        Assert.True(_session.Rename(device.Id, "  K7 ").IsSuccess);
        Assert.Equal("K7", device.ProductTag);
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal("K1", device.ProductTag);
    }

    [Fact]
    public void Block_editor_starts_from_a_copy_of_the_built_in_symbol_and_saves_with_assignment()
    {
        var coil = Device("K1").Functions.Single(function => function.Kind == FunctionKind.Coil);

        var start = _session.StartingSymbolFor(coil)!;
        Assert.Equal(2, start.Pins.Count);
        Assert.Contains(start.Shapes, shape => shape is SymbolRect);
        Assert.Null(start.Check());

        Assert.True(_session.SaveSymbol(start, coil.Id).IsSuccess);
        Assert.Equal(start.Id, coil.SymbolId);
        Assert.Same(start, _session.StartingSymbolFor(coil));

        Assert.True(_session.Undo().IsSuccess);
        Assert.Null(coil.SymbolId);
        Assert.Empty(_session.Project!.Symbols);
    }

    [Fact]
    public void Saving_a_symbol_that_does_not_fit_the_function_changes_nothing()
    {
        var coil = Device("K1").Functions.Single(function => function.Kind == FunctionKind.Coil);
        var onePin = new SymbolDefinition(SymbolId.New(), "Tek uç", [new SymbolLine(0, 0, 0, 5)], [new SymbolPin(0, 0, 0, -1)]);

        Assert.Equal("sembol.uc_uyumsuz", _session.SaveSymbol(onePin, coil.Id).Error!.Code);
        Assert.Empty(_session.Project!.Symbols);
        Assert.False(_session.CanUndo);

        Assert.True(_session.SaveSymbol(onePin, null).IsSuccess);
        Assert.Single(_session.Project.Symbols);
        Assert.Null(_session.StartingSymbolFor(null));
    }

    [Fact]
    public void Renaming_to_the_same_tag_leaves_no_history_and_an_invalid_tag_is_rejected()
    {
        var device = Device("K1");

        Assert.True(_session.Rename(device.Id, "K1").IsSuccess);
        Assert.False(_session.CanUndo);

        Assert.Equal("tag.gecersiz", _session.Rename(device.Id, "K 1").Error!.Code);
        Assert.Equal("device.yok", _session.Rename(DeviceId.New(), "K2").Error!.Code);
        Assert.Equal("K1", device.ProductTag);
    }
}
