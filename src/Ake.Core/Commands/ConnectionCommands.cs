using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>İki bağlantı noktası arasında Connection kurar.</summary>
public sealed class Connect(Connection connection) : ICommand
{
    private string _description = "Bağlantı kuruldu";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var existing = project.IdSet();
        if (!existing.Add(connection.Id.Value) || (connection.Wire is { } wire && !existing.Add(wire.Id.Value)))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({connection.Id})."));
        }

        var error = project.CheckConnection(connection, project.CoreIds(), ConnectionMedia.UsedCores(project));
        if (error is not null)
        {
            return Result.Failure(error);
        }

        project.ConnectionList.Add(connection);
        _description =
            $"Bağlantı kuruldu: {project.DesignationOf(connection.From)} – {project.DesignationOf(connection.To)}";
        return Result.Success();
    }

    public void Undo(Project project) => project.ConnectionList.Remove(connection);
}

/// <summary>Connection'ı, teli ve damar atamasıyla birlikte kaldırır.</summary>
public sealed class Disconnect(ConnectionId connectionId) : ICommand
{
    private Connection? _connection;
    private int _index;
    private string _description = "Bağlantı kaldırıldı";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var connection = project.FindConnection(connectionId);
        if (connection is null)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        _description =
            $"Bağlantı kaldırıldı: {project.DesignationOf(connection.From)} – {project.DesignationOf(connection.To)}";
        _connection = connection;
        _index = project.ConnectionList.IndexOf(connection);
        project.ConnectionList.RemoveAt(_index);
        return Result.Success();
    }

    public void Undo(Project project) => project.ConnectionList.Insert(_index, _connection!);
}

/// <summary>
/// Bağlantının köşe noktalarını değiştirir (ADR-0029). Boş liste (ya da <c>null</c>) telin yeniden otomatik yönlendirilmesini
/// ister.
/// </summary>
public sealed class SetWireRoute(ConnectionId connectionId, IReadOnlyList<WirePoint>? route) : ICommand
{
    private Connection? _connection;
    private IReadOnlyList<WirePoint> _previous = [];

    public string Description => route is { Count: > 0 } ? "Tel yönlendirildi" : "Tel otomatik yönlendirmeye döndü";

    public Result Execute(Project project)
    {
        var connection = project.FindConnection(connectionId);
        if (connection is null)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        var points = route ?? [];
        if (Project.CheckRoute(points) is { } problem)
        {
            return Result.Failure(new Error("connection.yol_gecersiz", $"Köşe noktaları geçersiz: {problem}."));
        }

        _connection = connection;
        _previous = connection.Route;
        connection.Route = [.. points];
        return Result.Success();
    }

    public void Undo(Project project) => _connection!.Route = _previous;
}

/// <summary>
/// Bağlantının ortamını değiştirir: tel, kablo damarı ya da hiçbiri (ikisi de <c>null</c>).
/// </summary>
public sealed class AssignWire(ConnectionId connectionId, Wire? wire, CableCoreId? coreId) : ICommand
{
    private Connection? _connection;
    private (Wire? Wire, CableCoreId? CoreId) _previous;
    private string _description = "Tel atandı";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var connection = project.FindConnection(connectionId);
        if (connection is null)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        // Aynı telin özelliklerini değiştirmek (numara, kesit) kimlik çakışması değildir.
        if (wire is not null && wire.Id != connection.Wire?.Id && project.IdSet().Contains(wire.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({wire.Id})."));
        }

        var error = Project.CheckMedium(
            connectionId, wire, coreId, project.CoreIds(), ConnectionMedia.UsedCores(project, except: connection));
        if (error is not null)
        {
            return Result.Failure(error);
        }

        _connection = connection;
        _previous = (connection.Wire, connection.CoreId);
        connection.Wire = wire;
        connection.CoreId = coreId;

        var ends = $"{project.DesignationOf(connection.From)} – {project.DesignationOf(connection.To)}";
        _description = wire is not null
            ? $"Tel atandı: {ends}, no {wire.Number ?? "-"}"
            : coreId is not null ? $"Kablo damarı atandı: {ends}" : $"Tel kaldırıldı: {ends}";
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _connection!.Wire = _previous.Wire;
        _connection.CoreId = _previous.CoreId;
    }
}

internal static class ConnectionMedia
{
    public static HashSet<CableCoreId> UsedCores(Project project, Connection? except = null) =>
        [.. project.Connections
            .Where(connection => !ReferenceEquals(connection, except))
            .Select(connection => connection.CoreId)
            .OfType<CableCoreId>()];
}
