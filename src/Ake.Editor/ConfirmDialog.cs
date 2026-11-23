using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ake.Editor;

/// <summary>Kullanıcıya tek soruluk onay penceresi gösterir (Evet = <c>true</c>, Hayır/Esc = <c>false</c>).</summary>
public sealed class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmLabel = "Evet", string cancelLabel = "Hayır")
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        Title = UiStrings.T(title);
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var yes = new Button
        {
            Content = UiStrings.T(confirmLabel),
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(14, 5),
            IsDefault = true,
        };
        yes.Classes.Add("accent");
        yes.Click += (_, _) => Close(true);

        var no = new Button
        {
            Content = UiStrings.T(cancelLabel),
            MinWidth = 96,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(14, 5),
            IsCancel = true,
        };
        no.Click += (_, _) => Close(false);

        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 18,
            Children =
            {
                new TextBlock
                {
                    Text = message,
                    MaxWidth = 460,
                    TextWrapping = TextWrapping.Wrap,
                },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { yes, no },
                },
            },
        };

        Opened += (_, _) => yes.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(false);
            }
        };
    }
}
