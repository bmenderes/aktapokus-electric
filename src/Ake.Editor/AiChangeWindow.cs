using Ake.Ai;
using Ake.Core;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Ake.Editor;

/// <summary>
/// Yapay zeka ile değişiklik penceresi (ADR-0044): Ollama, Claude (Anthropic), Antigravity (Gemini / vekil) ve
/// OpenAI uyumlu uç nokta seçilir; istem yazılır, öneri doğrulanıp listelenir ve onaylanırsa tek adımda uygulanır.
/// </summary>
public sealed class AiChangeWindow : Window
{
    private readonly Func<AiSettings, string, CancellationToken, Task<Result<PatchProposal>>> _propose;
    private readonly Func<AiSettings, Task<Result<IReadOnlyList<string>>>> _listModels;
    private readonly ComboBox _provider = new()
    {
        Width = 240,
        ItemsSource = new[]
        {
            UiStrings.T("Yerel Ollama"),
            UiStrings.T("Claude (Anthropic)"),
            UiStrings.T("Antigravity (Gemini / Proxy)"),
            UiStrings.T("OpenAI uyumlu uç nokta"),
        },
    };
    private readonly ComboBox _model = new() { Width = 290, IsEditable = true };
    private readonly TextBox _address = new() { Width = 290 };
    private readonly TextBox _apiKey = new()
    {
        Width = 290,
        PasswordChar = '•',
        PlaceholderText = UiStrings.Current == UiLanguage.English
            ? "Session-only (not saved; empty uses env var)"
            : "Oturum için (diske yazılmaz; boşsa ortam değişkeni)",
    };
    private readonly TextBlock _apiKeyLabel = new() { Text = UiStrings.T("API anahtarı"), VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _refresh = new() { Content = UiStrings.T("Modelleri yenile"), HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TextBlock _remote = new()
    {
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")),
        FontSize = 12,
        IsVisible = false,
    };
    private readonly TextBox _request = new()
    {
        AcceptsReturn = true,
        TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        MinHeight = 70,
        PlaceholderText = UiStrings.Current == UiLanguage.English
            ? "Describe the change, e.g. \"Add contactor coil K7 under =M1+P1 and connect A2 to -T1:-\""
            : "Yapılacak değişikliği yazın, örn. \"=M1+P1 altına K7 kontaktör bobini ekle ve A2'yi -T1:- noktasına bağla\"",
    };
    private readonly ProgressBar _busy = new() { IsIndeterminate = true, IsVisible = false };
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, Opacity = 0.75 };
    private readonly ListBox _changes = new() { MinHeight = 90, MaxHeight = 220 };
    private readonly Button _ask = new() { Content = UiStrings.T("Öneri iste"), IsDefault = false, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _apply = new() { Content = UiStrings.T("Uygula"), IsEnabled = false, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _close = new() { Content = UiStrings.T("Kapat"), IsCancel = true, MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    private CancellationTokenSource? _running;
    private PatchProposal? _proposal;

    public AiChangeWindow(
        AiSettings current,
        Func<AiSettings, Task<Result<IReadOnlyList<string>>>> listModels,
        Func<AiSettings, string, CancellationToken, Task<Result<PatchProposal>>> propose)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(listModels);
        ArgumentNullException.ThrowIfNull(propose);
        _propose = propose;
        _listModels = listModels;

        Title = UiStrings.T("Yapay zeka ile değiştir");
        Width = 650;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _provider.SelectedIndex = ProviderIndex(current.Provider);
        _address.Text = current.Address;
        PopulateModelChoices(current.Provider, current.Model);
        UpdateKeyRowVisibility();

        _provider.SelectionChanged += (_, _) =>
        {
            var providerId = SelectedProviderId();
            _address.Text = providerId switch
            {
                AiSettings.Claude => AnthropicChatProvider.DefaultAddress,
                AiSettings.Antigravity => AntigravityChatProvider.DefaultAddress,
                AiSettings.OpenAi => "http://127.0.0.1:1234",
                _ => OllamaChatProvider.DefaultAddress,
            };
            PopulateModelChoices(providerId, null);
            UpdateKeyRowVisibility();
            RefreshRemoteWarning();
        };
        _address.TextChanged += (_, _) => RefreshRemoteWarning();
        _refresh.Click += async (_, _) => await RefreshModelsAsync();
        RefreshRemoteWarning();

        _status.Text = DefaultStatusMessage(SelectedProviderId());
        _ask.Click += async (_, _) => await AskAsync();
        _apply.Click += (_, _) => Close(_proposal);
        _close.Click += (_, _) => Close(null);
        Closing += (_, _) => _running?.Cancel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_ask);
        buttons.Children.Add(_apply);
        buttons.Children.Add(_close);

        var provider = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("105,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"),
            RowSpacing = 6,
        };
        var rows = new (Control Label, Control Field)[]
        {
            (new TextBlock { Text = UiStrings.T("Sağlayıcı"), VerticalAlignment = VerticalAlignment.Center }, _provider),
            (new TextBlock { Text = UiStrings.T("Model"), VerticalAlignment = VerticalAlignment.Center }, ModelRow()),
            (new TextBlock { Text = UiStrings.T("Adres"), VerticalAlignment = VerticalAlignment.Center }, _address),
            (_apiKeyLabel, _apiKey),
        };
        for (var i = 0; i < rows.Length; i++)
        {
            Grid.SetRow(rows[i].Label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            provider.Children.Add(rows[i].Label);
            provider.Children.Add(rows[i].Field);
        }

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(18, 16) };
        panel.Children.Add(provider);
        panel.Children.Add(_remote);
        panel.Children.Add(new TextBlock { Text = UiStrings.T("Değişiklik isteği") });
        panel.Children.Add(_request);
        panel.Children.Add(_busy);
        panel.Children.Add(_status);
        panel.Children.Add(new TextBlock { Text = UiStrings.T("Önerilen değişiklikler"), Opacity = 0.6, FontSize = 12 });
        panel.Children.Add(_changes);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private static int ProviderIndex(string provider) => provider switch
    {
        AiSettings.Claude => 1,
        AiSettings.Antigravity => 2,
        AiSettings.OpenAi => 3,
        _ => 0,
    };

    private string SelectedProviderId() => _provider.SelectedIndex switch
    {
        1 => AiSettings.Claude,
        2 => AiSettings.Antigravity,
        3 => AiSettings.OpenAi,
        _ => AiSettings.Ollama,
    };

    private void PopulateModelChoices(string providerId, string? preferredModel)
    {
        var choices = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredModel))
        {
            choices.Add(preferredModel);
        }

        IEnumerable<string> presets = providerId switch
        {
            AiSettings.Claude => AnthropicChatProvider.PresetModels,
            AiSettings.Antigravity => AntigravityChatProvider.PresetModels,
            AiSettings.Ollama => [OllamaChatProvider.DefaultModel],
            _ => [],
        };
        foreach (var preset in presets)
        {
            if (!choices.Contains(preset, StringComparer.Ordinal))
            {
                choices.Add(preset);
            }
        }

        _model.ItemsSource = choices;
        if (choices.Count > 0)
        {
            _model.SelectedIndex = 0;
        }
        else
        {
            _model.Text = preferredModel ?? string.Empty;
        }
    }

    private void UpdateKeyRowVisibility()
    {
        var needsKey = SelectedProviderId() != AiSettings.Ollama;
        _apiKeyLabel.IsVisible = needsKey;
        _apiKey.IsVisible = needsKey;
    }

    private StackPanel ModelRow()
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(_model);
        row.Children.Add(_refresh);
        return row;
    }

    private AiSettings CurrentSettings()
    {
        var model = (_model.Text ?? _model.SelectedItem as string)?.Trim() ?? string.Empty;
        if (model.Length == 0)
        {
            model = (_model.SelectedItem as string)?.Trim() ?? string.Empty;
        }

        return new AiSettings(
            SelectedProviderId(),
            _address.Text?.Trim() ?? string.Empty,
            model,
            string.IsNullOrWhiteSpace(_apiKey.Text) ? null : _apiKey.Text.Trim());
    }

    private void RefreshRemoteWarning()
    {
        var settings = CurrentSettings();
        _remote.IsVisible = !settings.IsLocal;
        if (!_remote.IsVisible)
        {
            return;
        }

        var keyHint = settings.Provider switch
        {
            AiSettings.Claude => "ANTHROPIC_API_KEY / AKE_LLM_API_KEY",
            AiSettings.Antigravity => "ANTIGRAVITY_API_KEY / GEMINI_API_KEY / AKE_LLM_API_KEY",
            _ => "AKE_LLM_API_KEY",
        };
        _remote.Text = UiStrings.Current == UiLanguage.English
            ? $"Remote endpoint: project summary (device & wire names) will be sent there. Key can be entered above (never saved) or read from {keyHint}."
            : $"Bu adres bu bilgisayar değil: proje özeti (cihaz ve bağlantı adları) oraya gönderilir. API anahtarını yukarıya girebilirsiniz (diske yazılmaz) veya {keyHint} ortam değişkeni kullanılır.";
    }

    private static string DefaultStatusMessage(string providerId) => providerId switch
    {
        AiSettings.Claude => UiStrings.Current == UiLanguage.English
            ? "Claude (Anthropic) is selected; proposed changes are checked by ERC and shown before applying."
            : "Claude (Anthropic) seçili; öneri ERC'den geçirilir ve uygulanmadan önce gösterilir.",
        AiSettings.Antigravity => UiStrings.Current == UiLanguage.English
            ? "Antigravity (Gemini / local proxy) is selected; proposed changes are checked by ERC before applying."
            : "Antigravity (Gemini / yerel vekil) seçili; öneri ERC'den geçirilir ve uygulanmadan önce gösterilir.",
        _ => UiStrings.Current == UiLanguage.English
            ? "Local Ollama runs offline; CPU inference may take a few minutes and changes are previewed before applying."
            : "Yerel model (Ollama) kullanılır; veri bilgisayarınızdan çıkmaz. Öneri CPU'da birkaç dakika sürebilir ve uygulanmadan önce gösterilir.",
    };

    // Sağlayıcıdaki modelleri listeler; ulaşılamazsa neden durum satırında yazar ve model adı elle yazılabilir.
    private async Task RefreshModelsAsync()
    {
        var settings = CurrentSettings();
        _status.Text = UiStrings.Current == UiLanguage.English ? "Listing models…" : "Modeller listeleniyor…";
        var models = await _listModels(settings);
        if (models.IsFailure)
        {
            _status.Text = models.Error!.Message + (UiStrings.Current == UiLanguage.English
                ? " You can enter or pick a model manually."
                : " Model adını listeden seçebilir ya da elle yazabilirsiniz.");
            return;
        }

        var chosen = settings.Model;
        _model.ItemsSource = models.Value;
        _model.SelectedItem = models.Value.Contains(chosen) ? chosen : models.Value.FirstOrDefault();
        _status.Text = UiStrings.Current == UiLanguage.English
            ? $"{models.Value.Count} models found."
            : $"{models.Value.Count} model bulundu.";
    }

    private async Task AskAsync()
    {
        // Çalışıyorsa düğme İptal olur.
        if (_running is not null)
        {
            _running.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(_request.Text))
        {
            _status.Text = UiStrings.Current == UiLanguage.English ? "Enter your change request first." : "Önce yapılacak değişikliği yazın.";
            return;
        }

        _proposal = null;
        _changes.ItemsSource = null;
        _apply.IsEnabled = false;
        _busy.IsVisible = true;
        _ask.Content = UiStrings.T("İptal");
        _status.Text = UiStrings.Current == UiLanguage.English
            ? "Generating proposal…"
            : "Öneri hazırlanıyor… (CPU'da birkaç dakika sürebilir)";
        _running = new CancellationTokenSource();
        try
        {
            var settings = CurrentSettings();
            var text = _request.Text!;
            var token = _running.Token;
            var result = await Task.Run(() => _propose(settings, text, token), token);
            Dispatcher.UIThread.Post(() => Show(result));
        }
        catch (OperationCanceledException)
        {
            _status.Text = UiStrings.Current == UiLanguage.English ? "Request canceled." : "İstek iptal edildi.";
        }
        finally
        {
            _running = null;
            _busy.IsVisible = false;
            _ask.Content = UiStrings.T("Öneri iste");
        }
    }

    private void Show(Result<PatchProposal> result)
    {
        if (result.IsFailure)
        {
            _status.Text = result.Error!.Message;
            return;
        }

        _proposal = result.Value;
        _changes.ItemsSource = PatchSummary.Describe(result.Value.PatchJson);
        _apply.IsEnabled = true;
        _status.Text = result.Value.NewWarnings.Count == 0
            ? (UiStrings.Current == UiLanguage.English
                ? "Proposal verified with zero new ERC errors. Click Apply to run in one undoable step."
                : "Öneri doğrulandı: yeni ERC hatası yok. Uygula derseniz tek adımda yapılır ve Geri Al ile geri alınır.")
            : (UiStrings.Current == UiLanguage.English
                ? "Proposal verified with warnings: "
                : "Öneri doğrulandı, yeni uyarılar var: ") + string.Join(" ", result.Value.NewWarnings);
    }
}
