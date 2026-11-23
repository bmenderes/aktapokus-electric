using FluentIcons.Common;

namespace Ake.Editor;

/// <summary>
/// Blok editörünün komutları ve şeridi (ADR-0023'ün ana pencere şeridiyle aynı yapı). Şerit, Dosya menüsü, hızlı erişim ve
/// kısayollar buradan kurulur. Blok editörünün kısayolları sabittir (kısayol profili ana pencere içindir).
/// </summary>
public static class SymbolEditorCommands
{
    public const string ToolSelect = "blok.arac_sec";
    public const string ToolLine = "blok.arac_cizgi";
    public const string ToolRect = "blok.arac_dikdortgen";
    public const string ToolCircle = "blok.arac_daire";
    public const string ToolText = "blok.arac_yazi";
    public const string ToolPin = "blok.arac_uc";
    public const string Delete = "blok.sil";
    public const string Deselect = "blok.secimi_birak";
    public const string Undo = "blok.geri";
    public const string Redo = "blok.yinele";
    public const string TurnPin = "blok.uc_cevir";
    public const string FirstPin = "blok.ilk_uc";
    public const string DirectionUp = "blok.yon_yukari";
    public const string DirectionDown = "blok.yon_asagi";
    public const string DirectionLeft = "blok.yon_sola";
    public const string DirectionRight = "blok.yon_saga";
    public const string SaveToProject = "blok.proje_kaydet";
    public const string SaveToFile = "blok.dosya_kaydet";
    public const string SaveToLibrary = "blok.kutuphane_kaydet";
    public const string AddPinsOnSurface = "blok.yuzeye_uc";
    public const string Close = "blok.kapat";

    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        new(ToolSelect, "Seç", Symbol.CursorHover, "S", Toggle: true),
        new(ToolLine, "Çizgi", Symbol.Line, "L", Toggle: true),
        new(ToolRect, "Dikdörtgen", Symbol.RectangleLandscape, "D", Toggle: true),
        new(ToolCircle, "Daire", Symbol.Circle, "C", Toggle: true),
        new(ToolText, "Yazı", Symbol.TextT, "T", Toggle: true),
        new(ToolPin, "Uç", Symbol.PlugConnected, "U", Toggle: true),
        new(Delete, "Sil", Symbol.Delete, "Delete"),
        new(Deselect, "Seçimi bırak", Symbol.Dismiss, "Escape"),
        new(Undo, "Geri al", Symbol.ArrowUndo, "Ctrl+Z"),
        new(Redo, "Yinele", Symbol.ArrowRedo, "Ctrl+Y"),
        new(TurnPin, "Ucu çevir", Symbol.ArrowRotateClockwise),
        new(FirstPin, "İlk uç yap", Symbol.Target),
        new(DirectionUp, "Yukarı", Symbol.ArrowUp, Toggle: true),
        new(DirectionDown, "Aşağı", Symbol.ArrowDown, Toggle: true),
        new(DirectionLeft, "Sola", Symbol.ArrowLeft, Toggle: true),
        new(DirectionRight, "Sağa", Symbol.ArrowRight, Toggle: true),
        new(SaveToProject, "Projeye kaydet", Symbol.Save, "Ctrl+S"),
        new(SaveToFile, "Dosyaya kaydet…", Symbol.SaveEdit),
        new(SaveToLibrary, "Kütüphaneye kaydet", Symbol.Library),
        new(AddPinsOnSurface, "Yüzeye uç ekle", Symbol.PlugConnected),
        new(Close, "Kapat", Symbol.ArrowExit),
    ];

    /// <summary>Şerit tanımı: tek sekme (Çizim); araç, düzen ve uç grupları.</summary>
    public static RibbonDefinition Ribbon { get; } = new(
        All,
        [
            new RibbonTab("Çizim",
            [
                new("Araç", [new(ToolSelect, Large: true), new(ToolLine, Large: true), new(ToolRect, Large: true), new(ToolCircle, Large: true), new(ToolText, Large: true), new(ToolPin, Large: true)]),
                new("Kaydet", [new(SaveToProject, Large: true), new(SaveToLibrary, Large: true)]),
                new("Düzen", [new(Undo), new(Redo), new(Delete)]),
                new("Seçim", [new(Deselect), new(TurnPin), new(FirstPin)]),
                new("Yüzeye uç", [new(AddPinsOnSurface, Large: true)]),
                new("Yeni ucun yönü", [new(DirectionUp), new(DirectionDown), new(DirectionLeft), new(DirectionRight)]),
            ]),
        ],
        [SaveToProject, Undo, Redo],
        [SaveToProject, SaveToLibrary, SaveToFile, Close]);

    /// <summary>Çizim aracı komutları, <see cref="SymbolTool"/> karşılıklarıyla.</summary>
    public static IReadOnlyList<(string Command, SymbolTool Tool)> Tools { get; } =
    [
        (ToolSelect, SymbolTool.Select),
        (ToolLine, SymbolTool.Line),
        (ToolRect, SymbolTool.Rect),
        (ToolCircle, SymbolTool.Circle),
        (ToolText, SymbolTool.Text),
        (ToolPin, SymbolTool.Pin),
    ];

    /// <summary>Yeni ucun yön komutları, (x, y) yönleriyle.</summary>
    public static IReadOnlyList<(string Command, int X, int Y)> Directions { get; } =
    [
        (DirectionUp, 0, -1),
        (DirectionDown, 0, 1),
        (DirectionLeft, -1, 0),
        (DirectionRight, 1, 0),
    ];
}
