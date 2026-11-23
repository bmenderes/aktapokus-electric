using System.Globalization;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Model;
using Microsoft.Data.Sqlite;

namespace Ake.Library;

/// <summary>Ürünün serbest teknik özelliği (güç, bobin gerilimi…). Sayısal ise aramada sayı olarak kullanılır.</summary>
public sealed record ProductProperty(string Key, string Value, string? Unit = null);

/// <summary>
/// Kütüphanedeki ürün kaydı. <see cref="Source"/> ve <see cref="License"/> zorunludur: uygulama
/// üretici verisi dağıtmaz, her kaydın nereden ve hangi koşulla geldiği bilinir (ADR-0001).
/// </summary>
public sealed record LibraryProduct(
    ProductDefinition Definition,
    string Source,
    string License,
    string? Category = null,
    IReadOnlyList<ProductProperty>? Properties = null);

public sealed record ProductSummary(
    string Manufacturer,
    string OrderCode,
    string? TypeDesignation,
    string? Description,
    string? Category);

public sealed record ImportSummary(int Added, int Updated);

/// <summary>SQLite dosyasındaki komponent kütüphanesi.</summary>
public sealed partial class ComponentLibrary : IProductCatalog, ISymbolCatalog, IDisposable
{
    public const int SchemaVersion = 4;

    private const string Schema = """
        CREATE TABLE products (
            id               INTEGER PRIMARY KEY,
            manufacturer     TEXT NOT NULL,
            order_code       TEXT NOT NULL,
            type_designation TEXT,
            description      TEXT,
            category         TEXT,
            mounting_type    TEXT,
            width_mm         REAL,
            height_mm        REAL,
            depth_mm         REAL,
            clearance_mm     REAL,
            heat_loss_w      REAL,
            step_ref         TEXT,
            source           TEXT NOT NULL,
            license          TEXT NOT NULL,
            imported_at      TEXT NOT NULL,
            UNIQUE (manufacturer, order_code)
        );
        CREATE TABLE functions (
            id              INTEGER PRIMARY KEY,
            product_id      INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            position        INTEGER NOT NULL,
            kind            TEXT NOT NULL,
            pass_through    INTEGER NOT NULL,
            description     TEXT,
            rated_current_a TEXT
        );
        CREATE TABLE connection_points (
            function_id INTEGER NOT NULL REFERENCES functions(id) ON DELETE CASCADE,
            position    INTEGER NOT NULL,
            name        TEXT NOT NULL
        );
        CREATE TABLE properties (
            product_id    INTEGER NOT NULL REFERENCES products(id) ON DELETE CASCADE,
            position      INTEGER NOT NULL,
            key           TEXT NOT NULL,
            value         TEXT NOT NULL,
            numeric_value REAL,
            unit          TEXT
        );
        CREATE INDEX ix_products_order_code ON products(order_code);
        """;

    private readonly SqliteConnection _connection;
    private readonly TimeProvider _time;

    private ComponentLibrary(SqliteConnection connection, TimeProvider time)
    {
        _connection = connection;
        _time = time;
    }

    /// <summary>Dosyayı açar; yoksa oluşturur ve şemayı kurar.</summary>
    public static Result<ComponentLibrary> Open(string path, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        SqliteConnection? connection = null;
        try
        {
            if (Path.GetDirectoryName(Path.GetFullPath(path)) is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
            }

            // Pooling kapalı: dosya, kütüphane kapatılınca hemen serbest kalır.
            connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false,
                ForeignKeys = true,
            }.ToString());
            connection.Open();
            connection.CreateFunction<string?, string?>("fold", Fold, isDeterministic: true);

            var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture);
            if (version > SchemaVersion)
            {
                connection.Dispose();
                return Result.Failure<ComponentLibrary>(new Error(
                    "kutuphane.sema_yeni",
                    $"Kütüphane daha yeni bir sürümle oluşturulmuş (şema {version}, desteklenen {SchemaVersion})."));
            }

            if (version < SchemaVersion)
            {
                // Sürüm sürüm ileri taşınır: 0 boş dosya, 1 yalnızca ürünler, 2 semboller.
                using var transaction = connection.BeginTransaction();
                if (version < 1)
                {
                    Execute(connection, Schema);
                }

                if (version < 2)
                {
                    Execute(connection, SymbolSchema);
                }

                if (version < 3)
                {
                    Execute(connection, SymbolCategorySchema);
                }

                if (version < 4)
                {
                    Execute(connection, "ALTER TABLE functions ADD COLUMN symbol_id TEXT;");
                }

                Execute(connection, $"PRAGMA user_version = {SchemaVersion}");
                transaction.Commit();
            }

            return Result.Success(new ComponentLibrary(connection, timeProvider ?? TimeProvider.System));
        }
        catch (Exception ex) when (ex is SqliteException or IOException or UnauthorizedAccessException)
        {
            connection?.Dispose();
            return Result.Failure<ComponentLibrary>(
                new Error("kutuphane.acilamadi", $"Kütüphane '{path}' açılamadı: {ex.Message}"));
        }
    }

    public int Count => Convert.ToInt32(Scalar(_connection, "SELECT COUNT(*) FROM products"), CultureInfo.InvariantCulture);

    public void Dispose() => _connection.Dispose();

    /// <summary>
    /// Ürünleri tek veritabanı işleminde ekler ya da günceller: biri geçersizse hiçbiri yazılmaz.
    /// Aynı üretici ve sipariş koduna sahip kayıt varsa bütünüyle yenisiyle değiştirilir.
    /// </summary>
    public Result<ImportSummary> Import(IEnumerable<LibraryProduct> products)
    {
        ArgumentNullException.ThrowIfNull(products);

        using var transaction = _connection.BeginTransaction();
        var added = 0;
        var updated = 0;

        foreach (var product in products)
        {
            if (Validate(product) is { } error)
            {
                return Result.Failure<ImportSummary>(error);
            }

            var definition = product.Definition;
            var existing = Scalar(
                _connection,
                "SELECT id FROM products WHERE manufacturer = $m AND order_code = $o",
                ("$m", definition.Manufacturer),
                ("$o", definition.OrderCode));
            if (existing is null)
            {
                added++;
            }
            else
            {
                updated++;
                Execute(_connection, "DELETE FROM products WHERE id = $id", ("$id", existing));
            }

            Insert(product);
        }

        transaction.Commit();
        return Result.Success(new ImportSummary(added, updated));
    }

    public Result<ProductDefinition> Find(string orderCode, string? manufacturer = null)
    {
        var product = FindProduct(orderCode, manufacturer);
        return product.IsFailure
            ? Result.Failure<ProductDefinition>(product.Error!)
            : Result.Success(product.Value.Definition);
    }

    public Result<LibraryProduct> FindProduct(string orderCode, string? manufacturer = null)
    {
        ArgumentNullException.ThrowIfNull(orderCode);

        var matches = new List<(long Id, string Manufacturer)>();
        using (var command = Command(
            _connection,
            "SELECT id, manufacturer FROM products WHERE order_code = $o AND ($m IS NULL OR manufacturer = $m) ORDER BY manufacturer",
            ("$o", orderCode),
            ("$m", manufacturer)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                matches.Add((reader.GetInt64(0), reader.GetString(1)));
            }
        }

        return matches.Count switch
        {
            1 => Result.Success(Load(matches[0].Id)),
            0 => Result.Failure<LibraryProduct>(new Error(
                "kutuphane.urun_yok",
                $"Kütüphanede '{orderCode}' sipariş kodlu ürün yok{(manufacturer is null ? string.Empty : $" (üretici: {manufacturer})")}.")),
            _ => Result.Failure<LibraryProduct>(new Error(
                "kutuphane.urun_belirsiz",
                $"'{orderCode}' birden çok üreticide var: {string.Join(", ", matches.Select(m => m.Manufacturer))}. Üreticiyi belirtin.")),
        };
    }

    /// <summary>Üretici, sipariş kodu, tip, açıklama ve kategoride metin arar.</summary>
    public IReadOnlyList<ProductSummary> Search(string text, int limit = 50)
    {
        ArgumentNullException.ThrowIfNull(text);

        var pattern = $"%{Fold(text)!.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
        using var command = Command(
            _connection,
            """
            SELECT manufacturer, order_code, type_designation, description, category
            FROM products
            WHERE fold(manufacturer) LIKE $p ESCAPE '\' OR fold(order_code) LIKE $p ESCAPE '\'
               OR fold(type_designation) LIKE $p ESCAPE '\' OR fold(description) LIKE $p ESCAPE '\'
               OR fold(category) LIKE $p ESCAPE '\'
            ORDER BY manufacturer, order_code
            LIMIT $limit
            """,
            ("$p", pattern),
            ("$limit", limit));
        using var reader = command.ExecuteReader();

        var results = new List<ProductSummary>();
        while (reader.Read())
        {
            results.Add(new ProductSummary(
                reader.GetString(0), reader.GetString(1), Text(reader, 2), Text(reader, 3), Text(reader, 4)));
        }

        return results;
    }

    /// <summary>
    /// Kütüphanedeki bir ürünün üstverisini (üretici, sipariş kodu, tip, açıklama, kategori) ve
    /// fonksiyon şablonlarını (bağlı sembol kimlikleri dâhil) günceller.
    /// </summary>
    public Result UpdateProduct(string originalManufacturer, string originalOrderCode, LibraryProduct updated)
    {
        ArgumentNullException.ThrowIfNull(originalManufacturer);
        ArgumentNullException.ThrowIfNull(originalOrderCode);
        ArgumentNullException.ThrowIfNull(updated);

        if (Validate(updated) is { } invalid)
        {
            return Result.Failure(invalid);
        }

        var existingObj = Scalar(
            _connection,
            "SELECT id FROM products WHERE manufacturer = $m AND order_code = $o",
            ("$m", originalManufacturer),
            ("$o", originalOrderCode));
        if (existingObj is null)
        {
            return Result.Failure(new Error(
                "kutuphane.urun_yok",
                $"Kütüphanede '{originalManufacturer} {originalOrderCode}' ürünü yok."));
        }

        var existingId = Convert.ToInt64(existingObj, CultureInfo.InvariantCulture);
        var def = updated.Definition;
        if (!string.Equals(def.Manufacturer, originalManufacturer, StringComparison.Ordinal)
            || !string.Equals(def.OrderCode, originalOrderCode, StringComparison.Ordinal))
        {
            var collision = Scalar(
                _connection,
                "SELECT id FROM products WHERE manufacturer = $m AND order_code = $o AND id <> $id",
                ("$m", def.Manufacturer),
                ("$o", def.OrderCode),
                ("$id", existingId));
            if (collision is not null)
            {
                return Result.Failure(new Error(
                    "kutuphane.urun_zaten_var",
                    $"Kütüphanede zaten '{def.Manufacturer} {def.OrderCode}' ürünü var."));
            }
        }

        var mounting = def.Mounting;
        using var transaction = _connection.BeginTransaction();
        Execute(
            _connection,
            """
            UPDATE products
            SET manufacturer = $manufacturer,
                order_code = $order_code,
                type_designation = $type,
                description = $description,
                category = $category,
                mounting_type = $mounting_type,
                width_mm = $width,
                height_mm = $height,
                depth_mm = $depth,
                clearance_mm = $clearance,
                heat_loss_w = $heat,
                step_ref = $step,
                source = $source,
                license = $license,
                imported_at = $at
            WHERE id = $id
            """,
            ("$manufacturer", def.Manufacturer),
            ("$order_code", def.OrderCode),
            ("$type", def.TypeDesignation),
            ("$description", def.Description),
            ("$category", updated.Category),
            ("$mounting_type", mounting?.Type.ToString()),
            ("$width", mounting?.WidthMm),
            ("$height", mounting?.HeightMm),
            ("$depth", mounting?.DepthMm),
            ("$clearance", mounting?.ClearanceMm),
            ("$heat", mounting?.HeatLossW),
            ("$step", mounting?.StepRef),
            ("$source", updated.Source),
            ("$license", updated.License),
            ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)),
            ("$id", existingId));

        Execute(_connection, "DELETE FROM functions WHERE product_id = $id", ("$id", existingId));
        InsertFunctions(existingId, def.Functions);
        transaction.Commit();
        return Result.Success();
    }

    private static Error? Validate(LibraryProduct product)
    {
        var definition = product.Definition;
        if (string.IsNullOrWhiteSpace(definition.Manufacturer) || string.IsNullOrWhiteSpace(definition.OrderCode))
        {
            return new Error("kutuphane.urun_gecersiz", "Ürünün üreticisi ve sipariş kodu boş olamaz.");
        }

        if (string.IsNullOrWhiteSpace(product.Source) || string.IsNullOrWhiteSpace(product.License))
        {
            return new Error(
                "kutuphane.kaynak_yok",
                $"{definition.Manufacturer} {definition.OrderCode}: kaynak (source) ve lisans (license) zorunlu.");
        }

        return definition.Functions.Any(function => function.ConnectionPoints.Any(string.IsNullOrWhiteSpace))
            ? new Error(
                "kutuphane.urun_gecersiz",
                $"{definition.Manufacturer} {definition.OrderCode}: bağlantı noktası adı boş olamaz.")
            : null;
    }

    private void Insert(LibraryProduct product)
    {
        var definition = product.Definition;
        var mounting = definition.Mounting;

        Execute(
            _connection,
            """
            INSERT INTO products (manufacturer, order_code, type_designation, description, category, mounting_type,
                width_mm, height_mm, depth_mm, clearance_mm, heat_loss_w, step_ref, source, license, imported_at)
            VALUES ($manufacturer, $order_code, $type, $description, $category, $mounting_type,
                $width, $height, $depth, $clearance, $heat, $step, $source, $license, $at)
            """,
            ("$manufacturer", definition.Manufacturer),
            ("$order_code", definition.OrderCode),
            ("$type", definition.TypeDesignation),
            ("$description", definition.Description),
            ("$category", product.Category),
            ("$mounting_type", mounting?.Type.ToString()),
            ("$width", mounting?.WidthMm),
            ("$height", mounting?.HeightMm),
            ("$depth", mounting?.DepthMm),
            ("$clearance", mounting?.ClearanceMm),
            ("$heat", mounting?.HeatLossW),
            ("$step", mounting?.StepRef),
            ("$source", product.Source),
            ("$license", product.License),
            ("$at", _time.GetUtcNow().ToString("O", CultureInfo.InvariantCulture)));
        var productId = Convert.ToInt64(Scalar(_connection, "SELECT last_insert_rowid()")!, CultureInfo.InvariantCulture);

        InsertFunctions(productId, definition.Functions);

        var properties = product.Properties ?? [];
        for (var i = 0; i < properties.Count; i++)
        {
            var numeric = decimal.TryParse(
                properties[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? (double?)number
                : null;
            Execute(
                _connection,
                """
                INSERT INTO properties (product_id, position, key, value, numeric_value, unit)
                VALUES ($product, $position, $key, $value, $numeric, $unit)
                """,
                ("$product", productId),
                ("$position", i),
                ("$key", properties[i].Key),
                ("$value", properties[i].Value),
                ("$numeric", numeric),
                ("$unit", properties[i].Unit));
        }
    }

    private void InsertFunctions(long productId, IReadOnlyList<FunctionTemplate> functions)
    {
        for (var i = 0; i < functions.Count; i++)
        {
            var function = functions[i];
            Execute(
                _connection,
                """
                INSERT INTO functions (product_id, position, kind, pass_through, description, rated_current_a, symbol_id)
                VALUES ($product, $position, $kind, $pass, $description, $rated, $symbol_id)
                """,
                ("$product", productId),
                ("$position", i),
                ("$kind", function.Kind.ToString()),
                ("$pass", function.PassThrough ? 1 : 0),
                ("$description", function.Description),
                ("$rated", function.RatedCurrentA?.ToString(CultureInfo.InvariantCulture)),
                ("$symbol_id", function.SymbolId?.ToString()));
            var functionId = Scalar(_connection, "SELECT last_insert_rowid()")!;

            for (var p = 0; p < function.ConnectionPoints.Count; p++)
            {
                Execute(
                    _connection,
                    "INSERT INTO connection_points (function_id, position, name) VALUES ($function, $position, $name)",
                    ("$function", functionId),
                    ("$position", p),
                    ("$name", function.ConnectionPoints[p]));
            }
        }
    }

    private LibraryProduct Load(long productId)
    {
        var functions = new List<(long Id, FunctionKind Kind, bool PassThrough, string? Description, decimal? Rated, SymbolId? SymbolId)>();
        using (var command = Command(
            _connection,
            "SELECT id, kind, pass_through, description, rated_current_a, symbol_id FROM functions WHERE product_id = $id ORDER BY position",
            ("$id", productId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                SymbolId? symbolId = Text(reader, 5) is { } symText && Guid.TryParse(symText, out var symGuid)
                    ? new SymbolId(symGuid)
                    : null;
                functions.Add((
                    reader.GetInt64(0),
                    Enum.Parse<FunctionKind>(reader.GetString(1)),
                    reader.GetInt64(2) != 0,
                    Text(reader, 3),
                    Text(reader, 4) is { } rated ? decimal.Parse(rated, CultureInfo.InvariantCulture) : null,
                    symbolId));
            }
        }

        var templates = new List<FunctionTemplate>();
        foreach (var function in functions)
        {
            var points = new List<string>();
            using var command = Command(
                _connection,
                "SELECT name FROM connection_points WHERE function_id = $id ORDER BY position",
                ("$id", function.Id));
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                points.Add(reader.GetString(0));
            }

            templates.Add(new FunctionTemplate(
                function.Kind, points, function.PassThrough, function.Description, function.Rated, function.SymbolId));
        }

        var properties = new List<ProductProperty>();
        using (var command = Command(
            _connection,
            "SELECT key, value, unit FROM properties WHERE product_id = $id ORDER BY position",
            ("$id", productId)))
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                properties.Add(new ProductProperty(reader.GetString(0), reader.GetString(1), Text(reader, 2)));
            }
        }

        using var productCommand = Command(
            _connection,
            """
            SELECT manufacturer, order_code, type_designation, description, category, mounting_type,
                   width_mm, height_mm, depth_mm, clearance_mm, heat_loss_w, step_ref, source, license
            FROM products WHERE id = $id
            """,
            ("$id", productId));
        using var product = productCommand.ExecuteReader();
        product.Read();

        var mounting = Text(product, 5) is { } mountingType
            ? new Mounting(
                Enum.Parse<MountingType>(mountingType),
                product.GetDouble(6),
                product.GetDouble(7),
                product.GetDouble(8),
                product.GetDouble(9),
                product.GetDouble(10),
                Text(product, 11))
            : null;

        return new LibraryProduct(
            new ProductDefinition(
                product.GetString(0), product.GetString(1), templates, Text(product, 2), Text(product, 3), mounting),
            product.GetString(12),
            product.GetString(13),
            Text(product, 4),
            properties);
    }

    private static string? Text(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static SqliteCommand Command(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    private static void Execute(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        var value = command.ExecuteScalar();
        return value is DBNull ? null : value;
    }
}
