using Ake.Core.Model;
using Ake.Core.Numbering;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>Yeni sayfa penceresinin seçimi: sayfa türü, başlığı, numarası, yapısı (= / +) ve şablonu.</summary>
public sealed record NewPageChoice(
    PageKind Kind,
    string Title,
    bool AfterActive,
    SheetTemplateId? TemplateId = null,
    StructureNodeId? FunctionNodeId = null,
    StructureNodeId? LocationNodeId = null,
    string? Number = null,
    bool ExplicitStructure = false);

/// <summary>
/// Yeni sayfa penceresi (ADR-0046, Faz 6d-2): tam sayfa adı (=Fonksiyon+Yer/Numara), sayfa türü, açıklama,
/// numara, yapı düğümleri (= ve +), konum, çerçeve (şablon) ve ölçek/ızgara özeti. Tamam seçimi, İptal ya da Esc <c>null</c> döner.
/// </summary>
public sealed class NewPageWindow : Window
{
    private sealed record PageType(PageKind Kind, string Label, string Title, string Description)
    {
        public override string ToString() => Label;
    }

    private static readonly PageType[] Types =
    [
        new(PageKind.Schematic, "Şema sayfası", "Sayfa", "Elle çizilen şema: semboller, teller, serbest grafik."),
        new(PageKind.Cover, "Kapak sayfası", "Kapak", "Proje adı, açıklaması ve proje özelliklerinden (müşteri, sipariş no, çizim no, çizen, tarih, revizyon) üretilir."),
        new(PageKind.Contents, "İçindekiler", "İçindekiler", "Projenin sayfa listesi; PDF'te her satır ilgili sayfaya bağlantıdır."),
        new(PageKind.PartsList, "Malzeme listesi", "Malzeme listesi", "Ürün bilgisi olan cihazlar ve kablolar, adetleriyle."),
        new(PageKind.TerminalPlan, "Klemens planı", "Klemens planı", "Her klemensin iç ve dış hedefi, telleri, köprüleri."),
        new(PageKind.CableList, "Kablo listesi", "Kablo listesi", "Kablo damarları, kesit, renk ve iki ucu."),
        new(PageKind.PotentialOverview, "Potansiyel özeti", "Potansiyel özeti", "Her potansiyelin adı, tel numaraları ve uçları."),
        new(PageKind.PinConnections, "Pin-bağlantı şeması", "Pin-bağlantı şeması", "Her cihaz ucunun hedefi ve teli ya da kablo damarı."),
        new(PageKind.PlcDiagram, "PLC şeması", "PLC şeması", "PLC modüllerini kutu olarak çizer: kanal, adres, sembolik ad, açıklama, modül ucu, klemens ve saha ucu."),
        new(PageKind.RevisionOverview, "Revizyon özeti", "Revizyon özeti", "Revizyon geçmişi: işaret, tarih, hazırlayan, açıklama (Revizyon ekle… ile girilir)."),
        new(PageKind.FrameDocumentation, "Çerçeve dokümantasyonu", "Çerçeve dokümantasyonu", "Projedeki sayfa şablonları: kâğıt, ızgara, antet ve kullanan sayfa sayısı."),
    ];

    private readonly EditorSession? _session;
    private readonly int? _activePageIndex;
    private readonly IReadOnlyList<(SheetTemplateId? Id, string Label)> _templates;
    private readonly TextBox _fullName = new() { IsReadOnly = true };
    private readonly ComboBox _type = new() { ItemsSource = Types, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _number = new();
    private readonly TextBox _title = new();
    private readonly ComboBox _functionNode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _locationNode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _template = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _position = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _scaleAndGrid = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.85, FontSize = 12 };
    private readonly TextBlock _description = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75, FontSize = 12 };
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
    private string _suggestedTitle = Types[0].Title;
    private string _suggestedNumber;

    /// <param name="hasActivePage">Etkin sayfa var mı (varsa "etkin sayfadan sonra" konumu seçilebilir).</param>
    /// <param name="templates">Seçilebilir şablonlar: ilki <c>null</c> kimlikli "önceki sayfayla aynı" seçeneği olmalıdır.</param>
    public NewPageWindow(
        bool hasActivePage,
        IReadOnlyList<(SheetTemplateId? Id, string Label)> templates,
        EditorSession? session = null,
        int? activePageIndex = null)
    {
        ArgumentNullException.ThrowIfNull(templates);
        _templates = templates;
        _session = session;
        _activePageIndex = activePageIndex;
        _template.ItemsSource = templates.Select(template => template.Label).ToList();
        _template.SelectedIndex = 0;
        Title = "Yeni sayfa";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        var project = session?.Project;
        var fnChoices = project is null ? [new NodeChoice(null, "—")] : EditorSession.StructureChoices(project, Aspect.Function);
        var locChoices = project is null ? [new NodeChoice(null, "—")] : EditorSession.StructureChoices(project, Aspect.Location);
        _functionNode.ItemsSource = fnChoices;
        _locationNode.ItemsSource = locChoices;

        var basisPage = project is { Pages.Count: > 0 }
            ? (activePageIndex is { } idx && idx >= 0 && idx < project.Pages.Count ? project.Pages[idx] : project.Pages[^1])
            : null;
        _functionNode.SelectedIndex = EditorSession.IndexOfNode(fnChoices, basisPage?.FunctionNodeId);
        _locationNode.SelectedIndex = EditorSession.IndexOfNode(locChoices, basisPage?.LocationNodeId);

        _position.ItemsSource = hasActivePage
            ? new[] { "Listenin sonuna", "Etkin sayfadan sonra (alt numara, örn. 3.1)" }
            : new[] { "Listenin sonuna" };
        _position.SelectedIndex = 0;

        _suggestedNumber = NextSuggestedNumber();
        _number.Text = _suggestedNumber;
        _title.Text = _suggestedTitle;
        _description.Text = Types[0].Description;

        _type.SelectionChanged += (_, _) =>
        {
            if (_type.SelectedItem is not PageType type)
            {
                return;
            }

            // Başlığı kullanıcı elle değiştirmediyse yeni türün önerisine geçer.
            if (_title.Text == _suggestedTitle)
            {
                _title.Text = type.Title;
            }

            _suggestedTitle = type.Title;
            _description.Text = type.Description;
        };

        _position.SelectionChanged += (_, _) =>
        {
            var next = NextSuggestedNumber();
            if (string.IsNullOrWhiteSpace(_number.Text) || _number.Text == _suggestedNumber)
            {
                _number.Text = next;
            }

            _suggestedNumber = next;
            UpdatePreviews();
        };

        _number.TextChanged += (_, _) => UpdatePreviews();
        _functionNode.SelectionChanged += (_, _) => UpdatePreviews();
        _locationNode.SelectionChanged += (_, _) => UpdatePreviews();
        _template.SelectionChanged += (_, _) => UpdatePreviews();
        UpdatePreviews();

        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        ok.Click += (_, _) => Submit();
        cancel.Click += (_, _) => Close(null);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 8 };
        var rows = new (string Label, Control Field)[]
        {
            ("Tam sayfa adı", _fullName),
            ("Sayfa türü", _type),
            ("Sayfa numarası", _number),
            ("Sayfa açıklaması", _title),
            ("Fonksiyon (=)", _functionNode),
            ("Yer (+)", _locationNode),
            ("Konum", _position),
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

        Content = new StackPanel
        {
            Margin = new Thickness(18, 16),
            Spacing = 10,
            Children =
            {
                grid,
                _description,
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

    private string NextSuggestedNumber()
    {
        if (_session?.Project is not { } project)
        {
            return "1";
        }

        return _position.SelectedIndex == 1 && _activePageIndex is { } active && active >= 0 && active < project.Pages.Count
            ? PageNumbering.NextSubNumber(project, active)
            : PageNumbering.NextNumber(project);
    }

    private void UpdatePreviews()
    {
        var fn = (_functionNode.SelectedItem as NodeChoice)?.Id is not null ? (_functionNode.SelectedItem as NodeChoice)!.Label : null;
        var loc = (_locationNode.SelectedItem as NodeChoice)?.Id is not null ? (_locationNode.SelectedItem as NodeChoice)!.Label : null;
        _fullName.Text = EditorSession.FullPageName(fn, loc, _number.Text);

        var chosenId = _templates[Math.Max(0, _template.SelectedIndex)].Id;
        SheetTemplate? resolved = null;
        if (_session?.Project is { } project)
        {
            if (chosenId is { } tid)
            {
                resolved = project.SheetTemplates.FirstOrDefault(t => t.Id == tid) ?? StandardSheetTemplates.Find(tid);
            }
            else if (project.Pages.Count > 0)
            {
                var basis = _position.SelectedIndex == 1 && _activePageIndex is { } idx && idx >= 0 && idx < project.Pages.Count
                    ? project.Pages[idx]
                    : project.Pages[^1];
                resolved = project.TemplateFor(basis);
            }
            else
            {
                resolved = project.SheetTemplates.FirstOrDefault();
            }
        }
        else if (chosenId is { } tid)
        {
            resolved = StandardSheetTemplates.Find(tid);
        }

        _scaleAndGrid.Text = EditorSession.TemplateSummary(resolved ?? StandardSheetTemplates.A3Landscape);
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

        if (_session?.Project is { } project && project.Pages.Any(page => page.Number == number))
        {
            _message.Text = $"Sayfa numarası zaten kullanılıyor: {number}";
            return;
        }

        Close(Choice());
    }

    private NewPageChoice Choice()
    {
        var type = _type.SelectedItem as PageType ?? Types[0];
        var title = string.IsNullOrWhiteSpace(_title.Text) ? type.Title : _title.Text.Trim();
        var fnId = (_functionNode.SelectedItem as NodeChoice)?.Id;
        var locId = (_locationNode.SelectedItem as NodeChoice)?.Id;
        var number = string.IsNullOrWhiteSpace(_number.Text) ? null : _number.Text.Trim();
        return new NewPageChoice(
            type.Kind,
            title,
            _position.SelectedIndex == 1,
            _templates[Math.Max(0, _template.SelectedIndex)].Id,
            fnId,
            locId,
            number,
            ExplicitStructure: _session is not null);
    }
}
