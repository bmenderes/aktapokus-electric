using System.Globalization;
using Ake.Core;
using Ake.Render;
using Ake.Reports;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// Masaüstü dışa aktarma ve rapor merkezi (Faz 6c-3, ADR-0056):
/// proje PDF, DXF/DWG sayfa veya tüm proje çıktısı, kesim çizgili kablo/tel etiketi PDF'i
/// ve önizlemeli Excel (.xlsx) / CSV rapor dışa aktarımı sağlar.
/// </summary>
public sealed class ExportWindow : Window
{
    private sealed record ReportChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ReportChoice[] Choices =
    [
        new("bom", "Malzeme listesi (bom)"),
        new("cables", "Kablo listesi (cables)"),
        new("wires", "Bağlantı / tel listesi (wires)"),
        new("terminals", "Klemens planı (terminals)"),
        new("io", "PLC I/O listesi (io)"),
        new("labels", "Kablo ve tel etiketleri (labels)"),
        new("plc-xref", "PLC adresi ↔ tel çapraz referansı (plc-xref)"),
        new("motors", "Motor ve redüktör listesi (motors)"),
        new("islands", "Ada listesi (islands)"),
        new("controller-io-blocks", "Kontrolör I/O blok listesi (controller-io-blocks)"),
        new("driver-io-blocks", "Sürücü I/O blok listesi (driver-io-blocks)"),
    ];

    private readonly EditorSession _session;
    private readonly int _activePageIndex;
    private readonly ComboBox _reportBox = new() { ItemsSource = Choices, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ListBox _preview = new() { Height = 190 };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    public ExportWindow(EditorSession session, int activePageIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _activePageIndex = activePageIndex;
        var project = session.Project ?? throw new ArgumentException("Proje açık değil.", nameof(session));

        Title = $"Dışa aktar ve raporlar — {project.Name}";
        Width = 820;
        Height = 590;
        MinWidth = 700;
        MinHeight = 500;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        var pdfButton = ActionButton("Proje PDF'i kaydet…", 160);
        var dxfPageButton = ActionButton("Etkin sayfa DXF…", 145);
        var dwgPageButton = ActionButton("Etkin sayfa DWG…", 145);
        var dxfAllButton = ActionButton("Tüm sayfalar DXF…", 150);

        pdfButton.Click += (_, _) => _ = SaveFileAsync("Proje PDF'i kaydet", $"{project.Name}.pdf", "pdf", "PDF Belgesi", path => _session.ExportPdf(path, DateOnly.FromDateTime(DateTime.Now)));
        dxfPageButton.IsEnabled = activePageIndex >= 0 && activePageIndex < project.Pages.Count;
        dwgPageButton.IsEnabled = dxfPageButton.IsEnabled;
        dxfPageButton.Click += (_, _) => _ = SaveFileAsync("Sayfa DXF kaydet", $"{project.Name}-sayfa-{project.Pages[_activePageIndex].Number}.dxf", "dxf", "DXF Çizimi", path => _session.ExportCadPage(_activePageIndex, path, CadFormat.Dxf, DateOnly.FromDateTime(DateTime.Now)));
        dwgPageButton.Click += (_, _) => _ = SaveFileAsync("Sayfa DWG kaydet", $"{project.Name}-sayfa-{project.Pages[_activePageIndex].Number}.dwg", "dwg", "DWG Çizimi", path => _session.ExportCadPage(_activePageIndex, path, CadFormat.Dwg, DateOnly.FromDateTime(DateTime.Now)));
        dxfAllButton.Click += (_, _) => _ = SaveFolderDxfAsync();

        var cadCard = Card(
            "Şema ve CAD çıktısı",
            $"{project.Pages.Count} sayfa PDF ya da AutoCAD 2013 DXF/DWG biçiminde dışa aktarılır.",
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { pdfButton, dxfPageButton, dwgPageButton, dxfAllButton } });

        var labelCount = ReportCatalog.Labels(project).Rows.Count;
        var labelPdfButton = ActionButton("Etiket PDF'i kaydet…", 165);
        labelPdfButton.Click += (_, _) => _ = SaveFileAsync("Etiket PDF'i kaydet", $"{project.Name}-etiketler.pdf", "pdf", "PDF Etiket Tabakası", path => _session.ExportLabelsPdf(path));
        var labelCard = Card(
            "Baskıya hazır kablo ve tel etiketi PDF'i",
            $"{labelCount} adet etiket hücresi (3 × 10 A4 düzeni, iki uçlu hedef bilgisi ve kesim kılavuz çizgili).",
            labelPdfButton);

        var xlsxButton = ActionButton("Tüm raporlar Excel (.xlsx)…", 190);
        var csvButton = ActionButton("Seçili rapor CSV (.csv)…", 175);
        xlsxButton.Click += (_, _) => _ = SaveFileAsync("Raporları Excel olarak kaydet", $"{project.Name}-raporlar.xlsx", "xlsx", "Excel Çalışma Kitabı", path => _session.ExportReportsXlsx(path));
        csvButton.Click += (_, _) =>
        {
            var choice = _reportBox.SelectedItem as ReportChoice ?? Choices[0];
            _ = SaveFileAsync("Raporu CSV olarak kaydet", $"{project.Name}-{choice.Key}.csv", "csv", "CSV Tablosu", path => _session.ExportReportCsv(choice.Key, path));
        };

        _reportBox.SelectionChanged += (_, _) => RefreshPreview();
        RefreshPreview();

        var reportTop = new Grid { ColumnDefinitions = new ColumnDefinitions("*,8,Auto,8,Auto") };
        Grid.SetColumn(_reportBox, 0);
        Grid.SetColumn(xlsxButton, 2);
        Grid.SetColumn(csvButton, 4);
        reportTop.Children.Add(_reportBox);
        reportTop.Children.Add(xlsxButton);
        reportTop.Children.Add(csvButton);

        var reportSection = new DockPanel { LastChildFill = true };
        var repTitle = new TextBlock { Text = "Mühendislik raporları (Excel / CSV) — Önizleme", FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(repTitle, Avalonia.Controls.Dock.Top);
        DockPanel.SetDock(reportTop, Avalonia.Controls.Dock.Top);
        reportTop.Margin = new Thickness(0, 0, 0, 8);
        reportSection.Children.Add(repTitle);
        reportSection.Children.Add(reportTop);
        reportSection.Children.Add(new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Child = _preview,
        });

        var closeButton = new Button { Content = "Kapat", IsDefault = true, IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        closeButton.Click += (_, _) => Close();
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(16, 6, 16, 12), Children = { closeButton } };

        var body = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 6),
            Spacing = 12,
            Children = { cadCard, labelCard, reportSection },
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_status, Avalonia.Controls.Dock.Bottom);
        _status.Margin = new Thickness(16, 2);
        root.Children.Add(footer);
        root.Children.Add(_status);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
    }

    private void RefreshPreview()
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var choice = _reportBox.SelectedItem as ReportChoice ?? Choices[0];
        var built = ReportCatalog.Build(choice.Key, project);
        if (built.IsFailure)
        {
            _preview.ItemsSource = new[] { built.Error!.Message };
            return;
        }

        var table = built.Value;
        var lines = new List<string>
        {
            string.Join("  |  ", table.Columns.Select(c => c.Header)),
            new string('─', 78),
        };
        foreach (var row in table.Rows.Take(50))
        {
            lines.Add(string.Join("  |  ", row.Select(cell => Convert.ToString(cell, CultureInfo.InvariantCulture) is { Length: > 0 } text ? text : "—")));
        }

        if (table.Rows.Count == 0)
        {
            lines.Add("(Kayıt yok)");
        }
        else if (table.Rows.Count > 50)
        {
            lines.Add($"… toplam {table.Rows.Count} satırın ilk 50'si gösteriliyor");
        }

        _preview.ItemsSource = lines;
    }

    private async Task SaveFileAsync(string title, string suggestedName, string ext, string filterName, Func<string, Result> writer)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            DefaultExtension = ext,
            FileTypeChoices = [new FilePickerFileType(filterName) { Patterns = [$"*.{ext}"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        var saved = writer(path);
        SetStatus(saved.IsSuccess ? $"Yazıldı: {path}" : saved.Error!.Message, error: saved.IsFailure);
    }

    private async Task SaveFolderDxfAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "DXF sayfaları için klasör seç",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } directory)
        {
            return;
        }

        var saved = _session.ExportAllCadPages(directory, CadFormat.Dxf, DateOnly.FromDateTime(DateTime.Now));
        SetStatus(
            saved.IsSuccess ? $"{saved.Value} sayfa DXF olarak yazıldı: {directory}" : saved.Error!.Message,
            error: saved.IsFailure);
    }

    private void SetStatus(string text, bool error)
    {
        _status.Foreground = Brush(error ? "#C0392B" : "#2E6F40");
        _status.Text = text;
    }

    private static Button ActionButton(string label, double width) =>
        new() { Content = label, MinWidth = width, HorizontalContentAlignment = HorizontalAlignment.Center };

    private static Border Card(string heading, string subtext, Control action) =>
        new()
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            Background = Brush("#FAF8F3"),
            Padding = new Thickness(12, 10),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold },
                    new TextBlock { Text = subtext, FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap },
                    action,
                },
            },
        };

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
