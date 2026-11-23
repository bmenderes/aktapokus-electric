using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Editor;

/// <summary>Faz 6 model formları, oturum tarafı: yapı (= / +), kablo / damar ve potansiyel işaretleri.</summary>
public sealed partial class EditorSession
{
    /// <summary>Yapı ağacına yeni bir fonksiyon (=) ya da yer (+) düğümü ekler; kimliğini döner.</summary>
    public Result<StructureNodeId> AddStructureNode(
        Aspect aspect, string name, StructureNodeId? parentId = null, string? description = null)
    {
        if (Project is null)
        {
            return Result.Failure<StructureNodeId>(NoProject().Error!);
        }

        var trimmed = TrimStructurePrefix(name);
        if (ValidateNodeName(trimmed) is { } invalid)
        {
            return Result.Failure<StructureNodeId>(invalid);
        }

        if (Project.Structure.Any(other => other.Aspect == aspect && other.ParentId == parentId && other.Name == trimmed))
        {
            return Result.Failure<StructureNodeId>(new Error(
                "structure.ad_tekrar", $"Aynı üst düğüm altında '{trimmed}' adlı başka bir düğüm var."));
        }

        var node = new StructureNode(
            StructureNodeId.New(),
            aspect,
            trimmed,
            parentId,
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
        var added = Execute(new AddStructureNode(node));
        return added.IsFailure ? Result.Failure<StructureNodeId>(added.Error!) : Result.Success(node.Id);
    }

    /// <summary>Var olan yapı düğümünün adını ve açıklamasını günceller.</summary>
    public Result UpdateStructureNode(StructureNodeId nodeId, string name, string? description = null) =>
        Project is null
            ? NoProject()
            : Execute(new SetStructureNode(nodeId, TrimStructurePrefix(name), description));

    /// <summary>Kullanılmayan yapı düğümünü siler (alt düğüm, sayfa veya cihaz varsa reddedilir).</summary>
    public Result RemoveStructureNode(StructureNodeId nodeId) =>
        Project is null ? NoProject() : Execute(new RemoveStructureNode(nodeId));

    /// <summary>En az bir damarla yeni bir kablo ekler; damar adları boş olamaz ve aynı kabloda tekrarlanamaz.</summary>
    public Result<CableId> AddCableItem(
        string productTag,
        string? type,
        IReadOnlyList<(string Designation, decimal? CrossSectionMm2, string? Color)> cores)
    {
        ArgumentNullException.ThrowIfNull(cores);
        if (Project is null)
        {
            return Result.Failure<CableId>(NoProject().Error!);
        }

        var tag = (productTag ?? string.Empty).Trim().TrimStart('-');
        if (!ReferenceDesignation.IsValidProductTag(tag))
        {
            return Result.Failure<CableId>(new Error("tag.gecersiz", $"Geçersiz kablo tag'i: '{productTag}'"));
        }

        if (Project.Cables.Any(cable => cable.ProductTag == tag))
        {
            return Result.Failure<CableId>(new Error("cable.tag_tekrar", $"-{tag} adlı bir kablo zaten var."));
        }

        if (cores.Count == 0)
        {
            return Result.Failure<CableId>(new Error("cable.damar_bos", "Kabloda en az bir damar olmalıdır."));
        }

        if (CheckCoreNames(cores.Select(core => core.Designation)) is { } coreError)
        {
            return Result.Failure<CableId>(coreError);
        }

        var cable = new Cable(
            CableId.New(),
            tag,
            cores.Select(core => new CableCore(
                CableCoreId.New(),
                core.Designation.Trim(),
                core.CrossSectionMm2,
                string.IsNullOrWhiteSpace(core.Color) ? null : core.Color.Trim())),
            string.IsNullOrWhiteSpace(type) ? null : type.Trim());
        var added = Execute(new AddCable(cable));
        return added.IsFailure ? Result.Failure<CableId>(added.Error!) : Result.Success(cable.Id);
    }

    /// <summary>Var olan kablonun tag'ini, tipini ve damar listesini günceller.</summary>
    public Result UpdateCableItem(
        CableId cableId,
        string productTag,
        string? type,
        IReadOnlyList<(CableCoreId? Id, string Designation, decimal? CrossSectionMm2, string? Color)> cores)
    {
        ArgumentNullException.ThrowIfNull(cores);
        if (Project is null)
        {
            return NoProject();
        }

        var tag = (productTag ?? string.Empty).Trim().TrimStart('-');
        if (Project.Cables.Any(cable => cable.Id != cableId && cable.ProductTag == tag))
        {
            return Result.Failure(new Error("cable.tag_tekrar", $"-{tag} adlı bir kablo zaten var."));
        }

        if (cores.Count == 0)
        {
            return Result.Failure(new Error("cable.damar_bos", "Kabloda en az bir damar olmalıdır."));
        }

        var updated = new Cable(
            cableId,
            tag,
            cores.Select(core => new CableCore(
                core.Id ?? CableCoreId.New(),
                core.Designation?.Trim() ?? string.Empty,
                core.CrossSectionMm2,
                string.IsNullOrWhiteSpace(core.Color) ? null : core.Color.Trim())),
            string.IsNullOrWhiteSpace(type) ? null : type.Trim());
        return Execute(new SetCable(updated));
    }

    /// <summary>Kabloyu ve bağlantılardaki damar atamalarını kaldırır.</summary>
    public Result RemoveCableItem(CableId cableId) =>
        Project is null ? NoProject() : Execute(new RemoveCable(cableId));

    /// <summary>
    /// Bir bağlantıya ya kablo damarı (<paramref name="coreId"/>) ya da fiziksel tel (numara, kesit, renk) atar.
    /// İkisi birlikte atanamaz. Değişiklik yoksa geçmişe kayıt düşmez.
    /// </summary>
    public Result AssignConnectionMedium(
        ConnectionId connectionId,
        string? wireNumber,
        decimal? crossSectionMm2,
        string? color,
        CableCoreId? coreId)
    {
        if (Project is null)
        {
            return NoProject();
        }

        if (Project.FindConnection(connectionId) is not { } existing)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        if (coreId is { } cid)
        {
            return existing.CoreId == cid && existing.Wire is null
                ? Result.Success()
                : Execute(new AssignWire(connectionId, null, cid));
        }

        var number = string.IsNullOrWhiteSpace(wireNumber) ? null : wireNumber.Trim();
        var cleanedColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        if (existing.CoreId is null && existing.Wire is { } current
            && current.Number == number && current.CrossSectionMm2 == crossSectionMm2 && current.Color == cleanedColor)
        {
            return Result.Success();
        }

        var wire = new Wire(existing.Wire?.Id ?? WireId.New(), number, crossSectionMm2, cleanedColor);
        return Execute(new AssignWire(connectionId, wire, null));
    }

    /// <summary>
    /// Bir bağlantının tel/damar ortamını, rengini, çizgi biçimini (düz, kesik, noktalı) ve isteğe bağlı bağlantı türünü
    /// tek geri alma adımında günceller.
    /// </summary>
    public Result AssignConnectionAppearance(
        ConnectionId connectionId,
        string? wireNumber,
        decimal? crossSectionMm2,
        string? color,
        CableCoreId? coreId,
        LineStyle lineStyle,
        ConnectionKindId? explicitKindId = null)
    {
        if (Project is null)
        {
            return NoProject();
        }

        if (Project.FindConnection(connectionId) is not { } existing)
        {
            return Result.Failure(new Error("connection.yok", $"Bağlantı modelde yok ({connectionId})."));
        }

        var commands = new List<ICommand>();

        if (coreId is { } cid)
        {
            if (existing.CoreId != cid || existing.Wire is not null)
            {
                commands.Add(new AssignWire(connectionId, null, cid));
            }
        }
        else
        {
            var number = string.IsNullOrWhiteSpace(wireNumber) ? null : wireNumber.Trim();
            var cleanedColor = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
            if (!(existing.CoreId is null
                && existing.Wire is { } current
                && current.Number == number
                && current.CrossSectionMm2 == crossSectionMm2
                && current.Color == cleanedColor))
            {
                var wire = new Wire(existing.Wire?.Id ?? WireId.New(), number, crossSectionMm2, cleanedColor);
                commands.Add(new AssignWire(connectionId, wire, null));
            }
        }

        if (explicitKindId is { } chosenKind)
        {
            if (existing.KindId != chosenKind)
            {
                commands.Add(new SetConnectionKind(connectionId, chosenKind));
            }
        }
        else if (lineStyle != LineStyle.Solid)
        {
            var rawColor = color;
            if (string.IsNullOrWhiteSpace(rawColor) && coreId is { } cid2)
            {
                rawColor = Project.Cables
                    .SelectMany(cable => cable.Cores)
                    .FirstOrDefault(core => core.Id == cid2)
                    ?.Color;
            }

            var hex = WireColorPalette.ResolveHex(rawColor);
            var customKind = Project.ConnectionKinds.FirstOrDefault(kind =>
                string.Equals(kind.Color, hex, StringComparison.OrdinalIgnoreCase) && kind.Style == lineStyle);
            if (customKind is null)
            {
                var styleLabel = lineStyle switch
                {
                    LineStyle.Dashed => "Kesik",
                    LineStyle.DashDot => "Çizgi-nokta",
                    LineStyle.Dotted => "Noktalı",
                    _ => "Düz",
                };
                var hash = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes($"aktapokus.ozel-tur/{hex}/{lineStyle}"));
                customKind = new ConnectionKind(new ConnectionKindId(new Guid(hash[..16])), $"{hex} {styleLabel}", hex, lineStyle);
                commands.Add(new PutConnectionKind(customKind));
            }

            if (existing.KindId != customKind.Id)
            {
                commands.Add(new SetConnectionKind(connectionId, customKind.Id));
            }
        }
        else if (existing.KindId is not null)
        {
            commands.Add(new SetConnectionKind(connectionId, null));
        }

        return ExecuteIfAny(new Transaction("Bağlantı özellikleri güncellendi", commands));
    }

    /// <summary>Bir bağlantı noktasına L1, 24VDC, 0V gibi potansiyel adı işareti koyar.</summary>
    public Result<PotentialSourceId> AddPotentialMarker(ConnectionPointId pointId, string name, PotentialKind kind)
    {
        if (Project is null)
        {
            return Result.Failure<PotentialSourceId>(NoProject().Error!);
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Failure<PotentialSourceId>(new Error("potential.ad_bos", "Potansiyel adı boş olamaz."));
        }

        var source = new PotentialSource(PotentialSourceId.New(), trimmed, kind, pointId);
        var added = Execute(new AddPotentialSource(source));
        return added.IsFailure ? Result.Failure<PotentialSourceId>(added.Error!) : Result.Success(source.Id);
    }

    /// <summary>Potansiyel işaretini kaldırır.</summary>
    public Result RemovePotentialMarker(PotentialSourceId sourceId) =>
        Project is null ? NoProject() : Execute(new RemovePotentialSource(sourceId));

    private static string TrimStructurePrefix(string? name) => (name ?? string.Empty).Trim().TrimStart('=', '+');

    private static Error? ValidateNodeName(string trimmed) =>
        trimmed.Length == 0 || trimmed.Any(c => c is '.' or '=' or '+' or '-' or ':' || char.IsWhiteSpace(c))
            ? new Error("structure.ad_gecersiz", $"Yapı düğümü adı boş olamaz; boşluk ve '. = + - :' içeremez: '{trimmed}'")
            : null;

    private static Error? CheckCoreNames(IEnumerable<string> designations)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in designations)
        {
            var cleaned = raw?.Trim() ?? string.Empty;
            if (cleaned.Length == 0 || !seen.Add(cleaned))
            {
                return new Error("cable.damar_gecersiz", $"Damar adları boş olamaz ve aynı kabloda tekrarlanamaz: '{raw}'");
            }
        }

        return null;
    }
}
