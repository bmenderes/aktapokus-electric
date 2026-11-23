using System.Globalization;
using Ake.Core.Model;
using Ake.Core.Numbering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>
/// Tel numaralandırma ayarları penceresi (ADR-0041): kod (önek), başlangıç, adım, basamak sayısı, şema ve örnek numaralar. Tamam yeni ayarı,
/// İptal ya da Esc <c>null</c> döner. Ayarı değiştirmek mevcut numaralara dokunmaz.
/// </summary>
public sealed class WireNumberingWindow : Window
{
    private readonly TextBox _prefix = new() { Width = 120, MaxLength = WireNumberSettings.MaxPrefixLength, PlaceholderText = "örn. W" };
    private readonly TextBox _start = new() { Width = 120 };
    private readonly TextBox _step = new() { Width = 120 };
    private readonly TextBox _width = new() { Width = 120 };
    private readonly ComboBox _scheme = new() { Width = 220, ItemsSource = new[] { "Potansiyel bazlı (aynı potansiyel aynı numara)", "Sıralı (her tel ayrı numara)" } };
    private readonly TextBlock _sample = new() { FontWeight = Avalonia.Media.FontWeight.SemiBold };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly Button _ok = new() { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };

    public WireNumberingWindow(WireNumberSettings current)
    {
        ArgumentNullException.ThrowIfNull(current);

        Title = "Tel numaralandırma ayarları";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _prefix.Text = current.Prefix;
        _start.Text = current.Start.ToString(CultureInfo.InvariantCulture);
        _step.Text = current.Step.ToString(CultureInfo.InvariantCulture);
        _width.Text = current.Width.ToString(CultureInfo.InvariantCulture);
        _scheme.SelectedIndex = current.Scheme == WireNumberingScheme.PerPotential ? 0 : 1;
        foreach (var box in new[] { _prefix, _start, _step, _width })
        {
            box.TextChanged += (_, _) => Refresh();
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), RowSpacing = 8, Margin = new Thickness(18, 16, 18, 8) };
        var rows = new (string Label, Control Field)[]
        {
            ("Kod (önek)", _prefix),
            ("Başlangıç numarası", _start),
            ("Adım", _step),
            ("Basamak sayısı (0: doldurma yok)", _width),
            ("Şema", _scheme),
        };
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

        var note = new TextBlock
        {
            Text = "Bu ayar yalnızca yeni numaralara uygulanır. Mevcut tel numaraları değişmez; \"Yeni telleri numarala\" kaldığı yerden devam eder, "
                + "\"Baştan numarala\" hepsini yeniden verir.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Opacity = 0.7,
            FontSize = 12,
            Margin = new Thickness(18, 4, 18, 4),
        };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        cancel.Click += (_, _) => Close(null);
        _ok.Click += (_, _) => Close(Read());
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 8) };
        buttons.Children.Add(_ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel();
        panel.Children.Add(grid);
        panel.Children.Add(new StackPanel { Margin = new Thickness(18, 4), Children = { new TextBlock { Text = "Örnek numaralar", Opacity = 0.6, FontSize = 12 }, _sample } });
        panel.Children.Add(_message);
        panel.Children.Add(note);
        panel.Children.Add(buttons);
        Content = panel;
        Refresh();
    }

    // Alanlardan ayar kurar; geçersiz alan ya da ayar bulunursa null.
    private WireNumberSettings? TryRead()
    {
        var parsed = int.TryParse(_start.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var start)
            & int.TryParse(_step.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var step)
            & int.TryParse(_width.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var width);
        if (!parsed)
        {
            return null;
        }

        return new WireNumberSettings(_prefix.Text?.Trim() ?? string.Empty, start, step, width, _scheme.SelectedIndex == 1 ? WireNumberingScheme.Sequential : WireNumberingScheme.PerPotential);
    }

    private WireNumberSettings? Read() => TryRead();

    private void Refresh()
    {
        var settings = TryRead();
        var error = settings is null ? "Başlangıç, adım ve basamak sayısı sıfır ya da pozitif tam sayı olmalı." : settings.Check()?.Message;
        _message.Text = error ?? string.Empty;
        _ok.IsEnabled = error is null;
        _sample.Text = settings is not null && error is null
            ? string.Join("   ", Enumerable.Range(0, 4).Select(i => settings.Format(settings.Start + (i * settings.Step)))) + "   …"
            : "—";
    }
}
