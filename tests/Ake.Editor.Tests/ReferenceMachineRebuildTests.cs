using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Erc;
using Ake.Core.Model;
using Ake.Render;
using Ake.Reports;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>
/// Faz 6 DoD (ADR-0031): referans makine, <b>boş bir projeden yalnızca komutlarla</b> yeniden kurulur ve aynı raporları, aynı ERC
/// sonucunu ve aynı sayfa çizimlerini verir. Komutlar editörün kendi yolundan (<see cref="EditorSession.Run"/>) çalışır.
/// Kurulan model referansla aynı nesneleri paylaşmaz: bütün kimlikler yenidir.
/// </summary>
public sealed class ReferenceMachineRebuildTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 18);
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-rebuild-");

    public void Dispose() => _directory.Delete(recursive: true);

    private static Project Reference()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        return ProjectFile.Load(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
    }

    // Boş proje: sayfa, şablon, cihaz yok. Editör bunu açar ve komutlar sırayla her şeyi kurar.
    private EditorSession EmptySession(Project reference)
    {
        var path = Path.Combine(_directory.FullName, "bos.json");
        Assert.True(ProjectFile.Save(Project.Create(ProjectId.New(), reference.Name).Value, path).IsSuccess);
        var session = new EditorSession("dod");
        Assert.True(session.Open(path).IsSuccess);
        Assert.Empty(session.Project!.Devices);
        Assert.Empty(session.Project.Pages);
        return session;
    }

    /// <summary>
    /// Referansın her parçası için yeni kimlikli komutlar, aşamalara bölünmüş. Sıra bağımlılıklara uyar: ayarlar ve yapı, cihazlar,
    /// kablolar, bağlantılar, potansiyel işaretleri, PLC, sayfalar, yerleşimler.
    /// </summary>
    private static List<(string Phase, ICommand Command)> Script(Project reference)
    {
        var nodes = new Dictionary<StructureNodeId, StructureNodeId>();
        var devices = new Dictionary<DeviceId, DeviceId>();
        var functions = new Dictionary<FunctionId, FunctionId>();
        var points = new Dictionary<ConnectionPointId, ConnectionPointId>();
        var cores = new Dictionary<CableCoreId, CableCoreId>();
        var ports = new Dictionary<PortId, PortId>();
        var pages = new Dictionary<PageId, PageId>();
        var script = new List<(string, ICommand)>();

        StructureNodeId? Node(StructureNodeId? id) => id is { } known ? nodes[known] : null;

        // 1) Proje ayarları, şablon, tag şeması, yapı ağacı (üst düğümler önce).
        foreach (var (key, value) in reference.Properties)
        {
            script.Add(("ayarlar", new SetProjectProperty(key, value)));
        }

        if (reference.TagScheme != TagScheme.LetterNumber)
        {
            script.Add(("ayarlar", new SetTagScheme(reference.TagScheme)));
        }

        foreach (var template in reference.SheetTemplates)
        {
            script.Add(("ayarlar", new PutSheetTemplate(template)));
        }

        foreach (var symbol in reference.Symbols)
        {
            script.Add(("ayarlar", new PutSymbol(symbol)));
        }

        var remaining = reference.Structure.ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(node => node.ParentId is null || nodes.ContainsKey(node.ParentId.Value)).ToList();
            Assert.NotEmpty(ready);
            foreach (var node in ready)
            {
                var copy = new StructureNode(StructureNodeId.New(), node.Aspect, node.Name, Node(node.ParentId), node.Description);
                nodes[node.Id] = copy.Id;
                script.Add(("yapı", new AddStructureNode(copy)));
                remaining.Remove(node);
            }
        }

        // 2) Cihazlar (function ve bağlantı noktalarıyla).
        foreach (var device in reference.Devices)
        {
            var copy = new Device(
                DeviceId.New(),
                device.ProductTag,
                device.Functions.Select(function =>
                {
                    var newFunction = new Function(
                        FunctionId.New(),
                        function.Kind,
                        function.ConnectionPoints.Select(point =>
                        {
                            var newPoint = new ConnectionPoint(ConnectionPointId.New(), point.Name);
                            points[point.Id] = newPoint.Id;
                            return newPoint;
                        }).ToList(),
                        function.PassThrough,
                        function.Description,
                        function.RatedCurrentA,
                        function.SymbolId);
                    functions[function.Id] = newFunction.Id;
                    return newFunction;
                }).ToList(),
                device.Kind,
                Node(device.FunctionNodeId),
                Node(device.LocationNodeId),
                device.Description,
                device.Part,
                device.Mounting);
            devices[device.Id] = copy.Id;
            script.Add(("cihazlar", new AddDevice(copy)));
        }

        // 3) Kablolar ve damarları.
        foreach (var cable in reference.Cables)
        {
            var copy = new Cable(
                CableId.New(),
                cable.ProductTag,
                cable.Cores.Select(core =>
                {
                    var newCore = new CableCore(CableCoreId.New(), core.Designation, core.CrossSectionMm2, core.Color);
                    cores[core.Id] = newCore.Id;
                    return newCore;
                }).ToList(),
                cable.Type);
            script.Add(("kablolar", new AddCable(copy)));
        }

        // 4) Bağlantılar (tel numarası, kesit, renk, damar, köşe noktaları).
        foreach (var connection in reference.Connections)
        {
            script.Add(("bağlantılar", new Connect(new Connection(
                ConnectionId.New(),
                points[connection.From],
                points[connection.To],
                connection.Wire is { } wire ? new Wire(WireId.New(), wire.Number, wire.CrossSectionMm2, wire.Color) : null,
                connection.CoreId is { } core ? cores[core] : null,
                connection.Route))));
        }

        // 5) Potansiyel işaretleri.
        foreach (var source in reference.PotentialSources)
        {
            script.Add(("potansiyeller", new AddPotentialSource(new PotentialSource(PotentialSourceId.New(), source.Name, source.Kind, points[source.At]))));
        }

        // 6) PLC: denetleyici, ağlar, düğümler, modüller, kanallar (function atamalarıyla).
        foreach (var controller in reference.Controllers)
        {
            var networks = controller.Networks.Select(network =>
            {
                var newNodes = network.Nodes.Select(node =>
                {
                    var newPorts = node.Ports.Select(port =>
                    {
                        var newPort = new Port(PortId.New(), port.Name);
                        ports[port.Id] = newPort.Id;
                        return newPort;
                    }).ToList();
                    var newModules = node.Modules.Select(module => new Module(
                        ModuleId.New(),
                        devices[module.DeviceId],
                        module.Position,
                        module.Channels.Select(channel => new Channel(
                            ChannelId.New(),
                            channel.Index,
                            channel.SignalType,
                            channel.FunctionId is { } function ? functions[function] : null,
                            channel.Address,
                            channel.SymbolName,
                            channel.Description,
                            channel.ProcessData)).ToList())).ToList();
                    return new Node(NodeId.New(), devices[node.DeviceId], node.Name, node.Position, newModules, newPorts);
                }).ToList();
                return new Network(
                    NetworkId.New(),
                    network.Kind,
                    network.Name,
                    newNodes,
                    network.PortLinks.Select(link => new PortLink(PortLinkId.New(), ports[link.From], ports[link.To])).ToList());
            }).ToList();
            script.Add(("plc", new AddController(new Controller(ControllerId.New(), devices[controller.DeviceId], controller.Name, networks))));
        }

        // 7) Sayfalar ve yerleşimler.
        foreach (var page in reference.Pages)
        {
            var copy = new Page(PageId.New(), page.Number, page.Title, Node(page.FunctionNodeId), Node(page.LocationNodeId), page.TemplateId);
            pages[page.Id] = copy.Id;
            script.Add(("sayfalar", new AddPage(copy)));
        }

        foreach (var placement in reference.Placements)
        {
            script.Add(("yerleşimler", new AddPlacement(new Placement(
                PlacementId.New(), pages[placement.PageId], functions[placement.FunctionId], placement.Role, placement.X, placement.Y, placement.RotationDeg))));
        }

        return script;
    }

    private static string Describe(ErcFinding finding) => $"{finding.Severity}|{finding.Message}";

    // Bağlantı kimlikleri (PDF hedef adları) iki projede farklıdır; sayfa sırasıyla değiştirilir.
    private static List<SceneItem> Normalized(Project project, Scene scene)
    {
        var anchors = project.Pages.Select((page, index) => (Anchor: SheetRenderer.AnchorOf(page), Index: index)).ToDictionary(item => item.Anchor, item => item.Index);
        return [.. scene.Items.Select(item => item is LinkItem link ? link with { Target = $"sayfa#{anchors[link.Target]}" } : item)];
    }

    [Fact]
    public void The_reference_machine_is_rebuilt_from_an_empty_project_with_commands_only_and_gives_the_same_reports_erc_and_pages()
    {
        var reference = Reference();
        using var session = EmptySession(reference);

        foreach (var phase in Script(reference).GroupBy(step => step.Phase, step => step.Command))
        {
            var result = session.Run(new Transaction($"DoD: {phase.Key}", phase));
            Assert.True(result.IsSuccess, $"{phase.Key}: {result.Error?.Message}");
        }

        var rebuilt = session.Project!;
        Assert.True(rebuilt.CheckIntegrity().IsSuccess);

        // Aynı nesneleri paylaşmıyor ve aynı içerik var.
        Assert.Empty(rebuilt.Devices.Select(device => device.Id).Intersect(reference.Devices.Select(device => device.Id)));
        Assert.Equal(
            (reference.Devices.Count, reference.Connections.Count, reference.Cables.Count, reference.PotentialSources.Count, reference.Controllers.Count, reference.Pages.Count, reference.Placements.Count, reference.Structure.Count),
            (rebuilt.Devices.Count, rebuilt.Connections.Count, rebuilt.Cables.Count, rebuilt.PotentialSources.Count, rebuilt.Controllers.Count, rebuilt.Pages.Count, rebuilt.Placements.Count, rebuilt.Structure.Count));

        // Aynı raporlar (BOM, kablo, tel, klemens planı, I/O listesi).
        foreach (var name in ReportCatalog.Names)
        {
            Assert.Equal(
                CsvReport.Write(ReportCatalog.Build(name, reference).Value),
                CsvReport.Write(ReportCatalog.Build(name, rebuilt).Value));
        }

        // Aynı ERC sonucu.
        var expectedErc = ErcEngine.Default.Run(reference);
        Assert.Equal(expectedErc.Findings.Select(Describe).Order(), session.Erc!.Findings.Select(Describe).Order());
        Assert.False(session.Erc.HasErrors);

        // Aynı sayfa çizimleri: konum, yazı, tel yolu, çapraz referans bağlantıları.
        for (var index = 0; index < reference.Pages.Count; index++)
        {
            var expected = SheetRenderer.Render(reference, reference.Pages[index], Today).Value;
            var actual = SheetRenderer.Render(rebuilt, rebuilt.Pages[index], Today).Value;
            Assert.Equal(expected.Warnings, actual.Warnings);
            Assert.Equal(Normalized(reference, expected), Normalized(rebuilt, actual));
        }
    }

    [Fact]
    public void The_whole_rebuild_can_be_undone_step_by_step_back_to_the_empty_project()
    {
        var reference = Reference();
        using var session = EmptySession(reference);
        var steps = Script(reference).GroupBy(step => step.Phase, step => step.Command).ToList();
        foreach (var phase in steps)
        {
            Assert.True(session.Run(new Transaction($"DoD: {phase.Key}", phase)).IsSuccess);
        }

        foreach (var _ in steps)
        {
            Assert.True(session.Undo().IsSuccess);
        }

        Assert.False(session.CanUndo);
        Assert.Empty(session.Project!.Devices);
        Assert.Empty(session.Project.Pages);
        Assert.Empty(session.Project.Structure);
        Assert.Empty(session.Project.Cables);
        Assert.Empty(session.Project.Controllers);
    }

    [Fact]
    public void A_broken_step_stops_the_rebuild_with_a_clear_error_and_changes_nothing_in_that_phase()
    {
        var reference = Reference();
        using var session = EmptySession(reference);
        var phases = Script(reference).GroupBy(step => step.Phase, step => step.Command).ToList();

        // "cihazlar" aşamasını yapı düğümleri olmadan çalıştırmak, referanssız düğüm yüzünden reddedilir.
        var devices = phases.First(phase => phase.Key == "cihazlar");
        var result = session.Run(new Transaction("DoD: cihazlar (yapısız)", devices));

        Assert.True(result.IsFailure);
        Assert.Empty(session.Project!.Devices);
        Assert.False(session.CanUndo);
    }
}
