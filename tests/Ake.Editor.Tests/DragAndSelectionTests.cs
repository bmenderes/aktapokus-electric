using Ake.Core.Model;
using Ake.Library;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>
/// Çoklu seçim, kopyala/yapıştır, tel çekme ve sürükle-bırakla yerleştirme, oturum üzerinden (ADR-0026).
/// Her test referans makinenin geçici bir kopyasıyla çalışır.
/// </summary>
public sealed class DragAndSelectionTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-drag-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;

    public DragAndSelectionTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        _original = File.ReadAllText(path);
        Assert.True(_session.Open(path).IsSuccess);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private Device Tagged(string tag) => _session.Project!.Devices.Single(device => device.ProductTag == tag);

    private List<PlacementId> PlacementsOf(string tag)
    {
        var functions = Tagged(tag).Functions.Select(f => f.Id).ToHashSet();
        return [.. _session.Project!.Placements.Where(p => functions.Contains(p.FunctionId)).Select(p => p.Id)];
    }

    private void AssertUnchanged() => Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));

    [Fact]
    public void Window_selection_takes_only_symbols_entirely_inside()
    {
        var boxes = _session.BoxesFor(1);
        var motor = boxes.Single(box => PlacementsOf("M1").Contains(box.Placement)).Bounds;

        var inside = _session.BoxesIn(1, new Box(motor.X - 1, motor.Y - 1, motor.Width + 2, motor.Height + 2));
        var cut = _session.BoxesIn(1, new Box(motor.X + 1, motor.Y + 1, motor.Width, motor.Height));

        Assert.Contains(inside, box => PlacementsOf("M1").Contains(box.Placement));
        Assert.DoesNotContain(cut, box => PlacementsOf("M1").Contains(box.Placement));
    }

    [Fact]
    public void Moving_deleting_and_rotating_a_selection_are_single_undoable_steps()
    {
        var selected = PlacementsOf("K1").Concat(PlacementsOf("F1")).ToList();

        Assert.True(_session.MoveMany(selected, 9.0, 0).IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.RotateMany(selected).IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.DeleteMany(PlacementsOf("M1")).IsSuccess);
        Assert.DoesNotContain(_session.Project!.Devices, device => device.ProductTag == "M1" && device.FunctionNodeId == Tagged("K1").FunctionNodeId);
        Assert.True(_session.Undo().IsSuccess);

        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void Move_shorter_than_the_snap_step_changes_nothing()
    {
        Assert.True(_session.MoveMany(PlacementsOf("K1"), 1.0, 2.0).IsSuccess);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public void Copy_and_paste_gives_new_tags_and_returns_the_pasted_symbols()
    {
        Assert.False(_session.CanPaste);
        Assert.Equal("pano.bos", _session.Paste(1).Error!.Code);

        Assert.True(_session.Copy(1, PlacementsOf("K1")).IsSuccess);
        var pasted = _session.Paste(1, 300, 60);

        Assert.True(pasted.IsSuccess, pasted.Error?.Message);
        Assert.Equal(PlacementsOf("K1").Count, pasted.Value.Count);
        Assert.Contains(_session.Project!.Devices, device => device.ProductTag == "K2" && device.FunctionNodeId == Tagged("K1").FunctionNodeId);
        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void Wire_is_drawn_between_two_pins_found_near_the_click()
    {
        var pins = _session.PinsFor(1);
        var motorPe = pins.Single(pin => pin.Name == "PE" && Tagged("M1").Functions.SelectMany(f => f.ConnectionPoints).Any(p => p.Id == pin.Point));
        var coilA1 = pins.Single(pin => pin.Name == "A1");

        Assert.Equal(motorPe.Point, _session.PinAt(1, motorPe.X + 1, motorPe.Y - 1)!.Point);
        Assert.Null(_session.PinAt(1, motorPe.X + 10, motorPe.Y));

        var connections = _session.Project!.Connections.Count;
        Assert.True(_session.ConnectPins(motorPe.Point, coilA1.Point).IsSuccess);
        Assert.Equal(connections + 1, _session.Project.Connections.Count);
        Assert.NotNull(_session.Project.Connections[^1].Wire);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.ConnectPins(motorPe.Point, motorPe.Point).IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void Dropping_a_device_places_its_next_unplaced_function_on_the_grid()
    {
        var coil = Tagged("K1").Functions.Single(f => f.Kind == FunctionKind.Coil);
        var coilPlacement = _session.Project!.Placements.Single(p => p.FunctionId == coil.Id).Id;
        Assert.True(_session.DeleteMany([coilPlacement]).IsSuccess);

        var placed = _session.PlaceNextFunction(Tagged("K1").Id, 2, 201.7, 98.4);

        Assert.True(placed.IsSuccess, placed.Error?.Message);
        var placement = _session.Project.Placements.Single(p => p.Id == placed.Value);
        Assert.Equal(coil.Id, placement.FunctionId);
        // Bırakılan nokta sayfa koordinatındadır (201,7 / 98,4); yerleşim çizim alanına göredir (alan 15, 15'te başlar).
        Assert.Equal((185.0, 85.0), (placement.X, placement.Y));
        Assert.Equal("yerlesim.hepsi_yerlesik", _session.PlaceNextFunction(Tagged("K1").Id, 2, 0, 0).Error!.Code);
    }

    [Fact]
    public void Dropping_a_library_symbol_creates_a_device_named_after_it_in_one_step()
    {
        var lamp = new SymbolDefinition(
            SymbolId.New(), "Sinyal lambası", [new SymbolCircle(0, 7.5, 4)], [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)], "test", "CC0");
        var libraryPath = Path.Combine(_directory.FullName, "library.db");
        using (var library = ComponentLibrary.Open(libraryPath).Value)
        {
            library.ImportSymbols([new LibrarySymbol(lamp, "sinyal")]);
        }

        Assert.Equal("kutuphane.acik_degil", _session.PlaceLibrarySymbol(lamp.Id, 2, 50, 50).Error!.Code);
        Assert.True(_session.OpenLibrary(libraryPath).IsSuccess);

        var placed = _session.PlaceLibrarySymbol(lamp.Id, 2, 52, 48);

        Assert.True(placed.IsSuccess, placed.Error?.Message);
        var device = _session.Project!.Devices[^1];
        Assert.StartsWith("P", device.ProductTag, StringComparison.Ordinal);
        Assert.Equal(["1", "2"], device.Functions[0].ConnectionPoints.Select(p => p.Name));
        Assert.Equal(lamp.Id, device.Functions[0].SymbolId);
        Assert.Contains(_session.Project.Symbols, symbol => symbol.Id == lamp.Id);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Theory]
    [InlineData("Sinyal LAMBASI", "P")]
    [InlineData("Kontaktör bobini", "K")]
    [InlineData("Motor 3~", "M")]
    [InlineData("Sigorta 3p", "F")]
    [InlineData("Acil stop butonu", "S")]
    [InlineData("Klemens", "X")]
    [InlineData("Bilinmeyen kutu", "A")]
    public void Tag_letter_is_guessed_from_the_symbol_name(string name, string letter) =>
        Assert.Equal(letter, EditorSession.TagLetterFor(name));

    [Fact]
    public void Clicking_a_wire_puts_a_junction_on_it_and_splits_the_wire_there()
    {
        var wire = _session.WiresFor(0).First(w => w.Path.Count >= 2 && Math.Abs(w.Path[0].X - w.Path[1].X) < 0.01 && Math.Abs(w.Path[0].Y - w.Path[1].Y) > 12);
        var (x, y) = (wire.Path[0].X, (wire.Path[0].Y + wire.Path[1].Y) / 2);
        var connections = _session.Project!.Connections.Count;

        var split = _session.SplitWire(0, wire, x + 0.4, y);

        Assert.True(split.IsSuccess, split.Error?.Message);
        Assert.Equal(connections + 1, _session.Project.Connections.Count);
        Assert.Equal(x, split.Value.X, 3);
        Assert.InRange(split.Value.Y, Math.Min(wire.Path[0].Y, wire.Path[1].Y), Math.Max(wire.Path[0].Y, wire.Path[1].Y));
        Assert.Equal(split.Value.Point, _session.PinAt(0, x + 1, split.Value.Y)!.Point);
        Assert.Equal(2, _session.WiresFor(0).Count(w => _session.EndsOf(0, w.Connection) is { } ends && (ends.From.Point == split.Value.Point || ends.To.Point == split.Value.Point)));
        Assert.True(_session.SceneFor(0, new DateOnly(2026, 11, 15)).Value.Warnings.Count == 0);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void Library_symbol_with_named_pins_attributes_device_name_and_tag_letter_makes_a_matching_device()
    {
        var attributes = new PointAttributes(PinKind.Signal, ConnectorType.M12, ContactType.NormallyClosed, "A-kod 4 pin");
        var sensor = new SymbolDefinition(
            SymbolId.New(), "Endüktif sensör", [new SymbolCircle(0, 7.5, 4)],
            [new SymbolPin(0, 0, 0, -1, "BN", attributes), new SymbolPin(0, 15, 0, 1, "BU")],
            "test", "CC0", "Endüktif yakınlık sensörü", "B");
        var libraryPath = Path.Combine(_directory.FullName, "library.db");
        using (var library = ComponentLibrary.Open(libraryPath).Value)
        {
            library.ImportSymbols([new LibrarySymbol(sensor, "sensör")]);
        }

        Assert.True(_session.OpenLibrary(libraryPath).IsSuccess);
        var placed = _session.PlaceLibrarySymbol(sensor.Id, 2, 52, 48);

        Assert.True(placed.IsSuccess, placed.Error?.Message);
        var device = _session.Project!.Devices[^1];
        Assert.Equal("B1", device.ProductTag);
        Assert.Equal("Endüktif yakınlık sensörü", device.Description);
        Assert.Equal(["BN", "BU"], device.Functions[0].ConnectionPoints.Select(p => p.Name));
        Assert.Equal(attributes, device.Functions[0].ConnectionPoints[0].Attributes);
        Assert.True(device.Functions[0].ConnectionPoints[1].Attributes.IsEmpty);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void Standard_kinds_are_added_once_and_a_kind_is_given_to_several_wires_in_one_undoable_step()
    {
        Assert.True(_session.AddStandardConnectionKinds().IsSuccess);
        var count = _session.ConnectionKinds.Count;
        Assert.Equal(Ake.Core.Model.ConnectionKind.Standard.Count, count);

        Assert.True(_session.AddStandardConnectionKinds().IsSuccess);
        Assert.Equal(count, _session.ConnectionKinds.Count);

        var kind = _session.ConnectionKinds.Single(item => item.Name == "Profinet");
        var wires = _session.Project!.Connections.Take(3).Select(connection => connection.Id).ToList();
        Assert.True(_session.SetConnectionKind(wires, kind.Id).IsSuccess);
        Assert.All(_session.Project.Connections.Take(3), connection => Assert.Equal(kind.Id, connection.KindId));

        Assert.True(_session.Undo().IsSuccess);
        Assert.All(_session.Project.Connections.Take(3), connection => Assert.Null(connection.KindId));
        Assert.True(_session.Undo().IsSuccess);
        Assert.Empty(_session.ConnectionKinds);
        AssertUnchanged();
    }

    [Fact]
    public void The_communication_page_is_added_once_opened_again_and_refused_without_communication()
    {
        var pages = _session.Project!.Pages.Count;
        Assert.Equal("haberlesme.yok", _session.AddCommunicationPage().Error!.Code);

        var project = _session.Project;
        var connection = project.Connections.First(c => project.Locate(c.From)!.Device != project.Locate(c.To)!.Device);
        var communication = new Ake.Core.Model.PointAttributes(Ake.Core.Model.PinKind.Communication, Ake.Core.Model.ConnectorType.Rj45);
        Assert.True(_session.Run(new Ake.Core.Commands.Transaction(
            "haberleşme",
            [
                new Ake.Core.Commands.SetPointAttributes(connection.From, communication),
                new Ake.Core.Commands.SetPointAttributes(connection.To, communication),
            ])).IsSuccess);

        var added = _session.AddCommunicationPage();

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal(pages, added.Value);
        var page = _session.Project.Pages[added.Value];
        Assert.Equal((Ake.Core.Model.PageKind.CommunicationDiagram, "Haberleşme şeması"), (page.Kind, page.Title));
        Assert.Empty(_session.SceneFor(added.Value, new DateOnly(2026, 11, 18)).Value.Warnings);
        Assert.Equal(added.Value, _session.AddCommunicationPage().Value);
        Assert.Equal(pages + 1, _session.Project.Pages.Count);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void The_single_line_page_is_added_once_opened_again_and_refused_without_a_power_chain()
    {
        var pages = _session.Project!.Pages.Count;

        var added = _session.AddSingleLinePage();

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal(pages, added.Value);
        var page = _session.Project.Pages[added.Value];
        Assert.Equal((Ake.Core.Model.PageKind.SingleLineDiagram, "Tek hat şeması"), (page.Kind, page.Title));
        Assert.Empty(_session.SceneFor(added.Value, new DateOnly(2026, 11, 18)).Value.Warnings);
        Assert.Equal(added.Value, _session.AddSingleLinePage().Value);
        Assert.Equal(pages + 1, _session.Project.Pages.Count);

        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();

        using var empty = new EditorSession("boş");
        Assert.True(empty.NewProject("Boş proje").IsSuccess);
        Assert.Equal("tekhat.zincir_yok", empty.AddSingleLinePage().Error!.Code);
    }

    [Fact]
    public void Wire_numbering_settings_new_wires_and_renumbering_work_through_the_session_and_undo()
    {
        Assert.True(_session.SetWireNumbering(new Ake.Core.Model.WireNumberSettings("W", 200, 1, 3, Ake.Core.Numbering.WireNumberingScheme.Sequential)).IsSuccess);
        Assert.True(_session.RenumberWires().IsSuccess);
        var wires = _session.Project!.Connections.Where(c => c.Wire is not null).ToList();
        Assert.Equal("W200", wires[0].Wire!.Number);

        // Numarası alınan tel kaldırılırsa ve yeni tel eklenirse yeni numara kaldığı yerden sürer.
        var a = _session.Project.Devices.Single(d => d.ProductTag == "K1").Functions.First().ConnectionPoints[0].Id;
        var b = _session.Project.Devices.Single(d => d.ProductTag == "K2").Functions.First().ConnectionPoints[0].Id;
        Assert.True(_session.ConnectPins(a, b).IsSuccess);
        Assert.True(_session.NumberNewWires().IsSuccess);
        var last = _session.Project.Connections.Last(c => c.Wire is not null);
        Assert.Equal($"W{200 + wires.Count:000}", last.Wire!.Number);
        Assert.True(_session.NumberNewWires().IsSuccess);
        Assert.Equal("W200", wires[0].Wire!.Number);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        AssertUnchanged();
    }

    [Fact]
    public void A_page_is_inserted_as_a_sub_page_without_touching_the_others_and_pages_can_be_renumbered_on_request()
    {
        var before = _session.Project!.Pages.Select(page => page.Number).ToList();

        var inserted = _session.InsertPageAfter(2);

        Assert.True(inserted.IsSuccess, inserted.Error?.Message);
        Assert.Equal(3, inserted.Value);
        var pages = _session.Project.Pages;
        Assert.Equal(["1", "2", "3", "3.1", "4", "5"], pages.Select(page => page.Number));
        Assert.Equal((pages[2].TemplateId, pages[2].FunctionNodeId, pages[2].LocationNodeId), (pages[3].TemplateId, pages[3].FunctionNodeId, pages[3].LocationNodeId));
        Assert.Empty(_session.SceneFor(3, new DateOnly(2026, 11, 18)).Value.Warnings);

        Assert.Equal("3.2", _session.Project.Pages[_session.InsertPageAfter(3).Value].Number);
        Assert.True(_session.RenumberPages().IsSuccess);
        Assert.Equal(["1", "2", "3", "4", "5", "6", "7"], _session.Project.Pages.Select(page => page.Number));

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(before, _session.Project.Pages.Select(page => page.Number));
        AssertUnchanged();
    }

    [Fact]
    public void A_new_page_is_appended_with_the_next_number_and_its_number_can_be_changed_and_undone()
    {
        var count = _session.Project!.Pages.Count;

        var added = _session.AppendPage();

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.Equal(count, added.Value);
        var page = _session.Project.Pages[^1];
        Assert.Equal("6", page.Number);
        Assert.Equal(_session.Project.Pages[count - 1].TemplateId, page.TemplateId);
        Assert.Empty(_session.SceneFor(count, new DateOnly(2026, 11, 19)).Value.Warnings);

        Assert.True(_session.SetPageNumberAt(count, "6A").IsSuccess);
        Assert.Equal("6A", page.Number);
        Assert.Equal("page.numara_tekrar", _session.SetPageNumberAt(count, "1").Error!.Code);
        Assert.Equal("editor.sayfa_yok", _session.SetPageNumberAt(99, "7").Error!.Code);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(count, _session.Project.Pages.Count);
        AssertUnchanged();
    }

    [Fact]
    public void The_page_commands_are_in_the_page_tab()
    {
        var group = EditorCommands.Main.Tabs.Single(tab => tab.Label == "Sayfa").Groups.Single(group => group.Label == "Sayfa");

        Assert.Equal([EditorCommands.NewPage, EditorCommands.InsertPage, EditorCommands.PageProperties], group.Items.Select(item => item.CommandId));
    }

    [Fact]
    public void A_page_moves_in_the_list_without_changing_numbers_and_the_scene_follows_the_new_order()
    {
        var before = _session.Project!.Pages.Select(page => page.Number).ToList();

        Assert.True(_session.MovePageTo(4, 0).IsSuccess);

        Assert.Equal(["5", "1", "2", "3", "4"], _session.Project.Pages.Select(page => page.Number));
        Assert.Equal("5  Acil stop", _session.PageLabels[0]);
        Assert.True(_session.MovePageTo(0, 0).IsSuccess); // aynı yere taşımak bir şey yapmaz
        Assert.Equal("page.sira_gecersiz", _session.MovePageTo(0, 9).Error!.Code);
        Assert.Equal("editor.sayfa_yok", _session.MovePageTo(9, 0).Error!.Code);
        Assert.Empty(_session.SceneFor(0, new DateOnly(2026, 11, 19)).Value.Warnings);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(before, _session.Project.Pages.Select(page => page.Number));
        Assert.False(_session.CanUndo);
        AssertUnchanged();
    }

    [Fact]
    public void The_move_commands_are_in_the_page_tab()
    {
        var group = EditorCommands.Main.Tabs.Single(tab => tab.Label == "Sayfa").Groups.Single(group => group.Label == "Sıra");

        Assert.Equal([EditorCommands.MovePageFirst, EditorCommands.MovePageUp, EditorCommands.MovePageDown], group.Items.Select(item => item.CommandId));
    }
}
