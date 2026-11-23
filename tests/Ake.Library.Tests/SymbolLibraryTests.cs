using Ake.Core.Model;
using Microsoft.Data.Sqlite;

namespace Ake.Library.Tests;

/// <summary>Paylaşılan sembol kütüphanesi; her test geçici bir klasörde kendi dosyasıyla.</summary>
public sealed class SymbolLibraryTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-sym-lib-");

    private string LibraryPath => Path.Combine(_directory.FullName, "library.db");

    public void Dispose() => _directory.Delete(recursive: true);

    private ComponentLibrary Open()
    {
        var opened = ComponentLibrary.Open(LibraryPath);
        Assert.True(opened.IsSuccess, opened.Error?.Message);
        return opened.Value;
    }

    private static SymbolDefinition Symbol(string name, int pins = 2, string? license = "CC0", SymbolId? id = null) => new(
        id ?? SymbolId.New(),
        name,
        [new SymbolLine(0, 0, 0, 15)],
        [.. Enumerable.Range(0, pins).Select(i => new SymbolPin(i * 5, i == 0 ? 0 : 15, 0, i == 0 ? -1 : 1))],
        "test",
        license);

    [Fact]
    public void Symbols_survive_closing_and_reopening_the_library()
    {
        var lamp = Symbol("Lamba");
        using (var library = Open())
        {
            Assert.Equal(new ImportSummary(1, 0), library.ImportSymbols([new LibrarySymbol(lamp, "kontrol")]).Value);
        }

        using var reopened = Open();
        var found = reopened.FindSymbol(lamp.Id.ToString()).Value;

        Assert.Equal(1, reopened.SymbolCount);
        Assert.Equal(lamp.Name, found.Name);
        Assert.Equal(lamp.Shapes, found.Shapes);
        Assert.Equal(lamp.Pins, found.Pins);
        Assert.Equal("CC0", found.License);
    }

    [Fact]
    public void Importing_the_same_symbol_again_updates_it()
    {
        using var library = Open();
        var id = SymbolId.New();
        library.ImportSymbols([new LibrarySymbol(Symbol("Eski", id: id), null)]);

        var summary = library.ImportSymbols([new LibrarySymbol(Symbol("Yeni", id: id), null)]).Value;

        Assert.Equal(new ImportSummary(0, 1), summary);
        Assert.Equal("Yeni", library.FindSymbol(id.ToString()).Value.Name);
        Assert.Equal(1, library.SymbolCount);
    }

    [Fact]
    public void Symbol_without_a_license_or_that_cannot_be_drawn_is_refused_and_nothing_is_written()
    {
        using var library = Open();

        var unlicensed = library.ImportSymbols([new LibrarySymbol(Symbol("A"), null), new LibrarySymbol(Symbol("B", license: null), null)]);
        var invalid = library.ImportSymbols([new LibrarySymbol(Symbol("C") with { Pins = [] }, null)]);

        Assert.Equal("kutuphane.kaynak_yok", unlicensed.Error!.Code);
        Assert.Equal("sembol.gecersiz", invalid.Error!.Code);
        Assert.Equal(0, library.SymbolCount);
    }

    [Fact]
    public void Find_works_by_id_by_unique_name_and_by_category_path()
    {
        using var library = Open();
        var one = Symbol("Sigorta");
        var two = Symbol("Sigorta");
        var unique = Symbol("Termik röle");
        library.ImportSymbols(
        [
            new LibrarySymbol(one, "11_singlepole/fuses"),
            new LibrarySymbol(two, @"10_allpole\fuses"),
            new LibrarySymbol(unique, "11_singlepole/thermal"),
        ]);

        Assert.Equal(unique.Id, library.FindSymbol("Termik röle").Value.Id);
        Assert.Equal(two.Id, library.FindSymbol("10_allpole/fuses/Sigorta").Value.Id);
        Assert.Equal(one.Id, library.FindSymbol($"  {one.Id}  ").Value.Id);
        Assert.Equal("kutuphane.sembol_belirsiz", library.FindSymbol("Sigorta").Error!.Code);
        Assert.Equal("kutuphane.sembol_yok", library.FindSymbol("Yok").Error!.Code);
        Assert.Equal("kutuphane.sembol_yok", library.FindSymbol(Guid.NewGuid().ToString()).Error!.Code);
    }

    [Theory]
    [InlineData("şalter")]
    [InlineData("ŞALTER")]
    [InlineData("salter")]
    [InlineData("ISIK")]
    [InlineData("ışık")]
    public void Search_ignores_turkish_case_and_accents(string text)
    {
        using var library = Open();
        library.ImportSymbols([new LibrarySymbol(Symbol("Şalter ışık lambası"), "koruma")]);

        Assert.Single(library.SearchSymbols(text));
    }

    [Fact]
    public void Search_looks_in_name_and_category_and_can_filter_by_pin_count()
    {
        using var library = Open();
        library.ImportSymbols(
        [
            new LibrarySymbol(Symbol("Sigorta"), "fuses"),
            new LibrarySymbol(Symbol("Sigorta 3p", pins: 6), "fuses"),
            new LibrarySymbol(Symbol("Lamba"), "signaling"),
        ]);

        Assert.Equal(["Sigorta", "Sigorta 3p"], library.SearchSymbols("sigorta").Select(s => s.Name));
        Assert.Equal(["Lamba"], library.SearchSymbols("signal").Select(s => s.Name));
        Assert.Equal(["Sigorta 3p"], library.SearchSymbols("", pins: 6).Select(s => s.Name));
        Assert.Equal(2, library.SearchSymbols("", limit: 2).Count);
        Assert.Equal("fuses", library.SearchSymbols("3p")[0].Category);
    }

    [Fact]
    public void Library_of_the_previous_version_gets_the_symbol_table_and_keeps_its_products()
    {
        using (var raw = new SqliteConnection($"Data Source={LibraryPath};Pooling=False"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE products (id INTEGER PRIMARY KEY, manufacturer TEXT NOT NULL, order_code TEXT NOT NULL,
                    type_designation TEXT, description TEXT, category TEXT, mounting_type TEXT, width_mm REAL,
                    height_mm REAL, depth_mm REAL, clearance_mm REAL, heat_loss_w REAL, step_ref TEXT,
                    source TEXT NOT NULL, license TEXT NOT NULL, imported_at TEXT NOT NULL, UNIQUE (manufacturer, order_code));
                CREATE TABLE functions (id INTEGER PRIMARY KEY, product_id INTEGER NOT NULL, position INTEGER NOT NULL,
                    kind TEXT NOT NULL, pass_through INTEGER NOT NULL, description TEXT, rated_current_a TEXT);
                CREATE TABLE connection_points (function_id INTEGER NOT NULL, position INTEGER NOT NULL, name TEXT NOT NULL);
                CREATE TABLE properties (product_id INTEGER NOT NULL, position INTEGER NOT NULL, key TEXT NOT NULL,
                    value TEXT NOT NULL, numeric_value REAL, unit TEXT);
                INSERT INTO products (manufacturer, order_code, source, license, imported_at) VALUES ('Acme', 'C-1', 's', 'l', 'x');
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }

        using var library = Open();

        Assert.Equal(1, library.Count);
        Assert.Equal(0, library.SymbolCount);
        Assert.True(library.ImportSymbols([new LibrarySymbol(Symbol("Lamba"), null)]).IsSuccess);
    }

    [Fact]
    public void Symbol_pin_names_attributes_device_name_and_tag_letter_are_kept_in_the_library()
    {
        var attributes = new PointAttributes(PinKind.Signal, ConnectorType.M8, ContactType.NormallyOpen, "3 pin");
        var symbol = Symbol("Endüktif sensör") with
        {
            Pins = [new SymbolPin(0, 0, 0, -1, "BN", attributes), new SymbolPin(0, 15, 0, 1, "BU")],
            DeviceName = "Endüktif yakınlık sensörü",
            TagLetter = "B",
        };
        using (var library = Open())
        {
            library.ImportSymbols([new LibrarySymbol(symbol, "sensör")]);
        }

        using var again = Open();
        var found = again.FindSymbol(symbol.Id.ToString());

        Assert.True(found.IsSuccess, found.Error?.Message);
        Assert.Equal(symbol.Pins, found.Value.Pins);
        Assert.Equal(("Endüktif yakınlık sensörü", "B"), (found.Value.DeviceName, found.Value.TagLetter));
    }
}
