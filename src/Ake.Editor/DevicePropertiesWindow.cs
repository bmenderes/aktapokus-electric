using System.Globalization;
using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ake.Editor;

/// <summary>
/// Gelişmiş 5 sekmeli bileşen özellikleri penceresi (çift tık, sağ tık > Özellikler ya da Enter; ADR-0054):
/// 1. Birincil fonksiyon sekmesi (Bobin / Kontak / Genel + Özellikler tablosu),
/// 2. Görüntüleme (döndürme, X/Y konum, görünüm rolü),
/// 3. Sembol / Noktalar (sembol seçimi, önizleme ve tüm uçlar tablosu),
/// 4. Ürün (üretici, sipariş kodu, tip),
/// 5. Kontak aynası (cihazın tüm function'ları ve sayfa.kolon konumları).
/// </summary>
public sealed class DevicePropertiesWindow : Window
{
    private static readonly int[] Rotations = [0, 90, 180, 270];
    private static readonly string[] PropertyCategories = ["Tüm kategoriler", "Fonksiyon", "Cihaz", "Bağlantı", "Görüntüleme", "Özel"];
    private readonly Dictionary<string, string> _customProperties = new(StringComparer.Ordinal);
    private readonly StackPanel _customListPanel = new() { Spacing = 4 };
    private Border? _customSection;

    private readonly DeviceEditorData _data;
    private readonly EditorSession? _session;
    private readonly Action<DeviceEdits>? _onApply;

    private readonly TextBox _tag = new();
    private readonly TextBox _fullDt = new() { IsReadOnly = true };
    private readonly ComboBox _functionNode = new();
    private readonly ComboBox _locationNode = new();
    private readonly TextBox _quickPinNames = new();
    private readonly TextBox _quickPinCodes = new();
    private readonly TextBox _description = new();
    private readonly TextBox _primaryFunctionDesc = new();
    private readonly TextBox _address = new() { PlaceholderText = "örn. 192.168.0.50" };
    private readonly CheckBox _mainFunctionCheck = new() { Content = "Ana fonksiyon" };

    private readonly ComboBox _categoryFilter = new();
    private readonly StackPanel _propertyRowsPanel = new() { Spacing = 0 };
    private readonly List<(string Category, Border Row)> _propertyRows = [];

    private readonly ComboBox _rotationBox = new();
    private readonly TextBox _xBox = new();
    private readonly TextBox _yBox = new();
    private readonly ComboBox _roleBox = new();

    private readonly TextBlock _symbolNameText = new() { FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly SceneView _symbolPreview = new() { Width = 96, Height = 72 };
    private readonly TextBox _manufacturer = new();
    private readonly TextBox _orderCode = new();
    private readonly TextBox _type = new();

    private readonly List<(FunctionEdit Function, TextBox Description, List<PointRow> Points)> _functions = [];
    private SymbolId? _assignedSymbol;
    private bool _syncing;

    public DevicePropertiesWindow(DeviceEditorData data, EditorSession? session = null, Action<DeviceEdits>? onApply = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _session = session;
        _onApply = onApply;

        Title = $"Özellikler (bileşen): {data.Designation}";
        Width = 820;
        Height = 580;
        MinWidth = 720;
        MinHeight = 500;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        _tag.Text = data.ProductTag;
        _description.Text = data.Description ?? string.Empty;
        Fill(_functionNode, data.FunctionNodes, data.FunctionNode);
        Fill(_locationNode, data.LocationNodes, data.LocationNode);
        _manufacturer.Text = data.Manufacturer;
        _orderCode.Text = data.OrderCode;
        _type.Text = data.TypeDesignation ?? string.Empty;
        _address.Text = data.Address ?? string.Empty;
        _mainFunctionCheck.IsChecked = data.Role == PlacementRole.Main;

        _rotationBox.ItemsSource = Rotations.Select(deg => $"{deg}°").ToList();
        _rotationBox.SelectedIndex = Math.Max(0, Array.IndexOf(Rotations, data.RotationDeg));
        _xBox.Text = data.X.ToString("0.##", CultureInfo.InvariantCulture);
        _yBox.Text = data.Y.ToString("0.##", CultureInfo.InvariantCulture);
        _roleBox.ItemsSource = new[] { "Ana görünüm", "Referans görünüm" };
        _roleBox.SelectedIndex = data.Role == PlacementRole.Main ? 0 : 1;

        _symbolNameText.Text = data.SymbolName ?? "Yerleşik parametrik sembol";
        _symbolPreview.Scene = data.SymbolPreview;

        // Nokta satırlarını önce oluştur ki 1. sekmedeki hızlı alanlarla çift yönlü bağlanabilsin.
        var pointsContent = BuildPointsTab();
        var primaryFunc = _functions.FirstOrDefault(f => f.Function.Id == data.PrimaryFunction);
        if (primaryFunc.Function is null && _functions.Count > 0)
        {
            primaryFunc = _functions[0];
        }

        var primaryLabel = primaryFunc.Function is null
            ? "Genel cihaz"
            : EditorSession.FunctionKindLabel(primaryFunc.Function.Kind);

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = primaryLabel, FontSize = 13, Content = BuildPrimaryTab(primaryFunc) },
                new TabItem { Header = "Görüntüleme", FontSize = 13, Content = BuildDisplayTab() },
                new TabItem { Header = "Sembol / Noktalar", FontSize = 13, Content = pointsContent },
                new TabItem { Header = "Ürün", FontSize = 13, Content = BuildProductTab() },
                new TabItem { Header = "Kontak aynası", FontSize = 13, Content = BuildContactMirrorTab() },
            },
        };

        WireSynchronization(primaryFunc);
        UpdateFullDt();

        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var apply = new Button
        {
            Content = "Uygula",
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = _onApply is not null,
        };
        ok.Click += (_, _) => Close(Collect());
        cancel.Click += (_, _) => Close(null);
        apply.Click += (_, _) => _onApply?.Invoke(Collect());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0),
            Children = { ok, cancel, apply },
        };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel { Margin = new Thickness(14), Children = { buttons, tabs } };

        Opened += (_, _) =>
        {
            _tag.Focus();
            _tag.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private ScrollViewer BuildPrimaryTab((FunctionEdit Function, TextBox Description, List<PointRow> Points) primary)
    {
        var pickExisting = new Button { Content = "…", Width = 30, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(pickExisting, "Projedeki mevcut cihazlardan etiket seç");
        pickExisting.IsEnabled = _data.ExistingDevices is { Count: > 0 };
        pickExisting.Click += (_, _) => ShowExistingDevicesMenu(pickExisting);

        var clearTag = new Button { Content = "×", Width = 30, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(clearTag, "Ürün tag'ini temizle");
        clearTag.Click += (_, _) =>
        {
            _tag.Text = string.Empty;
            _tag.Focus();
        };

        var tagRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 4 };
        var dash = new TextBlock { Text = "-", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        Grid.SetColumn(_tag, 1);
        Grid.SetColumn(pickExisting, 2);
        Grid.SetColumn(clearTag, 3);
        tagRow.Children.AddRange([dash, _tag, pickExisting, clearTag]);

        var structureRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,*") };
        Grid.SetColumn(_locationNode, 2);
        structureRow.Children.AddRange([_functionNode, _locationNode]);

        var dtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), RowSpacing = 6, Margin = new Thickness(0, 6, 0, 8) };
        dtGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        dtGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        dtGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        dtGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        AddField(dtGrid, 0, 0, "Görünen etiket:", tagRow);
        AddField(dtGrid, 0, 2, "Tam etiket (=Fonksiyon +Yer -Ürün):", _fullDt);
        AddField(dtGrid, 2, 0, "Bağlantı noktası adları (¶ ile ayırın):", _quickPinNames);
        AddField(dtGrid, 2, 2, "Bağlantı noktası kodu / açıklaması:", _quickPinCodes);

        var detailGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,14,*"), RowSpacing = 6, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < 6; i++)
        {
            detailGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        AddField(detailGrid, 0, 0, "Teknik karakteristikler (Cihaz açıklaması):", _description);
        AddField(detailGrid, 0, 2, "Fonksiyon metni:", _primaryFunctionDesc);
        AddField(detailGrid, 2, 0, "Ağ adresi (IP) / Montaj yeri:", _address);
        AddField(detailGrid, 2, 2, "Fonksiyon (=) ve Yer (+):", structureRow);

        Grid.SetRow(_mainFunctionCheck, 4);
        Grid.SetColumn(_mainFunctionCheck, 0);
        _mainFunctionCheck.Margin = new Thickness(0, 4, 0, 0);
        detailGrid.Children.Add(_mainFunctionCheck);

        // Özellikler tablosu (2. görselin alt kısmı)
        _categoryFilter.ItemsSource = PropertyCategories;
        _categoryFilter.SelectedIndex = 0;
        _categoryFilter.MinWidth = 220;
        _categoryFilter.SelectionChanged += (_, _) => FilterPropertyRows();

        _customProperties.Clear();
        if (_data.CustomProperties is not null)
        {
            foreach (var (key, value) in _data.CustomProperties)
            {
                _customProperties[key] = value;
            }
        }

        var customKeyBox = new TextBox { PlaceholderText = "Özellik adı", Width = 150 };
        var customValueBox = new TextBox { PlaceholderText = "Değer", Width = 180 };
        var customAddButton = new Button { Content = "Özel ekle" };
        customAddButton.Click += (_, _) =>
        {
            var key = customKeyBox.Text?.Trim() ?? string.Empty;
            var value = customValueBox.Text?.Trim() ?? string.Empty;
            if (key.Length == 0 || value.Length == 0)
            {
                return;
            }

            _customProperties[key] = value;
            customKeyBox.Text = string.Empty;
            customValueBox.Text = string.Empty;
            RefreshCustomProperties();
        };

        var customHeaderBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children =
            {
                new TextBlock { Text = "Özel özellik:", VerticalAlignment = VerticalAlignment.Center },
                customKeyBox,
                customValueBox,
                customAddButton,
            },
        };

        _customSection = new Border
        {
            Padding = new Thickness(0, 4, 0, 4),
            Child = new StackPanel
            {
                Spacing = 6,
                Children = { customHeaderBar, _customListPanel },
            },
        };
        RefreshCustomProperties();

        _categoryFilter.SelectionChanged += (_, _) => UpdateCustomSectionVisibility();

        var filterBar = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 6,
            Margin = new Thickness(0, 6, 0, 6),
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock { Text = "Kategori:", VerticalAlignment = VerticalAlignment.Center },
                        _categoryFilter,
                    },
                },
                _customSection,
            },
        };

        PopulatePropertyRows(primary);

        var tableHeader = new Border
        {
            Background = BrushResource("HeaderBrush", Color.FromRgb(0xe8, 0xe0, 0xcc)),
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            BorderThickness = new Thickness(1, 1, 1, 1),
            Padding = new Thickness(8, 5),
            Child = TwoColumn("Özellik adı", "Değer", bold: true),
        };

        var tableBody = new Border
        {
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            BorderThickness = new Thickness(1, 0, 1, 1),
            Background = Brushes.White,
            Child = _propertyRowsPanel,
        };

        var root = new StackPanel { Spacing = 2, Margin = new Thickness(6, 6, 8, 8) };
        root.Children.Add(dtGrid);
        root.Children.Add(detailGrid);
        root.Children.Add(new TextBlock { Text = "Özellikler", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
        root.Children.Add(filterBar);
        root.Children.Add(tableHeader);
        root.Children.Add(tableBody);
        return new ScrollViewer { Content = root };
    }

    private void PopulatePropertyRows((FunctionEdit Function, TextBox Description, List<PointRow> Points) primary)
    {
        _propertyRowsPanel.Children.Clear();
        _propertyRows.Clear();

        void AddReadOnly(string category, string name, Func<string> valueOf)
        {
            var box = new TextBlock { Text = valueOf(), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
            AddPropertyRow(category, name, box);
        }

        void AddMirrored(string category, string name, TextBox source)
        {
            var editor = new TextBox
            {
                Text = source.Text ?? string.Empty,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(2, 1),
            };
            editor.TextChanged += (_, _) =>
            {
                if (_syncing)
                {
                    return;
                }

                _syncing = true;
                try
                {
                    source.Text = editor.Text;
                }
                finally
                {
                    _syncing = false;
                }
            };
            source.TextChanged += (_, _) =>
            {
                if (_syncing)
                {
                    return;
                }

                _syncing = true;
                try
                {
                    editor.Text = source.Text ?? string.Empty;
                }
                finally
                {
                    _syncing = false;
                }
            };
            AddPropertyRow(category, name, editor);
        }

        var kindLabel = primary.Function is null ? "Genel cihaz" : EditorSession.FunctionKindLabel(primary.Function.Kind);
        AddReadOnly("Fonksiyon", "Fonksiyon tanımı", () => kindLabel);
        AddMirrored("Fonksiyon", "Fonksiyon metni", _primaryFunctionDesc);
        AddMirrored("Cihaz", "Cihaz açıklaması / Not", _description);
        AddMirrored("Bağlantı", "Bağlantı noktası adları", _quickPinNames);
        AddMirrored("Cihaz", "Ağ adresi (IP)", _address);
        AddMirrored("Cihaz", "Üretici", _manufacturer);
        AddMirrored("Cihaz", "Sipariş kodu", _orderCode);
        AddMirrored("Cihaz", "Tip tanımı", _type);
        AddReadOnly("Görüntüleme", "Çapraz referans gösterimi", () => "Proje ayarlarından (sayfa.kolon)");
    }

    private void AddPropertyRow(string category, string name, Control editor)
    {
        var rowGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 8 };
        var label = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(editor, 1);
        rowGrid.Children.AddRange([label, editor]);

        var border = new Border
        {
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 3),
            Child = rowGrid,
        };
        _propertyRows.Add((category, border));
        _propertyRowsPanel.Children.Add(border);
    }

    private void FilterPropertyRows()
    {
        var selected = _categoryFilter.SelectedItem as string;
        var all = selected is null || selected == PropertyCategories[0];
        foreach (var (category, row) in _propertyRows)
        {
            row.IsVisible = all || category == selected;
        }
    }

    private Grid BuildDisplayTab()
    {
        _rotationBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        _roleBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        return Form(
            ("Döndürme açısı", _rotationBox),
            ("Konum X (mm)", _xBox),
            ("Konum Y (mm)", _yBox),
            ("Görünüm rolü", _roleBox));
    }

    private DockPanel BuildPointsTab()
    {
        var symbolHeader = new Border
        {
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            BorderThickness = new Thickness(1),
            Background = BrushResource("PanelBrush", Color.FromRgb(0xfb, 0xf8, 0xf1)),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(4, 10, 8, 8),
        };

        var chooseSymbol = new Button
        {
            Content = "Sembol seç…",
            IsEnabled = _session is not null,
            Padding = new Thickness(12, 5),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chooseSymbol.Click += (_, _) => _ = ChooseSymbolForPrimary();

        var previewFrame = new Border
        {
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = _symbolPreview,
        };

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,12,*,Auto"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_symbolNameText, 2);
        Grid.SetColumn(chooseSymbol, 3);
        headerGrid.Children.AddRange([previewFrame, _symbolNameText, chooseSymbol]);
        symbolHeader.Child = headerGrid;

        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(4, 4, 8, 4) };
        foreach (var function in _data.Functions)
        {
            var description = new TextBox { Text = function.Description ?? string.Empty, PlaceholderText = "Fonksiyon açıklaması" };
            var rows = function.Points.Select(point => new PointRow(point)).ToList();
            _functions.Add((function, description, rows));

            var points = new StackPanel { Spacing = 6 };
            points.Children.Add(PointRow.Header());
            foreach (var row in rows)
            {
                points.Children.Add(row.Panel);
            }

            panel.Children.Add(new TextBlock
            {
                Text = $"{EditorSession.FunctionKindLabel(function.Kind)} ({function.Kind})",
                FontWeight = FontWeight.SemiBold,
            });
            panel.Children.Add(description);
            panel.Children.Add(points);
        }

        var root = new DockPanel();
        DockPanel.SetDock(symbolHeader, Avalonia.Controls.Dock.Top);
        root.Children.Add(symbolHeader);
        root.Children.Add(new ScrollViewer { Content = panel });
        return root;
    }

    private Control BuildProductTab()
    {
        var fields = Form(("Üretici", _manufacturer), ("Sipariş kodu", _orderCode), ("Tip", _type));
        var pickProduct = new Button
        {
            Content = "Kütüphaneden ürün seç…",
            MinWidth = 180,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = _session is not null,
        };
        var clearProduct = new Button
        {
            Content = "Temizle",
            MinWidth = 88,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        pickProduct.Click += async (_, _) =>
        {
            if (_session is null)
            {
                return;
            }

            var chosen = await new ProductSelectionWindow(_session, allowAddDevice: false, initialQuery: _orderCode.Text).ShowDialog<Ake.Library.LibraryProduct?>(this);
            if (chosen is null)
            {
                return;
            }

            _manufacturer.Text = chosen.Definition.Manufacturer;
            _orderCode.Text = chosen.Definition.OrderCode;
            _type.Text = chosen.Definition.TypeDesignation ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_description.Text) && !string.IsNullOrWhiteSpace(chosen.Definition.Description))
            {
                _description.Text = chosen.Definition.Description;
            }
        };
        clearProduct.Click += (_, _) =>
        {
            _manufacturer.Text = string.Empty;
            _orderCode.Text = string.Empty;
            _type.Text = string.Empty;
        };

        return new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 4),
            Children =
            {
                fields,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Margin = new Thickness(190, 0, 0, 0),
                    Children = { pickProduct, clearProduct },
                },
            },
        };
    }

    private Control BuildContactMirrorTab()
    {
        var rows = _data.ContactMirror ?? [];
        var stack = new StackPanel { Spacing = 0, Margin = new Thickness(6, 12, 6, 6) };

        Border MakeRow(string kind, string pins, string desc, string role, string location, bool header = false)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("120,110,*,80,90"), ColumnSpacing = 8 };
            var texts = new[] { kind, pins, desc, role, location };
            for (var i = 0; i < texts.Length; i++)
            {
                var cell = new TextBlock
                {
                    Text = texts[i],
                    FontWeight = header ? FontWeight.SemiBold : FontWeight.Normal,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(cell, i);
                grid.Children.Add(cell);
            }

            return new Border
            {
                BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
                BorderThickness = new Thickness(1, header ? 1 : 0, 1, 1),
                Background = header ? BrushResource("HeaderBrush", Color.FromRgb(0xe8, 0xe0, 0xcc)) : Brushes.White,
                Padding = new Thickness(8, 6),
                Child = grid,
            };
        }

        stack.Children.Add(MakeRow("Tür", "Uçlar", "Fonksiyon metni", "Rol", "Konum", header: true));
        foreach (var item in rows)
        {
            stack.Children.Add(MakeRow(item.Kind, item.Pins, item.Description ?? string.Empty, item.Role, item.Location));
        }

        if (rows.Count == 0)
        {
            stack.Children.Add(MakeRow("—", "—", "Kayıt yok", "—", "—"));
        }

        return new ScrollViewer { Content = stack };
    }

    private void WireSynchronization((FunctionEdit Function, TextBox Description, List<PointRow> Points) primary)
    {
        _tag.TextChanged += (_, _) => UpdateFullDt();
        _functionNode.SelectionChanged += (_, _) => UpdateFullDt();
        _locationNode.SelectionChanged += (_, _) => UpdateFullDt();

        _mainFunctionCheck.IsCheckedChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                _roleBox.SelectedIndex = _mainFunctionCheck.IsChecked == true ? 0 : 1;
            }
            finally
            {
                _syncing = false;
            }
        };
        _roleBox.SelectionChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                _mainFunctionCheck.IsChecked = _roleBox.SelectedIndex == 0;
            }
            finally
            {
                _syncing = false;
            }
        };

        if (primary.Function is null)
        {
            return;
        }

        _primaryFunctionDesc.Text = primary.Description.Text ?? string.Empty;
        _primaryFunctionDesc.TextChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                primary.Description.Text = _primaryFunctionDesc.Text;
            }
            finally
            {
                _syncing = false;
            }
        };
        primary.Description.TextChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            _syncing = true;
            try
            {
                _primaryFunctionDesc.Text = primary.Description.Text ?? string.Empty;
            }
            finally
            {
                _syncing = false;
            }
        };

        // Bağlantı noktası adları ve kodları: 1. sekmedeki tek satırlık hızlı giriş ile Noktalar sekmesi senkron kalır.
        void RefreshQuickPoints()
        {
            _syncing = true;
            try
            {
                _quickPinNames.Text = string.Join("¶", primary.Points.Select(p => p.NameText));
                _quickPinCodes.Text = string.Join("¶", primary.Points.Select(p => p.CodeText));
            }
            finally
            {
                _syncing = false;
            }
        }

        RefreshQuickPoints();

        _quickPinNames.TextChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            var parts = (_quickPinNames.Text ?? string.Empty).Split(['¶', ';', ',']);
            _syncing = true;
            try
            {
                for (var i = 0; i < primary.Points.Count && i < parts.Length; i++)
                {
                    primary.Points[i].NameText = parts[i].Trim();
                }
            }
            finally
            {
                _syncing = false;
            }
        };

        _quickPinCodes.TextChanged += (_, _) =>
        {
            if (_syncing)
            {
                return;
            }

            var parts = (_quickPinCodes.Text ?? string.Empty).Split(['¶', ';', ',']);
            _syncing = true;
            try
            {
                for (var i = 0; i < primary.Points.Count && i < parts.Length; i++)
                {
                    primary.Points[i].CodeText = parts[i].Trim();
                }
            }
            finally
            {
                _syncing = false;
            }
        };

        foreach (var pointRow in primary.Points)
        {
            pointRow.Changed += () =>
            {
                if (!_syncing)
                {
                    RefreshQuickPoints();
                }
            };
        }
    }

    private void UpdateFullDt()
    {
        var fn = _functionNode.SelectedItem is NodeChoice { Id: not null } f ? $"={f.Label}" : string.Empty;
        var loc = _locationNode.SelectedItem is NodeChoice { Id: not null } l ? $"+{l.Label}" : string.Empty;
        var tag = (_tag.Text ?? string.Empty).Trim();
        _fullDt.Text = $"{fn}{loc}-{tag}";
    }

    private void ShowExistingDevicesMenu(Control anchor)
    {
        if (_data.ExistingDevices is not { Count: > 0 } devices)
        {
            return;
        }

        var menu = new ContextMenu();
        foreach (var choice in devices)
        {
            var label = string.IsNullOrWhiteSpace(choice.Description)
                ? choice.Designation
                : $"{choice.Designation}  —  {choice.Description}";
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                _tag.Text = choice.ProductTag;
                _functionNode.SelectedItem = _data.FunctionNodes.FirstOrDefault(n => n.Id == choice.FunctionNode) ?? _data.FunctionNodes[0];
                _locationNode.SelectedItem = _data.LocationNodes.FirstOrDefault(n => n.Id == choice.LocationNode) ?? _data.LocationNodes[0];
                if (!string.IsNullOrWhiteSpace(choice.Manufacturer))
                {
                    _manufacturer.Text = choice.Manufacturer;
                }

                if (!string.IsNullOrWhiteSpace(choice.OrderCode))
                {
                    _orderCode.Text = choice.OrderCode;
                }

                if (!string.IsNullOrWhiteSpace(choice.TypeDesignation))
                {
                    _type.Text = choice.TypeDesignation;
                }

                if (string.IsNullOrWhiteSpace(_description.Text) && !string.IsNullOrWhiteSpace(choice.Description))
                {
                    _description.Text = choice.Description;
                }
            };
            menu.Items.Add(item);
        }

        menu.Open(anchor);
    }

    private async Task ChooseSymbolForPrimary()
    {
        if (_session is null)
        {
            return;
        }

        var primaryPins = _functions.FirstOrDefault(f => f.Function.Id == _data.PrimaryFunction).Points?.Count
            ?? _functions.FirstOrDefault().Points?.Count;
        var picker = new SymbolSelectionWindow(_session, requiredPins: primaryPins, initialSymbol: _assignedSymbol, showVariant: false);
        var chosen = await picker.ShowDialog<SymbolSelectionChoice?>(this);
        if (chosen is null)
        {
            return;
        }

        _assignedSymbol = chosen.SymbolId;
        var def = _session.LibrarySymbol(chosen.SymbolId);
        _symbolNameText.Text = def?.Name ?? chosen.SymbolId.ToString();
        _symbolPreview.Scene = _session.PreviewOf(chosen.SymbolId);
    }

    private static void AddField(Grid grid, int row, int col, string caption, Control field)
    {
        var label = new TextBlock { Text = caption, FontSize = 12, Opacity = 0.78 };
        Grid.SetRow(label, row);
        Grid.SetColumn(label, col);
        Grid.SetRow(field, row + 1);
        Grid.SetColumn(field, col);
        grid.Children.AddRange([label, field]);
    }

    private static Grid TwoColumn(string left, string right, bool bold = false)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("240,*"), ColumnSpacing = 8 };
        var l = new TextBlock { Text = left, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal };
        var r = new TextBlock { Text = right, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal };
        Grid.SetColumn(r, 1);
        grid.Children.AddRange([l, r]);
        return grid;
    }

    private static void Fill(ComboBox box, IReadOnlyList<NodeChoice> choices, StructureNodeId? selected)
    {
        box.ItemsSource = choices;
        box.SelectedItem = choices.FirstOrDefault(choice => choice.Id == selected) ?? choices[0];
        box.HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    private static Grid Form(params (string Label, Control Field)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowSpacing = 10, ColumnSpacing = 12, Margin = new Thickness(4, 14, 4, 4) };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var caption = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(caption, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(caption);
            grid.Children.Add(rows[i].Field);
        }

        return grid;
    }

    private DeviceEdits Collect()
    {
        var x = double.TryParse(_xBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedX) ? parsedX : _data.X;
        var y = double.TryParse(_yBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedY) ? parsedY : _data.Y;
        var rotation = _rotationBox.SelectedIndex >= 0 && _rotationBox.SelectedIndex < Rotations.Length
            ? Rotations[_rotationBox.SelectedIndex]
            : _data.RotationDeg;
        var role = _mainFunctionCheck.IsChecked == true ? PlacementRole.Main : PlacementRole.Reference;

        return new DeviceEdits(
            _data.Device,
            _tag.Text ?? string.Empty,
            _description.Text,
            _manufacturer.Text,
            _orderCode.Text,
            _type.Text,
            (_functionNode.SelectedItem as NodeChoice)?.Id,
            (_locationNode.SelectedItem as NodeChoice)?.Id,
            [.. _functions.SelectMany(function => function.Points.Select(row => row.ToEdit()))],
            [.. _functions.Select(function => (function.Function.Id, (string?)function.Description.Text))],
            _address.Text,
            _data.PrimaryPlacement,
            rotation,
            x,
            y,
            role,
            _assignedSymbol,
            new SortedDictionary<string, string>(_customProperties, StringComparer.Ordinal));
    }

    private void UpdateCustomSectionVisibility()
    {
        if (_customSection is null)
        {
            return;
        }

        var selected = _categoryFilter.SelectedItem as string;
        _customSection.IsVisible = selected is null or "Tüm kategoriler" or "Özel";
    }

    private void RefreshCustomProperties()
    {
        _customListPanel.Children.Clear();
        foreach (var key in _customProperties.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
        {
            var valueBox = new TextBox { Text = _customProperties[key], Width = 220 };
            var capturedKey = key;
            valueBox.TextChanged += (_, _) => _customProperties[capturedKey] = valueBox.Text ?? string.Empty;

            var removeBtn = new Button { Content = "×", Width = 30 };
            removeBtn.Click += (_, _) =>
            {
                _customProperties.Remove(capturedKey);
                RefreshCustomProperties();
            };

            _customListPanel.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = capturedKey, Width = 150, VerticalAlignment = VerticalAlignment.Center },
                    valueBox,
                    removeBtn,
                },
            });
        }
    }

    private static IBrush BrushResource(string key, Color fallback) =>
        Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    // Bir bağlantı noktasının satırı: ad, uç türü, bağlantı tipi, kontak türü, konektör kodu (ADR-0033).
    private sealed class PointRow
    {
        private readonly PointEdit _point;
        private readonly TextBox _name;
        private readonly ComboBox _kind;
        private readonly ComboBox _connector;
        private readonly ComboBox _contact;
        private readonly TextBox _code;

        public event Action? Changed;

        public PointRow(PointEdit point)
        {
            _point = point;
            _name = new TextBox { Text = point.Name, Width = 84 };
            _kind = PointAttributeLabels.Combo(PointAttributeLabels.Kinds, point.Attributes.Kind, 128);
            _connector = PointAttributeLabels.Combo(PointAttributeLabels.Connectors, point.Attributes.Connector, 150);
            _contact = PointAttributeLabels.Combo(PointAttributeLabels.Contacts, point.Attributes.Contact, 150);
            _code = new TextBox { Text = point.Attributes.ConnectorCode ?? string.Empty, Width = 120, MaxLength = PointAttributes.MaxConnectorCodeLength };

            _name.TextChanged += (_, _) => Changed?.Invoke();
            _code.TextChanged += (_, _) => Changed?.Invoke();

            Panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            Panel.Children.AddRange([_name, _kind, _connector, _contact, _code]);
        }

        public StackPanel Panel { get; }

        public string NameText
        {
            get => _name.Text ?? string.Empty;
            set => _name.Text = value;
        }

        public string CodeText
        {
            get => _code.Text ?? string.Empty;
            set => _code.Text = value;
        }

        public static StackPanel Header()
        {
            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            foreach (var (text, width) in new[] { ("Ad", 84), ("Uç türü", 128), ("Bağlantı", 150), ("Kontak", 150), ("Kod / pin", 120) })
            {
                header.Children.Add(new TextBlock { Text = text, Width = width, FontSize = 12, Opacity = 0.6 });
            }

            return header;
        }

        public PointEdit ToEdit() => new(
            _point.Id,
            _name.Text ?? string.Empty,
            new PointAttributes(
                PointAttributeLabels.Selected(_kind, PointAttributeLabels.Kinds),
                PointAttributeLabels.Selected(_connector, PointAttributeLabels.Connectors),
                PointAttributeLabels.Selected(_contact, PointAttributeLabels.Contacts),
                _code.Text));
    }
}
