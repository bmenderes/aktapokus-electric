using Ake.Core.Model;

namespace Ake.Core.Commands;

// Modelin büyük parçalarını ekleyen komutlar (ADR-0031): yapı düğümü, kablo, potansiyel işareti, PLC denetleyicisi. Hepsi
// aynı desendedir: kimlikler tekil mi bakılır, nesne eklenir, modelin bütünlüğü (Project.CheckIntegrity) denetlenir; bozulursa
// ekleme geri alınır ve hata döner. Böylece referanslar (üst düğüm, uç, function) eksik bir nesne modele giremez.

/// <summary>Yapı ağacına fonksiyon (<c>=</c>) ya da yer (<c>+</c>) düğümü ekler. Üst düğümü önce eklenmiş olmalıdır.</summary>
public sealed class AddStructureNode(StructureNode node) : ICommand
{
    public string Description => $"Yapı düğümü eklendi: {(node.Aspect == Aspect.Function ? "=" : "+")}{node.Name}";

    public Result Execute(Project project)
    {
        if (project.IdSet().Contains(node.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({node.Id})."));
        }

        project.AddNode(node);
        if (project.CheckIntegrity() is { IsFailure: true } broken)
        {
            project.RemoveNode(node);
            return broken;
        }

        return Result.Success();
    }

    public void Undo(Project project) => project.RemoveNode(node);
}

/// <summary>Yapı ağacındaki bir düğümün adını ve açıklamasını değiştirir.</summary>
public sealed class SetStructureNode(StructureNodeId nodeId, string name, string? description) : ICommand
{
    private StructureNode? _node;
    private (string Name, string? Description) _previous;

    public string Description => $"Yapı düğümü güncellendi: {name}";

    public Result Execute(Project project)
    {
        var node = project.Structure.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node is null)
        {
            return Result.Failure(new Error("structure.dugum_yok", $"Yapı düğümü bulunamadı ({nodeId})."));
        }

        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Any(c => c is '.' or '=' or '+' or '-' or ':' || char.IsWhiteSpace(c)))
        {
            return Result.Failure(new Error("structure.ad_gecersiz", $"Yapı düğümü adı boş olamaz; boşluk ve '. = + - :' içeremez: '{name}'"));
        }

        if (project.Structure.Any(other => other.Id != nodeId && other.Aspect == node.Aspect && other.ParentId == node.ParentId && other.Name == trimmed))
        {
            return Result.Failure(new Error("structure.ad_tekrar", $"Aynı üst düğüm altında '{trimmed}' adlı başka bir düğüm var."));
        }

        _node = node;
        _previous = (node.Name, node.Description);
        node.Name = trimmed;
        node.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _node!.Name = _previous.Name;
        _node.Description = _previous.Description;
    }
}

/// <summary>Kullanılmayan (alt düğümü, sayfası ve cihazı olmayan) yapı düğümünü siler.</summary>
public sealed class RemoveStructureNode(StructureNodeId nodeId) : ICommand
{
    private StructureNode? _node;

    public string Description => _node is null ? "Yapı düğümü silindi" : $"Yapı düğümü silindi: {_node.Name}";

    public Result Execute(Project project)
    {
        var node = project.Structure.FirstOrDefault(candidate => candidate.Id == nodeId);
        if (node is null)
        {
            return Result.Failure(new Error("structure.dugum_yok", $"Yapı düğümü bulunamadı ({nodeId})."));
        }

        var inUse = project.Structure.Any(other => other.ParentId == nodeId)
            || project.Devices.Any(device => device.FunctionNodeId == nodeId || device.LocationNodeId == nodeId)
            || project.Pages.Any(page => page.FunctionNodeId == nodeId || page.LocationNodeId == nodeId);
        if (inUse)
        {
            return Result.Failure(new Error("structure.kullaniliyor", $"'{node.Name}' düğümü alt düğüm, sayfa ya da cihaz tarafından kullanılıyor; önce onları kaldırın."));
        }

        _node = node;
        project.RemoveNode(node);
        return Result.Success();
    }

    public void Undo(Project project) => project.AddNode(_node!);
}

/// <summary>Projeye çok damarlı kablo ekler (damarları ile birlikte).</summary>
public sealed class AddCable(Cable cable) : ICommand
{
    public string Description => $"Kablo eklendi: -{cable.ProductTag}";

    public Result Execute(Project project)
    {
        var used = project.IdSet();
        if (used.Contains(cable.Id.Value) || cable.Cores.Any(core => used.Contains(core.Id.Value)))
        {
            return Result.Failure(new Error("id.tekrar", $"Kablonun ya da damarlarının kimliği zaten kullanılıyor ({cable.Id})."));
        }

        project.CableList.Add(cable);
        if (project.CheckIntegrity() is { IsFailure: true } broken)
        {
            project.CableList.Remove(cable);
            return broken;
        }

        return Result.Success();
    }

    public void Undo(Project project) => project.CableList.Remove(cable);
}

/// <summary>Mevcut bir kabloyu (tag, tip ve damar listesi) günceller.</summary>
public sealed class SetCable(Cable cable) : ICommand
{
    private Cable? _previous;
    private int _index;

    public string Description => $"Kablo güncellendi: -{cable.ProductTag}";

    public Result Execute(Project project)
    {
        _index = project.CableList.FindIndex(candidate => candidate.Id == cable.Id);
        if (_index < 0)
        {
            return Result.Failure(new Error("cable.yok", $"Kablo modelde yok ({cable.Id})."));
        }

        if (!ReferenceDesignation.IsValidProductTag(cable.ProductTag))
        {
            return Result.Failure(new Error("tag.gecersiz", $"Geçersiz kablo tag'i: '{cable.ProductTag}'"));
        }

        var coreNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var core in cable.Cores)
        {
            var cleaned = core.Designation?.Trim() ?? string.Empty;
            if (cleaned.Length == 0 || !coreNames.Add(cleaned))
            {
                return Result.Failure(new Error("cable.damar_gecersiz", $"Damar adları boş olamaz ve aynı kabloda tekrarlanamaz: '{core.Designation}'"));
            }
        }

        _previous = project.CableList[_index];
        project.CableList[_index] = cable;
        if (project.CheckIntegrity() is { IsFailure: true } broken)
        {
            project.CableList[_index] = _previous;
            return broken;
        }

        return Result.Success();
    }

    public void Undo(Project project) => project.CableList[_index] = _previous!;
}

/// <summary>Kabloyu ve damarlarının bağlantılardaki atamalarını kaldırır.</summary>
public sealed class RemoveCable(CableId cableId) : ICommand
{
    private Cable? _cable;
    private int _index;
    private List<(Connection Connection, CableCoreId CoreId)> _clearedConnections = [];

    public string Description => _cable is null ? "Kablo silindi" : $"Kablo silindi: -{_cable.ProductTag}";

    public Result Execute(Project project)
    {
        _index = project.CableList.FindIndex(candidate => candidate.Id == cableId);
        if (_index < 0)
        {
            return Result.Failure(new Error("cable.yok", $"Kablo modelde yok ({cableId})."));
        }

        var cable = project.CableList[_index];
        var coreIds = cable.Cores.Select(core => core.Id).ToHashSet();
        _clearedConnections = [];
        foreach (var connection in project.Connections)
        {
            if (connection.CoreId is { } coreId && coreIds.Contains(coreId))
            {
                _clearedConnections.Add((connection, coreId));
                connection.CoreId = null;
            }
        }

        _cable = cable;
        project.CableList.RemoveAt(_index);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        project.CableList.Insert(_index, _cable!);
        foreach (var (connection, coreId) in _clearedConnections)
        {
            connection.CoreId = coreId;
        }
    }
}

/// <summary>Bir bağlantı noktasına potansiyel adı işareti (L1, 24VDC, 0V) koyar.</summary>
public sealed class AddPotentialSource(PotentialSource source) : ICommand
{
    public string Description => $"Potansiyel işareti eklendi: {source.Name}";

    public Result Execute(Project project)
    {
        if (project.IdSet().Contains(source.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({source.Id})."));
        }

        project.PotentialSourceList.Add(source);
        if (project.CheckIntegrity() is { IsFailure: true } broken)
        {
            project.PotentialSourceList.Remove(source);
            return broken;
        }

        return Result.Success();
    }

    public void Undo(Project project) => project.PotentialSourceList.Remove(source);
}

/// <summary>Bir potansiyel işaretini projeden kaldırır.</summary>
public sealed class RemovePotentialSource(PotentialSourceId sourceId) : ICommand
{
    private PotentialSource? _source;
    private int _index;

    public string Description => _source is null ? "Potansiyel işareti silindi" : $"Potansiyel işareti silindi: {_source.Name}";

    public Result Execute(Project project)
    {
        _index = project.PotentialSourceList.FindIndex(candidate => candidate.Id == sourceId);
        if (_index < 0)
        {
            return Result.Failure(new Error("potential.yok", $"Potansiyel işareti modelde yok ({sourceId})."));
        }

        _source = project.PotentialSourceList[_index];
        project.PotentialSourceList.RemoveAt(_index);
        return Result.Success();
    }

    public void Undo(Project project) => project.PotentialSourceList.Insert(_index, _source!);
}

/// <summary>
/// PLC denetleyicisini ağları, düğümleri, modülleri ve kanallarıyla ekler. Denetleyici, düğüm ve modüllerin cihazları modelde
/// olmalıdır; kanalların function atamaları varsa onlar da.
/// </summary>
public sealed class AddController(Controller controller) : ICommand
{
    public string Description => $"PLC denetleyicisi eklendi: {controller.Name}";

    public Result Execute(Project project)
    {
        var used = project.IdSet();
        if (used.Contains(controller.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({controller.Id})."));
        }

        project.ControllerList.Add(controller);
        if (project.CheckIntegrity() is { IsFailure: true } broken)
        {
            project.ControllerList.Remove(controller);
            return broken;
        }

        return Result.Success();
    }

    public void Undo(Project project) => project.ControllerList.Remove(controller);
}
