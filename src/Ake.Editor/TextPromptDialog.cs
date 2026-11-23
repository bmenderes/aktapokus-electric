using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>Tek satır metin sorar (serbest yazı düzenleme). Tamam yeni metni, İptal ya da Esc <c>null</c> döner.</summary>
public sealed class TextPromptDialog : Window
{
    private readonly TextBox _box;

    public TextPromptDialog(string title, string text)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(text);

        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _box = new TextBox { Text = text };
        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Close(_box.Text ?? string.Empty);
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(18),
            Spacing = 14,
            Children =
            {
                _box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { ok, cancel },
                },
            },
        };

        Opened += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }
}
