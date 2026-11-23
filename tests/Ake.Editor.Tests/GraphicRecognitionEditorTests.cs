using Ake.Core.Model;
using Ake.Library;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Ake.Editor.Tests;

public sealed class GraphicRecognitionEditorTests : IDisposable
{
    private const double Pt = 72.0 / 25.4;
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("ake-ed-recog-");

    public void Dispose() => _dir.Delete(recursive: true);

    private static PdfPoint At(double xMm, double yFromTopMm) => new(xMm * Pt, (297 - yFromTopMm) * Pt);

    private string BuildSamplePdf()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(420 * Pt, 297 * Pt);

        // Cihaz 1: 1Q1 [80, 60] - [110, 80]
        page.DrawLine(At(80, 60), At(110, 60), 0.7);
        page.DrawLine(At(110, 60), At(110, 80), 0.7);
        page.DrawLine(At(110, 80), At(80, 80), 0.7);
        page.DrawLine(At(80, 80), At(80, 60), 0.7);
        page.AddText("1Q1", 10, At(64, 72), font);
        page.AddText("VCF3", 8, At(64, 79), font);
        page.AddText("1", 8, At(89, 58), font);
        page.AddText("2", 8, At(89, 85), font);
        page.AddText("3", 8, At(99, 58), font);
        page.AddText("4", 8, At(99, 85), font);

        // Cihaz 2: 1K1 [80, 120] - [110, 140]
        page.DrawLine(At(80, 120), At(110, 120), 0.7);
        page.DrawLine(At(110, 120), At(110, 140), 0.7);
        page.DrawLine(At(110, 140), At(80, 140), 0.7);
        page.DrawLine(At(80, 140), At(80, 120), 0.7);
        page.AddText("1K1", 10, At(64, 132), font);
        page.AddText("A9C20863", 8, At(60, 139), font);
        page.AddText("1", 8, At(89, 118), font);
        page.AddText("3", 8, At(99, 118), font);
        page.AddText("A1", 8, At(108, 128), font);

        // İki cihaz arasında 1 tel (pin 2 <-> pin 1 @ x=90, y=80..120), pin 4 @ x=100 boşta (sonradan manuel bağlanacak)
        page.DrawLine(At(90, 80), At(90, 120), 0.7);
        page.DrawLine(At(100, 60), At(100, 40), 0.7);
        page.DrawLine(At(100, 80), At(100, 92), 0.7);
        page.DrawLine(At(100, 108), At(100, 120), 0.7);
        page.DrawLine(At(90, 60), At(90, 40), 0.7);

        var path = Path.Combine(_dir.FullName, "ornek.pdf");
        File.WriteAllBytes(path, builder.Build());
        return path;
    }

    [Fact]
    public void Session_imports_pdf_recognizes_wires_and_pins_allows_wiring_undo_and_library_export()
    {
        using var session = new EditorSession();
        Assert.True(session.NewProject("PDF Test").IsSuccess);

        var pdf = PdfImport.Read(BuildSamplePdf()).Value;
        Assert.True(session.ImportPdf(pdf).IsSuccess);
        var pageIndex = session.PageLabels.Count - 1;
        Assert.True(session.PageHasGraphics(pageIndex));

        var rec = session.RecognizePage(pageIndex);
        Assert.True(rec.IsSuccess, rec.Error?.Message);
        Assert.Equal(2, rec.Value.Devices.Count);
        Assert.Single(rec.Value.Wires);

        var conv = session.ConvertRecognized(rec.Value.Devices, rec.Value.Wires);
        Assert.True(conv.IsSuccess, conv.Error?.Message);
        Assert.Equal(2, conv.Value.DevicesAdded);
        Assert.Equal(1, conv.Value.ConnectionsAdded);

        // Gerçek tel uçları görünüyor ve manuel tel çekmeyle yeni bağlantı kurulabiliyor
        var pins = session.PinsFor(pageIndex);
        Assert.True(pins.Count >= 6);
        var qPins = session.Project!.Devices.Single(d => d.ProductTag == "1Q1").Functions[0].ConnectionPoints;
        var kPins = session.Project.Devices.Single(d => d.ProductTag == "1K1").Functions[0].ConnectionPoints;
        var extraPinQ = pins.First(p => qPins.Any(cp => cp.Id == p.Point) && Math.Abs(p.X - 100) < 2 && Math.Abs(p.Y - 80) < 2);
        var extraPinK = pins.First(p => kPins.Any(cp => cp.Id == p.Point) && Math.Abs(p.X - 100) < 2 && Math.Abs(p.Y - 120) < 2);
        Assert.True(session.ConnectPins(extraPinQ.Point, extraPinK.Point).IsSuccess);
        Assert.Equal(2, session.Project.Connections.Count);

        // Kütüphaneye aktarım + arama
        var libPath = Path.Combine(_dir.FullName, "lib.db");
        ComponentLibrary.Open(libPath).Value.Dispose();
        Assert.True(session.OpenLibrary(libPath).IsSuccess);
        var exported = session.ExportRecognizedToLibrary(rec.Value.Devices);
        Assert.True(exported.IsSuccess, exported.Error?.Message);
        Assert.NotEmpty(session.SearchLibrary("A9C20863"));
        Assert.NotEmpty(session.SearchProducts("VCF3"));

        // Geri alma tel bağlamayı ve dönüştürmeyi temizçe geri alır
        Assert.True(session.Undo().IsSuccess);
        Assert.Single(session.Project.Connections);
        Assert.True(session.Undo().IsSuccess);
        Assert.Empty(session.Project.Devices);
    }

    [Fact]
    public void Ribbon_and_commands_include_recognition_and_show_pins()
    {
        Assert.Contains(EditorCommands.All, c => c.Id == EditorCommands.RecognizeGraphics);
        Assert.Contains(EditorCommands.All, c => c.Id == EditorCommands.ShowPins && c.Toggle);
        Assert.Contains(
            EditorCommands.Main.Tabs.Single(t => t.Label == "Araçlar").Groups,
            g => g.Items.Any(i => i.CommandId == EditorCommands.RecognizeGraphics));
        Assert.Contains(
            EditorCommands.Main.Tabs.Single(t => t.Label == "Görünüm").Groups,
            g => g.Items.Any(i => i.CommandId == EditorCommands.ShowPins));
    }
}
