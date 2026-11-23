using Ake.Core.Model;

namespace Ake.Render.Tests;

/// <summary>Haberleşme şeması: haberleşme uçlarından ve türlü bağlantılardan otomatik topoloji sayfası (ADR-0037).</summary>
public class CommunicationDiagramTests
{
    private static readonly PointAttributes Comm = new(PinKind.Communication, ConnectorType.Rj45);

    internal static Project Topology()
    {
        var sc1 = new StructureNode(StructureNodeId.New(), Aspect.Location, "SC1");
        var st1 = new StructureNode(StructureNodeId.New(), Aspect.Location, "ST1");

        Device Dev(string tag, StructureNode place, string description, string? address, params string[] ports) => new(
            DeviceId.New(),
            tag,
            [new Function(FunctionId.New(), FunctionKind.Generic, ports.Select(name => new ConnectionPoint(ConnectionPointId.New(), name, Comm)))],
            locationNodeId: place.Id,
            description: description,
            address: address);

        var kf0 = Dev("KF0", sc1, "NCU 710.3B PN", "192.168.214.1", "X150", "X120");
        var kf5 = Dev("KF5", sc1, "Scalance X108", null, "P1", "P2", "P3", "P4", "P5", "P6");
        var kf30 = Dev("KF30", sc1, "IM153-4 PN IO HF", "192.168.0.50", "X1_P1", "X1_P2");
        var kf50 = Dev("KF50", sc1, "IM151-3 PN", "192.168.0.51", "X1_P1");
        var a20 = Dev("A20", sc1, "OP 012 PCU 50.5-C", "192.168.214.241", "X2");
        var x30 = Dev("X30", st1, "Box PN Plus", null, "P1", "P2");
        var a30 = Dev("A30", st1, "HT8", null, "X1");

        var profinet = ConnectionKind.Standard.First(kind => kind.Name == "Profinet");
        var ethernet = ConnectionKind.Standard.First(kind => kind.Name == "Ethernet");

        ConnectionPointId Point(Device device, string name) => device.Functions[0].ConnectionPoints.Single(point => point.Name == name).Id;
        Connection Link(Device a, string pa, Device b, string pb, ConnectionKind kind, string cable) =>
            new(ConnectionId.New(), Point(a, pa), Point(b, pb), new Wire(WireId.New(), cable), kindId: kind.Id);

        var template = Ake.Serialization.SheetTemplateJson.Deserialize(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "templates", "a3-yatay.sablon.json"))).Value;
        var page = new Page(PageId.New(), "1", "Haberleşme şeması", templateId: template.Id, kind: PageKind.CommunicationDiagram);

        return Project.Create(
            ProjectId.New(),
            "Bus topolojisi",
            pages: [page],
            sheetTemplates: [template],
            structure: [sc1, st1],
            devices: [kf0, kf5, kf30, kf50, a20, x30, a30],
            connections:
            [
                Link(kf0, "X150", kf5, "P1", profinet, "-WKF0.3"),
                Link(kf30, "X1_P2", kf5, "P5", profinet, "-WKF30"),
                Link(kf50, "X1_P1", kf30, "X1_P1", profinet, "-WKF50"),
                Link(a20, "X2", kf5, "P2", ethernet, "-WKF1"),
                Link(kf0, "X120", a20, "X2", ethernet, "-WKF0.2"),
                Link(x30, "P1", kf5, "P3", ethernet, "-WA10"),
                Link(a30, "X1", x30, "P2", ethernet, "-WA30"),
            ],
            connectionKinds: [profinet, ethernet]).Value;
    }

    [Fact]
    public void The_topology_page_shows_devices_by_location_with_ip_cable_names_colors_and_a_legend()
    {
        var scene = CommunicationDiagram.Build(Topology()).Value;
        var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();

        Assert.Contains("Haberleşme şeması", texts);
        Assert.Contains("+SC1", texts);
        Assert.Contains("+ST1", texts);
        Assert.Contains("-KF5", texts);
        Assert.Contains("Scalance X108", texts);
        Assert.Contains(scene.Items.OfType<TextItem>(), text => text is { Text: "192.168.0.50", Color: "#C00000" });
        Assert.Contains("-WKF30", texts);
        Assert.Contains("-WA30", texts);
        Assert.Contains("P5", texts);
        Assert.Contains(scene.Items.OfType<LineItem>(), line => line is { Color: "#2E9E2E", Style: LineStyle.Solid });
        Assert.Contains(scene.Items.OfType<LineItem>(), line => line is { Color: "#1B5E20", Style: LineStyle.DashDot });
        Assert.Contains("Kablo türleri", texts);
        Assert.Contains("Profinet", texts);
        Assert.Contains("Ethernet", texts);
        Assert.Equal(420, scene.WidthMm);
    }

    [Fact]
    public void Every_cable_is_drawn_once_and_devices_without_communication_are_left_out()
    {
        var project = Topology();
        var scene = CommunicationDiagram.Build(project).Value;

        var colored = scene.Items.OfType<LineItem>().Where(line => line.Color is not null && line.StrokeMm == 0.6).ToList();
        Assert.True(colored.Count >= project.Connections.Count, $"{colored.Count} çizgi");
        Assert.DoesNotContain(scene.Items.OfType<TextItem>(), text => text.Text == "-Q1");
    }

    [Fact]
    public void Without_any_communication_connection_the_builder_says_so()
    {
        var project = Ake.Serialization.ProjectFile.Load(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "ref-machine", "ref-machine.json")).Value;

        Assert.Equal("haberlesme.yok", CommunicationDiagram.Build(project).Error!.Code);
    }

    [Fact]
    public void The_diagram_renders_to_png_and_svg()
    {
        var scene = CommunicationDiagram.Build(Topology()).Value;
        var path = Path.Combine(Path.GetTempPath(), $"ake-haberlesme-{Guid.NewGuid():N}.png");
        try
        {
            Assert.True(PngExporter.Save(scene, path, null, 60).IsSuccess);
            Assert.True(new FileInfo(path).Length > 1000);
        }
        finally
        {
            File.Delete(path);
        }

        var svg = SvgExporter.Write(scene);
        Assert.Contains("fill=\"#C00000\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke-dasharray", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void The_page_has_the_frame_and_title_block_and_everything_stays_inside_the_drawing_area()
    {
        var project = Topology();
        var template = project.SheetTemplates[0];
        var area = SheetGeometry.For(template).DrawingArea;
        var scene = SheetRenderer.Render(project, project.Pages[0], new DateOnly(2026, 11, 18)).Value;

        // Antet: şablonun yer tutucuları dolu (proje adı, sayfa başlığı, sayfa sayısı).
        var texts = scene.Items.OfType<TextItem>().Select(text => text.Text).ToList();
        Assert.Contains("Bus topolojisi", texts);
        Assert.Contains("Haberleşme şeması", texts);
        Assert.Contains(scene.Items.OfType<RectItem>(), rect => rect.Width > template.WidthMm - (2 * template.MarginMm) - 1);

        // Şema içeriği (kutular, kablolar, bantlar, gösterge) çizim alanının dışına taşmaz.
        var content = scene.Items.OfType<LineItem>().Where(line => line.Color is not null).ToList();
        Assert.NotEmpty(content);
        Assert.All(content, line =>
        {
            Assert.InRange(line.X1, area.X - 0.01, area.Right + 0.01);
            Assert.InRange(line.X2, area.X - 0.01, area.Right + 0.01);
            Assert.InRange(line.Y1, area.Y - 0.01, area.Bottom + 0.01);
            Assert.InRange(line.Y2, area.Y - 0.01, area.Bottom + 0.01);
        });
    }

    [Fact]
    public void No_cable_runs_through_a_device_box()
    {
        var project = Topology();
        var scene = SheetRenderer.Render(project, project.Pages[0], new DateOnly(2026, 11, 18)).Value;
        var boxes = scene.Items.OfType<RectItem>().Where(rect => Math.Abs(rect.StrokeMm - 0.4) < 0.001).ToList();
        var cables = scene.Items.OfType<LineItem>().Where(line => line.Color is not null && Math.Abs(line.StrokeMm - 0.6) < 0.001).ToList();

        Assert.Equal(7, boxes.Count);
        Assert.NotEmpty(cables);
        foreach (var cable in cables)
        {
            foreach (var box in boxes)
            {
                // Çizgi yatay ya da dikeydir; kutunun iç bölgesiyle (kenarlardan 0,2 mm içeride) kesişmemeli.
                var (x1, x2) = (Math.Min(cable.X1, cable.X2), Math.Max(cable.X1, cable.X2));
                var (y1, y2) = (Math.Min(cable.Y1, cable.Y2), Math.Max(cable.Y1, cable.Y2));
                var overlapsX = x2 > box.X + 0.2 && x1 < (box.X + box.Width) - 0.2;
                var overlapsY = y2 > box.Y + 0.2 && y1 < (box.Y + box.Height) - 0.2;
                Assert.False(overlapsX && overlapsY, $"Kablo ({cable.X1:0.#},{cable.Y1:0.#})–({cable.X2:0.#},{cable.Y2:0.#}) kutunun içinden geçiyor");
            }
        }
    }

    [Fact]
    public void A_far_apart_cable_uses_a_corridor_between_the_boxes_and_a_project_without_a_template_is_refused()
    {
        var project = Topology();
        var (a, b) = (project.Devices.Single(d => d.ProductTag == "A30"), project.Devices.Single(d => d.ProductTag == "KF50"));
        var profinet = ConnectionKind.Standard.First(kind => kind.Name == "Profinet");
        var far = new Connection(
            ConnectionId.New(), a.Functions[0].ConnectionPoints[0].Id, b.Functions[0].ConnectionPoints[0].Id, new Wire(WireId.New(), "-WFAR"), kindId: profinet.Id);
        Assert.True(new Ake.Core.Commands.Connect(far).Execute(project).IsSuccess);

        var scene = SheetRenderer.Render(project, project.Pages[0], new DateOnly(2026, 11, 18)).Value;

        Assert.Contains("-WFAR", scene.Items.OfType<TextItem>().Select(text => text.Text));
        var boxes = scene.Items.OfType<RectItem>().Where(rect => Math.Abs(rect.StrokeMm - 0.4) < 0.001).ToList();
        foreach (var cable in scene.Items.OfType<LineItem>().Where(line => line.Color is not null && Math.Abs(line.StrokeMm - 0.6) < 0.001))
        {
            Assert.DoesNotContain(boxes, box =>
                Math.Max(cable.X1, cable.X2) > box.X + 0.2 && Math.Min(cable.X1, cable.X2) < (box.X + box.Width) - 0.2
                && Math.Max(cable.Y1, cable.Y2) > box.Y + 0.2 && Math.Min(cable.Y1, cable.Y2) < (box.Y + box.Height) - 0.2);
        }

        var withoutTemplate = Project.Create(ProjectId.New(), "Şablonsuz", structure: project.Structure, devices: project.Devices, connections: project.Connections, connectionKinds: project.ConnectionKinds).Value;
        Assert.Equal("render.sablon_yok", CommunicationDiagram.Build(withoutTemplate).Error!.Code);
    }

    [Fact]
    public void Many_devices_are_scaled_down_to_fit_the_drawing_area()
    {
        var sc1 = new StructureNode(StructureNodeId.New(), Aspect.Location, "SC1");
        var devices = Enumerable.Range(1, 40).Select(i => new Device(
            DeviceId.New(),
            $"N{i}",
            [new Function(FunctionId.New(), FunctionKind.Generic, [new ConnectionPoint(ConnectionPointId.New(), "P1", Comm), new ConnectionPoint(ConnectionPointId.New(), "P2", Comm)])],
            locationNodeId: sc1.Id,
            description: $"Cihaz {i}",
            address: $"10.0.0.{i}")).ToList();
        var template = Ake.Serialization.SheetTemplateJson.Deserialize(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "examples", "templates", "a3-yatay.sablon.json"))).Value;
        var chain = Enumerable.Range(0, 39).Select(i => new Connection(
            ConnectionId.New(), devices[i].Functions[0].ConnectionPoints[1].Id, devices[i + 1].Functions[0].ConnectionPoints[0].Id)).ToList();
        var project = Project.Create(
            ProjectId.New(), "Büyük", pages: [new Page(PageId.New(), "1", "Haberleşme", templateId: template.Id, kind: PageKind.CommunicationDiagram)],
            sheetTemplates: [template], structure: [sc1], devices: devices, connections: chain,
            connectionKinds: [ConnectionKind.Standard[0]]).Value;
        var area = SheetGeometry.For(template).DrawingArea;

        var scene = SheetRenderer.Render(project, project.Pages[0], new DateOnly(2026, 11, 18)).Value;

        var lines = scene.Items.OfType<LineItem>().Where(line => line.Color is null && line.StrokeMm < 0.45).ToList();
        Assert.True(scene.Items.OfType<RectItem>().Count(rect => rect.Width < 40) >= 40, "Kutular küçülmüş olmalı");
        Assert.All(scene.Items.OfType<TextItem>().Where(text => text.Text.StartsWith("10.0.0.", StringComparison.Ordinal)), text =>
        {
            Assert.InRange(text.Y, area.Y, area.Bottom);
            Assert.InRange(text.X, area.X, area.Right);
        });
        Assert.NotEmpty(lines);
    }
}
