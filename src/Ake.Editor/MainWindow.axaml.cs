using System.Globalization;
using Ake.Core;
using Ake.Core.Model;
using Ake.Render;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// Ana pencere, yaygın E-CAD düzeninde: komut şeridi, taşınabilir paneller (navigator'lar, mesajlar),
/// ortada sekmeli sayfalar, altta bölmeli durum çubuğu. Kararları oturum verir; pencere yalnızca gösterir
/// ve çağırır.
/// </summary>
public partial class MainWindow : Window
{
    // Kısayol profili: varsayılan set, üstüne kullanıcının kisayollar.json'u (KeyMap).
    private KeyMap _keys = KeyMap.Default();
    private IReadOnlyList<(string Command, KeyGesture Gesture)> _gestures = [];

    // Uygulama içi sürükle-bırakın taşıdığı veri: bir cihaz ya da bir kütüphane sembolü.
    private static readonly DataFormat<DragPayload> PayloadFormat = DataFormat.CreateInProcessFormat<DragPayload>("aktapokus-electric/surukle");

    // Ürün kütüphanesinden şema sayfasına sürükle-bırak verisi.
    internal sealed record ProductDragPayload(string OrderCode, string? Manufacturer);
    internal static readonly DataFormat<ProductDragPayload> ProductDragFormat =
        DataFormat.CreateInProcessFormat<ProductDragPayload>("aktapokus-electric/urun-ekle");

    // Sayfalar listesinde sürükle-bırak ile sıra değiştirme verisi.
    private sealed record PageDragPayload(int PageIndex);
    private static readonly DataFormat<PageDragPayload> PageDragFormat = DataFormat.CreateInProcessFormat<PageDragPayload>("aktapokus-electric/sayfa-sira");

    private readonly EditorSession _session = new(Environment.UserName);
    private readonly EditorLayout _layout;
    private ProductSelectionWindow? _productLibraryWindow;

    // Açık sayfa yokken komutların dokunduğu, ekranda olmayan çizim alanı; boş kontrol gerektirmez.
    private readonly SceneView _idle = new();
    private PlacedBox? _selected;
    private List<PlacedBox> _selection = [];
    private List<GraphicBox> _graphicSelection = [];
    private List<WirePath> _wireSelection = [];
    private (double X, double Y)? _pointer;
    private (double X, double Y)? _contextPoint;
    private bool _wireMode;
    private bool _showPins;
    private PlacedPinInfo? _wireFrom;
    private readonly List<WirePoint> _wirePoints = [];
    private (ConnectionId Id, ConnectionPointId To)? _reroute;
    private string? _notice;
    private bool _gridOn = true;
    private bool _darkCanvas;
    private bool _syncingPageList;
    private bool _closeConfirmed;
    private (bool Navigators, bool Messages, bool Ribbon)? _beforeFocus;

    public MainWindow()
    {
        InitializeComponent();

        _layout = new EditorLayout(PagesPart, DeviceList, PotentialList, TerminalList, PlcList, CableList, SymbolsPart, MessageList, CreateView);
        DockHost.Content = _layout.Control;
        _layout.ActivePageChanged += _ => ShowActivePage();

        Ribbon.Bind(
            new Dictionary<string, Action>
            {
                [EditorCommands.NewProject] = () => _ = NewProject(),
                [EditorCommands.ProjectProperties] = () => _ = ShowProjectProperties(),
                [EditorCommands.AddRevisionEntry] = () => _ = ShowAddRevision(),
                [EditorCommands.Shortcuts] = () => _ = ShowKeyMapDialog(),
                [EditorCommands.ToggleLanguage] = ToggleUiLanguage,
                [EditorCommands.Help] = () => _ = ShowHelpWindow(),
                [EditorCommands.TerminalStrip] = () => _ = ShowTerminalStrip(),
                [EditorCommands.Open] = () => _ = ChooseAndOpen(),
                [EditorCommands.Save] = () => _ = SaveAsync(),
                [EditorCommands.ImportPdf] = () => _ = ImportPdfAsync(),
                [EditorCommands.ImportDxf] = () => _ = ImportDxfAsync(),
                [EditorCommands.ImportList] = () => _ = ImportSeeListAsync(),
                [EditorCommands.SaveAs] = () => _ = SaveAsAsync(),
                [EditorCommands.CloseProject] = () => _ = CloseProjectAsync(),
                [EditorCommands.ExportProject] = () => _ = ShowExportWindow(),
                [EditorCommands.Exit] = Close,
                [EditorCommands.Undo] = () => Apply(_session.Undo()),
                [EditorCommands.Redo] = () => Apply(_session.Redo()),
                [EditorCommands.Rotate] = RotateSelected,
                [EditorCommands.Properties] = () => _ = ShowProperties(),
                [EditorCommands.Deselect] = () =>
                {
                    if (_wireMode)
                    {
                        ToggleWireMode();
                    }

                    Select(null);
                },
                [EditorCommands.Delete] = () => Apply(_session.DeleteSelected(SelectedPlacements, SelectedGraphics, SelectedWires)),
                [EditorCommands.SelectAll] = () => SetSelection(_session.BoxesFor(ActivePage), null, _session.GraphicBoxesFor(ActivePage)),
                [EditorCommands.Copy] = CopySelection,
                [EditorCommands.Cut] = () =>
                {
                    CopySelection();
                    Apply(_session.DeleteMany(SelectedPlacements));
                },
                [EditorCommands.Paste] = PasteClipboard,
                [EditorCommands.DrawWire] = ToggleWireMode,
                [EditorCommands.InsertSymbol] = () => _ = InsertSymbolAt(ActivePage, _contextPoint?.X ?? _pointer?.X, _contextPoint?.Y ?? _pointer?.Y),
                [EditorCommands.ChangeSymbol] = () => _ = ChangeSelectedSymbol(),
                [EditorCommands.AddText] = () => _ = AddTextAt(ActivePage, _contextPoint?.X ?? _pointer?.X, _contextPoint?.Y ?? _pointer?.Y),
                [EditorCommands.NewSymbol] = () => _ = OpenSymbolEditor(_selected),
                [EditorCommands.EditSymbol] = () => _ = OpenSymbolEditor(_selected),
                [EditorCommands.OpenLibrary] = () => _ = ChooseAndOpenLibrary(),
                [EditorCommands.ProductLibrary] = ShowProductLibraryWindow,
                [EditorCommands.InsertMacro] = () => _ = ChooseAndInsertMacro(),
                [EditorCommands.MacroFromPage] = () => _ = SaveMacro(_session.MacroFromPage(ActivePage)),
                [EditorCommands.MacroFromSelection] = () =>
                {
                    if (_selected is { } selected)
                    {
                        _ = SaveMacro(_session.MacroFromSelection(selected.Placement));
                    }
                },
                [EditorCommands.RenumberWires] = () => _ = ConfirmRenumberWires(),
                [EditorCommands.NumberNewWires] = NumberNewWiresWithFeedback,
                [EditorCommands.NewPage] = () => _ = ShowNewPage(),
                [EditorCommands.InsertPage] = InsertPageAfterActive,
                [EditorCommands.PageProperties] = () => _ = ShowPageProperties(ActivePage),
                [EditorCommands.TitleBlockEditor] = () => _ = ShowTitleBlockEditor(),
                [EditorCommands.MovePageUp] = () => MovePage(ActivePage, ActivePage - 1),
                [EditorCommands.MovePageDown] = () => MovePage(ActivePage, ActivePage + 1),
                [EditorCommands.MovePageFirst] = () => MovePage(ActivePage, 0),
                [EditorCommands.AiChange] = () => _ = ShowAiChange(),
                [EditorCommands.RenumberPages] = () => Apply(_session.RenumberPages()),
                [EditorCommands.WireNumberingSettings] = () => _ = ShowWireNumbering(),
                [EditorCommands.StandardKinds] = AddStandardKindsWithFeedback,
                [EditorCommands.Cables] = () => _ = ShowCableWindow(),
                [EditorCommands.Potentials] = () => _ = ShowPotentialWindow(),
                [EditorCommands.CommunicationDiagram] = ShowCommunicationDiagram,
                [EditorCommands.About] = () => _ = new AboutWindow().ShowDialog(this),
                [EditorCommands.SingleLineDiagram] = ShowSingleLineDiagram,
                [EditorCommands.PlcTable] = () => _ = ShowPlcIoWindow(),
                [EditorCommands.PreviousPage] = () => StepPage(-1),
                [EditorCommands.NextPage] = () => StepPage(1),
                [EditorCommands.Fit] = () => Canvas.FitToView(),
                [EditorCommands.Grid] = () =>
                {
                    _gridOn = !_gridOn;
                    ShowGrid();
                },
                [EditorCommands.DarkCanvas] = ToggleDarkCanvas,
                [EditorCommands.ShowPins] = ToggleShowPins,
                [EditorCommands.RecognizeGraphics] = () => _ = ShowGraphicRecognition(),
                [EditorCommands.NavigatorPanel] = () => Panels(() => _layout.SetNavigatorsHidden(!_layout.NavigatorsHidden)),
                [EditorCommands.MessagesPanel] = () => Panels(() => _layout.SetMessagesHidden(!_layout.MessagesHidden)),
                [EditorCommands.CollapseRibbon] = () =>
                {
                    Ribbon.Collapsed = !Ribbon.Collapsed;
                    UpdateCommands();
                },
                [EditorCommands.Focus] = ToggleFocus,
                [EditorCommands.ResetLayout] = () => Panels(_layout.Reset),
                [EditorCommands.RenumberTags] = () => Apply(_session.RenumberTags()),
                [EditorCommands.LetterScheme] = () => Apply(_session.ChangeTagScheme(TagScheme.LetterNumber)),
                [EditorCommands.PageScheme] = () => Apply(_session.ChangeTagScheme(TagScheme.PageLetterNumber)),
            },
            CanRun,
            IsChecked);
        SymbolSearch.TextChanged += (_, _) => RefreshSymbolList();
        FitOnly.IsCheckedChanged += (_, _) => RefreshSymbolList();
        SymbolList.SelectionChanged += (_, _) => ShowSymbolPreview();
        SymbolList.DoubleTapped += (_, _) => AssignSelectedSymbol();
        AssignSymbolButton.Click += (_, _) => AssignSelectedSymbol();
        PlaceSymbolButton.Click += (_, _) => PlaceSelectedSymbol();
        EditSymbolButton.Click += (_, _) => _ = EditSelectedSymbol();
        VisualSymbolPickerButton.Click += (_, _) => _ = OpenVisualSymbolPicker();
        SymbolList.ItemTemplate = new Avalonia.Controls.Templates.FuncTreeDataTemplate<object>(
            (item, _) => new TextBlock
            {
                Text = item is SymbolGroup group ? group.ToString() : (item as SymbolRow)?.Display ?? string.Empty,
                FontWeight = item is SymbolGroup ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            },
            item => (item as SymbolGroup)?.Items ?? (System.Collections.IEnumerable)Array.Empty<object>());
        SymbolPreview.GridStepMm = SymbolDraft.GridMm;
        Closing += OnClosing;
        Closed += (_, _) => _session.Dispose();

        PageTree.SelectionChanged += (_, _) =>
        {
            if (!_syncingPageList && PageTree.SelectedItem is PageTreeRow { PageIndex: { } index })
            {
                OpenPage(index);
            }
        };

        // Grup satırına tıklamak katlar/açar (seçim değişmese de tıklama gelir).
        PageTree.Tapped += (_, _) =>
        {
            if (PageTree.SelectedItem is PageTreeRow { IsGroup: true } group)
            {
                ToggleGroup(group);
            }
        };
        // Sağ tık: sayfa ekle, araya ekle, numara değiştir, baştan numarala. Satır üzerinde ise o sayfa, değilse etkin sayfa esas alınır.
        PageTree.ContextRequested += (_, e) =>
        {
            var row = (e.Source as Avalonia.Controls.Control)?.DataContext as PageTreeRow;
            ShowPageMenu(row?.PageIndex ?? (ActivePage >= 0 ? ActivePage : null));
            e.Handled = true;
        };
        PageFilter.TextChanged += (_, _) => SyncPageList(ActivePage, _session.PageLabels);

        // Sayfa gezgini (ADR-0051): süzgeç alanı, Ağaç/Liste görünümü ve seçili sayfanın grafik önizlemesi.
        PageFilterFieldBox.ItemsSource = EditorSession.PageFilterFields.Select(field => field.Label).ToList();
        PageFilterFieldBox.SelectedIndex = 0;
        PageFilterFieldBox.SelectionChanged += (_, _) => SyncPageList(ActivePage, _session.PageLabels);
        PageTreeModeButton.Click += (_, _) => SetPageListMode(false);
        PageListModeButton.Click += (_, _) => SetPageListMode(true);
        PageTree.SelectionChanged += (_, _) => ShowPagePreview();
        TerminalList.DoubleTapped += (_, _) => GoTo(TerminalList.SelectedItem as NavigatorRow);
        PlcList.DoubleTapped += (_, _) => GoTo(PlcList.SelectedItem as NavigatorRow);
        PlcList.ContextRequested += (_, e) =>
        {
            _ = ShowPlcIoWindow();
            e.Handled = true;
        };
        CableList.DoubleTapped += (_, _) =>
        {
            var row = CableList.SelectedItem as NavigatorRow;
            if (row?.PageIndex is null)
            {
                _ = ShowCableWindow();
            }
            else
            {
                GoTo(row);
            }
        };
        CableList.ContextRequested += (_, e) =>
        {
            _ = ShowCableWindow();
            e.Handled = true;
        };
        DeviceList.DoubleTapped += (_, _) => GoTo(DeviceList.SelectedItem as NavigatorRow);
        PotentialList.DoubleTapped += (_, _) => GoTo(PotentialList.SelectedItem as NavigatorRow);
        PotentialList.ContextRequested += (_, e) =>
        {
            _ = ShowPotentialWindow();
            e.Handled = true;
        };
        MessageList.DoubleTapped += (_, _) => GoTo(MessageList.SelectedItem as NavigatorRow);
        EnableDrag(DeviceList, item => item is NavigatorRow { Device: { } device } ? new DragPayload(device, null) : null);
        EnableDrag(SymbolList, item => item is SymbolRow row ? new DragPayload(null, row.Id) : null);
        EnablePageDragAndDrop();

        // Kısayollar pencere düzeyinde yakalanır; bir metin kutusuna yazarken devreye girmez.
        AddHandler(KeyDownEvent, OnShortcut, RoutingStrategies.Tunnel);
        ShowGrid();
        ApplyLanguage();
        UpdateCommands();
        ShowStatus();
    }

    /// <summary>Arayüz dili tercihinin (<c>dil.json</c>) saklandığı dosya yolu.</summary>
    public string? LanguagePath { get; set; }

    /// <summary>
    /// Panel düzeninin saklandığı dosya. Uygulama verir; verilmezse düzen kaydedilmez (görüntü aracı,
    /// testler). Pencere kapanırken yazılır.
    /// </summary>
    public string? LayoutPath { get; set; }

    /// <summary>Etkin sayfa sekmesinin çizim alanı.</summary>
    private SceneView Canvas => _layout.ActiveView ?? _idle;

    private int ActivePage => _layout.ActivePage;

    /// <summary>Açık sayfa sekmesi sayısı; yalnızca açık sayfalar çizilir (görüntü aracı ve testler için).</summary>
    public int OpenPageCount => _layout.OpenViews.Count();

    /// <summary>Etkin sayfanın çizim alanı (görüntü aracı ve testler için).</summary>
    public SceneView ActiveCanvas => Canvas;

    /// <summary>
    /// Kısayol profilinin saklandığı dosya. Uygulama verir; verilmezse profil kaydedilmez (görüntü aracı, testler).
    /// </summary>
    /// <summary>Yapay zeka sağlayıcı/model ayarının saklandığı dosya okunur ve seçimler oraya yazılır (ADR-0044).</summary>
    public void UseAiSettingsFile(string path) => _session.UseAiSettingsFile(path);

    public string? KeyMapPath { get; set; }

    /// <summary>Kullanıcının kısayol dosyasını okur; yoksa varsayılan set, bozuksa varsayılan set ve durum satırında neden.</summary>
    public void LoadKeyMap()
    {
        if (KeyMapPath is not null)
        {
            var loaded = KeyMap.Load(KeyMapPath);
            if (loaded.IsSuccess)
            {
                _keys = loaded.Value;
            }
            else
            {
                _notice = $"{loaded.Error!.Message} Varsayılan kısayollar kullanılıyor.";
            }
        }

        ApplyKeys();
        ShowStatus();
    }

    // Profil değişince tuş yakalama, şerit ipuçları ve menüler yenilenir.
    private void ApplyKeys()
    {
        _gestures = _keys.Bindings;
        Ribbon.GestureOf = id => _keys.GestureOf(id);
    }

    private async Task ShowKeyMapDialog()
    {
        var edited = await new KeyMapWindow(_keys).ShowDialog<KeyMap?>(this);
        if (edited is null)
        {
            return;
        }

        _keys = edited;
        ApplyKeys();
        var saved = KeyMapPath is null ? Result.Success() : _keys.Save(KeyMapPath);
        _notice = saved.IsSuccess ? "Kısayollar kaydedildi" : saved.Error!.Message;
        ShowStatus();
    }

    /// <summary>Kaydedilmiş panel düzenini yükler; dosya yoksa ya da bozuksa varsayılan düzen kalır ve düzeltilir.</summary>
    public void LoadLayout()
    {
        if (LayoutPath is null || !File.Exists(LayoutPath))
        {
            return;
        }

        try
        {
            if (!_layout.Load(File.ReadAllText(LayoutPath)))
            {
                _notice = "Kaydedilmiş panel düzeni okunamadı; varsayılan düzen kullanılıyor.";
                SaveLayout();
            }
            else if (_layout.LastLoadRepaired)
            {
                SaveLayout();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Panel düzeni okunamadı");
            _notice = $"Panel düzeni okunamadı: {ex.Message}";
            SaveLayout();
        }

        UpdateCommands();
        ShowStatus();
    }

    /// <summary>Projeyi açar; başarısız olursa nedeni durum satırında gösterir ve açık proje kalır.</summary>
    public void OpenProject(string path)
    {
        var opened = _session.Open(path);
        if (opened.IsFailure)
        {
            _notice = opened.Error!.Message;
            ShowStatus();
            return;
        }

        ShowProject();
    }

    /// <summary>Açık projeyi kaydedilmemiş değişiklikleri sorarak tamamen kapatır.</summary>
    public async Task<bool> CloseProjectAsync()
    {
        if (_session.Project is null)
        {
            return true;
        }

        if (!await ConfirmDiscard())
        {
            return false;
        }

        CloseProjectCore();
        return true;
    }

    internal void CloseProjectCore()
    {
        ResetWireDraft();
        _wireMode = false;
        _showPins = false;
        _session.CloseProject();
        _notice = null;
        SetSelection([], null);
        _layout.CloseAllPages();
        _collapsedGroups.Clear();
        SyncPageList(-1, _session.PageLabels);
        _lastPageLabels = _session.PageLabels;
        PagePreview.Scene = null;
        PointerText.Text = "X —  Y —";
        RefreshNavigators();
        RefreshSymbolList();
        UpdateCommands();
        ShowStatus();
    }

    /// <summary>
    /// Yeni, boş proje açar (Ctrl+N). Açık projede kaydedilmemiş değişiklik varsa önce sorar.
    /// </summary>
    public async Task NewProject(NewProjectInfo? preset = null)
    {
        if (!await ConfirmDiscard())
        {
            return;
        }

        // Proje oluşturma penceresi (ADR-0047): ad, klasör, şablon, oluşturan, tarih. Hazır seçim verilirse pencere açılmaz (betik ve test).
        var info = preset ?? await new NewProjectWindow(DefaultProjectFolder()).ShowDialog<NewProjectInfo?>(this);
        if (info is null)
        {
            return;
        }

        var created = _session.NewProject(info, DateOnly.FromDateTime(DateTime.Now));
        if (created.IsFailure)
        {
            _notice = created.Error!.Message;
            ShowStatus();
            return;
        }

        ShowProject();

        // Klasör verildiyse proje hemen klasörd.json olarak kaydedilir.
        if (info.Folder is { } folder)
        {
            var saved = _session.SaveAs(Path.Combine(folder, info.Name + ".json"));
            _notice = saved.IsFailure ? saved.Error!.Message : null;
            ShowStatus();
        }
    }

    // Yeni projeler için önerilen klasör: Belgelerimktapokus-electric.
    private static string DefaultProjectFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "aktapokus-electric");

    // Revizyon ekle penceresi (ADR-0050): işaret, tarih, hazırlayan, açıklama; antetteki revizyon da güncellenir.
    private async Task ShowAddRevision()
    {
        if (_session.Project is null)
        {
            return;
        }

        var suggestion = _session.SuggestRevision(DateOnly.FromDateTime(DateTime.Now));
        var revision = await new RevisionWindow(suggestion, _session.Project.Revisions).ShowDialog<Ake.Core.Model.Revision?>(this);
        if (revision is not null)
        {
            Apply(_session.AddRevisionRecord(revision));
        }
    }

    // Proje özellikleri penceresi: ad ve kapak/antet bilgileri tek adımda değişir (geri alınır); Yapı sekmesi düğümleri doğrudan günceller.
    private async Task ShowProjectProperties()
    {
        if (_session.Project is null)
        {
            return;
        }

        var window = new ProjectPropertiesWindow(_session);
        var edit = await window.ShowDialog<ProjectInfoEdit?>(this);
        if (edit is not null)
        {
            Apply(_session.SetProjectInfo(edit.Name, edit.Values));
        }
        else if (window.StructureChanged)
        {
            Apply(Result.Success());
        }

        if (window.StructureChanged)
        {
            SyncPageList(ActivePage, _session.PageLabels);
        }

        if (window.OpenTitleBlockAfter)
        {
            await ShowTitleBlockEditor();
        }
    }

    // Açılan ya da yeni oluşturulan projeyi gösterir: sekmeler kapanır, navigator'lar ve ilk sayfa yenilenir.
    private void ShowProject()
    {
        _notice = null;
        SetSelection([], null);
        _layout.CloseAllPages();
        SyncPageList(-1, _session.PageLabels);
        _lastPageLabels = _session.PageLabels;
        RefreshNavigators();
        if (_session.PageLabels.Count > 0)
        {
            OpenPage(0);
        }

        UpdateCommands();
        ShowStatus();
    }

    /// <summary>
    /// Kaydedilmemiş değişiklik varsa ne yapılacağını sorar. Devam edilebilirse (kaydedildi, atıldı ya da
    /// değişiklik yok) <c>true</c>; kullanıcı vazgeçerse ya da kaydetme başarısız olursa <c>false</c>.
    /// </summary>
    private async Task<bool> ConfirmDiscard()
    {
        if (!_session.IsDirty)
        {
            return true;
        }

        var choice = await new UnsavedChangesDialog(_session.Project!.Name).ShowDialog<UnsavedChoice?>(this);
        return choice switch
        {
            UnsavedChoice.Save => await SaveAsync(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    // Kapatmadan önce sorulur; soru zaman uyumsuz olduğu için ilk kapanma iptal edilir ve cevaptan sonra yeniden kapatılır.
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (!_closeConfirmed && _session.IsDirty)
        {
            e.Cancel = true;
            _ = ConfirmAndClose();
            return;
        }

        SaveLayout();
    }

    private async Task ConfirmAndClose()
    {
        if (await ConfirmDiscard())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>Sayfayı sekmede açar (ya da öne getirir).</summary>
    public void OpenPage(int index)
    {
        if (index >= 0 && index < _session.PageLabels.Count)
        {
            _layout.OpenPage(index, _session.PageLabels[index]);
        }
    }

    /// <summary>Paylaşılan sembol kütüphanesini açar; Semboller paneli onu gösterir.</summary>
    public void OpenLibrary(string path)
    {
        var opened = _session.OpenLibrary(path);
        _notice = opened.IsSuccess ? null : opened.Error!.Message;
        RefreshSymbolList();
        ShowStatus();
    }

    /// <summary>Semboller panelini öne getirir.</summary>
    public void ShowSymbolsTab() => _layout.ShowTool(EditorLayout.SymbolsId);

    /// <summary>Paneli öne getirir (görüntü aracı için): sayfalar, cihazlar, potansiyeller, klemensler, plc, kablolar, mesajlar.</summary>
    public void ShowPanel(string panelId) => _layout.ShowTool(panelId);

    // Her sayfa sekmesinin kendi çizim alanı vardır; olaylar o sekmenin sayfasıyla çalışır.
    private SceneView CreateView(int page)
    {
        var view = new SceneView();

        // Sol tık: sembol seçer (Ctrl ile seçime ekler/çıkarır); seçili bir sembolü sürüklemek bütün seçimi
        // taşır. Boş alanda sürüklemek seçim dikdörtgeni çizer. Tel çekme modunda tık uca yakalanır.
        view.ContentPressed = (x, y) =>
        {
            if (_wireMode)
            {
                WireClick(page, x, y);
                return true;
            }

            var hit = _session.HitTest(page, x, y);
            var adding = view.PressModifiers.HasFlag(KeyModifiers.Control);
            if (hit is null)
            {
                // Sembol yoksa serbest grafik denenir (çizgi, daire, yazı).
                if (_session.GraphicAt(page, x, y) is { } graphic)
                {
                    var present = _graphicSelection.Any(g => g.Graphic == graphic.Graphic);
                    if (adding)
                    {
                        SetSelection(_selection, _selected, present ? [.. _graphicSelection.Where(g => g.Graphic != graphic.Graphic)] : [.. _graphicSelection, graphic], _wireSelection);
                    }
                    else if (!present)
                    {
                        SetSelection([], null, [graphic]);
                    }

                    return true;
                }

                // Grafik de yoksa tel denenir. Tel sürüklenmez (taşınacak bir şey yok); sürükleme dikdörtgen seçer.
                if (_session.WireAt(page, x, y) is { } wire)
                {
                    var present = _wireSelection.Any(w => w.Connection == wire.Connection);
                    if (adding)
                    {
                        SetSelection(_selection, _selected, _graphicSelection, present ? [.. _wireSelection.Where(w => w.Connection != wire.Connection)] : [.. _wireSelection, wire]);
                    }
                    else if (!present)
                    {
                        SetSelection([], null, [], [wire]);
                    }

                    return false;
                }

                if (!adding)
                {
                    Select(null);
                }

                return false;
            }

            if (adding)
            {
                var others = _selection.Where(box => box.Placement != hit.Placement).ToList();
                SetSelection(others.Count < _selection.Count ? others : [.. _selection, hit], hit, _graphicSelection, _wireSelection);
            }
            else if (_selection.All(box => box.Placement != hit.Placement))
            {
                Select(hit);
            }
            else
            {
                SetSelection(_selection, hit, _graphicSelection, _wireSelection);
            }

            return _selection.Count > 0 || _graphicSelection.Count > 0;
        };
        view.RegionSelected += region =>
        {
            var inside = _session.BoxesIn(page, region);
            var graphics = _session.GraphicsIn(page, region);
            var adding = view.PressModifiers.HasFlag(KeyModifiers.Control);
            SetSelection(
                adding ? [.. _selection, .. inside.Where(box => _selection.All(s => s.Placement != box.Placement))] : inside,
                inside.LastOrDefault(),
                adding ? [.. _graphicSelection, .. graphics.Where(g => _graphicSelection.All(s => s.Graphic != g.Graphic))] : graphics,
                adding ? _wireSelection : null);
        };
        view.ContentDoubleClicked += (x, y) =>
        {
            if (_wireMode)
            {
                return;
            }

            var symbol = _session.HitTest(page, x, y);
            if (symbol is null && LinkTargetAt(view, x, y) is { } target)
            {
                OpenPage(target);
                return;
            }

            if (symbol is null && _session.GraphicAt(page, x, y) is { } graphic)
            {
                SetSelection([], null, [graphic]);
                _ = EditSelectedText();
                return;
            }

            Select(symbol);
            _ = ShowProperties();
        };
        view.ContentDragged += (dx, dy) =>
        {
            if (!_wireMode)
            {
                var (sx, sy) = EditorSession.Snap(dx, dy);
                view.Ghosts = [.. _selection.Select(box => box.Bounds).Concat(_graphicSelection.Select(g => g.Bounds))
                    .Select(bounds => bounds with { X = bounds.X + sx, Y = bounds.Y + sy })];
            }
        };
        view.ContentReleased += (dx, dy) =>
        {
            view.Ghosts = [];
            if (!_wireMode && (_selection.Count > 0 || _graphicSelection.Count > 0))
            {
                Apply(_session.MoveSelected(SelectedPlacements, SelectedGraphics, dx, dy));
            }
        };
        view.ContentContextRequested += (x, y) => ShowContextMenu(view, page, x, y);
        view.AcceptsDrop = data => data.Contains(PayloadFormat) || data.Contains(ProductDragFormat) || data.Contains(DataFormat.File);
        view.Dropped += (data, x, y) => Drop(page, data, x, y);
        view.SheetPointerMoved += (x, y) =>
        {
            _pointer = (x, y);
            if (_wireMode)
            {
                view.HoverPin = _session.PinAt(page, x, y, 12 * view.MmPerPixel) is { } hovered ? (hovered.X, hovered.Y) : null;
            }

            PointerText.Text = string.Create(CultureInfo.InvariantCulture, $"X {x:0.0}   Y {y:0.0} mm");
            if (_wireMode && _wireFrom is { } from)
            {
                // İmleç bir uca yakınsa yol o uca biter; değilse imlecin yapışma adımına oturan noktasına.
                var end = _session.PinAt(page, x, y, 12 * view.MmPerPixel) is { } pin ? (pin.X, pin.Y) : EditorSession.Snap(x, y);
                view.PreviewPath = SheetRenderer.RoutePreview(from, _wirePoints, end);
            }
        };

        var scene = _session.SceneFor(page, DateOnly.FromDateTime(DateTime.Now));
        view.Scene = scene.IsSuccess ? scene.Value : null;
        view.ImageBaseDirectory = _session.ProjectDirectory;
        view.DarkCanvas = _darkCanvas;
        view.GridArea = _gridOn && _session.IsSchematicPage(page) ? _session.DrawingAreaFor(page) : null;
        ShowPinMarkers(page, view);
        return view;
    }

    private async Task ChooseAndOpenLibrary()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Sembol kütüphanesi aç",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Kütüphane") { Patterns = ["*.db"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            OpenLibrary(path);
            ShowSymbolsTab();
        }
    }

    private static readonly FilePickerFileType MacroFiles = new("Makro") { Patterns = ["*.makro.json"] };

    /// <summary>Hazır makro kütüphanesini değer kümeleriyle açar (ya da .makro.json seçtirir) ve etkin sayfaya yerleştirir.</summary>
    private async Task ChooseAndInsertMacro()
    {
        var window = new MacroLibraryWindow(Ake.Library.StandardMacros.All);
        var picked = await window.ShowDialog<MacroSelectionResult?>(this);
        if (picked is null)
        {
            return;
        }

        var inserted = _session.InsertMacro(picked.Macro, ActivePage, valueSetName: picked.ValueSetName);
        Apply(inserted);
        if (inserted.IsSuccess)
        {
            var suffix = string.IsNullOrWhiteSpace(picked.ValueSetName) ? string.Empty : $" [{picked.ValueSetName}]";
            _notice = $"Makro yerleştirildi: {picked.Macro.Name}{suffix} ({picked.Macro.Devices.Count} cihaz). Taşımak için sembolleri sürükleyin.";
            ShowStatus();
        }
    }

    /// <summary>Yakalanan makroyu kaydetme penceresiyle dosyaya yazar.</summary>
    private async Task SaveMacro(Result<Ake.Core.Macros.Macro> captured)
    {
        if (captured.IsFailure)
        {
            Apply(Result.Failure(captured.Error!));
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Makroyu kaydet",
            SuggestedFileName = $"{captured.Value.Name.Trim('-')}.makro.json",
            DefaultExtension = "makro.json",
            FileTypeChoices = [MacroFiles],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        var saved = Ake.Serialization.MacroJson.Save(captured.Value, path);
        _notice = saved.IsSuccess
            ? $"Makro kaydedildi: {captured.Value.Name} ({captured.Value.Devices.Count} cihaz, {captured.Value.Placements.Count} sembol)"
            : saved.Error!.Message;
        ShowStatus();
    }

    // Seçili cihaza uyanlar işaretliyse yalnızca seçili function'ın bağlantı noktası sayısı kadar ucu olanlar.
    private void RefreshSymbolList()
    {
        int? pins = FitOnly.IsChecked == true && _selected is { } selected
            ? _session.FunctionOf(selected.Placement)?.ConnectionPoints.Count
            : null;
        var searching = !string.IsNullOrWhiteSpace(SymbolSearch.Text) || pins is not null;
        var extraCategories = searching ? null : _session.SymbolCategories();
        SymbolList.ItemsSource = SymbolPalette.Build(_session.SearchLibrary(SymbolSearch.Text ?? string.Empty, pins), searching, extraCategories);
        LibraryInfo.Text = _session.LibraryPath is null
            ? "Kütüphane açık değil (Ekle > Kütüphane aç)."
            : $"{_session.LibrarySymbolCount} sembol" + (pins is { } n ? $"  ·  {n} uçlular" : string.Empty);
        AssignSymbolButton.IsEnabled = _selected is not null && SymbolList.SelectedItem is not null;
        PlaceSymbolButton.IsEnabled = ActivePage >= 0 && SymbolList.SelectedItem is SymbolRow;
        EditSymbolButton.IsEnabled = SymbolList.SelectedItem is SymbolRow;
    }

    private void ShowSymbolPreview()
    {
        var row = SymbolList.SelectedItem as SymbolRow;
        var scene = row is null ? null : _session.PreviewOf(row.Id);
        SymbolPreview.Scene = scene;
        SymbolPreview.GridArea = scene is null ? null : new Box(0, 0, scene.WidthMm, scene.HeightMm);
        AssignSymbolButton.IsEnabled = _selected is not null && row is not null;
        PlaceSymbolButton.IsEnabled = ActivePage >= 0 && row is not null;
        EditSymbolButton.IsEnabled = row is not null;
    }

    // Paletteki seçili sembolü etkin sayfaya yerleştirir: imleç çizim alanındaysa imlecin olduğu yere, değilse alanın ortasına.
    private void PlaceSelectedSymbol()
    {
        if (SymbolList.SelectedItem is not SymbolRow row || ActivePage < 0 || _session.DrawingAreaFor(ActivePage) is not { } area)
        {
            _notice = "Önce bir sayfa açın ve listeden bir sembol seçin.";
            ShowStatus();
            return;
        }

        var inside = _pointer is { } p && p.X >= area.X && p.X <= area.Right && p.Y >= area.Y && p.Y <= area.Bottom;
        var (x, y) = inside ? _pointer!.Value : (area.X + (area.Width / 2), area.Y + (area.Height / 2));
        var placed = _session.PlaceLibrarySymbol(row.Id, ActivePage, x, y);
        if (placed.IsFailure)
        {
            Apply(Result.Failure(placed.Error!));
            return;
        }

        Apply(Result.Success());
        Select(_session.BoxesFor(ActivePage).FirstOrDefault(box => box.Placement == placed.Value));
    }

    // Sayfayı listede taşır ve taşınan sayfayı açık tutar; numaralar değişmez (ADR-0048).
    private void MovePage(int from, int to)
    {
        if (from < 0 || to < 0 || to >= _session.PageLabels.Count)
        {
            return;
        }

        var moved = _session.MovePageTo(from, to);
        Apply(moved);
        if (moved.IsSuccess)
        {
            OpenPage(to);
        }
    }

    // Yeni sayfa penceresi: tür (şema, kapak, içindekiler, malzeme/klemens/kablo listesi), başlık ve konum; eklenen ilk sayfa açılır (ADR-0046).
    private async Task ShowNewPage()
    {
        if (_session.Project is null)
        {
            return;
        }

        // Şablon seçenekleri: önceki sayfayla aynı, projedeki şablonlar, projede olmayan hazır şablonlar (seçilirse projeye eklenir).
        var templates = new List<(Ake.Core.Model.SheetTemplateId? Id, string Label)> { (null, "Önceki sayfayla aynı") };
        templates.AddRange(_session.Project.SheetTemplates.Select(template => ((Ake.Core.Model.SheetTemplateId?)template.Id, template.Name)));
        templates.AddRange(Ake.Core.Model.StandardSheetTemplates.All
            .Where(standard => _session.Project.SheetTemplates.All(template => template.Id != standard.Id))
            .Select(standard => ((Ake.Core.Model.SheetTemplateId?)standard.Id, $"{standard.Name} (projeye eklenir)")));
        var choice = await new NewPageWindow(ActivePage >= 0, templates, _session, ActivePage >= 0 ? ActivePage : null).ShowDialog<NewPageChoice?>(this);
        if (choice is null)
        {
            return;
        }

        var added = _session.AddPages(
            choice.Kind,
            choice.Title,
            choice.AfterActive && ActivePage >= 0 ? ActivePage : null,
            choice.TemplateId,
            choice.FunctionNodeId,
            choice.LocationNodeId,
            choice.Number,
            choice.ExplicitStructure);
        if (added.IsFailure)
        {
            Apply(Result.Failure(added.Error!));
            return;
        }

        Apply(Result.Success());
        OpenPage(added.Value);
    }

    private async Task ShowPageProperties(int pageIndex)
    {
        if (_session.PageDataOf(pageIndex) is not { } data)
        {
            return;
        }

        var edits = await new PagePropertiesWindow(data).ShowDialog<PageEdits?>(this);
        if (edits is null)
        {
            return;
        }

        var result = _session.ApplyPageEdits(pageIndex, edits);
        Apply(result);
        if (result.IsSuccess && _session.IndexOfPage(data.PageId) is >= 0 and var movedIndex)
        {
            OpenPage(movedIndex);
            SyncPageList(movedIndex, _session.PageLabels);
        }
        else
        {
            SyncPageList(ActivePage, _session.PageLabels);
        }
    }

    /// <summary>Görsel önizlemeli antet ve sayfa şablonu oluşturma / düzenleme penceresini açar.</summary>
    private async Task ShowTitleBlockEditor()
    {
        if (_session.Project is null)
        {
            return;
        }

        var result = await new TitleBlockEditorWindow(_session, ActivePage).ShowDialog<TitleBlockEditorResult?>(this);
        if (result is null)
        {
            return;
        }

        var saved = _session.SaveSheetTemplate(result.Template, result.ApplyToActivePage, ActivePage, result.ApplyToAllPages);
        Apply(saved);
        if (saved.IsSuccess)
        {
            _notice = $"Antet / şablon kaydedildi: {result.Template.Name}";
            ShowStatus();
        }
    }

    private async Task ShowCableWindow(ConnectionId? connection = null)
    {
        if (_session.Project is null)
        {
            return;
        }

        var target = connection ?? (_wireSelection is [{ } onlyWire] ? onlyWire.Connection : (ConnectionId?)null);
        var window = new CableWindow(_session, target);
        await window.ShowDialog(this);
        if (window.Changed)
        {
            Apply(Result.Success());
        }
    }

    private async Task ShowPotentialWindow(ConnectionPointId? point = null)
    {
        if (_session.Project is null)
        {
            return;
        }

        var target = point;
        if (target is null && _selected is { } box && _session.FunctionOf(box.Placement) is { } fn && fn.ConnectionPoints.Count > 0)
        {
            target = fn.ConnectionPoints[0].Id;
        }
        else if (target is null && _wireSelection is [{ } wire] && _session.Project.FindConnection(wire.Connection) is { } conn)
        {
            target = conn.From;
        }

        var window = new PotentialWindow(_session, target);
        await window.ShowDialog(this);
        if (window.Changed)
        {
            Apply(Result.Success());
        }
    }

    private void ShowProductLibraryWindow()
    {
        if (_productLibraryWindow is not null)
        {
            _productLibraryWindow.Activate();
            return;
        }

        EnsureDefaultLibraryOpen();
        var window = new ProductSelectionWindow(
            _session,
            allowAddDevice: true,
            onPlaceProduct: PlaceProductFromLibraryWindow,
            onLibraryChanged: () => RefreshSymbolList());
        _productLibraryWindow = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_productLibraryWindow, window))
            {
                _productLibraryWindow = null;
            }
        };
        window.Show(this);
    }

    private Result PlaceProductFromLibraryWindow(Ake.Library.LibraryProduct product)
    {
        if (_session.Project is null)
        {
            return Result.Failure(new Error("proje.acik_degil", "Önce bir proje açın."));
        }

        int? targetPage = ActivePage >= 0 && _session.IsSchematicPage(ActivePage)
            ? ActivePage
            : null;
        var placed = _session.PlaceLibraryProduct(
            product.Definition.OrderCode,
            product.Definition.Manufacturer,
            targetPage);
        if (placed.IsFailure)
        {
            Apply(Result.Failure(placed.Error!));
            return Result.Failure(placed.Error!);
        }

        var placedPage = targetPage ?? EditorSession.FirstSchematicPageIndex(_session.Project);
        Apply(Result.Success());
        if (placedPage >= 0)
        {
            OpenPage(placedPage);
            if (placed.Value.Count > 0)
            {
                var firstId = placed.Value[0];
                Select(_session.BoxesFor(placedPage).FirstOrDefault(box => box.Placement == firstId));
            }
        }

        return Result.Success();
    }

    private async Task ShowExportWindow()
    {
        if (_session.Project is null)
        {
            return;
        }

        await new ExportWindow(_session, ActivePage).ShowDialog(this);
    }

    private async Task ShowPlcIoWindow()
    {
        if (_session.Project is null)
        {
            return;
        }

        var window = new PlcIoWindow(_session);
        await window.ShowDialog(this);
        if (window.Changed)
        {
            Apply(Result.Success());
        }
    }

    // Etkin sayfanın arkasına, sonraki sayfaları yeniden numaralamadan alt sayfa (3.1) ekler ve açar (ADR-0042).
    private void InsertPageAfterActive()
    {
        var inserted = _session.InsertPageAfter(ActivePage);
        if (inserted.IsFailure)
        {
            Apply(Result.Failure(inserted.Error!));
            return;
        }

        Apply(Result.Success());
        OpenPage(inserted.Value);
    }

    // Yerel LLM ile değişiklik: öneri penceresi; kullanıcı Uygula derse tek adımda uygulanır (ADR-0044).
    private async Task ShowAiChange()
    {
        if (_session.Project is null)
        {
            return;
        }

        var window = new AiChangeWindow(
            _session.AiSettings,
            settings => _session.ListAiModelsAsync(settings),
            (settings, request, token) => _session.ProposeAiChangeAsync(request, settings, token));
        _ = _session.PreloadAiModelAsync();
        var accepted = await window.ShowDialog<Ake.Ai.PatchProposal?>(this);
        if (accepted is not null)
        {
            Apply(_session.ApplyAiProposal(accepted));
        }
    }

    // Tel numaralandırma ayarları: kod, başlangıç, adım, basamak, şema.
    private async Task ShowWireNumbering()
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var settings = await new WireNumberingWindow(project.WireNumbering).ShowDialog<WireNumberSettings?>(this);
        if (settings is not null)
        {
            Apply(_session.SetWireNumbering(settings));
        }
    }

    // Tek hat şeması: haberleşme şeması gibi antetli bir sayfadır; içeriği güç zincirinden üretilir (ADR-0039).
    private void ShowSingleLineDiagram()
    {
        var page = _session.AddSingleLinePage();
        if (page.IsFailure)
        {
            Apply(Result.Failure(page.Error!));
            _ = ShowInfoDialogAsync(UiStrings.T("Tek hat şeması"), page.Error!.Message);
            return;
        }

        Apply(Result.Success());
        OpenPage(page.Value);
        _notice = UiStrings.T("Tek hat şeması sayfası oluşturuldu.");
        ShowStatus();
    }

    // Haberleşme şeması: antetli bir sayfa olarak projeye eklenir (varsa açılır); içeriği projeden otomatik üretilir (ADR-0037).
    private void ShowCommunicationDiagram()
    {
        var page = _session.AddCommunicationPage();
        if (page.IsFailure)
        {
            Apply(Result.Failure(page.Error!));
            _ = ShowInfoDialogAsync(UiStrings.T("Haberleşme şeması"), page.Error!.Message);
            return;
        }

        Apply(Result.Success());
        OpenPage(page.Value);
        _notice = UiStrings.T("Haberleşme şeması sayfası oluşturuldu.");
        ShowStatus();
    }

    private async Task ShowHelpWindow() => await new HelpWindow().ShowDialog(this);

    private void ToggleUiLanguage()
    {
        UiStrings.Current = UiStrings.Current == UiLanguage.Turkish
            ? UiLanguage.English
            : UiLanguage.Turkish;
        if (!string.IsNullOrEmpty(LanguagePath))
        {
            UiStrings.Save(LanguagePath);
        }

        ApplyLanguage();
        _notice = UiStrings.T("Arayüz dili güncellendi.");
        ShowStatus();
    }

    public void ApplyLanguage()
    {
        Ribbon.ApplyLanguage();
        _layout.SetTitle(EditorLayout.PagesId, UiStrings.T("Sayfalar"));
        _layout.SetTitle(EditorLayout.DevicesId, UiStrings.T("Cihazlar"));
        _layout.SetTitle(EditorLayout.PotentialsId, UiStrings.T("Potansiyeller"));
        _layout.SetTitle(EditorLayout.TerminalsId, UiStrings.T("Klemensler"));
        _layout.SetTitle(EditorLayout.PlcId, UiStrings.T("PLC"));
        _layout.SetTitle(EditorLayout.CablesId, UiStrings.T("Kablolar"));
        _layout.SetTitle(EditorLayout.SymbolsId, UiStrings.T("Semboller"));

        PageTreeModeButton.Content = UiStrings.T("Ağaç");
        PageListModeButton.Content = UiStrings.T("Liste");
        PageFilter.PlaceholderText = UiStrings.T("Sayfa ara");
        var fieldIndex = Math.Max(0, PageFilterFieldBox.SelectedIndex);
        PageFilterFieldBox.ItemsSource = EditorSession.PageFilterFields.Select(field => UiStrings.T(field.Label)).ToList();
        PageFilterFieldBox.SelectedIndex = fieldIndex;

        SymbolSearch.PlaceholderText = UiStrings.T("Sembol ara");
        FitOnly.Content = UiStrings.T("Seçili cihaza uyanlar");
        VisualSymbolPickerButton.Content = UiStrings.T("Görsel sembol seçici…");
        EditSymbolButton.Content = UiStrings.T("Sembolü düzenle…");
        PlaceSymbolButton.Content = UiStrings.T("Sayfaya yerleştir");
        AssignSymbolButton.Content = UiStrings.T("Seçili sembole ata");

        RefreshNavigators();
        ShowStatus();
    }

    private async Task ConfirmRenumberWires()
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        if (project.Connections.Count == 0)
        {
            var emptyMessage = UiStrings.T("Projede numaralandırılacak tel veya bağlantı bulunamadı. Önce 'Tel çek' aracıyla iki uç arasında bağlantı oluşturun.");
            _notice = emptyMessage;
            ShowStatus();
            await ShowInfoDialogAsync(UiStrings.T("Tel numaralarını baştan ver"), emptyMessage);
            return;
        }

        var question = UiStrings.T(
            "Projedeki tüm tel numaraları baştan numaralandırılacak ve mevcut numaralar değişecektir. Devam etmek istediğinize emin misiniz?\n\n(Hatalı tıklarsanız Ctrl+Z / Geri Al ile tek adımda geri alabilirsiniz.)");
        var confirmed = await new ConfirmDialog(UiStrings.T("Tel numaralarını baştan ver"), question).ShowDialog<bool>(this);
        if (!confirmed)
        {
            _notice = UiStrings.T("Baştan numaralandırma iptal edildi.");
            ShowStatus();
            return;
        }

        var result = _session.RenumberWires();
        Apply(result);
        if (result.IsSuccess)
        {
            _notice = UiStrings.T("Tüm teller baştan numaralandı (geri almak için Ctrl+Z).");
            ShowStatus();
        }
    }

    private void NumberNewWiresWithFeedback()
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        if (project.Connections.Count == 0)
        {
            _notice = UiStrings.T("Projede henüz tel bağlantısı yok.");
            ShowStatus();
            return;
        }

        var unnumbered = project.Connections.Count(c => c.CoreId is null && string.IsNullOrWhiteSpace(c.Wire?.Number));
        var result = _session.NumberNewWires();
        Apply(result);
        if (result.IsSuccess)
        {
            var remaining = _session.Project?.Connections.Count(c => c.CoreId is null && string.IsNullOrWhiteSpace(c.Wire?.Number)) ?? 0;
            var numbered = Math.Max(0, unnumbered - remaining);
            _notice = numbered == 0
                ? UiStrings.T("Numaralandırılacak yeni tel yok (tüm teller zaten numaralı).")
                : string.Format(UiStrings.T("{0} yeni tel numaralandırıldı."), numbered);
            ShowStatus();
        }
    }

    private void AddStandardKindsWithFeedback()
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var before = project.ConnectionKinds.Count;
        var result = _session.AddStandardConnectionKinds();
        Apply(result);
        if (result.IsSuccess)
        {
            var added = (_session.Project?.ConnectionKinds.Count ?? before) - before;
            _notice = added == 0
                ? UiStrings.T("Standart bağlantı türleri zaten projede mevcut.")
                : string.Format(UiStrings.T("{0} standart bağlantı türü eklendi. 'Kablo ve damar…' penceresinden bağlantılara atayabilirsiniz."), added);
            ShowStatus();
        }
    }

    private async Task ShowInfoDialogAsync(string title, string message)
    {
        var ok = new Button
        {
            Content = UiStrings.T("Tamam"),
            MinWidth = 96,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            IsDefault = true,
            IsCancel = true,
        };
        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter"),
            FontSize = 14,
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(20, 18),
                Spacing = 16,
                Children =
                {
                    new TextBlock { Text = message, MaxWidth = 460, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    ok,
                },
            },
        };
        ok.Click += (_, _) => dialog.Close();
        await dialog.ShowDialog(this);
    }

    // Paletteki sembolü blok editöründe açar. Kullanıcının kendi sembolü yerinde güncellenir; standart set ve üretici sembolleri
    // kopya olarak "Kendi sembollerim" kategorisine kaydedilir (asıl sembol bir sonraki kurulumda yeniden yazılırdı).
    private async Task EditSelectedSymbol()
    {
        if (SymbolList.SelectedItem is not SymbolRow row || _session.LibrarySymbol(row.Id) is not { } symbol)
        {
            return;
        }

        var editor = new SymbolEditorWindow(_session, symbol, row.Category == SymbolPalette.UserCategory);
        await editor.ShowDialog(this);
        if (editor.SavedToLibrary)
        {
            RefreshSymbolList();
        }
    }

    private void AssignSelectedSymbol()
    {
        if (_selected is { } selected && SymbolList.SelectedItem is SymbolRow row)
        {
            Apply(_session.AssignFromLibrary(selected.Placement, row.Id));
        }
    }

    // Komutların ne zaman yapılabileceği; şerit, Dosya menüsü ve kısayollar buna uyar.
    private bool CanRun(string id)
    {
        var open = _session.Project is not null;
        var selected = _selected is not null;
        return id switch
        {
            EditorCommands.NewProject or EditorCommands.Open or EditorCommands.Exit or EditorCommands.OpenLibrary or EditorCommands.ProductLibrary or EditorCommands.ImportPdf or EditorCommands.About or EditorCommands.Help or EditorCommands.ToggleLanguage => true,
            EditorCommands.NavigatorPanel or EditorCommands.MessagesPanel or EditorCommands.CollapseRibbon => true,
            EditorCommands.Focus or EditorCommands.ResetLayout or EditorCommands.Grid => true,
            EditorCommands.Undo => _session.CanUndo,
            EditorCommands.Redo => _session.CanRedo,
            EditorCommands.Properties => selected || SelectedText is not null,
            EditorCommands.TerminalStrip => StripToEdit() is not null,
            EditorCommands.EditSymbol or EditorCommands.ChangeSymbol => selected,
            EditorCommands.InsertSymbol or EditorCommands.AddText => open && _session.IsSchematicPage(ActivePage),
            EditorCommands.Delete => _selection.Count > 0 || _graphicSelection.Count > 0 || _wireSelection.Count > 0,
            EditorCommands.Rotate or EditorCommands.Copy or EditorCommands.Cut => _selection.Count > 0,
            EditorCommands.Deselect => _selection.Count > 0 || _graphicSelection.Count > 0 || _wireSelection.Count > 0 || _wireMode,
            EditorCommands.Paste => _session.CanPaste && ActivePage >= 0,
            EditorCommands.SelectAll or EditorCommands.DrawWire => ActivePage >= 0,
            EditorCommands.MacroFromSelection => selected,
            EditorCommands.InsertMacro or EditorCommands.MacroFromPage => open && ActivePage >= 0,
            EditorCommands.Fit => ActivePage >= 0,
            EditorCommands.ShowPins => open,
            EditorCommands.RecognizeGraphics => open && _session.GraphicPageIndices().Count > 0,
            EditorCommands.PageProperties => open && ActivePage >= 0,
            EditorCommands.PreviousPage => open && ActivePage > 0,
            EditorCommands.MovePageUp or EditorCommands.MovePageFirst => open && ActivePage > 0,
            EditorCommands.MovePageDown => open && ActivePage >= 0 && ActivePage < _session.PageLabels.Count - 1,
            EditorCommands.NextPage => open && ActivePage >= 0 && ActivePage < _session.PageLabels.Count - 1,
            _ => open,
        };
    }

    private bool IsChecked(string id) => id switch
    {
        EditorCommands.Grid => _gridOn,
        EditorCommands.DarkCanvas => _darkCanvas,
        EditorCommands.ShowPins => _showPins,
        EditorCommands.DrawWire => _wireMode,
        EditorCommands.NavigatorPanel => !_layout.NavigatorsHidden,
        EditorCommands.MessagesPanel => !_layout.MessagesHidden,
        EditorCommands.CollapseRibbon => Ribbon.Collapsed,
        EditorCommands.Focus => _beforeFocus is not null,
        EditorCommands.LetterScheme => _session.Project?.TagScheme == TagScheme.LetterNumber,
        EditorCommands.PageScheme => _session.Project?.TagScheme == TagScheme.PageLetterNumber,
        _ => false,
    };

    private async Task ChooseAndOpen()
    {
        if (!await ConfirmDiscard())
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Proje aç",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Proje dosyası") { Patterns = ["*.json"] }],
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            OpenProject(path);
        }
    }

    /// <summary>
    /// PDF seçtirir, arka planda okur ve açık projenin sonuna sayfa sayfa ekler. Proje açık değilse önce boş bir proje
    /// açılır. Başka bir araçtan gelen vektör PDF düzenlenebilir olarak gelir (serbest grafik, ADR-0028).
    /// </summary>
    private async Task ImportPdfAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "PDF'ten al",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        await ImportPdf(path);
    }

    /// <summary>PDF'i okuyup projeye ekler; sonucu durum satırında gösterir.</summary>
    public async Task ImportPdf(string path, bool openRecognitionDialog = true)
    {
        _notice = "PDF okunuyor…";
        ShowStatus();
        var drawing = await Task.Run(() => Ake.Library.PdfImport.Read(path));
        if (drawing.IsFailure)
        {
            _notice = drawing.Error!.Message;
            ShowStatus();
            return;
        }

        if (_session.Project is null)
        {
            var created = _session.NewProject();
            if (created.IsFailure)
            {
                _notice = created.Error!.Message;
                ShowStatus();
                return;
            }

            ShowProject();
        }

        var before = _session.PageLabels.Count;
        var imported = _session.ImportPdf(drawing.Value);
        if (imported.IsFailure)
        {
            Apply(Result.Failure(imported.Error!));
            return;
        }

        SyncPageList(-1, _session.PageLabels);
        _lastPageLabels = _session.PageLabels;
        OpenPage(before);
        Apply(Result.Success());
        _notice = $"PDF alındı: {imported.Value} sayfa ({drawing.Value.Pages.Sum(page => page.Shapes.Count)} grafik)";
        ShowStatus();

        if (openRecognitionDialog && IsVisible)
        {
            _notice = "PDF alındı; cihaz ve bağlantılar taranıyor…";
            ShowStatus();
            var recognized = await Task.Run(() => _session.RecognizeAllGraphicPages());
            if (recognized.IsSuccess && recognized.Value.Any(r => r.Devices.Count > 0))
            {
                await ShowGraphicRecognition(startWithAllPages: true, precomputed: recognized.Value);
            }
        }
    }

    /// <summary>DXF/DWG/.dxf.zip çizim dosyası seçtirip düzenlenebilir serbest grafik sayfaları olarak içe aktarır.</summary>
    private async Task ImportDxfAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "DXF / DWG çizim al",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("CAD çizimleri") { Patterns = ["*.dxf", "*.dxf.zip", "*.dwg"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        _notice = "DXF/DWG okunuyor…";
        ShowStatus();
        var drawing = await Task.Run(() => Ake.Library.DxfPageImport.Read(path));
        if (drawing.IsFailure)
        {
            _notice = drawing.Error!.Message;
            ShowStatus();
            return;
        }

        if (_session.Project is null)
        {
            var created = _session.NewProject();
            if (created.IsFailure)
            {
                _notice = created.Error!.Message;
                ShowStatus();
                return;
            }

            ShowProject();
        }

        var before = _session.PageLabels.Count;
        var imported = _session.ImportDxf(drawing.Value);
        if (imported.IsFailure)
        {
            Apply(Result.Failure(imported.Error!));
            return;
        }

        SyncPageList(-1, _session.PageLabels);
        _lastPageLabels = _session.PageLabels;
        OpenPage(before);
        Apply(Result.Success());
        _notice = $"DXF alındı: {imported.Value} sayfa ({drawing.Value.Pages.Sum(page => page.Shapes.Count)} grafik)";
        ShowStatus();
    }

    /// <summary>SEE Electrical Excel/CSV mühendislik listesi seçtirip modele içe aktarır.</summary>
    private async Task ImportSeeListAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "SEE Electrical listesi içe aktar (.xlsx / .csv)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Mühendislik listeleri") { Patterns = ["*.xlsx", "*.csv"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        if (_session.Project is null)
        {
            var created = _session.NewProject();
            if (created.IsFailure)
            {
                Apply(created);
                return;
            }

            ShowProject();
        }

        var imported = _session.ImportSeeList(path);
        if (imported.IsFailure)
        {
            Apply(Result.Failure(imported.Error!));
            return;
        }

        Apply(Result.Success());
        var s = imported.Value;
        _notice = $"Liste alındı: +{s.DevicesAdded} cihaz, +{s.ChannelsAdded} I/O kanalı, +{s.TerminalsAdded} klemens";
        ShowStatus();
    }

    private void ToggleDarkCanvas()
    {
        _darkCanvas = !_darkCanvas;
        foreach (var (_, view) in _layout.OpenViews)
        {
            view.DarkCanvas = _darkCanvas;
        }

        UpdateCommands();
    }

    private void ToggleShowPins()
    {
        _showPins = !_showPins;
        foreach (var (page, view) in _layout.OpenViews)
        {
            ShowPinMarkers(page, view);
        }

        UpdateCommands();
        _notice = _showPins ? "Tel uçları (bağlantı noktaları) görünür." : "Tel uçları yalnızca tel çekme modunda gösterilir.";
        ShowStatus();
    }

    private async Task ShowGraphicRecognition(
        bool startWithAllPages = false,
        IReadOnlyList<Ake.Library.PageRecognitionResult>? precomputed = null)
    {
        if (_session.Project is null || _session.GraphicPageIndices().Count == 0)
        {
            _notice = "Bu projede taranacak serbest grafik bulunamadı.";
            ShowStatus();
            return;
        }

        var window = new GraphicRecognitionWindow(
            _session, ActivePage, EnsureDefaultLibraryOpen, startWithAllPages, precomputed);
        await window.ShowDialog<bool>(this);

        if (window.LibraryChanged)
        {
            RefreshSymbolList();
        }

        if (window.ModelChanged)
        {
            _showPins = true;
            Apply(Result.Success());
            if (window.LastMessage is { Length: > 0 } msg)
            {
                _notice = msg;
                ShowStatus();
            }
        }
        else if (window.LastMessage is { Length: > 0 } msg)
        {
            _notice = msg;
            ShowStatus();
        }
    }

    /// <summary>Kaydeder; yeni projede dosya sorar. Kaydedildiyse <c>true</c>.</summary>
    private async Task<bool> SaveAsync() =>
        _session.IsUntitled ? await SaveAsAsync() : Saved(_session.Save());

    /// <summary>Dosya seçtirip oraya kaydeder; seçim iptal edilirse <c>false</c>.</summary>
    private async Task<bool> SaveAsAsync()
    {
        if (_session.Project is null)
        {
            return false;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Farklı kaydet",
            SuggestedFileName = _session.ProjectPath is { } current ? Path.GetFileName(current) : $"{_session.Project.Name}.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Proje dosyası") { Patterns = ["*.json"] }],
        });
        return file?.TryGetLocalPath() is { } path && Saved(_session.SaveAs(path));
    }

    private bool Saved(Result saved)
    {
        if (saved.IsSuccess)
        {
            var baseDir = _session.ProjectDirectory;
            foreach (var (_, view) in _layout.OpenViews)
            {
                view.ImageBaseDirectory = baseDir;
            }
        }

        _notice = saved.IsSuccess ? "Kaydedildi" : saved.Error!.Message;
        UpdateCommands();
        ShowStatus();
        return saved.IsSuccess;
    }

    private void SaveLayout()
    {
        if (LayoutPath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            File.WriteAllText(LayoutPath, _layout.Save());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Düzen kaydedilemezse uygulama yine kapanır; bir sonraki açılışta varsayılan düzen gelir.
            Serilog.Log.Warning(ex, "Panel düzeni kaydedilemedi");
        }
    }

    private void RotateSelected()
    {
        if (_selection.Count > 0)
        {
            Apply(_session.RotateMany(SelectedPlacements));
        }
    }

    // Tek bir serbest yazı seçiliyse onun kimliği (Enter ve çift tık metni düzenler).
    private GraphicId? SelectedText =>
        _selected is null && _graphicSelection is [{ } only] && _session.TextOf(only.Graphic) is not null ? only.Graphic : null;

    private async Task EditSelectedText()
    {
        if (SelectedText is not { } id)
        {
            return;
        }

        var text = await new TextPromptDialog("Yazıyı düzenle", _session.TextOf(id)!).ShowDialog<string?>(this);
        if (text is not null)
        {
            Apply(_session.SetGraphicText(id, text));
        }
    }

    private async Task ShowProperties()
    {
        if (SelectedText is not null)
        {
            await EditSelectedText();
            return;
        }

        if (_selected is not { } selected || _session.EditorDataOf(selected.Placement) is not { } data)
        {
            return;
        }

        var edits = await new DevicePropertiesWindow(data, _session, applied => Apply(_session.ApplyDeviceEdits(applied))).ShowDialog<DeviceEdits?>(this);
        if (edits is not null)
        {
            Apply(_session.ApplyDeviceEdits(edits));
        }
    }

    private void EnsureDefaultLibraryOpen()
    {
        if (_session.LibraryPath is not null)
        {
            return;
        }

        var library = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "aktapokus-electric",
            "library.db");
        Directory.CreateDirectory(Path.GetDirectoryName(library)!);
        var installed = Ake.Library.StandardSymbols.EnsureInstalled(library);
        if (installed.IsSuccess || File.Exists(library))
        {
            OpenLibrary(library);
        }
    }

    private async Task InsertSymbolAt(int page, double? atX, double? atY)
    {
        if (!_session.IsSchematicPage(page) || _session.DrawingAreaFor(page) is not { } area)
        {
            _notice = "Sembol yalnızca şema sayfasına eklenebilir.";
            ShowStatus();
            return;
        }

        EnsureDefaultLibraryOpen();
        var selector = new SymbolSelectionWindow(_session);
        var choice = await selector.ShowDialog<SymbolSelectionChoice?>(this);
        if (selector.Changed)
        {
            RefreshSymbolList();
            ShowStatus();
        }

        if (choice is null)
        {
            return;
        }

        var (x, y) = atX is { } px && atY is { } py
            ? (px, py)
            : (area.X + (area.Width / 2), area.Y + (area.Height / 2));
        var placed = _session.PlaceLibrarySymbol(choice.SymbolId, page, x, y, choice.RotationDeg);
        if (placed.IsFailure)
        {
            Apply(Result.Failure(placed.Error!));
            return;
        }

        Apply(Result.Success());
        Select(_session.BoxesFor(page).FirstOrDefault(box => box.Placement == placed.Value));
        await ShowProperties();
    }

    private async Task ChangeSelectedSymbol()
    {
        if (_selected is not { } selected || _session.FunctionOf(selected.Placement) is not { } function)
        {
            return;
        }

        EnsureDefaultLibraryOpen();
        var selector = new SymbolSelectionWindow(
            _session,
            requiredPins: function.ConnectionPoints.Count,
            initialSymbol: function.SymbolId,
            showVariant: false);
        var choice = await selector.ShowDialog<SymbolSelectionChoice?>(this);
        if (selector.Changed)
        {
            RefreshSymbolList();
            ShowStatus();
        }

        if (choice is not null)
        {
            Apply(_session.AssignFromLibrary(selected.Placement, choice.SymbolId));
        }
    }

    private async Task OpenVisualSymbolPicker()
    {
        if (_session.IsSchematicPage(ActivePage))
        {
            await InsertSymbolAt(ActivePage, null, null);
            return;
        }

        EnsureDefaultLibraryOpen();
        var selector = new SymbolSelectionWindow(_session, showVariant: false);
        var choice = await selector.ShowDialog<SymbolSelectionChoice?>(this);
        if (selector.Changed)
        {
            RefreshSymbolList();
        }

        if (choice is not null)
        {
            var scene = _session.PreviewOf(choice.SymbolId);
            SymbolPreview.Scene = scene;
            SymbolPreview.GridArea = scene is null ? null : new Box(0, 0, scene.WidthMm, scene.HeightMm);
            _notice = "Sembol seçildi (yerleştirmek için bir şema sayfası açın)";
        }

        ShowStatus();
    }

    private async Task AddTextAt(int page, double? atX, double? atY)
    {
        if (!_session.IsSchematicPage(page) || _session.DrawingAreaFor(page) is not { } area)
        {
            _notice = "Metin yalnızca şema sayfasına eklenebilir.";
            ShowStatus();
            return;
        }

        var text = await new TextPromptDialog("Metin ekle", string.Empty).ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var (x, y) = atX is { } px && atY is { } py
            ? (px, py)
            : (area.X + (area.Width / 2), area.Y + (area.Height / 2));
        var added = _session.AddFreeText(page, x, y, text);
        if (added.IsFailure)
        {
            Apply(Result.Failure(added.Error!));
            return;
        }

        Apply(Result.Success());
        var box = _session.GraphicBoxesFor(page).FirstOrDefault(g => g.Graphic == added.Value);
        if (box is not null)
        {
            SetSelection([], null, [box]);
        }
    }

    /// <summary>Blok editörünü açar: seçili bir sembol varsa onun function'ı için, yoksa boş.</summary>
    private async Task OpenSymbolEditor(PlacedBox? from)
    {
        if (_session.Project is null)
        {
            return;
        }

        var function = from is null ? null : _session.FunctionOf(from.Placement);
        var editor = new SymbolEditorWindow(_session, function);
        await editor.ShowDialog(this);
        if (editor.Saved)
        {
            Apply(Result.Success());
        }
    }

    private void OnShortcut(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox)
        {
            return;
        }

        // Tel çekerken Backspace son köşe noktasını geri alır.
        if (_wireMode && e.Key == Key.Back && _wirePoints.Count > 0)
        {
            _wirePoints.RemoveAt(_wirePoints.Count - 1);
            Canvas.PreviewMarkers = [.. _wirePoints.Select(point => (point.X, point.Y))];
            e.Handled = true;
            return;
        }

        // Kısayollar komut tanımlarından gelir (EditorCommands); yapılamayan komut tuşu yutmaz.
        foreach (var (command, gesture) in _gestures)
        {
            if (gesture.Matches(e) && Ribbon.Run(command))
            {
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Bir değişikliğin sonucunu gösterir: bütün açık sayfalar (çapraz referanslar başka sayfayı da
    /// değiştirebilir) ve navigator'lar yenilenir, seçim korunur.
    /// </summary>
    private void Apply(Result result)
    {
        _notice = result.IsFailure ? result.Error!.Message : null;

        // Sayfa listesinde seçili sayfanın önizlemesi düzenlemeyle güncel kalır.
        ShowPagePreview();

        // Sayfa eklendi/numarası değişti/geri alındı: sayfa listesi ve açık sekme başlıkları yeniden kurulur.
        var labels = _session.PageLabels;
        if (_lastPageLabels is null || !labels.SequenceEqual(_lastPageLabels))
        {
            _lastPageLabels = labels;
            _layout.SyncPageTitles(labels);
            SyncPageList(ActivePage, labels);
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var (page, view) in _layout.OpenViews)
        {
            var scene = _session.SceneFor(page, today);
            view.UpdateScene(scene.IsSuccess ? scene.Value : null);
            ShowPinMarkers(page, view);
        }

        // Seçim korunur: hâlâ sayfada duran semboller seçili kalır (silinenler düşer).
        var boxes = _session.BoxesFor(ActivePage);
        var graphicBoxes = _session.GraphicBoxesFor(ActivePage);
        SetSelection(
            [.. boxes.Where(box => _selection.Any(s => s.Placement == box.Placement))],
            boxes.FirstOrDefault(box => box.Placement == _selected?.Placement),
            [.. graphicBoxes.Where(box => _graphicSelection.Any(s => s.Graphic == box.Graphic))],
            _wireSelection.Count == 0 ? null : [.. _session.WiresFor(ActivePage).Where(wire => _wireSelection.Any(s => s.Connection == wire.Connection))]);
        RefreshNavigators();
        ShowStatus();
    }

    private IReadOnlyCollection<PlacementId> SelectedPlacements => [.. _selection.Select(box => box.Placement)];

    private IReadOnlyCollection<GraphicId> SelectedGraphics => [.. _graphicSelection.Select(box => box.Graphic)];

    private IReadOnlyCollection<ConnectionId> SelectedWires => [.. _wireSelection.Select(wire => wire.Connection)];

    /// <summary>
    /// Sayfaya bırakılan: Cihazlar navigator'ından bir cihaz (yerleştirilmemiş ilk function'ı), Semboller
    /// panelinden bir kütüphane sembolü (yeni cihaz) ya da Gezgin'den makro dosyası. Bırakılan seçilir.
    /// </summary>
    private void Drop(int page, IDataTransfer data, double x, double y)
    {
        OpenPage(page);
        if (data.TryGetValue(ProductDragFormat) is { } productDrag)
        {
            var placed = _session.PlaceLibraryProduct(productDrag.OrderCode, productDrag.Manufacturer, page, x, y);
            if (placed.IsFailure)
            {
                Apply(Result.Failure(placed.Error!));
                return;
            }

            Apply(Result.Success());
            if (placed.Value.Count > 0)
            {
                var firstId = placed.Value[0];
                Select(_session.BoxesFor(page).FirstOrDefault(box => box.Placement == firstId));
            }

            return;
        }

        if (data.TryGetValue(PayloadFormat) is { } payload)
        {
            var placed = payload.Device is { } device
                ? _session.PlaceNextFunction(device, page, x, y)
                : _session.PlaceLibrarySymbol(payload.Symbol!.Value, page, x, y);
            if (placed.IsFailure)
            {
                Apply(Result.Failure(placed.Error!));
                return;
            }

            Apply(Result.Success());
            Select(_session.BoxesFor(page).FirstOrDefault(box => box.Placement == placed.Value));
            return;
        }

        var macros = (data.TryGetFiles() ?? [])
            .Select(file => file.TryGetLocalPath())
            .OfType<string>()
            .Where(path => path.EndsWith(".makro.json", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (macros.Count == 0)
        {
            _notice = "Yalnızca makro dosyası (*.makro.json) bırakılabilir.";
            ShowStatus();
            return;
        }

        foreach (var path in macros)
        {
            Apply(_session.InsertMacroAt(path, page, x, y));
        }
    }

    // Listeden sürükleme: basılıp birkaç piksel çekilince satırın taşıdığı şeyle sürükleme başlar.
    private static void EnableDrag(Control list, Func<object?, DragPayload?> payloadOf)
    {
        PointerPressedEventArgs? pressed = null;
        var start = default(Avalonia.Point);
        list.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(list).Properties.IsLeftButtonPressed)
            {
                pressed = e;
                start = e.GetPosition(list);
            }
        }, RoutingStrategies.Tunnel);
        list.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);
        list.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } args)
            {
                return;
            }

            var position = e.GetPosition(list);
            if (Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 6)
            {
                return;
            }

            pressed = null;
            if (payloadOf((args.Source as Avalonia.StyledElement)?.DataContext) is not { } payload)
            {
                return;
            }

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(PayloadFormat, payload));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy);
        }, RoutingStrategies.Tunnel);
    }

    // Sayfalar listesinde sürükle-bırak ile sıra değiştirme (bırakınca kullanıcıdan onay ister).
    private void EnablePageDragAndDrop()
    {
        DragDrop.SetAllowDrop(PageTree, true);
        PointerPressedEventArgs? pressed = null;
        var start = default(Avalonia.Point);

        PageTree.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(PageTree).Properties.IsLeftButtonPressed
                && FindPageRow(e.Source) is { IsGroup: false, PageIndex: not null })
            {
                pressed = e;
                start = e.GetPosition(PageTree);
            }
            else
            {
                pressed = null;
            }
        }, RoutingStrategies.Tunnel);

        PageTree.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);

        PageTree.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } args)
            {
                return;
            }

            if (!e.GetCurrentPoint(PageTree).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            var position = e.GetPosition(PageTree);
            if (Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 6)
            {
                return;
            }

            pressed = null;
            if (FindPageRow(args.Source) is not { IsGroup: false, PageIndex: { } fromIndex })
            {
                return;
            }

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(PageDragFormat, new PageDragPayload(fromIndex)));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
        }, RoutingStrategies.Tunnel);

        PageTree.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var valid = e.DataTransfer.TryGetValue(PageDragFormat) is { } dragged
                && FindPageRow(e.Source) is { IsGroup: false, PageIndex: { } toIndex }
                && toIndex != dragged.PageIndex;
            e.DragEffects = valid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        });

        PageTree.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.DataTransfer.TryGetValue(PageDragFormat) is not { } dragged
                || FindPageRow(e.Source) is not { IsGroup: false, PageIndex: { } toIndex }
                || toIndex == dragged.PageIndex)
            {
                return;
            }

            var fromIndex = dragged.PageIndex;
            e.Handled = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(() => _ = ConfirmAndMovePageAsync(fromIndex, toIndex));
        });
    }

    private async Task ConfirmAndMovePageAsync(int from, int to)
    {
        if (_session.Project is null
            || from == to
            || from < 0
            || to < 0
            || from >= _session.PageLabels.Count
            || to >= _session.PageLabels.Count)
        {
            return;
        }

        var source = _session.PageLabels[from];
        var target = _session.PageLabels[to];
        var side = to < from ? "önüne" : "arkasına";
        var question = $"'{source}' sayfası '{target}' sayfasının {side} taşınacak.\n\nYer değişikliğini onaylıyor musunuz?";
        var confirmed = await new ConfirmDialog("Sayfa yer değişikliği", question).ShowDialog<bool>(this);
        if (confirmed)
        {
            MovePage(from, to);
        }
    }

    private static PageTreeRow? FindPageRow(object? source)
    {
        for (var current = source as Avalonia.StyledElement; current is not null; current = current.Parent)
        {
            if (current.DataContext is PageTreeRow row)
            {
                return row;
            }
        }

        return null;
    }

    private void CopySelection()
    {
        var copied = _session.Copy(ActivePage, SelectedPlacements);
        _notice = copied.IsSuccess ? $"{_selection.Count} sembol kopyalandı" : copied.Error!.Message;
        UpdateCommands();
        ShowStatus();
    }

    // Yapıştırma imlecin son durduğu yere; imleç sayfada hiç olmadıysa kopyalanan yerin sağ altına.
    private void PasteClipboard()
    {
        var pasted = _session.Paste(ActivePage, _pointer?.X, _pointer?.Y);
        if (pasted.IsFailure)
        {
            Apply(Result.Failure(pasted.Error!));
            return;
        }

        Apply(Result.Success());
        var boxes = _session.BoxesFor(ActivePage);
        SetSelection([.. boxes.Where(box => pasted.Value.Contains(box.Placement))], null);
        _notice = $"{pasted.Value.Count} sembol yapıştırıldı (yeni tag'lerle)";
        ShowStatus();
    }

    // Tel çekme modunda sayfadaki bütün bağlanabilir uçlar halka olarak görünür; mod kapalıyken hiçbiri çizilmez.
    private void ShowPinMarkers(int page, SceneView view)
    {
        view.PinMarkers = (_wireMode || _showPins) ? [.. _session.PinsFor(page).Select(pin => (pin.X, pin.Y))] : [];
        view.HoverPin = null;
    }

    private void ToggleWireMode()
    {
        _wireMode = !_wireMode;
        ResetWireDraft();
        foreach (var (page, view) in _layout.OpenViews)
        {
            ShowPinMarkers(page, view);
        }

        _notice = _wireMode ? "Tel çek: ilk uca tıklayın; boş yerlere tıklayarak köşe noktası verin, ikinci uca tıklayıp bitirin. Bir telin üzerine tıklamak orada kavşak koyar. Bitirmek için W ya da Esc." : null;
        UpdateCommands();
        ShowStatus();
    }

    // Yarım kalmış teli (ilk uç, köşe noktaları, yeniden yönlendirme, önizleme) bırakır.
    private void ResetWireDraft()
    {
        _wireFrom = null;
        _wirePoints.Clear();
        _reroute = null;
        foreach (var (_, view) in _layout.OpenViews)
        {
            view.PreviewPath = null;
            view.PreviewMarkers = [];
        }
    }

    // Tel çekme: ilk tık ucu tutar; boş yerlere tıklamak köşe noktası verir (yapışma adımına oturur); ikinci uca tıklamak
    // bağlar. Mod açık kalır, sıradaki tel çekilebilir. Backspace son köşe noktasını geri alır.
    private void WireClick(int page, double x, double y)
    {
        var pin = _session.PinAt(page, x, y, 12 * Canvas.MmPerPixel);

        // Bir telin üzerine tıklamak orada kavşak koyar (ADR-0032): tel ikiye bölünür, kavşak tıklanan uç gibi sayılır. Yeniden
        // yönlendirirken uçlar sabittir, kavşak konmaz.
        if (pin is null && _reroute is null && _session.WireAt(page, x, y) is { } hitWire)
        {
            var split = _session.SplitWire(page, hitWire, x, y);
            Apply(split.IsSuccess ? Result.Success() : Result.Failure(split.Error!));
            if (split.IsFailure)
            {
                return;
            }

            pin = split.Value;
        }

        if (_wireFrom is not { } from)
        {
            if (pin is null)
            {
                _notice = "Burada bağlantı noktası yok; bir sembolün ucuna tıklayın.";
                ShowStatus();
                return;
            }

            _wireFrom = pin;
            _notice = $"Tel: {pin.Name} → köşe noktaları için boş yere, bitirmek için ikinci uca tıklayın";
            ShowStatus();
            return;
        }

        if (pin is null)
        {
            var (sx, sy) = EditorSession.Snap(x, y);
            _wirePoints.Add(new WirePoint(sx, sy));
            Canvas.PreviewMarkers = [.. _wirePoints.Select(point => (point.X, point.Y))];
            _notice = $"Köşe noktası eklendi ({_wirePoints.Count}). Bitirmek için telin ucuna tıklayın; Backspace son noktayı siler.";
            ShowStatus();
            return;
        }

        var route = _wirePoints.ToList();
        if (_reroute is { } reroute)
        {
            if (pin.Point != reroute.To)
            {
                _notice = "Yeniden yönlendirme: bitirmek için telin diğer ucuna tıklayın.";
                ShowStatus();
                return;
            }

            ResetWireDraft();
            _wireMode = false;
            Apply(_session.SetWireRoute(reroute.Id, route));
            _notice ??= route.Count == 0 ? "Tel otomatik yönlendirmeye döndü" : "Tel yönlendirildi";
            ShowStatus();
            return;
        }

        ResetWireDraft();
        Apply(_session.ConnectPins(from.Point, pin.Point, route));
        if (_notice is null)
        {
            _notice = $"Tel çekildi: {from.Name} – {pin.Name}";
            ShowStatus();
        }
    }

    // Seçili teli yeniden yönlendirir: tel çekme gibi, ama uçlar sabit. Köşe noktalarına tıklanır, bitirmek için telin diğer ucuna.
    private void StartReroute(int page, WirePath wire)
    {
        if (_session.EndsOf(page, wire.Connection) is not { } ends)
        {
            return;
        }

        _wireMode = true;
        ResetWireDraft();
        foreach (var (openPage, openView) in _layout.OpenViews)
        {
            ShowPinMarkers(openPage, openView);
        }

        _wireFrom = ends.From;
        _reroute = (wire.Connection, ends.To.Point);
        _notice = $"Yeniden yönlendir: köşe noktaları için boş yere tıklayın, bitirmek için {ends.To.Name} ucuna tıklayın (Esc: vazgeç).";
        UpdateCommands();
        ShowStatus();
    }

    /// <summary>Navigator ya da mesaj satırının gösterdiği sembole gider: sayfası açılır, sembol seçilip ortalanır.</summary>
    private void GoTo(NavigatorRow? row)
    {
        if (row?.PageIndex is not { } pageIndex)
        {
            return;
        }

        OpenPage(pageIndex);
        var box = _session.BoxesFor(pageIndex).FirstOrDefault(candidate => candidate.Placement == row.Placement);
        Select(box);
        if (box is not null)
        {
            Canvas.CenterOn(box.Bounds);
        }

        Canvas.Focus();
    }

    private void StepPage(int step) => OpenPage(ActivePage + step);

    private void RefreshNavigators()
    {
        DeviceList.ItemsSource = _session.DeviceRows();
        PotentialList.ItemsSource = _session.PotentialRows();
        TerminalList.ItemsSource = _session.TerminalRows();
        PlcList.ItemsSource = _session.PlcRows();
        CableList.ItemsSource = _session.CableRows();
        var findings = _session.FindingRows();
        MessageList.ItemsSource = findings;
        _layout.SetTitle(EditorLayout.MessagesId, findings.Count == 0 ? UiStrings.T("Mesajlar") : $"{UiStrings.T("Mesajlar")} ({findings.Count})");
        ErcText.Text = _session.Project is null ? string.Empty : findings.Count == 0 ? UiStrings.T("ERC temiz") : $"ERC: {findings.Count} {UiStrings.T("bulgu")}";
        UpdateCommands();
    }

    private void UpdateCommands() => Ribbon.Refresh();

    private void Select(PlacedBox? box) => SetSelection(box is null ? [] : [box], box);

    /// <summary>
    /// Seçimi değiştirir. <paramref name="primary"/> özellikler ve blok editörü için tek sembol; verilmezse
    /// seçimdeki son sembol.
    /// </summary>
    private void SetSelection(
        IReadOnlyList<PlacedBox> boxes, PlacedBox? primary, IReadOnlyList<GraphicBox>? graphics = null, IReadOnlyList<WirePath>? wires = null)
    {
        var selection = boxes.ToList();
        var main = primary is not null && selection.Any(box => box.Placement == primary.Placement) ? primary : selection.LastOrDefault();
        var changed = main?.Placement != _selected?.Placement;
        foreach (var (_, view) in _layout.OpenViews)
        {
            view.Highlights = [];
            view.WireHighlights = [];
        }

        _selection = selection;
        _selected = main;
        _graphicSelection = [.. graphics ?? []];
        _wireSelection = [.. wires ?? []];
        Canvas.WireHighlights = [.. _wireSelection.Select(wire => wire.Path)];
        Canvas.Highlights = [.. selection.Select(box => box.Bounds), .. _graphicSelection.Select(box => box.Bounds)];
        UpdateCommands();
        if (changed && FitOnly.IsChecked == true && _session.LibraryPath is not null)
        {
            RefreshSymbolList();
        }
    }

    // Panel komutu çalışır, sonra şerit durumu yenilenir (iğneli paneller düğmede basılı görünmez).
    private void Panels(Action change)
    {
        change();
        UpdateCommands();
    }

    // Tam çizim alanı: paneller kenara iğnelenir, şerit daralır; ikinci kez basınca önceki hâline döner.
    private void ToggleFocus()
    {
        if (_beforeFocus is { } before)
        {
            _beforeFocus = null;
            Ribbon.Collapsed = before.Ribbon;
            _layout.SetNavigatorsHidden(!before.Navigators);
            _layout.SetMessagesHidden(!before.Messages);
        }
        else
        {
            _beforeFocus = (!_layout.NavigatorsHidden, !_layout.MessagesHidden, Ribbon.Collapsed);
            Ribbon.Collapsed = true;
            _layout.SetNavigatorsHidden(true);
            _layout.SetMessagesHidden(true);
        }

        UpdateCommands();
    }

    private void ShowGrid()
    {
        foreach (var (page, view) in _layout.OpenViews)
        {
            view.GridArea = _gridOn && _session.IsSchematicPage(page) ? _session.DrawingAreaFor(page) : null;
        }

        GridText.Text = _gridOn ? "Izgara açık" : "Izgara kapalı";
        UpdateCommands();
    }

    // Sekme değişti: seçim bırakılır, sayfa listesi eşlenir, sayfanın uyarıları gösterilir.
    private void ShowActivePage()
    {
        // Yarım kalmış tel başka sayfaya taşınmaz.
        ResetWireDraft();

        Select(null);
        SyncPageList(ActivePage, null);
        var scene = Canvas.Scene;
        _notice = ActivePage >= 0 && scene is null && _session.Project is not null
            ? _session.SceneFor(ActivePage, DateOnly.FromDateTime(DateTime.Now)).Error?.Message
            : scene is { Warnings.Count: > 0 }
                ? string.Join("  ·  ", scene.Warnings)
                : null;
        UpdateCommands();
        ShowStatus();
    }

    // Sayfa ağacının seçimi sekmeyi izler; bu değişiklik yeniden sekme açtırmaz. <paramref name="items"/> verilirse
    // ağaç (süzgeçle) yeniden kurulur.
    private void SyncPageList(int index, IReadOnlyList<string>? items)
    {
        _syncingPageList = true;
        try
        {
            if (items is not null)
            {
                var field = EditorSession.PageFilterFields[Math.Max(0, PageFilterFieldBox.SelectedIndex)].Field;
                PageTree.ItemsSource = _pageListMode
                    ? _session.PageListRows(PageFilter.Text, field)
                    : _session.PageTreeRows(PageFilter.Text, _collapsedGroups, field);
            }

            PageTree.SelectedItem = (PageTree.ItemsSource as IReadOnlyList<PageTreeRow>)?.FirstOrDefault(row => row.PageIndex == index);
        }
        finally
        {
            _syncingPageList = false;
        }
    }

    // Sayfa listesinin en son kurulduğu andaki satırlar: değişim Apply'da fark edilir.
    private IReadOnlyList<string>? _lastPageLabels;

    // Liste görünümü mü (düz, projedeki sırayla) yoksa ağaç görünümü mü.
    private bool _pageListMode;

    private void SetPageListMode(bool list)
    {
        _pageListMode = list;
        PageTreeModeButton.IsChecked = !list;
        PageListModeButton.IsChecked = list;
        SyncPageList(ActivePage, _session.PageLabels);
    }

    // Sayfa listesinde seçili sayfanın küçük resmi; seçim yoksa ya da grup satırıysa boş.
    private void ShowPagePreview()
    {
        if (PageTree.SelectedItem is PageTreeRow { PageIndex: { } index } && _session.Project is not null
            && _session.SceneFor(index, DateOnly.FromDateTime(DateTime.Now)) is { IsSuccess: true } scene)
        {
            PagePreview.Scene = scene.Value;
            return;
        }

        PagePreview.Scene = null;
    }

    // Katlanmış grupların anahtarları (EditorSession.PageTreeRows); varsayılan hepsi açık.
    private readonly HashSet<string> _collapsedGroups = [];

    private void ToggleGroup(PageTreeRow group)
    {
        if (!_collapsedGroups.Remove(group.Key))
        {
            _collapsedGroups.Add(group.Key);
        }

        SyncPageList(ActivePage, _session.PageLabels);
        _syncingPageList = true;
        try
        {
            PageTree.SelectedItem = (PageTree.ItemsSource as IReadOnlyList<PageTreeRow>)?.FirstOrDefault(row => row.Key == group.Key);
        }
        finally
        {
            _syncingPageList = false;
        }
    }

    // İlk bölme: önce bildirim (varsa), sonra proje özeti. İmleç, ızgara ve ERC kendi bölmelerinde.
    private void ShowStatus()
    {
        StatusText.Text = string.Join(
            "  ·  ", new[] { _notice, _session.Status }.Where(part => !string.IsNullOrEmpty(part)));
        Title = WindowTitle(_session);
    }

    /// <summary>Pencere başlığı: kaydedilmemiş değişiklik "●" ile, dosyası olmayan proje dosya adı yerine "kaydedilmedi" ile.</summary>
    internal static string WindowTitle(EditorSession session) => session.Project is not { } project
        ? "aktapokus-electric"
        : $"{(session.IsDirty ? "● " : string.Empty)}{project.Name} — {session.ProjectPath ?? UiStrings.T("kaydedilmedi")} — aktapokus-electric";
}

/// <summary>Sürükle-bırakın taşıdığı: navigator'daki bir cihaz ya da kütüphanedeki bir sembol.</summary>
internal sealed record DragPayload(DeviceId? Device, SymbolId? Symbol);
