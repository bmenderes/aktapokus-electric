using Ake.Core;
using Ake.Core.Model;

namespace Ake.Render;

/// <summary>
/// Haberleşme şeması (bus topolojisi sayfası; ADR-0037): haberleşme uçlarını (<see cref="PinKind.Communication"/>) ve haberleşme
/// kategorisindeki bağlantı türlerini taşıyan bağlantılardan, cihaz kutuları ve renkli kablolarla otomatik çıkar. Cihazlar bulundukları
/// yere (+SC1…) göre bantlara ayrılır; kutuda tag, açıklama ve ağ adresi (IP, kırmızı) yazar; kablolar türünün renk ve çizgi biçimiyle,
/// kablo adı etiketiyle çizilir; kullanılan türler sağ üstte gösterge olarak yazılır. İçerik şablonun çerçevesi ve anteti içinde, çizim
/// alanına sığacak şekilde üretilir. Kablolar hiçbir kutunun içinden geçmez: kutular arası boşluklardaki koridorlardan ve satır
/// aralıklarındaki izlerden yürür, böylece çizim elle düzeltme gerektirmez. Proje değişmez; her seferinde yeniden üretilir.
/// </summary>
public static class CommunicationDiagram
{
    private const double BoxWidth = 40;
    private const double BoxHeight = 20;
    private const double ColumnGap = 12;
    private const double Margin = 3;
    private const double StubMm = 3.5;
    private const double TrackStep = 4.2;
    private const double LaneStep = 2.4;
    private const string FontFamily = "Arial";
    private const string AddressColor = "#C00000";

    /// <summary>
    /// Haberleşme şeması tek sayfa olarak, projenin şablonunun çerçevesi ve anteti ile; haberleşme bağlantısı ya da şablon yoksa hata.
    /// Editör sayfası, PDF/PNG/SVG çıktısı ve komut satırı aynı üretimi kullanır.
    /// </summary>
    public static Result<Scene> Build(Project project, DateOnly? today = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        if (project.SheetTemplates.FirstOrDefault() is not { } template)
        {
            return Result.Failure<Scene>(new Error(
                "render.sablon_yok", "Projede sayfa şablonu yok; haberleşme şemasının çerçevesi ve anteti için bir şablon ekleyin (ake template set)."));
        }

        if (FindEdges(project).Count == 0)
        {
            return Result.Failure<Scene>(new Error(NoCommunicationCode, NoCommunicationMessage));
        }

        var page = new Page(PageId.New(), "1", "Haberleşme şeması", templateId: template.Id, kind: PageKind.CommunicationDiagram);
        return SheetRenderer.Render(project, page, today ?? DateOnly.FromDateTime(DateTime.Now));
    }

    /// <summary>Projede şemaya girecek haberleşme bağlantısı var mı.</summary>
    public static bool HasCommunication(Project project) => FindEdges(project ?? throw new ArgumentNullException(nameof(project))).Count > 0;

    internal const string NoCommunicationCode = "haberlesme.yok";

    internal const string NoCommunicationMessage =
        "Haberleşme bağlantısı yok: uçlara 'Haberleşme' türü verin ya da bağlantılara haberleşme türünde bir bağlantı türü atayın.";

    /// <summary>Sayfa içeriği: şemayı verilen çizim alanına (çerçeve ve antetin içi) sığdırarak üretir.</summary>
    internal static void AddTo(List<SceneItem> items, List<string> warnings, Project project, Box area)
    {
        var edges = FindEdges(project);
        if (edges.Count == 0)
        {
            warnings.Add(NoCommunicationMessage);
            items.Add(new TextItem(area.X + (area.Width / 2), area.Y + (area.Height / 2), NoCommunicationMessage, 3, FontFamily, TextAlign.Center));
            return;
        }

        var nodes = BuildNodes(project, edges);

        // Sığmıyorsa küçültülür; küçülünce çizim alanı daha geniş mantıksal genişlik verir, yerleşim yeniden yapılır.
        var scale = 1.0;
        Layout laid = LayOut(project, edges, nodes, area.Width);
        for (var attempt = 0; attempt < 4 && laid.Height * scale > area.Height; attempt++)
        {
            scale = area.Height / laid.Height * 0.98;
            laid = LayOut(project, edges, nodes, area.Width / scale);
        }

        if (laid.Height * scale > area.Height)
        {
            scale = area.Height / laid.Height;
        }

        if (scale < 0.45)
        {
            warnings.Add("Haberleşme şeması çizim alanına çok küçük sığdı; cihazları yer (+) bakımından gruplayın ya da büyük bir şablon seçin.");
        }

        items.AddRange(laid.Items.Select(item => Place(item, scale, area.X, area.Y)));
    }

    // ---- model

    private sealed record Edge(
        Connection Connection, Device From, Device To, ConnectionKind? Kind, string? Label, ConnectionPoint FromPoint, ConnectionPoint ToPoint);

    private sealed class Node(Device device, string group)
    {
        public Device Device { get; } = device;

        public string Group { get; } = group;
    }

    private sealed record Layout(List<SceneItem> Items, double Height);

    private static List<Edge> FindEdges(Project project)
    {
        var cores = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (core.Id, Label: $"-{cable.ProductTag}:{core.Designation}")))
            .ToDictionary(item => item.Id, item => item.Label);
        var edges = new List<Edge>();
        foreach (var connection in project.Connections)
        {
            if (project.Locate(connection.From) is not { } from || project.Locate(connection.To) is not { } to || from.Device == to.Device)
            {
                continue;
            }

            var kind = connection.KindId is { } id ? project.FindConnectionKind(id) : null;
            var communication = (from.Point.Attributes.Kind == PinKind.Communication && to.Point.Attributes.Kind == PinKind.Communication)
                || kind?.Category == PinKind.Communication;
            if (!communication)
            {
                continue;
            }

            var label = connection.Wire?.Number ?? (connection.CoreId is { } coreId && cores.TryGetValue(coreId, out var core) ? core : null);
            edges.Add(new Edge(connection, from.Device, to.Device, kind, label, from.Point, to.Point));
        }

        return edges;
    }

    private static List<Node> BuildNodes(Project project, List<Edge> edges) =>
    [
        .. edges
            .SelectMany(edge => new[] { edge.From, edge.To })
            .Distinct()
            .Select(device => new Node(device, project.StructurePath(device.LocationNodeId) is { } path ? $"+{path}" : "Genel"))
            .OrderBy(node => node.Group == "Genel" ? 1 : 0)
            .ThenBy(node => node.Group, StringComparer.Ordinal)
            .ThenBy(node => node.Device.ProductTag, StringComparer.Ordinal),
    ];

    // ---- yerleşim

    private sealed record Segment(int Gap, double From, double To);

    // Bir kablonun iki ucu: satır, taraf (üst/alt kenar), konum ve ucun kanal aralığı (alt kenar: satırın altı, üst kenar: satırın üstü).
    private sealed record End(int Row, bool Top, double X)
    {
        public int Gap => Top ? Row - 1 : Row;
    }

    private sealed record Route(Edge Edge, End A, End B, double? Corridor);

    private static Layout LayOut(Project project, List<Edge> edges, List<Node> nodes, double width)
    {
        // Satırlar: her bant (grup) kendi satırlarını alır; sığmayan cihaz sonraki satıra geçer.
        var perRow = Math.Max(1, (int)Math.Floor((width - (2 * Margin) - 5 + ColumnGap) / (BoxWidth + ColumnGap)));
        var rowOf = new Dictionary<Device, int>();
        var xOf = new Dictionary<Device, double>();
        var groupOfRow = new List<string>();
        var row = 0;
        foreach (var group in nodes.GroupBy(node => node.Group))
        {
            var index = 0;
            foreach (var node in group)
            {
                rowOf[node.Device] = row + (index / perRow);
                xOf[node.Device] = Margin + 5 + ((index % perRow) * (BoxWidth + ColumnGap));
                index++;
            }

            var used = ((index - 1) / perRow) + 1;
            for (var i = 0; i < used; i++)
            {
                groupOfRow.Add(group.Key);
            }

            row += used;
        }

        var rowCount = row;

        // Uç tarafı: kabloyu üst satıra çıkaran uç değil, alt satırdaki uç üstten girer; aynı satırdaki ve üst satırdaki uçlar alttan.
        // Bir uç birden çok kabloya bağlıysa ve biri alttan istiyorsa uç alttadır (her kablo kendi ucunun tarafından girer).
        var needsBottom = new HashSet<ConnectionPointId>();
        var needsTop = new HashSet<ConnectionPointId>();
        foreach (var edge in edges)
        {
            var (a, b) = (rowOf[edge.From], rowOf[edge.To]);
            (a < b ? needsBottom : needsTop).Add(edge.FromPoint.Id);
            (a > b ? needsBottom : needsTop).Add(edge.ToPoint.Id);
            if (a == b)
            {
                needsTop.Remove(edge.FromPoint.Id);
                needsTop.Remove(edge.ToPoint.Id);
                needsBottom.Add(edge.FromPoint.Id);
                needsBottom.Add(edge.ToPoint.Id);
            }
        }

        var topPorts = new HashSet<ConnectionPointId>(needsTop.Where(id => !needsBottom.Contains(id)));

        // Uç konumları: kutu başına, taraf başına eşit aralık.
        var portX = new Dictionary<ConnectionPointId, double>();
        var portsOfNode = new Dictionary<Device, List<ConnectionPoint>>();
        foreach (var node in nodes)
        {
            var points = edges
                .SelectMany(edge => new[] { (edge.From, edge.FromPoint), (edge.To, edge.ToPoint) })
                .Where(item => item.Item1 == node.Device)
                .Select(item => item.Item2)
                .DistinctBy(point => point.Id)
                .ToList();
            portsOfNode[node.Device] = points;
            foreach (var side in new[] { true, false })
            {
                var onSide = points.Where(point => topPorts.Contains(point.Id) == side).OrderBy(point => point.Name, StringComparer.Ordinal).ToList();
                for (var i = 0; i < onSide.Count; i++)
                {
                    portX[onSide[i].Id] = xOf[node.Device] + ((i + 1) * BoxWidth / (onSide.Count + 1));
                }
            }
        }

        // Kablo yolları: iki ucun kanalı aynı aralıktaysa doğrudan o aralıktan; değilse kutular arası bir koridordan dikey inilir.
        var corridors = Enumerable.Range(0, perRow + 1).Select(i => Margin + 5 + (i * (BoxWidth + ColumnGap)) - (ColumnGap / 2)).ToArray();
        var routes = new List<Route>();
        foreach (var edge in edges
            .OrderBy(edge => Math.Min(rowOf[edge.From], rowOf[edge.To]))
            .ThenBy(edge => Math.Min(portX[edge.FromPoint.Id], portX[edge.ToPoint.Id])))
        {
            var a = new End(rowOf[edge.From], topPorts.Contains(edge.FromPoint.Id), portX[edge.FromPoint.Id]);
            var b = new End(rowOf[edge.To], topPorts.Contains(edge.ToPoint.Id), portX[edge.ToPoint.Id]);
            double? corridor = a.Gap != b.Gap ? corridors.MinBy(x => Math.Abs(x - ((a.X + b.X) / 2))) : null;
            routes.Add(new Route(edge, a, b, corridor));
        }

        // Her satır aralığındaki yatay iz sayısı: aralık, izlere göre yükselir.
        var tracksIn = new int[Math.Max(1, rowCount)];
        foreach (var route in routes)
        {
            tracksIn[route.A.Gap]++;
            if (route.Corridor is not null)
            {
                tracksIn[route.B.Gap]++;
            }
        }

        // Satır konumları. Bant sınırındaki aralık, bant çerçevelerinin payını da taşır.
        var legendKinds = edges.Select(edge => edge.Kind).Where(kind => kind is not null).Cast<ConnectionKind>().Distinct().ToList();
        var legendHeight = legendKinds.Count == 0 ? 0 : 3 + (legendKinds.Count * 4.5) + 1;
        var rowY = new double[rowCount];
        var gapHeight = new double[rowCount];
        var groupTop = new Dictionary<string, double>();
        var groupBottom = new Dictionary<string, double>();
        var y = (legendHeight == 0 ? 0 : legendHeight + 3) + 6 + StubMm;
        for (var r = 0; r < rowCount; r++)
        {
            var group = groupOfRow[r];
            var last = r == rowCount - 1;
            var bandEnds = last || groupOfRow[r + 1] != group;
            if (!groupTop.ContainsKey(group))
            {
                groupTop[group] = y - StubMm - 6;
            }

            rowY[r] = y;
            groupBottom[group] = y + BoxHeight + StubMm + 2;
            gapHeight[r] = Math.Max(last ? 6 : 14, 7 + (tracksIn[r] * TrackStep)) + (bandEnds && !last ? 12 : 0);
            y += BoxHeight + gapHeight[r];
        }

        var items = new List<SceneItem>();
        foreach (var (group, top) in groupTop)
        {
            items.Add(new RectItem(0, top, width, groupBottom[group] - top, 0.25));
            items.Add(new TextItem(2, top + 4, group, 3, FontFamily));
        }

        foreach (var node in nodes)
        {
            DrawBox(items, node.Device, xOf[node.Device], rowY[rowOf[node.Device]]);
        }

        foreach (var node in nodes)
        {
            var (x0, y0) = (xOf[node.Device], rowY[rowOf[node.Device]]);
            foreach (var point in portsOfNode[node.Device])
            {
                var top = topPorts.Contains(point.Id);
                var x = portX[point.Id];
                items.Add(new LineItem(x, top ? y0 : y0 + BoxHeight, x, top ? y0 - StubMm : y0 + BoxHeight + StubMm, 0.35));
                items.Add(new TextItem(x + 0.7, top ? y0 - 0.8 : y0 + BoxHeight + 2.6, point.Name, 1.7, FontFamily));
            }

            _ = x0;
        }

        // Kablolar: izler sırayla verilir; koridorlarda dikey şeritler ayrı kayar.
        var trackCursor = new int[Math.Max(1, rowCount)];
        var laneCursor = new Dictionary<double, int>();
        double TrackY(int gap) => rowY[gap] + BoxHeight + StubMm + 3.5 + (trackCursor[gap]++ * TrackStep);
        double StubEnd(int rowIndex, bool top) => top ? rowY[rowIndex] - StubMm : rowY[rowIndex] + BoxHeight + StubMm;

        foreach (var route in routes)
        {
            var (color, style) = route.Edge.Kind is { } kind ? (kind.Color, kind.Style) : (null, LineStyle.Solid);
            var startY = StubEnd(route.A.Row, route.A.Top);
            var endY = StubEnd(route.B.Row, route.B.Top);
            List<(double X, double Y)> path;
            (double X1, double X2, double Y) longest;
            if (route.Corridor is { } corridor)
            {
                var lane = laneCursor.GetValueOrDefault(corridor);
                laneCursor[corridor] = lane + 1;
                var cx = corridor + (((lane % 3) - 1) * LaneStep);
                var y1 = TrackY(route.A.Gap);
                var y2 = TrackY(route.B.Gap);
                path = [(route.A.X, startY), (route.A.X, y1), (cx, y1), (cx, y2), (route.B.X, y2), (route.B.X, endY)];
                longest = Math.Abs(cx - route.A.X) >= Math.Abs(route.B.X - cx) ? (route.A.X, cx, y1) : (cx, route.B.X, y2);
            }
            else
            {
                var track = TrackY(route.A.Gap);
                path = [(route.A.X, startY), (route.A.X, track), (route.B.X, track), (route.B.X, endY)];
                longest = (route.A.X, route.B.X, track);
            }

            for (var i = 1; i < path.Count; i++)
            {
                if (Math.Abs(path[i].X - path[i - 1].X) + Math.Abs(path[i].Y - path[i - 1].Y) > 0.001)
                {
                    items.Add(new LineItem(path[i - 1].X, path[i - 1].Y, path[i].X, path[i].Y, 0.6, color, style));
                }
            }

            if (!string.IsNullOrEmpty(route.Edge.Label))
            {
                var mid = (longest.X1 + longest.X2) / 2;
                var label = route.Edge.Label;
                items.Add(new RectItem(mid - 1.2 - (label.Length * 0.65), longest.Y - 2.9, (label.Length * 1.3) + 2.4, 3.1, 0.15));
                items.Add(new TextItem(mid, longest.Y - 0.8, label, 2.2, FontFamily, TextAlign.Center));
            }
        }

        DrawLegend(items, legendKinds, width);
        return new Layout(items, y);
    }

    private static void DrawBox(List<SceneItem> items, Device device, double x, double y)
    {
        items.Add(new RectItem(x, y, BoxWidth, BoxHeight, 0.4));
        var center = x + (BoxWidth / 2);
        items.Add(new TextItem(center, y + 6, $"-{device.ProductTag}", 3, FontFamily, TextAlign.Center));
        var description = device.Description ?? device.Part?.TypeDesignation;
        if (!string.IsNullOrWhiteSpace(description))
        {
            items.Add(new TextItem(center, y + 10.5, Trim(description, 26), 2.2, FontFamily, TextAlign.Center));
        }

        if (!string.IsNullOrWhiteSpace(device.Address))
        {
            items.Add(new TextItem(center, y + 15.5, device.Address, 2.6, FontFamily, TextAlign.Center, 0, AddressColor));
        }
    }

    private static void DrawLegend(List<SceneItem> items, List<ConnectionKind> kinds, double width)
    {
        if (kinds.Count == 0)
        {
            return;
        }

        const double rowHeight = 4.5;
        const double legendWidth = 48;
        var (x, y) = (width - legendWidth - 1, 0.0);
        items.Add(new RectItem(x, y, legendWidth, 3 + (kinds.Count * rowHeight) + 1));
        items.Add(new TextItem(x + 2, y + 3, "Kablo türleri", 2.2, FontFamily));
        for (var i = 0; i < kinds.Count; i++)
        {
            var rowY = y + 3 + ((i + 1) * rowHeight) - 0.5;
            items.Add(new LineItem(x + 2, rowY - 0.8, x + 14, rowY - 0.8, 0.6, kinds[i].Color, kinds[i].Style));
            items.Add(new TextItem(x + 16, rowY, kinds[i].Name, 2.2, FontFamily));
        }
    }

    // Mantıksal koordinatı sayfaya yerleştirir: ölçek ve çizim alanı başlangıcı; kalınlık ve yazı boyutu da ölçeklenir.
    private static SceneItem Place(SceneItem item, double scale, double offsetX, double offsetY) => item switch
    {
        LineItem line => line with
        {
            X1 = (line.X1 * scale) + offsetX,
            Y1 = (line.Y1 * scale) + offsetY,
            X2 = (line.X2 * scale) + offsetX,
            Y2 = (line.Y2 * scale) + offsetY,
            StrokeMm = line.StrokeMm * Math.Max(scale, 0.6),
        },
        RectItem rect => rect with
        {
            X = (rect.X * scale) + offsetX,
            Y = (rect.Y * scale) + offsetY,
            Width = rect.Width * scale,
            Height = rect.Height * scale,
            StrokeMm = rect.StrokeMm * Math.Max(scale, 0.6),
        },
        TextItem text => text with { X = (text.X * scale) + offsetX, Y = (text.Y * scale) + offsetY, SizeMm = text.SizeMm * scale },
        _ => item,
    };

    private static string Trim(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
