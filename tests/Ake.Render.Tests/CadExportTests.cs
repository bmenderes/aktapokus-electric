using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.IO;
using ACadSharp.Types.Units;
using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>DXF ve DWG çıktısı (ACadSharp, ADR-0022). Dosyalar geçici klasöre yazılıp geri okunur.</summary>
public sealed class CadExportTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-cad-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static Scene Sample() => new(
        420,
        297,
        [
            new LineItem(10, 20, 110, 20, 0.5),
            new LineItem(10, 30, 10, 60, 0.25, "#1f4fd8"),
            new RectItem(5, 5, 410, 287, 0.35),
            new CircleItem(50, 50, 4),
            new CircleItem(80, 50, 0.6, Filled: true),
            new TextItem(100, 100, "Güç dağıtımı", 2.5, "Arial", TextAlign.Right),
            new TextItem(120, 100, "-K1:14 / 2.1", 1.8, "Arial", TextAlign.Left, -90),
            new ImageItem(0, 0, 10, 10, "logo.png"),
            new LinkItem(0, 0, 10, 10, "sayfa-1"),
        ]);

    [Fact]
    public void Coordinates_are_millimetres_with_the_y_axis_pointing_up()
    {
        var document = CadExporter.ToDocument(Sample());

        var line = document.Entities.OfType<Line>().First();
        Assert.Equal(UnitsType.Millimeters, document.Header.InsUnits);
        Assert.Equal(ACadVersion.AC1027, document.Header.Version);
        Assert.Equal((10.0, 277.0), (line.StartPoint.X, line.StartPoint.Y));
        Assert.Equal((110.0, 277.0), (line.EndPoint.X, line.EndPoint.Y));
    }

    [Fact]
    public void Wires_texts_and_drawing_go_to_their_own_layers_with_their_colours()
    {
        var document = CadExporter.ToDocument(Sample());

        var lines = document.Entities.OfType<Line>().ToList();
        Assert.Equal(CadExporter.DrawingLayer, lines[0].Layer.Name);
        Assert.Equal(CadExporter.WireLayer, lines[1].Layer.Name);
        Assert.Equal((0x1f, 0x4f, 0xd8), (lines[1].Color.R, lines[1].Color.G, lines[1].Color.B));
        Assert.Equal((short)7, lines[0].Color.Index);
        Assert.All(document.Entities.OfType<TextEntity>(), text => Assert.Equal(CadExporter.TextLayer, text.Layer.Name));
    }

    [Fact]
    public void Line_weights_snap_to_the_standard_cad_weights()
    {
        var document = CadExporter.ToDocument(Sample());

        Assert.Equal(LineWeightType.W50, document.Entities.OfType<Line>().First().LineWeight);
        Assert.Equal(LineWeightType.W25, document.Entities.OfType<Line>().Last().LineWeight);
        Assert.Equal(LineWeightType.W35, document.Entities.OfType<LwPolyline>().Single(p => p.Vertices.Count == 4).LineWeight);
    }

    [Fact]
    public void Text_keeps_its_alignment_rotation_and_cap_height()
    {
        var document = CadExporter.ToDocument(Sample());

        var right = document.Entities.OfType<TextEntity>().Single(text => text.Value == "Güç dağıtımı");
        var vertical = document.Entities.OfType<TextEntity>().Single(text => text.Value.StartsWith('-'));

        Assert.Equal(TextHorizontalAlignment.Right, right.HorizontalAlignment);
        Assert.Equal((100.0, 197.0), (right.AlignmentPoint.X, right.AlignmentPoint.Y));
        Assert.Equal(2.5 * CadExporter.CapHeightRatio, right.Height, 6);
        Assert.Equal(Math.PI / 2, vertical.Rotation, 6);
        Assert.Equal("arial.ttf", right.Style.Filename);
    }

    [Fact]
    public void Filled_dot_becomes_a_wide_closed_polyline_and_images_and_links_are_skipped()
    {
        var document = CadExporter.ToDocument(Sample());

        var dot = document.Entities.OfType<LwPolyline>().Single(p => p.Vertices.Count == 2);
        Assert.True(dot.IsClosed);
        Assert.All(dot.Vertices, vertex => Assert.Equal((1.0, 0.6), (vertex.Bulge, vertex.StartWidth)));
        Assert.Single(document.Entities.OfType<Circle>());

        // 2 çizgi, 1 çerçeve, 1 daire, 1 nokta, 2 yazı; görsel ve bağlantı yok.
        Assert.Equal(7, document.Entities.Count);
    }

    [Theory]
    [InlineData(CadFormat.Dxf)]
    [InlineData(CadFormat.Dwg)]
    public void File_reads_back_with_turkish_text_layers_and_units(CadFormat format)
    {
        var path = Path.Combine(_directory.FullName, format == CadFormat.Dwg ? "sayfa.dwg" : "sayfa.dxf");

        Assert.True(CadExporter.Save(Sample(), path, format).IsSuccess);

        CadDocument read = format == CadFormat.Dwg ? DwgReader.Read(path) : DxfReader.Read(path);
        Assert.Equal(7, read.Entities.Count);
        Assert.Contains(read.Entities.OfType<TextEntity>(), text => text.Value == "Güç dağıtımı");
        Assert.True(read.Layers.Contains(CadExporter.WireLayer));
        Assert.Equal(UnitsType.Millimeters, read.Header.InsUnits);
    }

    [Fact]
    public void Reference_machine_page_reads_back_with_every_drawn_item()
    {
        var project = Ake.Serialization.ProjectFile.Load(
            Path.Combine(RepositoryRoot(), "examples", "ref-machine", "ref-machine.json")).Value;
        var scene = SheetRenderer.Render(project, project.Pages[1], new DateOnly(2026, 11, 15)).Value;
        var path = Path.Combine(_directory.FullName, "sayfa-2.dwg");

        Assert.True(CadExporter.Save(scene, path, CadFormat.Dwg).IsSuccess);

        var drawn = scene.Items.Count(item => item is LineItem or RectItem or CircleItem || item is TextItem { Text.Length: > 0 });
        Assert.Equal(drawn, DwgReader.Read(path).Entities.Count);
    }

    [Fact]
    public void Unwritable_path_fails_cleanly()
    {
        var blocker = Path.Combine(_directory.FullName, "dosya");
        File.WriteAllText(blocker, "x");

        Assert.Equal("render.dosya_yazilamadi", CadExporter.Save(Sample(), Path.Combine(blocker, "a.dxf"), CadFormat.Dxf).Error?.Code);
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
