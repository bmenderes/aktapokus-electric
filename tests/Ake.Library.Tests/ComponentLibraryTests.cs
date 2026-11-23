using Ake.Core.Catalog;
using Ake.Core.Commands;
using Ake.Core.Model;
using Microsoft.Data.Sqlite;

namespace Ake.Library.Tests;

/// <summary>Her test geçici bir klasörde kendi kütüphane dosyasıyla çalışır.</summary>
public sealed class ComponentLibraryTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-lib-test-");

    private string LibraryPath => Path.Combine(_directory.FullName, "library.db");

    public void Dispose() => _directory.Delete(recursive: true);

    private ComponentLibrary Open()
    {
        var opened = ComponentLibrary.Open(LibraryPath);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        return opened.Value;
    }

    private static IReadOnlyList<LibraryProduct> StarterSet()
    {
        var path = Path.Combine(RepositoryRoot(), "examples", "library", "baslangic-seti.json");
        var products = LibraryImport.FromFile(path);
        Assert.True(products.IsSuccess, products.Error?.Message);
        return products.Value;
    }

    private static LibraryProduct Product(
        string manufacturer, string orderCode, string source = "test", string license = "test", string? description = null) =>
        new(
            new ProductDefinition(
                manufacturer,
                orderCode,
                [new FunctionTemplate(FunctionKind.Coil, ["A1", "A2"])],
                Description: description),
            source,
            license);

    // ---- DoD: LC1D09 kütüphaneden eklenince A1/A2, 13/14, 21/22 otomatik oluşuyor

    [Fact]
    public void Contactor_from_the_library_becomes_a_device_with_all_its_functions()
    {
        using var library = Open();
        library.Import(StarterSet());

        var definition = library.Find("LC1D09BD").Value;
        var device = definition.CreateDevice("K5");
        var project = Project.Create(ProjectId.New(), "test").Value;
        var added = new AddDevice(device).Execute(project);

        Assert.True(added.IsSuccess, added.Error?.Message);
        Assert.True(project.CheckIntegrity().IsSuccess);
        Assert.Equal(
            [
                (FunctionKind.Coil, "A1,A2"),
                (FunctionKind.PowerContact, "1,2"),
                (FunctionKind.PowerContact, "3,4"),
                (FunctionKind.PowerContact, "5,6"),
                (FunctionKind.NoContact, "13,14"),
                (FunctionKind.NcContact, "21,22"),
            ],
            device.Functions.Select(f => (f.Kind, string.Join(',', f.ConnectionPoints.Select(p => p.Name)))));
        Assert.Equal(new PartRef("Schneider Electric", "LC1D09BD", "TeSys D 9 A, 24 VDC bobin"), device.Part);
        Assert.Equal(MountingType.DinRail35, device.Mounting!.Type);
        Assert.Equal("-K5:13", project.DesignationOf(device.Functions[4].ConnectionPoints[0].Id)!.ToString());
    }

    [Fact]
    public void Two_devices_from_the_same_product_do_not_share_identities()
    {
        using var library = Open();
        library.Import(StarterSet());
        var definition = library.Find("LC1D09BD").Value;

        var project = Project.Create(ProjectId.New(), "test").Value;
        Assert.True(new AddDevice(definition.CreateDevice("K1")).Execute(project).IsSuccess);
        Assert.True(new AddDevice(definition.CreateDevice("K2")).Execute(project).IsSuccess);

        Assert.True(project.CheckIntegrity().IsSuccess);
    }

    [Fact]
    public void Product_made_only_of_terminals_becomes_a_terminal_strip()
    {
        var definition = new ProductDefinition(
            "Test",
            "X-4",
            [
                new FunctionTemplate(FunctionKind.Terminal, ["1.a", "1.b"], PassThrough: true, Description: "1"),
                new FunctionTemplate(FunctionKind.Terminal, ["2.a", "2.b"], PassThrough: true, Description: "2"),
            ]);

        Assert.Equal(DeviceKind.TerminalStrip, definition.CreateDevice("X1").Kind);
        Assert.True(definition.CreateDevice("X1").Functions.All(function => function.PassThrough));
    }

    // ---- saklama

    [Fact]
    public void Products_survive_closing_and_reopening_the_library()
    {
        using (var library = Open())
        {
            Assert.Equal(new ImportSummary(6, 0), library.Import(StarterSet()).Value);
        }

        using var reopened = Open();
        var breaker = reopened.FindProduct("GV2ME10").Value;

        Assert.Equal(6, reopened.Count);
        Assert.Equal("Motor koruma şalteri", breaker.Category);
        Assert.All(breaker.Definition.Functions, function => Assert.Equal(6.3m, function.RatedCurrentA));
        Assert.Equal(
            [new ProductProperty("settingRangeMin", "4", "A"), new ProductProperty("settingRangeMax", "6.3", "A")],
            breaker.Properties);
        Assert.Contains("doğrulanmadı", breaker.Source);
        Assert.Null(breaker.Definition.Mounting);
    }

    [Fact]
    public void Importing_again_replaces_products_instead_of_duplicating_them()
    {
        using var library = Open();
        library.Import([Product("Acme", "C-1", description: "eski")]);

        var summary = library.Import([Product("Acme", "C-1", description: "yeni"), Product("Acme", "C-2")]).Value;

        Assert.Equal(new ImportSummary(1, 1), summary);
        Assert.Equal(2, library.Count);
        Assert.Equal("yeni", library.Find("C-1").Value.Description);
        Assert.Single(library.Find("C-1").Value.Functions);
    }

    [Fact]
    public void Import_is_all_or_nothing_and_requires_source_and_license()
    {
        using var library = Open();

        var result = library.Import([Product("Acme", "C-1"), Product("Acme", "C-2", license: " ")]);

        Assert.Equal("kutuphane.kaynak_yok", result.Error!.Code);
        Assert.Contains("Acme C-2", result.Error.Message);
        Assert.Equal(0, library.Count);
    }

    [Fact]
    public void Product_without_manufacturer_or_order_code_is_rejected()
    {
        using var library = Open();

        Assert.Equal("kutuphane.urun_gecersiz", library.Import([Product("", "C-1")]).Error!.Code);
        Assert.Equal("kutuphane.urun_gecersiz", library.Import([Product("Acme", " ")]).Error!.Code);
    }

    [Fact]
    public void Library_from_a_newer_version_is_not_opened()
    {
        Open().Dispose();
        using (var raw = new SqliteConnection($"Data Source={LibraryPath};Pooling=False"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {ComponentLibrary.SchemaVersion + 1}";
            command.ExecuteNonQuery();
        }

        Assert.Equal("kutuphane.sema_yeni", ComponentLibrary.Open(LibraryPath).Error!.Code);
    }

    [Fact]
    public void File_that_is_not_a_library_fails_without_throwing()
    {
        File.WriteAllText(LibraryPath, "bu bir sqlite dosyası değil, yeterince uzun bir metin olsun diye yazıldı");

        Assert.Equal("kutuphane.acilamadi", ComponentLibrary.Open(LibraryPath).Error!.Code);
    }

    [Fact]
    public void Library_is_created_in_a_folder_that_does_not_exist_yet()
    {
        var nested = Path.Combine(_directory.FullName, "alt", "klasör", "library.db");

        using var library = ComponentLibrary.Open(nested).Value;

        Assert.True(File.Exists(nested));
    }

    // ---- bulma ve arama

    [Fact]
    public void Order_code_shared_by_two_manufacturers_needs_the_manufacturer()
    {
        using var library = Open();
        library.Import([Product("Acme", "100"), Product("Zeta", "100")]);

        Assert.Equal("kutuphane.urun_belirsiz", library.Find("100").Error!.Code);
        Assert.Contains("Acme, Zeta", library.Find("100").Error!.Message);
        Assert.Equal("Zeta", library.Find("100", "Zeta").Value.Manufacturer);
        Assert.Equal("kutuphane.urun_yok", library.Find("100", "Başka").Error!.Code);
        Assert.Equal("kutuphane.urun_yok", library.Find("999").Error!.Code);
    }

    [Fact]
    public void Search_looks_in_every_descriptive_field_and_ignores_ascii_case()
    {
        using var library = Open();
        library.Import(StarterSet());

        Assert.Equal(4, library.Search("beckhoff").Count);
        Assert.Equal(["LC1D09BD"], library.Search("tesys d").Select(product => product.OrderCode));
        Assert.Equal(["EL1008", "EL2008"], library.Search("EtherCAT terminal").Select(product => product.OrderCode));
        Assert.Equal(["GV2ME10"], library.Search("Motor koruma").Select(product => product.OrderCode));
        Assert.Equal(6, library.Search(string.Empty).Count);
        Assert.Equal(2, library.Search(string.Empty, limit: 2).Count);
    }

    [Fact]
    public void Search_treats_wildcard_characters_as_plain_text()
    {
        using var library = Open();
        library.Import([Product("Acme", "A_1"), Product("Acme", "AB1"), Product("Acme", "100%")]);

        Assert.Equal(["A_1"], library.Search("A_1").Select(product => product.OrderCode));
        Assert.Equal(["100%"], library.Search("%").Select(product => product.OrderCode));
    }

    // ---- dosyadan okuma

    [Fact]
    public void Csv_columns_that_are_not_known_fields_become_properties()
    {
        var csv = """
            manufacturer,orderCode,description,category,powerKw,gearbox
            Schneider Electric,BMH0702P06A2A,"Servo motor, frensiz",Servo motor,1.159,6003K
            Schneider Electric,BMH1001P06A2A,Servo motor,Servo motor,1.26,
            """;

        var products = LibraryImport.FromCsv(csv, "müşteri motor listesi", "dahili").Value;

        Assert.Equal(2, products.Count);
        Assert.Equal("Servo motor, frensiz", products[0].Definition.Description);
        Assert.Equal(
            [new ProductProperty("powerKw", "1.159"), new ProductProperty("gearbox", "6003K")],
            products[0].Properties);
        Assert.Equal([new ProductProperty("powerKw", "1.26")], products[1].Properties);
        Assert.Equal("müşteri motor listesi", products[0].Source);
        Assert.Empty(products[0].Definition.Functions);
    }

    [Theory]
    [InlineData("")]
    [InlineData("orderCode,description\nX,y")]
    [InlineData("manufacturer,orderCode\nAcme,X,fazla")]
    public void Malformed_csv_is_rejected(string csv)
    {
        Assert.Equal("kutuphane.csv_gecersiz", LibraryImport.FromCsv(csv, "s", "l").Error!.Code);
    }

    [Fact]
    public void Csv_without_source_and_license_cannot_be_imported()
    {
        using var library = Open();
        var products = LibraryImport.FromCsv("manufacturer,orderCode\nAcme,X", source: null, license: null).Value;

        Assert.Equal("kutuphane.kaynak_yok", library.Import(products).Error!.Code);
    }

    [Theory]
    [InlineData("bozuk", "kutuphane.json_gecersiz")]
    [InlineData("""{ "libraryImportVersion": 2, "products": [] }""", "kutuphane.surum_gecersiz")]
    [InlineData("""{ "libraryImportVersion": 1, "products": [ { "manufacturer": "A" } ] }""", "kutuphane.json_gecersiz")]
    [InlineData("""{ "libraryImportVersion": 1, "products": [], "bilinmeyen": 1 }""", "kutuphane.json_gecersiz")]
    public void Malformed_json_is_rejected(string json, string expectedCode)
    {
        Assert.Equal(expectedCode, LibraryImport.FromJson(json).Error!.Code);
    }

    [Fact]
    public void Product_level_source_overrides_the_file_level_one()
    {
        var json = """
            {
              "libraryImportVersion": 1,
              "source": "dosya",
              "license": "dosya lisansı",
              "products": [
                { "manufacturer": "A", "orderCode": "1" },
                { "manufacturer": "A", "orderCode": "2", "source": "ürün", "license": "ürün lisansı" }
              ]
            }
            """;

        var products = LibraryImport.FromJson(json).Value;

        Assert.Equal(("dosya", "dosya lisansı"), (products[0].Source, products[0].License));
        Assert.Equal(("ürün", "ürün lisansı"), (products[1].Source, products[1].License));
    }

    [Fact]
    public void Unsupported_and_missing_files_fail_without_throwing()
    {
        var text = Path.Combine(_directory.FullName, "urunler.txt");
        File.WriteAllText(text, "x");

        Assert.Equal("kutuphane.bicim_yok", LibraryImport.FromFile(text).Error!.Code);
        Assert.Equal(
            "kutuphane.dosya_okunamadi",
            LibraryImport.FromFile(Path.Combine(_directory.FullName, "yok.json")).Error!.Code);
    }

    [Fact]
    public void Schema_v4_persists_function_symbol_id_updates_product_and_renames_symbol()
    {
        Assert.Equal(4, ComponentLibrary.SchemaVersion);

        using var library = Open();
        var sym = new SymbolDefinition(
            SymbolId.New(),
            "Eski Sembol",
            [new SymbolRect(0, 0, 20, 10), new SymbolText(10, 6, "BUS BAR MODULE", 2.0, TextAlign.Center)],
            [new SymbolPin(10, 0, 0, -1, "1"), new SymbolPin(10, 10, 0, 1, "2")],
            "test",
            "test");
        Assert.True(library.ImportSymbols([new LibrarySymbol(sym, "Otomasyon")]).IsSuccess);
        Assert.Equal("Otomasyon", library.GetSymbolCategory(sym.Id));

        Assert.True(library.RenameSymbol(sym.Id, "Yeni Sembol Adı").IsSuccess);
        Assert.Equal("Yeni Sembol Adı", library.FindSymbol(sym.Id.ToString()).Value.Name);
        Assert.Contains(library.SearchSymbols("Yeni Sembol"), s => s.Id == sym.Id);

        var product = new LibraryProduct(
            new ProductDefinition(
                "Acme",
                "DRV-01",
                [new FunctionTemplate(FunctionKind.Generic, ["1", "2"], Description: "Sürücü", SymbolId: sym.Id)],
                TypeDesignation: "T-01",
                Description: "İlk açıklama"),
            "test",
            "test",
            "Otomasyon");
        Assert.True(library.Import([product]).IsSuccess);

        var loaded = library.FindProduct("DRV-01", "Acme").Value;
        Assert.Equal(sym.Id, loaded.Definition.Functions[0].SymbolId);

        var updated = loaded with
        {
            Definition = loaded.Definition with
            {
                Manufacturer = "Acme Yeni",
                OrderCode = "DRV-02",
                Description = "Güncel açıklama",
            },
            Category = "Motor ve sürücü",
        };
        Assert.True(library.UpdateProduct("Acme", "DRV-01", updated).IsSuccess);
        Assert.False(library.FindProduct("DRV-01", "Acme").IsSuccess);

        var reloaded = library.FindProduct("DRV-02", "Acme Yeni").Value;
        Assert.Equal(("Güncel açıklama", "Motor ve sürücü", sym.Id), (reloaded.Definition.Description, reloaded.Category, reloaded.Definition.Functions[0].SymbolId));
    }

    [Fact]
    public void GraphicRecognition_ExportToLibrary_links_recognized_symbol_id_to_product_template()
    {
        using var library = Open();
        var sym = new SymbolDefinition(
            SymbolId.New(),
            "Sürücü taslak",
            [new SymbolRect(0, 0, 40, 20), new SymbolText(20, 10, "BUS BAR MODULE", 2.2, TextAlign.Center)],
            [new SymbolPin(10, 0, 0, -1, "CN1/1"), new SymbolPin(30, 0, 0, -1, "CN1/2")],
            "pdf",
            "Kullanıcı");
        var rec = new RecognizedDevice(
            "D1",
            PageId.New(),
            "10D1",
            FunctionKind.Generic,
            10,
            20,
            40,
            20,
            [new RecognizedPin("CN1/1", 20, 20, 0, -1), new RecognizedPin("CN1/2", 40, 20, 0, -1)],
            sym,
            [],
            Part: new PartRef("Schneider", "LXM62-TEST"),
            Description: "Servo sürücü");

        var exported = GraphicRecognition.ExportToLibrary(
            library, [rec], "PDF/Test", "PDF testi", "Kullanıcı");
        Assert.True(exported.IsSuccess, exported.Error?.Message);

        var product = library.FindProduct("LXM62-TEST", "Schneider").Value;
        var linkedId = product.Definition.Functions[0].SymbolId;
        Assert.NotNull(linkedId);
        var linkedSym = library.FindSymbol(linkedId!.Value.ToString()).Value;
        Assert.Contains(linkedSym.Shapes.OfType<SymbolText>(), t => t.Text == "BUS BAR MODULE");
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
