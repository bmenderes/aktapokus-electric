using System.Globalization;
using Ake.Core;
using Ake.Core.Commands;
using Ake.Core.Macros;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;

namespace Ake.Editor;

/// <summary>Sürükle-bırakla yerleştirme, oturum tarafı (ADR-0026).</summary>
public sealed partial class EditorSession
{
    // Sembol adından tag harfi (IEC 81346-2'ye yakın, yaygın kullanım). Sıra önemlidir: ilk eşleşen kazanır.
    private static readonly (string Letter, string[] Words)[] LetterHints =
    [
        ("P", ["lamba", "lamp", "voyant", "signal", "isik", "gosterge", "indicator"]),
        ("M", ["motor", "moteur"]),
        ("K", ["kontaktor", "contactor", "contacteur", "role", "relay", "relais", "bobin", "coil"]),
        ("F", ["sigorta", "fuse", "fusible", "salter", "breaker", "disjonct", "termik", "thermal", "protect"]),
        ("S", ["buton", "button", "bouton", "anahtar", "switch", "interrupt", "push", "acil"]),
        ("X", ["klemens", "terminal", "borne", "soket", "connector"]),
        ("T", ["trafo", "transform", "besleme", "power supply", "alimentation", "kaynak"]),
    ];

    /// <summary>
    /// Cihazın henüz yerleştirilmemiş ilk function'ını sayfada verilen noktaya (ızgaraya yakalanmış)
    /// yerleştirir. Navigator'dan sürükleyip bırakmak budur.
    /// </summary>
    public Result<PlacementId> PlaceNextFunction(DeviceId deviceId, int pageIndex, double x, double y)
    {
        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<PlacementId>(page.Error!);
        }

        if (Project!.FindDevice(deviceId) is not { } device)
        {
            return Result.Failure<PlacementId>(new Error("device.yok", $"Device modelde yok ({deviceId})."));
        }

        var placed = Project.Placements.Where(p => p.Role == PlacementRole.Main).Select(p => p.FunctionId).ToHashSet();
        if (device.Functions.FirstOrDefault(f => !placed.Contains(f.Id)) is not { } function)
        {
            return Result.Failure<PlacementId>(new Error(
                "yerlesim.hepsi_yerlesik", $"-{device.ProductTag} cihazının bütün function'ları zaten bir sayfada."));
        }

        var (sx, sy) = SnapPoint(x, y, pageIndex);
        var placement = new Placement(PlacementId.New(), page.Value.Id, function.Id, PlacementRole.Main, sx, sy);
        var added = Execute(new AddPlacement(placement));
        return added.IsFailure ? Result.Failure<PlacementId>(added.Error!) : Result.Success(placement.Id);
    }

    /// <summary>
    /// Kütüphanedeki sembolden yeni cihaz oluşturur ve sayfaya yerleştirir: uçları sembolün uçları kadar
    /// ("1", "2"…), tag harfi sembolün adından, numarası o harfteki bir sonraki. Sembol projeye kopyalanır.
    /// Hepsi tek adımda geri alınır.
    /// </summary>
    public Result<PlacementId> PlaceLibrarySymbol(SymbolId symbolId, int pageIndex, double x, double y, int rotationDeg = 0)
    {
        if (_library is null)
        {
            return Result.Failure<PlacementId>(new Error("kutuphane.acik_degil", "Sembol kütüphanesi açık değil."));
        }

        var page = PageAt(pageIndex);
        if (page.IsFailure)
        {
            return Result.Failure<PlacementId>(page.Error!);
        }

        var found = _library.FindSymbol(symbolId.ToString());
        if (found.IsFailure)
        {
            return Result.Failure<PlacementId>(found.Error!);
        }

        var symbol = found.Value;
        var function = new Function(
            FunctionId.New(),
            FunctionKind.Generic,
            [.. symbol.Pins.Select((pin, i) => new ConnectionPoint(
                ConnectionPointId.New(), pin.Name ?? (i + 1).ToString(CultureInfo.InvariantCulture), pin.Attributes))],
            symbolId: symbol.Id);
        var device = new Device(
            DeviceId.New(), (symbol.TagLetter ?? TagLetterFor(symbol.Name)) + "1", [function], description: symbol.DeviceName ?? symbol.Name);
        var single = new Macro(symbol.Name, [device], [], [new MacroPlacement(function.Id, 0, 0, rotationDeg)], [symbol]);

        var before = Project!.Placements.Select(p => p.Id).ToHashSet();
        var (sx, sy) = SnapPoint(x, y, pageIndex);
        var insert = MacroService.Insert(Project, single, page.Value.Id, sx, sy);
        if (insert.IsFailure)
        {
            return Result.Failure<PlacementId>(insert.Error!);
        }

        var executed = Execute(insert.Value);
        return executed.IsFailure
            ? Result.Failure<PlacementId>(executed.Error!)
            : Result.Success(Project.Placements.First(p => !before.Contains(p.Id)).Id);
    }

    /// <summary>Makroyu sayfada bırakılan noktaya yerleştirir (dosya sürükle-bırak).</summary>
    public Result InsertMacroAt(string path, int pageIndex, double x, double y)
    {
        var macro = Ake.Serialization.MacroJson.Load(path);
        if (macro.IsFailure)
        {
            return Result.Failure(macro.Error!);
        }

        var origin = DrawingAreaFor(pageIndex) ?? new Box(0, 0, 0, 0);
        return InsertMacro(macro.Value, pageIndex, x - origin.X, y - origin.Y);
    }

    /// <summary>Sembol adından tag harfi; tanınmayan sembol "A".</summary>
    public static string TagLetterFor(string symbolName)
    {
        ArgumentNullException.ThrowIfNull(symbolName);

        var folded = ComponentLibrary.FoldText(symbolName);
        return LetterHints.FirstOrDefault(hint => hint.Words.Any(word => folded.Contains(word, StringComparison.Ordinal))).Letter ?? "A";
    }

    // Bırakılan nokta sayfa koordinatındadır; yerleşim koordinatı çizim alanına göredir (alan sayfada kenar boşluğu ve cetvelden sonra
    // başlar). Çevrilmezse sembol bırakılan yerin alan başlangıcı kadar sağına/altına düşerdü.
    private (double X, double Y) SnapPoint(double x, double y, int pageIndex)
    {
        var origin = DrawingAreaFor(pageIndex) ?? new Box(0, 0, 0, 0);
        return Snap(x - origin.X, y - origin.Y);
    }
}
