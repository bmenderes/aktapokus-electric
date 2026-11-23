using Ake.Core;
using Ake.Core.Model;

namespace Ake.Serialization;

/// <summary>
/// Patch'teki atıfları modelin o andaki durumuna göre çözer. Atıf ya bir kimliktir (GUID) ya da bir
/// tag'dir. Tag'de yazılmayan aspect eşleşmeyi daraltmaz; birden çok nesne uyuyorsa atıf belirsizdir.
/// </summary>
internal static class ReferenceResolver
{
    public static Result<Device> Device(Project project, string reference)
    {
        if (Guid.TryParse(reference, out var id))
        {
            return project.FindDevice(new DeviceId(id)) is { } byId
                ? Result.Success(byId)
                : NotFound<Device>("Device", reference);
        }

        var parsed = ReferenceDesignation.Parse(reference);
        return parsed.IsFailure ? Result.Failure<Device>(parsed.Error!) : Device(project, parsed.Value, reference);
    }

    public static Result<ConnectionPointLocation> Point(Project project, string reference)
    {
        if (Guid.TryParse(reference, out var id))
        {
            return project.Locate(new ConnectionPointId(id)) is { } byId
                ? Result.Success(byId)
                : NotFound<ConnectionPointLocation>("Bağlantı noktası", reference);
        }

        var parsed = ReferenceDesignation.Parse(reference);
        if (parsed.IsFailure)
        {
            return Result.Failure<ConnectionPointLocation>(parsed.Error!);
        }

        if (parsed.Value.ConnectionPoint is not { } pointName)
        {
            return Result.Failure<ConnectionPointLocation>(new Error(
                "patch.nokta_yok", $"'{reference}' bir bağlantı noktası göstermiyor (':' ile nokta adı gerekli)."));
        }

        var device = Device(project, parsed.Value, reference);
        if (device.IsFailure)
        {
            return Result.Failure<ConnectionPointLocation>(device.Error!);
        }

        var matches = device.Value.Functions
            .SelectMany(function => function.ConnectionPoints.Select(point => (function, point)))
            .Where(item => item.point.Name == pointName)
            .ToList();

        return matches.Count switch
        {
            1 => Result.Success(new ConnectionPointLocation(device.Value, matches[0].function, matches[0].point)),
            0 => NotFound<ConnectionPointLocation>("Bağlantı noktası", reference),
            _ => Ambiguous<ConnectionPointLocation>(reference, matches.Count),
        };
    }

    public static Result<Connection> Connection(Project project, string from, string to)
    {
        var a = Point(project, from);
        if (a.IsFailure)
        {
            return Result.Failure<Connection>(a.Error!);
        }

        var b = Point(project, to);
        if (b.IsFailure)
        {
            return Result.Failure<Connection>(b.Error!);
        }

        var (first, second) = (a.Value.Point.Id, b.Value.Point.Id);
        var matches = project.Connections
            .Where(c => (c.From == first && c.To == second) || (c.From == second && c.To == first))
            .ToList();

        return matches.Count switch
        {
            1 => Result.Success(matches[0]),
            0 => NotFound<Connection>("Bağlantı", $"{from} – {to}"),
            _ => Ambiguous<Connection>($"{from} – {to}", matches.Count),
        };
    }

    /// <summary>Kablo damarı: -W1:GNYE ya da damarın kimliği.</summary>
    public static Result<CableCoreId> Core(Project project, string reference)
    {
        var cores = project.Cables.SelectMany(cable => cable.Cores.Select(core => (cable, core)));

        if (Guid.TryParse(reference, out var id))
        {
            return cores.Any(item => item.core.Id.Value == id)
                ? Result.Success(new CableCoreId(id))
                : NotFound<CableCoreId>("Kablo damarı", reference);
        }

        var parsed = ReferenceDesignation.Parse(reference);
        if (parsed.IsFailure)
        {
            return Result.Failure<CableCoreId>(parsed.Error!);
        }

        var matches = cores
            .Where(item => item.cable.ProductTag == parsed.Value.Product
                && item.core.Designation == parsed.Value.ConnectionPoint)
            .ToList();

        return matches.Count switch
        {
            1 => Result.Success(matches[0].core.Id),
            0 => NotFound<CableCoreId>("Kablo damarı", reference),
            _ => Ambiguous<CableCoreId>(reference, matches.Count),
        };
    }

    /// <summary>Kanal: kimliği ya da atanmış olduğu elektriksel uç (-A12:3).</summary>
    public static Result<Channel> Channel(Project project, string reference)
    {
        if (Guid.TryParse(reference, out var id)
            && project.Channels.FirstOrDefault(channel => channel.Id.Value == id) is { } byId)
        {
            return Result.Success(byId);
        }

        var point = Point(project, reference);
        if (point.IsFailure)
        {
            return Result.Failure<Channel>(point.Error!);
        }

        var matches = project.Channels.Where(channel => channel.FunctionId == point.Value.Function.Id).ToList();
        return matches.Count switch
        {
            1 => Result.Success(matches[0]),
            0 => NotFound<Channel>("PLC kanalı", reference),
            _ => Ambiguous<Channel>(reference, matches.Count),
        };
    }

    /// <summary>Yapı düğümü, kökten itibaren yoluyla (A1.M1). Yol <c>null</c> ise düğüm de <c>null</c>.</summary>
    public static Result<StructureNodeId?> Node(Project project, string? path, Aspect aspect)
    {
        if (path is null)
        {
            return Result.Success<StructureNodeId?>(null);
        }

        var node = project.Structure.FirstOrDefault(
            candidate => candidate.Aspect == aspect && project.StructurePath(candidate.Id) == path);

        return node is not null
            ? Result.Success<StructureNodeId?>(node.Id)
            : Result.Failure<StructureNodeId?>(new Error(
                "patch.dugum_yok",
                $"Yapı düğümü modelde yok: {(aspect == Aspect.Function ? '=' : '+')}{path}"));
    }

    private static Result<Device> Device(Project project, ReferenceDesignation designation, string reference)
    {
        if (designation.Product is null)
        {
            return Result.Failure<Device>(new Error("patch.tag_gecersiz", $"'{reference}' içinde ürün tag'i (-…) yok."));
        }

        var matches = project.Devices
            .Where(device => device.ProductTag == designation.Product
                && (designation.Function is null || project.StructurePath(device.FunctionNodeId) == designation.Function)
                && (designation.Location is null || project.StructurePath(device.LocationNodeId) == designation.Location))
            .ToList();

        return matches.Count switch
        {
            1 => Result.Success(matches[0]),
            0 => NotFound<Device>("Device", reference),
            _ => Ambiguous<Device>(reference, matches.Count),
        };
    }

    private static Result<T> NotFound<T>(string what, string reference) =>
        Result.Failure<T>(new Error("patch.bulunamadi", $"{what} modelde yok: {reference}"));

    private static Result<T> Ambiguous<T>(string reference, int count) =>
        Result.Failure<T>(new Error(
            "patch.tag_belirsiz", $"'{reference}' {count} nesneye uyuyor; kimlik ya da tam tag kullanın."));
}
