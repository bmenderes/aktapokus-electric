using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Library;
using Ake.Serialization;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Ake.Cli.Tests;

public sealed class ImportAndDiagramCliTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    private static (int Exit, string Output, string Error) Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exit = CliApp.Run(args, output, error);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public void Import_dxf_import_list_and_diagram_sayfa_ekle_persist_to_project()
    {
        var temp = Directory.CreateTempSubdirectory("ake-cli-import-");
        try
        {
            var repoRoot = FindRepoRoot();
            var projectPath = Path.Combine(temp.FullName, "proje.json");
            var refPath = Path.Combine(repoRoot, "examples", "ref-machine", "ref-machine.json");
            var project = ProjectFile.Load(refPath).Value;
            var p1 = new ConnectionPoint(ConnectionPointId.New(), "P1", new PointAttributes(PinKind.Communication));
            var p2 = new ConnectionPoint(ConnectionPointId.New(), "P1", new PointAttributes(PinKind.Communication));
            var n1 = new Device(DeviceId.New(), "NET1", [new Function(FunctionId.New(), FunctionKind.PlcChannel, [p1])]);
            var n2 = new Device(DeviceId.New(), "NET2", [new Function(FunctionId.New(), FunctionKind.PlcChannel, [p2])]);
            Assert.True(new AddDevice(n1).Execute(project).IsSuccess);
            Assert.True(new AddDevice(n2).Execute(project).IsSuccess);
            Assert.True(new Connect(new Connection(ConnectionId.New(), p1.Id, p2.Id)).Execute(project).IsSuccess);
            Assert.True(ProjectFile.Save(project, projectPath).IsSuccess);

            // 1. ake import dxf
            var pacDxf = Path.Combine(repoRoot, "examples", "dxf", "pacdrive-lxm62.dxf");
            var (dxfExit, dxfOut, dxfErr) = Run("import", "dxf", projectPath, pacDxf);
            Assert.True(dxfExit == 0, dxfErr);
            Assert.Contains("grafik", dxfOut, StringComparison.Ordinal);

            // 2. ake import list (CSV)
            var csvPath = Path.Combine(temp.FullName, "liste.csv");
            File.WriteAllText(csvPath, "Klemens;Nr;İsim;Kod\nX1:1;%IX0.0;Start;-S1\n");
            var (listExit, listOut, listErr) = Run("import", "list", projectPath, csvPath);
            Assert.True(listExit == 0, listErr);
            Assert.Contains("yeni cihaz", listOut, StringComparison.Ordinal);

            // 3. ake haberlesme --sayfa-ekle
            var (commExit, _, commErr) = Run("haberlesme", projectPath, "--sayfa-ekle");
            Assert.True(commExit == 0, commErr);

            // 4. ake tekhat --sayfa-ekle
            var (slExit, _, slErr) = Run("tekhat", projectPath, "--sayfa-ekle");
            Assert.True(slExit == 0, slErr);

            var reloaded = ProjectFile.Load(projectPath);
            Assert.True(reloaded.IsSuccess, reloaded.Error?.Message);
            Assert.NotEmpty(reloaded.Value.Graphics);
            Assert.Contains(reloaded.Value.Controllers.SelectMany(c => c.Networks).SelectMany(n => n.Nodes).SelectMany(n => n.Modules).SelectMany(m => m.Channels), ch => ch.Address == "%IX0.0");
            Assert.Contains(reloaded.Value.Pages, p => p.Kind == PageKind.CommunicationDiagram);
            Assert.Contains(reloaded.Value.Pages, p => p.Kind == PageKind.SingleLineDiagram);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public void Import_pdf_new_tani_and_recognize_convert_devices_and_export_library()
    {
        var temp = Directory.CreateTempSubdirectory("ake-cli-pdf-tani-");
        try
        {
            const double Pt = 72.0 / 25.4;
            PdfPoint At(double x, double y) => new(x * Pt, (297 - y) * Pt);

            var builder = new PdfDocumentBuilder();
            var font = builder.AddStandard14Font(Standard14Font.Helvetica);
            var page = builder.AddPage(420 * Pt, 297 * Pt);
            page.DrawLine(At(80, 60), At(110, 60), 0.7);
            page.DrawLine(At(110, 60), At(110, 80), 0.7);
            page.DrawLine(At(110, 80), At(80, 80), 0.7);
            page.DrawLine(At(80, 80), At(80, 60), 0.7);
            page.AddText("1F1", 10, At(64, 72), font);
            page.AddText("A9R44463", 8, At(60, 79), font);
            page.AddText("1", 8, At(89, 58), font);
            page.AddText("2", 8, At(89, 85), font);

            page.DrawLine(At(80, 120), At(110, 120), 0.7);
            page.DrawLine(At(110, 120), At(110, 140), 0.7);
            page.DrawLine(At(110, 140), At(80, 140), 0.7);
            page.DrawLine(At(80, 140), At(80, 120), 0.7);
            page.AddText("1K1", 10, At(64, 132), font);
            page.AddText("A9C20863", 8, At(60, 139), font);
            page.AddText("1", 8, At(89, 118), font);
            page.AddText("2", 8, At(89, 145), font);

            page.DrawLine(At(90, 40), At(90, 60), 0.7);
            page.DrawLine(At(90, 80), At(90, 120), 0.7);
            page.DrawLine(At(90, 140), At(90, 160), 0.7);

            var pdfPath = Path.Combine(temp.FullName, "sema.pdf");
            File.WriteAllBytes(pdfPath, builder.Build());

            // 1. ake import pdf --yeni --tani --kutuphane
            var projFromPdf = Path.Combine(temp.FullName, "yeni.json");
            var libPath = Path.Combine(temp.FullName, "lib.db");
            var (exit, stdout, stderr) = Run("import", "pdf", projFromPdf, pdfPath, "--yeni", "--tani", "--kutuphane", libPath);
            Assert.True(exit == 0, stderr);
            Assert.Contains("Tanındı:", stdout, StringComparison.Ordinal);

            var loaded = ProjectFile.Load(projFromPdf);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);
            Assert.Equal(2, loaded.Value.Devices.Count);
            Assert.Single(loaded.Value.Connections);
            using (var lib = ComponentLibrary.Open(libPath).Value)
            {
                Assert.True(lib.SymbolCount >= 1);
                Assert.True(lib.Count >= 1);
            }

            // 2. Önce düz import sonra ake import recognize
            var projRaw = Path.Combine(temp.FullName, "ham.json");
            Assert.Equal(0, Run("import", "pdf", projRaw, pdfPath, "--yeni").Exit);
            var (recExit, recOut, recErr) = Run("import", "recognize", projRaw);
            Assert.True(recExit == 0, recErr);
            Assert.Contains("Tanındı:", recOut, StringComparison.Ordinal);
            Assert.Equal(2, ProjectFile.Load(projRaw).Value.Devices.Count);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }
}
