using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>
/// Sayfa özellikleri penceresi (Faz 6d-2): tam sayfa adı (=Fonksiyon+Yer/Numara), sayfa numarası, açıklaması,
/// yapı düğümleri, şablon (çerçeve) ve ölçek/ızgara özeti. Tamam <see cref="PageEdits"/>, İptal/Esc <c>null</c> döner.
/// </summary>
public sealed class PagePropertiesWindow : Window
{
    private readonly PageEditorData _data;
    private readonly TextBox _fullName = new() { IsReadOnly = true };
    private readonly TextBox _number = new();
    private readonly TextBox _title = new();
    private readonly ComboBox _functionNode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _locationNode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _template = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _scaleAndGrid = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.85, FontSize = 12 };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };

    public PagePropertiesWindow(PageEditorData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;

        Title = $"Sayfa özellikleri: {data.Number} {data.Title}";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _number.Text = data.Number;
        _title.Text = data.Title;
        _functionNode.ItemsSource = data.FunctionNodes;
        _functionNode.SelectedIndex = EditorSession.IndexOfNode(data.FunctionNodes, data.FunctionNode);
        _locationNode.ItemsSource = data.LocationNodes;
        _locationNode.SelectedIndex = EditorSession.IndexOfNode(data.LocationNodes, data.LocationNode);
        _template.ItemsSource = data.Templates.Select(template => template.Label).ToList();
        _template.SelectedIndex = IndexOfTemplate(data.Templates, data.TemplateId);

        _number.TextChanged += (_, _) => UpdatePreviews();
        _title.TextChanged += (_, _) => _message.Text = string.Empty;
        _functionNode.SelectionChanged += (_, _) => UpdatePreviews();
        _locationNode.SelectionChanged += (_, _) => UpdatePreviews();
        _template.SelectionChanged += (_, _) => UpdatePreviews();
        UpdatePreviews();

        var kindLabel = new TextBlock { Text = data.KindLabel, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.85 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 8 };
        var rows = new (string Label, Control Field)[]
        {
            ("Tam sayfa adı", _fullName),
            ("Sayfa türü", kindLabel),
            ("Sayfa numarası", _number),
            ("Sayfa açıklaması", _title),
            ("Fonksiyon (=)", _functionNode),
            ("Yer (+)", _locationNode),
            ("Sayfa şablonu", _template),
            ("Ölçek / ızgara", _scaleAndGrid),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }

        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Submit();
        cancel.Click += (_, _) => Close(null);

        Content = new StackPanel
        {
            Margin = new Thickness(18, 16),
            Spacing = 10,
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

        Opened += (_, _) => _title.Focus();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private void UpdatePreviews()
    {
        var fn = (_functionNode.SelectedItem as NodeChoice)?.Id is not null ? (_functionNode.SelectedItem as NodeChoice)!.Label : null;
        var loc = (_locationNode.SelectedItem as NodeChoice)?.Id is not null ? (_locationNode.SelectedItem as NodeChoice)!.Label : null;
        _fullName.Text = EditorSession.FullPageName(fn, loc, _number.Text);

        var selectedTemplate = _data.Templates.Count == 0
            ? null
            : _data.Templates[Math.Max(0, _template.SelectedIndex)].Template;
        _scaleAndGrid.Text = EditorSession.TemplateSummary(selectedTemplate);
        _message.Text = string.Empty;
    }

    private void Submit()
    {
        var number = _number.Text?.Trim() ?? string.Empty;
        if (number.Length == 0 || number.Any(char.IsWhiteSpace) || number.Length > 16)
        {
            _message.Text = "Sayfa numarası boş olamaz, boşluk içeremez ve en çok 16 karakter olmalıdır.";
            return;
        }

        var title = _title.Text?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            _message.Text = "Sayfa başlığı boş olamaz.";
            return;
        }

        var fnId = (_functionNode.SelectedItem as NodeChoice)?.Id;
        var locId = (_locationNode.SelectedItem as NodeChoice)?.Id;
        var templateId = _data.Templates.Count == 0 ? null : _data.Templates[Math.Max(0, _template.SelectedIndex)].Id;
        Close(new PageEdits(number, title, fnId, locId, templateId));
    }

    private static int IndexOfTemplate(IReadOnlyList<(SheetTemplateId? Id, string Label, SheetTemplate? Template)> templates, SheetTemplateId? id)
    {
        for (var i = 0; i < templates.Count; i++)
        {
            if (templates[i].Id == id)
            {
                return i;
            }
        }

        return 0;
    }
}
