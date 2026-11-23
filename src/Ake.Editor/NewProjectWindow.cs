using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// Proje oluşturma penceresi (ADR-0047): proje adı, saklama klasörü, şablon, oluşturan ve tarih. Tamam seçimi, İptal ya da Esc <c>null</c> döner.
/// Proje <c>klasör\ad.json</c> olarak hemen kaydedilir; klasör boş bırakılırsa proje ilk kaydetmeye kadar dosyasızdır.
/// </summary>
public sealed class NewProjectWindow : Window
{
    private static readonly (ProjectPreset Preset, string Label)[] PresetItems =
    [
        (ProjectPreset.Standard, "Standart şema projesi (kapak + şema)"),
        (ProjectPreset.IndustrialMachine, "Endüstriyel makine (Güç, Kumanda, Tek hat, Haberleşme)"),
        (ProjectPreset.CommunicationTopology, "Haberleşme / ağ topolojisi (PLC & Ethernet)"),
        (ProjectPreset.Pneumatic, "Pnömatik / valf adası"),
    ];

    private readonly TextBox _name = new() { Text = "Yeni proje" };
    private readonly TextBox _folder = new();
    private readonly ComboBox _template = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _preset = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        ItemsSource = PresetItems.Select(p => p.Label).ToList(),
        SelectedIndex = 0,
    };
    private readonly TextBox _creator = new() { Text = Environment.UserName };
    private readonly TextBox _date = new() { Text = DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public NewProjectWindow(string defaultFolder)
    {
        Title = "Proje oluştur";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _folder.Text = defaultFolder;
        _template.ItemsSource = Ake.Core.Model.StandardSheetTemplates.All.Select(template => template.Name).ToList();
        _template.SelectedIndex = Ake.Core.Model.StandardSheetTemplates.All.ToList().IndexOf(Ake.Core.Model.StandardSheetTemplates.A3Landscape);

        var browse = new Button { Content = "…", Width = 36, HorizontalContentAlignment = HorizontalAlignment.Center };
        browse.Click += async (_, _) =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Projenin kaydedileceği klasör", AllowMultiple = false });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
            {
                _folder.Text = path;
            }
        };
        var folderRow = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(browse, Avalonia.Controls.Dock.Right);
        folderRow.Children.Add(browse);
        folderRow.Children.Add(_folder);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("130,*"), RowSpacing = 8 };
        var rows = new (string Label, Control Field)[]
        {
            ("Proje adı", _name),
            ("Saklama klasörü", folderRow),
            ("Şablon", _template),
            ("Proje türü", _preset),
            ("Oluşturan", _creator),
            ("Tarih", _date),
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

        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) =>
        {
            var problem = Check();
            if (problem is not null)
            {
                _message.Text = problem;
                return;
            }

            Close(new NewProjectInfo(
                _name.Text!.Trim(), _creator.Text?.Trim() ?? string.Empty, _date.Text?.Trim() ?? string.Empty,
                string.IsNullOrWhiteSpace(_folder.Text) ? null : _folder.Text.Trim(),
                Ake.Core.Model.StandardSheetTemplates.All[Math.Max(0, _template.SelectedIndex)].Id,
                PresetItems[Math.Clamp(_preset.SelectedIndex, 0, PresetItems.Length - 1)].Preset));
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
            _name.Focus();
            _name.SelectAll();
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    // Boş ad, geçersiz dosya adı karakteri ve var olan dosya burada yakalanır; proje oluşturulmadan önce kullanıcıya söylenir.
    private string? Check()
    {
        var name = _name.Text?.Trim() ?? string.Empty;
        if (name.Length == 0)
        {
            return "Projenin adı boş olamaz.";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Proje adı dosya adı olarak kullanılamayan karakter içeriyor (\\ / : * ? \" < > |).";
        }

        if (!string.IsNullOrWhiteSpace(_folder.Text) && File.Exists(Path.Combine(_folder.Text.Trim(), name + ".json")))
        {
            return "Bu klasörde aynı adla bir proje dosyası zaten var.";
        }

        return null;
    }
}
