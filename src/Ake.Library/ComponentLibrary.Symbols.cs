using System.Globalization;
using Ake.Core;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Library;

/// <summary>Kütüphanedeki bir sembolün özeti (arama sonucu).</summary>
public sealed record SymbolSummary(SymbolId Id, string Name, string? Category, int Pins, string? License);

/// <summary>Kütüphaneye konacak sembol ve kategorisi (ör. içe aktarıldığı klasör).</summary>
public sealed record LibrarySymbol(SymbolDefinition Symbol, string? Category);

/// <summary>
/// Paylaşılan sembol kütüphanesi. Semboller projeden ayrı, kütüphane dosyasında tutulur; bir projeye
/// yalnızca kullanılan semboller kopyalanır. Böylece binlerce sembollük bir koleksiyon proje dosyasını
/// büyütmez ve bir kez içe aktarılıp bütün projelerde kullanılır.
/// </summary>
public sealed partial class ComponentLibrary
{
    private const string SymbolSchema = """
        CREATE TABLE symbols (
            id          TEXT PRIMARY KEY,
            name        TEXT NOT NULL,
            category    TEXT,
            pins        INTEGER NOT NULL,
            source      TEXT,
            license     TEXT,
            definition  TEXT NOT NULL,
            imported_at TEXT NOT NULL
        );
        CREATE INDEX ix_symbols_name ON symbols(name);
        """;

    private const string SymbolCategorySchema = """
        CREATE TABLE IF NOT EXISTS symbol_categories (
            name TEXT PRIMARY KEY
        );
        ALTER TABLE symbols ADD COLUMN category_locked INTEGER NOT NULL DEFAULT 0;
        """;

    public int SymbolCount =>
        Convert.ToInt32(Scalar(_connection, "SELECT COUNT(*) FROM symbols"), CultureInfo.InvariantCulture);

    /// <summary>
    /// Sembolleri tek veritabanı işleminde ekler ya da (aynı kimlikle) günceller. Geçersiz bir sembol
    /// varsa hiçbiri yazılmaz. Lisansı olmayan sembol kabul edilmez (ADR-0001).
    /// </summary>
    public Result<ImportSummary> ImportSymbols(IEnumerable<LibrarySymbol> symbols)
    {
        ArgumentNullException.ThrowIfNull(symbols);

        using var transaction = _connection.BeginTransaction();
        var (added, updated) = (0, 0);
        foreach (var (symbol, category) in symbols)
        {
            if (symbol.Check() is { } invalid)
            {
                return Result.Failure<ImportSummary>(invalid);
            }

            if (string.IsNullOrWhiteSpace(symbol.License))
            {
                return Result.Failure<ImportSummary>(new Error(
                    "kutuphane.kaynak_yok", $"'{symbol.Name}' sembolünün lisansı yok; kütüphaneye alınmadı."));
            }

            var exists = Scalar(_connection, "SELECT 1 FROM symbols WHERE id = $id", ("$id", symbol.Id.ToString())) is not null;
            Execute(
                _connection,
                """
                INSERT INTO symbols (id, name, category, pins, source, license, definition, imported_at)
                VALUES ($id, $name, $category, $pins, $source, $license, $definition, $at)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    category = CASE WHEN symbols.category_locked = 1 THEN symbols.category ELSE excluded.category END,
                    pins = excluded.pins,
                    source = excluded.source, license = excluded.license,
                    definition = excluded.definition, imported_at = excluded.imported_at
                """,
                ("$id", symbol.Id.ToString()),
                ("$name", symbol.Name),
                ("$category", NormalizeCategory(category)),
                ("$pins", symbol.Pins.Count),
                ("$source", symbol.Source),
                ("$license", symbol.License),
                ("$definition", SymbolJson.Serialize(symbol)),
                ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));

            if (exists)
            {
                updated++;
            }
            else
            {
                added++;
            }
        }

        transaction.Commit();
        return Result.Success(new ImportSummary(added, updated));
    }

    /// <summary>Ad ve kategoride metin arar; isteğe bağlı olarak uç sayısına göre süzer.</summary>
    public IReadOnlyList<SymbolSummary> SearchSymbols(string text, int? pins = null, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(text);

        var pattern = $"%{Fold(text)!.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        using var command = Command(
            _connection,
            """
            SELECT id, name, category, pins, license FROM symbols
            WHERE (fold(name) LIKE $p ESCAPE '\' OR fold(category) LIKE $p ESCAPE '\')
              AND ($pins IS NULL OR pins = $pins)
            ORDER BY category, name
            LIMIT $limit
            """,
            ("$p", pattern),
            ("$pins", pins),
            ("$limit", limit));
        using var reader = command.ExecuteReader();

        var results = new List<SymbolSummary>();
        while (reader.Read())
        {
            results.Add(new SymbolSummary(
                new SymbolId(Guid.Parse(reader.GetString(0))),
                reader.GetString(1),
                Text(reader, 2),
                reader.GetInt32(3),
                Text(reader, 4)));
        }

        return results;
    }

    /// <inheritdoc />
    public Result<SymbolDefinition> FindSymbol(string reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        var trimmed = reference.Trim();
        if (Guid.TryParse(trimmed, out var id))
        {
            return Load("id = $r", id.ToString(), trimmed);
        }

        // "kategori/ad": son eğik çizgiden önceki kısım kategoridir.
        var slash = trimmed.LastIndexOf('/');
        if (slash > 0)
        {
            var category = NormalizeCategory(trimmed[..slash]);
            var name = trimmed[(slash + 1)..];
            var byPath = Matches("category = $c AND name = $r", name, category);
            if (byPath.Count == 1)
            {
                return Load("id = $r", byPath[0], trimmed);
            }
        }

        var byName = Matches("name = $r", trimmed, null);
        return byName.Count switch
        {
            1 => Load("id = $r", byName[0], trimmed),
            0 => Result.Failure<SymbolDefinition>(new Error("kutuphane.sembol_yok", $"Kütüphanede sembol yok: {trimmed}")),
            _ => Result.Failure<SymbolDefinition>(new Error(
                "kutuphane.sembol_belirsiz",
                $"'{trimmed}' adında {byName.Count} sembol var; kimliğini ya da 'kategori/ad' yolunu kullanın.")),
        };
    }

    private List<string> Matches(string where, string reference, string? category)
    {
        using var command = Command(
            _connection, $"SELECT id FROM symbols WHERE {where} ORDER BY id", ("$r", reference), ("$c", category));
        using var reader = command.ExecuteReader();
        var ids = new List<string>();
        while (reader.Read())
        {
            ids.Add(reader.GetString(0));
        }

        return ids;
    }

    private Result<SymbolDefinition> Load(string where, string value, string reference)
    {
        var json = Scalar(_connection, $"SELECT definition FROM symbols WHERE {where}", ("$r", value)) as string;
        return json is null
            ? Result.Failure<SymbolDefinition>(new Error("kutuphane.sembol_yok", $"Kütüphanede sembol yok: {reference}"))
            : SymbolJson.Deserialize(json);
    }

    /// <summary>
    /// Arama için metni sadeleştirir: aksanlar atılır, küçük harfe çevrilir, Türkçe noktasız ı i olur. Böylece
    /// "salter" de "şalter" de "Şalter"i bulur. SQLite'ın LIKE'ı yalnızca İngilizce harflerde büyük-küçük
    /// harf ayırmaz; bu işlev kütüphane açılırken <c>fold</c> adıyla kaydedilir.
    /// </summary>
    /// <summary>Aramadaki gibi Türkçe harflere ve aksanlara duyarsız küçük harfli metin ("ŞALTER" → "salter").</summary>
    public static string FoldText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Fold(text)!;
    }

    internal static string? Fold(string? text)
    {
        if (text is null)
        {
            return null;
        }

        var decomposed = text.Normalize(System.Text.NormalizationForm.FormD);
        var builder = new System.Text.StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().ToLowerInvariant().Replace('ı', 'i');
    }

    private static string? NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category) ? null : category.Replace('\\', '/').Trim('/');

    /// <summary>Kullanıcı tarafından oluşturulan veya kullanılan özel sembol kategorilerini döndürür.</summary>
    public IReadOnlyList<string> SymbolCategories()
    {
        using var command = Command(_connection, "SELECT name FROM symbol_categories ORDER BY name");
        using var reader = command.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
        {
            list.Add(reader.GetString(0));
        }

        return list;
    }

    /// <summary>Kütüphanede yeni bir sembol kategori klasörü (boş olsa bile görünür) oluşturur.</summary>
    public Result CreateSymbolCategory(string category)
    {
        var valid = ValidateCategory(category);
        if (valid.IsFailure)
        {
            return Result.Failure(valid.Error!);
        }

        Execute(_connection, "INSERT OR IGNORE INTO symbol_categories (name) VALUES ($name)", ("$name", valid.Value));
        return Result.Success();
    }

    /// <summary>Kütüphanedeki bir sembolün kategori yolunu döndürür (yoksa null).</summary>
    public string? GetSymbolCategory(SymbolId id) =>
        Scalar(_connection, "SELECT category FROM symbols WHERE id = $id", ("$id", id.ToString())) as string;

    /// <summary>Kütüphanedeki bir sembolün görünen adını ve tanımındaki adı günceller.</summary>
    public Result RenameSymbol(SymbolId id, string newName)
    {
        var trimmed = newName?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return Result.Failure(new Error("kutuphane.sembol_adi_bos", "Sembol adı boş olamaz."));
        }

        var existing = Load("id = $r", id.ToString(), id.ToString());
        if (existing.IsFailure)
        {
            return Result.Failure(existing.Error!);
        }

        var updated = existing.Value with { Name = trimmed };
        if (updated.Check() is { } invalid)
        {
            return Result.Failure(invalid);
        }

        Execute(
            _connection,
            "UPDATE symbols SET name = $name, definition = $def WHERE id = $id",
            ("$name", trimmed),
            ("$def", SymbolJson.Serialize(updated)),
            ("$id", id.ToString()));
        return Result.Success();
    }

    /// <summary>Kütüphanedeki bir sembolü başka kategori klasörüne taşır ve sonraki standart içe aktarımlarda korur.</summary>
    public Result MoveSymbolCategory(SymbolId id, string category)
    {
        var valid = ValidateCategory(category);
        if (valid.IsFailure)
        {
            return Result.Failure(valid.Error!);
        }

        var idText = id.ToString();
        if (Scalar(_connection, "SELECT 1 FROM symbols WHERE id = $id", ("$id", idText)) is null)
        {
            return Result.Failure(new Error("kutuphane.sembol_yok", $"Kütüphanede sembol yok ({id})."));
        }

        using var transaction = _connection.BeginTransaction();
        Execute(
            _connection,
            "UPDATE symbols SET category = $category, category_locked = 1 WHERE id = $id",
            ("$category", valid.Value),
            ("$id", idText));
        Execute(_connection, "INSERT OR IGNORE INTO symbol_categories (name) VALUES ($name)", ("$name", valid.Value));
        transaction.Commit();
        return Result.Success();
    }

    private static Result<string> ValidateCategory(string? category)
    {
        var normalized = NormalizeCategory(category);
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 120)
        {
            return Result.Failure<string>(new Error("kutuphane.kategori_gecersiz", "Kategori adı boş olamaz ve en fazla 120 karakter olabilir."));
        }

        var segments = normalized.Split('/', StringSplitOptions.TrimEntries);
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".."))
        {
            return Result.Failure<string>(new Error("kutuphane.kategori_gecersiz", $"Geçersiz kategori yolu: '{category}'."));
        }

        return Result.Success(string.Join('/', segments));
    }
}
