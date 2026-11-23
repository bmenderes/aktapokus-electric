using System.Text.Json;
using Ake.Core;

namespace Ake.Reports;

/// <summary>Rapor sütunu. <see cref="Key"/> kararlıdır (şablonlar ona atıf yapar), başlık gösterilir.</summary>
public sealed record ReportColumn(string Key, string Header);

/// <summary>
/// Bir raporun biçimden bağımsız içeriği. Hücre değeri <c>string</c>, <c>int</c>, <c>decimal</c>
/// ya da <c>null</c> olur. CSV ve XLSX yazıcıları aynı tabloyu yazar.
/// </summary>
public sealed class ReportTable(
    string name,
    string title,
    IReadOnlyList<ReportColumn> columns,
    IReadOnlyList<IReadOnlyList<object?>> rows)
{
    /// <summary>Raporun kararlı adı (bom, cables…); sayfa ve dosya adı olarak da kullanılır.</summary>
    public string Name { get; } = name;

    public string Title { get; } = title;

    public IReadOnlyList<ReportColumn> Columns { get; } = columns;

    public IReadOnlyList<IReadOnlyList<object?>> Rows { get; } = rows;

    /// <summary>Şablondaki sütunları, şablondaki sırayla ve başlıklarla seçer.</summary>
    public Result<ReportTable> Apply(ReportTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var indexes = new List<int>();
        var selected = new List<ReportColumn>();
        foreach (var wanted in template.Columns)
        {
            var index = Columns.Select((column, i) => (column, i)).FirstOrDefault(x => x.column.Key == wanted.Key, (null!, -1)).i;
            if (index < 0)
            {
                return Result.Failure<ReportTable>(new Error(
                    "rapor.kolon_yok",
                    $"'{Name}' raporunda '{wanted.Key}' sütunu yok. Sütunlar: {string.Join(", ", Columns.Select(c => c.Key))}"));
            }

            indexes.Add(index);
            selected.Add(new ReportColumn(wanted.Key, wanted.Header ?? Columns[index].Header));
        }

        var rows = Rows.Select(row => (IReadOnlyList<object?>)[.. indexes.Select(i => row[i])]).ToList();
        return Result.Success(new ReportTable(Name, template.Title ?? Title, selected, rows));
    }
}

public sealed record TemplateColumn(string Key, string? Header = null);

/// <summary>
/// Rapor şablonu: hangi sütunlar, hangi sırayla, hangi başlıkla. Dosya biçimi:
/// <c>{ "title": "…", "columns": [ { "key": "symbol", "header": "İsim" }, "terminal" ] }</c>
/// </summary>
public sealed record ReportTemplate(IReadOnlyList<TemplateColumn> Columns, string? Title = null)
{
    public static Result<ReportTemplate> Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("columns", out var columns)
                || columns.ValueKind != JsonValueKind.Array)
            {
                return Invalid("'columns' dizisi yok.");
            }

            var parsed = new List<TemplateColumn>();
            foreach (var column in columns.EnumerateArray())
            {
                if (column.ValueKind == JsonValueKind.String)
                {
                    parsed.Add(new TemplateColumn(column.GetString()!));
                }
                else if (column.ValueKind == JsonValueKind.Object
                    && column.TryGetProperty("key", out var key)
                    && key.ValueKind == JsonValueKind.String)
                {
                    var header = column.TryGetProperty("header", out var h) && h.ValueKind == JsonValueKind.String
                        ? h.GetString()
                        : null;
                    parsed.Add(new TemplateColumn(key.GetString()!, header));
                }
                else
                {
                    return Invalid("her sütun bir metin ya da 'key' alanı olan bir nesne olmalı.");
                }
            }

            if (parsed.Count == 0)
            {
                return Invalid("en az bir sütun gerekli.");
            }

            var title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString()
                : null;
            return Result.Success(new ReportTemplate(parsed, title));
        }
        catch (JsonException ex)
        {
            return Invalid(ex.Message);
        }
    }

    private static Result<ReportTemplate> Invalid(string detail) =>
        Result.Failure<ReportTemplate>(new Error("rapor.sablon_gecersiz", $"Rapor şablonu geçersiz: {detail}"));
}
