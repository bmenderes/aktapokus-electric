using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>
/// Potansiyel işareti penceresi (Faz 6 kalan model formları): bağlantı noktalarına L1, N, PE, 24VDC, 0V vb.
/// potansiyel adı ve türü tanımlar, var olan işaretleri listeler ve kaldırır.
/// </summary>
public sealed class PotentialWindow : Window
{
    private sealed record MarkerRow(PotentialSourceId Id, string Display)
    {
        public override string ToString() => Display;
    }

    private sealed record PointChoice(ConnectionPointId Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record KindOption(PotentialKind Kind, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly KindOption[] KindOptions =
    [
        new(PotentialKind.Phase, "Faz (AC)"),
        new(PotentialKind.Neutral, "Nötr (N)"),
        new(PotentialKind.ProtectiveEarth, "Koruma topraklaması (PE)"),
        new(PotentialKind.DcPositive, "DC (+)"),
        new(PotentialKind.DcNegative, "DC (-)"),
        new(PotentialKind.Other, "Diğer"),
    ];

    private static readonly (string Name, PotentialKind Kind)[] Presets =
    [
        ("L1", PotentialKind.Phase),
        ("L2", PotentialKind.Phase),
        ("L3", PotentialKind.Phase),
        ("N", PotentialKind.Neutral),
        ("PE", PotentialKind.ProtectiveEarth),
        ("24VDC", PotentialKind.DcPositive),
        ("0V", PotentialKind.DcNegative),
    ];

    private readonly EditorSession _session;
    private readonly ListBox _markers = new() { Height = 175 };
    private readonly ComboBox _points = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _name = new() { PlaceholderText = "örn. 24VDC veya L1" };
    private readonly ComboBox _kind = new() { ItemsSource = KindOptions, SelectedIndex = 3, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly Button _removeButton = new() { Content = "Seçili işareti sil", MinWidth = 130, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };

    public bool Changed { get; private set; }

    public PotentialWindow(EditorSession session, ConnectionPointId? selectedPoint = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var project = session.Project ?? throw new ArgumentException("Proje açık değil.", nameof(session));

        Title = $"Potansiyel işaretleri — {project.Name}";
        Width = 680;
        Height = 490;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var pointChoices = project.Devices
            .Where(device => device.Kind != DeviceKind.Junction)
            .OrderBy(device => project.DesignationOf(device).ToString(), StringComparer.Ordinal)
            .SelectMany(device => device.Functions.SelectMany(function => function.ConnectionPoints.Select(point =>
                new PointChoice(point.Id, $"{project.DesignationOf(device)}:{point.Name}"))))
            .ToList();
        _points.ItemsSource = pointChoices;
        var preselect = selectedPoint is { } target ? pointChoices.FindIndex(choice => choice.Id == target) : -1;
        _points.SelectedIndex = pointChoices.Count > 0 ? Math.Max(0, preselect) : -1;

        var presetBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var (presetName, presetKind) in Presets)
        {
            var chip = new Button { Content = presetName, MinWidth = 48, FontSize = 12 };
            chip.Click += (_, _) =>
            {
                _name.Text = presetName;
                _kind.SelectedIndex = Math.Max(0, Array.FindIndex(KindOptions, option => option.Kind == presetKind));
                _message.Text = string.Empty;
            };
            presetBar.Children.Add(chip);
        }

        var addButton = new Button { Content = "İşaret ekle", MinWidth = 104, HorizontalContentAlignment = HorizontalAlignment.Center };
        addButton.IsEnabled = pointChoices.Count > 0;
        addButton.Click += (_, _) =>
        {
            if (_points.SelectedItem is not PointChoice pointChoice)
            {
                _message.Text = "Önce bir bağlantı noktası seçin.";
                return;
            }

            var kind = (_kind.SelectedItem as KindOption)?.Kind ?? PotentialKind.Other;
            var added = _session.AddPotentialMarker(pointChoice.Id, _name.Text ?? string.Empty, kind);
            if (added.IsFailure)
            {
                _message.Text = added.Error!.Message;
                return;
            }

            Changed = true;
            _message.Text = string.Empty;
            RefreshMarkers(selectId: added.Value);
        };

        _markers.SelectionChanged += (_, _) => _removeButton.IsEnabled = _markers.SelectedItem is MarkerRow;
        _removeButton.Click += (_, _) =>
        {
            if (_markers.SelectedItem is not MarkerRow row)
            {
                return;
            }

            var removed = _session.RemovePotentialMarker(row.Id);
            if (removed.IsFailure)
            {
                _message.Text = removed.Error!.Message;
                return;
            }

            Changed = true;
            _message.Text = string.Empty;
            RefreshMarkers(selectId: null);
        };

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), RowSpacing = 8, ColumnSpacing = 8 };
        var rows = new (string Label, Control Field)[]
        {
            ("Bağlantı noktası", _points),
            ("Hızlı seçim", presetBar),
            ("Potansiyel adı", _name),
            ("Potansiyel türü", _kind),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            form.Children.Add(label);
            form.Children.Add(rows[i].Field);
        }

        var close = new Button { Content = "Kapat", IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 8), Children = { close } };

        var body = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Projede tanımlı potansiyel işaretleri", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                _markers,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addButton, _removeButton } },
                form,
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        _message.Margin = new Thickness(16, 0);
        root.Children.Add(footer);
        root.Children.Add(_message);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;

        RefreshMarkers(selectId: null);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close();
            }
        };
    }

    private void RefreshMarkers(PotentialSourceId? selectId)
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var rows = project.PotentialSources
            .OrderBy(source => source.Name, StringComparer.Ordinal)
            .Select(source =>
            {
                var point = project.DesignationOf(source.At)?.ToString() ?? source.At.ToString();
                var kindLabel = KindOptions.FirstOrDefault(option => option.Kind == source.Kind)?.Label ?? source.Kind.ToString();
                return new MarkerRow(source.Id, $"{source.Name}  ({kindLabel})  ·  {point}");
            })
            .ToList();
        _markers.ItemsSource = rows;
        var match = selectId is { } id ? rows.FirstOrDefault(row => row.Id == id) : null;
        _markers.SelectedItem = match;
        _removeButton.IsEnabled = match is not null;
    }
}
