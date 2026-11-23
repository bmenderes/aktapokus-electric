using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>Bağlantı türünü ekler; aynı kimlikli tür varsa yerine geçer. Ad projede tekil olmalı (ADR-0036).</summary>
public sealed class PutConnectionKind(ConnectionKind kind) : ICommand
{
    private ConnectionKind? _previous;
    private int _index = -1;

    public string Description => $"Bağlantı türü kaydedildi: {kind.Name}";

    public Result Execute(Project project)
    {
        if (kind.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var kinds = project.ConnectionKindList;
        _index = kinds.FindIndex(existing => existing.Id == kind.Id);
        if (kinds.Any(other => other.Id != kind.Id && string.Equals(other.Name, kind.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            return Result.Failure(new Error("kind.ad_tekrar", $"'{kind.Name}' adlı bağlantı türü zaten var."));
        }

        if (_index < 0)
        {
            if (project.IdSet().Contains(kind.Id.Value))
            {
                return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({kind.Id})."));
            }

            _previous = null;
            kinds.Add(kind);
            return Result.Success();
        }

        _previous = kinds[_index];
        kinds[_index] = kind;
        return Result.Success();
    }

    public void Undo(Project project)
    {
        if (_previous is null)
        {
            project.ConnectionKindList.Remove(kind);
        }
        else
        {
            project.ConnectionKindList[_index] = _previous;
        }
    }
}

/// <summary>Bağlantı türünü kaldırır; ona atanmış bağlantılar türsüz kalır (geri almak hepsini getirir).</summary>
public sealed class RemoveConnectionKind(ConnectionKindId id) : ICommand
{
    private ConnectionKind? _kind;
    private int _index;
    private List<Connection> _users = [];

    public string Description => _kind is null ? "Bağlantı türü kaldırıldı" : $"Bağlantı türü kaldırıldı: {_kind.Name}";

    public Result Execute(Project project)
    {
        _index = project.ConnectionKindList.FindIndex(kind => kind.Id == id);
        if (_index < 0)
        {
            return Result.Failure(new Error("kind.yok", $"Bağlantı türü modelde yok ({id})."));
        }

        _kind = project.ConnectionKindList[_index];
        _users = [.. project.Connections.Where(connection => connection.KindId == id)];
        foreach (var connection in _users)
        {
            connection.KindId = null;
        }

        project.ConnectionKindList.RemoveAt(_index);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        project.ConnectionKindList.Insert(_index, _kind!);
        foreach (var connection in _users)
        {
            connection.KindId = id;
        }
    }
}

/// <summary>Bağlantıya tür atar; <c>null</c> türü kaldırır.</summary>
public sealed class SetConnectionKind(ConnectionId connectionId, ConnectionKindId? kindId) : ICommand
{
    private Connection? _connection;
    private ConnectionKindId? _previous;

    public string Description => kindId is null ? "Bağlantı türü kaldırıldı" : "Bağlantı türü atandı";

    public Result Execute(Project project)
    {
        var connection = project.FindConnection(connectionId);
        if (connection is null)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        if (kindId is { } id && project.FindConnectionKind(id) is null)
        {
            return Result.Failure(new Error("kind.yok", $"Bağlantı türü modelde yok ({id})."));
        }

        _connection = connection;
        _previous = connection.KindId;
        connection.KindId = kindId;
        return Result.Success();
    }

    public void Undo(Project project) => _connection!.KindId = _previous;
}
