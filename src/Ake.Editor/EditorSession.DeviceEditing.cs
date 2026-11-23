using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Yapı düğümü seçeneği (özellik penceresinde açılır liste); <c>Id</c> boşsa "yok".</summary>
public sealed record NodeChoice(StructureNodeId? Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record PointEdit(ConnectionPointId Id, string Name, PointAttributes Attributes = default);

public sealed record FunctionEdit(FunctionId Id, string Kind, string? Description, IReadOnlyList<PointEdit> Points);

/// <summary>Kontak aynası sekmesinde cihazın her function'ının konumu ve rolü (ADR-0054).</summary>
public sealed record ContactMirrorRow(
    FunctionId FunctionId, string Kind, string Pins, string? Description, string Role, string Location);

/// <summary>Görünen etiket seçicisi ([...]) için projedeki mevcut cihaz özeti (ADR-0054).</summary>
public sealed record ExistingDeviceChoice(
    string ProductTag,
    string Designation,
    string? Description,
    StructureNodeId? FunctionNode,
    StructureNodeId? LocationNode,
    string Manufacturer,
    string OrderCode,
    string? TypeDesignation);

/// <summary>Sekmeli özellik penceresinin gösterdikleri: tek bir cihazın düzenlenebilir bilgileri.</summary>
public sealed record DeviceEditorData(
    DeviceId Device,
    string Designation,
    string ProductTag,
    string? Description,
    string Manufacturer,
    string OrderCode,
    string? TypeDesignation,
    StructureNodeId? FunctionNode,
    StructureNodeId? LocationNode,
    IReadOnlyList<NodeChoice> FunctionNodes,
    IReadOnlyList<NodeChoice> LocationNodes,
    IReadOnlyList<FunctionEdit> Functions,
    string? Address = null,
    PlacementId? PrimaryPlacement = null,
    FunctionId? PrimaryFunction = null,
    int RotationDeg = 0,
    double X = 0,
    double Y = 0,
    PlacementRole Role = PlacementRole.Main,
    string? SymbolName = null,
    Scene? SymbolPreview = null,
    IReadOnlyList<ExistingDeviceChoice>? ExistingDevices = null,
    IReadOnlyList<ContactMirrorRow>? ContactMirror = null,
    IReadOnlyDictionary<string, string>? CustomProperties = null);

/// <summary>Özellik penceresinden dönen yeni değerler.</summary>
public sealed record DeviceEdits(
    DeviceId Device,
    string ProductTag,
    string? Description,
    string? Manufacturer,
    string? OrderCode,
    string? TypeDesignation,
    StructureNodeId? FunctionNode,
    StructureNodeId? LocationNode,
    IReadOnlyList<PointEdit> Points,
    IReadOnlyList<(FunctionId Function, string? Description)> FunctionDescriptions,
    string? Address = null,
    PlacementId? Placement = null,
    int? RotationDeg = null,
    double? X = null,
    double? Y = null,
    PlacementRole? Role = null,
    SymbolId? AssignedLibrarySymbol = null,
    IReadOnlyDictionary<string, string>? CustomProperties = null);

/// <summary>Sekmeli özellik penceresi, oturum tarafı (Faz 6.2, madde 11 ve ADR-0054).</summary>
public sealed partial class EditorSession
{
    /// <summary>Yerleşimin cihazının düzenlenebilir bilgileri; yerleşim yoksa <c>null</c>.</summary>
    public DeviceEditorData? EditorDataOf(PlacementId placementId)
    {
        if (Project is not { } project
            || project.Placements.FirstOrDefault(placement => placement.Id == placementId) is not { } found
            || project.Devices.FirstOrDefault(candidate => candidate.Functions.Any(function => function.Id == found.FunctionId)) is not { } device)
        {
            return null;
        }

        NodeChoice[] Choices(Aspect aspect) => StructureChoices(project, aspect);

        var primaryFunction = device.Functions.FirstOrDefault(function => function.Id == found.FunctionId);
        var (symbolName, symbolPreview) = SymbolSummaryOf(project, primaryFunction);

        var existingDevices = project.Devices
            .Where(candidate => candidate.Kind != DeviceKind.Junction)
            .Select(candidate => (Device: candidate, Designation: project.DesignationOf(candidate).ToString()))
            .OrderBy(item => item.Designation, StringComparer.Ordinal)
            .Select(item => new ExistingDeviceChoice(
                item.Device.ProductTag,
                item.Designation,
                item.Device.Description,
                item.Device.FunctionNodeId,
                item.Device.LocationNodeId,
                item.Device.Part?.Manufacturer ?? string.Empty,
                item.Device.Part?.OrderCode ?? string.Empty,
                item.Device.Part?.TypeDesignation))
            .ToList();

        var contactMirror = device.Functions
            .Select(function => new ContactMirrorRow(
                function.Id,
                FunctionKindLabel(function.Kind),
                string.Join(", ", function.ConnectionPoints.Select(point => point.Name)),
                function.Description,
                RoleLabelOf(project, function.Id),
                LocationOf(project, function.Id)))
            .ToList();

        return new DeviceEditorData(
            device.Id,
            project.DesignationOf(device).ToString(),
            device.ProductTag,
            device.Description,
            device.Part?.Manufacturer ?? string.Empty,
            device.Part?.OrderCode ?? string.Empty,
            device.Part?.TypeDesignation,
            device.FunctionNodeId,
            device.LocationNodeId,
            Choices(Aspect.Function),
            Choices(Aspect.Location),
            [
                .. device.Functions.Select(function => new FunctionEdit(
                    function.Id,
                    function.Kind.ToString(),
                    function.Description,
                    [.. function.ConnectionPoints.Select(point => new PointEdit(point.Id, point.Name, point.Attributes))])),
            ],
            device.Address,
            found.Id,
            found.FunctionId,
            found.RotationDeg,
            found.X,
            found.Y,
            found.Role,
            symbolName,
            symbolPreview,
            existingDevices,
            contactMirror,
            new SortedDictionary<string, string>(new Dictionary<string, string>(device.Properties), StringComparer.Ordinal));
    }

    /// <summary>
    /// Özellik penceresinin değişikliklerini tek adımda uygular (tag ve yapı, açıklama ve ürün, nokta adları,
    /// function açıklamaları, konum/döndürme, rol ve kütüphane sembolü). Değişmeyen için komut üretilmez; biri
    /// reddedilirse hiçbiri uygulanmaz.
    /// </summary>
    public Result ApplyDeviceEdits(DeviceEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);

        if (Project?.FindDevice(edits.Device) is not { } device)
        {
            return Result.Failure(new Error("device.yok", $"Device modelde yok ({edits.Device})."));
        }

        var commands = new List<ICommand>();
        var tag = edits.ProductTag.Trim();
        if (tag != device.ProductTag || edits.FunctionNode != device.FunctionNodeId || edits.LocationNode != device.LocationNodeId)
        {
            commands.Add(new Retag(device.Id, tag, edits.FunctionNode, edits.LocationNode));
        }

        var (manufacturer, orderCode, type) = (Clean(edits.Manufacturer), Clean(edits.OrderCode), Clean(edits.TypeDesignation));
        PartRef? part = manufacturer is null && orderCode is null && type is null
            ? null
            : new PartRef(manufacturer ?? string.Empty, orderCode ?? string.Empty, type);
        if (Clean(edits.Description) != Clean(device.Description) || part != device.Part)
        {
            commands.Add(new SetDeviceDetails(device.Id, edits.Description, part));
        }

        if (Clean(edits.Address) != Clean(device.Address))
        {
            commands.Add(new SetDeviceAddress(device.Id, edits.Address));
        }

        if (edits.CustomProperties is not null)
        {
            var next = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (key, value) in edits.CustomProperties)
            {
                var k = key.Trim();
                var v = Clean(value);
                if (k.Length > 0 && v is not null)
                {
                    next[k] = v;
                }
            }

            foreach (var oldKey in device.Properties.Keys.Where(k => !next.ContainsKey(k)).OrderBy(k => k, StringComparer.Ordinal))
            {
                commands.Add(new SetDeviceProperty(device.Id, oldKey, null));
            }

            foreach (var (key, value) in next.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                if (!device.Properties.TryGetValue(key, out var existing) || !string.Equals(existing, value, StringComparison.Ordinal))
                {
                    commands.Add(new SetDeviceProperty(device.Id, key, value));
                }
            }
        }

        var points = device.Functions.SelectMany(function => function.ConnectionPoints).ToDictionary(point => point.Id);
        commands.AddRange(edits.Points
            .Where(edit => points.TryGetValue(edit.Id, out var point) && edit.Name.Trim() != point.Name)
            .Select(edit => new RenameConnectionPoint(edit.Id, edit.Name)));
        commands.AddRange(edits.Points
            .Where(edit => points.TryGetValue(edit.Id, out var point)
                && edit.Attributes with { ConnectorCode = Clean(edit.Attributes.ConnectorCode) } != point.Attributes)
            .Select(edit => new SetPointAttributes(edit.Id, edit.Attributes)));

        var functions = device.Functions.ToDictionary(function => function.Id);
        commands.AddRange(edits.FunctionDescriptions
            .Where(edit => functions.TryGetValue(edit.Function, out var function) && Clean(edit.Description) != Clean(function.Description))
            .Select(edit => new SetFunctionDescription(edit.Function, edit.Description)));

        Placement? targetPlacement = null;
        if (edits.Placement is { } placementId
            && Project.Placements.FirstOrDefault(candidate => candidate.Id == placementId) is { } foundPlacement)
        {
            targetPlacement = foundPlacement;
            var targetX = edits.X ?? foundPlacement.X;
            var targetY = edits.Y ?? foundPlacement.Y;
            var targetRotation = edits.RotationDeg ?? foundPlacement.RotationDeg;
            if (Math.Abs(targetX - foundPlacement.X) > 0.0001
                || Math.Abs(targetY - foundPlacement.Y) > 0.0001
                || targetRotation != foundPlacement.RotationDeg)
            {
                commands.Add(new MovePlacement(foundPlacement.Id, targetX, targetY, targetRotation));
            }

            if (edits.Role is { } role && role != foundPlacement.Role)
            {
                commands.Add(new SetPlacementRole(foundPlacement.Id, role));
            }
        }

        if (edits.AssignedLibrarySymbol is { } symbolId)
        {
            var targetFunction = targetPlacement is not null
                ? functions.GetValueOrDefault(targetPlacement.FunctionId)
                : device.Functions.FirstOrDefault();
            if (targetFunction is not null && targetFunction.SymbolId != symbolId)
            {
                if (Project.FindSymbol(symbolId) is null)
                {
                    if (_library is null)
                    {
                        return Result.Failure(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
                    }

                    var found = _library.FindSymbol(symbolId.ToString());
                    if (found.IsFailure)
                    {
                        return Result.Failure(found.Error!);
                    }

                    commands.Add(new PutSymbol(found.Value));
                }

                commands.Add(new AssignSymbol(targetFunction.Id, symbolId));
            }
        }

        return ExecuteIfAny(new Transaction($"Cihaz özellikleri değiştirildi: -{device.ProductTag}", commands));

        static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    internal static NodeChoice[] StructureChoices(Project project, Aspect aspect) =>
    [
        new NodeChoice(null, "—"),
        .. project.Structure
            .Where(node => node.Aspect == aspect)
            .Select(node => new NodeChoice(node.Id, project.StructurePath(node.Id)!))
            .OrderBy(choice => choice.Label, StringComparer.Ordinal),
    ];

    internal static string FunctionKindLabel(string kind) =>
        Enum.TryParse<FunctionKind>(kind, out var parsed) ? FunctionKindLabel(parsed) : kind;

    internal static string FunctionKindLabel(FunctionKind kind) => kind switch
    {
        FunctionKind.Coil => "Bobin",
        FunctionKind.NoContact => "NO kontak",
        FunctionKind.NcContact => "NC kontak",
        FunctionKind.PowerContact => "Güç kontağı",
        FunctionKind.Terminal => "Klemens",
        FunctionKind.Protection => "Koruma",
        FunctionKind.Motor => "Motor",
        FunctionKind.PowerSupply => "Güç kaynağı",
        FunctionKind.PlcChannel => "PLC kanalı",
        FunctionKind.Junction => "Kavşak",
        _ => "Genel cihaz",
    };

    private (string? Name, Scene? Preview) SymbolSummaryOf(Project project, Function? function)
    {
        if (function is null)
        {
            return (null, null);
        }

        if (function.SymbolId is { } id && project.FindSymbol(id) is { } assigned)
        {
            return (assigned.Name, Preview(assigned));
        }

        var starter = StartingSymbolFor(function);
        return (FunctionKindLabel(function.Kind) + " (yerleşik)", starter is null ? null : Preview(starter));
    }

    private static string RoleLabelOf(Project project, FunctionId functionId)
    {
        var placements = project.Placements.Where(p => p.FunctionId == functionId).ToList();
        return placements.Any(p => p.Role == PlacementRole.Main) ? "Ana"
            : placements.Count > 0 ? "Referans"
            : "—";
    }

    private static string LocationOf(Project project, FunctionId functionId)
    {
        var placements = project.Placements
            .Where(p => p.FunctionId == functionId)
            .OrderBy(p => p.Role == PlacementRole.Main ? 0 : 1)
            .ToList();
        if (placements.Count == 0)
        {
            return "Yerleştirilmemiş";
        }

        var locations = new List<string>();
        foreach (var placement in placements)
        {
            var page = project.Pages.FirstOrDefault(candidate => candidate.Id == placement.PageId);
            if (page is null)
            {
                continue;
            }

            if (project.TemplateFor(page) is not { } template)
            {
                locations.Add(page.Number);
                continue;
            }

            var geometry = SheetGeometry.For(template);
            var column = Math.Clamp((int)Math.Floor(placement.X / geometry.ColumnWidth), 0, template.Columns - 1);
            locations.Add($"{page.Number}.{(template.FirstColumnNumber + column).ToString(CultureInfo.InvariantCulture)}");
        }

        return locations.Count == 0 ? "Yerleştirilmemiş" : string.Join(", ", locations);
    }
}
