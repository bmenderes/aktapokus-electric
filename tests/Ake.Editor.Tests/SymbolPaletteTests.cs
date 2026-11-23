using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor.Tests;

/// <summary>Palette ağacı, sembol düzenleme ve blok editörü ızgarası (ADR-0035).</summary>
public sealed class SymbolPaletteTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-palette-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static SymbolRow Row(string category, string name, int pins = 2) =>
        new(SymbolId.New(), $"{name}  ·  {pins} uç  ·  {category}", pins, category, name);

    [Theory]
    [InlineData("fav0100_A10_contact_NO_detent", "Contact NO detent")]
    [InlineData("fav0100_contact/fav0100_A2_contact_2xNO", "Contact 2xNO")]
    [InlineData("fav0123_A11_terminal_1x_v", "Terminal 1x v")]
    [InlineData("Basma düğmesi (NO)", "Basma düğmesi (NO)")]
    public void Manufacturer_file_names_become_readable(string raw, string clean) => Assert.Equal(clean, SymbolPalette.CleanName(raw));

    [Theory]
    [InlineData("fav0100_contact", "Kontaklar")]
    [InlineData("fav0100_contact_NO_normal_open", "Normalde açık (NO)")]
    [InlineData("fav0102_contact_CO_change_over", "Değiştirmeli (CO)")]
    [InlineData("fav0110_relay_coil", "Röle bobinleri")]
    [InlineData("fav0123_electrical_terminal", "Klemens ve konnektör")]
    [InlineData("fav0123_a20_plug_and_socket", "Fiş ve priz")]
    [InlineData("fav0150_dxf_bloc_pneumatic", "Pnömatik (DXF)")]
    [InlineData("fav0200_dxf_diagram_connection", "Bağlantı şemaları")]
    [InlineData("fav0300_dxf_single_line_diagram", "Tek hat şeması")]
    [InlineData("fav0402_a1_frequency_converter", "Frekans konvertörü")]
    [InlineData("10_allpole", "Çok hatlı (All-pole)")]
    [InlineData("310_relays_contactors_contacts", "Röle, kontaktör ve kontaklar")]
    [InlineData("Kumanda", "Kumanda")]
    public void Manufacturer_folders_become_turkish_group_names(string folder, string name) =>
        Assert.Equal(name, SymbolPalette.FriendlyFolder(folder));

    [Fact]
    public void Symbols_are_grouped_into_a_tree_standard_categories_first_and_same_names_merged()
    {
        var rows = new[]
        {
            Row("fav0123_electrical_terminal", "fav0123_A11_terminal_1x_v"),
            Row("Klemens ve konnektör", "Klemens"),
            Row("fav0100_contact/fav0100_contact_NO_normal_open", "fav0100_A10_contact_NO_detent"),
            Row("fav0100_contact/fav0104_contact_unusual/fav0104_legend_passing", "fav0104_A11_legend"),
            Row("Kumanda", "Basma düğmesi (NO)"),
            Row(SymbolPalette.UserCategory, "Benim sembolüm"),
        };

        var groups = SymbolPalette.Build(rows, expanded: false);

        Assert.Equal(["Kendi sembollerim", "Kumanda", "Klemens ve konnektör", "Kontaklar"], groups.Select(group => group.Name));
        Assert.Equal(2, groups.Single(group => group.Name == "Klemens ve konnektör").Count);
        var contacts = groups.Single(group => group.Name == "Kontaklar");
        Assert.Equal(2, contacts.Count);
        var unusual = contacts.Items.OfType<SymbolGroup>().Single(group => group.Name == "Özel kontaklar");
        Assert.Equal("Geçişli", Assert.IsType<SymbolGroup>(Assert.Single(unusual.Items)).Name);
        var open = contacts.Items.OfType<SymbolGroup>().First(group => group.Name == "Normalde açık (NO)");
        Assert.Equal("Contact NO detent  ·  2 uç", open.Items.OfType<SymbolRow>().Single().Display);
        Assert.All(groups, group => Assert.False(group.Expanded));
        Assert.All(SymbolPalette.Build(rows, expanded: true), group => Assert.True(group.Expanded));
    }

    [Fact]
    public void An_edited_standard_symbol_is_saved_as_a_copy_and_a_users_own_symbol_is_overwritten()
    {
        using var session = new EditorSession("deneme");
        var path = Path.Combine(_directory.FullName, "library.db");
        Assert.True(StandardSymbols.EnsureInstalled(path).IsSuccess);
        Assert.True(session.OpenLibrary(path).IsSuccess);
        var count = session.LibrarySymbolCount;
        var original = StandardSymbols.All.First(item => item.Symbol.Name == "Sinyal lambası").Symbol;
        var edited = original with { Name = "Sinyal lambası (kırmızı)" };

        var copy = session.SaveSymbolToLibrary(edited, overwrite: false);

        Assert.True(copy.IsSuccess, copy.Error?.Message);
        Assert.NotEqual(original.Id, copy.Value);
        Assert.Equal(count + 1, session.LibrarySymbolCount);
        Assert.Equal(original.Name, session.LibrarySymbol(original.Id)!.Name);
        var stored = session.LibrarySymbol(copy.Value)!;
        Assert.Equal("Sinyal lambası (kırmızı)", stored.Name);
        Assert.StartsWith("Düzenlendi:", stored.Source, StringComparison.Ordinal);
        Assert.Contains(session.SearchLibrary("kırmızı"), row => row.Category == SymbolPalette.UserCategory);

        var again = session.SaveSymbolToLibrary(stored with { Name = "Kırmızı lamba" }, overwrite: true);

        Assert.Equal(copy.Value, again.Value);
        Assert.Equal(count + 1, session.LibrarySymbolCount);
        Assert.Equal("Kırmızı lamba", session.LibrarySymbol(copy.Value)!.Name);
    }

    [Fact]
    public void Saving_without_an_open_library_fails_clearly()
    {
        using var session = new EditorSession("deneme");
        var symbol = new SymbolDefinition(
            SymbolId.New(), "X", [new SymbolLine(0, 0, 0, 10)], [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 10, 0, 1)]);

        Assert.Equal("kutuphane.acik_degil", session.SaveSymbolToLibrary(symbol, false).Error!.Code);
    }

    [Fact]
    public void A_finer_grid_lets_a_pin_be_placed_between_the_default_grid_points()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Pin, Grid = 0.5 };

        draft.Gesture(1.3, 2.9, 1.3, 2.9);

        Assert.Equal(new SymbolPin(1.5, 3, 0, -1), Assert.Single(draft.Pins));
        Assert.Equal(1.25, new SymbolDraft { Grid = 1.25 }.Round(1.3));
    }

    [Fact]
    public void A_pin_dropped_near_a_circle_sits_on_its_edge_and_points_outwards()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Circle };
        draft.Gesture(0, 0, 10, 0);
        draft.Tool = SymbolTool.Pin;

        draft.Gesture(10, 0, 10, 0);
        draft.Gesture(7.4, 7.2, 7.4, 7.2);

        Assert.Equal((10.0, 0.0, 1, 0), (draft.Pins[0].X, draft.Pins[0].Y, draft.Pins[0].DirectionX, draft.Pins[0].DirectionY));
        var pin = draft.Pins[1];
        Assert.Equal(10, Math.Sqrt((pin.X * pin.X) + (pin.Y * pin.Y)), 1);
        Assert.Equal(1, Math.Abs(pin.DirectionX) + Math.Abs(pin.DirectionY));
    }

    [Fact]
    public void A_connector_type_replaces_the_plain_pin_ring_with_its_own_symbol()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Pin };
        draft.Gesture(0, 0, 0, 0);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(0, 0);
        var plain = draft.ToScene();
        Assert.Contains(plain.Items.OfType<CircleItem>(), circle => circle.Radius == 0.6);

        draft.SetSelectedPin(null, new PointAttributes(PinKind.Communication, ConnectorType.Rj45));
        var rj45 = draft.ToScene();

        Assert.DoesNotContain(rj45.Items.OfType<CircleItem>(), circle => circle.Radius == 0.6);
        Assert.Contains(rj45.Items.OfType<RectItem>(), rect => rect.Width == 4.4 && rect.Height == 3.4);
        Assert.Contains(rj45.Items.OfType<TextItem>(), text => text.Text.EndsWith("RJ45", StringComparison.Ordinal));

        draft.SetSelectedPin(null, new PointAttributes(Connector: ConnectorType.M12));
        var m12 = draft.ToScene();
        Assert.DoesNotContain(m12.Items.OfType<RectItem>(), rect => rect.Width == 4.4);
        Assert.Contains(m12.Items.OfType<CircleItem>(), circle => circle.Radius == 2.2);
    }

    [Theory]
    [InlineData(ConnectorType.Screw)]
    [InlineData(ConnectorType.PushIn)]
    [InlineData(ConnectorType.Faston)]
    [InlineData(ConnectorType.M8)]
    [InlineData(ConnectorType.M12)]
    [InlineData(ConnectorType.Rj45)]
    [InlineData(ConnectorType.DSub)]
    [InlineData(ConnectorType.Other)]
    public void Every_connector_type_has_its_own_symbol_centered_on_the_pin(ConnectorType connector)
    {
        var items = ConnectorIcons.Items(connector, 10, 20);

        Assert.NotEmpty(items);
        Assert.Equal(connector != ConnectorType.Screw, ConnectorIcons.ShortName(connector).Length > 0);
        Assert.True(ConnectorIcons.RadiusOf(connector) > 0);
        Assert.NotEqual(
            ConnectorIcons.Items(ConnectorType.Unspecified, 10, 20).Select(item => item.ToString()),
            items.Select(item => item.ToString()));
    }

    [Fact]
    public void Pins_are_spread_evenly_on_a_selected_circle_starting_at_the_top_and_pointing_outwards()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Circle };
        draft.Gesture(0, 0, 10, 0);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(10, 0);
        Assert.True(draft.SelectedIsSurface);

        Assert.True(draft.AddPinsOnSelectedSurface(4).IsSuccess);

        Assert.Equal(
            [(0.0, -10.0, 0, -1), (10.0, 0.0, 1, 0), (0.0, 10.0, 0, 1), (-10.0, 0.0, -1, 0)],
            draft.Pins.Select(pin => (pin.X, pin.Y, pin.DirectionX, pin.DirectionY)));
        Assert.Null(draft.Selected);

        draft.Undo();
        Assert.Empty(draft.Pins);
    }

    [Fact]
    public void Six_pins_on_a_circle_are_equally_spaced_in_angle()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Circle };
        draft.Gesture(0, 0, 10, 0);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(10, 0);

        draft.AddPinsOnSelectedSurface(6);

        Assert.Equal(6, draft.Pins.Count);
        Assert.All(draft.Pins, pin => Assert.Equal(10, Math.Sqrt((pin.X * pin.X) + (pin.Y * pin.Y)), 1));
        var angles = draft.Pins.Select(pin => Math.Atan2(pin.Y, pin.X) * 180 / Math.PI).ToList();
        for (var i = 1; i < 6; i++)
        {
            var step = ((angles[i] - angles[i - 1]) + 360) % 360;
            Assert.Equal(60, step, 0);
        }
    }

    [Fact]
    public void Pins_on_a_rectangle_run_along_its_perimeter_and_point_outwards()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Rect };
        draft.Gesture(0, 0, 10, 5);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(0, 0);
        Assert.True(draft.SelectedIsSurface);

        Assert.True(draft.AddPinsOnSelectedSurface(6).IsSuccess);

        // Çevre 30 mm, adım 5 mm, ilk uç 2,5 mm: üstte 2,5 ve 7,5; sağda 2,5; altta 7,5 ve 2,5 (sağdan sola); solda.
        Assert.Equal(
            [(2.5, 0.0, 0, -1), (7.5, 0.0, 0, -1), (10.0, 2.5, 1, 0), (7.5, 5.0, 0, 1), (2.5, 5.0, 0, 1), (0.0, 2.5, -1, 0)],
            draft.Pins.Select(pin => (pin.X, pin.Y, pin.DirectionX, pin.DirectionY)));
    }

    [Fact]
    public void Adding_pins_needs_a_selected_circle_or_rectangle_and_a_sane_count()
    {
        var draft = new SymbolDraft { Tool = SymbolTool.Line };
        draft.Gesture(0, 0, 10, 0);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(5, 0);

        Assert.False(draft.SelectedIsSurface);
        Assert.Equal("sembol.yuzey_degil", draft.AddPinsOnSelectedSurface(4).Error!.Code);

        draft.Tool = SymbolTool.Circle;
        draft.Gesture(20, 0, 25, 0);
        draft.Tool = SymbolTool.Select;
        draft.SelectAt(25, 0);
        Assert.Equal("sembol.uc_sayisi", draft.AddPinsOnSelectedSurface(0).Error!.Code);
        Assert.Equal("sembol.uc_sayisi", draft.AddPinsOnSelectedSurface(65).Error!.Code);
        Assert.Empty(draft.Pins);
    }

    [Fact]
    public void The_m12_and_m8_standard_connectors_are_round()
    {
        var byName = StandardSymbols.All.ToDictionary(item => item.Symbol.Name, item => item.Symbol);

        Assert.Single(byName["M12 konnektör (4 pin)"].Shapes.OfType<SymbolCircle>());
        Assert.Empty(byName["M12 konnektör (4 pin)"].Shapes.OfType<SymbolRect>());
        Assert.Single(byName["M8 konnektör (3 pin)"].Shapes.OfType<SymbolCircle>());
        Assert.Single(byName["Ethernet portu (RJ45)"].Shapes.OfType<SymbolRect>());
    }

    [Fact]
    public void Session_imports_symbol_folder_and_refreshes_library_count()
    {
        using var session = new EditorSession("deneme");
        var libPath = Path.Combine(_directory.FullName, "session-import.db");
        Assert.True(StandardSymbols.EnsureInstalled(libPath).IsSuccess);
        Assert.True(session.OpenLibrary(libPath).IsSuccess);
        var before = session.LibrarySymbolCount;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        var qetFolder = Path.Combine(dir!.FullName, "examples", "qelectrotech");
        var imported = session.ImportSymbolsFromPath(qetFolder);

        Assert.True(imported.IsSuccess, imported.Error?.Message);
        Assert.Equal(3, imported.Value.Added);
        Assert.Equal(before + 3, session.LibrarySymbolCount);
    }
}
