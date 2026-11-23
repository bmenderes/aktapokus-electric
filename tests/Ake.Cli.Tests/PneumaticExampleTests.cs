using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Layout;
using Ake.Library;
using Ake.Render;
using Ake.Serialization;

namespace Ake.Cli.Tests;

/// <summary>
/// Pnömatik örnek proje (ADR-0052): kompresör → hazırlama ünitesi → 5/2 selenoid valf → hız kontrol valfleri → çift etkili silindir, egzozlar
/// susturuculu. Proje standart semboller, hava hattı bağlantı türleri ve otomatik yerleşimle kurulur; <c>AKE_WRITE_EXAMPLES=1</c> ile
/// <c>examples/pnomatik/pnomatik.json</c> yeniden yazılır (isteğe bağlı <c>AKE_EXAMPLE_PNG</c> klasörüne sayfa görüntüleri de düşer).
/// </summary>
public sealed class PneumaticExampleTests : IDisposable
{
    private const string Patch = """
        {
          "patchVersion": 1,
          "description": "Pnömatik devre: çift etkili silindir kumandası",
          "commands": [
            { "op": "addDevice", "tag": "=P1+S1-M1", "description": "Kompresör", "functions": [ { "kind": "Generic", "connectionPoints": ["1"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-F1", "description": "Hava hazırlama ünitesi", "functions": [ { "kind": "Generic", "connectionPoints": ["1", "2"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-Q1", "description": "5/2 valf, selenoid / yay",
              "functions": [
                { "kind": "Generic", "connectionPoints": ["4", "2", "5", "1", "3"] },
                { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-Q2", "description": "Hız kontrol valfi (A)", "functions": [ { "kind": "Generic", "connectionPoints": ["1", "2"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-Q3", "description": "Hız kontrol valfi (B)", "functions": [ { "kind": "Generic", "connectionPoints": ["1", "2"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-M2", "description": "Çift etkili silindir", "functions": [ { "kind": "Generic", "connectionPoints": ["A", "B"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-R1", "description": "Susturucu", "functions": [ { "kind": "Generic", "connectionPoints": ["3"] } ] },
            { "op": "addDevice", "tag": "=P1+S1-R2", "description": "Susturucu", "functions": [ { "kind": "Generic", "connectionPoints": ["3"] } ] },

            { "op": "connect", "from": "=P1+S1-M1:1", "to": "=P1+S1-F1:1" },
            { "op": "connect", "from": "=P1+S1-F1:2", "to": "=P1+S1-Q1:1" },
            { "op": "connect", "from": "=P1+S1-Q1:4", "to": "=P1+S1-Q2:1" },
            { "op": "connect", "from": "=P1+S1-Q2:2", "to": "=P1+S1-M2:A" },
            { "op": "connect", "from": "=P1+S1-Q1:2", "to": "=P1+S1-Q3:1" },
            { "op": "connect", "from": "=P1+S1-Q3:2", "to": "=P1+S1-M2:B" },
            { "op": "connect", "from": "=P1+S1-Q1:3", "to": "=P1+S1-R1:3" },
            { "op": "connect", "from": "=P1+S1-Q1:5", "to": "=P1+S1-R2:3" },

            { "op": "assignSymbol", "function": "=P1+S1-M1:1", "symbol": "Kompresör" },
            { "op": "assignSymbol", "function": "=P1+S1-F1:1", "symbol": "Hava hazırlama ünitesi (FRL)" },
            { "op": "assignSymbol", "function": "=P1+S1-Q1:4", "symbol": "5/2 valf, selenoid / yay" },
            { "op": "assignSymbol", "function": "=P1+S1-Q1:A1", "symbol": "Selenoid valf bobini" },
            { "op": "assignSymbol", "function": "=P1+S1-Q2:1", "symbol": "Tek yönlü hız kontrol valfi" },
            { "op": "assignSymbol", "function": "=P1+S1-Q3:1", "symbol": "Tek yönlü hız kontrol valfi" },
            { "op": "assignSymbol", "function": "=P1+S1-M2:A", "symbol": "Silindir (çift etkili)" },
            { "op": "assignSymbol", "function": "=P1+S1-R1:3", "symbol": "Susturucu (egzoz)" },
            { "op": "assignSymbol", "function": "=P1+S1-R2:3", "symbol": "Susturucu (egzoz)" }
          ]
        }
        """;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-pnomatik-");

    public void Dispose() => _directory.Delete(recursive: true);

    private Project Build()
    {
        var libraryPath = Path.Combine(_directory.FullName, "library.db");
        Assert.True(StandardSymbols.EnsureInstalled(libraryPath).IsSuccess);
        using var library = ComponentLibrary.Open(libraryPath).Value;

        var template = StandardSheetTemplates.A3Landscape;
        var function = new StructureNode(StructureNodeId.New(), Aspect.Function, "P1", description: "Pnömatik devre");
        var location = new StructureNode(StructureNodeId.New(), Aspect.Location, "S1", description: "Makine");
        var project = Project.Create(
            ProjectId.New(),
            "Pnömatik örnek: silindir kumandası",
            structure: [function, location],
            pages:
            [
                new Page(PageId.New(), "1", "Kapak", templateId: template.Id, kind: PageKind.Cover),
                new Page(PageId.New(), "2", "Pnömatik devre şeması", function.Id, location.Id, template.Id),
            ],
            sheetTemplates: [template],
            properties: new Dictionary<string, string>
            {
                ["description"] = "Çift etkili silindirin 5/2 selenoid valfle kumandası",
                ["drawingNo"] = "PNO-001",
                ["drawnBy"] = "AKE",
                ["date"] = "19.11.2026",
                ["revision"] = "A",
            }).Value;

        var stack = new CommandStack(project);
        var patch = PatchJson.Deserialize(Patch);
        Assert.True(patch.IsSuccess, patch.Error?.Message);
        var applied = stack.Execute(patch.Value.ToCommand(library, library), ChangeSource.User, "ornek");
        Assert.True(applied.IsSuccess, applied.Error?.Message);

        // Hava hatları: besleme/çalışma hatları "Hava hattı", egzoz hatları "Egzoz hattı".
        foreach (var kind in ConnectionKind.Standard.Where(kind => kind.Category == PinKind.Pneumatic))
        {
            Assert.True(stack.Execute(new PutConnectionKind(kind), ChangeSource.User, "ornek").IsSuccess);
        }

        var air = ConnectionKind.Standard.Single(kind => kind.Name == "Hava hattı");
        var exhaust = ConnectionKind.Standard.Single(kind => kind.Name == "Egzoz hattı");
        foreach (var connection in project.Connections.ToList())
        {
            var toSilencer = project.Locate(connection.To)?.Device.ProductTag is "R1" or "R2";
            Assert.True(stack.Execute(new SetConnectionKind(connection.Id, (toSilencer ? exhaust : air).Id), ChangeSource.User, "ornek").IsSuccess);
        }

        Assert.True(stack.Execute(AutoLayout.Build(project), ChangeSource.User, "ornek").IsSuccess);
        return project;
    }

    [Fact]
    public void The_pneumatic_example_builds_with_standard_symbols_air_line_kinds_and_a_clean_erc()
    {
        var project = Build();

        Assert.Equal(8, project.Devices.Count);
        Assert.Equal(8, project.Connections.Count);
        Assert.All(project.Connections, connection => Assert.NotNull(connection.KindId));
        Assert.Equal(["Hava hattı", "Egzoz hattı"], project.Connections.Select(connection => project.FindConnectionKind(connection.KindId!.Value)!.Name).Distinct());

        // Her function standart bir sembolle çizilir ve uçları pnömatik olarak işaretlidir.
        var valve = project.Devices.Single(device => device.ProductTag == "Q1").Functions[0];
        Assert.NotNull(valve.SymbolId);
        Assert.All(valve.ConnectionPoints, point => Assert.Equal(PinKind.Pneumatic, point.Attributes.Kind));
        Assert.Equal(["4", "2", "5", "1", "3"], valve.ConnectionPoints.Select(point => point.Name));
        Assert.All(project.Placements, placement => Assert.Equal(project.Pages[1].Id, placement.PageId));
        Assert.Equal(9, project.Placements.Count); // 8 cihaz, valfin bobini ayrı function

        var erc = ErcEngine.Default.Run(project);
        Assert.False(erc.HasErrors, string.Join("; ", erc.Findings.Select(finding => finding.Message)));
    }

    [Fact]
    public void The_pneumatic_example_page_renders_and_the_example_file_can_be_written()
    {
        var project = Build();
        var scene = SheetRenderer.Render(project, project.Pages[1], new DateOnly(2026, 11, 19)).Value;

        Assert.True(scene.Items.OfType<LineItem>().Count() > 40);
        Assert.Empty(scene.Warnings);
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text.Text.Contains("M2", StringComparison.Ordinal));

        if (Environment.GetEnvironmentVariable("AKE_WRITE_EXAMPLES") == "1")
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
            {
                root = root.Parent;
            }

            var folder = Path.Combine(root!.FullName, "examples", "pnomatik");
            Directory.CreateDirectory(folder);
            Assert.True(ProjectFile.Save(project, Path.Combine(folder, "pnomatik.json")).IsSuccess);
        }

        if (Environment.GetEnvironmentVariable("AKE_EXAMPLE_PNG") is { Length: > 0 } pngFolder)
        {
            Directory.CreateDirectory(pngFolder);
            Assert.True(PngExporter.Save(scene, Path.Combine(pngFolder, "pnomatik.png"), null, 130).IsSuccess);
        }
    }

    [Fact]
    public void The_checked_in_example_file_loads_validates_and_renders_with_air_line_kinds()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(root!.FullName, "examples", "pnomatik", "pnomatik.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        Assert.Equal(0, CliApp.Run(["validate", path], output, error));

        var project = ProjectFile.Load(path).Value;
        Assert.Equal(8, project.Devices.Count);
        Assert.Equal(["Hava hattı", "Egzoz hattı"], project.ConnectionKinds.Where(kind => kind.Category == PinKind.Pneumatic && project.Connections.Any(connection => connection.KindId == kind.Id)).Select(kind => kind.Name));
        Assert.Empty(SheetRenderer.Render(project, project.Pages[1], new DateOnly(2026, 11, 19)).Value.Warnings);
    }
}
