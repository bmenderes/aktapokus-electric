using System.Globalization;
using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// PLC adresi ↔ tel numarası iki yönlü çapraz referans tablosu ve kanal düzenleyicisi (Faz 6c-4, ADR-0056):
/// PLC adresine veya tel numarasına göre sıralar, kanal bilgilerini düzenler ve CSV/noktalı virgüllü PLC adres listesi içe aktarır.
/// </summary>
public sealed class PlcIoWindow : Window
{
    private sealed record FunctionChoice(FunctionId? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly EditorSession _session;
    private readonly ComboBox _sortBox = new()
    {
        ItemsSource = new[] { UiStrings.T("PLC adresine göre sırala"), UiStrings.T("Tel numarasına göre sırala") },
        SelectedIndex = 0,
        Width = 230,
    };

    private readonly TextBox _searchBox = new() { PlaceholderText = UiStrings.T("Adres, sembolik ad, tel no, klemens veya saha ucu ara…") };
    private readonly ListBox _list = new() { Height = 240 };

    private readonly TextBlock _selectedSummary = new() { FontWeight = FontWeight.SemiBold, FontSize = 12 };
    private readonly TextBox _address = new() { PlaceholderText = "örn. %I0.0" };
    private readonly TextBox _symbol = new() { PlaceholderText = "örn. bStart" };
    private readonly TextBox _description = new() { PlaceholderText = UiStrings.T("Kanal açıklaması") };
    private readonly ComboBox _functionBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _applyButton = new() { Content = UiStrings.T("Kanalı güncelle"), MinWidth = 132, HorizontalContentAlignment = HorizontalAlignment.Center };

    private readonly Border _csvPanel;
    private readonly TextBox _csvInput = new()
    {
        AcceptsReturn = true,
        Height = 90,
        PlaceholderText = UiStrings.T("Örn: %I0.0, bAcilStop, Acil stop kontağı\nveya: DI0; %I0.0; bAcilStop; Acil stop kontağı"),
    };

    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    public bool Changed { get; private set; }

    public PlcIoWindow(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var project = session.Project ?? throw new ArgumentException("Proje açık değil.", nameof(session));

        Title = $"{UiStrings.T("PLC Adresi ↔ Tel Çapraz Referans Tablosu")} — {project.Name}";
        Width = 1040;
        Height = 620;
        MinWidth = 900;
        MinHeight = 520;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        var addPlcButton = new Button { Content = UiStrings.T("Yeni PLC ekle (8 DI / 8 DO)"), MinWidth = 178, HorizontalContentAlignment = HorizontalAlignment.Center };
        var toggleCsvButton = new Button { Content = UiStrings.T("CSV adres yapıştır…"), MinWidth = 148, HorizontalContentAlignment = HorizontalAlignment.Center };
        var fileCsvButton = new Button { Content = UiStrings.T("CSV dosyasından al…"), MinWidth = 152, HorizontalContentAlignment = HorizontalAlignment.Center };

        _csvPanel = BuildCsvImportPanel();
        _csvPanel.IsVisible = false;
        addPlcButton.Click += (_, _) =>
        {
            var added = _session.CreateQuickPlcController();
            if (added.IsFailure)
            {
                SetStatus(added.Error!.Message, error: true);
                return;
            }

            Changed = true;
            if (_session.Project is { } updatedProject)
            {
                PopulateFunctions(updatedProject);
            }

            RefreshRows();
            SetStatus(UiStrings.T("Örnek PLC denetleyicisi eklendi (8 DI / 8 DO). Adresleri düzenleyip saha cihazına bağlayabilirsiniz."), error: false);
        };
        toggleCsvButton.Click += (_, _) => _csvPanel.IsVisible = !_csvPanel.IsVisible;
        fileCsvButton.Click += (_, _) => _ = ImportCsvFileAsync();

        var topBar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,8,*,8,Auto,8,Auto,8,Auto"), Margin = new Thickness(0, 0, 0, 8) };
        Grid.SetColumn(_sortBox, 0);
        Grid.SetColumn(_searchBox, 2);
        Grid.SetColumn(addPlcButton, 4);
        Grid.SetColumn(toggleCsvButton, 6);
        Grid.SetColumn(fileCsvButton, 8);
        topBar.Children.Add(_sortBox);
        topBar.Children.Add(_searchBox);
        topBar.Children.Add(addPlcButton);
        topBar.Children.Add(toggleCsvButton);
        topBar.Children.Add(fileCsvButton);

        var header = BuildHeaderRow();
        _list.ItemTemplate = new FuncDataTemplate<PlcCrossReferenceRow>((row, _) => BuildDataRow(row), supportsRecycling: false);

        _sortBox.SelectionChanged += (_, _) => RefreshRows();
        _searchBox.TextChanged += (_, _) => RefreshRows();
        _list.SelectionChanged += (_, _) => LoadSelectedRow();

        PopulateFunctions(project);
        _applyButton.Click += (_, _) => SaveChannel();

        var editForm = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*,12,130,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 8, ColumnSpacing = 8 };
        AddPair(editForm, 0, 0, UiStrings.T("PLC adresi"), _address);
        AddPair(editForm, 0, 3, UiStrings.T("Sembolik ad"), _symbol);
        AddPair(editForm, 1, 0, UiStrings.T("Açıklama"), _description);
        AddPair(editForm, 1, 3, UiStrings.T("Saha function'ı"), _functionBox);

        var editBox = new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#FAF8F3"),
            Padding = new Thickness(12, 10),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new DockPanel
                    {
                        LastChildFill = false,
                        Children = { _selectedSummary },
                    },
                    editForm,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { _applyButton } },
                },
            },
        };

        var closeButton = new Button { Content = UiStrings.T("Kapat"), IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        closeButton.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 6, 16, 12), Children = { closeButton } };

        var body = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 6),
            Spacing = 6,
            Children =
            {
                topBar,
                _csvPanel,
                header,
                new Border { BorderBrush = Brush("#D0CBC0"), BorderThickness = new Thickness(1, 0, 1, 1), Child = _list },
                editBox,
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_status, Avalonia.Controls.Dock.Bottom);
        _status.Margin = new Thickness(16, 2);
        root.Children.Add(footer);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;

        RefreshRows();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close();
            }
        };
    }

    private Border BuildCsvImportPanel()
    {
        var runButton = new Button { Content = UiStrings.T("Adres listesini uygula"), MinWidth = 150, HorizontalContentAlignment = HorizontalAlignment.Center };
        runButton.Click += (_, _) =>
        {
            var imported = _session.ImportPlcAddressesCsv(_csvInput.Text ?? string.Empty);
            if (imported.IsFailure)
            {
                SetStatus(imported.Error!.Message, error: true);
                return;
            }

            Changed = true;
            if (_session.Project is { } updatedProject)
            {
                PopulateFunctions(updatedProject);
            }

            SetStatus($"{imported.Value} {UiStrings.T("PLC kanalı güncellendi.")}", error: false);
            _csvPanel.IsVisible = false;
            RefreshRows();
        };

        return new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#F4EFE3"),
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = UiStrings.T("CSV / noktalı virgül listesi (Adres, SembolikAd, Açıklama veya Kanal, Adres, SembolikAd, Açıklama):"), FontSize = 12 },
                    _csvInput,
                    new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { runButton } },
                },
            },
        };
    }

    private async Task ImportCsvFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = UiStrings.T("PLC adres listesi (.csv / .txt) aç"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("CSV / Metin") { Patterns = ["*.csv", "*.txt"] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus($"{UiStrings.T("Dosya okunamadı")}: {ex.Message}", error: true);
            return;
        }

        var imported = _session.ImportPlcAddressesCsv(text);
        if (imported.IsFailure)
        {
            SetStatus(imported.Error!.Message, error: true);
            return;
        }

        Changed = true;
        if (_session.Project is { } updatedProject)
        {
            PopulateFunctions(updatedProject);
        }

        SetStatus($"{imported.Value} {UiStrings.T("PLC kanalı dosyadan güncellendi.")}", error: false);
        RefreshRows();
    }

    private void PopulateFunctions(Project project)
    {
        var items = new List<FunctionChoice> { new(null, UiStrings.T("— (atanmamış)")) };
        foreach (var device in project.Devices
            .Where(d => d.Kind != DeviceKind.Junction)
            .OrderBy(d => project.DesignationOf(d).ToString(), StringComparer.Ordinal))
        {
            var tag = project.DesignationOf(device).ToString();
            foreach (var fn in device.Functions)
            {
                var pins = string.Join(", ", fn.ConnectionPoints.Select(p => p.Name));
                var desc = string.IsNullOrWhiteSpace(fn.Description) ? string.Empty : $" · {fn.Description}";
                items.Add(new FunctionChoice(fn.Id, $"{tag} — {EditorSession.FunctionKindLabel(fn.Kind)} ({pins}){desc}"));
            }
        }

        _functionBox.ItemsSource = items;
        _functionBox.SelectedIndex = 0;
    }

    private void RefreshRows(ChannelId? selectChannel = null)
    {
        var sortByWire = _sortBox.SelectedIndex == 1;
        var rows = _session.PlcCrossReferenceRows(sortByWire, _searchBox.Text);
        _list.ItemsSource = rows;
        var match = selectChannel is { } id
            ? rows.ToList().FindIndex(r => r.ChannelId == id)
            : (rows.Count > 0 ? 0 : -1);
        _list.SelectedIndex = match;
        LoadSelectedRow();
    }

    private void LoadSelectedRow()
    {
        if (_list.SelectedItem is not PlcCrossReferenceRow row)
        {
            _selectedSummary.Text = _session.Project?.Controllers.Count == 0
                ? UiStrings.T("Projede tanımlı PLC denetleyicisi yok. Sağ üstteki 'Yeni PLC ekle (8 DI / 8 DO)' ile hemen oluşturabilirsiniz.")
                : UiStrings.T("Düzenlemek için tablodan bir kanal seçin.");
            _applyButton.IsEnabled = false;
            return;
        }

        _selectedSummary.Text = $"{row.Controller}  ›  {row.Module}  ·  {row.Channel}";
        _address.Text = row.Address ?? string.Empty;
        _symbol.Text = row.SymbolName ?? string.Empty;
        _description.Text = row.Description ?? string.Empty;
        var functions = _functionBox.ItemsSource as IReadOnlyList<FunctionChoice> ?? [];
        var fnIdx = 0;
        for (var i = 0; i < functions.Count; i++)
        {
            if (functions[i].Id == row.FunctionId)
            {
                fnIdx = i;
                break;
            }
        }

        _functionBox.SelectedIndex = fnIdx;
        _applyButton.IsEnabled = true;
    }

    private void SaveChannel()
    {
        if (_list.SelectedItem is not PlcCrossReferenceRow row)
        {
            return;
        }

        var fnId = (_functionBox.SelectedItem as FunctionChoice)?.Id;
        var updated = _session.AssignPlcChannel(row.ChannelId, _address.Text, _symbol.Text, _description.Text, fnId);
        if (updated.IsFailure)
        {
            SetStatus(updated.Error!.Message, error: true);
            return;
        }

        Changed = true;
        SetStatus($"Kanal güncellendi: {row.Module} {row.Channel}", error: false);
        RefreshRows(selectChannel: row.ChannelId);
    }

    private void SetStatus(string text, bool error)
    {
        _status.Foreground = Brush(error ? "#C0392B" : "#2E6F40");
        _status.Text = text;
    }

    private static ColumnDefinitions Columns() => new("85,130,55,95,110,125,105,*");

    private static Border BuildHeaderRow()
    {
        var grid = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = 6 };
        var headers = new[]
        {
            UiStrings.T("PLC Adresi"),
            UiStrings.T("Sembolik Ad"),
            UiStrings.T("Sinyal"),
            UiStrings.T("Tel / Damar"),
            UiStrings.T("Klemens"),
            UiStrings.T("Saha Ucu"),
            UiStrings.T("Modül Ucu"),
            UiStrings.T("Modül · Açıklama"),
        };
        for (var i = 0; i < headers.Length; i++)
        {
            var block = new TextBlock { Text = headers[i], FontWeight = FontWeight.SemiBold, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#E8E0CC"),
            Padding = new Thickness(8, 6),
            Child = grid,
        };
    }

    private static Grid BuildDataRow(PlcCrossReferenceRow? row)
    {
        var grid = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = 6 };
        if (row is null)
        {
            return grid;
        }

        var moduleDetail = string.IsNullOrWhiteSpace(row.Description) ? row.Module : $"{row.Module} · {row.Description}";
        var cells = new[]
        {
            row.Address ?? "—",
            row.SymbolName ?? "—",
            row.Channel,
            row.WireNumber ?? "—",
            row.Terminal ?? "—",
            row.FieldTarget ?? "—",
            row.ModulePoint ?? "—",
            moduleDetail,
        };
        for (var i = 0; i < cells.Length; i++)
        {
            var block = new TextBlock
            {
                Text = cells[i],
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(block, i);
            grid.Children.Add(block);
        }

        return grid;
    }

    private static void AddPair(Grid grid, int row, int col, string label, Control field)
    {
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(text, row);
        Grid.SetColumn(text, col);
        Grid.SetRow(field, row);
        Grid.SetColumn(field, col + 1);
        grid.Children.Add(text);
        grid.Children.Add(field);
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
