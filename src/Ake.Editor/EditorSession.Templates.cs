using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Sayfa şablonu ve antet düzenleme oturumu işlemleri.</summary>
public sealed partial class EditorSession
{
    /// <summary>Projede kayıtlı sayfa şablonları.</summary>
    public IReadOnlyList<SheetTemplate> ProjectTemplates => Project?.SheetTemplates ?? [];

    /// <summary>Açık proje dosyasının klasörü (göreli logo yollarını çözmek için).</summary>
    public string? ProjectDirectory => ProjectPath is { Length: > 0 } path ? Path.GetDirectoryName(path) : null;

    /// <summary>Verilen sayfa için etkin şablonu döner (sayfa şablonu yoksa proje varsayılanı, o da yoksa A3 yatay).</summary>
    public SheetTemplate ActiveTemplateFor(int pageIndex)
    {
        if (Project is { } project)
        {
            if (pageIndex >= 0 && pageIndex < project.Pages.Count && project.TemplateFor(project.Pages[pageIndex]) is { } found)
            {
                return found;
            }

            if (project.SheetTemplates.FirstOrDefault() is { } first)
            {
                return first;
            }
        }

        return StandardSheetTemplates.A3Landscape;
    }

    /// <summary>Bir sayfa şablonu taslağı için canlı önizleme sahnesi üretir.</summary>
    public Result<Scene> PreviewTemplate(SheetTemplate template, int activePageIndex, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(template);

        Page? page = null;
        if (Project is { } project && project.Pages.Count > 0)
        {
            page = activePageIndex >= 0 && activePageIndex < project.Pages.Count
                ? project.Pages[activePageIndex]
                : project.Pages[0];
        }

        return SheetRenderer.PreviewTemplate(template, Project, page, today);
    }

    /// <summary>
    /// Sayfa şablonunu (antetiyle birlikte) projeye kaydeder ve isteğe göre etkin sayfaya veya tüm sayfalara uygular.
    /// Tek adımda geri alınır.
    /// </summary>
    public Result SaveSheetTemplate(
        SheetTemplate template,
        bool applyToActivePage = false,
        int activePageIndex = 0,
        bool applyToAllPages = false)
    {
        ArgumentNullException.ThrowIfNull(template);

        if (Project is null)
        {
            return NoProject();
        }

        if (template.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var commands = new List<ICommand> { new PutSheetTemplate(template) };

        if (applyToAllPages)
        {
            foreach (var page in Project.Pages)
            {
                if (page.TemplateId != template.Id)
                {
                    commands.Add(new SetPageDetails(page.Id, page.Title, page.FunctionNodeId, page.LocationNodeId, template.Id));
                }
            }
        }
        else if (applyToActivePage && PageAt(activePageIndex) is { IsSuccess: true } found && found.Value.TemplateId != template.Id)
        {
            var page = found.Value;
            commands.Add(new SetPageDetails(page.Id, page.Title, page.FunctionNodeId, page.LocationNodeId, template.Id));
        }

        return Execute(new Transaction($"Antet / şablon kaydedildi: {template.Name}", commands));
    }
}
