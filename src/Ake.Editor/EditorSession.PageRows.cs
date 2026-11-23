namespace Ake.Editor;

/// <summary>
/// Sayfa ağacının düzleştirilmiş bir satırı. Ağaç sanallaştırılan bir listede gösterilir: yüzlerce sayfada her düğüm için
/// denetim kurmak açılışı saniyelerce geciktirir. <c>Key</c> grubun yoludur ("=M1/+P1"); katlama durumu onunla tutulur.
/// </summary>
public sealed record PageTreeRow(string Label, int Depth, int? PageIndex, string Key, bool IsGroup, bool Expanded)
{
    /// <summary>Listede görünen metin: derinliğe göre girinti, grup satırında açık/kapalı işareti.</summary>
    public override string ToString() =>
        new string(' ', Depth * 3) + (IsGroup ? (Expanded ? "▾ " : "▸ ") : "  ") + Label;
}

public sealed partial class EditorSession
{
    /// <summary>
    /// <see cref="PageTree"/>'nin düzleştirilmiş hâli: açık grupların altındaki satırlar sırayla gelir. <paramref name="collapsed"/>
    /// katlanmış grupların anahtarlarıdır; süzgeç etkinken (boş değil) her şey açık gösterilir, çünkü eşleşenler zaten az ve
    /// gizlenmeleri aramanın amacına aykırıdır.
    /// </summary>
    public IReadOnlyList<PageTreeRow> PageTreeRows(string? filter, IReadOnlySet<string> collapsed)
    {
        ArgumentNullException.ThrowIfNull(collapsed);

        var rows = new List<PageTreeRow>();
        var filtering = !string.IsNullOrWhiteSpace(filter);

        void Add(IEnumerable<PageTreeNode> nodes, int depth, string parent)
        {
            foreach (var node in nodes)
            {
                if (node.PageIndex is { } page)
                {
                    rows.Add(new PageTreeRow(node.Label, depth, page, $"{parent}/#{page}", IsGroup: false, Expanded: false));
                    continue;
                }

                var key = $"{parent}/{node.Label}";
                var expanded = filtering || !collapsed.Contains(key);
                rows.Add(new PageTreeRow(node.Label, depth, null, key, IsGroup: true, expanded));
                if (expanded)
                {
                    Add(node.Children, depth + 1, key);
                }
            }
        }

        Add(PageTree(filter), 0, string.Empty);
        return rows;
    }
}
