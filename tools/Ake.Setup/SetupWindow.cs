using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

namespace Ake.Setup;

public sealed class SetupWindow : Window
{
    private readonly TextBox _dirBox = new() { Text = InstallEngine.DefaultInstallDirectory };
    private readonly CheckBox _desktopCheck = new() { Content = "Masaüstüne kısayol ekle", IsChecked = true };
    private readonly CheckBox _startMenuCheck = new() { Content = "Başlat Menüsüne kısayol ekle ve 'ake' komutunu PATH'e yaz", IsChecked = true };
    private readonly CheckBox _ollamaCheck = new() { Content = "Ollama yerel yapay zeka motorunu da kur (isteğe bağlı)" };
    private readonly TextBlock _ollamaStatusText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };

    private readonly ProgressBar _progressBar = new() { Minimum = 0, Maximum = 100, Height = 18 };
    private readonly TextBlock _progressLabel = new() { Text = "Hazırlanıyor…", TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox _launchAfterCheck = new() { Content = "aktapokus-electric uygulamasını şimdi başlat", IsChecked = true };
    private readonly TextBlock _doneTitle = new() { Text = "Kurulum tamamlandı", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#1B6B3A") };
    private readonly TextBlock _summaryText = new() { TextWrapping = TextWrapping.Wrap };

    private readonly Control _optionsPage;
    private readonly Control _progressPage;
    private readonly Control _donePage;
    private readonly Button _actionButton = new() { Content = "Hızlı Kur", Classes = { "accent" }, MinWidth = 112, IsDefault = true };
    private readonly Button _cancelButton = new() { Content = "İptal", MinWidth = 88, IsCancel = true };

    private string? _installedEditorExe;
    private bool _finished;
    private bool _retryMode;
    private CancellationTokenSource? _cts;

    public SetupWindow()
    {
        Title = "aktapokus-electric Kurulumu";
        Width = 560;
        Height = 430;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Brush.Parse("#F2EDE1");

        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://aktapokus-electric-setup/Assets/logo.ico"));
            Icon = new WindowIcon(iconStream);
        }
        catch
        {
            // Simge yüklenemezse varsayılan pencere simgesiyle devam et.
        }

        var ollamaStatus = OllamaDetector.Detect();
        if (ollamaStatus.IsInstalled)
        {
            _ollamaCheck.IsChecked = false;
            _ollamaCheck.IsEnabled = false;
            _ollamaStatusText.Text = $"✓ Ollama bu bilgisayarda zaten kurulu ({ollamaStatus.ExecutablePath}) — otomatik atlanacak.";
            _ollamaStatusText.Foreground = Brush.Parse("#1B6B3A");
        }
        else
        {
            _ollamaCheck.IsChecked = true;
            _ollamaCheck.IsEnabled = true;
            _ollamaStatusText.Text = InstallEngine.HasEmbeddedOllama
                ? "Bilgisayarda Ollama bulunamadı. Paket içindeki imzalı OllamaSetup.exe çalıştırılacak."
                : "Bilgisayarda Ollama bulunamadı. Seçiliyse resmi ollama.com üzerinden indirilip imzası doğrulanarak kurulacak.";
            _ollamaStatusText.Foreground = Brush.Parse("#5E5645");
        }

        _optionsPage = BuildOptionsPage();
        _progressPage = BuildProgressPage();
        _donePage = BuildDonePage();
        _progressPage.IsVisible = false;
        _donePage.IsVisible = false;

        _actionButton.Click += async (_, _) => await OnActionClickedAsync();
        _cancelButton.Click += (_, _) =>
        {
            _cts?.Cancel();
            Close();
        };

        var header = BuildHeader();
        var bodyCard = new Border
        {
            Background = Brush.Parse("#FBF8F1"),
            BorderBrush = Brush.Parse("#D6CDB8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(16),
            Child = new Grid
            {
                Children = { _optionsPage, _progressPage, _donePage },
            },
        };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
            Children = { _cancelButton, _actionButton },
        };

        var root = new DockPanel { Margin = new Thickness(18), LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.AddRange([header, footer, bodyCard]);
        Content = root;
    }

    private Control BuildHeader()
    {
        var logo = new Image { Width = 44, Height = 44 };
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://aktapokus-electric-setup/Assets/logo-256.png"));
            logo.Source = new Bitmap(stream);
        }
        catch
        {
            // Logo yoksa boş bırak.
        }

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            Margin = new Thickness(0, 0, 0, 14),
            Children =
            {
                logo,
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "aktapokus-electric Kurulumu", FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#2B2619") },
                        new TextBlock { Text = "Sürüm 0.1.0 · Açık kaynaklı elektrik projelendirme (E-CAD)", FontSize = 12, Foreground = Brush.Parse("#6B6455") },
                    },
                },
            },
        };
    }

    private Control BuildOptionsPage()
    {
        var browse = new Button { Content = "Gözat…" };
        browse.Click += async (_, _) =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null)
            {
                return;
            }

            var picked = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Kurulum klasörünü seç",
                AllowMultiple = false,
            });
            if (picked.Count > 0 && StorageProviderExtensions.TryGetLocalPath(picked[0]) is { Length: > 0 } local)
            {
                _dirBox.Text = Path.Combine(local, "aktapokus-electric");
            }
        };

        var dirRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(_dirBox, 0);
        Grid.SetColumn(browse, 1);
        dirRow.Children.AddRange([_dirBox, browse]);

        var advanced = new Expander
        {
            Header = "Gelişmiş ayarlar (kurulum klasörü ve PATH)",
            IsExpanded = false,
            Content = new StackPanel
            {
                Spacing = 8,
                Margin = new Thickness(0, 6, 0, 0),
                Children =
                {
                    new TextBlock { Text = "Kurulum klasörü", FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#2B2619") },
                    dirRow,
                    _startMenuCheck,
                },
            },
        };

        var ollamaBox = new Border
        {
            Background = Brush.Parse("#F2EDE1"),
            BorderBrush = Brush.Parse("#D6CDB8"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(10),
            Child = new StackPanel
            {
                Spacing = 4,
                Children = { _ollamaCheck, _ollamaStatusText },
            },
        };

        return new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = "Tek tıkla kurulum · Yönetici izni gerekmez · Masaüstüne kısayol eklenir",
                    FontWeight = FontWeight.SemiBold,
                    Foreground = Brush.Parse("#2B2619"),
                    TextWrapping = TextWrapping.Wrap,
                },
                _desktopCheck,
                ollamaBox,
                advanced,
            },
        };
    }

    private Control BuildProgressPage() =>
        new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "aktapokus-electric kuruluyor…", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Brush.Parse("#2B2619") },
                _progressBar,
                _progressLabel,
            },
        };

    private Control BuildDonePage() =>
        new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
            Children =
            {
                _doneTitle,
                _summaryText,
                _launchAfterCheck,
            },
        };

    private async Task OnActionClickedAsync()
    {
        if (_retryMode)
        {
            _retryMode = false;
            _donePage.IsVisible = false;
            _optionsPage.IsVisible = true;
            _actionButton.Content = "Hızlı Kur";
            _cancelButton.Content = "İptal";
            _cancelButton.IsVisible = true;
            return;
        }

        if (_finished)
        {
            if (_launchAfterCheck.IsChecked == true && !string.IsNullOrWhiteSpace(_installedEditorExe) && File.Exists(_installedEditorExe))
            {
                Process.Start(new ProcessStartInfo(_installedEditorExe)
                {
                    WorkingDirectory = Path.GetDirectoryName(_installedEditorExe)!,
                    UseShellExecute = false,
                });
            }

            Close();
            return;
        }

        if (InstallEngine.ValidateInstallDirectory(_dirBox.Text) is { } validationError)
        {
            _optionsPage.IsVisible = false;
            _donePage.IsVisible = true;
            _doneTitle.Text = "Geçersiz kurulum klasörü";
            _doneTitle.Foreground = Brush.Parse("#A12626");
            _summaryText.Text = validationError;
            _launchAfterCheck.IsVisible = false;
            _retryMode = true;
            _actionButton.Content = "Tekrar Dene";
            _cancelButton.Content = "Kapat";
            return;
        }

        _optionsPage.IsVisible = false;
        _progressPage.IsVisible = true;
        _actionButton.IsEnabled = false;
        _cts = new CancellationTokenSource();

        var options = new InstallOptions(
            (_dirBox.Text ?? string.Empty).Trim(),
            _desktopCheck.IsChecked == true,
            _startMenuCheck.IsChecked == true,
            _startMenuCheck.IsChecked == true,
            _ollamaCheck.IsChecked == true);

        var progress = new Progress<(int Percent, string Message)>(update =>
        {
            _progressBar.Value = update.Percent;
            _progressLabel.Text = update.Message;
        });

        var result = await Task.Run(() => InstallEngine.InstallAsync(options, progress, _cts.Token));

        _progressPage.IsVisible = false;
        _donePage.IsVisible = true;
        _actionButton.IsEnabled = true;

        if (result.Success)
        {
            _finished = true;
            _retryMode = false;
            _cancelButton.IsVisible = false;
            _actionButton.Content = "Bitir";
            _installedEditorExe = result.EditorExePath;
            _doneTitle.Text = "Kurulum tamamlandı";
            _doneTitle.Foreground = Brush.Parse("#1B6B3A");
            _summaryText.Text = $"Konum: {Path.GetDirectoryName(result.EditorExePath)}\nOllama durumu: {result.OllamaOutcome}";
            _launchAfterCheck.IsVisible = true;
        }
        else
        {
            _retryMode = true;
            _cancelButton.Content = "Kapat";
            _cancelButton.IsVisible = true;
            _actionButton.Content = "Tekrar Dene";
            _doneTitle.Text = "Kurulum tamamlanamadı";
            _doneTitle.Foreground = Brush.Parse("#A12626");
            _summaryText.Text = result.ErrorMessage ?? "Bilinmeyen kurulum hatası.";
            _launchAfterCheck.IsVisible = false;
        }
    }
}
