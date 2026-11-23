using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>Projeye sayfa ekler (sona).</summary>
public sealed class AddPage(Page page, int? index = null) : ICommand
{
    public string Description => $"Sayfa eklendi: {page.Number} {page.Title}";

    public Result Execute(Project project)
    {
        if (string.IsNullOrWhiteSpace(page.Number))
        {
            return Result.Failure(new Error("page.numara_bos", "Sayfa numarası boş olamaz."));
        }

        if (project.IdSet().Contains(page.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({page.Id})."));
        }

        var owner = $"Sayfa {page.Number}";
        var nodeError = project.CheckNode(page.FunctionNodeId, Aspect.Function, owner)
            ?? project.CheckNode(page.LocationNodeId, Aspect.Location, owner);
        if (nodeError is not null)
        {
            return Result.Failure(nodeError);
        }

        if (page.TemplateId is { } templateId && project.SheetTemplates.All(template => template.Id != templateId))
        {
            return Result.Failure(new Error("page.sablon_yok", $"{owner}: sayfa şablonu modelde yok ({templateId})."));
        }

        // Sıra verilirse sayfa oraya girer (araya sayfa); numarası ayrıca tekil olmalı. Sondaki ekleme eskisi gibidir.
        if (index is { } at)
        {
            if (at < 0 || at > project.Pages.Count)
            {
                return Result.Failure(new Error("page.sira_gecersiz", $"Sayfa sırası 0 ile {project.Pages.Count} arasında olmalı: {at}"));
            }

            if (project.Pages.Any(other => other.Number == page.Number))
            {
                return Result.Failure(new Error("page.numara_tekrar", $"Sayfa numarası zaten kullanılıyor: {page.Number}"));
            }

            project.PageList.Insert(at, page);
            return Result.Success();
        }

        project.PageList.Add(page);
        return Result.Success();
    }

    public void Undo(Project project) => project.PageList.Remove(page);
}

/// <summary>
/// Bir function'ı sayfaya yerleştirir. Yalnızca grafik temsili ekler; elektriksel modeli değiştirmez.
/// </summary>
public sealed class AddPlacement(Placement placement) : ICommand
{
    public string Description => "Sembol yerleştirildi";

    public Result Execute(Project project)
    {
        if (project.IdSet().Contains(placement.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({placement.Id})."));
        }

        if (project.Pages.All(page => page.Id != placement.PageId))
        {
            return Result.Failure(new Error("placement.sayfa_yok", $"Yerleşimin sayfası modelde yok ({placement.PageId})."));
        }

        if (!project.Devices.SelectMany(device => device.Functions).Any(function => function.Id == placement.FunctionId))
        {
            return Result.Failure(new Error(
                "placement.function_yok", $"Yerleşimin function'ı modelde yok ({placement.FunctionId})."));
        }

        if (placement.RotationDeg is not (0 or 90 or 180 or 270))
        {
            return Result.Failure(new Error(
                "placement.donus_gecersiz",
                $"Yerleşim dönüşü 0, 90, 180 ya da 270 olmalı; verilen: {placement.RotationDeg}"));
        }

        if (placement.Role == PlacementRole.Main
            && project.Placements.Any(p => p.FunctionId == placement.FunctionId && p.Role == PlacementRole.Main))
        {
            return Result.Failure(new Error(
                "placement.ana_tekrar", $"Function'ın zaten bir ana görünümü var ({placement.FunctionId})."));
        }

        project.PlacementList.Add(placement);
        return Result.Success();
    }

    public void Undo(Project project) => project.PlacementList.Remove(placement);
}

/// <summary>
/// Sayfanın numarasını değiştirir (ADR-0042). Numara boş olamaz, boşluk içeremez ve projede tekil olmalıdır; "3.1" gibi alt sayfa numaraları
/// geçerlidir. Çizimdeki çapraz referanslar, içindekiler ve bağlantılar numarayı sayfadan okuduğu için kendiliğinden güncellenir.
/// </summary>
public sealed class SetPageNumber(PageId pageId, string number) : ICommand
{
    private Page? _page;
    private string _previous = string.Empty;

    public string Description => $"Sayfa numarası: {number}";

    public Result Execute(Project project)
    {
        var page = project.Pages.FirstOrDefault(candidate => candidate.Id == pageId);
        if (page is null)
        {
            return Result.Failure(new Error("page.yok", $"Sayfa modelde yok ({pageId})."));
        }

        var trimmed = number?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Any(char.IsWhiteSpace) || trimmed.Length > 16)
        {
            return Result.Failure(new Error("page.numara_gecersiz", $"Sayfa numarası boş olamaz, boşluk içeremez ve en çok 16 karakter olmalı: '{number}'"));
        }

        if (project.Pages.Any(other => other.Id != pageId && other.Number == trimmed))
        {
            return Result.Failure(new Error("page.numara_tekrar", $"Sayfa numarası zaten kullanılıyor: {trimmed}"));
        }

        _page = page;
        _previous = page.Number;
        page.Number = trimmed;
        return Result.Success();
    }

    public void Undo(Project project) => _page!.Number = _previous;
}

/// <summary>
/// Sayfayı listede başka bir sıraya taşır (ADR-0048). Yalnızca sıra değişir: sayfa numaraları, çapraz referanslar ve tel numaraları
/// aynı kalır; numaralar istenirse ayrıca baştan verilir. <paramref name="toIndex"/> taşıma sonrası sayfanın sırasıdır (0'dan).
/// </summary>
public sealed class MovePage(PageId pageId, int toIndex) : ICommand
{
    private int _from;

    public string Description => $"Sayfa taşındı: {pageId} → sıra {toIndex}";

    public Result Execute(Project project)
    {
        var from = project.PageList.FindIndex(page => page.Id == pageId);
        if (from < 0)
        {
            return Result.Failure(new Error("page.yok", $"Sayfa modelde yok ({pageId})."));
        }

        if (toIndex < 0 || toIndex >= project.PageList.Count)
        {
            return Result.Failure(new Error("page.sira_gecersiz", $"Sayfa sırası 0 ile {project.PageList.Count - 1} arasında olmalı: {toIndex}"));
        }

        _from = from;
        var page = project.PageList[from];
        project.PageList.RemoveAt(from);
        project.PageList.Insert(toIndex, page);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        var page = project.PageList[toIndex];
        project.PageList.RemoveAt(toIndex);
        project.PageList.Insert(_from, page);
    }
}

/// <summary>Sayfanın başlığını, yapı düğümlerini (= fonksiyon, + yer) ve şablonunu değiştirir.</summary>
public sealed class SetPageDetails(
    PageId pageId,
    string title,
    StructureNodeId? functionNodeId,
    StructureNodeId? locationNodeId,
    SheetTemplateId? templateId) : ICommand
{
    private Page? _page;
    private (string Title, StructureNodeId? Function, StructureNodeId? Location, SheetTemplateId? Template) _previous;
    private string _description = "Sayfa özellikleri değiştirildi";

    public string Description => _description;

    public Result Execute(Project project)
    {
        var page = project.Pages.FirstOrDefault(candidate => candidate.Id == pageId);
        if (page is null)
        {
            return Result.Failure(new Error("page.yok", $"Sayfa modelde yok ({pageId})."));
        }

        var trimmed = title?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Failure(new Error("page.baslik_bos", "Sayfa başlığı boş olamaz."));
        }

        var owner = $"Sayfa {page.Number}";
        var nodeError = project.CheckNode(functionNodeId, Aspect.Function, owner)
            ?? project.CheckNode(locationNodeId, Aspect.Location, owner);
        if (nodeError is not null)
        {
            return Result.Failure(nodeError);
        }

        if (templateId is { } id && project.SheetTemplates.All(template => template.Id != id))
        {
            return Result.Failure(new Error("page.sablon_yok", $"{owner}: sayfa şablonu modelde yok ({id})."));
        }

        _page = page;
        _previous = (page.Title, page.FunctionNodeId, page.LocationNodeId, page.TemplateId);
        _description = $"Sayfa özellikleri değiştirildi: {page.Number} {trimmed}";

        page.Title = trimmed;
        page.FunctionNodeId = functionNodeId;
        page.LocationNodeId = locationNodeId;
        page.TemplateId = templateId;
        return Result.Success();
    }

    public void Undo(Project project)
    {
        _page!.Title = _previous.Title;
        _page.FunctionNodeId = _previous.Function;
        _page.LocationNodeId = _previous.Location;
        _page.TemplateId = _previous.Template;
    }
}
