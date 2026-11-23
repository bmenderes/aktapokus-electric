using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;
using FluentIcons.Common;

namespace Ake.Editor;

/// <summary>
/// Klemens şeridi düzenleyicisi: klemensleri sırala, adlandır, ekle, kaldır ve komşu klemensler arasına iç/dış köprü koy.
/// Tamam nihai listeyi (<see cref="TerminalStripEdits"/>), İptal ya da Esc <c>null</c> döner. Değişiklikler ancak Tamam ile
/// modele gider ve tek geri alma adımıdır.
/// </summary>
public sealed class TerminalStripWindow : Window
{
    private readonly TerminalStripData _data;
    private readonly List<RowState> _rows;
    private readonly StackPanel _list = new() { Spacing = 4 };
    private readonly TextBlock _message = new() { Foreground = new SolidColorBrush(Color.Parse("#c0392b")), TextWrapping = TextWrapping.Wrap };
    private readonly Button _ok = new() { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };

    public TerminalStripWindow(TerminalStripData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _rows = [.. data.Terminals.Select(row => new RowState(row))];

        Title = $"Klemens şeridi — {data.Designation}";
        Width = 820;
        Height = 540;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var add = new Button { Content = "Klemens ekle", MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
        var renumber = new Button { Content = "Yeniden numarala", MinWidth = 140, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        add.Click += (_, _) =>
        {
            _rows.Add(new RowState(new TerminalRow(null, EditorSession.NextTerminalName(_rows.Select(row => row.Name)), false, false, 0, string.Empty, 1, 1, TerminalRole.Normal)));
            Rebuild(focusLast: true);
        };
        renumber.Click += (_, _) =>
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                _rows[i].Name = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            Rebuild();
        };
        _ok.Click += (_, _) => Close(Collect());
        cancel.Click += (_, _) => Close(null);

        var header = new Grid { ColumnDefinitions = Columns(), Margin = new Thickness(0, 0, 10, 4) };
        AddHeader(header, 2, "Klemens");
        AddHeader(header, 3, "Kat");
        AddHeader(header, 4, "Bağl.");
        AddHeader(header, 5, "Tür");
        AddHeader(header, 6, "İç köprü ↓");
        AddHeader(header, 7, "Dış köprü ↓");

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        left.Children.Add(add);
        left.Children.Add(renumber);
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(_ok);
        right.Children.Add(cancel);
        var buttons = new DockPanel { Margin = new Thickness(0, 8, 0, 0), LastChildFill = false };
        DockPanel.SetDock(left, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(right, Avalonia.Controls.Dock.Right);
        buttons.Children.Add(left);
        buttons.Children.Add(right);

        DockPanel.SetDock(header, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        Content = new DockPanel
        {
            Margin = new Thickness(14),
            Children = { header, buttons, _message, new ScrollViewer { Content = new Border { Padding = new Thickness(0, 0, 10, 0), Child = _list } } },
        };

        Rebuild();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close(null);
            }
        };
    }

    private static ColumnDefinitions Columns() => new("28,28,*,58,58,120,70,70,28,130");

    private static void AddHeader(Grid grid, int column, string text)
    {
        var block = new TextBlock { Text = text, FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#5F6670")) };
        Grid.SetColumn(block, column);
        grid.Children.Add(block);
    }

    private void Rebuild(bool focusLast = false)
    {
        _list.Children.Clear();
        TextBox? last = null;
        for (var i = 0; i < _rows.Count; i++)
        {
            var (row, index) = (_rows[i], i);
            var grid = new Grid { ColumnDefinitions = Columns(), ColumnSpacing = 4 };

            var up = Round(Symbol.ChevronUp, "Yukarı al");
            up.IsEnabled = index > 0;
            up.Click += (_, _) => Swap(index, index - 1);
            var down = Round(Symbol.ChevronDown, "Aşağı al");
            down.IsEnabled = index < _rows.Count - 1;
            down.Click += (_, _) => Swap(index, index + 1);

            var name = new TextBox { Text = row.Name };
            name.TextChanged += (_, _) =>
            {
                row.Name = name.Text ?? string.Empty;
                Validate();
            };
            row.Box = name;
            last = name;

            var inner = new CheckBox { IsChecked = row.Inner, IsEnabled = index < _rows.Count - 1, HorizontalAlignment = HorizontalAlignment.Center };
            inner.IsCheckedChanged += (_, _) => row.Inner = inner.IsChecked == true;
            var outer = new CheckBox { IsChecked = row.Outer, IsEnabled = index < _rows.Count - 1, HorizontalAlignment = HorizontalAlignment.Center };
            outer.IsCheckedChanged += (_, _) => row.Outer = outer.IsChecked == true;

            var remove = Round(Symbol.Dismiss, row.Connections > 0 ? "Bağlantısı var; önce bağlantıyı kaldırın" : "Klemensi kaldır");
            remove.IsEnabled = row.Connections == 0 && _rows.Count > 1;
            remove.Click += (_, _) =>
            {
                _rows.RemoveAt(index);
                Rebuild();
            };

            var info = new TextBlock
            {
                Text = row.IsNew ? "yeni" : $"{row.Where} · {row.Connections} bağlantı",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.Parse("#5F6670")),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };

            Place(grid, up, 0);
            Place(grid, down, 1);
            var level = new ComboBox { ItemsSource = new[] { "1", "2", "3" }, SelectedIndex = Math.Clamp(row.Level, 1, Function.MaxLevel) - 1, Width = 66 };
            level.SelectionChanged += (_, _) =>
            {
                row.Level = Math.Max(1, level.SelectedIndex + 1);
                Validate();
            };

            // Taraf başına bağlantı sayısı ve tür (sıradan, PE, sonlandırıcı): yeni satırda seçilir; var olan klemenste bağlantı sayısı
            // değişmez, tür yalnızca sıradan ile PE arasında değişir.
            var ways = new ComboBox { ItemsSource = new[] { "1", "2", "3", "4" }, SelectedIndex = Math.Clamp(row.Ways, 1, Function.MaxWays) - 1, Width = 54, IsEnabled = row.IsNew && row.Role != TerminalRole.EndCover };
            ways.SelectionChanged += (_, _) => row.Ways = Math.Max(1, ways.SelectedIndex + 1);
            var roles = new[] { "Sıradan", "PE", "Sonlandırıcı" };
            var role = new ComboBox
            {
                ItemsSource = roles,
                SelectedIndex = row.Role switch { TerminalRole.ProtectiveEarth => 1, TerminalRole.EndCover => 2, _ => 0 },
                Width = 116,
                IsEnabled = row.IsNew || row.Role != TerminalRole.EndCover,
            };
            role.SelectionChanged += (_, _) =>
            {
                row.Role = role.SelectedIndex switch { 1 => TerminalRole.ProtectiveEarth, 2 => TerminalRole.EndCover, _ => TerminalRole.Normal };
                ways.IsEnabled = row.IsNew && row.Role != TerminalRole.EndCover;
                inner.IsEnabled = outer.IsEnabled = index < _rows.Count - 1 && row.Role != TerminalRole.EndCover;
            };
            // Var olan klemenste sonlandırıcıya geçiş yoktur (noktalar silinirdi): seçenek yalnızca yeni satırda.
            if (!row.IsNew)
            {
                role.SelectionChanged += (_, _) =>
                {
                    if (role.SelectedIndex == 2)
                    {
                        role.SelectedIndex = row.Role == TerminalRole.ProtectiveEarth ? 1 : 0;
                    }
                };
            }

            Place(grid, name, 2);
            Place(grid, level, 3);
            Place(grid, ways, 4);
            Place(grid, role, 5);
            Place(grid, inner, 6);
            Place(grid, outer, 7);
            Place(grid, remove, 8);
            Place(grid, info, 9);
            _list.Children.Add(grid);
        }

        Validate();
        if (focusLast && last is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                last.Focus();
                last.SelectAll();
            });
        }
    }

    private void Swap(int a, int b)
    {
        (_rows[a], _rows[b]) = (_rows[b], _rows[a]);
        Rebuild();
    }

    private static void Place(Grid grid, Control control, int column)
    {
        Grid.SetColumn(control, column);
        grid.Children.Add(control);
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

    // Boş, boşluklu, ':' içeren ya da yinelenen ad kırmızı çerçeve alır; hata varken Tamam kapalıdır.
    private void Validate()
    {
        var problems = new List<string>();
        foreach (var row in _rows)
        {
            var name = row.Name.Trim();
            var invalid = name.Length == 0 || name.Any(char.IsWhiteSpace) || name.Contains(':', StringComparison.Ordinal);
            var duplicate = !invalid && _rows.Count(other => other.Name.Trim() == name && other.Level == row.Level) > 1;
            if (row.Box is { } box)
            {
                box.BorderBrush = invalid || duplicate ? new SolidColorBrush(Color.Parse("#c0392b")) : null;
            }

            if (invalid)
            {
                problems.Add("Klemens adı boş olamaz, boşluk ya da ':' içeremez.");
            }
            else if (duplicate)
            {
                problems.Add($"'{name}' adlı birden çok klemens var.");
            }
        }

        _message.Text = problems.Count == 0 ? string.Empty : problems[0];
        _ok.IsEnabled = problems.Count == 0 && _rows.Count > 0;
    }

    private TerminalStripEdits Collect() => new(
        _data.Device,
        [.. _rows.Select(row => new TerminalRow(row.Id, row.Name.Trim(), row.Inner, row.Outer, row.Connections, row.Where, row.Level, row.Ways, row.Role))]);

    private sealed class RowState(TerminalRow source)
    {
        public FunctionId? Id { get; } = source.Id;

        public string Name { get; set; } = source.Name;

        public bool Inner { get; set; } = source.JumperInner;

        public bool Outer { get; set; } = source.JumperOuter;

        public int Connections { get; } = source.Connections;

        public string Where { get; } = source.Where;

        public int Level { get; set; } = source.Level;

        public int Ways { get; set; } = source.Ways;

        public TerminalRole Role { get; set; } = source.Role;

        public bool IsNew => Id is null;

        public TextBox? Box { get; set; }
    }
}
