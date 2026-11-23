using Ake.Core;
using Ake.Core.Model;
using Ake.Library;
using Ake.Render;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ake.Editor;

/// <summary>
/// PDF veya serbest grafik sayfalarından tanınan cihazları, tel uçlarını ve bağlantıları önizleyip
/// onaylamaya; elektriksel modele dönüştürmeye ve sembol/ürün kütüphanesine aktarmaya yarayan pencere.
/// </summary>
public sealed class GraphicRecognitionWindow : Window
{
    private readonly EditorSession _session;
    private readonly int _activePageIndex;
    private readonly Action _ensureLibraryOpen;
    private readonly Dictionary<int, PageRecognitionResult> _cache = [];
    private readonly List<DeviceItem> _allDevices = [];
    private readonly List<WireItem> _allWires = [];
    private readonly List<DeviceItem> _pageDevices = [];
    private readonly List<WireItem> _pageWires = [];

    private readonly ComboBox _scopeCombo = new()
    {
        Width = 230,
        ItemsSource = new[] { "Etkin sayfa", "Grafik içeren tüm sayfalar" },
    };

    private readonly ComboBox _previewPageCombo = new() { Width = 280 };
    private readonly TextBlock _summaryText = new() { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
    private readonly SceneView _preview = new() { MinWidth = 480, MinHeight = 400 };
    private readonly SceneView _symbolPreview = new() { Height = 128 };
    private readonly TextBlock _selectedDetail = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private readonly StackPanel _deviceListPanel = new() { Spacing = 3 };
    private readonly StackPanel _wireListPanel = new() { Spacing = 3 };
    private readonly TextBlock _deviceHeader = new() { Text = "Cihazlar (0)", FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _wireHeader = new() { Text = "Bağlantılar (0)", FontWeight = FontWeight.SemiBold };
    private readonly CheckBox _removeGraphicsCheck = new()
    {
        Content = "Dönüştürülen serbest grafikleri sayfadan temizle",
        IsChecked = true,
    };

    private readonly CheckBox _exportAlsoCheck = new()
    {
        Content = "Seçili cihazları kütüphaneye de kaydet (sembol + ürün)",
        IsChecked = false,
    };

    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private bool _updatingUi;

    public GraphicRecognitionWindow(
        EditorSession session,
        int activePageIndex,
        Action ensureLibraryOpen,
        bool startWithAllPages = false,
        IReadOnlyList<PageRecognitionResult>? precomputedAll = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(ensureLibraryOpen);

        _session = session;
        _activePageIndex = activePageIndex;
        _ensureLibraryOpen = ensureLibraryOpen;

        Title = "Grafikten Cihaz, Tel Ucu ve Bağlantı Tanıma";
        Width = 1180;
        Height = 760;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (precomputedAll is not null && _session.Project is not null)
        {
            var pageIndexMap = new Dictionary<PageId, int>();
            for (var i = 0; i < _session.Project.Pages.Count; i++)
            {
                pageIndexMap[_session.Project.Pages[i].Id] = i;
            }

            foreach (var r in precomputedAll)
            {
                if (pageIndexMap.TryGetValue(r.PageId, out var idx))
                {
                    _cache[idx] = r;
                }
            }
        }

        _preview.ImageBaseDirectory = _session.ProjectDirectory;
        _scopeCombo.SelectedIndex = startWithAllPages ? 1 : 0;

        Content = BuildContent();
        WireEvents();
        ReloadScope();
    }

    /// <summary>Pencere açıkken en az bir kez kütüphaneye aktarım yapıldı mı.</summary>
    public bool LibraryChanged { get; private set; }

    /// <summary>Cihaz/tel uçları elektriksel modele dönüştürüldü mü.</summary>
    public bool ModelChanged { get; private set; }

    /// <summary>Son işlem sonucu bilgilendirme mesajı.</summary>
    public string? LastMessage { get; private set; }

    private sealed class DeviceItem(int pageIndex, RecognizedDevice device)
    {
        public int PageIndex { get; } = pageIndex;
        public RecognizedDevice Device { get; } = device;
        public bool Selected { get; set; } = true;
    }

    private sealed class WireItem(int pageIndex, RecognizedWire wire)
    {
        public int PageIndex { get; } = pageIndex;
        public RecognizedWire Wire { get; } = wire;
        public bool Selected { get; set; } = true;
    }

    private Control BuildContent()
    {
        var fitButton = new Button { Content = "Sayfaya sığdır", Padding = new Thickness(10, 4) };
        fitButton.Click += (_, _) => _preview.FitToView();

        var zoomSelectedButton = new Button { Content = "Seçili cihaza odaklan", Padding = new Thickness(10, 4) };
        zoomSelectedButton.Click += (_, _) => FocusSelectedDevice();

        var darkCheck = new CheckBox { Content = "Koyu zemin", VerticalAlignment = VerticalAlignment.Center };
        darkCheck.IsCheckedChanged += (_, _) => _preview.DarkCanvas = darkCheck.IsChecked == true;

        var topBar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 10, 12, 8),
        };
        topBar.Children.Add(new TextBlock { Text = "Kapsam:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        topBar.Children.Add(_scopeCombo);
        topBar.Children.Add(new TextBlock { Text = "Önizlenen sayfa:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) });
        topBar.Children.Add(_previewPageCombo);
        topBar.Children.Add(new Border { Width = 14 });
        topBar.Children.Add(_summaryText);

        var previewToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { fitButton, zoomSelectedButton, darkCheck },
        };

        var symbolBox = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("180,*"),
                ColumnSpacing = 10,
                Children =
                {
                    new Border
                    {
                        BorderBrush = Brushes.LightGray,
                        BorderThickness = new Thickness(1),
                        Child = _symbolPreview,
                    },
                    SetGridColumn(new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock { Text = "Seçili Sembol ve Tel Uçları", FontWeight = FontWeight.SemiBold },
                            _selectedDetail,
                        },
                    }, 1),
                },
            },
        };

        var leftPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            RowSpacing = 8,
            Margin = new Thickness(12, 0, 8, 8),
        };
        leftPane.Children.Add(previewToolbar);
        var previewBorder = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = _preview,
        };
        Grid.SetRow(previewBorder, 1);
        leftPane.Children.Add(previewBorder);
        Grid.SetRow(symbolBox, 2);
        leftPane.Children.Add(symbolBox);

        var selectAllDevices = new Button { Content = "Tümünü seç", Padding = new Thickness(8, 3) };
        selectAllDevices.Click += (_, _) => SetAllDevicesChecked(true);
        var clearDevices = new Button { Content = "Hiçbiri", Padding = new Thickness(8, 3) };
        clearDevices.Click += (_, _) => SetAllDevicesChecked(false);

        var deviceToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _deviceHeader, selectAllDevices, clearDevices },
        };

        var selectAllWires = new Button { Content = "Tümünü seç", Padding = new Thickness(8, 3) };
        selectAllWires.Click += (_, _) => SetAllWiresChecked(true);
        var clearWires = new Button { Content = "Hiçbiri", Padding = new Thickness(8, 3) };
        clearWires.Click += (_, _) => SetAllWiresChecked(false);

        var wireToolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _wireHeader, selectAllWires, clearWires },
        };

        var rightPane = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,3*,Auto,2*"),
            RowSpacing = 6,
            Margin = new Thickness(4, 0, 12, 8),
        };
        rightPane.Children.Add(deviceToolbar);
        var devScroll = new ScrollViewer
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Content = _deviceListPanel,
        };
        Grid.SetRow(devScroll, 1);
        rightPane.Children.Add(devScroll);

        Grid.SetRow(wireToolbar, 2);
        rightPane.Children.Add(wireToolbar);
        var wireScroll = new ScrollViewer
        {
            BorderBrush = Brushes.LightGray,
            BorderThickness = new Thickness(1),
            Content = _wireListPanel,
        };
        Grid.SetRow(wireScroll, 3);
        rightPane.Children.Add(wireScroll);

        var mainGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,430"),
        };
        mainGrid.Children.Add(leftPane);
        Grid.SetColumn(rightPane, 1);
        mainGrid.Children.Add(rightPane);

        var exportLibButton = new Button
        {
            Content = "Yalnızca kütüphaneye aktar",
            Padding = new Thickness(12, 6),
        };
        exportLibButton.Click += (_, _) => ExportOnlyToLibrary();

        var convertButton = new Button
        {
            Content = "Modele dönüştür (Cihaz + Tel Ucu + Bağlantı)",
            Padding = new Thickness(14, 6),
            FontWeight = FontWeight.SemiBold,
        };
        convertButton.Click += (_, _) => ConvertAndClose();

        var closeButton = new Button
        {
            Content = "Kapat",
            Padding = new Thickness(12, 6),
        };
        closeButton.Click += (_, _) => Close(ModelChanged);

        var footerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(12, 6, 12, 12),
            ColumnSpacing = 12,
        };
        footerGrid.Children.Add(new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 14,
                    Children = { _removeGraphicsCheck, _exportAlsoCheck },
                },
                _statusText,
            },
        });
        footerGrid.Children.Add(SetGridColumn(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { exportLibButton, convertButton, closeButton },
        }, 1));

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
        };
        root.Children.Add(topBar);
        Grid.SetRow(mainGrid, 1);
        root.Children.Add(mainGrid);
        Grid.SetRow(footerGrid, 2);
        root.Children.Add(footerGrid);
        return root;
    }

    private static Control SetGridColumn(Control c, int col)
    {
        Grid.SetColumn(c, col);
        return c;
    }

    private void WireEvents()
    {
        _scopeCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingUi)
            {
                ReloadScope();
            }
        };

        _previewPageCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingUi && _previewPageCombo.SelectedItem is PageChoice choice)
            {
                LoadPreviewPage(choice.PageIndex);
            }
        };
    }

    private sealed record PageChoice(int PageIndex, string Label)
    {
        public override string ToString() => Label;
    }

    private void ReloadScope()
    {
        _updatingUi = true;
        try
        {
            _allDevices.Clear();
            _allWires.Clear();

            var project = _session.Project;
            if (project is null)
            {
                return;
            }

            var targetIndices = _scopeCombo.SelectedIndex == 1
                ? _session.GraphicPageIndices()
                : (_activePageIndex >= 0 ? [_activePageIndex] : Array.Empty<int>());

            var pageChoices = new List<PageChoice>();
            foreach (var idx in targetIndices)
            {
                if (!_cache.TryGetValue(idx, out var res))
                {
                    var recognized = _session.RecognizePage(idx);
                    if (recognized.IsFailure)
                    {
                        continue;
                    }

                    res = recognized.Value;
                    _cache[idx] = res;
                }

                if (_scopeCombo.SelectedIndex == 1 && res.Devices.Count == 0 && res.Wires.Count == 0)
                {
                    continue;
                }

                foreach (var d in res.Devices)
                {
                    _allDevices.Add(new DeviceItem(idx, d));
                }

                foreach (var w in res.Wires)
                {
                    _allWires.Add(new WireItem(idx, w));
                }

                var page = project.Pages[idx];
                pageChoices.Add(new PageChoice(idx, $"{page.Number} {page.Title} ({res.Devices.Count} cihaz, {res.Wires.Count} tel)"));
            }

            if (pageChoices.Count == 0 && _activePageIndex >= 0 && _activePageIndex < project.Pages.Count)
            {
                var p = project.Pages[_activePageIndex];
                pageChoices.Add(new PageChoice(_activePageIndex, $"{p.Number} {p.Title}"));
            }

            _previewPageCombo.ItemsSource = pageChoices;
            var initial = pageChoices.FirstOrDefault(c => c.PageIndex == _activePageIndex) ?? pageChoices.FirstOrDefault();
            _previewPageCombo.SelectedItem = initial;
            _summaryText.Text =
                $"Toplam {_allDevices.Count} cihaz · {_allDevices.Sum(d => d.Device.Pins.Count)} tel ucu · {_allWires.Count} bağlantı";

            if (initial is not null)
            {
                LoadPreviewPageCore(initial.PageIndex);
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void LoadPreviewPage(int pageIndex)
    {
        _updatingUi = true;
        try
        {
            LoadPreviewPageCore(pageIndex);
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void LoadPreviewPageCore(int pageIndex)
    {
        _pageDevices.Clear();
        _pageDevices.AddRange(_allDevices.Where(d => d.PageIndex == pageIndex));
        _pageWires.Clear();
        _pageWires.AddRange(_allWires.Where(w => w.PageIndex == pageIndex));

        var scene = _session.SceneFor(pageIndex, DateOnly.FromDateTime(DateTime.Now));
        _preview.Scene = scene.IsSuccess ? scene.Value : null;
        _preview.GridArea = _session.DrawingAreaFor(pageIndex);

        RebuildDeviceList();
        RebuildWireList();
        RefreshOverlays();

        if (_pageDevices.Count > 0)
        {
            ShowDeviceDetail(_pageDevices[0].Device);
        }
        else
        {
            _symbolPreview.Scene = null;
            _selectedDetail.Text = "Bu sayfada tanınan cihaz yok.";
        }
    }

    private void RebuildDeviceList()
    {
        _deviceListPanel.Children.Clear();
        _deviceHeader.Text = _scopeCombo.SelectedIndex == 1
            ? $"Sayfa cihazları ({_pageDevices.Count}) / Toplam {_allDevices.Count}"
            : $"Cihazlar ({_pageDevices.Count})";

        foreach (var item in _pageDevices)
        {
            var dev = item.Device;
            var check = new CheckBox
            {
                IsChecked = item.Selected,
                VerticalAlignment = VerticalAlignment.Center,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                item.Selected = check.IsChecked == true;
                RefreshOverlays();
            };

            var info = new StackPanel { Spacing = 1 };
            var partLabel = dev.Part is { } p ? $" · {p.OrderCode}" : string.Empty;
            info.Children.Add(new TextBlock
            {
                Text = $"-{dev.Tag}{partLabel}  ({dev.Pins.Count} uç)",
                FontWeight = FontWeight.SemiBold,
                FontSize = 12,
            });
            var descText = string.IsNullOrWhiteSpace(dev.Description) ? dev.Kind.ToString() : dev.Description;
            info.Children.Add(new TextBlock
            {
                Text = descText,
                FontSize = 11,
                Opacity = 0.75,
            });

            var rowButton = new Button
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(4, 2),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Content = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing = 6,
                    Children = { check, SetGridColumn(info, 1) },
                },
            };
            rowButton.Click += (_, _) =>
            {
                ShowDeviceDetail(dev);
                _preview.FocusBox(new Box(dev.X - 6, dev.Y - 6, dev.Width + 12, dev.Height + 12));
            };
            _deviceListPanel.Children.Add(rowButton);
        }
    }

    private void RebuildWireList()
    {
        _wireListPanel.Children.Clear();
        _wireHeader.Text = _scopeCombo.SelectedIndex == 1
            ? $"Sayfa bağlantıları ({_pageWires.Count}) / Toplam {_allWires.Count}"
            : $"Bağlantılar ({_pageWires.Count})";

        var devNames = _pageDevices.ToDictionary(d => d.Device.Key, d => d.Device.Tag, StringComparer.Ordinal);
        foreach (var item in _pageWires)
        {
            var wire = item.Wire;
            var fromTag = devNames.GetValueOrDefault(wire.FromDeviceKey, wire.FromDeviceKey);
            var toTag = devNames.GetValueOrDefault(wire.ToDeviceKey, wire.ToDeviceKey);
            var numText = string.IsNullOrWhiteSpace(wire.WireNumber) ? string.Empty : $"No:{wire.WireNumber}  ";
            var colText = string.IsNullOrWhiteSpace(wire.WireColor) ? string.Empty : $" [{wire.WireColor}]";

            var check = new CheckBox
            {
                IsChecked = item.Selected,
                Content = $"{numText}-{fromTag}:{wire.FromPinName} ↔ -{toTag}:{wire.ToPinName}{colText}",
                FontSize = 12,
            };
            check.IsCheckedChanged += (_, _) =>
            {
                item.Selected = check.IsChecked == true;
                RefreshOverlays();
            };
            _wireListPanel.Children.Add(check);
        }
    }

    private RecognizedDevice? _focusedDevice;

    private void ShowDeviceDetail(RecognizedDevice dev)
    {
        _focusedDevice = dev;
        var scene = EditorSession.Preview(dev.Symbol);
        _symbolPreview.Scene = scene;
        _symbolPreview.GridArea = new Box(0, 0, scene.WidthMm, scene.HeightMm);

        var pinSummary = string.Join(", ", dev.Pins.Take(12).Select(p => p.Name));
        if (dev.Pins.Count > 12)
        {
            pinSummary += $" … (+{dev.Pins.Count - 12})";
        }

        _selectedDetail.Text =
            $"-{dev.Tag}  ·  {dev.Part?.Manufacturer ?? ""} {dev.Part?.OrderCode ?? ""}".Trim() + Environment.NewLine
            + $"Uçlar ({dev.Pins.Count}): {pinSummary}";
    }

    private void FocusSelectedDevice()
    {
        if (_focusedDevice is { } dev)
        {
            _preview.FocusBox(new Box(dev.X - 6, dev.Y - 6, dev.Width + 12, dev.Height + 12));
        }
    }

    private void RefreshOverlays()
    {
        var activeDevs = _pageDevices.Where(d => d.Selected).Select(d => d.Device).ToList();
        var activeDevKeys = activeDevs.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);

        _preview.Highlights = [.. activeDevs.Select(d => new Box(d.X, d.Y, d.Width, d.Height))];
        _preview.PinMarkers = [.. activeDevs.SelectMany(d => d.Pins.Select(p => (p.SheetX, p.SheetY)))];

        var pinCoords = activeDevs.SelectMany(d => d.Pins.Select(p => (Key: (d.Key, p.Name), Pos: (p.SheetX, p.SheetY))))
            .ToDictionary(x => x.Key, x => x.Pos);
        var wirePaths = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var w in _pageWires)
        {
            if (!w.Selected
                || !activeDevKeys.Contains(w.Wire.FromDeviceKey)
                || !activeDevKeys.Contains(w.Wire.ToDeviceKey)
                || !pinCoords.TryGetValue((w.Wire.FromDeviceKey, w.Wire.FromPinName), out var fromPos)
                || !pinCoords.TryGetValue((w.Wire.ToDeviceKey, w.Wire.ToPinName), out var toPos))
            {
                continue;
            }

            var path = new List<(double X, double Y)> { fromPos };
            foreach (var pt in w.Wire.Route)
            {
                path.Add((pt.X, pt.Y));
            }

            path.Add(toPos);
            wirePaths.Add(path);
        }

        _preview.WireHighlights = wirePaths;
    }

    private void SetAllDevicesChecked(bool value)
    {
        foreach (var item in _pageDevices)
        {
            item.Selected = value;
        }

        RebuildDeviceList();
        RefreshOverlays();
    }

    private void SetAllWiresChecked(bool value)
    {
        foreach (var item in _pageWires)
        {
            item.Selected = value;
        }

        RebuildWireList();
        RefreshOverlays();
    }

    private (List<RecognizedDevice> Devices, List<RecognizedWire> Wires) SelectedPayloads()
    {
        var devices = _allDevices.Where(d => d.Selected).Select(d => d.Device).ToList();
        var devKeys = devices.Select(d => d.Key).ToHashSet(StringComparer.Ordinal);
        var wires = _allWires
            .Where(w => w.Selected && devKeys.Contains(w.Wire.FromDeviceKey) && devKeys.Contains(w.Wire.ToDeviceKey))
            .Select(w => w.Wire)
            .ToList();
        return (devices, wires);
    }

    private void ExportOnlyToLibrary()
    {
        var (devices, _) = SelectedPayloads();
        if (devices.Count == 0)
        {
            _statusText.Text = "Kütüphaneye aktarmak için en az bir cihaz seçin.";
            return;
        }

        _ensureLibraryOpen();
        var res = _session.ExportRecognizedToLibrary(devices);
        if (res.IsFailure)
        {
            _statusText.Text = res.Error!.Message;
            return;
        }

        LibraryChanged = true;
        var s = res.Value;
        LastMessage = $"Kütüphaneye aktarıldı: {s.SymbolsAdded} yeni sembol ({s.SymbolsUpdated} güncellendi), {s.ProductsAdded} yeni ürün.";
        _statusText.Text = LastMessage;
    }

    private void ConvertAndClose()
    {
        var (devices, wires) = SelectedPayloads();
        if (devices.Count == 0 && wires.Count == 0)
        {
            _statusText.Text = "Dönüştürmek için en az bir cihaz seçin.";
            return;
        }

        if (_exportAlsoCheck.IsChecked == true && devices.Count > 0)
        {
            _ensureLibraryOpen();
            var libRes = _session.ExportRecognizedToLibrary(devices);
            if (libRes.IsSuccess)
            {
                LibraryChanged = true;
            }
        }

        var conv = _session.ConvertRecognized(devices, wires, _removeGraphicsCheck.IsChecked != false);
        if (conv.IsFailure)
        {
            _statusText.Text = conv.Error!.Message;
            return;
        }

        ModelChanged = true;
        var summary = conv.Value;
        LastMessage = $"Modele aktarıldı: {summary.DevicesAdded} cihaz, {summary.SymbolsAdded} sembol, {summary.ConnectionsAdded} bağlantı.";
        Close(true);
    }
}
