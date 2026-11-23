using System.Text;
using System.Text.Json;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>
/// Dosyadan okunmuş bir değişiklik paketi. <see cref="ToCommand"/> tek bir transaction üretir:
/// ya tamamı uygulanır ya hiçbiri.
/// </summary>
public sealed class Patch
{
    private readonly PatchDto _dto;

    internal Patch(PatchDto dto) => _dto = dto;

    public ChangeSource Source => _dto.Source;

    public string? Actor => _dto.Actor;

    public string? Description => _dto.Description;

    public int CommandCount => _dto.Commands.Count;

    /// <summary>
    /// Atıflar çalışma anında çözülür; böylece bir adım, aynı patch'te önceki adımın eklediği
    /// nesneye tag'iyle atıfta bulunabilir.
    /// </summary>
    /// <param name="catalog">Ürün kütüphanesi: <c>addDevice</c> sipariş koduyla cihaz eklerken.</param>
    /// <param name="symbols">
    /// Sembol kütüphanesi: <c>assignSymbol</c> projede olmayan bir sembolü isterse buradan projeye kopyalanır.
    /// </param>
    public ICommand ToCommand(IProductCatalog? catalog = null, ISymbolCatalog? symbols = null) => new Transaction(
        _dto.Description ?? "Patch uygulandı",
        _dto.Commands.Select(op => new DeferredCommand(OpName(op), project => Build(op, project, catalog, symbols))));

    private static string OpName(PatchOpDto op) => op.GetType().Name;

    private static Result<ICommand> Build(
        PatchOpDto op, Project project, IProductCatalog? catalog, ISymbolCatalog? symbols) => op switch
        {
            AddDeviceOp add => Build(add, project, catalog),
            RemoveDeviceOp remove => Build(remove, project),
            ConnectOp connect => Build(connect, project),
            DisconnectOp disconnect => Build(disconnect, project),
            AssignWireOp assignWire => Build(assignWire, project),
            RetagOp retag => Build(retag, project),
            AssignIoOp assignIo => Build(assignIo, project),
            SetPropertyOp setProperty => Ok(new SetProjectProperty(setProperty.Key, setProperty.Value)),
            SetDevicePropertyOp setDeviceProperty => Build(setDeviceProperty, project),
            AddPageOp addPage => Build(addPage, project),
            AssignSymbolOp assignSymbol => Build(assignSymbol, project, symbols),
            _ => throw new InvalidOperationException($"Bilinmeyen patch komutu: {op.GetType().Name}"),
        };

    private static Result<ICommand> Build(AddDeviceOp op, Project project, IProductCatalog? catalog)
    {
        var tag = ReferenceDesignation.Parse(op.Tag);
        if (tag.IsFailure)
        {
            return Fail(tag.Error!);
        }

        if (tag.Value.Product is not { } productTag || tag.Value.ConnectionPoint is not null)
        {
            return Fail(new Error("patch.tag_gecersiz", $"'{op.Tag}' bir device tag'i değil."));
        }

        var functionNode = ReferenceResolver.Node(project, tag.Value.Function, Aspect.Function);
        if (functionNode.IsFailure)
        {
            return Fail(functionNode.Error!);
        }

        var locationNode = ReferenceResolver.Node(project, tag.Value.Location, Aspect.Location);
        if (locationNode.IsFailure)
        {
            return Fail(locationNode.Error!);
        }

        var deviceId = op.Id is { } givenId ? new DeviceId(givenId) : (DeviceId?)null;

        if (op.Product is not null)
        {
            if (op.Functions is not null || op.Part is not null || op.Mounting is not null)
            {
                return Fail(new Error(
                    "patch.urun_cakisma",
                    $"'{op.Tag}': 'product' verildiğinde functions, part ve mounting yazılamaz; bunlar kütüphaneden gelir."));
            }

            if (catalog is null)
            {
                return Fail(new Error(
                    "patch.kutuphane_yok",
                    $"'{op.Tag}' kütüphane ürünü '{op.Product}' istiyor ama kütüphane verilmedi."));
            }

            var product = catalog.Find(op.Product, op.Manufacturer);
            return product.IsFailure
                ? Fail(product.Error!)
                : Ok(new AddDevice(product.Value.CreateDevice(
                    productTag, functionNode.Value, locationNode.Value, op.Description, deviceId)));
        }

        var functions = (op.Functions ?? []).Select(function => new Function(
            FunctionId.New(),
            function.Kind,
            (function.ConnectionPoints ?? []).Select(name => new ConnectionPoint(ConnectionPointId.New(), name)),
            function.PassThrough,
            function.Description,
            function.RatedCurrentA));

        return Ok(new AddDevice(new Device(
            deviceId ?? DeviceId.New(),
            productTag,
            functions,
            op.Kind,
            functionNode.Value,
            locationNode.Value,
            op.Description,
            op.Part is { } part ? new PartRef(part.Manufacturer, part.OrderCode, part.TypeDesignation) : null,
            op.Mounting is { } m
                ? new Mounting(m.Type, m.WidthMm, m.HeightMm, m.DepthMm, m.ClearanceMm, m.HeatLossW, m.StepRef)
                : null)));
    }

    private static Result<ICommand> Build(AssignSymbolOp op, Project project, ISymbolCatalog? library)
    {
        var point = ReferenceResolver.Point(project, op.Function);
        if (point.IsFailure)
        {
            return Fail(point.Error!);
        }

        if (op.Symbol is null)
        {
            return Ok(new AssignSymbol(point.Value.Function.Id, null));
        }

        var matches = project.Symbols
            .Where(symbol => symbol.Name == op.Symbol
                || (Guid.TryParse(op.Symbol, out var id) && symbol.Id.Value == id))
            .ToList();
        return matches.Count switch
        {
            1 => Ok(new AssignSymbol(point.Value.Function.Id, matches[0].Id)),
            0 => FromLibrary(library, op.Symbol, point.Value.Function.Id),
            _ => Fail(new Error("patch.tag_belirsiz", $"'{op.Symbol}' adında {matches.Count} sembol var; kimliğini kullanın.")),
        };
    }

    // Projede yoksa sembol kütüphanesinden alınır: kopyalanıp atanır, ikisi tek adımdır.
    private static Result<ICommand> FromLibrary(ISymbolCatalog? library, string reference, FunctionId function)
    {
        if (library is null)
        {
            return Fail(new Error("patch.bulunamadi", $"Sembol projede yok: {reference}"));
        }

        var found = library.FindSymbol(reference);
        return found.IsFailure
            ? Fail(found.Error!)
            : Ok(new Transaction(
                $"Kütüphaneden sembol: {found.Value.Name}",
                [new PutSymbol(found.Value), new AssignSymbol(function, found.Value.Id)]));
    }

    private static Result<ICommand> Build(AddPageOp op, Project project)
    {
        var functionNode = ReferenceResolver.Node(project, op.Function, Aspect.Function);
        if (functionNode.IsFailure)
        {
            return Fail(functionNode.Error!);
        }

        var locationNode = ReferenceResolver.Node(project, op.Location, Aspect.Location);
        if (locationNode.IsFailure)
        {
            return Fail(locationNode.Error!);
        }

        return Ok(new AddPage(new Page(
            op.Id is { } id ? new PageId(id) : PageId.New(),
            op.Number,
            op.Title,
            functionNode.Value,
            locationNode.Value)));
    }

    private static Result<ICommand> Build(RemoveDeviceOp op, Project project)
    {
        var device = ReferenceResolver.Device(project, op.Device);
        return device.IsFailure ? Fail(device.Error!) : Ok(new RemoveDevice(device.Value.Id));
    }

    private static Result<ICommand> Build(SetDevicePropertyOp op, Project project)
    {
        var device = ReferenceResolver.Device(project, op.Device);
        return device.IsFailure ? Fail(device.Error!) : Ok(new SetDeviceProperty(device.Value.Id, op.Key, op.Value));
    }

    private static Result<ICommand> Build(ConnectOp op, Project project)
    {
        var from = ReferenceResolver.Point(project, op.From);
        if (from.IsFailure)
        {
            return Fail(from.Error!);
        }

        var to = ReferenceResolver.Point(project, op.To);
        if (to.IsFailure)
        {
            return Fail(to.Error!);
        }

        var core = Core(project, op.Core);
        if (core.IsFailure)
        {
            return Fail(core.Error!);
        }

        return Ok(new Connect(new Connection(
            op.Id is { } id ? new ConnectionId(id) : ConnectionId.New(),
            from.Value.Point.Id,
            to.Value.Point.Id,
            NewWire(op.Wire, existing: null),
            core.Value)));
    }

    private static Result<ICommand> Build(DisconnectOp op, Project project)
    {
        var connection = ReferenceResolver.Connection(project, op.From, op.To);
        return connection.IsFailure ? Fail(connection.Error!) : Ok(new Disconnect(connection.Value.Id));
    }

    private static Result<ICommand> Build(AssignWireOp op, Project project)
    {
        var connection = ReferenceResolver.Connection(project, op.From, op.To);
        if (connection.IsFailure)
        {
            return Fail(connection.Error!);
        }

        var core = Core(project, op.Core);
        if (core.IsFailure)
        {
            return Fail(core.Error!);
        }

        return Ok(new AssignWire(connection.Value.Id, NewWire(op.Wire, connection.Value.Wire), core.Value));
    }

    private static Result<ICommand> Build(RetagOp op, Project project)
    {
        var device = ReferenceResolver.Device(project, op.Device);
        if (device.IsFailure)
        {
            return Fail(device.Error!);
        }

        var tag = ReferenceDesignation.Parse(op.Tag);
        if (tag.IsFailure)
        {
            return Fail(tag.Error!);
        }

        if (tag.Value.ConnectionPoint is not null)
        {
            return Fail(new Error("patch.tag_gecersiz", $"'{op.Tag}' bir device tag'i değil."));
        }

        // Yazılmayan aspect değişmez.
        var functionNode = tag.Value.Function is null
            ? Result.Success(device.Value.FunctionNodeId)
            : ReferenceResolver.Node(project, tag.Value.Function, Aspect.Function);
        if (functionNode.IsFailure)
        {
            return Fail(functionNode.Error!);
        }

        var locationNode = tag.Value.Location is null
            ? Result.Success(device.Value.LocationNodeId)
            : ReferenceResolver.Node(project, tag.Value.Location, Aspect.Location);
        if (locationNode.IsFailure)
        {
            return Fail(locationNode.Error!);
        }

        return Ok(new Retag(
            device.Value.Id,
            tag.Value.Product ?? device.Value.ProductTag,
            functionNode.Value,
            locationNode.Value));
    }

    private static Result<ICommand> Build(AssignIoOp op, Project project)
    {
        var channel = ReferenceResolver.Channel(project, op.Channel);
        if (channel.IsFailure)
        {
            return Fail(channel.Error!);
        }

        var functionId = channel.Value.FunctionId;
        if (op.Function is not null)
        {
            var point = ReferenceResolver.Point(project, op.Function);
            if (point.IsFailure)
            {
                return Fail(point.Error!);
            }

            functionId = point.Value.Function.Id;
        }

        return Ok(new AssignIo(
            channel.Value.Id,
            functionId,
            op.SymbolName ?? channel.Value.SymbolName,
            op.Address ?? channel.Value.Address,
            op.Description ?? channel.Value.Description));
    }

    private static Result<CableCoreId?> Core(Project project, string? reference)
    {
        if (reference is null)
        {
            return Result.Success<CableCoreId?>(null);
        }

        var core = ReferenceResolver.Core(project, reference);
        return core.IsFailure ? Result.Failure<CableCoreId?>(core.Error!) : Result.Success<CableCoreId?>(core.Value);
    }

    // Var olan tel yeniden tanımlanıyorsa kimliği korunur.
    private static Wire? NewWire(NewWireDto? dto, Wire? existing) =>
        dto is null ? null : new Wire(existing?.Id ?? WireId.New(), dto.Number, dto.CrossSectionMm2, dto.Color);

    private static Result<ICommand> Ok(ICommand command) => Result.Success(command);

    private static Result<ICommand> Fail(Error error) => Result.Failure<ICommand>(error);
}

public static class PatchJson
{
    public const int CurrentPatchVersion = 1;

    private static readonly AkeJsonContext Context = new(JsonDefaults.CreateOptions());

    public static Result<Patch> Deserialize(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var versioned = JsonDefaults.ReadVersioned(json, "patchVersion", CurrentPatchVersion, "patch", "Patch dosyası");
        if (versioned.IsFailure)
        {
            return Result.Failure<Patch>(versioned.Error!);
        }

        try
        {
            var dto = versioned.Value.Root.Deserialize(Context.PatchDto);
            return dto is null
                ? Result.Failure<Patch>(new Error("patch.json_gecersiz", "Patch dosyası boş."))
                : Result.Success(new Patch(dto));
        }
        catch (JsonException ex)
        {
            return Result.Failure<Patch>(new Error("patch.json_gecersiz", $"Patch dosyası okunamadı: {ex.Message}"));
        }
    }

    public static Result<Patch> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure<Patch>(new Error("patch.dosya_okunamadi", $"'{path}' okunamadı: {ex.Message}"));
        }
    }
}

/// <summary>
/// Audit log dosyası: satır başına bir JSON kaydı, yalnızca sona eklenir. Proje dosyasının yanında
/// durur (<c>proje.json</c> → <c>proje.audit.jsonl</c>).
/// </summary>
public static class AuditLog
{
    private static readonly AkeJsonContext Context = new(CreateLineOptions());

    public static string PathFor(string projectPath) => Path.ChangeExtension(projectPath, ".audit.jsonl");

    public static Result Append(string path, IEnumerable<AuditEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(entries);

        var lines = entries.Select(entry => JsonSerializer.Serialize(
            new AuditEntryDto(entry.At, entry.Source, entry.Actor, entry.Action, entry.Summary),
            Context.AuditEntryDto));
        try
        {
            File.AppendAllText(path, string.Concat(lines.Select(line => line + "\n")), new UTF8Encoding(false));
            return Result.Success();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result.Failure(new Error("audit.dosya_yazilamadi", $"'{path}' yazılamadı: {ex.Message}"));
        }
    }

    private static JsonSerializerOptions CreateLineOptions()
    {
        var options = JsonDefaults.CreateOptions();
        options.WriteIndented = false;
        return options;
    }
}
