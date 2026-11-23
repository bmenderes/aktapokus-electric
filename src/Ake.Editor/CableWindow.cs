using System.Globalization;
using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ake.Editor;

/// <summary>
/// Kablo ve damar düzenleme penceresi: çok damarlı kabloları tanımlama, renk kartelası seçimi,
/// tel numarası / kesit / renk / çizgi biçimi (düz, kesik, çizgi-nokta, noktalı) ve bağlantı türü atama.
/// </summary>
public sealed class CableWindow : Window
{
    private sealed record CableRow(Cable Cable, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record ConnectionRow(Connection Connection, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record CoreChoice(CableCoreId? CoreId, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record KindChoice(ConnectionKindId? Id, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly LineStyle[] LineStyles =
    [
        LineStyle.Solid,
        LineStyle.Dashed,
        LineStyle.DashDot,
        LineStyle.Dotted,
    ];

    private readonly EditorSession _session;
    private readonly ConnectionId? _initialConnection;
    private readonly ListBox _cableList = new() { Height = 150 };
    private readonly TextBox _cableTag = new() { PlaceholderText = "örn. W1" };
    private readonly TextBox _cableType = new() { PlaceholderText = "örn. 4G1.5" };
    private readonly TextBox _coresText = new()
    {
        AcceptsReturn = true,
        Height = 130,
        PlaceholderText = "Her satır bir damar (Ad : Kesit : Renk)\n1 : 1.5 : BK\n2 : 1.5 : BN\nPE : 1.5 : GNYE",
    };

    private readonly Button _updateCable = new() { Content = UiStrings.T("Güncelle"), MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _removeCable = new() { Content = UiStrings.T("Sil"), MinWidth = 72, HorizontalContentAlignment = HorizontalAlignment.Center };

    private readonly ComboBox _connections = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly RadioButton _wireMode = new() { Content = UiStrings.T("Fiziksel tel"), IsChecked = true };
    private readonly RadioButton _coreMode = new() { Content = UiStrings.T("Kablo damarı") };
    private readonly TextBox _wireNumber = new() { PlaceholderText = "örn. W101" };
    private readonly TextBox _wireSection = new() { PlaceholderText = "örn. 1.5" };
    private readonly TextBox _wireColor = new() { PlaceholderText = "BK, RD, BU veya #RRGGBB" };
    private readonly Border _wireColorPreview = new()
    {
        Width = 28,
        Height = 28,
        CornerRadius = new CornerRadius(4),
        BorderBrush = new SolidColorBrush(Color.Parse("#8F8670")),
        BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.Parse("#111111")),
    };
    private readonly ComboBox _lineStyleBox = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = LineStyles.Select(WireColorPalette.LineStyleLabel).ToList(),
        SelectedIndex = 0,
    };
    private readonly ComboBox _kindBox = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _addStdKindsButton = new() { Content = "+", Width = 32, Padding = new Thickness(4, 2) };
    private readonly ComboBox _coreChoices = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _applyConnection = new() { Content = UiStrings.T("Bağlantıya uygula"), Classes = { "accent" }, MinWidth = 156, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _message = new() { Foreground = new SolidColorBrush(Color.Parse("#c0392b")), TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private bool _syncingKind;

    public bool Changed { get; private set; }

    public CableWindow(EditorSession session, ConnectionId? selectedConnection = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _initialConnection = selectedConnection;
        var project = session.Project ?? throw new ArgumentException("Proje açık değil.", nameof(session));

        Title = $"{UiStrings.T("Kablolar ve damar atama")} — {project.Name}";
        Width = 980;
        Height = 640;
        MinWidth = 860;
        MinHeight = 560;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var leftPane = BuildCablePane();
        var rightPane = BuildConnectionPane();

        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,*"), Margin = new Thickness(16) };
        Grid.SetColumn(leftPane, 0);
        Grid.SetColumn(rightPane, 2);
        columns.Children.Add(leftPane);
        columns.Children.Add(rightPane);

        var close = new Button { Content = UiStrings.T("Kapat"), IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8), Children = { close } };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        _message.Margin = new Thickness(16, 0);
        root.Children.Add(footer);
        root.Children.Add(_message);
        root.Children.Add(new ScrollViewer { Content = columns });
        Content = root;

        RefreshAll(selectedCable: project.Cables.FirstOrDefault()?.Id, selectedConnection: _initialConnection);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close();
            }
        };
    }

    private Control BuildCablePane()
    {
        var addCable = new Button { Content = UiStrings.T("Kablo ekle"), MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
        var clearCable = new Button { Content = UiStrings.T("Yeni / Temizle"), MinWidth = 104, HorizontalContentAlignment = HorizontalAlignment.Center };
        var preset3 = new Button { Content = "3×1.5", MinWidth = 56, FontSize = 12 };
        var preset4 = new Button { Content = "4G1.5", MinWidth = 56, FontSize = 12 };
        preset3.Click += (_, _) =>
        {
            _cableType.Text = "3x1.5";
            _coresText.Text = "1 : 1.5 : BN\n2 : 1.5 : BU\nPE : 1.5 : GNYE";
        };
        preset4.Click += (_, _) =>
        {
            _cableType.Text = "4G1.5";
            _coresText.Text = "1 : 1.5 : BK\n2 : 1.5 : BN\n3 : 1.5 : GY\nPE : 1.5 : GNYE";
        };

        _cableList.SelectionChanged += (_, _) => LoadSelectedCable();
        clearCable.Click += (_, _) =>
        {
            _cableList.SelectedItem = null;
            _cableTag.Text = NextCableTag();
            _cableType.Text = string.Empty;
            _coresText.Text = string.Empty;
            SetMessage(string.Empty, error: false);
        };

        addCable.Click += (_, _) =>
        {
            if (!TryParseCores(_coresText.Text, null, out var parsed, out var error))
            {
                SetMessage(error, error: true);
                return;
            }

            var added = _session.AddCableItem(
                _cableTag.Text ?? string.Empty,
                _cableType.Text,
                parsed.Select(c => (c.Designation, c.CrossSectionMm2, c.Color)).ToList());
            if (added.IsFailure)
            {
                SetMessage(added.Error!.Message, error: true);
                return;
            }

            Changed = true;
            SetMessage("Kablo eklendi.", error: false);
            RefreshAll(selectedCable: added.Value, selectedConnection: CurrentConnectionId());
        };

        _updateCable.Click += (_, _) =>
        {
            if (_cableList.SelectedItem is not CableRow row)
            {
                return;
            }

            if (!TryParseCores(_coresText.Text, row.Cable.Cores, out var parsed, out var error))
            {
                SetMessage(error, error: true);
                return;
            }

            var updated = _session.UpdateCableItem(row.Cable.Id, _cableTag.Text ?? string.Empty, _cableType.Text, parsed);
            if (updated.IsFailure)
            {
                SetMessage(updated.Error!.Message, error: true);
                return;
            }

            Changed = true;
            SetMessage("Kablo güncellendi.", error: false);
            RefreshAll(selectedCable: row.Cable.Id, selectedConnection: CurrentConnectionId());
        };

        _removeCable.Click += (_, _) =>
        {
            if (_cableList.SelectedItem is not CableRow row)
            {
                return;
            }

            var removed = _session.RemoveCableItem(row.Cable.Id);
            if (removed.IsFailure)
            {
                SetMessage(removed.Error!.Message, error: true);
                return;
            }

            Changed = true;
            SetMessage("Kablo silindi.", error: false);
            RefreshAll(selectedCable: null, selectedConnection: CurrentConnectionId());
        };

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 6, ColumnSpacing = 8 };
        var tagLabel = new TextBlock { Text = UiStrings.T("Ürün etiketi (-)"), VerticalAlignment = VerticalAlignment.Center };
        var typeLabel = new TextBlock { Text = UiStrings.T("Kablo tipi"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(tagLabel, 0);
        Grid.SetRow(_cableTag, 0);
        Grid.SetColumn(_cableTag, 1);
        Grid.SetRow(typeLabel, 1);
        Grid.SetRow(_cableType, 1);
        Grid.SetColumn(_cableType, 1);
        form.Children.Add(tagLabel);
        form.Children.Add(_cableTag);
        form.Children.Add(typeLabel);
        form.Children.Add(_cableType);

        var coreHeader = new DockPanel { LastChildFill = false };
        var coreLabel = new TextBlock { Text = UiStrings.T("Damarlar (Ad : Kesit : Renk)"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Opacity = 0.85 };
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { preset3, preset4 } };
        DockPanel.SetDock(coreLabel, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(presets, Avalonia.Controls.Dock.Right);
        coreHeader.Children.Add(coreLabel);
        coreHeader.Children.Add(presets);

        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = UiStrings.T("Projedeki kablolar"), FontWeight = FontWeight.SemiBold },
                _cableList,
                form,
                coreHeader,
                _coresText,
                BuildColorPaletteBar(AppendCoreColor),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { addCable, _updateCable, _removeCable, clearCable } },
            },
        };
    }

    private Control BuildConnectionPane()
    {
        _connections.SelectionChanged += (_, _) => LoadSelectedConnection();
        _wireMode.IsCheckedChanged += (_, _) => UpdateModeControls();
        _coreMode.IsCheckedChanged += (_, _) => UpdateModeControls();
        _wireColor.TextChanged += (_, _) => UpdateColorPreview();
        _kindBox.SelectionChanged += (_, _) =>
        {
            if (!_syncingKind && (_kindBox.SelectedItem as KindChoice)?.Id is { } id && _session.Project?.FindConnectionKind(id) is { } kind)
            {
                _wireColor.Text = kind.Color;
                var styleIndex = Array.IndexOf(LineStyles, kind.Style);
                if (styleIndex >= 0)
                {
                    _lineStyleBox.SelectedIndex = styleIndex;
                }
            }
        };

        ToolTip.SetTip(_addStdKindsButton, UiStrings.T("Standart türleri ekle"));
        _addStdKindsButton.Click += (_, _) =>
        {
            var res = _session.AddStandardConnectionKinds();
            if (res.IsFailure)
            {
                SetMessage(res.Error!.Message, error: true);
                return;
            }

            Changed = true;
            SetMessage("Standart bağlantı türleri eklendi.", error: false);
            RefreshKindChoices((_kindBox.SelectedItem as KindChoice)?.Id);
        };

        _applyConnection.Click += (_, _) => ApplySelectedConnection();

        var colorEntryRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto") };
        Grid.SetColumn(_wireColor, 0);
        Grid.SetColumn(_wireColorPreview, 2);
        colorEntryRow.Children.Add(_wireColor);
        colorEntryRow.Children.Add(_wireColorPreview);

        var clearColorButton = new Button { Content = "×", Width = 26, Height = 26, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(clearColorButton, "Rengi temizle");
        clearColorButton.Click += (_, _) =>
        {
            _wireColor.Text = string.Empty;
            _kindBox.SelectedIndex = 0;
        };

        var palettePanel = BuildColorPaletteBar(code =>
        {
            _wireColor.Text = code;
            if ((_kindBox.SelectedItem as KindChoice)?.Id is not null)
            {
                _kindBox.SelectedIndex = 0;
            }
        });
        if (palettePanel is WrapPanel wp)
        {
            wp.Children.Add(clearColorButton);
        }

        var kindRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,6,Auto") };
        Grid.SetColumn(_kindBox, 0);
        Grid.SetColumn(_addStdKindsButton, 2);
        kindRow.Children.Add(_kindBox);
        kindRow.Children.Add(_addStdKindsButton);

        var wireForm = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto"),
            RowSpacing = 8,
            ColumnSpacing = 8,
        };
        var rows = new (string Label, Control Field)[]
        {
            (UiStrings.T("Tel numarası"), _wireNumber),
            (UiStrings.T("Kesit (mm²)"), _wireSection),
            (UiStrings.T("Renk"), colorEntryRow),
            (UiStrings.T("Renk kartelası"), palettePanel),
            (UiStrings.T("Çizgi biçimi"), _lineStyleBox),
            (UiStrings.T("Bağlantı türü"), kindRow),
            (UiStrings.T("Kablo damarı"), _coreChoices),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            wireForm.Children.Add(label);
            wireForm.Children.Add(rows[i].Field);
        }

        return new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = UiStrings.T("Bağlantıya tel / kablo damarı ata"), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = UiStrings.T("Bağlantı seçimi:"), FontSize = 12, Opacity = 0.85 },
                _connections,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _wireMode, _coreMode } },
                wireForm,
                _applyConnection,
            },
        };
    }

    private void ApplySelectedConnection()
    {
        if (_connections.SelectedItem is not ConnectionRow row)
        {
            return;
        }

        var style = _lineStyleBox.SelectedIndex >= 0 && _lineStyleBox.SelectedIndex < LineStyles.Length
            ? LineStyles[_lineStyleBox.SelectedIndex]
            : LineStyle.Solid;
        var kindId = (_kindBox.SelectedItem as KindChoice)?.Id;

        if (_coreMode.IsChecked == true)
        {
            var choice = _coreChoices.SelectedItem as CoreChoice;
            if (choice?.CoreId is not { } coreId)
            {
                SetMessage("Atanacak bir kablo damarı seçin (ya da Fiziksel tel seçeneğini kullanın).", error: true);
                return;
            }

            var assigned = _session.AssignConnectionAppearance(row.Connection.Id, null, null, null, coreId, style, kindId);
            if (assigned.IsFailure)
            {
                SetMessage(assigned.Error!.Message, error: true);
                return;
            }
        }
        else
        {
            decimal? section = null;
            if (!string.IsNullOrWhiteSpace(_wireSection.Text))
            {
                var normalized = _wireSection.Text.Trim().Replace(',', '.');
                if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                {
                    SetMessage("Kesit sıfırdan büyük bir sayı olmalıdır (örn. 1.5).", error: true);
                    return;
                }

                section = parsed;
            }

            var assigned = _session.AssignConnectionAppearance(
                row.Connection.Id,
                _wireNumber.Text,
                section,
                _wireColor.Text,
                null,
                style,
                kindId);
            if (assigned.IsFailure)
            {
                SetMessage(assigned.Error!.Message, error: true);
                return;
            }
        }

        Changed = true;
        SetMessage("Bağlantı özellikleri güncellendi.", error: false);
        RefreshAll(selectedCable: (_cableList.SelectedItem as CableRow)?.Cable.Id, selectedConnection: row.Connection.Id);
    }

    private static Control BuildColorPaletteBar(Action<string> onPick)
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var swatch in WireColorPalette.Standard)
        {
            var btn = new Button
            {
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 0, 4, 4),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.Parse(swatch.Code == "WH" ? "#F4F4F4" : swatch.Hex)),
                BorderBrush = new SolidColorBrush(Color.Parse("#5E5645")),
                BorderThickness = new Thickness(1),
                Content = new Border(),
            };
            var localizedName = UiStrings.Current == UiLanguage.English ? swatch.NameEn : swatch.NameTr;
            ToolTip.SetTip(btn, $"{swatch.Code} — {localizedName}");
            var code = swatch.Code;
            btn.Click += (_, _) => onPick(code);
            panel.Children.Add(btn);
        }

        return panel;
    }

    private void AppendCoreColor(string colorCode)
    {
        var text = _coresText.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            _coresText.Text = $"1 : 1.5 : {colorCode}";
            return;
        }

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None).ToList();
        var targetIndex = lines.FindLastIndex(line => !string.IsNullOrWhiteSpace(line));
        if (targetIndex < 0)
        {
            _coresText.Text = $"1 : 1.5 : {colorCode}";
            return;
        }

        var parts = lines[targetIndex].Split(':', 3, StringSplitOptions.TrimEntries);
        var name = parts[0];
        var section = parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]) ? parts[1] : "1.5";
        lines[targetIndex] = $"{name} : {section} : {colorCode}";
        _coresText.Text = string.Join("\n", lines);
    }

    private void RefreshAll(CableId? selectedCable, ConnectionId? selectedConnection)
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var cableRows = project.Cables
            .OrderBy(cable => cable.ProductTag, StringComparer.Ordinal)
            .Select(cable =>
            {
                var names = string.Join(", ", cable.Cores.Select(core => core.Designation));
                var type = string.IsNullOrWhiteSpace(cable.Type) ? string.Empty : $" · {cable.Type}";
                return new CableRow(cable, $"-{cable.ProductTag}{type} · {cable.Cores.Count} damar ({names})");
            })
            .ToList();
        _cableList.ItemsSource = cableRows;
        _cableList.SelectedItem = selectedCable is { } cid
            ? cableRows.FirstOrDefault(row => row.Cable.Id == cid)
            : cableRows.FirstOrDefault();
        LoadSelectedCable();

        string PointLabel(ConnectionPointId pointId) =>
            project.DesignationOf(pointId)?.ToString() ?? pointId.ToString();

        var coreLabels = project.Cables
            .SelectMany(cable => cable.Cores.Select(core => (core.Id, Label: $"-{cable.ProductTag}:{core.Designation}")))
            .ToDictionary(entry => entry.Id, entry => entry.Label);

        var connectionRows = project.Connections
            .Select(connection =>
            {
                var medium = connection.CoreId is { } coreId && coreLabels.TryGetValue(coreId, out var coreText)
                    ? coreText
                    : connection.Wire?.Number is { } num ? $"Tel {num}" : "numarasız tel";
                var kindTag = connection.KindId is { } kid && project.FindConnectionKind(kid) is { } kind
                    ? $" · {kind.Name}"
                    : string.Empty;
                return new ConnectionRow(connection, $"{PointLabel(connection.From)} → {PointLabel(connection.To)}  [{medium}{kindTag}]");
            })
            .ToList();
        _connections.ItemsSource = connectionRows;
        var chosenConn = selectedConnection is { } targetId
            ? connectionRows.FirstOrDefault(row => row.Connection.Id == targetId)
            : connectionRows.FirstOrDefault();
        _connections.SelectedItem = chosenConn;
        _applyConnection.IsEnabled = connectionRows.Count > 0;
        RefreshKindChoices(chosenConn?.Connection.KindId);
        LoadSelectedConnection();
    }

    private void RefreshKindChoices(ConnectionKindId? selectedKind)
    {
        _syncingKind = true;
        try
        {
            var items = new List<KindChoice> { new(null, "— (Özel renk / çizgi biçimi)") };
            if (_session.Project is { } project)
            {
                items.AddRange(project.ConnectionKinds.Select(k =>
                    new KindChoice(k.Id, $"{k.Name}  ({WireColorPalette.LineStyleLabel(k.Style)})")));
            }

            _kindBox.ItemsSource = items;
            var match = selectedKind is { } id ? items.FindIndex(item => item.Id == id) : 0;
            _kindBox.SelectedIndex = Math.Max(0, match);
        }
        finally
        {
            _syncingKind = false;
        }
    }

    private void LoadSelectedCable()
    {
        if (_cableList.SelectedItem is not CableRow row)
        {
            _updateCable.IsEnabled = false;
            _removeCable.IsEnabled = false;
            return;
        }

        _updateCable.IsEnabled = true;
        _removeCable.IsEnabled = true;
        _cableTag.Text = row.Cable.ProductTag;
        _cableType.Text = row.Cable.Type ?? string.Empty;
        _coresText.Text = string.Join("\n", row.Cable.Cores.Select(core =>
        {
            var sec = core.CrossSectionMm2 is { } s ? s.ToString(CultureInfo.InvariantCulture) : string.Empty;
            var clr = core.Color ?? string.Empty;
            return $"{core.Designation} : {sec} : {clr}".TrimEnd(' ', ':');
        }));
    }

    private void LoadSelectedConnection()
    {
        if (_session.Project is not { } project || _connections.SelectedItem is not ConnectionRow row)
        {
            _coreChoices.ItemsSource = Array.Empty<CoreChoice>();
            return;
        }

        var usedByOthers = project.Connections
            .Where(connection => connection.Id != row.Connection.Id && connection.CoreId is not null)
            .Select(connection => connection.CoreId!.Value)
            .ToHashSet();

        var choices = new List<CoreChoice> { new(null, "— (damar seçin)") };
        foreach (var cable in project.Cables.OrderBy(candidate => candidate.ProductTag, StringComparer.Ordinal))
        {
            foreach (var core in cable.Cores)
            {
                if (!usedByOthers.Contains(core.Id))
                {
                    var spec = string.Join(" ", new[]
                    {
                        core.CrossSectionMm2 is { } s ? $"{s.ToString(CultureInfo.InvariantCulture)}mm²" : null,
                        core.Color,
                    }.Where(p => p is not null));
                    choices.Add(new CoreChoice(core.Id, $"-{cable.ProductTag} : {core.Designation}" + (spec.Length == 0 ? string.Empty : $"  ({spec})")));
                }
            }
        }

        _coreChoices.ItemsSource = choices;
        if (row.Connection.CoreId is { } assignedCore)
        {
            _coreMode.IsChecked = true;
            _coreChoices.SelectedIndex = Math.Max(0, choices.FindIndex(choice => choice.CoreId == assignedCore));
            _wireNumber.Text = string.Empty;
            _wireSection.Text = string.Empty;
            _wireColor.Text = string.Empty;
        }
        else
        {
            _wireMode.IsChecked = true;
            _coreChoices.SelectedIndex = 0;
            _wireNumber.Text = row.Connection.Wire?.Number ?? string.Empty;
            _wireSection.Text = row.Connection.Wire?.CrossSectionMm2 is { } s ? s.ToString(CultureInfo.InvariantCulture) : string.Empty;
            _wireColor.Text = row.Connection.Wire?.Color ?? string.Empty;
        }

        var existingKind = row.Connection.KindId is { } kid ? project.FindConnectionKind(kid) : null;
        var styleIndex = existingKind is not null ? Array.IndexOf(LineStyles, existingKind.Style) : 0;
        _lineStyleBox.SelectedIndex = Math.Max(0, styleIndex);
        RefreshKindChoices(row.Connection.KindId);
        UpdateColorPreview();
        UpdateModeControls();
    }

    private void UpdateColorPreview()
    {
        var hex = string.IsNullOrWhiteSpace(_wireColor.Text) ? "#111111" : WireColorPalette.ResolveHex(_wireColor.Text);
        _wireColorPreview.Background = new SolidColorBrush(Color.Parse(hex));
    }

    private void UpdateModeControls()
    {
        var isCore = _coreMode.IsChecked == true;
        _wireNumber.IsEnabled = !isCore;
        _wireSection.IsEnabled = !isCore;
        _wireColor.IsEnabled = !isCore;
        _coreChoices.IsEnabled = isCore;
    }

    private void SetMessage(string text, bool error)
    {
        _message.Foreground = new SolidColorBrush(Color.Parse(error ? "#c0392b" : "#1b6b3a"));
        _message.Text = text;
    }

    private ConnectionId? CurrentConnectionId() => (_connections.SelectedItem as ConnectionRow)?.Connection.Id;

    private string NextCableTag()
    {
        if (_session.Project is null)
        {
            return "W1";
        }

        var n = 1;
        while (_session.Project.Cables.Any(cable => cable.ProductTag == $"W{n}"))
        {
            n++;
        }

        return $"W{n}";
    }

    private static bool TryParseCores(
        string? rawText,
        IReadOnlyList<CableCore>? existing,
        out List<(CableCoreId? Id, string Designation, decimal? CrossSectionMm2, string? Color)> cores,
        out string error)
    {
        cores = [];
        error = string.Empty;
        var lines = (rawText ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            error = "Kablo için en az bir damar satırı girin (örn. 1 : 1.5 : BK).";
            return false;
        }

        var remaining = existing is null ? [] : existing.ToList();
        for (var i = 0; i < lines.Length; i++)
        {
            var parts = lines[i].Split(':', 3, StringSplitOptions.TrimEntries);
            var designation = parts[0];
            if (string.IsNullOrWhiteSpace(designation))
            {
                error = $"{i + 1}. satırda damar adı boş olamaz.";
                return false;
            }

            decimal? section = null;
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
            {
                var normalized = parts[1].Replace(',', '.');
                if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
                {
                    error = $"'{designation}' damarının kesiti geçersiz: '{parts[1]}'.";
                    return false;
                }

                section = parsed;
            }

            var color = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2]) ? parts[2] : null;
            var matched = remaining.FirstOrDefault(c => c.Designation == designation)
                ?? (existing is not null && i < existing.Count && remaining.Contains(existing[i]) ? existing[i] : null);
            if (matched is not null)
            {
                remaining.Remove(matched);
            }

            cores.Add((matched?.Id, designation, section, color));
        }

        return true;
    }
}
