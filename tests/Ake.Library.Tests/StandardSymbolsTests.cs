using Ake.Core.Model;

namespace Ake.Library.Tests;

/// <summary>Editörle gelen standart komponent seti (ADR-0035).</summary>
public sealed class StandardSymbolsTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-standard-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Every_standard_symbol_is_valid_licensed_categorised_and_unique()
    {
        var all = StandardSymbols.All;

        Assert.True(all.Count >= 100, $"{all.Count} sembol");
        Assert.All(all, item =>
        {
            Assert.Null(item.Symbol.Check());
            Assert.False(string.IsNullOrWhiteSpace(item.Symbol.License));
            Assert.False(string.IsNullOrWhiteSpace(item.Category));
            Assert.False(string.IsNullOrWhiteSpace(item.Symbol.TagLetter));
            Assert.False(string.IsNullOrWhiteSpace(item.Symbol.DeviceName));
        });
        Assert.Equal(all.Count, all.Select(item => item.Symbol.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(item => item.Symbol.Name).Distinct().Count());
        Assert.Equal(
            ["Kumanda", "Sinyal", "Koruma", "Röle ve kontaktör", "Motor ve aktüatör", "Güç", "Klemens ve konnektör", "Sensör", "Otomasyon", "Pnömatik"],
            all.Select(item => item.Category!).Distinct());
    }

    [Fact]
    public void Contacts_and_connectors_carry_their_pin_attributes()
    {
        var byName = StandardSymbols.All.ToDictionary(item => item.Symbol.Name, item => item.Symbol);

        Assert.All(byName["Basma düğmesi (NO)"].Pins, pin => Assert.Equal(ContactType.NormallyOpen, pin.Attributes.Contact));
        Assert.All(byName["Acil stop (NC)"].Pins, pin => Assert.Equal(ContactType.NormallyClosed, pin.Attributes.Contact));
        Assert.Equal(
            [ContactType.Changeover, ContactType.NormallyClosed, ContactType.NormallyOpen],
            byName["Değiştirmeli kontak (CO)"].Pins.Select(pin => pin.Attributes.Contact));
        Assert.All(byName["M12 konnektör (4 pin)"].Pins, pin => Assert.Equal(ConnectorType.M12, pin.Attributes.Connector));
        Assert.Equal(PinKind.Communication, Assert.Single(byName["Ethernet portu (RJ45)"].Pins).Attributes.Kind);
        Assert.Equal(["P1", "P2", "P3", "P4", "P5", "24V", "0V"], byName["Ethernet anahtarı (5 port)"].Pins.Select(pin => pin.Name));
        Assert.Equal(3, byName["Motor 3~"].Pins.Count);
    }

    [Fact]
    public void Installing_creates_the_library_and_is_repeatable_without_touching_user_symbols()
    {
        var path = Path.Combine(_directory.FullName, "library.db");
        var own = new SymbolDefinition(
            SymbolId.New(), "Benim sembolüm", [new SymbolLine(0, 0, 0, 15)], [new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 15, 0, 1)], "test", "CC0");

        Assert.True(StandardSymbols.EnsureInstalled(path).IsSuccess);
        using (var library = ComponentLibrary.Open(path).Value)
        {
            Assert.Equal(StandardSymbols.All.Count, library.SymbolCount);
            library.ImportSymbols([new LibrarySymbol(own, "kişisel")]);
        }

        Assert.True(StandardSymbols.EnsureInstalled(path).IsSuccess);

        using var again = ComponentLibrary.Open(path).Value;
        Assert.Equal(StandardSymbols.All.Count + 1, again.SymbolCount);
        Assert.NotEmpty(again.SearchSymbols("kontaktör"));
        Assert.Single(again.SearchSymbols("Benim"));
    }

    [Fact]
    public void The_pneumatic_set_has_the_standard_ports_and_pneumatic_pin_attributes()
    {
        var pneumatic = StandardSymbols.All.Where(item => item.Category == "Pnömatik").ToDictionary(item => item.Symbol.Name, item => item.Symbol);

        Assert.Equal(14, pneumatic.Count);
        // ISO 1219 / ISO 5599 uç adları: 1 basınç, 2-4 çalışma, 3-5 egzoz, A/B silindir.
        Assert.Equal(["4", "2", "5", "1", "3"], pneumatic["5/2 valf, selenoid / yay"].Pins.Select(pin => pin.Name));
        Assert.Equal(["4", "2", "5", "1", "3"], pneumatic["5/2 valf, çift selenoid (hafızalı)"].Pins.Select(pin => pin.Name));
        Assert.Equal(["2", "1", "3"], pneumatic["3/2 valf, selenoid / yay (NC)"].Pins.Select(pin => pin.Name));
        Assert.Equal(["A", "B"], pneumatic["Silindir (çift etkili)"].Pins.Select(pin => pin.Name));
        Assert.Equal(["A"], pneumatic["Silindir (tek etkili, yaylı)"].Pins.Select(pin => pin.Name));
        Assert.Equal(["1", "2"], pneumatic["Hava hazırlama ünitesi (FRL)"].Pins.Select(pin => pin.Name));
        Assert.Equal(["3"], pneumatic["Susturucu (egzoz)"].Pins.Select(pin => pin.Name));

        Assert.All(pneumatic.Values.SelectMany(symbol => symbol.Pins), pin => Assert.Equal(PinKind.Pneumatic, pin.Attributes.Kind));
        Assert.All(pneumatic["Silindir (çift etkili)"].Pins, pin => Assert.Equal((ConnectorType.PushIn, "Ø6 mm"), (pin.Attributes.Connector, pin.Attributes.ConnectorCode)));
        Assert.All(pneumatic["Kompresör"].Pins, pin => Assert.Equal((ConnectorType.Thread, "G1/4"), (pin.Attributes.Connector, pin.Attributes.ConnectorCode)));
    }

    [Fact]
    public void Standard_products_install_alongside_symbols_and_cover_industrial_categories()
    {
        Assert.True(StandardProducts.All.Count >= 24);
        Assert.All(StandardProducts.All, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Definition.Manufacturer));
            Assert.False(string.IsNullOrWhiteSpace(item.Definition.OrderCode));
            Assert.False(string.IsNullOrWhiteSpace(item.Category));
            Assert.False(string.IsNullOrWhiteSpace(item.Source));
            Assert.False(string.IsNullOrWhiteSpace(item.License));
        });

        var path = Path.Combine(_directory.FullName, "products.db");
        Assert.True(StandardSymbols.EnsureInstalled(path).IsSuccess);

        using var library = ComponentLibrary.Open(path).Value;
        Assert.True(library.Count >= StandardProducts.All.Count);
        Assert.NotEmpty(library.Search("M12"));
        Assert.True(library.FindProduct("LC1D09BD", "Schneider Electric").IsSuccess);
    }

    [Fact]
    public void Discovered_collections_and_path_import_load_qet_and_dxf_symbols()
    {
        var repo = RepositoryRoot();
        var collections = StandardSymbols.DiscoverCollections(repo);
        Assert.Contains(collections, c => c.Kind == "qet" && c.Path.EndsWith(Path.Combine("examples", "qelectrotech"), StringComparison.OrdinalIgnoreCase));

        var libPath = Path.Combine(_directory.FullName, "collections.db");
        var qetSource = Path.Combine(repo, "examples", "qelectrotech");
        var qetImported = StandardSymbols.ImportFromPath(libPath, qetSource);
        Assert.True(qetImported.IsSuccess, qetImported.Error?.Message);
        Assert.Equal(3, qetImported.Value.Added);

        var jsonSource = Path.Combine(repo, "examples", "symbols", "sinyal-lambasi.sembol.json");
        var jsonImported = StandardSymbols.ImportFromPath(libPath, jsonSource);
        Assert.True(jsonImported.IsSuccess, jsonImported.Error?.Message);
        Assert.Equal(1, jsonImported.Value.Added);

        var dxfPath = Path.Combine(_directory.FullName, "test-sembol.dxf");
        var doc = new ACadSharp.CadDocument();
        doc.Header.InsUnits = ACadSharp.Types.Units.UnitsType.Millimeters;
        doc.Entities.Add(new ACadSharp.Entities.LwPolyline(
            [new CSMath.XY(0, 0), new CSMath.XY(10, 0), new CSMath.XY(10, 15), new CSMath.XY(0, 15)])
        { IsClosed = true });
        doc.Entities.Add(new ACadSharp.Entities.Point { Location = new CSMath.XYZ(5, 15, 0) });
        doc.Entities.Add(new ACadSharp.Entities.Point { Location = new CSMath.XYZ(5, 0, 0) });
        ACadSharp.IO.DxfWriter.Write(dxfPath, doc, false);

        var dxfImported = StandardSymbols.ImportFromPath(libPath, dxfPath, "Apache-2.0");
        Assert.True(dxfImported.IsSuccess, dxfImported.Error?.Message);
        Assert.Equal(1, dxfImported.Value.Added);

        using var library = ComponentLibrary.Open(libPath).Value;
        Assert.Equal(5, library.SymbolCount);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Depo kökü bulunamadı.");
    }
}
