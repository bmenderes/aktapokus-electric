using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>Bağlantı türleri (renk ve çizgi biçimi), oturum tarafı (ADR-0036).</summary>
public sealed partial class EditorSession
{
    /// <summary>Projedeki bağlantı türleri; proje yoksa boş.</summary>
    public IReadOnlyList<ConnectionKind> ConnectionKinds => Project?.ConnectionKinds ?? [];

    /// <summary>
    /// Hazır bağlantı türlerini projeye ekler (<see cref="ConnectionKind.Standard"/>); projede aynı adlı olan atlanır. Tek adımda geri
    /// alınır. Eklenecek bir şey yoksa geçmişe kayıt düşmez.
    /// </summary>
    public Result AddStandardConnectionKinds()
    {
        if (Project is null)
        {
            return NoProject();
        }

        var present = Project.ConnectionKinds.Select(kind => kind.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        return ExecuteIfAny(new Transaction(
            "Standart bağlantı türleri eklendi",
            [.. ConnectionKind.Standard.Where(kind => !present.Contains(kind.Name)).Select(kind => new PutConnectionKind(kind))]));
    }

    /// <summary>Verilen bağlantılara tür atar; <c>null</c> türü kaldırır. Tek adımdır, geri alınır.</summary>
    public Result SetConnectionKind(IEnumerable<ConnectionId> connections, ConnectionKindId? kind)
    {
        ArgumentNullException.ThrowIfNull(connections);

        return Project is null
            ? NoProject()
            : ExecuteIfAny(new Transaction(
                kind is null ? "Bağlantı türü kaldırıldı" : "Bağlantı türü atandı",
                [.. connections.Select(connection => new SetConnectionKind(connection, kind))]));
    }
}
