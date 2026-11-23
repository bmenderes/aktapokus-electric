using System.Globalization;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>Antet / sayfa şablonu penceresinin döndürdüğü kayıt seçimi.</summary>
public sealed record TitleBlockEditorResult(SheetTemplate Template, bool ApplyToActivePage, bool ApplyToAllPages);

/// <summary>
/// Antet ve sayfa şablonu oluşturma / düzenleme penceresi:
/// sol tarafta gerçek logo ve proje bilgileriyle canlı antet önizlemesi, sağ tarafta kağıt/antet ölçüleri,
/// hızlı firma logosu seçimi ve hücre bazlı antet düzenleyicisi sunar.
/// </summary>
public sealed class TitleBlockEditorWindow : Window
{
    private static readonly (string Label, string Placeholder)[] Placeholders =
    [
        ("Proje adı — {project.name}", "{project.name}"),
        ("Açıklama — {project.description}", "{project.description}"),
        ("Müşteri — {project.customer}", "{project.customer}"),
        ("Sipariş / iş no — {project.commission}", "{project.commission}"),
        ("Çizim no — {project.drawingNo}", "{project.drawingNo}"),
        ("Firma adı — {project.company}", "{project.company}"),
        ("Firma adresi — {project.companyAddress}", "{project.companyAddress}"),
        ("Çizen — {project.drawnBy}", "{project.drawnBy}"),
        ("Proje tarihi — {project.date}", "{project.date}"),
        ("Revizyon — {project.revision}", "{project.revision}"),
        ("Firma logosu — {project.companyLogo}", "{project.companyLogo}"),
        ("Sayfa numarası — {page.number}", "{page.number}"),
        ("Sayfa başlığı — {page.title}", "{page.title}"),
        ("Toplam sayfa — {page.count}", "{page.count}"),
        ("Sonraki sayfa — {page.next}", "{page.next}"),
        ("Fonksiyon (=) — {page.function}", "{page.function}"),
        ("Yer (+) — {page.location}", "{page.location}"),
        ("Bugünün tarihi — {today}", "{today}"),
    ];

    private static readonly (string Label, double W, double H, int Cols, int Rows)[] PaperSizes =
    [
        ("A4 yatay (297 × 210)", 297, 210, 8, 6),
        ("A4 dikey (210 × 297)", 210, 297, 6, 8),
        ("A3 yatay (420 × 297)", 420, 297, 10, 6),
        ("A3 dikey (297 × 420)", 297, 420, 8, 10),
        ("A2 yatay (594 × 420)", 594, 420, 14, 8),
    ];

    private readonly EditorSession _session;
    private readonly int _activePageIndex;
    private SheetTemplateId _templateId;
    private SheetTemplate _originalTemplate;
    private readonly List<TitleBlockCell> _cells = [];
    private readonly List<SheetTemplate> _sourceChoices = [];
    private bool _updatingUi;
    private bool _zoomTitleBlock = true;

    private readonly SceneView _preview = new() { MinWidth = 420, MinHeight = 400 };
    private readonly ComboBox _sourceCombo = new() { Width = 250 };
    private readonly ComboBox _paperCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _nameBox = new();
    private readonly TextBox _widthBox = new();
    private readonly TextBox _heightBox = new();
    private readonly TextBox _marginBox = new();
    private readonly TextBox _bandBox = new();
    private readonly TextBox _titleHeightBox = new();
    private readonly TextBox _columnsBox = new();
    private readonly TextBox _rowsBox = new();
    private readonly TextBox _fontBox = new();

    private readonly TextBlock _logoStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private readonly ListBox _cellList = new() { Height = 135 };
    private readonly TextBox _cellXBox = new();
    private readonly TextBox _cellYBox = new();
    private readonly TextBox _cellWBox = new();
    private readonly TextBox _cellHBox = new();
    private readonly TextBox _cellLabelBox = new();
    private readonly TextBox _cellContentBox = new();
    private readonly ComboBox _placeholderCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _cellFontSizeBox = new();
    private readonly ComboBox _cellAlignCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = new[] { "Sol", "Orta", "Sağ" } };
    private readonly CheckBox _cellBorderCheck = new() { Content = "Çerçeve çizgisi göster" };
    private readonly TextBox _cellImageBox = new() { PlaceholderText = "logo.png veya {project.companyLogo}" };

    private readonly TextBlock _validationText = new()
    {
        Foreground = new SolidColorBrush(Color.Parse("#c0392b")),
        TextWrapping = TextWrapping.Wrap,
        FontSize = 12,
    };
    private readonly CheckBox _asNewCopyCheck = new() { Content = "Yeni kopya olarak ekle (mevcut şablonu ezme)" };
    private readonly CheckBox _applyActiveCheck = new() { Content = "Etkin sayfaya uygula", IsChecked = true };
    private readonly CheckBox _applyAllCheck = new() { Content = "Tüm sayfalara uygula", IsChecked = true };
    private readonly Button _saveButton = new() { Content = "Projeye kaydet", Classes = { "accent" }, IsDefault = true, MinWidth = 130 };

    public TitleBlockEditorWindow(EditorSession session, int activePageIndex)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _activePageIndex = activePageIndex;
        _originalTemplate = session.ActiveTemplateFor(activePageIndex);
        _preview.ImageBaseDirectory = session.ProjectDirectory;

        Title = "Antet ve Sayfa Şablonu Oluştur / Düzenle";
        Width = 1140;
        Height = 760;
        MinWidth = 940;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Brush.Parse("#F2EDE1");

        PopulateSourceTemplates();
        _paperCombo.ItemsSource = PaperSizes.Select(p => p.Label).ToList();
        _placeholderCombo.ItemsSource = new[] { "Yer tutucu ekle…" }.Concat(Placeholders.Select(p => p.Label)).ToList();
        _placeholderCombo.SelectedIndex = 0;

        LoadTemplate(_originalTemplate);
        WireEvents();

        var root = new DockPanel { Margin = new Thickness(14), LastChildFill = true };
        var topBar = BuildTopBar();
        var bottomBar = BuildBottomBar();
        var mainSplit = BuildMainSplit();

        DockPanel.SetDock(topBar, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(bottomBar, Avalonia.Controls.Dock.Bottom);
        root.Children.AddRange([topBar, bottomBar, mainSplit]);
        Content = root;

        Opened += (_, _) => RefreshPreview(focusTitleBlock: true);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private Control BuildTopBar()
    {
        var newBlankBtn = new Button { Content = "Yeni boş antet" };
        newBlankBtn.Click += (_, _) => CreateBlankTemplate();

        var openFileBtn = new Button { Content = "Dosyadan aç… (.sablon.json)" };
        openFileBtn.Click += async (_, _) => await OpenFromFileAsync();

        var saveFileBtn = new Button { Content = "Dosyaya kaydet… (.sablon.json)" };
        saveFileBtn.Click += async (_, _) => await SaveToFileAsync();

        return new Border
        {
            Background = Brush.Parse("#FBF8F1"),
            BorderBrush = Brush.Parse("#D6CDB8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Başlangıç şablonu:", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold },
                    _sourceCombo,
                    newBlankBtn,
                    openFileBtn,
                    saveFileBtn,
                },
            },
        };
    }

    private Control BuildMainSplit()
    {
        var zoomBlockBtn = new Button { Content = "Antete yakınlaştır" };
        zoomBlockBtn.Click += (_, _) =>
        {
            _zoomTitleBlock = true;
            RefreshPreview(focusTitleBlock: true);
        };

        var fitPageBtn = new Button { Content = "Tam sayfayı göster" };
        fitPageBtn.Click += (_, _) =>
        {
            _zoomTitleBlock = false;
            _preview.FitToView();
        };

        var darkCheck = new CheckBox { Content = "Koyu zemin", VerticalAlignment = VerticalAlignment.Center };
        darkCheck.IsCheckedChanged += (_, _) => _preview.DarkCanvas = darkCheck.IsChecked == true;

        var previewTools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
            Children =
            {
                zoomBlockBtn,
                fitPageBtn,
                darkCheck,
                new TextBlock { Text = "İpucu: Önizlemede bir hücreye tıklayarak seçebilirsiniz.", VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.7 },
            },
        };

        var previewPane = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(previewTools, Avalonia.Controls.Dock.Top);
        previewPane.Children.AddRange([previewTools, new Border
        {
            BorderBrush = Brush.Parse("#D6CDB8"),
            BorderThickness = new Thickness(1),
            Child = _preview,
        }]);

        var formStack = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(4, 0, 8, 8),
            Children =
            {
                BuildPageSettingsSection(),
                BuildQuickLogoSection(),
                BuildCellsSection(),
            },
        };

        var rightScroll = new ScrollViewer
        {
            Content = formStack,
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,12,460") };
        Grid.SetColumn(previewPane, 0);
        Grid.SetColumn(rightScroll, 2);
        grid.Children.AddRange([previewPane, rightScroll]);
        return grid;
    }

    private Control BuildPageSettingsSection()
    {
        var dimsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowSpacing = 6,
        };

        AddLabeledRow(dimsGrid, 0, 0, "Genişlik (mm)", _widthBox);
        AddLabeledRow(dimsGrid, 0, 2, "Yükseklik (mm)", _heightBox);
        AddLabeledRow(dimsGrid, 1, 0, "Kenar boşluğu", _marginBox);
        AddLabeledRow(dimsGrid, 1, 2, "Izgara bandı", _bandBox);
        AddLabeledRow(dimsGrid, 2, 0, "Antet yüksekliği", _titleHeightBox);
        AddLabeledRow(dimsGrid, 2, 2, "Yazı tipi", _fontBox);
        AddLabeledRow(dimsGrid, 3, 0, "Kolon sayısı", _columnsBox);
        AddLabeledRow(dimsGrid, 3, 2, "Satır (1-26)", _rowsBox);

        return Card("1. Sayfa ve antet ölçüleri", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                Labeled("Şablon adı", _nameBox),
                Labeled("Hazır kâğıt boyutu (opsiyonel)", _paperCombo),
                dimsGrid,
            },
        });
    }

    private Control BuildQuickLogoSection()
    {
        var pickLogoBtn = new Button { Content = "Logo dosyası seç…", Classes = { "accent" } };
        pickLogoBtn.Click += async (_, _) => await PickLogoAsync(targetCellOnly: false);

        var projectLogoBtn = new Button { Content = "Proje logosunu bağla ({project.companyLogo})" };
        projectLogoBtn.Click += (_, _) => SetQuickLogo("{project.companyLogo}");

        var clearLogoBtn = new Button { Content = "Logoyu kaldır" };
        clearLogoBtn.Click += (_, _) => ClearQuickLogo();

        return Card("2. Firma logosu", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _logoStatus,
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        WithMargin(pickLogoBtn),
                        WithMargin(projectLogoBtn),
                        WithMargin(clearLogoBtn),
                    },
                },
            },
        });
    }

    private Control BuildCellsSection()
    {
        var addTextBtn = new Button { Content = "+ Metin hücresi" };
        addTextBtn.Click += (_, _) => AddNewCell(isLogo: false);

        var addLogoBtn = new Button { Content = "+ Logo hücresi" };
        addLogoBtn.Click += (_, _) => AddNewCell(isLogo: true);

        var removeBtn = new Button { Content = "Hücreyi sil" };
        removeBtn.Click += (_, _) => RemoveSelectedCell();

        var pickCellImageBtn = new Button { Content = "Seç…" };
        pickCellImageBtn.Click += async (_, _) => await PickLogoAsync(targetCellOnly: true);

        var clearCellImageBtn = new Button { Content = "×", Width = 32 };
        clearCellImageBtn.Click += (_, _) => _cellImageBox.Text = string.Empty;

        var imgRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(_cellImageBox, 0);
        Grid.SetColumn(pickCellImageBtn, 1);
        Grid.SetColumn(clearCellImageBtn, 2);
        imgRow.Children.AddRange([_cellImageBox, pickCellImageBtn, clearCellImageBtn]);

        var geomGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,6,*,6,*,6,*"),
            RowSpacing = 4,
        };
        AddLabeledRow(geomGrid, 0, 0, "X (mm)", _cellXBox);
        AddLabeledRow(geomGrid, 0, 2, "Y (mm)", _cellYBox);
        AddLabeledRow(geomGrid, 0, 4, "Genişlik", _cellWBox);
        AddLabeledRow(geomGrid, 0, 6, "Yükseklik", _cellHBox);

        var styleGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,8,*"),
            RowSpacing = 4,
        };
        AddLabeledRow(styleGrid, 0, 0, "Yazı boyutu (mm)", _cellFontSizeBox);
        AddLabeledRow(styleGrid, 0, 2, "Hizalama", _cellAlignCombo);

        return Card("3. Antet hücreleri", new StackPanel
        {
            Spacing = 8,
            Children =
            {
                _cellList,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 6,
                    Children = { addTextBtn, addLogoBtn, removeBtn },
                },
                geomGrid,
                Labeled("Üst etiket (ör. Proje:, Çizen:)", _cellLabelBox),
                Labeled("İçerik / Yer tutucu", _cellContentBox),
                _placeholderCombo,
                styleGrid,
                _cellBorderCheck,
                Labeled("Görsel / logo yolu", imgRow),
            },
        });
    }

    private Control BuildBottomBar()
    {
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 90 };
        cancel.Click += (_, _) => Close(null);

        _saveButton.Click += (_, _) => Submit();

        return new Border
        {
            Padding = new Thickness(0, 10, 0, 0),
            Child = new DockPanel
            {
                LastChildFill = false,
                Children =
                {
                    SetDock(new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            _validationText,
                            new StackPanel
                            {
                                Orientation = Orientation.Horizontal,
                                Spacing = 14,
                                Children = { _asNewCopyCheck, _applyActiveCheck, _applyAllCheck },
                            },
                        },
                    }, Avalonia.Controls.Dock.Left),
                    SetDock(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Spacing = 8,
                        VerticalAlignment = VerticalAlignment.Bottom,
                        Children = { cancel, _saveButton },
                    }, Avalonia.Controls.Dock.Right),
                },
            },
        };
    }

    private void PopulateSourceTemplates()
    {
        _sourceChoices.Clear();
        var items = new List<string>();
        foreach (var t in _session.ProjectTemplates)
        {
            _sourceChoices.Add(t);
            items.Add($"Projede: {t.Name}");
        }

        foreach (var std in StandardSheetTemplates.All)
        {
            if (_sourceChoices.All(x => x.Id != std.Id))
            {
                _sourceChoices.Add(std);
                items.Add($"Hazır: {std.Name}");
            }
        }

        _sourceCombo.ItemsSource = items;
        var currentIndex = _sourceChoices.FindIndex(t => t.Id == _originalTemplate.Id);
        if (currentIndex >= 0)
        {
            _sourceCombo.SelectedIndex = currentIndex;
        }
    }

    private void LoadTemplate(SheetTemplate template)
    {
        _updatingUi = true;
        try
        {
            _templateId = template.Id;
            _nameBox.Text = template.Name;
            _widthBox.Text = Num(template.WidthMm);
            _heightBox.Text = Num(template.HeightMm);
            _marginBox.Text = Num(template.MarginMm);
            _bandBox.Text = Num(template.GridBandMm);
            _titleHeightBox.Text = Num(template.TitleBlockHeightMm);
            _columnsBox.Text = template.Columns.ToString(CultureInfo.InvariantCulture);
            _rowsBox.Text = template.Rows.ToString(CultureInfo.InvariantCulture);
            _fontBox.Text = template.FontFamily;

            _cells.Clear();
            _cells.AddRange(template.Cells);
            RefreshCellList(0);
            LoadSelectedCellToFields();
            UpdateLogoStatus();
        }
        finally
        {
            _updatingUi = false;
        }

        RefreshPreview(focusTitleBlock: _zoomTitleBlock);
    }

    private void WireEvents()
    {
        _sourceCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || _sourceCombo.SelectedIndex < 0 || _sourceCombo.SelectedIndex >= _sourceChoices.Count)
            {
                return;
            }

            LoadTemplate(_sourceChoices[_sourceCombo.SelectedIndex]);
        };

        _paperCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || _paperCombo.SelectedIndex < 0 || _paperCombo.SelectedIndex >= PaperSizes.Length)
            {
                return;
            }

            var (_, w, h, cols, rows) = PaperSizes[_paperCombo.SelectedIndex];
            _updatingUi = true;
            _widthBox.Text = Num(w);
            _heightBox.Text = Num(h);
            _columnsBox.Text = cols.ToString(CultureInfo.InvariantCulture);
            _rowsBox.Text = rows.ToString(CultureInfo.InvariantCulture);
            _updatingUi = false;
            RefreshPreview(focusTitleBlock: _zoomTitleBlock);
        };

        foreach (var box in new[] { _nameBox, _widthBox, _heightBox, _marginBox, _bandBox, _titleHeightBox, _columnsBox, _rowsBox, _fontBox })
        {
            box.TextChanged += (_, _) =>
            {
                if (!_updatingUi)
                {
                    RefreshPreview(focusTitleBlock: false);
                }
            };
        }

        _cellList.SelectionChanged += (_, _) =>
        {
            if (!_updatingUi)
            {
                LoadSelectedCellToFields();
                HighlightSelectedCell();
            }
        };

        foreach (var box in new[] { _cellXBox, _cellYBox, _cellWBox, _cellHBox, _cellLabelBox, _cellContentBox, _cellFontSizeBox, _cellImageBox })
        {
            box.TextChanged += (_, _) =>
            {
                if (!_updatingUi)
                {
                    CommitSelectedCellFromFields();
                }
            };
        }

        _cellAlignCombo.SelectionChanged += (_, _) =>
        {
            if (!_updatingUi)
            {
                CommitSelectedCellFromFields();
            }
        };

        _cellBorderCheck.IsCheckedChanged += (_, _) =>
        {
            if (!_updatingUi)
            {
                CommitSelectedCellFromFields();
            }
        };

        _placeholderCombo.SelectionChanged += (_, _) =>
        {
            if (_updatingUi || _placeholderCombo.SelectedIndex <= 0)
            {
                return;
            }

            var token = Placeholders[_placeholderCombo.SelectedIndex - 1].Placeholder;
            if (token == "{project.companyLogo}")
            {
                _cellImageBox.Text = token;
            }
            else
            {
                _cellContentBox.Text = string.IsNullOrEmpty(_cellContentBox.Text)
                    ? token
                    : _cellContentBox.Text + token;
            }

            _placeholderCombo.SelectedIndex = 0;
        };

        _preview.ContentPressed = (x, y) =>
        {
            var built = BuildTemplate();
            var block = SheetGeometry.For(built).TitleBlock;
            var rx = x - block.X;
            var ry = y - block.Y;
            for (var i = 0; i < _cells.Count; i++)
            {
                var c = _cells[i];
                if (rx >= c.XMm && rx <= c.XMm + c.WidthMm && ry >= c.YMm && ry <= c.YMm + c.HeightMm)
                {
                    _cellList.SelectedIndex = i;
                    return true;
                }
            }

            return false;
        };
    }

    private void CreateBlankTemplate()
    {
        var width = ParsePositive(_widthBox.Text, 420);
        var height = ParsePositive(_heightBox.Text, 297);
        var margin = Math.Max(0, ParseDouble(_marginBox.Text, 10));
        var band = Math.Max(0, ParseDouble(_bandBox.Text, 5));
        var tbHeight = Math.Max(10, ParsePositive(_titleHeightBox.Text, 20));
        var cols = Math.Max(1, ParseInt(_columnsBox.Text, 10));
        var rows = Math.Clamp(ParseInt(_rowsBox.Text, 6), 1, 26);
        var inner = Math.Max(60, width - (2 * margin));
        var logoW = Math.Round(Math.Min(65, inner * 0.18), 2);
        var titleW = Math.Round((inner - logoW) * 0.55, 2);
        var infoW = Math.Round(inner - logoW - titleW, 2);

        var blank = new SheetTemplate(
            SheetTemplateId.New(),
            "Özel antet",
            width,
            height,
            margin,
            cols,
            rows,
            band,
            tbHeight,
            string.IsNullOrWhiteSpace(_fontBox.Text) ? "Arial" : _fontBox.Text.Trim(),
            [
                new TitleBlockCell(0, 0, logoW, tbHeight, ImageRef: "{project.companyLogo}"),
                new TitleBlockCell(logoW, 0, titleW, tbHeight, "Başlık:", "{page.title}", 3.5),
                new TitleBlockCell(logoW + titleW, 0, infoW, tbHeight / 2, "Proje:", "{project.name}"),
                new TitleBlockCell(logoW + titleW, tbHeight / 2, infoW, tbHeight / 2, "Sayfa:", "{page.number} / {page.count}", 2.5, TextAlign.Right),
            ]);

        _asNewCopyCheck.IsChecked = false;
        LoadTemplate(blank);
    }

    private void RefreshCellList(int selectIndex)
    {
        _updatingUi = true;
        try
        {
            _cellList.ItemsSource = _cells.Select((c, i) => CellDisplay(i, c)).ToList();
            if (_cells.Count == 0)
            {
                _cellList.SelectedIndex = -1;
            }
            else
            {
                _cellList.SelectedIndex = Math.Clamp(selectIndex, 0, _cells.Count - 1);
            }
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private static string CellDisplay(int index, TitleBlockCell cell)
    {
        var desc = cell.ImageRef is { Length: > 0 }
            ? $"Logo: {(Path.GetFileName(cell.ImageRef) is { Length: > 0 } f ? f : cell.ImageRef)}"
            : $"{cell.Label} {cell.Content}".Trim();
        if (string.IsNullOrWhiteSpace(desc))
        {
            desc = "(Boş hücre)";
        }

        return $"#{index + 1} [{cell.XMm:0.#}, {cell.YMm:0.#} · {cell.WidthMm:0.#}×{cell.HeightMm:0.#}]  {desc}";
    }

    private void LoadSelectedCellToFields()
    {
        _updatingUi = true;
        try
        {
            var idx = _cellList.SelectedIndex;
            if (idx < 0 || idx >= _cells.Count)
            {
                _cellXBox.Text = _cellYBox.Text = _cellWBox.Text = _cellHBox.Text = string.Empty;
                _cellLabelBox.Text = _cellContentBox.Text = _cellFontSizeBox.Text = _cellImageBox.Text = string.Empty;
                _cellAlignCombo.SelectedIndex = 0;
                _cellBorderCheck.IsChecked = true;
                return;
            }

            var cell = _cells[idx];
            _cellXBox.Text = Num(cell.XMm);
            _cellYBox.Text = Num(cell.YMm);
            _cellWBox.Text = Num(cell.WidthMm);
            _cellHBox.Text = Num(cell.HeightMm);
            _cellLabelBox.Text = cell.Label ?? string.Empty;
            _cellContentBox.Text = cell.Content ?? string.Empty;
            _cellFontSizeBox.Text = Num(cell.FontSizeMm);
            _cellAlignCombo.SelectedIndex = (int)cell.Align;
            _cellBorderCheck.IsChecked = cell.Border;
            _cellImageBox.Text = cell.ImageRef ?? string.Empty;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void CommitSelectedCellFromFields()
    {
        var idx = _cellList.SelectedIndex;
        if (idx < 0 || idx >= _cells.Count)
        {
            return;
        }

        var existing = _cells[idx];
        var updated = new TitleBlockCell(
            Math.Max(0, ParseDouble(_cellXBox.Text, existing.XMm)),
            Math.Max(0, ParseDouble(_cellYBox.Text, existing.YMm)),
            Math.Max(1, ParsePositive(_cellWBox.Text, existing.WidthMm)),
            Math.Max(1, ParsePositive(_cellHBox.Text, existing.HeightMm)),
            Clean(_cellLabelBox.Text),
            Clean(_cellContentBox.Text),
            Math.Max(0.8, ParsePositive(_cellFontSizeBox.Text, existing.FontSizeMm)),
            (TextAlign)Math.Clamp(_cellAlignCombo.SelectedIndex, 0, 2),
            _cellBorderCheck.IsChecked == true,
            Clean(_cellImageBox.Text));

        _cells[idx] = updated;
        RefreshCellList(idx);
        UpdateLogoStatus();
        RefreshPreview(focusTitleBlock: false);
    }

    private void AddNewCell(bool isLogo)
    {
        var template = BuildTemplate();
        var innerW = Math.Max(20, template.WidthMm - (2 * template.MarginMm));
        var h = Math.Min(10, template.TitleBlockHeightMm);
        var w = Math.Min(isLogo ? 60 : 50, innerW);
        var newCell = isLogo
            ? new TitleBlockCell(0, 0, w, template.TitleBlockHeightMm, ImageRef: "{project.companyLogo}")
            : new TitleBlockCell(Math.Max(0, innerW - w), 0, w, h, "Alan:", "{project.name}", 2.5);
        _cells.Add(newCell);
        RefreshCellList(_cells.Count - 1);
        LoadSelectedCellToFields();
        UpdateLogoStatus();
        RefreshPreview(focusTitleBlock: false);
    }

    private void RemoveSelectedCell()
    {
        var idx = _cellList.SelectedIndex;
        if (idx < 0 || idx >= _cells.Count)
        {
            return;
        }

        _cells.RemoveAt(idx);
        RefreshCellList(Math.Min(idx, _cells.Count - 1));
        LoadSelectedCellToFields();
        UpdateLogoStatus();
        RefreshPreview(focusTitleBlock: false);
    }

    private async Task PickLogoAsync(bool targetCellOnly)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Antet firma logosu seç",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Resim dosyaları") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0 || StorageProviderExtensions.TryGetLocalPath(files[0]) is not { Length: > 0 } localPath)
        {
            return;
        }

        var storedPath = MakeRelativeIfInsideProject(localPath);
        if (targetCellOnly && _cellList.SelectedIndex >= 0 && _cellList.SelectedIndex < _cells.Count)
        {
            _cellImageBox.Text = storedPath;
        }
        else
        {
            SetQuickLogo(storedPath);
        }
    }

    private string MakeRelativeIfInsideProject(string localPath)
    {
        if (_session.ProjectDirectory is not { Length: > 0 } projDir)
        {
            return localPath;
        }

        var fullProj = Path.GetFullPath(projDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullFile = Path.GetFullPath(localPath);
        return fullFile.StartsWith(fullProj, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(projDir, fullFile).Replace('\\', '/')
            : fullFile;
    }

    private void SetQuickLogo(string path)
    {
        var logoIdx = _cells.FindIndex(c => c.ImageRef is { Length: > 0 });
        if (logoIdx < 0)
        {
            var template = BuildTemplate();
            var innerW = Math.Max(40, template.WidthMm - (2 * template.MarginMm));
            var logoCell = new TitleBlockCell(0, 0, Math.Round(Math.Min(70, innerW * 0.2), 2), template.TitleBlockHeightMm, ImageRef: path);
            _cells.Insert(0, logoCell);
            logoIdx = 0;
        }
        else
        {
            _cells[logoIdx] = _cells[logoIdx] with { ImageRef = path };
        }

        RefreshCellList(logoIdx);
        LoadSelectedCellToFields();
        UpdateLogoStatus();
        RefreshPreview(focusTitleBlock: false);
    }

    private void ClearQuickLogo()
    {
        var logoIdx = _cells.FindIndex(c => c.ImageRef is { Length: > 0 });
        if (logoIdx < 0)
        {
            return;
        }

        var cell = _cells[logoIdx];
        if (string.IsNullOrEmpty(cell.Label) && string.IsNullOrEmpty(cell.Content) && _cells.Count > 1)
        {
            _cells.RemoveAt(logoIdx);
        }
        else
        {
            _cells[logoIdx] = cell with { ImageRef = null };
        }

        RefreshCellList(Math.Min(logoIdx, _cells.Count - 1));
        LoadSelectedCellToFields();
        UpdateLogoStatus();
        RefreshPreview(focusTitleBlock: false);
    }

    private void UpdateLogoStatus()
    {
        var logoCell = _cells.FirstOrDefault(c => c.ImageRef is { Length: > 0 });
        _logoStatus.Text = logoCell is not null
            ? $"Etkin logo referansı: {logoCell.ImageRef}"
            : "Antette henüz logo hücresi yok; 'Logo dosyası seç…' ile ekleyebilirsiniz.";
    }

    private SheetTemplate BuildTemplate()
    {
        var id = _asNewCopyCheck.IsChecked == true ? SheetTemplateId.New() : _templateId;
        return new SheetTemplate(
            id,
            string.IsNullOrWhiteSpace(_nameBox.Text) ? "Özel antet" : _nameBox.Text.Trim(),
            ParsePositive(_widthBox.Text, 420),
            ParsePositive(_heightBox.Text, 297),
            Math.Max(0, ParseDouble(_marginBox.Text, 10)),
            Math.Max(1, ParseInt(_columnsBox.Text, 10)),
            Math.Max(1, ParseInt(_rowsBox.Text, 6)),
            Math.Max(0, ParseDouble(_bandBox.Text, 5)),
            Math.Max(0, ParseDouble(_titleHeightBox.Text, 20)),
            string.IsNullOrWhiteSpace(_fontBox.Text) ? "Arial" : _fontBox.Text.Trim(),
            [.. _cells]);
    }

    private void RefreshPreview(bool focusTitleBlock)
    {
        var candidate = BuildTemplate();
        var error = candidate.Check();
        if (error is not null)
        {
            _validationText.Text = error.Message;
            _saveButton.IsEnabled = false;
            return;
        }

        _validationText.Text = string.Empty;
        _saveButton.IsEnabled = true;

        var preview = _session.PreviewTemplate(candidate, _activePageIndex, DateOnly.FromDateTime(DateTime.Now));
        if (preview.IsSuccess)
        {
            _preview.UpdateScene(preview.Value);
            HighlightSelectedCell();
            if (focusTitleBlock)
            {
                var tb = SheetGeometry.For(candidate).TitleBlock;
                _preview.FocusBox(tb);
            }
        }
    }

    private void HighlightSelectedCell()
    {
        var idx = _cellList.SelectedIndex;
        if (idx < 0 || idx >= _cells.Count)
        {
            _preview.Highlights = [];
            return;
        }

        var tb = SheetGeometry.For(BuildTemplate()).TitleBlock;
        var c = _cells[idx];
        _preview.Highlights = [new Box(tb.X + c.XMm, tb.Y + c.YMm, c.WidthMm, c.HeightMm)];
    }

    private async Task OpenFromFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Antet / sayfa şablonu aç",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Sayfa şablonu") { Patterns = ["*.sablon.json", "*.json"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0 || StorageProviderExtensions.TryGetLocalPath(files[0]) is not { Length: > 0 } path)
        {
            return;
        }

        var loaded = SheetTemplateJson.Load(path);
        if (loaded.IsSuccess)
        {
            LoadTemplate(loaded.Value);
        }
        else
        {
            _validationText.Text = loaded.Error!.Message;
        }
    }

    private async Task SaveToFileAsync()
    {
        var template = BuildTemplate();
        if (template.Check() is { } error)
        {
            _validationText.Text = error.Message;
            return;
        }

        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
        {
            return;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Antet / sayfa şablonunu dosyaya kaydet",
            SuggestedFileName = $"{template.Name}.sablon.json",
            DefaultExtension = "json",
            FileTypeChoices = [new FilePickerFileType("Sayfa şablonu") { Patterns = ["*.sablon.json", "*.json"] }],
        });

        if (file is null || StorageProviderExtensions.TryGetLocalPath(file) is not { Length: > 0 } path)
        {
            return;
        }

        var saved = SheetTemplateJson.Save(template, path);
        if (saved.IsFailure)
        {
            _validationText.Text = saved.Error!.Message;
        }
    }

    private void Submit()
    {
        var template = BuildTemplate();
        if (template.Check() is { } error)
        {
            _validationText.Text = error.Message;
            return;
        }

        Close(new TitleBlockEditorResult(
            template,
            _applyActiveCheck.IsChecked == true,
            _applyAllCheck.IsChecked == true));
    }

    private static Control Card(string heading, Control body) =>
        new Border
        {
            Background = Brush.Parse("#FBF8F1"),
            BorderBrush = Brush.Parse("#D6CDB8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#2B2619") },
                    body,
                },
            },
        };

    private static Control Labeled(string label, Control field) =>
        new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new TextBlock { Text = label, FontSize = 12, Opacity = 0.78 },
                field,
            },
        };

    private static void AddLabeledRow(Grid grid, int row, int col, string label, Control field)
    {
        while (grid.RowDefinitions.Count <= row)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        var wrapped = Labeled(label, field);
        Grid.SetRow(wrapped, row);
        Grid.SetColumn(wrapped, col);
        grid.Children.Add(wrapped);
    }

    private static Control WithMargin(Control c)
    {
        c.Margin = new Thickness(0, 0, 6, 6);
        return c;
    }

    private static T SetDock<T>(T control, Avalonia.Controls.Dock dock) where T : Control
    {
        DockPanel.SetDock(control, dock);
        return control;
    }

    private static double ParsePositive(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) && val > 0 ? val : fallback;

    private static double ParseDouble(string? text, double fallback) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val) && double.IsFinite(val) ? val : fallback;

    private static int ParseInt(string? text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var val) ? val : fallback;

    private static string Num(double value) => Math.Round(value, 2).ToString(CultureInfo.InvariantCulture);

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
