using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Avalonia.Json;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace Ake.Editor;

/// <summary>
/// Editörün taşınabilir panel düzeni (Dock.Avalonia, MIT; ADR-0024). Solda navigator'lar, ortada
/// sayfa sekmeleri, altta mesajlar. Paneller sürüklenip başka kenara ya da birbirinin yanına bırakılır,
/// kenara iğnelenip otomatik gizlenir; düzen kullanıcı profiline kaydedilir. Her açık sayfanın kendi
/// çizim alanı (<see cref="SceneView"/>) vardır.
/// </summary>
internal sealed class EditorLayout
{
    public const string PagesId = "sayfalar";
    public const string DevicesId = "cihazlar";
    public const string PotentialsId = "potansiyeller";
    public const string TerminalsId = "klemensler";
    public const string PlcId = "plc";
    public const string CablesId = "kablolar";
    public const string SymbolsId = "semboller";
    public const string MessagesId = "mesajlar";
    private const string DocumentsId = "sayfa-sekmeleri";
    private const string PagePrefix = "sayfa:";
    private const string LeftId = "sol";
    private const string CenterId = "orta";
    private const string MainId = "ana";
    private const string BottomId = "alt";
    private const double LeftProportion = 0.2;
    private const double CenterProportion = 1.0 - LeftProportion;
    private const double BottomProportion = 0.26;
    private const double DocumentsProportion = 1.0 - BottomProportion;
    private const double MinDocumentProportion = 0.15;
    private const double MaxBottomProportion = 0.85;
    private const double MinCenterProportion = 0.25;
    private const double MaxLeftProportion = 0.75;
    private const double MinDocumentHeightPx = 160;
    private const double MinCenterWidthPx = 240;

    // Solda sayfa ve cihaz gezgini, sembol seçici; altta mesajlar ve geniş listeler (potansiyel, klemens, PLC, kablo).
    private static readonly string[] NavigatorIds = [PagesId, DevicesId, SymbolsId];
    private static readonly string[] BottomIds = [MessagesId, PotentialsId, TerminalsId, PlcId, CablesId];

    private readonly Dictionary<string, (string Title, Control Content)> _tools;
    private readonly Func<int, SceneView> _createView;
    private readonly Dictionary<int, (Document Document, SceneView View)> _pages = [];
    private Factory _factory = null!;
    private IRootDock _root = null!;
    private DocumentDock _documents = null!;

    public EditorLayout(
        Control pages,
        Control devices,
        Control potentials,
        Control terminals,
        Control plc,
        Control cables,
        Control symbols,
        Control messages,
        Func<int, SceneView> createView)
    {
        _tools = new Dictionary<string, (string, Control)>
        {
            [PagesId] = ("Sayfalar", pages),
            [DevicesId] = ("Cihazlar", devices),
            [PotentialsId] = ("Potansiyeller", potentials),
            [TerminalsId] = ("Klemensler", terminals),
            [PlcId] = ("PLC", plc),
            [CablesId] = ("Kablolar", cables),
            [SymbolsId] = ("Semboller", symbols),
            [MessagesId] = ("Mesajlar", messages),
        };
        _createView = createView;
        Control = new DockControl();
        Reset();
    }

    public DockControl Control { get; }

    /// <summary>Son yüklenen düzende çökük/bozuk oranlar onarıldıysa <c>true</c>.</summary>
    public bool LastLoadRepaired { get; private set; }

    /// <summary>Etkin sayfa sekmesinin sayfa sırası; açık sayfa yoksa -1.</summary>
    public int ActivePage { get; private set; } = -1;

    /// <summary>Etkin sayfanın çizim alanı; açık sayfa yoksa <c>null</c>.</summary>
    public SceneView? ActiveView => _pages.TryGetValue(ActivePage, out var page) ? page.View : null;

    /// <summary>Açık sayfaların çizim alanları.</summary>
    public IEnumerable<(int Page, SceneView View)> OpenViews => _pages.Select(entry => (entry.Key, entry.Value.View));

    /// <summary>Kullanıcı başka bir sayfa sekmesine geçti ya da sekme kapandı.</summary>
    public event Action<int>? ActivePageChanged;

    /// <summary>Sayfayı sekmede açar; zaten açıksa öne getirir.</summary>
    public void OpenPage(int index, string title)
    {
        if (!_pages.TryGetValue(index, out var page))
        {
            if (!_factory.Find(_root, dockable => ReferenceEquals(dockable, _documents)).Any())
            {
                Reset();
            }

            var view = _createView(index);
            var document = new Document { Id = PagePrefix + index, Title = title, Content = view, Context = index, CanFloat = false };
            page = (document, view);
            _pages[index] = page;
            _factory.AddDockable(_documents, document);
        }

        _factory.SetActiveDockable(page.Document);
        _factory.SetFocusedDockable(_documents, page.Document);
        _documents.IsEmpty = false;
        RestoreProportions();
        SetActive(index);
    }

    /// <summary>
    /// Sayfa listesi değişince (sayfa eklendi, numara değişti, geri alındı) açık sekmelerin başlıklarını yeniler; artık olmayan bir sayfanın
    /// sekmesi kapanır.
    /// </summary>
    public void SyncPageTitles(IReadOnlyList<string> labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        foreach (var (index, entry) in _pages.ToList())
        {
            if (index < labels.Count)
            {
                entry.Document.Title = labels[index];
                continue;
            }

            _factory.RemoveDockable(entry.Document, false);
            _pages.Remove(index);
        }
    }

    /// <summary>Bütün sayfa sekmelerini kapatır (başka proje açılırken).</summary>
    public void CloseAllPages()
    {
        foreach (var (document, _) in _pages.Values.ToList())
        {
            _factory.RemoveDockable(document, false);
        }

        _pages.Clear();
        SetActive(-1);
    }

    /// <summary>Panelin başlığını değiştirir (ör. "Mesajlar · 3").</summary>
    public void SetTitle(string toolId, string title)
    {
        if (FindTool(toolId) is { } tool)
        {
            tool.Title = title;
        }
    }

    /// <summary>Paneli öne getirir; iğneliyse açar.</summary>
    public void ShowTool(string toolId)
    {
        if (FindTool(toolId) is { } tool)
        {
            if (IsPinned(tool))
            {
                _factory.UnpinDockable(tool);
            }

            _factory.SetActiveDockable(tool);
        }
    }

    public bool NavigatorsHidden => NavigatorIds.All(id => FindTool(id) is not { } tool || IsPinned(tool));

    public bool MessagesHidden => BottomIds.All(id => FindTool(id) is not { } tool || IsPinned(tool));

    /// <summary>Navigator'ları kenara iğneler (otomatik gizler) ya da geri açar.</summary>
    public void SetNavigatorsHidden(bool hidden)
    {
        SetHidden(NavigatorIds, hidden);
        if (!hidden && FindTool(PagesId) is { } pages)
        {
            _factory.SetActiveDockable(pages);
        }
    }

    public void SetMessagesHidden(bool hidden) => SetHidden(BottomIds, hidden);

    /// <summary>Varsayılan düzen: solda navigator'lar, ortada sayfalar, altta mesajlar. Açık sayfalar korunur.</summary>
    public void Reset()
    {
        var open = _pages.ToDictionary(entry => entry.Key, entry => entry.Value.Document.Title);
        var active = ActivePage;
        Build(navigators: [.. NavigatorIds.Select(id => MakeTool(id))], bottomTools: [.. BottomIds.Select(id => MakeTool(id))]);
        Reopen(open, active);
    }

    /// <summary>Düzenin JSON'u (sayfa sekmeleri hariç; onlar projeye bağlıdır).</summary>
    public string Save()
    {
        var pages = _pages.ToDictionary(entry => entry.Key, entry => entry.Value.Document.Title);
        var active = ActivePage;
        foreach (var (document, _) in _pages.Values.ToList())
        {
            _factory.RemoveDockable(document, false);
        }

        _pages.Clear();
        var json = new AvaloniaDockSerializer().Serialize(_root);
        Reopen(pages, active);
        return json;
    }

    /// <summary>
    /// Kaydedilmiş düzeni yükler. Bozuk ya da eksik panel içeren düzen reddedilir ve varsayılan düzen
    /// kalır; dönüş değeri yüklenip yüklenmediğidir.
    /// </summary>
    public bool Load(string json)
    {
        LastLoadRepaired = false;
        try
        {
            var root = new AvaloniaDockSerializer().Deserialize<RootDock>(json);
            if (root is null)
            {
                return false;
            }

            var factory = new Factory();
            var found = _tools.Keys.ToDictionary(id => id, id => factory.Find(root, dockable => dockable.Id == id).FirstOrDefault());
            var documents = factory.Find(root, dockable => dockable.Id == DocumentsId).OfType<DocumentDock>().FirstOrDefault();
            var isDocumentsPinned = documents is not null
                && new[] { root.LeftPinnedDockables, root.RightPinnedDockables, root.TopPinnedDockables, root.BottomPinnedDockables }
                    .Any(list => list?.Contains(documents) == true);
            if (found.Values.Any(tool => tool is not Tool) || documents is null || isDocumentsPinned)
            {
                return false;
            }

            foreach (var (id, dockable) in found)
            {
                var tool = (Tool)dockable!;
                Detach(_tools[id].Content);
                tool.Content = _tools[id].Content;
                tool.Title = _tools[id].Title;
            }

            var open = _pages.ToDictionary(entry => entry.Key, entry => entry.Value.Document.Title);
            var active = ActivePage;
            LastLoadRepaired = Attach(factory, root, documents);
            Reopen(open, active);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Panel düzeni yüklenemedi");
            return false;
        }
    }

    private void Build(IReadOnlyList<Tool> navigators, IReadOnlyList<Tool> bottomTools)
    {
        var factory = new Factory();
        var left = new ToolDock
        {
            Id = LeftId,
            Alignment = Alignment.Left,
            Proportion = LeftProportion,
            CollapsedProportion = LeftProportion,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>([.. navigators]),
            ActiveDockable = navigators[0],
        };
        var documents = new DocumentDock
        {
            Id = DocumentsId,
            CanCreateDocument = false,
            IsCollapsable = false,
            Proportion = DocumentsProportion,
            CollapsedProportion = DocumentsProportion,
            MinHeight = MinDocumentHeightPx,
            VisibleDockables = factory.CreateList<IDockable>(),
        };
        var bottom = new ToolDock
        {
            Id = BottomId,
            Alignment = Alignment.Bottom,
            Proportion = BottomProportion,
            CollapsedProportion = BottomProportion,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>([.. bottomTools]),
            ActiveDockable = bottomTools[0],
        };
        var center = new ProportionalDock
        {
            Id = CenterId,
            Orientation = Orientation.Vertical,
            IsCollapsable = false,
            Proportion = CenterProportion,
            CollapsedProportion = CenterProportion,
            MinWidth = MinCenterWidthPx,
            VisibleDockables = factory.CreateList<IDockable>(documents, new ProportionalDockSplitter(), bottom),
        };
        var main = new ProportionalDock
        {
            Id = MainId,
            Orientation = Orientation.Horizontal,
            IsCollapsable = false,
            VisibleDockables = factory.CreateList<IDockable>(left, new ProportionalDockSplitter(), center),
        };
        var root = factory.CreateRootDock();
        root.Id = "kok";
        root.IsCollapsable = false;
        root.VisibleDockables = factory.CreateList<IDockable>(main);
        root.ActiveDockable = main;
        root.DefaultDockable = main;
        _ = Attach(factory, root, documents);
    }

    private bool Attach(Factory factory, IRootDock root, DocumentDock documents)
    {
        if (_factory is not null)
        {
            _factory.ActiveDockableChanged -= OnActiveDockableChanged;
            _factory.FocusedDockableChanged -= OnFocusedDockableChanged;
            _factory.DockableClosed -= OnDockableClosed;
        }

        if (_documents is not null)
        {
            _documents.PropertyChanged -= OnDocumentsPropertyChanged;
        }

        EnsureNotCollapsable(root);
        _factory = factory;
        _root = root;
        _documents = documents;
        factory.InitLayout(root);
        documents.IsEmpty = false;
        documents.MinHeight = MinDocumentHeightPx;
        documents.PropertyChanged += OnDocumentsPropertyChanged;
        var repaired = NormalizeLayout(factory, root, documents, NavigatorsHidden, MessagesHidden);
        factory.ActiveDockableChanged += OnActiveDockableChanged;
        factory.FocusedDockableChanged += OnFocusedDockableChanged;
        factory.DockableClosed += OnDockableClosed;
        Control.Factory = factory;
        Control.Layout = root;
        return repaired;
    }

    private void OnDocumentsPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == nameof(IDock.IsEmpty) && sender is DocumentDock doc && doc.IsEmpty)
        {
            doc.IsEmpty = false;
        }
    }

    // Tüm dock düğümleri kapanma sırasında ağaçtan çöküp silinmesin.
    private static void EnsureNotCollapsable(IDockable dockable)
    {
        if (dockable is not IDock dock)
        {
            return;
        }

        dock.IsCollapsable = false;
        if (dock.VisibleDockables is null)
        {
            return;
        }

        foreach (var child in dock.VisibleDockables)
        {
            EnsureNotCollapsable(child);
        }
    }

    // Sekmeler yeni düzene taşınırken çizim alanları yeniden kullanılır; yakınlaştırma korunur.
    private void Reopen(Dictionary<int, string> pages, int active)
    {
        var views = _pages.ToDictionary(entry => entry.Key, entry => entry.Value.View);
        _pages.Clear();
        foreach (var (index, title) in pages.OrderBy(entry => entry.Key))
        {
            var view = views.TryGetValue(index, out var existing) ? existing : _createView(index);
            Detach(view);
            var document = new Document { Id = PagePrefix + index, Title = title, Content = view, Context = index, CanFloat = false };
            _pages[index] = (document, view);
            _factory.AddDockable(_documents, document);
        }

        if (_pages.TryGetValue(active, out var page))
        {
            _factory.SetActiveDockable(page.Document);
        }

        SetActive(_pages.ContainsKey(active) ? active : -1);
    }

    private Tool MakeTool(string id)
    {
        var (title, content) = _tools[id];
        Detach(content);
        return new Tool { Id = id, Title = title, Content = content, CanClose = false, CanFloat = false };
    }

    private void SetHidden(IEnumerable<string> ids, bool hidden)
    {
        foreach (var id in ids)
        {
            if (FindTool(id) is { } tool && IsPinned(tool) != hidden)
            {
                if (hidden)
                {
                    _factory.PinDockable(tool);
                }
                else
                {
                    _factory.UnpinDockable(tool);
                }
            }
        }

        _ = RestoreProportions();
    }

    // İğnelenip geri açılan ya da çökük kaydedilen panel bölmelerinin oranı 0 veya 1 kalabilir;
    // çizim alanı veya yan/alt bölmeler görünmez olduğunda sağlıklı oranlara döndürülür.
    private bool RestoreProportions() =>
        NormalizeLayout(_factory, _root, _documents, NavigatorsHidden, MessagesHidden);

    internal static bool NormalizeLayout(
        Factory factory,
        IRootDock root,
        DocumentDock documents,
        bool navigatorsHidden,
        bool messagesHidden)
    {
        var repaired = false;

        if (documents.IsEmpty)
        {
            documents.IsEmpty = false;
            repaired = true;
        }

        if (double.IsNaN(documents.MinHeight) || documents.MinHeight < MinDocumentHeightPx)
        {
            documents.MinHeight = MinDocumentHeightPx;
        }

        var bottom = factory.Find(root, dockable => dockable.Id == BottomId).OfType<IDock>().FirstOrDefault();
        if (!messagesHidden && bottom is not null)
        {
            if (double.IsNaN(documents.Proportion)
                || documents.Proportion < MinDocumentProportion
                || documents.Proportion > MaxBottomProportion
                || double.IsNaN(bottom.Proportion)
                || bottom.Proportion <= 0.01
                || bottom.Proportion > MaxBottomProportion)
            {
                documents.Proportion = DocumentsProportion;
                documents.CollapsedProportion = DocumentsProportion;
                bottom.Proportion = BottomProportion;
                bottom.CollapsedProportion = BottomProportion;
                repaired = true;
            }
        }
        else if (messagesHidden && (double.IsNaN(documents.Proportion) || documents.Proportion < MinDocumentProportion))
        {
            documents.Proportion = 1.0;
            documents.CollapsedProportion = DocumentsProportion;
            repaired = true;
        }

        var left = factory.Find(root, dockable => dockable.Id == LeftId).OfType<IDock>().FirstOrDefault();
        var center = factory.Find(root, dockable => dockable.Id == CenterId).OfType<IDock>().FirstOrDefault();
        if (center is not null && (double.IsNaN(center.MinWidth) || center.MinWidth < MinCenterWidthPx))
        {
            center.MinWidth = MinCenterWidthPx;
        }

        if (!navigatorsHidden && left is not null)
        {
            var leftBad = double.IsNaN(left.Proportion) || left.Proportion <= 0.01 || left.Proportion > MaxLeftProportion;
            var centerBad = center is not null && (double.IsNaN(center.Proportion) || center.Proportion < MinCenterProportion || center.Proportion > 0.98);
            if (leftBad || centerBad)
            {
                left.Proportion = LeftProportion;
                left.CollapsedProportion = LeftProportion;
                if (center is not null)
                {
                    center.Proportion = CenterProportion;
                    center.CollapsedProportion = CenterProportion;
                }

                repaired = true;
            }
        }

        return repaired;
    }

    // Panel, kimliğiyle düzen ağacında aranır (iğneli olanlar dahil).
    private IDockable? FindTool(string id) =>
        _factory.Find(_root, dockable => dockable.Id == id).FirstOrDefault()
        ?? new[] { _root.LeftPinnedDockables, _root.RightPinnedDockables, _root.TopPinnedDockables, _root.BottomPinnedDockables }
            .SelectMany(list => list ?? []).FirstOrDefault(dockable => dockable.Id == id);

    private bool IsPinned(IDockable tool) =>
        new[] { _root.LeftPinnedDockables, _root.RightPinnedDockables, _root.TopPinnedDockables, _root.BottomPinnedDockables }
            .Any(list => list?.Contains(tool) == true);

    // Bir kontrol yalnızca bir yerde durabilir; yeni düzene konmadan önce eskisinden ayrılır.
    private static void Detach(Control control)
    {
        switch (control.Parent)
        {
            case Panel panel:
                panel.Children.Remove(control);
                break;
            case ContentControl host:
                host.Content = null;
                break;
            case Decorator decorator:
                decorator.Child = null;
                break;
            case Avalonia.Controls.Presenters.ContentPresenter presenter:
                presenter.Content = null;
                break;
        }
    }

    private void OnActiveDockableChanged(object? sender, Dock.Model.Core.Events.ActiveDockableChangedEventArgs e) => Track(e.Dockable);

    private void OnFocusedDockableChanged(object? sender, Dock.Model.Core.Events.FocusedDockableChangedEventArgs e) => Track(e.Dockable);

    private void Track(IDockable? dockable)
    {
        if (dockable is Document { Context: int index } && _pages.ContainsKey(index))
        {
            SetActive(index);
        }
    }

    private void OnDockableClosed(object? sender, Dock.Model.Core.Events.DockableClosedEventArgs e)
    {
        if (e.Dockable is Document { Context: int index })
        {
            _pages.Remove(index);
            if (ActivePage == index)
            {
                SetActive(_documents.ActiveDockable is Document { Context: int next } && _pages.ContainsKey(next) ? next : -1);
            }
        }
    }

    private void SetActive(int index)
    {
        if (ActivePage == index)
        {
            return;
        }

        ActivePage = index;
        ActivePageChanged?.Invoke(index);
    }
}
