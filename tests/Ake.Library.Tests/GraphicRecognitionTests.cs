using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Library.Tests;

public sealed class GraphicRecognitionTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("ake-recog-");

    public void Dispose() => _dir.Delete(recursive: true);

    private static (Project Project, Page Page) CreateDriveAndMotorPage()
    {
        var template = StandardSheetTemplates.A3Landscape;
        var page = new Page(PageId.New(), "1", "Sürücü ve Motor", templateId: template.Id);
        var graphics = new List<PageGraphic>();

        void Add(GraphicShape s) => graphics.Add(new PageGraphic(GraphicId.New(), page.Id, s));

        // 1. Sürücü kutusu: [50, 80] - [130, 120] (genişlik 80, yükseklik 40)
        Add(new GraphicPolyline([50, 80, 130, 80, 130, 120, 50, 120], Closed: true));
        Add(new GraphicText(30, 95, "10D1/D2", 3.5));
        Add(new GraphicText(20, 102, "LXM62DD15D21000", 3.0));
        Add(new GraphicText(62, 92, "BUS BAR", 3.0));
        // Dik dönük dahili port yazısı
        Add(new GraphicText(100, 92, "A_DI1", 2.5, TextAlign.Left, -45));

        // Üst kenarda terminal halkası + bağlı olmayan klemens (CN1/1 @ x=60, y=80)
        Add(new GraphicCircle(60, 81, 1.0));
        Add(new GraphicText(59.1, 79, "CN1/1", 3.0, TextAlign.Left, -90));

        // Alt kenarda U/V/W/PE pinleri (60, 70, 80, 90 @ y=120)
        Add(new GraphicText(59.1, 128, "U", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(69.1, 128, "V", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(79.1, 128, "W", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(89.1, 128, "PE", 3.0, TextAlign.Left, -90));

        // 2. Motor çemberi: merkez (70, 180), yarıçap 7.5
        Add(new GraphicCircle(70, 180, 7.5));
        Add(new GraphicText(68, 181, "M", 3.5));
        Add(new GraphicText(48, 181, "10SRM1", 3.5));
        Add(new GraphicText(40, 187, "BMH1001P06A2A", 3.0));
        Add(new GraphicText(62, 200, "Konveyör Motor", 4.8));

        // Motor üst ve alt pinleri: U1(60, 169), V1(70, 169), PE(70, 191)
        Add(new GraphicText(59.1, 172, "U1", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(69.1, 172, "V1", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(69.1, 193, "PE", 3.0, TextAlign.Left, -90));

        // Teller: düz tel 53 (60,120 -> 60,169), L tel 55 (70,120 -> 70,150 -> 80,150 -> 80,169 değil (70,169)), U-şekilli yeşil kesikli PE (90,120 -> 90,191 -> 70,191)
        Add(new GraphicPolyline([60, 120, 60, 169]));
        Add(new GraphicText(60.5, 145, "53", 2.5, TextAlign.Left, -90));

        Add(new GraphicPolyline([70, 120, 70, 169]));
        Add(new GraphicText(70.5, 145, "55", 2.5, TextAlign.Left, -90));

        // Yeşil kesikli PE teli (köşe dönüşünde ~1.5 mm kesik boşluğu bırakır)
        for (var y = 120.0; y < 189.0; y += 4.0)
        {
            Add(new GraphicPolyline([90, y, 90, Math.Min(y + 2.5, 189.5)]) { Color = "#00ff00" });
        }

        for (var x = 88.5; x > 71.0; x -= 4.0)
        {
            Add(new GraphicPolyline([x, 191, Math.Max(x - 2.5, 70.0), 191]) { Color = "#00ff00" });
        }

        var project = Project.Create(
            ProjectId.New(),
            "Tanıma Testi",
            pages: [page],
            sheetTemplates: [template],
            graphics: graphics).Value;
        return (project, page);
    }

    [Fact]
    public void Recognizes_devices_pins_colored_dashed_wires_and_converts_with_undo()
    {
        var (project, page) = CreateDriveAndMotorPage();
        var hints = new PdfBookmarkComponent[]
        {
            new("10D1/D2", ["CN1/1", "U", "V", "W", "PE"]),
            new("10SRM1", ["U1", "V1", "PE"]),
        };

        var recognized = GraphicRecognition.RecognizePage(project, page, hints);

        Assert.Equal(2, recognized.Devices.Count);
        var drive = Assert.Single(recognized.Devices, d => d.Tag == "10D1/D2");
        Assert.Equal("Schneider Electric", drive.Part?.Manufacturer);
        Assert.Equal("LXM62DD15D21000", drive.Part?.OrderCode);
        Assert.Contains(drive.Pins, p => p.Name == "CN1/1");
        Assert.Contains(drive.Pins, p => p.Name == "U");
        Assert.Contains(drive.Pins, p => p.Name == "PE");
        Assert.Contains(drive.Symbol.Shapes.OfType<SymbolText>(), t => t.Text == "A_DI1" && Math.Abs(t.RotationDeg - (-45)) < 0.1);

        var motor = Assert.Single(recognized.Devices, d => d.Tag == "10SRM1");
        Assert.Equal(FunctionKind.Motor, motor.Kind);
        Assert.Equal("Konveyör Motor", motor.Description);
        Assert.Contains(motor.Pins, p => p.Name == "U1");
        Assert.Contains(motor.Pins, p => p.Name == "PE");

        Assert.Equal(3, recognized.Wires.Count);
        Assert.Contains(recognized.Wires, w => w.FromPinName is "U" or "U1" && w.ToPinName is "U" or "U1" && w.WireNumber == "53");
        Assert.Contains(recognized.Wires, w => w.FromPinName is "PE" && w.ToPinName is "PE" && w.WireColor == "GNYE");

        // Saf sezgisel (yer imsiz) tanıma da aynı 2 cihazı ve telleri yakalar
        var heuristic = GraphicRecognition.RecognizePage(project, page, null);
        Assert.Contains(heuristic.Devices, d => d.Tag == "10D1/D2");
        Assert.Contains(heuristic.Devices, d => d.Tag == "10SRM1");
        Assert.True(heuristic.Wires.Count >= 2);

        // Modele dönüşüm + Undo kontrolü
        var conv = GraphicRecognition.PlanConversion(
            project,
            recognized.Devices,
            recognized.Wires,
            p => (SheetGeometryLikeOriginX, SheetGeometryLikeOriginY));
        Assert.True(conv.IsSuccess, conv.Error?.Message);

        var before = ProjectJson.Serialize(project);
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(conv.Value.Transaction, ChangeSource.Import, "test").IsSuccess);
        Assert.True(project.CheckIntegrity().IsSuccess);
        Assert.Equal(2, project.Devices.Count);
        Assert.Equal(3, project.Connections.Count);
        // Motor altındaki serbest açıklama yazısı çizim üzerinde korunur, diğer tüm cihaz/tel grafikleri tüketilir
        var remaining = Assert.Single(project.Graphics);
        var remText = Assert.IsType<GraphicText>(remaining.Shape);
        Assert.Equal("Konveyör Motor", remText.Text);

        Assert.True(stack.Undo().IsSuccess);
        Assert.Equal(before, ProjectJson.Serialize(project));
    }

    private const double SheetGeometryLikeOriginX = 15;
    private const double SheetGeometryLikeOriginY = 15;

    [Fact]
    public void Groups_multi_function_device_under_one_device_and_exports_deduplicated_to_library()
    {
        var template = StandardSheetTemplates.A3Landscape;
        var page = new Page(PageId.New(), "1", "Röle", templateId: template.Id);
        var graphics = new List<PageGraphic>();
        void Add(GraphicShape s) => graphics.Add(new PageGraphic(GraphicId.New(), page.Id, s));

        // 48R1 bobin
        Add(new GraphicText(30, 100, "48R1", 3.5));
        Add(new GraphicText(30, 108, "PLC-RPT-24DC/21", 3.0));
        Add(new GraphicPolyline([40, 95, 48, 95, 48, 105, 40, 105], Closed: true));
        Add(new GraphicText(43.1, 93, "A1", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(43.1, 113, "A2", 3.0, TextAlign.Left, -90));
        Add(new GraphicPolyline([44, 80, 44, 95]));
        Add(new GraphicPolyline([44, 105, 44, 125]));

        // 48R1 kontak (aynı tag, ikinci fonksiyon)
        Add(new GraphicText(75, 100, "48R1", 3.5));
        Add(new GraphicPolyline([90, 95, 98, 95, 98, 107, 90, 107], Closed: true));
        Add(new GraphicText(93.1, 93, "14", 3.0, TextAlign.Left, -90));
        Add(new GraphicText(93.1, 115, "11", 3.0, TextAlign.Left, -90));
        Add(new GraphicPolyline([94, 80, 94, 95]));
        Add(new GraphicPolyline([94, 107, 94, 125]));

        var project = Project.Create(ProjectId.New(), "RöleTest", pages: [page], sheetTemplates: [template], graphics: graphics).Value;
        var hints = new PdfBookmarkComponent[]
        {
            new("48R1", ["A1", "A2"]),
            new("48R1", ["14", "11"]),
        };

        var rec = GraphicRecognition.RecognizePage(project, page, hints);
        Assert.Equal(2, rec.Devices.Count);
        Assert.All(rec.Devices, d => Assert.Equal("48R1", d.Tag));

        var conv = GraphicRecognition.PlanConversion(project, rec.Devices, rec.Wires, _ => (15, 15));
        Assert.True(conv.IsSuccess, conv.Error?.Message);
        Assert.True(new CommandStack(project).Execute(conv.Value.Transaction, ChangeSource.Import, "test").IsSuccess);
        var relay = Assert.Single(project.Devices);
        Assert.Equal(2, relay.Functions.Count);
        Assert.Contains(relay.Functions, f => f.Kind == FunctionKind.Coil);

        // Kütüphaneye aktarım (tekrarlı cihazı tek ürün/sembolde birleştirir)
        var libPath = Path.Combine(_dir.FullName, "kutuphane.db");
        using var lib = ComponentLibrary.Open(libPath).Value;
        var exported = GraphicRecognition.ExportToLibrary(lib, rec.Devices, "Kendi sembollerim", "Test", "Kullanıcı çizimi");
        Assert.True(exported.IsSuccess, exported.Error?.Message);
        Assert.Equal(2, exported.Value.SymbolsAdded);
        Assert.Equal(1, exported.Value.ProductsAdded);
        Assert.NotEmpty(lib.Search("PLC-RPT-24DC/21"));
        Assert.NotEmpty(lib.SearchSymbols("PLC-RPT-24DC/21"));
    }
}
