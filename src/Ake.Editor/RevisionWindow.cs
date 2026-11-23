using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>
/// Revizyon ekleme penceresi (ADR-0050): revizyon işareti (sıradaki önerilir), tarih, hazırlayan ve ne değiştiği. Tamam kaydı, İptal ya da Esc
/// <c>null</c> döner. Açıklama boş olamaz.
/// </summary>
public sealed class RevisionWindow : Window
{
    private readonly TextBox _mark = new();
    private readonly TextBox _date = new();
    private readonly TextBox _author = new();
    private readonly TextBox _description = new() { AcceptsReturn = false, PlaceholderText = "Ne değişti?" };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public RevisionWindow(Revision suggestion, IReadOnlyList<Revision> existing)
    {
        ArgumentNullException.ThrowIfNull(suggestion);
        ArgumentNullException.ThrowIfNull(existing);

        Title = "Revizyon ekle";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _mark.Text = suggestion.Mark;
        _date.Text = suggestion.Date;
        _author.Text = suggestion.Author;
        _description.Text = suggestion.Description;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 8 };
        var rows = new (string Label, Control Field)[] { ("Revizyon işareti", _mark), ("Tarih", _date), ("Hazırlayan", _author), ("Açıklama", _description) };
        for (var i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }

        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            var mark = _mark.Text?.Trim() ?? string.Empty;
            if (mark.Length == 0 || mark.Any(char.IsWhiteSpace) || mark.Length > 8)
            {
                _message.Text = "Revizyon işareti boş olamaz, boşluk içeremez ve en çok 8 karakter olmalı.";
                return;
            }

            if (existing.Any(revision => revision.Mark == mark))
            {
                _message.Text = $"'{mark}' işaretli bir revizyon zaten var.";
                return;
            }

            if (string.IsNullOrWhiteSpace(_description.Text))
            {
                _message.Text = "Açıklama boş olamaz.";
                return;
            }

            Close(new Revision(mark, _date.Text?.Trim() ?? string.Empty, _author.Text?.Trim() ?? string.Empty, _description.Text.Trim()));
        };
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(18, 16),
            Spacing = 12,
            Children =
            {
                grid,
                _message,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { ok, cancel } },
            },
        };

        Opened += (_, _) => _description.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }
}
