using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Ake.Editor;

/// <summary>
/// Kısayol düzenleme penceresi (Araçlar > Kısayollar). Satırdaki kutuya tıklayıp istenen tuşlara basılır; çakışma
/// altta bildirilir ve atama yapılmaz. Yuvarlak düğmeler kısayolu kaldırır ya da varsayılana döndürür. Tamam yeni
/// profili, İptal ya da Esc <c>null</c> döner.
/// </summary>
public sealed class KeyMapWindow : Window
{
    private readonly KeyMap _map;
    private readonly Dictionary<string, (TextBox Box, Button Reset)> _rows = [];
    private readonly TextBlock _message = new() { Foreground = new SolidColorBrush(Color.Parse("#c0392b")), TextWrapping = TextWrapping.Wrap };

    public KeyMapWindow(KeyMap current)
    {
        ArgumentNullException.ThrowIfNull(current);
        _map = current.Clone();

        Title = "Kısayollar";
        Width = 560;
        Height = 560;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var rows = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var command in EditorCommands.All)
        {
            rows.Children.Add(Row(command));
        }

        var resetAll = new Button { Content = "Hepsini sıfırla", MinWidth = 120, HorizontalContentAlignment = HorizontalAlignment.Center };
        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        resetAll.Click += (_, _) =>
        {
            _map.ResetAll();
            _message.Text = string.Empty;
            RefreshAll();
        };
        ok.Click += (_, _) => Close(_map);
        cancel.Click += (_, _) => Close(null);

        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        DockPanel.SetDock(resetAll, Avalonia.Controls.Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(ok);
        right.Children.Add(cancel);
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        buttons.Children.Add(resetAll);
        buttons.Children.Add(right);

        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            Margin = new Thickness(14),
            Children = { buttons, _message, new ScrollViewer { Content = rows } },
        };

        RefreshAll();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close(null);
            }
        };
    }

    private Grid Row(CommandInfo command)
    {
        var box = new TextBox { IsReadOnly = true, Width = 170, PlaceholderText = "—", HorizontalContentAlignment = HorizontalAlignment.Left };
        box.GotFocus += (_, _) => box.PlaceholderText = "Tuşlara bas";
        box.LostFocus += (_, _) => box.PlaceholderText = "—";
        box.KeyDown += (_, e) => Capture(command.Id, e);

        var clear = Round(Symbol.Dismiss, "Kısayolu kaldır");
        clear.Click += (_, _) =>
        {
            _map.Set(command.Id, null);
            _message.Text = string.Empty;
            RefreshAll();
        };
        var reset = Round(Symbol.ArrowReset, "Varsayılana döndür");
        reset.Click += (_, _) =>
        {
            _map.Reset(command.Id);
            _message.Text = string.Empty;
            RefreshAll();
        };
        _rows[command.Id] = (box, reset);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 6 };
        var label = new TextBlock { Text = command.Label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(box, 1);
        Grid.SetColumn(clear, 2);
        Grid.SetColumn(reset, 3);
        grid.Children.Add(label);
        grid.Children.Add(box);
        grid.Children.Add(clear);
        grid.Children.Add(reset);
        return grid;
    }

    private static Button Round(Symbol symbol, string tip)
    {
        var button = new Button
        {
            Content = new SymbolIcon { Symbol = symbol, FontSize = 14 },
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(button, tip);
        return button;
    }

    // Kutuya basılan tuş birleşimi komutun kısayolu olur. Yalnızca değiştirici tuşlar ve Esc yok sayılır (Esc yakalamayı bırakır).
    private void Capture(string commandId, KeyEventArgs e)
    {
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin or Key.None)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            Focus();
            return;
        }

        var result = _map.Set(commandId, new KeyGesture(e.Key, e.KeyModifiers).ToString());
        _message.Text = result.IsFailure ? result.Error!.Message : string.Empty;
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var (id, (box, reset)) in _rows)
        {
            box.Text = _map.GestureOf(id);
            reset.IsEnabled = _map.IsCustom(id);
            box.FontWeight = _map.IsCustom(id) ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }
}
