using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Ake.Library.Tests;

/// <summary>PDF'ten serbest grafik (ADR-0028). PDF'ler testte PdfPig yazıcısıyla geçici klasöre yazılır.</summary>
public sealed class PdfImportTests : IDisposable
{
    private const double Pt = 72 / 25.4;
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-pdf-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static PdfPoint At(double xMm, double yFromTopMm, double pageHeightMm = 297) =>
        new(xMm * Pt, (pageHeightMm - yFromTopMm) * Pt);

    private string Write(Action<PdfDocumentBuilder, PdfDocumentBuilder.AddedFont> draw, string name = "cizim.pdf", int pages = 1)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var i = 0; i < pages; i++)
        {
            draw(builder, font);
        }

        var path = Path.Combine(_directory.FullName, name);
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    private static PdfPageBuilder A3(PdfDocumentBuilder builder) => builder.AddPage(420 * Pt, 297 * Pt);

    [Fact]
    public void Lines_become_polylines_in_millimetres_with_the_y_axis_flipped()
    {
        var path = Write((builder, _) =>
        {
            var page = A3(builder);
            page.DrawLine(At(50, 100), At(90, 100), 0.7);
            page.DrawLine(At(90, 100), At(90, 120), 0.7);
        });

        var drawing = PdfImport.Read(path).Value;

        var imported = Assert.Single(drawing.Pages);
        Assert.Equal((420.0, 297.0), (Math.Round(imported.WidthMm, 1), Math.Round(imported.HeightMm, 1)));
        var polyline = Assert.IsType<GraphicPolyline>(Assert.Single(imported.Shapes));
        Assert.Equal([50.0, 100.0, 90.0, 100.0, 90.0, 120.0], polyline.Points.Select(v => Math.Round(v, 1)));
        Assert.Null(polyline.Color);
        Assert.False(polyline.Closed);
    }

    [Fact]
    public void Colours_are_kept_and_white_strokes_are_left_out()
    {
        var path = Write((builder, _) =>
        {
            var page = A3(builder);
            page.SetStrokeColor(0, 255, 0);
            page.DrawLine(At(10, 50), At(30, 50), 1);
            page.SetStrokeColor(255, 255, 255);
            page.DrawLine(At(10, 60), At(30, 60), 1);
            page.SetStrokeColor(0, 0, 0);
            page.DrawLine(At(10, 70), At(30, 70), 1);
        });

        var shapes = PdfImport.Read(path).Value.Pages[0].Shapes.OfType<GraphicPolyline>().ToList();

        Assert.Equal(["#00ff00", null], shapes.Select(shape => shape.Color));
    }

    [Fact]
    public void Stroke_widths_keep_their_ratio_around_a_typical_quarter_millimetre()
    {
        var path = Write((builder, _) =>
        {
            var page = A3(builder);
            for (var i = 0; i < 5; i++)
            {
                page.DrawLine(At(10, 50 + (i * 5)), At(30, 50 + (i * 5)), 1);
            }

            page.DrawLine(At(10, 100), At(30, 100), 4);
        });

        var widths = PdfImport.Read(path).Value.Pages[0].Shapes.OfType<GraphicPolyline>().Select(shape => shape.StrokeMm).ToList();

        Assert.Equal(0.25, widths.Min());
        Assert.Equal(1.0, widths.Max());
    }

    [Fact]
    public void Text_runs_become_single_texts_and_separate_words_stay_separate()
    {
        var path = Write((builder, font) =>
        {
            var page = A3(builder);
            page.AddText("Referans makine", 10, At(40, 60), font);
            page.AddText("U", 10, At(40, 80), font);
            page.AddText("V", 10, At(48, 80), font);
        });

        var texts = PdfImport.Read(path).Value.Pages[0].Shapes.OfType<GraphicText>().ToList();

        Assert.Equal(["Referans makine", "U", "V"], texts.Select(text => text.Text));
        Assert.Equal((40.0, 60.0), (Math.Round(texts[0].X, 1), Math.Round(texts[0].Y, 1)));
        Assert.Equal(Math.Round(10 / Pt, 2), texts[0].SizeMm);
        Assert.All(texts, text => Assert.Equal(0, text.RotationDeg));
    }

    [Fact]
    public void Frame_found_on_the_pages_is_dropped_and_what_is_inside_stays()
    {
        var path = Write(
            (builder, font) =>
            {
                var page = A3(builder);
                foreach (var y in new[] { 22.0, 287.0 }) { page.DrawLine(At(10, y), At(410, y), 1); }
                foreach (var y in new[] { 27.0, 267.0 }) { page.DrawLine(At(15, y), At(405, y), 1); }
                foreach (var x in new[] { 10.0, 410.0 }) { page.DrawLine(At(x, 22), At(x, 287), 1); }
                foreach (var x in new[] { 15.0, 405.0 }) { page.DrawLine(At(x, 27), At(x, 267), 1); }
                page.DrawLine(At(11, 100), At(14, 100), 1);
                page.AddText("0", 8, At(60, 25), font);
                page.AddText("Antet", 8, At(60, 275), font);
                page.DrawLine(At(100, 100), At(120, 100), 1);
                page.AddText("-K1", 8, At(100, 95), font);
            },
            pages: 3);

        var drawing = PdfImport.Read(path).Value;

        Assert.All(drawing.Pages, page =>
        {
            Assert.Equal((15.0, 27.0, 390.0, 240.0), page.Frame!.Value);
            Assert.Equal(2, page.Shapes.Count);
            Assert.Contains(page.Shapes, shape => shape is GraphicText { Text: "-K1" });
        });
        Assert.Equal(3 * 11, drawing.DroppedAsFrame);

        var kept = PdfImport.Read(path, keepFrame: true).Value;
        Assert.Equal(0, kept.DroppedAsFrame);
        Assert.All(kept.Pages, page => Assert.Equal(13, page.Shapes.Count));
    }

    [Fact]
    public void A_frame_seen_on_too_few_pages_is_not_assumed()
    {
        var path = Write((builder, font) =>
        {
            var page = A3(builder);
            page.DrawLine(At(100, 100), At(120, 100), 1);
        });

        var drawing = PdfImport.Read(path).Value;

        Assert.Null(drawing.Pages[0].Frame);
        Assert.Equal(0, drawing.DroppedAsFrame);
    }

    [Fact]
    public void Plan_adds_pages_and_graphics_in_one_undoable_step_shifted_onto_the_drawing_area()
    {
        var path = Write(
            (builder, font) =>
            {
                var page = A3(builder);
                foreach (var y in new[] { 22.0, 287.0 }) { page.DrawLine(At(10, y), At(410, y), 1); }
                foreach (var y in new[] { 27.0, 267.0 }) { page.DrawLine(At(15, y), At(405, y), 1); }
                foreach (var x in new[] { 10.0, 410.0 }) { page.DrawLine(At(x, 22), At(x, 287), 1); }
                foreach (var x in new[] { 15.0, 405.0 }) { page.DrawLine(At(x, 27), At(x, 267), 1); }
                page.DrawLine(At(100, 100), At(120, 100), 1);
                page.AddText("-K1", 8, At(100, 95), font);
            },
            pages: 2);
        var project = ProjectFile.Load(Path.Combine(Root(), "examples", "ref-machine", "ref-machine.json")).Value;
        var (pages, graphics) = (project.Pages.Count, project.Graphics.Count);
        var drawing = PdfImport.Read(path).Value;

        var plan = PdfImport.Plan(project, drawing, drawingAreaOrigin: (15, 15));

        Assert.True(plan.IsSuccess, plan.Error?.Message);
        var stack = new CommandStack(project);
        var before = ProjectJson.Serialize(project);
        Assert.True(stack.Execute(plan.Value, ChangeSource.Import, "test").IsSuccess);
        Assert.Equal(pages + 2, project.Pages.Count);
        Assert.Equal(graphics + 4, project.Graphics.Count);
        Assert.Equal([(pages + 1).ToString(), (pages + 2).ToString()], project.Pages.Skip(pages).Select(page => page.Number));
        var line = project.Graphics.Where(g => g.Shape is GraphicPolyline).Select(g => (GraphicPolyline)g.Shape).First();
        Assert.Equal([100.0, 88.0, 120.0, 88.0], line.Points.Select(v => Math.Round(v, 1)));

        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal(before, ProjectJson.Serialize(project));
    }

    [Fact]
    public void Plan_without_a_template_or_pages_is_refused()
    {
        var path = Write((builder, _) => A3(builder).DrawLine(At(10, 10), At(20, 20), 1));
        var empty = Project.Create(ProjectId.New(), "boş").Value;
        var drawing = PdfImport.Read(path).Value;

        Assert.Equal("pdf.sablon_yok", PdfImport.Plan(empty, drawing).Error!.Code);
        Assert.Equal("pdf.bos", PdfImport.Plan(empty, new PdfDrawing([], 0, 0)).Error!.Code);
    }

    [Fact]
    public void Files_that_are_not_pdf_fail_cleanly()
    {
        var broken = Path.Combine(_directory.FullName, "bozuk.pdf");
        File.WriteAllText(broken, "bu bir PDF değil");

        Assert.Equal("pdf.okunamadi", PdfImport.Read(broken).Error!.Code);
        Assert.Equal("pdf.okunamadi", PdfImport.Read(Path.Combine(_directory.FullName, "yok.pdf")).Error!.Code);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }
}
