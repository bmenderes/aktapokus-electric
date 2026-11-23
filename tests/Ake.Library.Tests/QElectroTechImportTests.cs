using Ake.Core.Model;

namespace Ake.Library.Tests;

public class QElectroTechImportTests
{
    private static string Examples()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "examples", "qelectrotech");
    }

    private static string Element(string description, string information = "Lisans: CC-BY 3.0", string names = "") => $"""
        <definition width="40" height="60" hotspot_x="20" hotspot_y="30" type="element">
            <names>{names}</names>
            <informations>{information}</informations>
            <description>{description}</description>
        </definition>
        """;

    private const string TwoPins = """<terminal x="0" y="-30" orientation="n"/><terminal x="0" y="30" orientation="s"/>""";

    [Fact]
    public void Example_folder_imports_every_element_with_its_turkish_name_and_license()
    {
        var results = QElectroTechImport.FromPath(Examples());

        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.Null(result.Error));
        Assert.Equal(
            ["Kontaktör bobini", "Sinyal lambası", "Start butonu (NO)"],
            results.Select(result => result.Symbol!.Name));
        Assert.All(results, result => Assert.Equal("Apache-2.0", result.Symbol!.License));
        Assert.All(results, result => Assert.StartsWith("QElectroTech: kontrol/", result.Symbol!.Source));
    }

    [Fact]
    public void Pixels_become_millimetres_and_the_first_terminal_becomes_the_origin()
    {
        var symbol = QElectroTechImport.FromXml(
            Element("""<rect x="-10" y="-10" width="20" height="20"/>""" + TwoPins), "bobin.elmt").Value;

        Assert.Equal([new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 30, 0, 1)], symbol.Pins);
        Assert.Equal(new SymbolRect(-5, 10, 10, 10), Assert.Single(symbol.Shapes));
    }

    [Fact]
    public void Scale_can_be_changed()
    {
        var symbol = QElectroTechImport.FromXml(Element(TwoPins), "x.elmt", scaleMmPerPixel: 0.25).Value;

        Assert.Equal(15, symbol.Pins[1].Y);
    }

    [Theory]
    [InlineData("n", 0, -1)]
    [InlineData("s", 0, 1)]
    [InlineData("e", 1, 0)]
    [InlineData("w", -1, 0)]
    public void Terminal_orientation_becomes_the_wire_direction(string orientation, int dx, int dy)
    {
        var symbol = QElectroTechImport.FromXml(
            Element($"""<line x1="0" y1="0" x2="0" y2="10"/><terminal x="0" y="0" orientation="{orientation}"/>"""),
            "x.elmt").Value;

        Assert.Equal((dx, dy), (symbol.Pins[0].DirectionX, symbol.Pins[0].DirectionY));
    }

    [Fact]
    public void Circle_polygon_and_text_are_converted()
    {
        var symbol = QElectroTechImport.FromXml(
            Element(
                """
                <circle x="-10" y="-10" diameter="20"/>
                <polygon x1="0" y1="0" x2="10" y2="0" x3="10" y3="10"/>
                <polygon x1="0" y1="0" x2="10" y2="0" x3="10" y3="10" closed="false"/>
                <text x="4" y="6" text=" K " size="8"/>
                """ + TwoPins),
            "x.elmt").Value;

        // İlk uç (0, -15) mm başlangıç olur; her şey 15 mm aşağı kayar.
        Assert.Contains(new SymbolCircle(0, 15, 5), symbol.Shapes);
        Assert.Equal(3 + 2, symbol.Shapes.OfType<SymbolLine>().Count());
        Assert.Contains(new SymbolText(2, 18, "K", 2.2), symbol.Shapes);
    }

    [Fact]
    public void Arc_and_ellipse_become_short_line_segments_on_the_curve()
    {
        var symbol = QElectroTechImport.FromXml(
            Element("""<arc x="-10" y="-10" width="20" height="20" start="0" angle="90"/>""" + TwoPins), "x.elmt").Value;

        var segments = symbol.Shapes.OfType<SymbolLine>().ToList();
        Assert.Equal(4, segments.Count);
        Assert.All(segments, segment =>
        {
            var radius = Math.Sqrt((segment.X1 * segment.X1) + ((segment.Y1 - 15) * (segment.Y1 - 15)));
            Assert.Equal(5, radius, 2);
        });
        Assert.Equal((5.0, 15.0), (segments[0].X1, segments[0].Y1));
        Assert.Equal((0.0, 10.0), (segments[^1].X2, segments[^1].Y2));
    }

    [Fact]
    public void Multi_line_text_becomes_one_text_per_line()
    {
        var symbol = QElectroTechImport.FromXml(
            Element("""<text x="0" y="0" text="1&#10;L1" size="9"/>""" + TwoPins), "x.elmt").Value;

        var texts = symbol.Shapes.OfType<SymbolText>().ToList();
        Assert.Equal(["1", "L1"], texts.Select(text => text.Text));
        Assert.Equal(2.48, texts[0].SizeMm);
        Assert.True(texts[1].Y > texts[0].Y);
    }

    [Fact]
    public void Lowercase_mit_in_a_german_description_is_not_a_license()
    {
        var xml = Element(TwoPins, information: "Schütz mit Hilfskontakt");

        Assert.Equal("qet.lisans_yok", QElectroTechImport.FromXml(xml, "x.elmt").Error!.Code);
    }

    [Fact]
    public void Collection_license_file_is_found_in_a_parent_folder()
    {
        var root = Directory.CreateTempSubdirectory("ake-qet-lisans-");
        try
        {
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "10_electric", "kontrol"));
            File.WriteAllText(Path.Combine(folder.FullName, "e.elmt"), Element(TwoPins, information: "Yazar: x"));
            File.WriteAllText(
                Path.Combine(root.FullName, "ELEMENTS.LICENSE"),
                "This work is licensed under the Creative Commons Attribution 3.0 License.");

            var imported = Assert.Single(QElectroTechImport.FromPath(Path.Combine(root.FullName, "10_electric")));

            Assert.Equal(QElectroTechImport.CollectionLicenseName, imported.Symbol!.License);
            Assert.Null(QElectroTechImport.CollectionLicense(Path.GetTempPath()));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dynamic_texts_and_unknown_elements_are_ignored()
    {
        var symbol = QElectroTechImport.FromXml(
            Element("""<dynamic_text x="0" y="0" text_from="ElementInfo"/><input x="0" y="0" text="_"/>""" + TwoPins),
            "x.elmt").Value;

        Assert.Empty(symbol.Shapes);
    }

    [Fact]
    public void Name_falls_back_from_turkish_to_english_to_the_file_name()
    {
        var english = QElectroTechImport.FromXml(
            Element(TwoPins, names: """<name lang="de">Spule</name><name lang="en">Coil</name>"""), "x.elmt").Value;
        var unnamed = QElectroTechImport.FromXml(Element(TwoPins), "klasor/role-bobini.elmt").Value;

        Assert.Equal("Coil", english.Name);
        Assert.Equal("role-bobini", unnamed.Name);
    }

    [Fact]
    public void Same_file_gets_the_same_id_so_importing_again_updates_instead_of_duplicating()
    {
        var first = QElectroTechImport.FromXml(Element(TwoPins), "a/b.elmt").Value;
        var again = QElectroTechImport.FromXml(Element(TwoPins), "a\\b.elmt").Value;
        var other = QElectroTechImport.FromXml(Element(TwoPins), "a/c.elmt").Value;

        Assert.Equal(first.Id, again.Id);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Fact]
    public void Element_without_a_license_is_refused_unless_one_is_given()
    {
        var xml = Element(TwoPins, information: "Yazar: bilinmiyor");

        Assert.Equal("qet.lisans_yok", QElectroTechImport.FromXml(xml, "x.elmt").Error!.Code);
        Assert.Equal("CC0", QElectroTechImport.FromXml(xml, "x.elmt", license: "CC0").Value.License);
    }

    [Theory]
    [InlineData("<definition><description><line x1=\"0\"/></description></definition>", "qet.lisans_yok")]
    [InlineData("<definition></definition>", "qet.xml_gecersiz")]
    [InlineData("<project></project>", "qet.xml_gecersiz")]
    [InlineData("bozuk <", "qet.xml_gecersiz")]
    public void Malformed_elements_are_refused(string xml, string code)
    {
        Assert.Equal(code, QElectroTechImport.FromXml(xml, "x.elmt").Error!.Code);
    }

    [Fact]
    public void Element_without_terminals_or_with_an_unknown_orientation_is_refused()
    {
        Assert.Equal("qet.uc_yok", QElectroTechImport.FromXml(Element("""<line x1="0" y1="0" x2="1" y2="1"/>"""), "x.elmt").Error!.Code);
        Assert.Equal(
            "qet.uc_yonu",
            QElectroTechImport.FromXml(Element("""<terminal x="0" y="0" orientation="x"/>"""), "x.elmt").Error!.Code);
    }

    [Fact]
    public void Bad_files_in_a_folder_are_reported_without_stopping_the_rest()
    {
        var directory = Directory.CreateTempSubdirectory("ake-qet-test-");
        try
        {
            File.WriteAllText(Path.Combine(directory.FullName, "iyi.elmt"), Element(TwoPins));
            File.WriteAllText(Path.Combine(directory.FullName, "bozuk.elmt"), "bozuk");
            File.WriteAllText(Path.Combine(directory.FullName, "baska.txt"), "alınmaz");

            var results = QElectroTechImport.FromPath(directory.FullName);

            Assert.Equal(2, results.Count);
            Assert.Equal("qet.xml_gecersiz", results.Single(result => result.File == "bozuk.elmt").Error!.Code);
            Assert.NotNull(results.Single(result => result.File == "iyi.elmt").Symbol);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
