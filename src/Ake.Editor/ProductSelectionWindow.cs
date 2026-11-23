using System.Globalization;
using Ake.Core;
using Ake.Core.Catalog;
using Ake.Core.Model;
using Ake.Library;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// Hazır ürün kataloğu ve ürün seçimi penceresi (Faz 6b-4, ADR-0056):
/// kategorili ürün tarayıcısı, teknik detay kartı, JSON/CSV içe aktarma, hızlı ürün ekleme ve
/// projeye yeni cihaz ekleme ya da seçili cihaza ürün atama için <see cref="LibraryProduct"/> döndürür.
/// </summary>
public sealed class ProductSelectionWindow : Window
{
    private sealed record CategoryRow(string? Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ProductRow(ProductSummary Summary)
    {
        public override string ToString()
        {
            var type = string.IsNullOrWhiteSpace(Summary.TypeDesignation) ? string.Empty : $" · {Summary.TypeDesignation}";
            var desc = string.IsNullOrWhiteSpace(Summary.Description) ? string.Empty : $" — {Summary.Description}";
            return $"{Summary.Manufacturer}  ·  {Summary.OrderCode}{type}{desc}";
        }
    }

    private readonly EditorSession _session;
    private readonly bool _allowAddDevice;
    private readonly Func<LibraryProduct, Result>? _onPlaceProduct;
    private readonly Action? _onLibraryChanged;
    private readonly TextBox _search = new() { PlaceholderText = "Üretici, sipariş kodu, tip veya kategori ara…" };
    private readonly ListBox _categories = new();
    private readonly ListBox _products = new();
    private readonly TextBlock _detailTitle = new() { FontWeight = FontWeight.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _detailBody = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly SceneView _symbolPreview = new() { IsHitTestVisible = false };
    private readonly TextBlock _previewEmpty = new()
    {
        FontSize = 11,
        Foreground = Brush("#6B6455"),
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(8),
        IsVisible = false,
    };
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _okButton = new() { Content = "Seç", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _addDeviceButton = new() { Content = "Sayfaya ekle", Classes = { "accent" }, MinWidth = 116, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _editProductButton = new() { Content = "Ürünü düzenle…", MinWidth = 118, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _editSymbolButton = new() { Content = "Sembolü düzenle…", MinWidth = 126, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _quickSaveButton = new() { Content = "Kütüphaneye kaydet", MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Center };

    private readonly Border _newProductPanel;
    private readonly TextBox _newManufacturer = new() { PlaceholderText = "Üretici" };
    private readonly TextBox _newOrderCode = new() { PlaceholderText = "Sipariş kodu" };
    private readonly TextBox _newType = new() { PlaceholderText = "Tip" };
    private readonly TextBox _newCategory = new() { PlaceholderText = "Kategori" };
    private readonly TextBox _newDescription = new() { PlaceholderText = "Açıklama" };
    private (string Manufacturer, string OrderCode)? _editingKey;

    /// <summary>Kütüphaneden doğrudan sayfaya cihaz eklendi ya da ürün/sembol düzenlendiyse <c>true</c>.</summary>
    public bool Changed { get; private set; }

    public ProductSelectionWindow(
        EditorSession session,
        bool allowAddDevice = false,
        string? initialQuery = null,
        Func<LibraryProduct, Result>? onPlaceProduct = null,
        Action? onLibraryChanged = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _allowAddDevice = allowAddDevice;
        _onPlaceProduct = onPlaceProduct;
        _onLibraryChanged = onLibraryChanged;

        Title = allowAddDevice ? "Ürün kütüphanesi" : "Kütüphaneden ürün seç";
        Width = 1040;
        Height = 660;
        MinWidth = 860;
        MinHeight = 540;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        if (!string.IsNullOrWhiteSpace(initialQuery))
        {
            _search.Text = initialQuery.Trim();
        }

        _search.TextChanged += (_, _) => RefreshProducts();
        _categories.SelectionChanged += (_, _) => RefreshProducts();
        _products.SelectionChanged += (_, _) => ShowDetail();
        _products.DoubleTapped += (_, _) =>
        {
            if (!_allowAddDevice && CurrentProduct() is { } chosen)
            {
                Close(chosen);
            }
        };
        EnableProductDrag();

        var leftPane = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_search, Avalonia.Controls.Dock.Top);
        var catHeader = new TextBlock { Text = "Kategoriler", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 6) };
        DockPanel.SetDock(catHeader, Avalonia.Controls.Dock.Top);
        _search.Margin = new Thickness(0);
        leftPane.Children.Add(_search);
        leftPane.Children.Add(catHeader);
        leftPane.Children.Add(new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Child = _categories,
        });

        var previewBox = new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Child = BuildPreviewHost(),
        };

        var detailGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,360") };
        var detailScroll = new ScrollViewer
        {
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { _detailTitle, _detailBody },
            },
        };
        Grid.SetColumn(detailScroll, 0);
        Grid.SetColumn(previewBox, 2);
        detailGrid.Children.Add(detailScroll);
        detailGrid.Children.Add(previewBox);

        var detailCard = new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#FAF8F3"),
            Padding = new Thickness(12, 10),
            Height = 220,
            Child = detailGrid,
        };

        var rightPane = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(detailCard, Avalonia.Controls.Dock.Bottom);
        detailCard.Margin = new Thickness(0, 8, 0, 0);
        rightPane.Children.Add(detailCard);
        rightPane.Children.Add(new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Child = _products,
        });

        var mainGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("240,12,*"), Margin = new Thickness(16, 14, 16, 8) };
        Grid.SetColumn(leftPane, 0);
        Grid.SetColumn(rightPane, 2);
        mainGrid.Children.Add(leftPane);
        mainGrid.Children.Add(rightPane);

        _newProductPanel = BuildQuickAddPanel();
        _newProductPanel.IsVisible = false;

        var importFileButton = new Button { Content = "Dosyadan al…", MinWidth = 116, HorizontalContentAlignment = HorizontalAlignment.Center };
        var toggleAddButton = new Button { Content = "Yeni ürün tanımla", MinWidth = 124, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancelButton = new Button { Content = allowAddDevice ? "Kapat" : "İptal", IsCancel = true, MinWidth = 84, HorizontalContentAlignment = HorizontalAlignment.Center };

        importFileButton.Click += (_, _) => _ = ImportFileAsync();
        toggleAddButton.Click += (_, _) => ToggleQuickAddPanel();
        _editProductButton.Click += (_, _) => OpenEditProductPanel();
        _editSymbolButton.Click += (_, _) => _ = EditSelectedProductSymbolAsync();
        _addDeviceButton.Click += (_, _) => PlaceSelectedProductOnPage();
        _okButton.Click += (_, _) => Close(CurrentProduct());
        cancelButton.Click += (_, _) => Close(null);

        _addDeviceButton.IsVisible = _allowAddDevice;
        _okButton.IsVisible = !_allowAddDevice;

        var leftActions = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _addDeviceButton, importFileButton, toggleAddButton, _editProductButton, _editSymbolButton },
        };
        foreach (Control btn in leftActions.Children)
        {
            btn.Margin = new Thickness(0, 0, 6, 4);
        }

        var rightActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _okButton, cancelButton } };
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(16, 6, 16, 14) };
        DockPanel.SetDock(leftActions, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(rightActions, Avalonia.Controls.Dock.Right);
        footer.Children.Add(leftActions);
        footer.Children.Add(rightActions);

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_newProductPanel, Avalonia.Controls.Dock.Bottom);
        _message.Margin = new Thickness(16, 2);
        root.Children.Add(footer);
        root.Children.Add(_message);
        root.Children.Add(_newProductPanel);
        root.Children.Add(mainGrid);
        Content = root;

        RefreshCategories();
        RefreshProducts();

        Opened += (_, _) => _search.Focus();
        Activated += (_, _) => ShowDetail();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private void EnableProductDrag()
    {
        PointerPressedEventArgs? pressed = null;
        var start = default(Avalonia.Point);
        _products.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_allowAddDevice && e.GetCurrentPoint(_products).Properties.IsLeftButtonPressed)
            {
                pressed = e;
                start = e.GetPosition(_products);
            }
        }, RoutingStrategies.Tunnel);
        _products.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);
        _products.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } args)
            {
                return;
            }

            if (!e.GetCurrentPoint(_products).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            var pos = e.GetPosition(_products);
            if (Math.Abs(pos.X - start.X) + Math.Abs(pos.Y - start.Y) < 6)
            {
                return;
            }

            pressed = null;
            if ((args.Source as StyledElement)?.DataContext is not ProductRow row)
            {
                return;
            }

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(
                MainWindow.ProductDragFormat,
                new MainWindow.ProductDragPayload(row.Summary.OrderCode, row.Summary.Manufacturer)));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Copy);
        }, RoutingStrategies.Tunnel);
    }

    private void ToggleQuickAddPanel()
    {
        if (_newProductPanel.IsVisible && _editingKey is null)
        {
            _newProductPanel.IsVisible = false;
            return;
        }

        _editingKey = null;
        _newManufacturer.Text = string.Empty;
        _newOrderCode.Text = string.Empty;
        _newType.Text = string.Empty;
        _newCategory.Text = string.Empty;
        _newDescription.Text = string.Empty;
        _quickSaveButton.Content = "Kütüphaneye kaydet";
        _newProductPanel.IsVisible = true;
        _newManufacturer.Focus();
    }

    private void OpenEditProductPanel()
    {
        var product = CurrentProduct();
        if (product is null)
        {
            return;
        }

        var d = product.Definition;
        _editingKey = (d.Manufacturer, d.OrderCode);
        _newManufacturer.Text = d.Manufacturer;
        _newOrderCode.Text = d.OrderCode;
        _newType.Text = d.TypeDesignation ?? string.Empty;
        _newCategory.Text = product.Category ?? string.Empty;
        _newDescription.Text = d.Description ?? string.Empty;
        _quickSaveButton.Content = "Değişikliği kaydet";
        _newProductPanel.IsVisible = true;
        _newOrderCode.Focus();
    }

    private Border BuildQuickAddPanel()
    {
        _quickSaveButton.Click += (_, _) =>
        {
            var mfg = _newManufacturer.Text?.Trim() ?? string.Empty;
            var code = _newOrderCode.Text?.Trim() ?? string.Empty;
            if (mfg.Length == 0 || code.Length == 0)
            {
                SetStatus("Üretici ve sipariş kodu zorunludur.", error: true);
                return;
            }

            if (_editingKey is { } orig)
            {
                var existing = _session.FindLibraryProduct(orig.OrderCode, orig.Manufacturer);
                if (existing is null)
                {
                    SetStatus("Düzenlenecek ürün kütüphanede bulunamadı.", error: true);
                    return;
                }

                var updated = existing with
                {
                    Definition = existing.Definition with
                    {
                        Manufacturer = mfg,
                        OrderCode = code,
                        TypeDesignation = Clean(_newType.Text),
                        Description = Clean(_newDescription.Text),
                    },
                    Category = Clean(_newCategory.Text) ?? "Diğer",
                };
                var res = _session.UpdateLibraryProduct(orig.Manufacturer, orig.OrderCode, updated);
                if (res.IsFailure)
                {
                    SetStatus(res.Error!.Message, error: true);
                    return;
                }

                _editingKey = null;
                Changed = true;
                SetStatus($"Ürün güncellendi: {mfg} {code}", error: false);
            }
            else
            {
                var product = new LibraryProduct(
                    new ProductDefinition(
                        mfg,
                        code,
                        [],
                        Clean(_newType.Text),
                        Clean(_newDescription.Text)),
                    "Kullanıcı tanımı",
                    "Kullanıcı verisi",
                    Clean(_newCategory.Text) ?? "Diğer");

                var saved = _session.SaveProductToLibrary(product);
                if (saved.IsFailure)
                {
                    SetStatus(saved.Error!.Message, error: true);
                    return;
                }

                Changed = true;
                SetStatus($"Ürün kütüphaneye kaydedildi: {mfg} {code}", error: false);
            }

            _newManufacturer.Text = string.Empty;
            _newOrderCode.Text = string.Empty;
            _newType.Text = string.Empty;
            _newCategory.Text = string.Empty;
            _newDescription.Text = string.Empty;
            _newProductPanel.IsVisible = false;
            RefreshCategories();
            RefreshProducts(selectCode: code, selectManufacturer: mfg);
        };

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,Auto"), ColumnSpacing = 6 };
        var fields = new Control[] { _newManufacturer, _newOrderCode, _newType, _newCategory, _newDescription, _quickSaveButton };
        for (var i = 0; i < fields.Length; i++)
        {
            Grid.SetColumn(fields[i], i);
            row.Children.Add(fields[i]);
        }

        return new Border
        {
            Margin = new Thickness(16, 4),
            Padding = new Thickness(10, 8),
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#F4EFE3"),
            Child = row,
        };
    }

    private async Task ImportFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Ürün kataloğu veya üretici dosyası içe aktar",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Katalog / üretici dosyaları")
                {
                    Patterns = ["*.json", "*.csv", "*.bmecat", "*.gsdml", "*.eds", "*.iodd", "*.aasx", "*.edz", "*.skycad", "*.xml"],
                },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        var imported = _session.ImportProductsFromFile(path);
        if (imported.IsFailure)
        {
            SetStatus(imported.Error!.Message, error: true);
            return;
        }

        Changed = true;
        SetStatus($"İçe aktarıldı: {imported.Value.Added} yeni, {imported.Value.Updated} güncellenen ürün.", error: false);
        RefreshCategories();
        RefreshProducts();
    }

    private void PlaceSelectedProductOnPage()
    {
        var product = CurrentProduct();
        if (product is null)
        {
            return;
        }

        var result = _onPlaceProduct is not null
            ? _onPlaceProduct(product)
            : ToResult(_session.PlaceLibraryProduct(product.Definition.OrderCode, product.Definition.Manufacturer));
        if (result.IsFailure)
        {
            SetStatus(result.Error!.Message, error: true);
            return;
        }

        Changed = true;
        SetStatus($"Sayfaya eklendi: {product.Definition.Manufacturer} {product.Definition.OrderCode}", error: false);

        static Result ToResult(Result<IReadOnlyList<PlacementId>> r) =>
            r.IsFailure ? Result.Failure(r.Error!) : Result.Success();
    }

    private async Task EditSelectedProductSymbolAsync()
    {
        var product = CurrentProduct();
        if (product is null)
        {
            return;
        }

        var (start, overwrite) = _session.StartingSymbolForProduct(product);
        var editor = new SymbolEditorWindow(_session, start, overwrite);
        await editor.ShowDialog(this);
        if (!editor.SavedToLibrary)
        {
            return;
        }

        var savedSymbolId = editor.LastSavedLibrarySymbolId ?? start.Id;
        var linked = _session.LinkSymbolToProduct(
            product.Definition.Manufacturer,
            product.Definition.OrderCode,
            savedSymbolId);
        if (linked.IsFailure)
        {
            SetStatus(linked.Error!.Message, error: true);
            return;
        }

        Changed = true;
        _onLibraryChanged?.Invoke();
        SetStatus($"Ürün sembolü güncellendi: {product.Definition.Manufacturer} {product.Definition.OrderCode}", error: false);
        ShowDetail();
    }

    private void RefreshCategories()
    {
        var current = (_categories.SelectedItem as CategoryRow)?.Value;
        var items = new List<CategoryRow> { new(null, $"Tüm ürünler ({_session.LibraryProductCount})") };
        items.AddRange(_session.ProductCategories().Select(category => new CategoryRow(category, category)));
        _categories.ItemsSource = items;
        var match = current is null ? 0 : items.FindIndex(item => item.Value == current);
        _categories.SelectedIndex = Math.Max(0, match);
    }

    private void RefreshProducts(string? selectCode = null, string? selectManufacturer = null)
    {
        var category = (_categories.SelectedItem as CategoryRow)?.Value;
        var items = _session.SearchProducts(_search.Text ?? string.Empty, category).Select(summary => new ProductRow(summary)).ToList();
        _products.ItemsSource = items;
        var idx = selectCode is not null
            ? items.FindIndex(row => row.Summary.OrderCode == selectCode
                && (selectManufacturer is null || row.Summary.Manufacturer == selectManufacturer))
            : (items.Count > 0 ? 0 : -1);
        _products.SelectedIndex = idx;
        ShowDetail();
    }

    private LibraryProduct? CurrentProduct() =>
        _products.SelectedItem is ProductRow row
            ? _session.FindLibraryProduct(row.Summary.OrderCode, row.Summary.Manufacturer)
            : null;

    private Control BuildPreviewHost()
    {
        var header = new Border
        {
            Background = Brush("#F4EFE3"),
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 3),
            Child = new TextBlock
            {
                Text = "Sembol önizlemesi",
                FontSize = 11,
                FontWeight = FontWeight.SemiBold,
                Foreground = Brush("#5E5645"),
            },
        };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(new Grid { Children = { _symbolPreview, _previewEmpty } });
        return panel;
    }

    private void ShowDetail()
    {
        var product = CurrentProduct();
        var hasLibrary = _session.LibraryPath is not null;
        _okButton.IsEnabled = product is not null;
        _addDeviceButton.IsEnabled = _allowAddDevice && _session.Project is not null && product is not null;
        _editProductButton.IsEnabled = hasLibrary && product is not null;
        _editSymbolButton.IsEnabled = hasLibrary && product is not null;

        if (product is null)
        {
            _symbolPreview.Scene = null;
            _previewEmpty.Text = "Ürün seçilmedi";
            _previewEmpty.IsVisible = true;
            _detailTitle.Text = _session.LibraryProductCount == 0 ? "Kütüphanede ürün yok" : "Ürün seçilmedi";
            _detailBody.Text = _session.LibraryProductCount == 0
                ? "Dosyadan al (.json/.csv/BMEcat/GSDML/EDS/IODD/AASX/EDZ) veya Yeni ürün tanımla ile kataloğa ürün ekleyebilirsiniz."
                : "Detayları görmek için listeden bir ürün seçin.";
            return;
        }

        var preview = _session.PreviewOfProduct(product.Definition);
        _symbolPreview.Scene = preview;
        _previewEmpty.IsVisible = preview is null;
        _previewEmpty.Text = "Bu ürün için tanımlı sembol yok";

        var d = product.Definition;
        _detailTitle.Text = $"{d.Manufacturer}  ·  {d.OrderCode}" + (string.IsNullOrWhiteSpace(d.TypeDesignation) ? string.Empty : $"  ({d.TypeDesignation})");

        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Description))
        {
            lines.Add(d.Description);
        }

        var meta = string.Join("  ·  ", new[]
        {
            string.IsNullOrWhiteSpace(product.Category) ? null : $"Kategori: {product.Category}",
            d.Mounting is { } m ? string.Format(CultureInfo.InvariantCulture, "Montaj: {0} ({1:0.#} × {2:0.#} × {3:0.#} mm)", m.Type, m.WidthMm, m.HeightMm, m.DepthMm) : null,
        }.Where(part => part is not null));
        if (meta.Length > 0)
        {
            lines.Add(meta);
        }

        if (d.Functions.Count > 0)
        {
            var fns = string.Join(", ", d.Functions.Select(f => $"{f.Description ?? f.Kind.ToString()} ({string.Join('/', f.ConnectionPoints)})"));
            lines.Add($"Fonksiyonlar ({d.Functions.Count}): {fns}");
        }

        if (product.Properties is { Count: > 0 })
        {
            var props = string.Join(" · ", product.Properties.Select(p => $"{p.Key}: {p.Value}{(string.IsNullOrWhiteSpace(p.Unit) ? string.Empty : " " + p.Unit)}"));
            lines.Add($"Teknik veriler: {props}");
        }

        _detailBody.Text = string.Join("\n", lines);
    }

    private void SetStatus(string text, bool error)
    {
        _message.Foreground = Brush(error ? "#C0392B" : "#2E6F40");
        _message.Text = text;
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
