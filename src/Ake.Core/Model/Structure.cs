namespace Ake.Core.Model;

/// <summary>IEC 81346 aspect'i. Ürün aspect'i (<c>-</c>) Device'ın kendi tag'idir, düğüm değildir.</summary>
public enum Aspect
{
    Function,
    Location,
}

/// <summary>Fonksiyon (<c>=</c>) ya da yer (<c>+</c>) ağacının bir düğümü.</summary>
public sealed class StructureNode(
    StructureNodeId id,
    Aspect aspect,
    string name,
    StructureNodeId? parentId = null,
    string? description = null)
{
    public StructureNodeId Id { get; } = id;

    public Aspect Aspect { get; } = aspect;

    public string Name { get; internal set; } = name;

    public StructureNodeId? ParentId { get; internal set; } = parentId;

    public string? Description { get; internal set; } = description;
}
