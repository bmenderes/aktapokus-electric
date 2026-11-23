using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Sembol seçicide bir satır: kütüphanedeki sembolün özeti.</summary>
public sealed record SymbolRow(SymbolId Id, string Label, int Pins, string? Category = null, string RawName = "")
{
    /// <summary>Paletteki okunur ad (üretici dosya adı temizlenmiş).</summary>
    public string Name => SymbolPalette.CleanName(RawName);

    /// <summary>Ağaç satırında görünen metin.</summary>
    public string Display => $"{Name}  ·  {Pins} uç";

    // Ağaçtaki stil bağlaması için: semboller hiçbir zaman açılan düğüm değildir.
    public bool Expanded => false;

    public override string ToString() => Label;
}

/// <summary>Sembol seçici için oturum tarafı: paylaşılan sembol kütüphanesi (ADR-0017).</summary>
public sealed partial class EditorSession : IDisposable
{
    private ComponentLibrary? _library;

    /// <summary>Açık kütüphane dosyası; açık değilse <c>null</c>.</summary>
    public string? LibraryPath { get; private set; }

    public int LibrarySymbolCount => _library?.SymbolCount ?? 0;

    /// <summary>Kütüphaneyi açar. Başarısız olursa önceki kütüphane açık kalır.</summary>
    public Result OpenLibrary(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var cleaned = path.Trim().Trim('"');
        if (!File.Exists(cleaned))
        {
            return Result.Failure(new Error("kutuphane.yok", $"Kütüphane dosyası bulunamadı: {cleaned}"));
        }

        var opened = ComponentLibrary.Open(cleaned);
        if (opened.IsFailure)
        {
            return Result.Failure(opened.Error!);
        }

        _library?.Dispose();
        _library = opened.Value;
        LibraryPath = Path.GetFullPath(cleaned);
        return Result.Success();
    }

    /// <summary>
    /// Kütüphanede arar. <paramref name="pins"/> verilirse yalnızca o sayıda ucu olan semboller döner;
    /// seçili function'a uyanları göstermek için kullanılır.
    /// </summary>
    public IReadOnlyList<SymbolRow> SearchLibrary(string text, int? pins = null, int limit = 20000)
    {
        ArgumentNullException.ThrowIfNull(text);

        return _library is null
            ? []
            : [.. _library.SearchSymbols(text.Trim(), pins, limit)
                .Select(symbol => new SymbolRow(
                    symbol.Id,
                    $"{symbol.Name}  ·  {symbol.Pins} uç  ·  {symbol.Category}",
                    symbol.Pins,
                    symbol.Category,
                    symbol.Name))];
    }

    /// <summary>Kütüphanedeki sembolün tam tanımı (blok editöründe düzenlemek için); yoksa <c>null</c>.</summary>
    public SymbolDefinition? LibrarySymbol(SymbolId id)
    {
        var found = _library?.FindSymbol(id.ToString());
        return found is { IsSuccess: true } ? found.Value : null;
    }

    /// <summary>
    /// Düzenlenmiş sembolü kütüphaneye yazar. <paramref name="overwrite"/> doğruysa aynı kimlikli kullanıcı sembolü güncellenir;
    /// değilse (standart set, üretici kütüphanesi) sembolün <b>kopyası</b> yeni kimlikle "Kendi sembollerim" kategorisine konur: asıl
    /// sembol bir sonraki kurulumda yeniden yazılacağı için üzerine yazmak değişikliği kaybettirirdi. Lisans korunur; yoksa sembol
    /// kullanıcı çizimi sayılır. Dönen kimlik, kaydedilen sembolün kimliğidir.
    /// </summary>
    public Result<SymbolId> SaveSymbolToLibrary(SymbolDefinition symbol, bool overwrite)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        if (_library is null)
        {
            return Result.Failure<SymbolId>(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        var existingCategory = overwrite ? _library.GetSymbolCategory(symbol.Id) : null;
        var stored = overwrite
            ? symbol
            : symbol with
            {
                Id = SymbolId.New(),
                Source = string.IsNullOrWhiteSpace(symbol.Source) ? "Düzenlendi" : $"Düzenlendi: {symbol.Source}",
            };
        if (string.IsNullOrWhiteSpace(stored.License))
        {
            stored = stored with { License = "Kullanıcı çizimi" };
        }

        var category = string.IsNullOrWhiteSpace(existingCategory) ? SymbolPalette.UserCategory : existingCategory;
        var imported = _library.ImportSymbols([new LibrarySymbol(stored, category)]);
        return imported.IsFailure ? Result.Failure<SymbolId>(imported.Error!) : Result.Success(stored.Id);
    }

    /// <summary>Kütüphanedeki bir sembolün görünen adını günceller.</summary>
    public Result RenameLibrarySymbol(SymbolId id, string newName) =>
        _library is null
            ? Result.Failure(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."))
            : _library.RenameSymbol(id, newName);

    /// <summary>
    /// Verilen dosya (.elmt, .dxf, .dwg, .sembol.json) ya da klasördeki geçerli sembolleri
    /// açık kütüphaneye aktarır ve kütüphaneyi tazeler.
    /// </summary>
    public Result<SymbolCollectionImportResult> ImportSymbolsFromPath(string path, string? license = null)
    {
        ArgumentNullException.ThrowIfNull(path);

        if (LibraryPath is null)
        {
            return Result.Failure<SymbolCollectionImportResult>(
                new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        var imported = StandardSymbols.ImportFromPath(LibraryPath, path, license);
        if (imported.IsFailure)
        {
            return imported;
        }

        var reopened = OpenLibrary(LibraryPath);
        return reopened.IsFailure
            ? Result.Failure<SymbolCollectionImportResult>(reopened.Error!)
            : imported;
    }

    /// <summary>
    /// Çalışma alanı veya uygulama kökünde keşfedilen tüm yerel sembol kütüphanelerini
    /// açık kütüphaneye aktarır.
    /// </summary>
    public Result<SymbolCollectionImportResult> ImportDiscoveredSymbolLibraries(
        string? baseDirectory = null,
        bool includeQetCollection = true)
    {
        if (LibraryPath is null)
        {
            return Result.Failure<SymbolCollectionImportResult>(
                new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        var imported = StandardSymbols.ImportDiscoveredCollections(LibraryPath, baseDirectory, includeQetCollection);
        if (imported.IsFailure)
        {
            return imported;
        }

        var reopened = OpenLibrary(LibraryPath);
        return reopened.IsFailure
            ? Result.Failure<SymbolCollectionImportResult>(reopened.Error!)
            : imported;
    }

    /// <summary>Kütüphanede kullanıcı tarafından oluşturulan ya da özel atanmış kategoriler.</summary>
    public IReadOnlyList<string> SymbolCategories() => _library?.SymbolCategories() ?? [];

    /// <summary>Açık kütüphanede yeni bir sembol kategori klasörü oluşturur.</summary>
    public Result CreateSymbolCategory(string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return _library is null
            ? Result.Failure(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."))
            : _library.CreateSymbolCategory(category);
    }

    /// <summary>Kütüphanedeki bir sembolü başka kategori klasörüne taşır.</summary>
    public Result MoveSymbolToCategory(SymbolId id, string category)
    {
        ArgumentNullException.ThrowIfNull(category);
        return _library is null
            ? Result.Failure(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."))
            : _library.MoveSymbolCategory(id, category);
    }

    /// <summary>Kütüphanedeki sembolün önizleme sahnesi (blok editörünün gösterdiği biçimde; istenirse 90° katlarında döndürülür).</summary>
    public Scene? PreviewOf(SymbolId id, int rotationDeg = 0)
    {
        if (_library is null)
        {
            return null;
        }

        var found = _library.FindSymbol(id.ToString());
        return found.IsSuccess ? Preview(found.Value, rotationDeg) : null;
    }

    /// <summary>
    /// Sembolü kendi kutusuna, kenar payıyla sığan bir sahneye çizer; uçlar mavi uzantı ve numarayla
    /// gösterilir. Blok editörünün sabit çalışma alanından farklı olarak büyük semboller kesilmez.
    /// </summary>
    public static Scene Preview(SymbolDefinition symbol, int rotationDeg = 0)
    {
        ArgumentNullException.ThrowIfNull(symbol);

        var rot = ((rotationDeg % 360) + 360) % 360;
        if (rot != 0)
        {
            symbol = RotateSymbol(symbol, rot);
        }

        const double margin = 5;
        var drawn = SymbolLibrary.FromDefinition(symbol);
        var (dx, dy) = (margin - drawn.MinX, margin - drawn.MinY);
        var items = new List<SceneItem>();
        foreach (var item in drawn.Items)
        {
            items.Add(item switch
            {
                LineItem line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
                RectItem rect => rect with { X = rect.X + dx, Y = rect.Y + dy },
                CircleItem circle => circle with { X = circle.X + dx, Y = circle.Y + dy },
                TextItem text => text with { X = text.X + dx, Y = text.Y + dy, FontFamily = "Arial" },
                _ => item,
            });
        }

        for (var i = 0; i < symbol.Pins.Count; i++)
        {
            var pin = symbol.Pins[i];
            var (px, py) = (pin.X + dx, pin.Y + dy);
            items.Add(new LineItem(px, py, px + (pin.DirectionX * 2.5), py + (pin.DirectionY * 2.5), 0.3, "#C77800"));
            items.Add(new TextItem(px + 0.9, py - 0.9, (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), 1.6, "Arial"));
        }

        return new Scene(drawn.MaxX - drawn.MinX + (2 * margin), drawn.Height - drawn.MinY + (2 * margin), items);
    }

    private static SymbolDefinition RotateSymbol(SymbolDefinition symbol, int rotationDeg)
    {
        static (double X, double Y) RotatePoint(double x, double y, int rot) => rot switch
        {
            90 => (-y, x),
            180 => (-x, -y),
            270 => (y, -x),
            _ => (x, y),
        };

        SymbolShape RotateShape(SymbolShape shape)
        {
            switch (shape)
            {
                case SymbolLine line:
                    var (x1, y1) = RotatePoint(line.X1, line.Y1, rotationDeg);
                    var (x2, y2) = RotatePoint(line.X2, line.Y2, rotationDeg);
                    return line with { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2 };
                case SymbolCircle circle:
                    var (cx, cy) = RotatePoint(circle.X, circle.Y, rotationDeg);
                    return circle with { X = cx, Y = cy };
                case SymbolText text:
                    var (tx, ty) = RotatePoint(text.X, text.Y, rotationDeg);
                    return text with { X = tx, Y = ty, RotationDeg = text.RotationDeg + rotationDeg };
                case SymbolRect rect:
                    var corners = new[]
                    {
                        RotatePoint(rect.X, rect.Y, rotationDeg),
                        RotatePoint(rect.X + rect.Width, rect.Y, rotationDeg),
                        RotatePoint(rect.X, rect.Y + rect.Height, rotationDeg),
                        RotatePoint(rect.X + rect.Width, rect.Y + rect.Height, rotationDeg),
                    };
                    var minX = corners.Min(c => c.X);
                    var minY = corners.Min(c => c.Y);
                    var maxX = corners.Max(c => c.X);
                    var maxY = corners.Max(c => c.Y);
                    return new SymbolRect(minX, minY, maxX - minX, maxY - minY, rect.StrokeMm);
                default:
                    return shape;
            }
        }

        return symbol with
        {
            Shapes = [.. symbol.Shapes.Select(RotateShape)],
            Pins =
            [
                .. symbol.Pins.Select(pin =>
                {
                    var (px, py) = RotatePoint(pin.X, pin.Y, rotationDeg);
                    var (dx, dy) = RotatePoint(pin.DirectionX, pin.DirectionY, rotationDeg);
                    return pin with
                    {
                        X = px,
                        Y = py,
                        DirectionX = (int)Math.Round(dx),
                        DirectionY = (int)Math.Round(dy),
                    };
                }),
            ],
        };
    }

    /// <summary>
    /// Kütüphanedeki sembolü seçili yerleşimin function'ına atar: sembol projeye kopyalanır (projede
    /// yoksa) ve atanır; ikisi tek adımdır, birlikte geri alınır.
    /// </summary>
    public Result AssignFromLibrary(PlacementId placementId, SymbolId symbolId)
    {
        if (_library is null)
        {
            return Result.Failure(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        if (FunctionOf(placementId) is not { } function)
        {
            return Result.Failure(new Error("placement.yok", $"Yerleşim modelde yok ({placementId})."));
        }

        var found = _library.FindSymbol(symbolId.ToString());
        if (found.IsFailure)
        {
            return Result.Failure(found.Error!);
        }

        return Execute(new Transaction(
            $"Kütüphaneden sembol atandı: {found.Value.Name}",
            [new PutSymbol(found.Value), new AssignSymbol(function.Id, found.Value.Id)]));
    }

    public void Dispose()
    {
        _library?.Dispose();
        _library = null;
    }
}
