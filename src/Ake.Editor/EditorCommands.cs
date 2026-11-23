using FluentIcons.Common;

namespace Ake.Editor;

/// <summary>Bir editör komutunun görünen yüzü. Ne yapacağı pencere tarafından bağlanır.</summary>
/// <param name="Gesture">Kısayol, Avalonia biçiminde ("Ctrl+S", "PageUp"); yoksa <c>null</c>.</param>
/// <param name="Toggle">Açık/kapalı durumu olan komut (ızgara, tag şeması).</param>
public sealed record CommandInfo(string Id, string Label, Symbol Icon, string? Gesture = null, bool Toggle = false);

/// <summary>Şerit grubundaki bir düğme: büyük (ikon üstte, yazı altta) ya da küçük (ikon ve yazı yan yana).</summary>
public sealed record RibbonItem(string CommandId, bool Large = false);

public sealed record RibbonGroup(string Label, IReadOnlyList<RibbonItem> Items);

public sealed record RibbonTab(string Label, IReadOnlyList<RibbonGroup> Groups);

/// <summary>
/// Bir pencerenin komut şeridi: komutları, sekmeleri, hızlı erişim çubuğu ve Dosya menüsü. Ana pencere ve blok editörü kendi
/// tanımını verir; <see cref="RibbonView"/> hangisi verilirse onu kurar.
/// </summary>
public sealed record RibbonDefinition(
    IReadOnlyList<CommandInfo> Commands,
    IReadOnlyList<RibbonTab> Tabs,
    IReadOnlyList<string> QuickAccess,
    IReadOnlyList<string> FileMenu)
{
    public CommandInfo Get(string id) =>
        Commands.FirstOrDefault(command => command.Id == id)
        ?? throw new ArgumentException($"Tanımsız komut: {id}", nameof(id));
}

/// <summary>
/// Editörün komutları ve komut şeridinin düzeni (ADR-0023). Tek kaynaktır: şerit, Dosya menüsü,
/// hızlı erişim çubuğu ve kısayollar buradan kurulur.
/// </summary>
public static class EditorCommands
{
    public const string NewProject = "proje.yeni";
    public const string Open = "proje.ac";
    public const string Save = "proje.kaydet";
    public const string SaveAs = "proje.farkli_kaydet";
    public const string CloseProject = "proje.kapat";
    public const string ExportProject = "proje.disa_aktar";
    public const string ImportPdf = "proje.pdf_al";
    public const string ImportDxf = "proje.dxf_al";
    public const string ImportList = "proje.liste_al";
    public const string ProjectProperties = "proje.ozellikler";
    public const string AddRevisionEntry = "proje.revizyon_ekle";
    public const string Exit = "proje.cikis";
    public const string Undo = "duzen.geri";
    public const string Redo = "duzen.yinele";
    public const string Rotate = "duzen.dondur";
    public const string Properties = "duzen.ozellikler";
    public const string Deselect = "duzen.secimi_birak";
    public const string Delete = "duzen.sil";
    public const string SelectAll = "duzen.tumunu_sec";
    public const string Copy = "duzen.kopyala";
    public const string Cut = "duzen.kes";
    public const string Paste = "duzen.yapistir";
    public const string DrawWire = "baglanti.tel_cek";
    public const string InsertSymbol = "sembol.ekle";
    public const string ChangeSymbol = "sembol.degistir";
    public const string AddText = "ekle.yazi";
    public const string NewSymbol = "sembol.yeni";
    public const string EditSymbol = "sembol.duzenle";
    public const string OpenLibrary = "sembol.kutuphane";
    public const string ProductLibrary = "sembol.urun_kutuphanesi";
    public const string InsertMacro = "makro.ekle";
    public const string TerminalStrip = "ekle.klemens_seridi";
    public const string MacroFromPage = "makro.sayfadan";
    public const string MacroFromSelection = "makro.secimden";
    public const string RenumberWires = "baglanti.tel_numaralari";
    public const string StandardKinds = "baglanti.standart_turler";
    public const string Cables = "baglanti.kablolar";
    public const string Potentials = "baglanti.potansiyel";
    public const string NumberNewWires = "baglanti.yeni_teller";
    public const string WireNumberingSettings = "baglanti.numara_ayarlari";
    public const string CommunicationDiagram = "baglanti.haberlesme_semasi";
    public const string About = "yardim.hakkinda";
    public const string Help = "yardim.kilavuz";
    public const string SingleLineDiagram = "baglanti.tek_hat_semasi";
    public const string PlcTable = "baglanti.plc_tablosu";
    public const string PreviousPage = "sayfa.onceki";
    public const string NextPage = "sayfa.sonraki";
    public const string NewPage = "sayfa.ekle";
    public const string InsertPage = "sayfa.araya_ekle";
    public const string PageProperties = "sayfa.ozellikler";
    public const string TitleBlockEditor = "sayfa.antet_editoru";
    public const string RenumberPages = "sayfa.numaralari";
    public const string MovePageUp = "sayfa.yukari_tasi";
    public const string MovePageDown = "sayfa.asagi_tasi";
    public const string MovePageFirst = "sayfa.basa_tasi";
    public const string Fit = "gorunum.sigdir";
    public const string Grid = "gorunum.izgara";
    public const string DarkCanvas = "gorunum.koyu_zemin";
    public const string ShowPins = "gorunum.tel_uclari";
    public const string NavigatorPanel = "gorunum.navigator";
    public const string MessagesPanel = "gorunum.mesajlar";
    public const string CollapseRibbon = "gorunum.serit";
    public const string Focus = "gorunum.tam_alan";
    public const string ResetLayout = "gorunum.duzen_sifirla";
    public const string RenumberTags = "araclar.tag_numarala";
    public const string LetterScheme = "araclar.sema_harf";
    public const string PageScheme = "araclar.sema_sayfa";
    public const string Shortcuts = "araclar.kisayollar";
    public const string ToggleLanguage = "araclar.dil";
    public const string AiChange = "araclar.yapay_zeka";
    public const string RecognizeGraphics = "araclar.grafik_tani";

    public static IReadOnlyList<CommandInfo> All { get; } =
    [
        new(NewProject, "Yeni", Symbol.DocumentAdd, "Ctrl+N"),
        new(Open, "Aç", Symbol.FolderOpen, "Ctrl+O"),
        new(Save, "Kaydet", Symbol.Save, "Ctrl+S"),
        new(SaveAs, "Farklı kaydet", Symbol.SaveEdit, "Ctrl+Shift+S"),
        new(CloseProject, "Projeyi kapat", Symbol.Dismiss, "Ctrl+W"),
        new(ExportProject, "Dışa aktar / Raporlar…", Symbol.DocumentPdf, "Ctrl+P"),
        new(ImportPdf, "PDF'ten al", Symbol.DocumentPdf),
        new(ImportDxf, "DXF/DWG al…", Symbol.DocumentAdd),
        new(ImportList, "Liste al (Excel/CSV)…", Symbol.TableAdd),
        new(ProjectProperties, "Proje özellikleri…", Symbol.DocumentSettings),
        new(AddRevisionEntry, "Revizyon ekle…", Symbol.History),
        new(Exit, "Çıkış", Symbol.ArrowExit),
        new(Undo, "Geri al", Symbol.ArrowUndo, "Ctrl+Z"),
        new(Redo, "Yinele", Symbol.ArrowRedo, "Ctrl+Y"),
        new(Rotate, "Döndür", Symbol.ArrowRotateClockwise, "R"),
        new(Properties, "Özellikler", Symbol.TextBulletListSquareEdit, "Enter"),
        new(Deselect, "Seçimi bırak", Symbol.Dismiss, "Escape"),
        new(Delete, "Sil", Symbol.Delete, "Delete"),
        new(SelectAll, "Tümünü seç", Symbol.SelectAllOn, "Ctrl+A"),
        new(Copy, "Kopyala", Symbol.Copy, "Ctrl+C"),
        new(Cut, "Kes", Symbol.Cut, "Ctrl+X"),
        new(Paste, "Yapıştır", Symbol.ClipboardPaste, "Ctrl+V"),
        new(DrawWire, "Tel çek", Symbol.PlugConnected, "W", Toggle: true),
        new(InsertSymbol, "Sembol ekle…", Symbol.AddSquare, "Insert"),
        new(ChangeSymbol, "Sembol değiştir…", Symbol.ArrowSwap),
        new(AddText, "Metin ekle…", Symbol.TextAdd),
        new(NewSymbol, "Blok editörü", Symbol.Shapes),
        new(EditSymbol, "Sembolü düzenle", Symbol.Edit, "Ctrl+E"),
        new(OpenLibrary, "Kütüphane aç", Symbol.Library),
        new(ProductLibrary, "Ürün kütüphanesi…", Symbol.Library),
        new(InsertMacro, "Makro ekle", Symbol.PuzzlePiece, "Ctrl+M"),
        new(TerminalStrip, "Klemens şeridi", Symbol.TableSimple),
        new(MacroFromPage, "Sayfadan makro", Symbol.SaveCopy),
        new(MacroFromSelection, "Seçimden makro", Symbol.SelectObject),
        new(RenumberWires, "Baştan numarala", Symbol.NumberSymbol),
        new(NumberNewWires, "Yeni telleri numarala", Symbol.NumberRow),
        new(WireNumberingSettings, "Numaralandırma ayarları…", Symbol.Settings),
        new(StandardKinds, "Standart türleri ekle", Symbol.PaintBrush),
        new(Cables, "Kablo ve damar…", Symbol.TableSimple),
        new(Potentials, "Potansiyel işareti…", Symbol.Target),
        new(CommunicationDiagram, "Haberleşme şeması sayfası", Symbol.Flow),
        new(About, "Hakkında", Symbol.Info),
        new(SingleLineDiagram, "Tek hat şeması sayfası", Symbol.Flowchart),
        new(PlcTable, "PLC adres / tel tablosu…", Symbol.TableSimple),
        new(PreviousPage, "Önceki sayfa", Symbol.DocumentArrowLeft, "PageUp"),
        new(NextPage, "Sonraki sayfa", Symbol.DocumentArrowRight, "PageDown"),
        new(NewPage, "Yeni sayfa…", Symbol.DocumentAdd),
        new(InsertPage, "Araya sayfa ekle (3.1)", Symbol.DocumentAdd),
        new(PageProperties, "Sayfa özellikleri…", Symbol.Edit),
        new(TitleBlockEditor, "Antet oluştur / düzenle…", Symbol.TableSimple),
        new(RenumberPages, "Sayfa numaralarını baştan ver", Symbol.NumberRow),
        new(MovePageUp, "Sayfayı yukarı taşı", Symbol.ArrowUp),
        new(MovePageDown, "Sayfayı aşağı taşı", Symbol.ArrowDown),
        new(MovePageFirst, "Sayfayı en başa taşı", Symbol.ArrowUpload),
        new(Fit, "Sayfayı sığdır", Symbol.ZoomFit, "Home"),
        new(Grid, "Izgara", Symbol.Grid, "G", Toggle: true),
        new(DarkCanvas, "Koyu zemin", Symbol.DarkTheme, Toggle: true),
        new(ShowPins, "Tel uçları", Symbol.PlugConnected, Toggle: true),
        new(NavigatorPanel, "Navigator", Symbol.PanelLeft, Toggle: true),
        new(MessagesPanel, "Mesajlar", Symbol.PanelBottom, Toggle: true),
        new(CollapseRibbon, "Şeridi daralt", Symbol.ChevronUp, "Ctrl+F1", Toggle: true),
        new(Focus, "Tam çizim alanı", Symbol.FullScreenMaximize, "F11", Toggle: true),
        new(ResetLayout, "Varsayılan düzen", Symbol.ArrowReset),
        new(RenumberTags, "Tag'leri numarala", Symbol.Tag),
        new(LetterScheme, "Harf + sıra (K1)", Symbol.TextNumberFormat, Toggle: true),
        new(PageScheme, "Sayfa + harf + sıra (3K1)", Symbol.DocumentOnePage, Toggle: true),
        new(Shortcuts, "Kısayollar", Symbol.Keyboard),
        new(ToggleLanguage, "Dil: TR / EN", Symbol.LocalLanguage),
        new(AiChange, "Yapay zeka ile değiştir", Symbol.Sparkle),
        new(RecognizeGraphics, "Grafikten cihaz / bağlantı tanı…", Symbol.Shapes),
        new(Help, "Kullanım kılavuzu (Yardım)", Symbol.QuestionCircle, "F1"),
    ];

    /// <summary>Şeridin üstünde, sekmelerin sağında her zaman görünen komutlar.</summary>
    public static IReadOnlyList<string> QuickAccess { get; } = [Save, Undo, Redo, Help];

    /// <summary>Şeridin solundaki Dosya düğmesinin menüsü.</summary>
    public static IReadOnlyList<string> FileMenu { get; } = [NewProject, Open, Save, SaveAs, ExportProject, ProjectProperties, ImportPdf, CloseProject, Exit];

    public static IReadOnlyList<RibbonTab> Ribbon { get; } =
    [
        new("Başlangıç",
        [
            new("Proje", [new(NewProject, Large: true), new(Open, Large: true), new(Save, Large: true), new(SaveAs), new(CloseProject), new(ExportProject), new(ImportPdf), new(ImportDxf), new(ImportList), new(ProjectProperties), new(AddRevisionEntry)]),
            new("Pano", [new(Paste, Large: true), new(Cut), new(Copy)]),
            new("Düzen", [new(Undo), new(Redo), new(Delete)]),
            new("Seçim", [new(Properties, Large: true), new(Rotate), new(SelectAll), new(Deselect)]),
        ]),
        new("Ekle",
        [
            new("Sembol", [new(InsertSymbol, Large: true), new(ChangeSymbol), new(NewSymbol, Large: true), new(EditSymbol), new(OpenLibrary), new(ProductLibrary), new(AddText)]),
            new("Makro", [new(InsertMacro, Large: true), new(MacroFromPage), new(MacroFromSelection)]),
            new("Klemens", [new(TerminalStrip, Large: true)]),
        ]),
        new("Bağlantı",
        [
            new("Çizim", [new(DrawWire, Large: true)]),
            new("Numaralandırma", [new(NumberNewWires, Large: true), new(RenumberWires, Large: true), new(WireNumberingSettings, Large: true)]),
            new("Bağlantı türleri", [new(StandardKinds, Large: true), new(Cables), new(Potentials)]),
            new("Şemalar", [new(CommunicationDiagram, Large: true), new(SingleLineDiagram, Large: true), new(PlcTable)]),
        ]),
        new("Sayfa",
        [
            new("Gezinme", [new(PreviousPage, Large: true), new(NextPage, Large: true)]),
            new("Sayfa", [new(NewPage, Large: true), new(InsertPage, Large: true), new(PageProperties)]),
            new("Antet / Şablon", [new(TitleBlockEditor, Large: true)]),
            new("Sıra", [new(MovePageFirst), new(MovePageUp), new(MovePageDown)]),
            new("Numaralandırma", [new(RenumberPages, Large: true)]),
        ]),
        new("Görünüm",
        [
            new("Yakınlaştırma", [new(Fit, Large: true)]),
            new("Gösterim", [new(Grid, Large: true), new(DarkCanvas), new(ShowPins)]),
            new("Paneller", [new(Focus, Large: true), new(NavigatorPanel), new(MessagesPanel), new(CollapseRibbon), new(ResetLayout)]),
        ]),
        new("Araçlar",
        [
            new("Tag'ler", [new(RenumberTags, Large: true), new(LetterScheme), new(PageScheme)]),
            new("Tanıma", [new(RecognizeGraphics, Large: true)]),
            new("Yapay zeka", [new(AiChange, Large: true)]),
            new("Ayarlar", [new(Shortcuts, Large: true), new(ToggleLanguage, Large: true)]),
            new("Yardım", [new(Help, Large: true), new(About, Large: true)]),
        ]),
    ];

    public static CommandInfo Get(string id) =>
        All.FirstOrDefault(command => command.Id == id)
        ?? throw new ArgumentException($"Tanımsız komut: {id}", nameof(id));

    /// <summary>Ana pencerenin şerit tanımı.</summary>
    public static RibbonDefinition Main { get; } = new(All, Ribbon, QuickAccess, FileMenu);
}
