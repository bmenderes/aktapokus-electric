using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>Kaydedilmemiş değişiklik sorusunun cevabı.</summary>
public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// Proje kapanmadan (çıkış, yeni proje, başka proje açma) önce kaydedilmemiş değişiklikleri sorar.
/// Tek satır soru ve üç düğme; Enter kaydeder, Esc vazgeçer.
/// </summary>
public sealed class UnsavedChangesDialog : Window
{
    public UnsavedChangesDialog(string projectName)
    {
        ArgumentNullException.ThrowIfNull(projectName);

        Title = "aktapokus-electric";
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        Button Choice(string label, UnsavedChoice choice)
        {
            var button = new Button { Content = label, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(14, 5) };
            button.Click += (_, _) => Close(choice);
            return button;
        }

        var save = Choice("Kaydet", UnsavedChoice.Save);
        save.IsDefault = true;
        save.Classes.Add("accent");
        var discard = Choice("Kaydetme", UnsavedChoice.Discard);
        var cancel = Choice("Vazgeç", UnsavedChoice.Cancel);
        cancel.IsCancel = true;

        Content = new StackPanel
        {
            Margin = new Thickness(20, 18),
            Spacing = 18,
            Children =
            {
                new TextBlock { Text = $"'{projectName}' kaydedilsin mi?", MaxWidth = 460, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { save, discard, cancel },
                },
            },
        };

        Opened += (_, _) => save.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(UnsavedChoice.Cancel);
            }
        };
    }
}
