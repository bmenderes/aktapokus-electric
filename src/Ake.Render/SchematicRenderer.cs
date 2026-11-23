using System.Globalization;
using Ake.Core.Model;

namespace Ake.Render;

/// <summary>
/// Sayfadaki yerleşimleri (semboller), aralarındaki telleri ve başka sayfaya giden bağlantıların
/// çapraz referanslarını sahneye çizer. Yerleşim konumu çizim alanının sol üst köşesine göredir.
/// </summary>
internal static class SchematicRenderer
{
    private const double TagSizeMm = 2.5;
    private const double PinNameSizeMm = 1.6;
    private const double NoteSizeMm = 1.8;
    private const double TagNeighbourMm = 8;
    private const double MirrorSizeMm = 1.6;

    // Bobin alt ucu ile ayna tablosu arasındaki boşluk: sayfa notu uzunluğu kadar.
    private const double MirrorGapMm = 15;
    private const string UnroutedColor = "#e0007a";
    private const double JunctionRadiusMm = 0.6;

    /// <param name="warnings">Çizimde göze görünmeyen sorunlar (ör. yolu bulunamayan tel) buraya eklenir.</param>
    /// <param name="wirePaths">Verilirse her telin çizilen yolu buraya da eklenir (seçim için).</param>
    public static void Add(
        List<SceneItem> items,
        List<string> warnings,
        Project project,
        Page page,
        SheetTemplate template,
        SheetGeometry geometry,
        List<WirePath>? wirePaths = null)
    {
        var placed = PlacePins(project, page, geometry);
        if (placed.Symbols.Count == 0)
        {
            return;
        }

        var font = template.FontFamily;
        var labels = new List<Box>();
        foreach (var symbol in placed.Symbols)
        {
            // Aynı cihazın yan yana duran sembollerinde (kutuplar) tag yalnızca en soldakine yazılır.
            var hasLeftNeighbour = placed.Symbols.Any(other =>
                other != symbol
                && ReferenceEquals(other.Device, symbol.Device)
                && Math.Abs(other.Bounds.Y - symbol.Bounds.Y) < 0.001
                && other.Bounds.X < symbol.Bounds.X
                && symbol.Bounds.X - other.Bounds.Right <= TagNeighbourMm);
            // Kavşak noktasının tag'i ve uç adı yazılmaz; yalnızca nokta çizilir.
            var junction = symbol.Function.Kind == FunctionKind.Junction;
            DrawSymbol(items, symbol, font, drawTag: !hasLeftNeighbour && !junction);
            AddCrossReferences(items, labels, project, symbol, font, drawTag: !hasLeftNeighbour && !junction);
            if (!hasLeftNeighbour && !junction)
            {
                // Tag yazısının kapladığı yer de engeldir; tel yazının üzerinden geçmez.
                var tagWidth = (symbol.Device.ProductTag.Length + 1) * TagSizeMm * 0.6;
                labels.Add(new Box(
                    symbol.Bounds.X - 1.5 - tagWidth,
                    symbol.Bounds.Y + (Math.Min(symbol.Bounds.Height, Symbol.Length) / 2) - 1.5,
                    tagWidth,
                    3));
            }
        }

        var cards = PlcCardFrames(placed.Symbols);
        items.AddRange(cards.Select(card => new RectItem(card.X, card.Y, card.Width, card.Height)));

        var connectivity = Connectivity.Compute(project);
        var netIds = new Dictionary<Net, int>(ReferenceEqualityComparer.Instance);
        int NetId(ConnectionPointId point)
        {
            var net = connectivity.NetOf(point);
            if (!netIds.TryGetValue(net, out var id))
            {
                id = netIds.Count + 1;
                netIds[net] = id;
            }

            return id;
        }

        var cores = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (core.Id, Label: $"-{cable.ProductTag}:{core.Designation}")))
            .ToDictionary(item => item.Id, item => item.Label);
        var wires = new List<(Connection Connection, PlacedPin From, PlacedPin To)>();
        var usedKinds = new List<ConnectionKind>();
        var notes = new Dictionary<ConnectionPointId, RemoteNotes>();

        foreach (var connection in project.Connections)
        {
            var from = placed.Pins.GetValueOrDefault(connection.From);
            var to = placed.Pins.GetValueOrDefault(connection.To);

            if (from is not null && to is not null)
            {
                wires.Add((connection, from, to));
            }
            else if (from is not null || to is not null)
            {
                var here = from ?? to!;
                var otherPoint = from is not null ? connection.To : connection.From;
                if (RemoteTarget(project, otherPoint) is not { } target)
                {
                    continue;
                }

                var targetPage = project.Locate(otherPoint) is { } remote ? PageOf(project, remote.Function.Id) : null;

                if (!notes.TryGetValue(here.Point, out var pinNotes))
                {
                    pinNotes = new RemoteNotes(here, WireLook(project, connection).Color);
                    notes[here.Point] = pinNotes;
                }

                pinNotes.Add(connectivity.PotentialOf(here.Point).Name ?? ShortTag(project, otherPoint), target, targetPage?.Id);
            }
        }

        var routed = wires.SelectMany(wire => new[] { wire.From, wire.To })
            .Concat(notes.Values.Select(note => note.Pin))
            .DistinctBy(pin => pin.Point)
            .Select(pin => (pin.Router, NetId(pin.Point)))
            .ToList();
        var router = new WireRouter(
            geometry.DrawingArea, [.. placed.Symbols.Select(symbol => symbol.Bounds), .. cards, .. labels], routed);

        // Kısa teller önce: uzun teller onların çevresinden dolaşır.
        var ordered = wires
            .Select((wire, index) => (wire, index))
            .OrderBy(item => Math.Abs(item.wire.From.Router.EndX - item.wire.To.Router.EndX)
                + Math.Abs(item.wire.From.Router.EndY - item.wire.To.Router.EndY))
            .ThenBy(item => item.index);

        foreach (var ((connection, from, to), _) in ordered)
        {
            // Telin üzerine tel numarası yazılır; bağlantı bir kablo damarıysa damarın adı (-W1:2).
            var label = connection.Wire?.Number
                ?? (connection.CoreId is { } coreId && cores.TryGetValue(coreId, out var core) ? core : null);
            if (connection.Route.Count > 0)
            {
                // Kullanıcının verdiği köşe noktaları: otomatik yönlendirme atlanır (ADR-0029).
                var manual = ManualPath(from.Router, to.Router, connection.Route);
                var look = WireLook(project, connection);
                DrawPath(items, manual, look.Color, label, font, look.Style);
                NoteKind(usedKinds, look.Kind);
                wirePaths?.Add(new WirePath(connection.Id, label, manual));
                continue;
            }

            var path = router.Route(from.Router, to.Router, NetId(from.Point));
            if (path is null)
            {
                // Engelden kaçan yol yok: tel düz çizilir ama kırmızıyla, ve kullanıcıya bildirilir; yoksa
                // bir sembolün içinden geçen tel fark edilmez.
                warnings.Add(
                    $"Tel {label ?? "(numarasız)"} ({ShortTag(project, connection.From)} – {ShortTag(project, connection.To)}) için engelsiz yol bulunamadı; düz çizildi.");
                var direct = DirectPath(from.Router, to.Router);
                DrawPath(items, direct, UnroutedColor, label, font);
                wirePaths?.Add(new WirePath(connection.Id, label, direct));
                continue;
            }

            var routedLook = WireLook(project, connection);
            DrawPath(items, path, routedLook.Color, label, font, routedLook.Style);
            NoteKind(usedKinds, routedLook.Kind);
            wirePaths?.Add(new WirePath(connection.Id, label, path));
        }

        foreach (var (x, y) in router.Junctions())
        {
            items.Add(new CircleItem(x, y, JunctionRadiusMm, 0.25, Filled: true));
        }

        foreach (var note in notes.Values)
        {
            DrawCrossReference(items, note, font);
        }

        DrawLegend(items, usedKinds, geometry.DrawingArea, font);
    }

    private static void NoteKind(List<ConnectionKind> used, ConnectionKind? kind)
    {
        if (kind is not null && !used.Contains(kind))
        {
            used.Add(kind);
        }
    }

    // Sayfada kullanılan bağlantı türleri: çizim alanının sağ üstünde, çizgi örneği ve adıyla otomatik gösterge (ADR-0036).
    private static void DrawLegend(List<SceneItem> items, List<ConnectionKind> kinds, Box area, string fontFamily)
    {
        if (kinds.Count == 0)
        {
            return;
        }

        const double rowHeight = 4.5;
        const double width = 46;
        var (x, y) = (area.Right - width - 2, area.Y + 2);
        items.Add(new RectItem(x, y, width, 3 + (kinds.Count * rowHeight) + 1));
        items.Add(new TextItem(x + 2, y + 3, "Bağlantı türleri", 2.2, fontFamily));
        for (var i = 0; i < kinds.Count; i++)
        {
            var rowY = y + 3 + ((i + 1) * rowHeight) - 0.5;
            items.Add(new LineItem(x + 2, rowY - 0.8, x + 14, rowY - 0.8, 0.5, kinds[i].Color, kinds[i].Style));
            items.Add(new TextItem(x + 16, rowY, kinds[i].Name, 2.2, fontFamily));
        }
    }

    /// <summary>
    /// Kontaktör ve röle çapraz referansları. Bobinin altına <b>kontak ayna tablosu</b> yazılır: cihazın
    /// her kontağı, türü (G güç, NO, NC), uç numaraları ve çizildiği <c>sayfa.kolon</c>. Kontakların
    /// tag'inin altına da bobinin yeri yazılır. Yazıların kapladığı yer tel yolu için engeldir.
    /// </summary>
    private static void AddCrossReferences(
        List<SceneItem> items, List<Box> obstacles, Project project, PlacedSymbol symbol, string fontFamily, bool drawTag)
    {
        var device = symbol.Device;
        var coil = device.Functions.FirstOrDefault(function => function.Kind == FunctionKind.Coil);
        if (coil is null)
        {
            return;
        }

        if (symbol.Function.Id == coil.Id)
        {
            var contacts = device.Functions
                .Where(function => function.Kind is FunctionKind.PowerContact or FunctionKind.NoContact or FunctionKind.NcContact)
                .ToList();
            if (contacts.Count == 0)
            {
                return;
            }

            // Tablo, bobinin alt ucundan inen telin sağında, uçların dik yazılan sayfa notlarının
            // (ör. "-K0:14 / 5.2") altında durur; yan kontağın notuyla üst üste binmez.
            var x = symbol.Bounds.X + (symbol.Bounds.Width / 2) + 1.5;
            var y = symbol.Bounds.Bottom + WireRouter.StubMm + MirrorGapMm;
            var contactPages = contacts.Select(contact => PageOf(project, contact.Id)).ToList();
            var lines = contacts.Select(contact =>
            {
                var kind = contact.Kind switch
                {
                    FunctionKind.PowerContact => "G ",
                    FunctionKind.NoContact => "NO",
                    _ => "NC",
                };
                var points = string.Join("-", contact.ConnectionPoints.Select(point => point.Name));
                return $"{kind} {points,-5} {PageColumnOf(project, contact.Id) ?? "—"}";
            }).ToList();

            var width = lines.Max(line => line.Length) * MirrorSizeMm * 0.6;
            items.Add(new LineItem(x, y, x + width, y, 0.18));
            for (var i = 0; i < lines.Count; i++)
            {
                items.Add(new TextItem(x, y + ((i + 1) * MirrorSizeMm * 1.3), lines[i], MirrorSizeMm, fontFamily));
                if (contactPages[i] is { } target)
                {
                    items.Add(new LinkItem(x, y + (i * MirrorSizeMm * 1.3) + (MirrorSizeMm * 0.3), width, MirrorSizeMm * 1.3, SheetRenderer.AnchorOf(target)));
                }
            }

            obstacles.Add(new Box(x - 0.5, y - 0.5, width + 1, (lines.Count * MirrorSizeMm * 1.3) + 1));
            return;
        }

        // Kontak: tag'in altına bobinin yeri. Kutuplardan yalnızca tag'i yazılan ilkine.
        if (drawTag && PageColumnOf(project, coil.Id) is { } coilAt)
        {
            var tagY = symbol.Bounds.Y + (Math.Min(symbol.Bounds.Height, Symbol.Length) / 2) + 1;
            var text = $"({coilAt})";
            items.Add(new TextItem(symbol.Bounds.X - 1.5, tagY + MirrorSizeMm + 1, text, MirrorSizeMm, fontFamily, TextAlign.Right));
            var width = text.Length * MirrorSizeMm * 0.6;
            if (PageOf(project, coil.Id) is { } coilPage)
            {
                items.Add(new LinkItem(symbol.Bounds.X - 1.5 - width, tagY + 1, width, MirrorSizeMm + 0.5, SheetRenderer.AnchorOf(coilPage)));
            }

            obstacles.Add(new Box(symbol.Bounds.X - 1.5 - width, tagY + 1, width, MirrorSizeMm + 1));
        }
    }

    /// <summary>Sayfadaki her yerleşimin sembol kutusu, sayfa koordinatlarında (seçim ve taşıma için).</summary>
    /// <summary>Sayfadaki bütün bağlantı noktalarının yeri, sayfa koordinatlarında (tel çekmek için).</summary>
    public static IReadOnlyList<PlacedPinInfo> Pins(Project project, Page page, SheetGeometry geometry) =>
        [.. PlacePins(project, page, geometry).Pins.Values.Select(pin => new PlacedPinInfo(
            pin.Point, pin.Name, pin.Router.X, pin.Router.Y, pin.Router.DirectionX, pin.Router.DirectionY))];

    public static IReadOnlyList<PlacedBox> Boxes(Project project, Page page, SheetGeometry geometry) =>
        [.. PlacePins(project, page, geometry).Symbols.Select(symbol => new PlacedBox(symbol.Placement, symbol.Bounds))];

    /// <summary>Başka sayfadaki ucun konumu, <c>sayfa.kolon</c> biçiminde; uç hiçbir sayfada değilse <c>null</c>.</summary>
    private static string? RemoteTarget(Project project, ConnectionPointId point)
    {
        if (project.Locate(point) is not { } location)
        {
            return null;
        }

        return PageColumnOf(project, location.Function.Id);
    }

    /// <summary>Function'ın ana görünümünün sayfası; hiçbir sayfada değilse <c>null</c>.</summary>
    private static Page? PageOf(Project project, FunctionId function)
    {
        var placement = project.Placements
            .Where(candidate => candidate.FunctionId == function)
            .OrderBy(candidate => candidate.Role == PlacementRole.Main ? 0 : 1)
            .FirstOrDefault();
        return placement is null ? null : project.Pages.FirstOrDefault(candidate => candidate.Id == placement.PageId);
    }

    /// <summary>Function'ın ana görünümünün yeri, <c>sayfa.kolon</c>; hiçbir sayfada değilse <c>null</c>.</summary>
    private static string? PageColumnOf(Project project, FunctionId function)
    {
        var placement = project.Placements
            .Where(candidate => candidate.FunctionId == function)
            .OrderBy(candidate => candidate.Role == PlacementRole.Main ? 0 : 1)
            .FirstOrDefault();
        var page = placement is null ? null : project.Pages.FirstOrDefault(candidate => candidate.Id == placement.PageId);
        if (placement is null || page is null || project.TemplateFor(page) is not { } template)
        {
            return null;
        }

        var geometry = SheetGeometry.For(template);
        var column = Math.Clamp((int)Math.Floor(placement.X / geometry.ColumnWidth), 0, template.Columns - 1);
        // Sayfa numarası alt sayfaysa (3.1) kolon "/" ile ayrılır (3.1/4); yoksa nokta ile (3.4). Böylece alt sayfa ile kolon karışmaz.
        var separator = page.Number.Contains('.', StringComparison.Ordinal) ? '/' : '.';
        return $"{page.Number}{separator}{(template.FirstColumnNumber + column).ToString(CultureInfo.InvariantCulture)}";
    }

    private static PlacedPage PlacePins(Project project, Page page, SheetGeometry geometry)
    {
        var symbols = new List<PlacedSymbol>();
        var pins = new Dictionary<ConnectionPointId, PlacedPin>();
        var functions = project.Devices
            .SelectMany(device => device.Functions.Select(function => (device, function)))
            .ToDictionary(item => item.function.Id);

        foreach (var placement in project.Placements.Where(placement => placement.PageId == page.Id))
        {
            if (!functions.TryGetValue(placement.FunctionId, out var owner))
            {
                continue;
            }

            var symbol = SymbolLibrary.For(owner.function, project);
            var transform = new Transform(
                geometry.DrawingArea.X + placement.X, geometry.DrawingArea.Y + placement.Y, placement.RotationDeg);
            var placedPins = new List<PlacedPin>();

            for (var i = 0; i < symbol.Pins.Count && i < owner.function.ConnectionPoints.Count; i++)
            {
                var pin = symbol.Pins[i];
                var (x, y) = transform.Point(pin.X, pin.Y);
                var (dx, dy) = transform.Direction(pin.DirectionX, pin.DirectionY);
                var point = owner.function.ConnectionPoints[i];
                var placedPin = new PlacedPin(point.Id, point.Name, new RouterPin(x, y, dx, dy), point.Attributes.Contact, point.Attributes.Connector);
                placedPins.Add(placedPin);

                // Function aynı sayfada birden çok kez yerleştirilmişse teller ilk yerleşime gider.
                pins.TryAdd(point.Id, placedPin);
            }

            var a = transform.Point(symbol.MinX, symbol.MinY);
            var b = transform.Point(symbol.MaxX, symbol.Height);
            var bounds = new Box(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
            symbols.Add(new PlacedSymbol(placement.Id, owner.device, owner.function, symbol, transform, placedPins, bounds));
        }

        return new PlacedPage(symbols, pins);
    }

    private static void DrawSymbol(List<SceneItem> items, PlacedSymbol placed, string fontFamily, bool drawTag)
    {
        var transform = placed.Transform;
        foreach (var item in placed.Symbol.Items)
        {
            items.Add(item switch
            {
                LineItem line => Line(transform.Point(line.X1, line.Y1), transform.Point(line.X2, line.Y2), line.StrokeMm, line.Color),
                CircleItem circle => circle with
                {
                    X = transform.Point(circle.X, circle.Y).X,
                    Y = transform.Point(circle.X, circle.Y).Y,
                },
                RectItem rect => Rect(transform, rect),
                TextItem text => text with
                {
                    X = transform.Point(text.X, text.Y).X,
                    Y = transform.Point(text.X, text.Y).Y,
                    FontFamily = fontFamily,
                    RotationDeg = text.RotationDeg + transform.RotationDeg,
                },
                _ => item,
            });
        }

        // Tag sembolün sayfadaki kutusunun solunda durur; sembol döndürülse de yeri değişmez.
        if (drawTag)
        {
            items.Add(new TextItem(
                placed.Bounds.X - 1.5,
                placed.Bounds.Y + (Math.Min(placed.Bounds.Height, Symbol.Length) / 2) + 1,
                $"-{placed.Device.ProductTag}", TagSizeMm, fontFamily, TextAlign.Right));
        }

        foreach (var pin in placed.Pins.Where(_ => placed.Function.Kind != FunctionKind.Junction))
        {
            if (pin.Connector != ConnectorType.Unspecified)
            {
                items.AddRange(ConnectorIcons.Items(pin.Connector, pin.Router.X, pin.Router.Y));
            }

            items.Add(new TextItem(
                pin.Router.X + 0.7 - (pin.Router.DirectionX * 2),
                pin.Router.Y + 0.5 - (pin.Router.DirectionY * 2),
                pin.Contact switch
                {
                    ContactType.NormallyOpen => $"{pin.Name} NO",
                    ContactType.NormallyClosed => $"{pin.Name} NC",
                    ContactType.Changeover => $"{pin.Name} CO",
                    _ => pin.Name,
                }
                + pin.Connector switch
                {
                    ConnectorType.Rj45 => " RJ45",
                    ConnectorType.M8 => " M8",
                    ConnectorType.M12 => " M12",
                    ConnectorType.DSub => " D-SUB",
                    _ => string.Empty,
                },
                PinNameSizeMm,
                fontFamily));
        }
    }

    /// <summary>
    /// Bir PLC kartının yan yana duran kanalları tek kutu içinde gösterilir; kanal başına ayrı kutu
    /// kartı ayrı cihazlar gibi gösterirdi.
    /// </summary>
    private static List<Box> PlcCardFrames(List<PlacedSymbol> symbols)
    {
        var frames = new List<Box>();
        var channels = symbols
            .Where(symbol => symbol.Function.Kind == FunctionKind.PlcChannel
                && symbol.Symbol.Pins.Count == 1
                && symbol.Transform.RotationDeg == 0)
            .GroupBy(symbol => (symbol.Device, symbol.Transform.OriginY));

        foreach (var row in channels)
        {
            var xs = row.Select(symbol => symbol.Transform.OriginX).Order().ToList();
            var start = xs[0];
            for (var i = 1; i <= xs.Count; i++)
            {
                if (i < xs.Count && xs[i] - xs[i - 1] <= Symbol.Pitch + 0.01)
                {
                    continue;
                }

                frames.Add(new Box(start - 4, row.Key.OriginY + 3, xs[i - 1] - start + 8, 9));
                if (i < xs.Count)
                {
                    start = xs[i];
                }
            }
        }

        return frames;
    }

    private static void DrawPath(
        List<SceneItem> items,
        IReadOnlyList<(double X, double Y)> path,
        string? color,
        string? number,
        string fontFamily,
        LineStyle style = LineStyle.Solid)
    {
        var longest = (Length: -1.0, X: 0.0, Y: 0.0, Horizontal: true);
        for (var i = 1; i < path.Count; i++)
        {
            var (x1, y1) = path[i - 1];
            var (x2, y2) = path[i];
            var length = Math.Abs(x2 - x1) + Math.Abs(y2 - y1);
            if (length < 0.001)
            {
                continue;
            }

            items.Add(new LineItem(x1, y1, x2, y2, style == LineStyle.Solid ? 0.25 : 0.35, color, style));
            if (length > longest.Length)
            {
                longest = (length, (x1 + x2) / 2, (y1 + y2) / 2, Math.Abs(y2 - y1) < 0.001);
            }
        }

        if (number is { Length: > 0 } && longest.Length > 0)
        {
            items.Add(longest.Horizontal
                ? new TextItem(longest.X, longest.Y - 0.7, number, NoteSizeMm, fontFamily, TextAlign.Center)
                : new TextItem(longest.X + 0.7, longest.Y + 0.6, number, NoteSizeMm, fontFamily));
        }
    }

    /// <summary>Uçtan çıkıp sırayla köşe noktalarından geçerek öbür uca varan dik yol.</summary>
    private static List<(double X, double Y)> ManualPath(RouterPin from, RouterPin to, IReadOnlyList<WirePoint> route) =>
        Orthogonal(
            [(from.X, from.Y), (from.EndX, from.EndY), .. route.Select(point => (point.X, point.Y)), (to.EndX, to.EndY), (to.X, to.Y)],
            from.DirectionX,
            from.DirectionY);

    /// <summary>
    /// Noktaları sırayla dik parçalarla birleştirir. Aynı yatay ya da dikey hizada olmayan iki nokta arasına bir dirsek konur;
    /// dirsek, önceki parçanın yönünü sürdürecek şekilde seçilir (geri dönüş yerine ileri gitmek). Yinelenen noktalar atılır.
    /// </summary>
    internal static List<(double X, double Y)> Orthogonal(IReadOnlyList<(double X, double Y)> points, double startDx, double startDy)
    {
        var path = new List<(double X, double Y)> { points[0] };
        var (dx, dy) = (startDx, startDy);
        for (var i = 1; i < points.Count; i++)
        {
            var (p, q) = (path[^1], points[i]);
            if (Math.Abs(p.X - q.X) < 0.001 && Math.Abs(p.Y - q.Y) < 0.001)
            {
                continue;
            }

            if (Math.Abs(p.X - q.X) >= 0.001 && Math.Abs(p.Y - q.Y) >= 0.001)
            {
                var horizontalFirst = Math.Abs(dx) > 0.5 ? (q.X - p.X) * dx >= 0
                    : Math.Abs(dy) > 0.5 ? (q.Y - p.Y) * dy < 0
                    : true;
                var elbow = horizontalFirst ? (q.X, p.Y) : (p.X, q.Y);
                path.Add(elbow);
                (dx, dy) = (Math.Sign(elbow.Item1 - p.X), Math.Sign(elbow.Item2 - p.Y));
                p = elbow;
            }

            path.Add(q);
            (dx, dy) = (Math.Sign(q.X - p.X), Math.Sign(q.Y - p.Y));
        }

        return path;
    }

    /// <summary>Yol bulunamadığında kullanılan engel tanımayan üç parçalı yol.</summary>
    private static List<(double X, double Y)> DirectPath(RouterPin from, RouterPin to)
    {
        double trackY;
        if (Math.Abs(from.DirectionY - to.DirectionY) < 0.5 && Math.Abs(from.DirectionY) > 0.5)
        {
            trackY = from.DirectionY < 0 ? Math.Min(from.EndY, to.EndY) : Math.Max(from.EndY, to.EndY);
        }
        else
        {
            trackY = (from.EndY + to.EndY) / 2;
        }

        return
        [
            (from.X, from.Y), (from.EndX, from.EndY), (from.EndX, trackY),
            (to.EndX, trackY), (to.EndX, to.EndY), (to.X, to.Y),
        ];
    }

    private static void DrawCrossReference(List<SceneItem> items, RemoteNotes notes, string fontFamily)
    {
        var pin = notes.Pin.Router;
        items.Add(new LineItem(pin.X, pin.Y, pin.EndX, pin.EndY, 0.25, notes.Color));

        // Not telin uzantısında dik yazılır: yan yana duran kutupların notları birbirine girmez.
        // Aynı ada giden bütün hedefler tek notta toplanır; farklı adlar yan yana dizilir.
        var index = 0;
        foreach (var group in notes.ByName)
        {
            var (name, targets) = (group.Name, group.Targets);
            // -90 derece dönmüş yazının harfleri taban çizgisinin solunda kalır; yazı telin sağına
            // düşsün diye taban çizgisi bir harf yüksekliği kadar sağa alınır.
            var offset = 0.5 + (index * (NoteSizeMm + 0.6));
            var text = $"{name} / {string.Join(", ", targets.Distinct().Order(StringComparer.Ordinal))}";
            // Yukarı giden uçların notu telin sağına, aşağı gidenlerinki soluna yazılır; alt alta duran
            // iki cihazın karşılıklı notları böylece aynı yere düşmez.
            var up = pin.DirectionY <= 0;
            var (textX, textY) = up ? (pin.EndX + offset + NoteSizeMm, pin.EndY - 0.8) : (pin.EndX - offset, pin.EndY + 0.8);
            items.Add(up
                ? new TextItem(textX, textY, text, NoteSizeMm, fontFamily, TextAlign.Left, -90)
                : new TextItem(textX, textY, text, NoteSizeMm, fontFamily, TextAlign.Right, -90));
            if (group.Page is { } target)
            {
                // Dik yazının kutusu: taban çizgisinin soluna bir harf boyu, yukarı (ya da aşağı) yazı uzunluğu kadar.
                var length = text.Length * NoteSizeMm * 0.55;
                items.Add(new LinkItem(textX - NoteSizeMm, up ? textY - length : textY, NoteSizeMm * 1.2, length, SheetRenderer.AnchorOf(target)));
            }

            index++;
        }
    }

    private static string ShortTag(Project project, ConnectionPointId point) =>
        project.Locate(point) is { } location ? $"-{location.Device.ProductTag}:{location.Point.Name}" : string.Empty;

    // Bağlantının görünümü: türü varsa türün rengi ve çizgi biçimi; yoksa telin veya kablo damarının renk kodu; o da yoksa siyah, düz.
    private static (string? Color, LineStyle Style, ConnectionKind? Kind) WireLook(Project project, Connection connection)
    {
        if (connection.KindId is { } id && project.FindConnectionKind(id) is { } kind)
        {
            return (kind.Color, kind.Style, kind);
        }

        return (WireColor(project, connection), LineStyle.Solid, null);
    }

    // Renk kodu (IEC 60757 veya #RRGGBB hex) çizgi rengine çevrilir; bilinmeyen kod siyah çizilir.
    private static string? WireColor(Project project, Connection connection)
    {
        var raw = connection.Wire?.Color;
        if (string.IsNullOrWhiteSpace(raw) && connection.CoreId is { } coreId)
        {
            raw = project.Cables
                .SelectMany(cable => cable.Cores)
                .FirstOrDefault(core => core.Id == coreId)
                ?.Color;
        }

        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        if (trimmed.Length == 7 && trimmed[0] == '#' && trimmed.Skip(1).All(Uri.IsHexDigit))
        {
            return trimmed;
        }

        return trimmed.ToUpperInvariant() switch
        {
            "BK" => null,
            "BN" => "#7a4a21",
            "RD" => "#d62828",
            "OG" => "#e8780c",
            "YE" => "#d4a017",
            "GNYE" or "GN" => "#2e9e2e",
            "BU" or "DBU" => "#1f4fd8",
            "VT" => "#7b2cbf",
            "GY" => "#808080",
            "WH" => "#b0b0b0",
            "PK" => "#d63384",
            "TQ" => "#008b8b",
            _ => null,
        };
    }

    private static LineItem Line((double X, double Y) a, (double X, double Y) b, double stroke, string? color) =>
        new(a.X, a.Y, b.X, b.Y, stroke, color);

    private static RectItem Rect(Transform transform, RectItem rect)
    {
        var a = transform.Point(rect.X, rect.Y);
        var b = transform.Point(rect.X + rect.Width, rect.Y + rect.Height);
        return new RectItem(
            Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y), rect.StrokeMm);
    }

    private sealed record PlacedPin(
        ConnectionPointId Point,
        string Name,
        RouterPin Router,
        ContactType Contact = ContactType.Unspecified,
        ConnectorType Connector = ConnectorType.Unspecified);

    private sealed record PlacedSymbol(
        PlacementId Placement,
        Device Device, Function Function, Symbol Symbol, Transform Transform, IReadOnlyList<PlacedPin> Pins, Box Bounds);

    private sealed record PlacedPage(List<PlacedSymbol> Symbols, Dictionary<ConnectionPointId, PlacedPin> Pins);

    /// <summary>Bir uçtan başka sayfalara giden bağlantılar, karşı ucun adına göre gruplanmış.</summary>
    private sealed class RemoteNotes(PlacedPin pin, string? color)
    {
        private readonly List<NoteGroup> _byName = [];

        public PlacedPin Pin { get; } = pin;

        public string? Color { get; } = color;

        public IReadOnlyList<NoteGroup> ByName => _byName;

        public void Add(string name, string target, PageId? page)
        {
            var group = _byName.FirstOrDefault(item => item.Name == name);
            if (group is null)
            {
                group = new NoteGroup(name);
                _byName.Add(group);
            }

            group.Targets.Add(target);
            group.Page ??= page;
        }
    }

    /// <summary>Aynı ada giden hedefler; <c>Page</c> ilk hedefin sayfasıdır (nota tıklayınca gidilen yer).</summary>
    private sealed class NoteGroup(string name)
    {
        public string Name { get; } = name;

        public List<string> Targets { get; } = [];

        public PageId? Page { get; set; }
    }

    /// <summary>Sembol koordinatını sayfa koordinatına çevirir: 90 derecenin katı dönüş ve öteleme.</summary>
    private readonly record struct Transform(double OriginX, double OriginY, int RotationDeg)
    {
        public (double X, double Y) Point(double x, double y)
        {
            var (rx, ry) = Direction(x, y);
            return (OriginX + rx, OriginY + ry);
        }

        public (double X, double Y) Direction(double x, double y) => RotationDeg switch
        {
            90 => (-y, x),
            180 => (-x, -y),
            270 => (y, -x),
            _ => (x, y),
        };
    }
}
