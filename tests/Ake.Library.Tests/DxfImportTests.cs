using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Tables;
using ACadSharp.Types.Units;
using Ake.Core.Model;
using CSMath;

namespace Ake.Library.Tests;

/// <summary>DXF/DWG çiziminden sembol (ADR-0027). Dosyalar testte ACadSharp ile yazılır ve geçici klasörde okunur.</summary>
public sealed class DxfImportTests : IDisposable
{
    private const string License = "üretici çizimi, yalnızca bu projede";
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-dxf-");

    public void Dispose() => _directory.Delete(recursive: true);

    private string Write(CadDocument document, string name = "cizim.dxf")
    {
        var path = Path.Combine(_directory.FullName, name);
        if (name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            document.Header.Version = ACadVersion.AC1027;
            DwgWriter.Write(path, document);
        }
        else
        {
            DxfWriter.Write(path, document, false);
        }

        return path;
    }

    private static CadDocument Box(UnitsType units = UnitsType.Millimeters)
    {
        // 20 x 30'luk dikdörtgen gövde; üstte ve altta birer uç noktası.
        var document = new CadDocument();
        document.Header.InsUnits = units;
        document.Entities.Add(new LwPolyline([new XY(100, 100), new XY(120, 100), new XY(120, 130), new XY(100, 130)]) { IsClosed = true });
        document.Entities.Add(new Point { Location = new XYZ(110, 130, 0) });
        document.Entities.Add(new Point { Location = new XYZ(110, 100, 0) });
        return document;
    }

    [Fact]
    public void Model_space_becomes_one_symbol_with_points_as_pins_and_y_pointing_down()
    {
        var document = Box();
        document.Entities.Add(new Circle { Center = new XYZ(110, 115, 0), Radius = 4 });
        document.Entities.Add(new TextEntity { Value = "LXM62", InsertPoint = new XYZ(102, 120, 0), Height = 3.5 });

        var imported = Assert.Single(DxfImport.FromPath(Write(document), License));

        var symbol = imported.Symbol!;
        Assert.Null(imported.Error);
        Assert.Null(imported.Warning);
        Assert.Equal("cizim", symbol.Name);
        Assert.Equal(License, symbol.License);
        Assert.Equal("DXF: cizim.dxf", symbol.Source);
        Assert.Equal([new SymbolPin(0, 0, 0, -1), new SymbolPin(0, 30, 0, 1)], symbol.Pins);
        Assert.Equal(4, symbol.Shapes.OfType<SymbolLine>().Count());
        Assert.Contains(new SymbolLine(10, 0, -10, 0), symbol.Shapes);
        Assert.Equal(new SymbolCircle(0, 15, 4), Assert.Single(symbol.Shapes.OfType<SymbolCircle>()));
        var text = Assert.Single(symbol.Shapes.OfType<SymbolText>());
        Assert.Equal((-8.0, 10.0, "LXM62", 5.0), (text.X, text.Y, text.Text, text.SizeMm));
    }

    [Fact]
    public void Arcs_and_bulged_polylines_are_split_into_short_lines_that_stay_on_the_curve()
    {
        var document = new CadDocument();
        document.Entities.Add(new Arc { Center = new XYZ(0, 0, 0), Radius = 10, StartAngle = 0, EndAngle = Math.PI / 2 });
        document.Entities.Add(new LwPolyline([new LwPolyline.Vertex(new XY(30, 0)) { Bulge = 1 }, new LwPolyline.Vertex(new XY(50, 0))]));
        document.Entities.Add(new Point { Location = new XYZ(0, 0, 0) });

        var symbol = Assert.Single(DxfImport.FromPath(Write(document), License)).Symbol!;

        var lines = symbol.Shapes.OfType<SymbolLine>().ToList();
        Assert.True(lines.Count >= 16, $"{lines.Count} parça");
        var quarter = lines.Where(l => l.X1 < 20).SelectMany(l => new[] { (l.X1, l.Y1), (l.X2, l.Y2) });
        Assert.All(quarter, p => Assert.Equal(10, Math.Sqrt((p.Item1 * p.Item1) + (p.Item2 * p.Item2)), 2));
        var half = lines.Where(l => l.X1 >= 20).SelectMany(l => new[] { (l.X1, l.Y1), (l.X2, l.Y2) }).ToList();
        Assert.All(half, p => Assert.Equal(10, Math.Sqrt(((p.Item1 - 40) * (p.Item1 - 40)) + (p.Item2 * p.Item2)), 2));
    }

    [Fact]
    public void Drawing_units_are_converted_and_scale_is_applied_on_top()
    {
        var inches = Assert.Single(DxfImport.FromPath(Write(Box(UnitsType.Inches)), License)).Symbol!;
        var halved = Assert.Single(DxfImport.FromPath(Write(Box(), "yarim.dxf"), License, scale: 0.5)).Symbol!;

        Assert.Equal(30 * 25.4, inches.Pins[1].Y, 3);
        Assert.Equal(15, halved.Pins[1].Y, 3);
    }

    [Fact]
    public void Drawing_without_points_gets_one_pin_at_its_top_left_corner_and_a_warning()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line { StartPoint = new XYZ(5, 5, 0), EndPoint = new XYZ(45, 25, 0) });

        var imported = Assert.Single(DxfImport.FromPath(Write(document), License));

        Assert.NotNull(imported.Warning);
        Assert.Equal([new SymbolPin(0, 0, 0, -1)], imported.Symbol!.Pins);
        Assert.Equal(new SymbolLine(0, 20, 40, 0), Assert.Single(imported.Symbol.Shapes));
    }

    [Fact]
    public void Inserted_blocks_are_exploded_and_hidden_layers_are_left_out()
    {
        var document = Box();
        var block = new BlockRecord("KLEMENS");
        block.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(4, 0, 0) });
        document.BlockRecords.Add(block);
        document.Entities.Add(new Insert(block) { InsertPoint = new XYZ(200, 100, 0), XScale = 2 });
        var frozen = new Layer("GIZLI") { Flags = LayerFlags.Frozen };
        document.Layers.Add(frozen);
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(500, 500, 0), Layer = frozen });

        var symbol = Assert.Single(DxfImport.FromPath(Write(document), License)).Symbol!;

        Assert.Contains(new SymbolLine(90, 30, 98, 30), symbol.Shapes);
        Assert.Equal(5, symbol.Shapes.Count);
    }

    [Fact]
    public void With_blocks_each_named_block_becomes_a_symbol_anchored_at_its_base_point()
    {
        var document = new CadDocument();
        foreach (var (name, width) in new[] { ("ROLE", 10.0), ("SIGORTA", 6.0) })
        {
            var block = new BlockRecord(name);
            block.BlockEntity.BasePoint = new XYZ(1, 1, 0);
            block.Entities.Add(new Line { StartPoint = new XYZ(1, 1, 0), EndPoint = new XYZ(1 + width, 1, 0) });
            document.BlockRecords.Add(block);
        }

        var results = DxfImport.FromPath(Write(document), License, blocks: true);

        Assert.Equal(["ROLE", "SIGORTA"], results.Select(r => r.Symbol!.Name));
        Assert.Equal(new SymbolLine(0, 0, 10, 0), Assert.Single(results[0].Symbol!.Shapes));
        Assert.Equal("dxf.blok_yok", Assert.Single(DxfImport.FromPath(Write(Box(), "bloksuz.dxf"), License, blocks: true)).Error!.Code);
    }

    [Fact]
    public void Dwg_is_read_like_dxf_and_the_same_file_keeps_its_symbol_id()
    {
        var dxf = Assert.Single(DxfImport.FromPath(Write(Box(), "govde.dxf"), License)).Symbol!;
        var dwg = Assert.Single(DxfImport.FromPath(Write(Box(), "govde.dwg"), License)).Symbol!;
        var again = Assert.Single(DxfImport.FromPath(Path.Combine(_directory.FullName, "govde.dxf"), License)).Symbol!;

        Assert.Equal("DWG: govde.dwg", dwg.Source);
        Assert.Equal(dxf.Pins, dwg.Pins);
        Assert.Equal(dxf.Shapes.Count, dwg.Shapes.Count);
        Assert.Equal(dxf.Id, again.Id);
        Assert.NotEqual(dxf.Id, dwg.Id);
    }

    [Fact]
    public void Missing_license_and_unreadable_files_are_reported_not_thrown()
    {
        var path = Write(Box());
        var broken = Path.Combine(_directory.FullName, "bozuk.dwg");
        File.WriteAllText(broken, "bu bir çizim değil");

        Assert.Equal("dxf.lisans_yok", Assert.Single(DxfImport.FromPath(path, license: null)).Error!.Code);
        Assert.Equal("dxf.okunamadi", Assert.Single(DxfImport.FromPath(broken, License)).Error!.Code);
        Assert.Equal(2, DxfImport.FromPath(_directory.FullName, License).Count);
    }

    // Üretici çizimleri tek dosyada birden çok görünüm taşır: biri (100..120, 100..130), öbürü uzakta (500..540, 100..120).
    private static CadDocument TwoViews()
    {
        var document = Box();
        document.Entities.Add(new LwPolyline([new XY(500, 100), new XY(540, 100), new XY(540, 120), new XY(500, 120)]) { IsClosed = true });
        document.Entities.Add(new Hatch());
        return document;
    }

    [Fact]
    public void Inspect_reports_bounds_units_entities_layers_and_what_would_be_ignored()
    {
        var info = DxfImport.Inspect(Write(TwoViews())).Value;

        Assert.Equal("cizim.dxf", info.File);
        Assert.Equal("Millimeters", info.Units);
        Assert.Equal((100.0, 100.0, 440.0, 30.0), info.Extents);
        Assert.Equal(2, info.Points);
        Assert.Contains(("LwPolyline", 2), info.Entities);
        Assert.Contains(("Hatch", 1), info.Ignored);
        Assert.Contains(info.Layers, layer => layer.Name == "0" && layer.Visible);
    }

    [Fact]
    public void Area_selects_one_view_and_an_empty_area_or_broken_file_is_reported()
    {
        var path = Write(TwoViews());

        var left = Assert.Single(DxfImport.FromPath(path, License, area: (90, 90, 50, 50))).Symbol!;
        var rightImport = Assert.Single(DxfImport.FromPath(path, License, area: (490, 90, 60, 40)));
        var right = rightImport.Symbol!;
        var empty = Assert.Single(DxfImport.FromPath(path, License, area: (1000, 1000, 10, 10)));
        var broken = Path.Combine(_directory.FullName, "bozuk.dxf");
        File.WriteAllText(broken, "bu bir çizim değil");

        Assert.Equal(4, left.Shapes.OfType<SymbolLine>().Count());
        Assert.Equal(2, left.Pins.Count);
        Assert.Equal(4, right.Shapes.OfType<SymbolLine>().Count());
        Assert.Single(right.Pins);
        Assert.Contains("uç (POINT) yok", rightImport.Warning, StringComparison.Ordinal);
        Assert.Equal("dxf.bos", empty.Error!.Code);
        Assert.Equal("dxf.okunamadi", DxfImport.Inspect(broken).Error!.Code);
    }

    [Fact]
    public void A_drawing_without_point_markers_gets_pins_at_the_free_line_ends_on_its_top_and_bottom_edge()
    {
        // Üretici kütüphanelerinin kontağı: üstte ve altta serbest uçlu dikey çizgiler, ortada bıçak. POINT yok.
        var document = new CadDocument();
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 15, 0), EndPoint = new XYZ(0, 10, 0) });
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 5, 0), EndPoint = new XYZ(0, 0, 0) });
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 5, 0), EndPoint = new XYZ(-3, 10.5, 0) });
        document.Entities.Add(new Line { StartPoint = new XYZ(6, 15, 0), EndPoint = new XYZ(6, 0, 0) });

        var imported = Assert.Single(DxfImport.FromPath(Write(document), License));

        Assert.Null(imported.Warning);
        // Üsttekiler soldan sağa: (0,15) ve (6,15); sonra alttakiler: (0,0) ve (6,0). İlk uç başlangıç noktasıdır.
        Assert.Equal(
            [new SymbolPin(0, 0, 0, -1), new SymbolPin(6, 0, 0, -1), new SymbolPin(0, 15, 0, 1), new SymbolPin(6, 15, 0, 1)],
            imported.Symbol!.Pins);
    }

    [Fact]
    public void A_small_drawing_whose_free_ends_are_not_on_the_edges_keeps_the_single_default_pin_and_the_warning()
    {
        var document = new CadDocument();
        document.Entities.Add(new Line { StartPoint = new XYZ(0, 0, 0), EndPoint = new XYZ(10, 0, 0) });

        var imported = Assert.Single(DxfImport.FromPath(Write(document), License));

        Assert.Single(imported.Symbol!.Pins);
        Assert.Contains("uç (POINT) yok", imported.Warning, StringComparison.Ordinal);
    }
}
