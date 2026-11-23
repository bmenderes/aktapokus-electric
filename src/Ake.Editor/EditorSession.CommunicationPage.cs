using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Otomatik üretilen şema sayfaları (haberleşme şeması, tek hat şeması), oturum tarafı (ADR-0037, ADR-0039).</summary>
public sealed partial class EditorSession
{
    /// <summary>
    /// Projeye haberleşme şeması sayfası ekler (projede zaten varsa onun sırasını döndürür). Şablon yoksa varsayılan şablon da eklenir;
    /// sayfa şablonun çerçevesi ve anteti ile gelir, içeriği projeden otomatik üretilir. Haberleşme bağlantısı yoksa eklenmez.
    /// Sayfa sırası (0 tabanlı) döner. Tek adımda geri alınır.
    /// </summary>
    public Result<int> AddCommunicationPage() => AddGeneratedPage(
        PageKind.CommunicationDiagram,
        "Haberleşme şeması",
        CommunicationDiagram.HasCommunication,
        new Error(
            "haberlesme.yok", "Haberleşme bağlantısı yok: uçlara 'Haberleşme' türü verin ya da bağlantılara haberleşme türünde bir bağlantı türü atayın."));

    /// <summary>Projeye tek hat şeması sayfası ekler (varsa onu döndürür); güç zinciri yoksa eklenmez. Davranışı haberleşme sayfasıyla aynıdır.</summary>
    public Result<int> AddSingleLinePage() => AddGeneratedPage(
        PageKind.SingleLineDiagram,
        "Tek hat şeması",
        SingleLineDiagram.HasChain,
        new Error(
            "tekhat.zincir_yok", "Güç zinciri bulunamadı: beslemeye (klemense) faz potansiyeli (L1…) işareti koyun ve koruma, kontaktör ile yükü teller bağlayın."));

    private Result<int> AddGeneratedPage(PageKind kind, string title, Func<Project, bool> hasContent, Error whenEmpty)
    {
        if (Project is null)
        {
            return Result.Failure<int>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        var existing = Project.Pages.ToList().FindIndex(page => page.Kind == kind);
        if (existing >= 0)
        {
            return Result.Success(existing);
        }

        if (!hasContent(Project))
        {
            return Result.Failure<int>(whenEmpty);
        }

        var commands = new List<ICommand>();
        var template = Project.SheetTemplates.FirstOrDefault();
        if (template is null)
        {
            var created = DefaultTemplate();
            if (created.IsFailure)
            {
                return Result.Failure<int>(created.Error!);
            }

            template = created.Value;
            commands.Add(new PutSheetTemplate(template));
        }

        var used = Project.Pages.Select(page => page.Number).ToHashSet(StringComparer.Ordinal);
        var next = Project.Pages.Select(page => int.TryParse(page.Number, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        while (used.Contains(next.ToString(CultureInfo.InvariantCulture)))
        {
            next++;
        }

        commands.Add(new AddPage(new Page(PageId.New(), next.ToString(CultureInfo.InvariantCulture), title, templateId: template.Id, kind: kind)));
        var executed = Execute(new Transaction($"{title} sayfası eklendi", commands));
        return executed.IsFailure ? Result.Failure<int>(executed.Error!) : Result.Success(Project.Pages.Count - 1);
    }
}
