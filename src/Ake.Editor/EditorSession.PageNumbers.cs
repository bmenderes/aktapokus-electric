using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Sayfa özellikleri penceresinin verisi (Faz 6d-2): numara, başlık, yapı düğümleri, şablon seçenekleri.</summary>
public sealed record PageEditorData(
    int PageIndex,
    PageId PageId,
    PageKind Kind,
    string KindLabel,
    string Number,
    string Title,
    StructureNodeId? FunctionNode,
    StructureNodeId? LocationNode,
    SheetTemplateId? TemplateId,
    IReadOnlyList<NodeChoice> FunctionNodes,
    IReadOnlyList<NodeChoice> LocationNodes,
    IReadOnlyList<(SheetTemplateId? Id, string Label, SheetTemplate? Template)> Templates);

/// <summary>Sayfa özellikleri penceresinden dönen yeni değerler.</summary>
public sealed record PageEdits(
    string Number,
    string Title,
    StructureNodeId? FunctionNode,
    StructureNodeId? LocationNode,
    SheetTemplateId? TemplateId);

/// <summary>Sayfa numaraları: araya sayfa ekleme ve baştan numaralama, oturum tarafı (ADR-0042).</summary>
public sealed partial class EditorSession
{
    /// <summary>Tam sayfa adı (<c>=Fonksiyon+Yer/Numara</c>); boş yapı düğümleri atlanır.</summary>
    public static string FullPageName(string? functionPath, string? locationPath, string? number)
    {
        var fn = string.IsNullOrWhiteSpace(functionPath) || functionPath == "—"
            ? string.Empty
            : "=" + functionPath.Trim().TrimStart('=');
        var loc = string.IsNullOrWhiteSpace(locationPath) || locationPath == "—"
            ? string.Empty
            : "+" + locationPath.Trim().TrimStart('+');
        var num = string.IsNullOrWhiteSpace(number) ? "?" : number.Trim();
        return $"{fn}{loc}/{num}";
    }

    internal static int IndexOfNode(IReadOnlyList<NodeChoice> choices, StructureNodeId? id)
    {
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Şablondan sayfanın ölçek, ızgara ve çerçeve özeti.</summary>
    public static string TemplateSummary(SheetTemplate? template) => template is null
        ? "—"
        : string.Format(
            CultureInfo.InvariantCulture,
            "Ölçek: 1:1  ·  Izgara: {0:0.00} mm  ·  Çerçeve: {1:0} × {2:0} mm ({3} kolon × {4} satır)",
            SnapMm,
            template.WidthMm,
            template.HeightMm,
            template.Columns,
            template.Rows);

    /// <summary>
    /// Verilen sıradaki sayfanın arkasına yeni, boş bir sayfa ekler. Numarası alt sayfadır (3 → 3.1, 3.1 → 3.2); sonraki sayfaların numaraları
    /// ve onlardaki tel numaraları değişmez. Yeni sayfa kendisinden önceki sayfanın şablonunu ve yapı düğümlerini alır. Yeni sayfanın sırası döner;
    /// tek adımda geri alınır.
    /// </summary>
    public Result<int> InsertPageAfter(int pageIndex) =>
        Project is { Pages.Count: 0 }
            ? Result.Failure<int>(new Error("editor.sayfa_yok", "Araya eklenecek sayfa yok; önce bir sayfa ekleyin."))
            : AddPages(PageKind.Schematic, "Sayfa", Math.Clamp(pageIndex, 0, Math.Max(0, (Project?.Pages.Count ?? 1) - 1)));

    /// <summary>
    /// Listenin sonuna yeni, boş bir sayfa ekler. Numarası bir sonraki tam sayıdır; şablonu ve yapı düğümleri son sayfadan alınır (proje boşsa
    /// proje varsayılanı). Yeni sayfanın sırası döner; tek adımda geri alınır.
    /// </summary>
    public Result<int> AppendPage() => AddPages(PageKind.Schematic, "Sayfa", afterIndex: null);

    /// <summary>
    /// Yeni sayfa penceresinin işi (ADR-0046, Faz 6d-2): verilen türde, başlıkta, yapı düğümlerinde, numarada ve şablonda sayfa ekler.
    /// <paramref name="afterIndex"/> boşsa listenin sonuna, doluysa o sayfanın arkasına alt numarayla (3.1) girer. Tablo türlerinde
    /// bugünkü satır sayısına yetecek kadar sayfa eklenir (devam sayfaları alt numara alır). İlk sayfanın sırası döner; tek adımda geri alınır.
    /// </summary>
    public Result<int> AddPages(
        PageKind kind,
        string title,
        int? afterIndex,
        SheetTemplateId? templateId = null,
        StructureNodeId? functionNodeId = null,
        StructureNodeId? locationNodeId = null,
        string? customNumber = null,
        bool explicitStructure = false)
    {
        if (Project is null)
        {
            return Result.Failure<int>(new Error("editor.proje_yok", "Proje açık değil."));
        }

        var project = Project;
        var basis = afterIndex is { } at && project.Pages.Count > 0
            ? project.Pages[Math.Clamp(at, 0, project.Pages.Count - 1)]
            : project.Pages.Count == 0 ? null : project.Pages[^1];
        // Şablon: seçilen (hazır şablonsa ve projede yoksa önce projeye eklenir), yoksa önceki sayfanın şablonu.
        var standard = templateId is { } wanted && project.SheetTemplates.All(candidate => candidate.Id != wanted) ? StandardSheetTemplates.Find(wanted) : null;
        if (templateId is not null && standard is null && project.SheetTemplates.All(candidate => candidate.Id != templateId))
        {
            return Result.Failure<int>(new Error("editor.sablon_yok", "Seçilen sayfa şablonu projede yok ve hazır şablon değil."));
        }

        var template = templateId is { } chosenId
            ? standard ?? project.SheetTemplates.First(candidate => candidate.Id == chosenId)
            : basis is null ? project.SheetTemplates.FirstOrDefault() : project.TemplateFor(basis);
        if ((SheetRenderer.IsTable(kind) || kind == PageKind.PlcDiagram) && template is null)
        {
            return Result.Failure<int>(new Error("editor.sablon_yok", "Tablo sayfası için projede bir sayfa şablonu olmalı."));
        }

        var first = !string.IsNullOrWhiteSpace(customNumber)
            ? customNumber.Trim()
            : afterIndex is null ? PageNumbering.NextNumber(project) : PageNumbering.NextSubNumber(project, afterIndex.Value);
        if (first.Length == 0 || first.Any(char.IsWhiteSpace) || first.Length > 16)
        {
            return Result.Failure<int>(new Error("page.numara_gecersiz", $"Sayfa numarası boş olamaz, boşluk içeremez ve en çok 16 karakter olmalı: '{customNumber}'"));
        }

        if (project.Pages.Any(other => other.Number == first))
        {
            return Result.Failure<int>(new Error("page.numara_tekrar", $"Sayfa numarası zaten kullanılıyor: {first}"));
        }

        var parts = template is null ? 1 : SheetRenderer.PartCount(project, kind, template);
        var insertAt = afterIndex is null
            ? (!string.IsNullOrWhiteSpace(customNumber)
                ? PageNumbering.NaturalIndexFor(project, null, first)
                : project.Pages.Count)
            : Math.Clamp(afterIndex.Value, 0, project.Pages.Count - 1) + 1;

        // Devam sayfalarının numaraları: ilk numaranın ana numarası altında, boş olan alt numaralar.
        var root = first.Contains('.', StringComparison.Ordinal) ? first[..first.IndexOf('.', StringComparison.Ordinal)] : first;
        var used = project.Pages.Select(page => page.Number).ToHashSet(StringComparer.Ordinal);
        used.Add(first);
        var commands = new List<ICommand>();
        if (standard is not null)
        {
            commands.Add(new PutSheetTemplate(standard));
        }

        var fnNode = explicitStructure ? functionNodeId : (functionNodeId ?? basis?.FunctionNodeId);
        var locNode = explicitStructure ? locationNodeId : (locationNodeId ?? basis?.LocationNodeId);
        var pageTemplate = templateId ?? basis?.TemplateId;
        var cleanedTitle = string.IsNullOrWhiteSpace(title) ? KindName(kind) : title.Trim();
        for (var part = 0; part < parts; part++)
        {
            var number = first;
            if (part > 0)
            {
                var sub = 1;
                while (used.Contains($"{root}.{sub}"))
                {
                    sub++;
                }

                number = $"{root}.{sub}";
                used.Add(number);
            }

            var page = new Page(PageId.New(), number, cleanedTitle, fnNode, locNode, pageTemplate, kind, part);
            commands.Add(new AddPage(page, insertAt + part));
        }

        var executed = Execute(new Transaction($"Sayfa eklendi: {cleanedTitle}", commands));
        return executed.IsFailure ? Result.Failure<int>(executed.Error!) : Result.Success(insertAt);
    }

    /// <summary>Verilen sıradaki sayfanın düzenlenebilir bilgileri (sayfa özellikleri penceresi).</summary>
    public PageEditorData? PageDataOf(int pageIndex)
    {
        if (PageAt(pageIndex) is not { IsSuccess: true } result || Project is not { } project)
        {
            return null;
        }

        var page = result.Value;
        var templates = new List<(SheetTemplateId? Id, string Label, SheetTemplate? Template)>();
        if (page.TemplateId is null)
        {
            var fallback = project.SheetTemplates.FirstOrDefault();
            templates.Add((null, fallback is null ? "Proje varsayılanı" : $"Proje varsayılanı ({fallback.Name})", fallback));
        }

        templates.AddRange(project.SheetTemplates.Select(template => ((SheetTemplateId?)template.Id, template.Name, (SheetTemplate?)template)));
        templates.AddRange(StandardSheetTemplates.All
            .Where(standard => project.SheetTemplates.All(template => template.Id != standard.Id))
            .Select(standard => ((SheetTemplateId?)standard.Id, $"{standard.Name} (projeye eklenir)", (SheetTemplate?)standard)));

        return new PageEditorData(
            pageIndex,
            page.Id,
            page.Kind,
            KindName(page.Kind),
            page.Number,
            page.Title,
            page.FunctionNodeId,
            page.LocationNodeId,
            page.TemplateId,
            StructureChoices(project, Aspect.Function),
            StructureChoices(project, Aspect.Location),
            templates);
    }

    /// <summary>Sayfa özelliklerini tek geri alma adımında uygular.</summary>
    public Result ApplyPageEdits(int pageIndex, PageEdits edits)
    {
        ArgumentNullException.ThrowIfNull(edits);
        var found = PageAt(pageIndex);
        if (found.IsFailure)
        {
            return Result.Failure(found.Error!);
        }

        var project = Project!;
        var page = found.Value;
        var commands = new List<ICommand>();

        var number = edits.Number?.Trim() ?? string.Empty;
        if (number != page.Number)
        {
            commands.Add(new SetPageNumber(page.Id, number));
            var targetIndex = PageNumbering.NaturalIndexFor(project, page.Id, number);
            if (targetIndex != pageIndex)
            {
                commands.Add(new MovePage(page.Id, targetIndex));
            }
        }

        if (edits.TemplateId is { } chosenId && project.SheetTemplates.All(candidate => candidate.Id != chosenId))
        {
            var standard = StandardSheetTemplates.Find(chosenId);
            if (standard is null)
            {
                return Result.Failure(new Error("editor.sablon_yok", "Seçilen sayfa şablonu projede yok ve hazır şablon değil."));
            }

            commands.Add(new PutSheetTemplate(standard));
        }

        var targetTemplate = edits.TemplateId ?? page.TemplateId;
        var title = edits.Title?.Trim() ?? string.Empty;
        if (title != page.Title
            || edits.FunctionNode != page.FunctionNodeId
            || edits.LocationNode != page.LocationNodeId
            || targetTemplate != page.TemplateId)
        {
            commands.Add(new SetPageDetails(page.Id, title, edits.FunctionNode, edits.LocationNode, targetTemplate));
        }

        return ExecuteIfAny(new Transaction($"Sayfa özellikleri değiştirildi: {number}", commands));
    }

    /// <summary>
    /// Sayfayı listede başka bir sıraya taşır (ADR-0048); numaralar değişmez. Aynı sıraya taşımak bir şey yapmaz. Geri alınır.
    /// </summary>
    public Result MovePageTo(int pageIndex, int toIndex)
    {
        if (Project is null)
        {
            return NoProject();
        }

        if (pageIndex < 0 || pageIndex >= Project.Pages.Count)
        {
            return Result.Failure(new Error("editor.sayfa_yok", $"Sayfa yok: {pageIndex}"));
        }

        return pageIndex == toIndex ? Result.Success() : Execute(new MovePage(Project.Pages[pageIndex].Id, toIndex));
    }

    /// <summary>Verilen kimlikteki sayfanın projedeki sırasını (0'dan) döndürür; yoksa -1.</summary>
    public int IndexOfPage(PageId pageId)
    {
        if (Project is null)
        {
            return -1;
        }

        for (var i = 0; i < Project.Pages.Count; i++)
        {
            if (Project.Pages[i].Id == pageId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Verilen sıradaki sayfanın numarasını değiştirir ve sayfa numarası değiştiyse sayfayı doğal sayısal sırasına taşır.</summary>
    public Result SetPageNumberAt(int pageIndex, string number)
    {
        if (Project is null)
        {
            return NoProject();
        }

        if (pageIndex < 0 || pageIndex >= Project.Pages.Count)
        {
            return Result.Failure(new Error("editor.sayfa_yok", $"Sayfa yok: {pageIndex}"));
        }

        var page = Project.Pages[pageIndex];
        var trimmed = number?.Trim() ?? string.Empty;
        if (trimmed == page.Number)
        {
            return Result.Success();
        }

        var commands = new List<ICommand> { new SetPageNumber(page.Id, number ?? string.Empty) };
        var targetIndex = PageNumbering.NaturalIndexFor(Project, page.Id, trimmed);
        if (targetIndex != pageIndex)
        {
            commands.Add(new MovePage(page.Id, targetIndex));
        }

        return Execute(new Transaction($"Sayfa numarası: {trimmed}", commands));
    }

    /// <summary>Sayfaları listedeki sıraya göre 1, 2, 3… diye yeniden numaralar (alt sayfa numaraları da gider); yalnızca kullanıcı isterse.</summary>
    public Result RenumberPages() =>
        Project is null ? NoProject() : ExecuteIfAny(PageNumbering.Renumber(Project));
}
