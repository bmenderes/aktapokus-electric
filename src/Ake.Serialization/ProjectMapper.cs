using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>Model ile dosya şeması arasındaki çift yönlü eşleme.</summary>
internal static class ProjectMapper
{
    public static ProjectDto ToDto(Project project) => new(
        ProjectJson.CurrentSchemaVersion,
        project.Id.Value,
        project.Name,
        project.TagScheme == TagScheme.LetterNumber ? null : project.TagScheme,
        new SortedDictionary<string, string>(project.Properties.ToDictionary(), StringComparer.Ordinal),
        [.. project.Structure.Select(ToDto)],
        [.. project.Devices.Select(ToDto)],
        [.. project.Connections.Select(ToDto)],
        [.. project.Cables.Select(ToDto)],
        [.. project.PotentialSources.Select(ToDto)],
        [.. project.Controllers.Select(ToDto)],
        [.. project.SheetTemplates.Select(ToDto)],
        [.. project.Symbols.Select(ToDto)],
        [.. project.Pages.Select(ToDto)],
        [.. project.Placements.Select(ToDto)],
        project.Graphics.Count == 0 ? null : [.. project.Graphics.Select(ToDto)],
        project.ConnectionKinds.Count == 0 ? null : [.. project.ConnectionKinds.Select(ToDto)],
        project.WireNumbering == new WireNumberSettings() ? null : ToDto(project.WireNumbering),
        project.Revisions.Count == 0 ? null : [.. project.Revisions.Select(revision => new RevisionDto(revision.Mark, revision.Date, revision.Author, revision.Description))]);

    public static Result<Project> ToModel(ProjectDto dto) => Project.Create(
        new ProjectId(dto.Id),
        dto.Name,
        (dto.Structure ?? []).Select(ToModel),
        (dto.Devices ?? []).Select(ToModel),
        (dto.Connections ?? []).Select(ToModel),
        (dto.Cables ?? []).Select(ToModel),
        (dto.PotentialSources ?? []).Select(ToModel),
        (dto.Controllers ?? []).Select(ToModel),
        (dto.Pages ?? []).Select(ToModel),
        (dto.Placements ?? []).Select(ToModel),
        (dto.SheetTemplates ?? []).Select(ToModel),
        dto.Properties,
        (dto.Symbols ?? []).Select(ToModel),
        dto.TagScheme ?? TagScheme.LetterNumber,
        (dto.Graphics ?? []).Select(ToModel),
        (dto.ConnectionKinds ?? []).Select(ToModel),
        dto.WireNumbering is { } numbering ? ToModel(numbering) : null,
        (dto.Revisions ?? []).Select(revision => new Revision(revision.Mark, revision.Date, revision.Author, revision.Description)));

    private static WireNumberingDto ToDto(WireNumberSettings settings)
    {
        var standard = new WireNumberSettings();
        return new WireNumberingDto(
            settings.Prefix == standard.Prefix ? null : settings.Prefix,
            settings.Start == standard.Start ? null : settings.Start,
            settings.Step == standard.Step ? null : settings.Step,
            settings.Width == standard.Width ? null : settings.Width,
            settings.Scheme == standard.Scheme ? null : settings.Scheme);
    }

    private static WireNumberSettings ToModel(WireNumberingDto dto)
    {
        var standard = new WireNumberSettings();
        return new WireNumberSettings(
            dto.Prefix ?? standard.Prefix, dto.Start ?? standard.Start, dto.Step ?? standard.Step, dto.Width ?? standard.Width, dto.Scheme ?? standard.Scheme);
    }

    private static ConnectionKindDto ToDto(ConnectionKind kind) => new(
        kind.Id.Value,
        kind.Name,
        kind.Color,
        kind.Style == LineStyle.Solid ? null : kind.Style,
        kind.Category == PinKind.Unspecified ? null : kind.Category);

    private static ConnectionKind ToModel(ConnectionKindDto dto) =>
        new(new ConnectionKindId(dto.Id), dto.Name, dto.Color, dto.Style ?? LineStyle.Solid, dto.Category ?? PinKind.Unspecified);

    private static StructureNodeDto ToDto(StructureNode node) =>
        new(node.Id.Value, node.Aspect, node.Name, node.ParentId?.Value, node.Description);

    private static StructureNode ToModel(StructureNodeDto dto) =>
        new(new StructureNodeId(dto.Id), dto.Aspect, dto.Name, NodeId(dto.ParentId), dto.Description);

    internal static DeviceDto ToDto(Device device) => new(
        device.Id.Value,
        device.ProductTag,
        device.Kind,
        device.FunctionNodeId?.Value,
        device.LocationNodeId?.Value,
        device.Description,
        device.Part is { } part ? new PartRefDto(part.Manufacturer, part.OrderCode, part.TypeDesignation) : null,
        device.Mounting is { } mounting
            ? new MountingDto(
                mounting.Type,
                mounting.WidthMm,
                mounting.HeightMm,
                mounting.DepthMm,
                mounting.ClearanceMm,
                mounting.HeatLossW,
                mounting.StepRef)
            : null,
        [.. device.Functions.Select(ToDto)],
        device.Address,
        device.Properties.Count == 0 ? null : new SortedDictionary<string, string>(device.Properties.ToDictionary(), StringComparer.Ordinal));

    internal static Device ToModel(DeviceDto dto) => new(
        new DeviceId(dto.Id),
        dto.Tag,
        (dto.Functions ?? []).Select(ToModel),
        dto.Kind,
        NodeId(dto.FunctionNodeId),
        NodeId(dto.LocationNodeId),
        dto.Description,
        dto.Part is { } part ? new PartRef(part.Manufacturer, part.OrderCode, part.TypeDesignation) : null,
        dto.Mounting is { } mounting
            ? new Mounting(
                mounting.Type,
                mounting.WidthMm,
                mounting.HeightMm,
                mounting.DepthMm,
                mounting.ClearanceMm,
                mounting.HeatLossW,
                mounting.StepRef)
            : null,
        dto.Address,
        dto.Properties);

    private static FunctionDto ToDto(Function function) => new(
        function.Id.Value,
        function.Kind,
        function.PassThrough,
        function.Description,
        function.RatedCurrentA,
        function.SymbolId?.Value,
        [.. function.ConnectionPoints.Select(point => new ConnectionPointDto(
            point.Id.Value,
            point.Name,
            Given(point.Attributes.Kind),
            Given(point.Attributes.Connector),
            Given(point.Attributes.Contact),
            point.Attributes.ConnectorCode))],
        function.Level == 1 ? null : function.Level,
        function.Role == TerminalRole.Normal ? null : function.Role);

    // Belirtilmemiş değer dosyaya yazılmaz.
    private static T? Given<T>(T value)
        where T : struct, Enum => value.Equals(default(T)) ? null : value;

    private static Function ToModel(FunctionDto dto) => new(
        new FunctionId(dto.Id),
        dto.Kind,
        (dto.ConnectionPoints ?? []).Select(point => new ConnectionPoint(
            new ConnectionPointId(point.Id),
            point.Name,
            new PointAttributes(point.Kind ?? default, point.Connector ?? default, point.Contact ?? default, point.ConnectorCode))),
        dto.PassThrough,
        dto.Description,
        dto.RatedCurrentA,
        dto.SymbolId is { } symbolId ? new SymbolId(symbolId) : null,
        dto.Level ?? 1,
        dto.Role ?? TerminalRole.Normal);

    internal static ConnectionDto ToDto(Connection connection) => new(
        connection.Id.Value,
        connection.From.Value,
        connection.To.Value,
        connection.Wire is { } wire
            ? new WireDto(wire.Id.Value, wire.Number, wire.CrossSectionMm2, wire.Color)
            : null,
        connection.CoreId?.Value,
        connection.Route.Count == 0 ? null : GraphicPolyline.Encode([.. connection.Route.SelectMany(point => new[] { point.X, point.Y })]),
        connection.KindId?.Value);

    internal static Connection ToModel(ConnectionDto dto) => new(
        new ConnectionId(dto.Id),
        new ConnectionPointId(dto.From),
        new ConnectionPointId(dto.To),
        dto.Wire is { } wire ? new Wire(new WireId(wire.Id), wire.Number, wire.CrossSectionMm2, wire.Color) : null,
        dto.CoreId is { } coreId ? new CableCoreId(coreId) : null,
        DecodeRoute(dto.Route),
        dto.Kind is { } kind ? new ConnectionKindId(kind) : null);

    // Bozuk nokta metni sonlu olmayan bir nokta olur; model doğrulaması "köşe noktaları geçersiz" diye reddeder.
    private static IEnumerable<WirePoint> DecodeRoute(string? text)
    {
        if (text is null)
        {
            return [];
        }

        var values = GraphicPolyline.Decode(text);
        return values is null || values.Length % 2 != 0
            ? [new WirePoint(double.NaN, double.NaN)]
            : Enumerable.Range(0, values.Length / 2).Select(i => new WirePoint(values[2 * i], values[(2 * i) + 1]));
    }

    private static CableDto ToDto(Cable cable) => new(
        cable.Id.Value,
        cable.ProductTag,
        cable.Type,
        [.. cable.Cores.Select(core => new CableCoreDto(core.Id.Value, core.Designation, core.CrossSectionMm2, core.Color))]);

    private static Cable ToModel(CableDto dto) => new(
        new CableId(dto.Id),
        dto.Tag,
        (dto.Cores ?? []).Select(core =>
            new CableCore(new CableCoreId(core.Id), core.Designation, core.CrossSectionMm2, core.Color)),
        dto.Type);

    private static PotentialSourceDto ToDto(PotentialSource source) =>
        new(source.Id.Value, source.Name, source.Kind, source.At.Value);

    private static PotentialSource ToModel(PotentialSourceDto dto) =>
        new(new PotentialSourceId(dto.Id), dto.Name, dto.Kind, new ConnectionPointId(dto.At));

    private static ControllerDto ToDto(Controller controller) => new(
        controller.Id.Value,
        controller.DeviceId.Value,
        controller.Name,
        [.. controller.Networks.Select(ToDto)]);

    private static Controller ToModel(ControllerDto dto) => new(
        new ControllerId(dto.Id),
        new DeviceId(dto.DeviceId),
        dto.Name,
        (dto.Networks ?? []).Select(ToModel));

    private static NetworkDto ToDto(Network network) => new(
        network.Id.Value,
        network.Kind,
        network.Name,
        [.. network.Nodes.Select(ToDto)],
        [.. network.PortLinks.Select(link => new PortLinkDto(link.Id.Value, link.From.Value, link.To.Value))]);

    private static Network ToModel(NetworkDto dto) => new(
        new NetworkId(dto.Id),
        dto.Kind,
        dto.Name,
        (dto.Nodes ?? []).Select(ToModel),
        (dto.PortLinks ?? []).Select(link =>
            new PortLink(new PortLinkId(link.Id), new PortId(link.From), new PortId(link.To))));

    private static NodeDto ToDto(Node node) => new(
        node.Id.Value,
        node.DeviceId.Value,
        node.Name,
        node.Position,
        [.. node.Ports.Select(port => new PortDto(port.Id.Value, port.Name))],
        [.. node.Modules.Select(ToDto)]);

    private static Node ToModel(NodeDto dto) => new(
        new NodeId(dto.Id),
        new DeviceId(dto.DeviceId),
        dto.Name,
        dto.Position,
        (dto.Modules ?? []).Select(ToModel),
        (dto.Ports ?? []).Select(port => new Port(new PortId(port.Id), port.Name)));

    private static ModuleDto ToDto(Module module) => new(
        module.Id.Value,
        module.DeviceId.Value,
        module.Position,
        [.. module.Channels.Select(ToDto)]);

    private static Module ToModel(ModuleDto dto) => new(
        new ModuleId(dto.Id),
        new DeviceId(dto.DeviceId),
        dto.Position,
        (dto.Channels ?? []).Select(ToModel));

    private static ChannelDto ToDto(Channel channel) => new(
        channel.Id.Value,
        channel.Index,
        channel.SignalType,
        channel.FunctionId?.Value,
        channel.Address,
        channel.SymbolName,
        channel.Description,
        [.. channel.ProcessData.Select(entry =>
            new ProcessDataEntryDto(entry.Name, entry.DataType, entry.BitOffset, entry.BitLength, entry.Direction))]);

    private static Channel ToModel(ChannelDto dto) => new(
        new ChannelId(dto.Id),
        dto.Index,
        dto.SignalType,
        dto.FunctionId is { } functionId ? new FunctionId(functionId) : null,
        dto.Address,
        dto.SymbolName,
        dto.Description,
        (dto.ProcessData ?? []).Select(entry =>
            new ProcessDataEntry(entry.Name, entry.DataType, entry.BitOffset, entry.BitLength, entry.Direction)));

    private static PageDto ToDto(Page page) => new(
        page.Id.Value,
        page.Number,
        page.Title,
        page.FunctionNodeId?.Value,
        page.LocationNodeId?.Value,
        page.TemplateId?.Value,
        page.Kind == PageKind.Schematic ? null : page.Kind,
        page.Part == 0 ? null : page.Part);

    public static SymbolDto ToDto(SymbolDefinition symbol) => new(
        symbol.Id.Value,
        symbol.Name,
        [
            .. symbol.Shapes.Select<SymbolShape, SymbolShapeDto>(shape => shape switch
            {
                SymbolLine l => new SymbolLineDto(l.X1, l.Y1, l.X2, l.Y2, l.StrokeMm),
                SymbolRect r => new SymbolRectDto(r.X, r.Y, r.Width, r.Height, r.StrokeMm),
                SymbolCircle c => new SymbolCircleDto(c.X, c.Y, c.Radius, c.StrokeMm, c.Filled),
                SymbolText t => new SymbolTextDto(t.X, t.Y, t.Text, t.SizeMm, t.Align, Math.Abs(t.RotationDeg) < 0.01 ? null : t.RotationDeg),
                _ => throw new InvalidOperationException($"Bilinmeyen sembol öğesi: {shape.GetType().Name}"),
            }),
        ],
        [.. symbol.Pins.Select(p => new SymbolPinDto(
            p.X,
            p.Y,
            p.DirectionX,
            p.DirectionY,
            p.Name,
            Given(p.Attributes.Kind),
            Given(p.Attributes.Connector),
            Given(p.Attributes.Contact),
            p.Attributes.ConnectorCode))],
        symbol.Source,
        symbol.License,
        symbol.DeviceName,
        symbol.TagLetter);

    public static SymbolDefinition ToModel(SymbolDto dto) => new(
        new SymbolId(dto.Id),
        dto.Name,
        [
            .. dto.Shapes.Select<SymbolShapeDto, SymbolShape>(shape => shape switch
            {
                SymbolLineDto l => new SymbolLine(l.X1, l.Y1, l.X2, l.Y2, l.StrokeMm),
                SymbolRectDto r => new SymbolRect(r.X, r.Y, r.Width, r.Height, r.StrokeMm),
                SymbolCircleDto c => new SymbolCircle(c.X, c.Y, c.Radius, c.StrokeMm, c.Filled),
                SymbolTextDto t => new SymbolText(t.X, t.Y, t.Text, t.SizeMm, t.Align, t.RotationDeg ?? 0),
                _ => throw new InvalidOperationException($"Bilinmeyen sembol öğesi: {shape.GetType().Name}"),
            }),
        ],
        [.. dto.Pins.Select(p => new SymbolPin(
            p.X,
            p.Y,
            p.DirectionX,
            p.DirectionY,
            p.Name,
            new PointAttributes(p.Kind ?? default, p.Connector ?? default, p.Contact ?? default, p.ConnectorCode)))],
        dto.Source,
        dto.License,
        dto.DeviceName,
        dto.TagLetter);

    public static SheetTemplateDto ToDto(SheetTemplate template) => new(
        template.Id.Value,
        template.Name,
        template.WidthMm,
        template.HeightMm,
        template.MarginMm,
        template.Columns,
        template.Rows,
        template.GridBandMm,
        template.TitleBlockHeightMm,
        template.FontFamily,
        template.FirstColumnNumber,
        [
            .. template.Cells.Select(cell => new TitleBlockCellDto(
                cell.XMm,
                cell.YMm,
                cell.WidthMm,
                cell.HeightMm,
                cell.Label,
                cell.Content,
                cell.FontSizeMm,
                cell.Align,
                cell.Border,
                cell.ImageRef)),
        ]);

    public static SheetTemplate ToModel(SheetTemplateDto dto) => new(
        new SheetTemplateId(dto.Id),
        dto.Name,
        dto.WidthMm,
        dto.HeightMm,
        dto.MarginMm,
        dto.Columns,
        dto.Rows,
        dto.GridBandMm,
        dto.TitleBlockHeightMm,
        dto.FontFamily,
        [
            .. (dto.Cells ?? []).Select(cell => new TitleBlockCell(
                cell.XMm,
                cell.YMm,
                cell.WidthMm,
                cell.HeightMm,
                cell.Label,
                cell.Content,
                cell.FontSizeMm,
                cell.Align,
                cell.Border,
                cell.ImageRef)),
        ],
        dto.FirstColumnNumber);

    private static Page ToModel(PageDto dto) => new(
        new PageId(dto.Id),
        dto.Number,
        dto.Title,
        NodeId(dto.FunctionNodeId),
        NodeId(dto.LocationNodeId),
        dto.TemplateId is { } templateId ? new SheetTemplateId(templateId) : null,
        dto.Kind ?? PageKind.Schematic,
        dto.Part ?? 0);

    private static PlacementDto ToDto(Placement placement) => new(
        placement.Id.Value,
        placement.PageId.Value,
        placement.FunctionId.Value,
        placement.Role,
        placement.X,
        placement.Y,
        placement.RotationDeg);

    private static Placement ToModel(PlacementDto dto) => new(
        new PlacementId(dto.Id),
        new PageId(dto.PageId),
        new FunctionId(dto.FunctionId),
        dto.Role,
        dto.X,
        dto.Y,
        dto.RotationDeg);

    private static GraphicDto ToDto(PageGraphic graphic) => new(graphic.Id.Value, graphic.PageId.Value, graphic.Shape switch
    {
        GraphicPolyline p => new GraphicPolylineDto(
            GraphicPolyline.Encode(p.Points), p.StrokeMm, p.Closed ? true : null, p.Filled ? true : null, p.Color),
        GraphicCircle c => new GraphicCircleDto(c.X, c.Y, c.Radius, c.StrokeMm, c.Filled ? true : null, c.Color),
        GraphicText t => new GraphicTextDto(t.X, t.Y, t.Text, t.SizeMm, t.Align, t.RotationDeg == 0 ? null : t.RotationDeg, t.Color),
        _ => throw new InvalidOperationException($"Bilinmeyen grafik: {graphic.Shape.GetType().Name}"),
    });

    private static PageGraphic ToModel(GraphicDto dto) => new(new GraphicId(dto.Id), new PageId(dto.PageId), dto.Shape switch
    {
        // Bozuk nokta metni boş liste olur; model doğrulaması "en az iki nokta" diye reddeder.
        GraphicPolylineDto p => new GraphicPolyline(
            GraphicPolyline.Decode(p.Points) ?? [], p.Closed ?? false, p.StrokeMm, p.Filled ?? false)
        { Color = p.Color },
        GraphicCircleDto c => new GraphicCircle(c.X, c.Y, c.Radius, c.StrokeMm, c.Filled ?? false) { Color = c.Color },
        GraphicTextDto t => new GraphicText(t.X, t.Y, t.Text, t.SizeMm, t.Align, t.RotationDeg ?? 0) { Color = t.Color },
        _ => throw new InvalidOperationException($"Bilinmeyen grafik: {dto.Shape.GetType().Name}"),
    });

    private static StructureNodeId? NodeId(Guid? id) => id is { } value ? new StructureNodeId(value) : null;
}
