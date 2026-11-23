using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using FluentIcons.Avalonia;

using DockSide = Avalonia.Controls.Dock;

namespace Ake.Editor;

/// <summary>Görsel sembol seçimi sonucu: seçilen kütüphane sembolü ve yerleştirme açısı (ADR-0054).</summary>
public sealed record SymbolSelectionChoice(SymbolId SymbolId, int RotationDeg = 0);

/// <summary>
/// Sağ tık menüsünden veya şeritten açılan görsel sembol seçimi penceresi (ADR-0054):
/// sol tarafta süzgeç, kategori ağacı / düz liste, kategori oluşturma ve sembol açıklaması;
/// sağ tarafta sembollerin küçük önizlemelerini gösteren ızgara, yön (varyant) seçimi ve
/// kategori klasörlerine sürükle-bırak ile sembol taşıma.
/// </summary>
public sealed class SymbolSelectionWindow : Window
{
    private const int MaxVisibleTiles = 240;

    private sealed record SymbolDragPayload(SymbolId SymbolId, string Name);

    private sealed record PinFilterChoice(string Label, int? ExactPins, int? MinPins)
    {
        public override string ToString() => Label;
    }

    private static readonly DataFormat<SymbolDragPayload> SymbolDragFormat =
        DataFormat.CreateInProcessFormat<SymbolDragPayload>("aktapokus-electric/sembol-kategori");

    private static readonly (string Label, int Rotation)[] Variants =
    [
        ("Varsayılan yön (0°)", 0),
        ("90° döndür", 90),
        ("180° döndür", 180),
        ("270° döndür", 270),
    ];

    private static readonly PinFilterChoice[] PinFilters =
    [
        new("Tüm uçlar", null, null),
        new("1 uçlu", 1, null),
        new("2 uçlu", 2, null),
        new("3 uçlu", 3, null),
        new("4 uçlu", 4, null),
        new("6+ uçlu", null, 6),
    ];

    private readonly EditorSession _session;
    private readonly int? _requiredPins;
    private readonly SymbolId? _initialSymbol;

    private readonly TextBox _searchBox = new() { PlaceholderText = "Sembol veya kategori ara…" };
    private readonly CheckBox? _fitPinsBox;
    private readonly ComboBox? _pinFilterBox;
    private readonly TreeView _categoryTree = new();
    private readonly ListBox _flatList = new() { IsVisible = false };
    private readonly ToggleButton _treeToggle = new() { Content = "Ağaç", IsChecked = true, Padding = new Thickness(12, 3) };
    private readonly ToggleButton _listToggle = new() { Content = "Liste", Padding = new Thickness(12, 3) };
    private readonly Button _newCategoryButton = new() { Content = "Yeni kategori…", Padding = new Thickness(10, 3) };
    private readonly TextBlock _summaryTitle = new() { FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summaryDetail = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly Button _renameSymbolButton = new() { Content = "Yeniden adlandır…", Padding = new Thickness(8, 3), FontSize = 12, IsEnabled = false };
    private readonly Button _editSymbolButton = new() { Content = "Sembolü düzenle…", Padding = new Thickness(8, 3), FontSize = 12, IsEnabled = false };
    private readonly WrapPanel _tilePanel = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _gridInfo = new() { FontSize = 11, Opacity = 0.7 };
    private readonly ComboBox _variantBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _importFileButton = new() { Content = "Dosyadan al (.elmt / .dxf)…" };
    private readonly Button _importFolderButton = new() { Content = "Klasörden al…" };
    private readonly Button _importDiscoveredButton = new() { Content = "Bulunan kütüphaneleri yükle" };
    private readonly TextBlock _importStatus = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Button _okButton = new() { Content = "Tamam", IsDefault = true, MinWidth = 92, HorizontalContentAlignment = HorizontalAlignment.Center };

    /// <summary>Pencere açıkken kütüphaneye yeni sembol eklendi veya kategori düzenlendiyse <c>true</c>.</summary>
    public bool Changed { get; private set; }

    private readonly List<(SymbolRow Row, Border Tile)> _tiles = [];
    private IReadOnlyList<SymbolRow> _allRows = [];
    private CategoryNode? _selectedCategory;
    private SymbolRow? _selectedSymbol;
    private bool _syncingSelection;

    public SymbolSelectionWindow(EditorSession session, int? requiredPins = null, SymbolId? initialSymbol = null, bool showVariant = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _requiredPins = requiredPins;
        _initialSymbol = initialSymbol;

        Title = requiredPins is { } pins ? $"Sembol seçimi ({pins} uçlu)" : "Sembol seçimi";
        Width = 980;
        Height = 620;
        MinWidth = 800;
        MinHeight = 480;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        if (requiredPins is { } pinCount)
        {
            _fitPinsBox = new CheckBox
            {
                Content = $"Yalnızca {pinCount} uçlu semboller",
                IsChecked = true,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _fitPinsBox.IsCheckedChanged += (_, _) => Reload();
        }
        else
        {
            _pinFilterBox = new ComboBox
            {
                ItemsSource = PinFilters,
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Margin = new Thickness(0, 4, 0, 0),
            };
            _pinFilterBox.SelectionChanged += (_, _) => Reload();
        }

        _searchBox.TextChanged += (_, _) => Reload();

        _categoryTree.Styles.Add(new Style(x => x.OfType<TreeViewItem>())
        {
            Setters =
            {
                new Setter(
                    TreeViewItem.IsExpandedProperty,
                    new Binding(nameof(CategoryNode.Expanded)) { Mode = BindingMode.TwoWay }),
            },
        });
        _categoryTree.ItemTemplate = new FuncTreeDataTemplate<CategoryNode>(
            (node, _) => new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new SymbolIcon
                    {
                        Symbol = node.Children.Count > 0
                            ? FluentIcons.Common.Symbol.Folder
                            : FluentIcons.Common.Symbol.FolderOpen,
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xC7, 0x8A, 0x20)),
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                    new TextBlock
                    {
                        Text = node.Label,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextTrimming = TextTrimming.CharacterEllipsis,
                    },
                },
            },
            node => node.Children);
        _categoryTree.SelectionChanged += (_, _) =>
        {
            if (_syncingSelection)
            {
                return;
            }

            _selectedCategory = _categoryTree.SelectedItem as CategoryNode;
            RebuildTiles();
        };
        ConfigureCategoryTreeDropTarget();

        _flatList.ItemTemplate = new FuncDataTemplate<SymbolRow>((row, _) =>
            new TextBlock { Text = $"{row.Name}  ·  {row.Pins} uç", TextTrimming = TextTrimming.CharacterEllipsis });
        _flatList.SelectionChanged += (_, _) =>
        {
            if (_syncingSelection || _flatList.SelectedItem is not SymbolRow row)
            {
                return;
            }

            SelectSymbol(row, syncList: false);
        };
        _flatList.DoubleTapped += (_, _) => ConfirmSelection();
        EnableFlatListDrag();

        _treeToggle.Click += (_, _) => SetBrowseMode(treeMode: true);
        _listToggle.Click += (_, _) => SetBrowseMode(treeMode: false);
        _newCategoryButton.Click += async (_, _) => await CreateCategoryAsync();
        _renameSymbolButton.Click += async (_, _) => await RenameSelectedSymbolAsync();
        _editSymbolButton.Click += async (_, _) => await EditSelectedSymbolAsync();

        _variantBox.ItemsSource = Variants.Select(v => v.Label).ToList();
        _variantBox.SelectedIndex = 0;
        _variantBox.SelectionChanged += (_, _) => RebuildTiles();

        var cancelButton = new Button { Content = "İptal", IsCancel = true, MinWidth = 92, HorizontalContentAlignment = HorizontalAlignment.Center };
        _okButton.Click += (_, _) => ConfirmSelection();
        cancelButton.Click += (_, _) => Close(null);

        var hasLibrary = _session.LibraryPath is not null;
        _importFileButton.IsEnabled = hasLibrary;
        _importFolderButton.IsEnabled = hasLibrary;
        _importDiscoveredButton.IsEnabled = hasLibrary;
        _newCategoryButton.IsEnabled = hasLibrary;
        _importFileButton.Click += async (_, _) => await ImportFileAsync();
        _importFolderButton.Click += async (_, _) => await ImportFolderAsync();
        _importDiscoveredButton.Click += async (_, _) => await ImportDiscoveredAsync();

        // Sol panel: süzgeç, kategori ağacı/liste, yeni kategori, sembol özeti ve görünüm anahtarı.
        var leftTop = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 6) };
        leftTop.Children.Add(new TextBlock { Text = "Süzgeç:", FontWeight = FontWeight.SemiBold });
        leftTop.Children.Add(_searchBox);
        if (_fitPinsBox is not null)
        {
            leftTop.Children.Add(_fitPinsBox);
        }
        else if (_pinFilterBox is not null)
        {
            leftTop.Children.Add(_pinFilterBox);
        }

        var modeBar = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var browseButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { _treeToggle, _listToggle },
        };
        DockPanel.SetDock(browseButtons, DockSide.Left);
        DockPanel.SetDock(_newCategoryButton, DockSide.Right);
        modeBar.Children.Add(browseButtons);
        modeBar.Children.Add(_newCategoryButton);

        var summaryActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 4, 0, 0),
            Children = { _renameSymbolButton, _editSymbolButton },
        };

        var summaryBox = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            Background = BrushResource("PanelBrush", Color.FromRgb(0xfb, 0xf8, 0xf1)),
            Padding = new Thickness(8, 6),
            Margin = new Thickness(0, 6, 0, 0),
            MinHeight = 56,
            Child = new StackPanel
            {
                Spacing = 2,
                Children = { _summaryTitle, _summaryDetail, summaryActions },
            },
        };

        var browserHost = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            Background = BrushResource("PanelBrush", Color.FromRgb(0xfb, 0xf8, 0xf1)),
            Child = new Grid
            {
                Children = { _categoryTree, _flatList },
            },
        };

        var leftPane = new DockPanel { Width = 290 };
        DockPanel.SetDock(leftTop, DockSide.Top);
        DockPanel.SetDock(modeBar, DockSide.Bottom);
        DockPanel.SetDock(summaryBox, DockSide.Bottom);
        leftPane.Children.Add(leftTop);
        leftPane.Children.Add(modeBar);
        leftPane.Children.Add(summaryBox);
        leftPane.Children.Add(browserHost);

        // Sağ panel: görsel ızgara ve yön (varyant) seçimi.
        var variantRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 10,
            Margin = new Thickness(0, 8, 0, 0),
            IsVisible = showVariant,
        };
        var variantLabel = new TextBlock { Text = "Varyant:", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(_variantBox, 1);
        Grid.SetColumn(_gridInfo, 2);
        _gridInfo.VerticalAlignment = VerticalAlignment.Center;
        variantRow.Children.Add(variantLabel);
        variantRow.Children.Add(_variantBox);
        variantRow.Children.Add(_gridInfo);

        var gridHost = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
            Background = Brushes.White,
            Child = new ScrollViewer
            {
                Padding = new Thickness(6),
                Content = _tilePanel,
            },
        };

        var rightPane = new DockPanel();
        DockPanel.SetDock(variantRow, DockSide.Bottom);
        rightPane.Children.Add(variantRow);
        rightPane.Children.Add(gridHost);

        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("290,12,*"),
        };
        Grid.SetColumn(rightPane, 2);
        body.Children.Add(leftPane);
        body.Children.Add(rightPane);

        var importButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Children = { _importFileButton, _importFolderButton, _importDiscoveredButton },
        };

        var actionButtons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { _okButton, cancelButton },
        };

        var buttons = new DockPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        DockPanel.SetDock(actionButtons, DockSide.Right);
        buttons.Children.Add(actionButtons);
        buttons.Children.Add(importButtons);

        var root = new DockPanel { Margin = new Thickness(14) };
        DockPanel.SetDock(buttons, DockSide.Bottom);
        DockPanel.SetDock(_importStatus, DockSide.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(_importStatus);
        root.Children.Add(body);
        Content = root;

        Opened += (_, _) =>
        {
            _searchBox.Focus();
            Reload();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private int SelectedRotation =>
        _variantBox.SelectedIndex >= 0 && _variantBox.SelectedIndex < Variants.Length
            ? Variants[_variantBox.SelectedIndex].Rotation
            : 0;

    private void SetBrowseMode(bool treeMode)
    {
        _treeToggle.IsChecked = treeMode;
        _listToggle.IsChecked = !treeMode;
        _categoryTree.IsVisible = treeMode;
        _flatList.IsVisible = !treeMode;
        RebuildTiles();
    }

    private void Reload(string? selectCategoryPath = null, SymbolId? selectSymbolId = null)
    {
        var query = _searchBox.Text ?? string.Empty;
        int? exactPins = null;
        int? minPins = null;

        if (_requiredPins is { } req)
        {
            exactPins = _fitPinsBox?.IsChecked == true ? req : null;
        }
        else if (_pinFilterBox?.SelectedItem is PinFilterChoice choice)
        {
            exactPins = choice.ExactPins;
            minPins = choice.MinPins;
        }

        var rows = _session.SearchLibrary(query, exactPins);
        if (minPins is { } minCount)
        {
            rows = [.. rows.Where(row => row.Pins >= minCount)];
        }

        _allRows = rows;

        var isSearching = !string.IsNullOrWhiteSpace(query) || exactPins is not null || minPins is not null;
        var extraCategories = isSearching ? null : _session.SymbolCategories();
        var groups = SymbolPalette.Build(_allRows, expanded: isSearching, extraCategories);
        var rootNode = new CategoryNode(
            "Tüm semboller",
            string.Empty,
            _allRows,
            [.. groups.Select(ToCategoryNode)],
            expanded: true);

        CategoryNode chosenCategory = rootNode;
        if (!string.IsNullOrEmpty(selectCategoryPath)
            && TryFindAndExpandCategory(rootNode, selectCategoryPath, out var matched)
            && matched is not null)
        {
            chosenCategory = matched;
        }

        _syncingSelection = true;
        try
        {
            _categoryTree.ItemsSource = new[] { rootNode };
            _flatList.ItemsSource = _allRows;
            _selectedCategory = chosenCategory;
            _categoryTree.SelectedItem = chosenCategory;
        }
        finally
        {
            _syncingSelection = false;
        }

        RebuildTiles();

        var preferredId = selectSymbolId ?? _initialSymbol;
        var visibleSource = _treeToggle.IsChecked == true && _selectedCategory is not null
            ? _selectedCategory.Rows
            : _allRows;
        var initial = preferredId is { } id
            ? visibleSource.FirstOrDefault(row => row.Id == id) ?? visibleSource.FirstOrDefault()
            : visibleSource.FirstOrDefault();
        SelectSymbol(initial, syncList: true);
    }

    private static CategoryNode ToCategoryNode(SymbolGroup group)
    {
        var childCategories = group.Items.OfType<SymbolGroup>().Select(ToCategoryNode).ToList();
        var rows = FlattenRows(group).ToList();
        return new CategoryNode(group.Name, group.CategoryPath, rows, childCategories, group.Expanded);
    }

    private static bool TryFindAndExpandCategory(CategoryNode node, string path, out CategoryNode? found)
    {
        if (string.Equals(node.CategoryPath, path, StringComparison.OrdinalIgnoreCase))
        {
            found = node;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryFindAndExpandCategory(child, path, out found))
            {
                node.Expanded = true;
                return true;
            }
        }

        found = null;
        return false;
    }

    private static IEnumerable<SymbolRow> FlattenRows(SymbolGroup group)
    {
        foreach (var item in group.Items)
        {
            if (item is SymbolRow row)
            {
                yield return row;
            }
            else if (item is SymbolGroup child)
            {
                foreach (var nested in FlattenRows(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private void RebuildTiles()
    {
        var source = _treeToggle.IsChecked == true && _selectedCategory is not null
            ? _selectedCategory.Rows
            : _allRows;
        var visible = source.Take(MaxVisibleTiles).ToList();
        var rotation = SelectedRotation;
        var hoverFill = new SolidColorBrush(Color.FromRgb(0xF2, 0xF7, 0xFD));

        _tilePanel.Children.Clear();
        _tiles.Clear();

        foreach (var row in visible)
        {
            var preview = new SceneView
            {
                Width = 104,
                Height = 76,
                IsHitTestVisible = false,
                Scene = _session.PreviewOf(row.Id, rotation),
            };
            var caption = new TextBlock
            {
                Text = row.Name,
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(3, 1, 3, 3),
            };
            var stack = new DockPanel();
            DockPanel.SetDock(caption, DockSide.Bottom);
            stack.Children.Add(caption);
            stack.Children.Add(preview);

            var tile = new Border
            {
                Width = 114,
                Height = 104,
                Margin = new Thickness(1),
                CornerRadius = new CornerRadius(1),
                BorderThickness = new Thickness(1),
                BorderBrush = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8)),
                Background = Brushes.White,
                Cursor = new Cursor(StandardCursorType.Hand),
                Child = stack,
            };
            ToolTip.SetTip(tile, row.Label);
            AttachTileInteractions(tile, row, hoverFill);

            _tiles.Add((row, tile));
            _tilePanel.Children.Add(tile);
        }

        _gridInfo.Text = source.Count > MaxVisibleTiles
            ? $"{MaxVisibleTiles} / {source.Count} sembol (daraltmak için arayın)"
            : $"{source.Count} sembol";

        if (_selectedSymbol is not null && visible.All(row => row.Id != _selectedSymbol.Id))
        {
            SelectSymbol(visible.FirstOrDefault(), syncList: true);
        }
        else
        {
            UpdateTileBorders();
        }
    }

    private void AttachTileInteractions(Border tile, SymbolRow row, IBrush hoverFill)
    {
        PointerPressedEventArgs? pressed = null;
        var start = default(Point);

        tile.PointerEntered += (_, _) =>
        {
            if (_selectedSymbol is null || row.Id != _selectedSymbol.Id)
            {
                tile.Background = hoverFill;
            }
        };
        tile.PointerExited += (_, _) =>
        {
            if (_selectedSymbol is null || row.Id != _selectedSymbol.Id)
            {
                tile.Background = Brushes.White;
            }
        };
        tile.PointerPressed += (_, e) =>
        {
            SelectSymbol(row, syncList: true);
            if (e.ClickCount >= 2)
            {
                pressed = null;
                ConfirmSelection();
                return;
            }

            if (_session.LibraryPath is not null && e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
            {
                pressed = e;
                start = e.GetPosition(tile);
            }
        };
        tile.PointerReleased += (_, _) => pressed = null;
        tile.PointerMoved += async (_, e) =>
        {
            if (pressed is not { } args)
            {
                return;
            }

            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            var position = e.GetPosition(tile);
            if (Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 6)
            {
                return;
            }

            pressed = null;
            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SymbolDragFormat, new SymbolDragPayload(row.Id, row.Name)));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
        };

        if (_session.LibraryPath is not null)
        {
            var renameItem = new MenuItem { Header = "Yeniden adlandır…" };
            renameItem.Click += async (_, _) => await RenameSelectedSymbolAsync(row);
            var editItem = new MenuItem { Header = "Sembolü düzenle…" };
            editItem.Click += async (_, _) => await EditSelectedSymbolAsync(row);
            var moveItem = new MenuItem { Header = "Kategoriye taşı…" };
            moveItem.Click += async (_, _) => await PromptMoveSymbolCategoryAsync(row);
            tile.ContextMenu = new ContextMenu { ItemsSource = new Control[] { renameItem, editItem, new Separator(), moveItem } };
        }
    }

    private void EnableFlatListDrag()
    {
        PointerPressedEventArgs? pressed = null;
        var start = default(Point);

        _flatList.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (_session.LibraryPath is not null
                && e.GetCurrentPoint(_flatList).Properties.IsLeftButtonPressed
                && FindSymbolRow(e.Source) is not null)
            {
                pressed = e;
                start = e.GetPosition(_flatList);
            }
            else
            {
                pressed = null;
            }
        }, RoutingStrategies.Tunnel);

        _flatList.AddHandler(PointerReleasedEvent, (_, _) => pressed = null, RoutingStrategies.Tunnel);

        _flatList.AddHandler(PointerMovedEvent, async (_, e) =>
        {
            if (pressed is not { } args)
            {
                return;
            }

            if (!e.GetCurrentPoint(_flatList).Properties.IsLeftButtonPressed)
            {
                pressed = null;
                return;
            }

            var position = e.GetPosition(_flatList);
            if (Math.Abs(position.X - start.X) + Math.Abs(position.Y - start.Y) < 6)
            {
                return;
            }

            pressed = null;
            if (FindSymbolRow(args.Source) is not { } row)
            {
                return;
            }

            var transfer = new DataTransfer();
            transfer.Add(DataTransferItem.Create(SymbolDragFormat, new SymbolDragPayload(row.Id, row.Name)));
            await DragDrop.DoDragDropAsync(args, transfer, DragDropEffects.Move);
        }, RoutingStrategies.Tunnel);
    }

    private void ConfigureCategoryTreeDropTarget()
    {
        DragDrop.SetAllowDrop(_categoryTree, true);
        _categoryTree.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            var valid = _session.LibraryPath is not null
                && e.DataTransfer.TryGetValue(SymbolDragFormat) is not null
                && FindCategoryNode(e.Source) is { CategoryPath.Length: > 0 };
            e.DragEffects = valid ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        });
        _categoryTree.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (_session.LibraryPath is null
                || e.DataTransfer.TryGetValue(SymbolDragFormat) is not { } dragged
                || FindCategoryNode(e.Source) is not { CategoryPath.Length: > 0 } target)
            {
                return;
            }

            var targetPath = target.CategoryPath;
            e.Handled = true;
            Dispatcher.UIThread.Post(() => MoveSymbolToCategory(dragged, targetPath));
        });
    }

    private void MoveSymbolToCategory(SymbolDragPayload dragged, string targetCategory)
    {
        var moved = _session.MoveSymbolToCategory(dragged.SymbolId, targetCategory);
        if (moved.IsFailure)
        {
            ShowImportStatus(moved.Error!.Message, isError: true);
            return;
        }

        Changed = true;
        ShowImportStatus($"'{dragged.Name}' → '{targetCategory}' kategorisine taşındı.", isError: false);
        Reload(selectCategoryPath: _selectedCategory?.CategoryPath, selectSymbolId: dragged.SymbolId);
    }

    private async Task PromptMoveSymbolCategoryAsync(SymbolRow row)
    {
        var initial = !string.IsNullOrWhiteSpace(row.Category)
            ? row.Category!
            : (_selectedCategory?.CategoryPath ?? string.Empty);
        var target = await new TextPromptDialog("Hedef kategori (ör. Kumanda veya Özel/Motor)", initial)
            .ShowDialog<string?>(this);
        if (!string.IsNullOrWhiteSpace(target))
        {
            MoveSymbolToCategory(new SymbolDragPayload(row.Id, row.Name), target);
        }
    }

    private async Task CreateCategoryAsync()
    {
        var prefix = string.IsNullOrEmpty(_selectedCategory?.CategoryPath)
            ? string.Empty
            : _selectedCategory.CategoryPath + "/";
        var entered = await new TextPromptDialog("Yeni kategori (alt klasör için '/' kullanın)", prefix)
            .ShowDialog<string?>(this);
        if (entered is null)
        {
            return;
        }

        var created = _session.CreateSymbolCategory(entered);
        if (created.IsFailure)
        {
            ShowImportStatus(created.Error!.Message, isError: true);
            return;
        }

        var normalized = string.Join(
            '/',
            entered.Trim().Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        Changed = true;
        ShowImportStatus($"Kategori oluşturuldu: {normalized}", isError: false);
        Reload(selectCategoryPath: normalized);
    }

    private static CategoryNode? FindCategoryNode(object? source)
    {
        for (var current = source as StyledElement; current is not null; current = current.Parent)
        {
            if (current.DataContext is CategoryNode node)
            {
                return node;
            }
        }

        return null;
    }

    private static SymbolRow? FindSymbolRow(object? source)
    {
        for (var current = source as StyledElement; current is not null; current = current.Parent)
        {
            if (current.DataContext is SymbolRow row)
            {
                return row;
            }
        }

        return null;
    }

    private void SelectSymbol(SymbolRow? row, bool syncList)
    {
        _selectedSymbol = row;
        _okButton.IsEnabled = row is not null;
        var canEdit = row is not null && _session.LibraryPath is not null;
        _renameSymbolButton.IsEnabled = canEdit;
        _editSymbolButton.IsEnabled = canEdit;

        if (syncList && row is not null)
        {
            _syncingSelection = true;
            try
            {
                _flatList.SelectedItem = row;
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        if (row is null)
        {
            _summaryTitle.Text = _session.LibraryPath is null ? "Kütüphane açık değil" : "Sembol seçilmedi";
            _summaryDetail.Text = _session.LibraryPath is null
                ? "Ekle > Kütüphane aç ile bir sembol kütüphanesi açın."
                : "Soldan kategori seçin, yeni kategori ekleyin veya sürükleyip klasöre bırakın.";
        }
        else
        {
            var def = _session.LibrarySymbol(row.Id);
            var letter = def?.TagLetter ?? EditorSession.TagLetterFor(row.Name);
            var category = string.IsNullOrWhiteSpace(row.Category) ? string.Empty : $"  ·  {row.Category}";
            _summaryTitle.Text = $"{letter} — {row.Name}";
            var detail = !string.IsNullOrWhiteSpace(def?.DeviceName) ? def!.DeviceName : row.Name;
            _summaryDetail.Text = $"{detail}\n{row.Pins} bağlantı ucu{category}";
        }

        UpdateTileBorders();
    }

    private void UpdateTileBorders()
    {
        var normalBorder = BrushResource("LineBrush", Color.FromRgb(0xd6, 0xcd, 0xb8));
        var accentBorder = BrushResource("AccentBrush", Color.FromRgb(0x1f, 0x6f, 0xd8));
        var softFill = BrushResource("AccentSoftBrush", Color.FromRgb(0xdc, 0xea, 0xfb));

        foreach (var (row, tile) in _tiles)
        {
            var isSelected = _selectedSymbol is not null && row.Id == _selectedSymbol.Id;
            tile.BorderThickness = new Thickness(isSelected ? 2 : 1);
            tile.BorderBrush = isSelected ? accentBorder : normalBorder;
            tile.Background = isSelected ? softFill : Brushes.White;
        }
    }

    private async Task RenameSelectedSymbolAsync(SymbolRow? target = null)
    {
        var row = target ?? _selectedSymbol;
        if (row is null || _session.LibraryPath is null)
        {
            return;
        }

        var newName = await new TextPromptDialog("Sembol adını değiştir", row.Name).ShowDialog<string?>(this);
        if (newName is null)
        {
            return;
        }

        var renamed = _session.RenameLibrarySymbol(row.Id, newName);
        if (renamed.IsFailure)
        {
            ShowImportStatus(renamed.Error!.Message, isError: true);
            return;
        }

        Changed = true;
        ShowImportStatus($"Sembol adı güncellendi: {newName.Trim()}", isError: false);
        Reload(selectCategoryPath: _selectedCategory?.CategoryPath, selectSymbolId: row.Id);
    }

    private async Task EditSelectedSymbolAsync(SymbolRow? target = null)
    {
        var row = target ?? _selectedSymbol;
        if (row is null || _session.LibraryPath is null || _session.LibrarySymbol(row.Id) is not { } symbol)
        {
            return;
        }

        var isUserEditable = row.Category == SymbolPalette.UserCategory
            || symbol.License == "Kullanıcı çizimi"
            || (symbol.Source?.StartsWith("Düzenlendi", StringComparison.Ordinal) == true);
        var editor = new SymbolEditorWindow(_session, symbol, isUserEditable);
        await editor.ShowDialog(this);
        if (!editor.SavedToLibrary)
        {
            return;
        }

        Changed = true;
        var savedId = editor.LastSavedLibrarySymbolId ?? row.Id;
        ShowImportStatus("Sembol kütüphanede güncellendi.", isError: false);
        Reload(selectCategoryPath: _selectedCategory?.CategoryPath, selectSymbolId: savedId);
    }

    private void ConfirmSelection()
    {
        if (_selectedSymbol is { } row)
        {
            Close(new SymbolSelectionChoice(row.Id, SelectedRotation));
        }
    }

    private async Task ImportFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Sembol dosyası al (.elmt / .dxf / .dxf.zip / .dwg / .sembol.json)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Sembol dosyaları") { Patterns = ["*.elmt", "*.dxf", "*.dxf.zip", "*.dwg", "*.sembol.json"] },
                FilePickerFileTypes.All,
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        await RunImportPathAsync(path);
    }

    private async Task ImportFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Sembol klasörü al (.elmt / .dxf / .dwg)",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        await RunImportPathAsync(path);
    }

    private async Task RunImportPathAsync(string path)
    {
        SetImportBusy(true, $"İçe aktarılıyor: {Path.GetFileName(path)}…");
        var imported = await Task.Run(() => _session.ImportSymbolsFromPath(path));
        if (imported.IsFailure && imported.Error!.Code is "dxf.lisans_yok" or "qet.lisans_yok")
        {
            var license = await new TextPromptDialog("Sembol lisansı / kullanım koşulu", "Kullanıcı sembolü")
                .ShowDialog<string?>(this);
            if (!string.IsNullOrWhiteSpace(license))
            {
                imported = await Task.Run(() => _session.ImportSymbolsFromPath(path, license.Trim()));
            }
        }

        ApplyImportResult(imported);
    }

    private async Task ImportDiscoveredAsync()
    {
        SetImportBusy(true, "Bulunan yerel sembol kütüphaneleri yükleniyor…");
        var imported = await Task.Run(() => _session.ImportDiscoveredSymbolLibraries());
        ApplyImportResult(imported);
    }

    private void ApplyImportResult(Ake.Core.Result<Ake.Library.SymbolCollectionImportResult> imported)
    {
        SetImportBusy(false);
        if (imported.IsFailure)
        {
            ShowImportStatus(imported.Error!.Message, isError: true);
            return;
        }

        var (added, updated, skipped) = imported.Value;
        Changed = true;
        ShowImportStatus(
            $"Tamamlandı: {added} yeni, {updated} güncellenen sembol ({skipped} atlandı). Toplam: {_session.LibrarySymbolCount} sembol.",
            isError: false);
        Reload();
    }

    private void SetImportBusy(bool busy, string? status = null)
    {
        var enabled = !busy && _session.LibraryPath is not null;
        _importFileButton.IsEnabled = enabled;
        _importFolderButton.IsEnabled = enabled;
        _importDiscoveredButton.IsEnabled = enabled;
        _newCategoryButton.IsEnabled = enabled;
        if (status is not null)
        {
            ShowImportStatus(status, isError: false);
        }
    }

    private void ShowImportStatus(string text, bool isError)
    {
        _importStatus.Foreground = new SolidColorBrush(isError ? Color.FromRgb(0xC0, 0x39, 0x2B) : Color.FromRgb(0x2E, 0x6F, 0x40));
        _importStatus.Text = text;
        _importStatus.IsVisible = true;
    }

    private static IBrush BrushResource(string key, Color fallback) =>
        Application.Current?.TryFindResource(key, out var found) == true && found is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    internal sealed class CategoryNode(
        string name,
        string categoryPath,
        IReadOnlyList<SymbolRow> rows,
        IReadOnlyList<CategoryNode> children,
        bool expanded = false)
    {
        public string Name { get; } = name;
        public string CategoryPath { get; } = categoryPath;
        public IReadOnlyList<SymbolRow> Rows { get; } = rows;
        public IReadOnlyList<CategoryNode> Children { get; } = children;
        public bool Expanded { get; set; } = expanded;
        public string Label => $"{Name} ({Rows.Count})";
    }
}
