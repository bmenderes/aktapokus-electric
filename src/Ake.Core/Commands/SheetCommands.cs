using Ake.Core.Model;

namespace Ake.Core.Commands;

/// <summary>
/// Sayfa şablonunu projeye koyar: aynı kimlikli şablon varsa yerine geçer, yoksa sona eklenir.
/// Projenin ilk şablonu, kendi şablonu olmayan sayfaların varsayılanıdır.
/// </summary>
public sealed class PutSheetTemplate(SheetTemplate template) : ICommand
{
    private SheetTemplate? _previous;
    private int _index;

    public string Description => $"Sayfa şablonu kaydedildi: {template.Name}";

    public Result Execute(Project project)
    {
        if (template.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var templates = project.SheetTemplateList;
        _index = templates.FindIndex(existing => existing.Id == template.Id);
        if (_index >= 0)
        {
            _previous = templates[_index];
            templates[_index] = template;
            return Result.Success();
        }

        if (project.IdSet().Contains(template.Id.Value))
        {
            return Result.Failure(new Error("id.tekrar", $"Kimlik zaten kullanılıyor ({template.Id})."));
        }

        _previous = null;
        templates.Add(template);
        return Result.Success();
    }

    public void Undo(Project project)
    {
        if (_previous is null)
        {
            project.SheetTemplateList.Remove(template);
        }
        else
        {
            project.SheetTemplateList[_index] = _previous;
        }
    }
}

/// <summary>
/// Revizyon geçmişine kayıt ekler (ADR-0050). İşaret ve açıklama boş olamaz, işaret tekil olmalıdır. Antetteki revizyon (<c>revision</c> özelliği)
/// ayrıca <see cref="SetProjectProperty"/> ile verilir; editör ikisini tek adımda yapar.
/// </summary>
public sealed class AddRevision(Revision revision) : ICommand
{
    public string Description => $"Revizyon eklendi: {revision.Mark}";

    public Result Execute(Project project)
    {
        if (string.IsNullOrWhiteSpace(revision.Mark) || revision.Mark.Any(char.IsWhiteSpace) || revision.Mark.Length > 8)
        {
            return Result.Failure(new Error("revizyon.isaret_gecersiz", $"Revizyon işareti boş olamaz, boşluk içeremez ve en çok 8 karakter olmalı: '{revision.Mark}'"));
        }

        if (string.IsNullOrWhiteSpace(revision.Description))
        {
            return Result.Failure(new Error("revizyon.aciklama_bos", "Revizyonun açıklaması boş olamaz."));
        }

        if (project.Revisions.Any(other => other.Mark == revision.Mark))
        {
            return Result.Failure(new Error("revizyon.isaret_tekrar", $"Revizyon işareti zaten kullanılıyor: {revision.Mark}"));
        }

        project.RevisionList.Add(revision);
        return Result.Success();
    }

    public void Undo(Project project) => project.RevisionList.Remove(revision);
}

/// <summary>Projenin adını değiştirir (kapakta, antette ve pencere başlığında görünen ad); boş ad reddedilir.</summary>
public sealed class SetProjectName(string name) : ICommand
{
    private string _previous = string.Empty;

    public string Description => $"Proje adı: {name}";

    public Result Execute(Project project)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Failure(new Error("proje.ad_bos", "Projenin adı boş olamaz."));
        }

        _previous = project.Name;
        project.Name = trimmed;
        return Result.Success();
    }

    public void Undo(Project project) => project.Name = _previous;
}

/// <summary>Proje alanını (çizim no, revizyon…) ayarlar; değer <c>null</c> ise alanı siler.</summary>
public sealed class SetProjectProperty(string key, string? value) : ICommand
{
    private string? _previous;

    public string Description => value is null ? $"Proje alanı silindi: {key}" : $"Proje alanı ayarlandı: {key}";

    public Result Execute(Project project)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(c => c is '{' or '}' || char.IsWhiteSpace(c)))
        {
            return Result.Failure(new Error(
                "proje.alan_gecersiz", $"Proje alanı adı boş olamaz; boşluk ve süslü parantez içeremez: '{key}'"));
        }

        _previous = project.PropertyMap.GetValueOrDefault(key);
        if (value is null)
        {
            project.PropertyMap.Remove(key);
        }
        else
        {
            project.PropertyMap[key] = value;
        }

        return Result.Success();
    }

    public void Undo(Project project)
    {
        if (_previous is null)
        {
            project.PropertyMap.Remove(key);
        }
        else
        {
            project.PropertyMap[key] = _previous;
        }
    }
}

/// <summary>
/// Projenin tag şemasını değiştirir. Tag'lere dokunmaz; şemaya uyan tag'ler için
/// <see cref="Numbering.Renumbering.ChangeTagScheme"/> bu command'ı yeniden etiketlemeyle birlikte üretir.
/// </summary>
public sealed class SetTagScheme(TagScheme scheme) : ICommand
{
    private TagScheme _previous;

    public string Description => $"Tag şeması: {scheme}";

    public Result Execute(Project project)
    {
        if (!Enum.IsDefined(scheme))
        {
            return Result.Failure(new Error("tag.sema_gecersiz", $"Bilinmeyen tag şeması: {scheme}"));
        }

        _previous = project.TagScheme;
        project.TagScheme = scheme;
        return Result.Success();
    }

    public void Undo(Project project) => project.TagScheme = _previous;
}

/// <summary>Tel numaralandırma ayarlarını değiştirir (ADR-0041). Mevcut tel numaralarına dokunmaz.</summary>
public sealed class SetWireNumbering(WireNumberSettings settings) : ICommand
{
    private WireNumberSettings _previous = new();

    public string Description => $"Tel numaralandırma ayarı: kod '{settings.Prefix}', başlangıç {settings.Start}";

    public Result Execute(Project project)
    {
        if (settings.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        _previous = project.WireNumbering;
        project.WireNumbering = settings;
        return Result.Success();
    }

    public void Undo(Project project) => project.WireNumbering = _previous;
}
