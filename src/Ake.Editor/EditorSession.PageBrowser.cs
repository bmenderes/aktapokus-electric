using Ake.Library;

namespace Ake.Editor;

/// <summary>Sayfa gezgininde süzgecin baktığı alan (ADR-0051).</summary>
public enum PageFilterField
{
    All,
    Number,
    Title,
    Kind,
    Function,
    Location,
    Template,
}

/// <summary>Sayfa gezgini (ADR-0051): alan bazlı süzgeç ve düz liste görünümü; ağaç görünümü <see cref="PageTreeRows"/> ile aynıdır.</summary>
public sealed partial class EditorSession
{
    /// <summary>Süzgeç alanları ve pencerede görünen adları.</summary>
    public static IReadOnlyList<(PageFilterField Field, string Label)> PageFilterFields { get; } =
    [
        (PageFilterField.All, "Tümü"),
        (PageFilterField.Number, "Numara"),
        (PageFilterField.Title, "Başlık"),
        (PageFilterField.Kind, "Tür"),
        (PageFilterField.Function, "Fonksiyon"),
        (PageFilterField.Location, "Yer"),
        (PageFilterField.Template, "Şablon"),
    ];

    /// <summary>
    /// Süzgece uyan sayfaların sıraları. <paramref name="text"/> seçilen alanda (Türkçe harflere ve büyük-küçük harfe duyarsız) aranır;
    /// <see cref="PageFilterField.All"/> bütün alanlara bakar. Metin boşsa hepsi döner.
    /// </summary>
    public IReadOnlyList<int> PagesMatching(PageFilterField field, string? text)
    {
        if (Project is not { } project)
        {
            return [];
        }

        var all = Enumerable.Range(0, project.Pages.Count);
        if (string.IsNullOrWhiteSpace(text))
        {
            return [.. all];
        }

        var wanted = ComponentLibrary.FoldText(text.Trim());
        bool Contains(string? value) => value is not null && ComponentLibrary.FoldText(value).Contains(wanted, StringComparison.Ordinal);

        return [.. all.Where(index =>
        {
            var page = project.Pages[index];
            return field switch
            {
                PageFilterField.Number => Contains(page.Number),
                PageFilterField.Title => Contains(page.Title),
                PageFilterField.Kind => Contains(KindName(page.Kind)),
                PageFilterField.Function => Contains(project.StructurePath(page.FunctionNodeId) is { } function ? $"={function}" : null),
                PageFilterField.Location => Contains(project.StructurePath(page.LocationNodeId) is { } location ? $"+{location}" : null),
                PageFilterField.Template => Contains(project.TemplateFor(page)?.Name),
                _ => Contains(page.Number) || Contains(page.Title) || Contains(KindName(page.Kind))
                    || Contains(project.StructurePath(page.FunctionNodeId) is { } fn ? $"={fn}" : null)
                    || Contains(project.StructurePath(page.LocationNodeId) is { } loc ? $"+{loc}" : null)
                    || Contains(project.TemplateFor(page)?.Name),
            };
        })];
    }

    /// <summary>
    /// Ağaç görünümü satırları, alan bazlı süzgeçle: <see cref="PageFilterField.All"/> eski davranıştır (sayfa ve grup adı); diğer alanlarda yalnızca
    /// uyan sayfalar ve onlara giden gruplar kalır (hepsi açık).
    /// </summary>
    public IReadOnlyList<PageTreeRow> PageTreeRows(string? filter, IReadOnlySet<string> collapsed, PageFilterField field)
    {
        if (field == PageFilterField.All || string.IsNullOrWhiteSpace(filter))
        {
            return PageTreeRows(filter, collapsed);
        }

        return FlattenTree(PageTree(null, PagesMatching(field, filter).ToHashSet()));
    }

    /// <summary>
    /// Liste görünümü: yapıya göre gruplamadan, sayfalar projedeki sırayla düz bir listedir; her satır numara, başlık ve türü gösterir.
    /// Süzgeç <paramref name="field"/> alanında uygulanır.
    /// </summary>
    public IReadOnlyList<PageTreeRow> PageListRows(string? filter, PageFilterField field)
    {
        if (Project is not { } project)
        {
            return [];
        }

        return [.. PagesMatching(field, filter).Select(index =>
        {
            var page = project.Pages[index];
            var kind = page.Kind == Ake.Core.Model.PageKind.Schematic ? string.Empty : $"  ·  {KindName(page.Kind)}";
            return new PageTreeRow($"{page.Number}  {page.Title}{kind}", 0, index, $"#{index}", IsGroup: false, Expanded: false);
        })];
    }

    // Ağaç düğümlerini düz satırlara açar (hepsi açık): alan bazlı süzgeçte katlama yoktur.
    private static List<PageTreeRow> FlattenTree(IReadOnlyList<PageTreeNode> nodes)
    {
        var rows = new List<PageTreeRow>();

        void Add(IEnumerable<PageTreeNode> items, int depth, string parent)
        {
            foreach (var node in items)
            {
                if (node.PageIndex is { } page)
                {
                    rows.Add(new PageTreeRow(node.Label, depth, page, $"{parent}/#{page}", IsGroup: false, Expanded: false));
                    continue;
                }

                var key = $"{parent}/{node.Label}";
                rows.Add(new PageTreeRow(node.Label, depth, null, key, IsGroup: true, Expanded: true));
                Add(node.Children, depth + 1, key);
            }
        }

        Add(nodes, 0, string.Empty);
        return rows;
    }
}
