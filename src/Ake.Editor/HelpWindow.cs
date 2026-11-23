using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>
/// İçindekiler listesi, anlık arama kutusu, adım adım açıklamalar ve vektörel görsel diyagramlar içeren
/// iki dilli (TR / EN) etkileşimli yardım penceresi; ayrıca tek dosyalık <c>yardim.html</c> üretebilir.
/// </summary>
public sealed class HelpWindow : Window
{
    private readonly TextBox _searchBox;
    private readonly ListBox _tocList = new();
    private readonly TextBlock _topicTitle = new() { FontSize = 18, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _topicSummary = new() { FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#313742")) };
    private readonly SceneView _diagramView = new() { Height = 230, GridStepMm = 0 };
    private readonly StackPanel _stepsPanel = new() { Spacing = 6 };
    private readonly WrapPanel _shortcutsPanel = new() { Orientation = Orientation.Horizontal };
    private readonly Border _shortcutsSection;
    private readonly StackPanel _tipsList = new() { Spacing = 4 };
    private readonly Border _tipsSection;
    private readonly TextBlock _matchCount = new() { FontSize = 11.5, Foreground = new SolidColorBrush(Color.Parse("#5A6270")) };
    private readonly TextBlock _statusText = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

    private UiLanguage Language => UiStrings.Current;

    public HelpWindow(string? initialTopicId = null)
    {
        Title = UiStrings.T("aktapokus-electric Yardım Kılavuzu");
        Width = 1080;
        Height = 720;
        MinWidth = 860;
        MinHeight = 560;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("fonts:Inter#Inter");
        FontSize = 13;

        _searchBox = new TextBox
        {
            PlaceholderText = UiStrings.T("Konu, komut veya özellik ara…"),
            Margin = new Thickness(0, 0, 0, 6),
        };
        _searchBox.TextChanged += (_, _) => RefreshTopics();

        _tocList.SelectionChanged += (_, _) =>
        {
            if (_tocList.SelectedItem is TopicEntry entry)
            {
                ShowTopic(entry.Topic);
            }
        };

        var leftPane = new Border
        {
            Width = 310,
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(0, 0, 1, 0),
            Background = Brush("#FAF7F0"),
            Padding = new Thickness(14),
            Child = new DockPanel
            {
                Children =
                {
                    WrapTop(new TextBlock { Text = UiStrings.T("İçindekiler"), FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) }),
                    WrapTop(_searchBox),
                    WrapTop(new Border { Child = _matchCount, Margin = new Thickness(0, 0, 0, 6) }),
                    _tocList,
                },
            },
        };

        _shortcutsSection = new Border
        {
            Margin = new Thickness(0, 8, 0, 0),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = UiStrings.T("Kısayollar"), FontWeight = FontWeight.SemiBold, FontSize = 13 },
                    _shortcutsPanel,
                },
            },
        };

        _tipsSection = new Border
        {
            Background = Brush("#F0F6FF"),
            BorderBrush = Brush("#1F6FD8"),
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(12, 10),
            CornerRadius = new CornerRadius(4),
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = UiStrings.T("İpuçları"), FontWeight = FontWeight.SemiBold, FontSize = 13, Foreground = Brush("#12386E") },
                    _tipsList,
                },
            },
        };

        var diagramCard = new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Background = Brushes.White,
            Padding = new Thickness(8),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new TextBlock { Text = UiStrings.T("Görsel anlatım"), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = Brush("#5A6270") },
                    _diagramView,
                },
            },
        };

        var detailContent = new StackPanel
        {
            Margin = new Thickness(22, 18),
            Spacing = 12,
            Children =
            {
                _topicTitle,
                _topicSummary,
                diagramCard,
                new TextBlock { Text = UiStrings.T("Adım adım kullanım"), FontWeight = FontWeight.SemiBold, FontSize = 14, Margin = new Thickness(0, 4, 0, 0) },
                _stepsPanel,
                _shortcutsSection,
                _tipsSection,
            },
        };

        var htmlButton = new Button
        {
            Content = UiStrings.T("HTML Yardım Dosyasını Kaydet / Aç…"),
            MinWidth = 220,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        htmlButton.Click += (_, _) => _ = ExportHtmlAsync();

        var closeButton = new Button
        {
            Content = UiStrings.T("Kapat"),
            IsDefault = true,
            IsCancel = true,
            MinWidth = 92,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        };
        closeButton.Click += (_, _) => Close();

        var footer = new Border
        {
            BorderBrush = Brush("#D0CBC0"),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Background = Brush("#FAF7F0"),
            Padding = new Thickness(14, 10),
            Child = new DockPanel
            {
                LastChildFill = true,
                Children =
                {
                    WrapRight(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { htmlButton, closeButton } }),
                    _statusText,
                },
            },
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(leftPane, Avalonia.Controls.Dock.Left);
        root.Children.Add(footer);
        root.Children.Add(leftPane);
        root.Children.Add(new ScrollViewer { Content = detailContent });
        Content = root;

        RefreshTopics(initialTopicId);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && e.Source is not TextBox)
            {
                Close();
            }
        };
    }

    private void RefreshTopics(string? selectId = null)
    {
        var previousId = selectId ?? (_tocList.SelectedItem as TopicEntry)?.Topic.Id;
        var matches = HelpContent.Search(_searchBox.Text, Language);
        var entries = matches.Select(topic => new TopicEntry(topic, Language)).ToList();
        _tocList.ItemsSource = entries;
        _matchCount.Text = $"{entries.Count} / {HelpContent.Topics.Count}";

        if (entries.Count == 0)
        {
            _topicTitle.Text = UiStrings.T("Eşleşen yardım konusu bulunamadı.");
            _topicSummary.Text = string.Empty;
            _diagramView.UpdateScene(null);
            _stepsPanel.Children.Clear();
            _shortcutsSection.IsVisible = false;
            _tipsSection.IsVisible = false;
            return;
        }

        var idx = previousId is null
            ? 0
            : Math.Max(0, entries.FindIndex(e => string.Equals(e.Topic.Id, previousId, StringComparison.Ordinal)));
        _tocList.SelectedIndex = idx;
        ShowTopic(entries[idx].Topic);
    }

    private void ShowTopic(HelpTopic topic)
    {
        _topicTitle.Text = topic.TitleFor(Language);
        _topicSummary.Text = topic.SummaryFor(Language);
        _diagramView.UpdateScene(topic.BuildIllustration(Language));

        _stepsPanel.Children.Clear();
        var steps = topic.StepsFor(Language);
        for (var i = 0; i < steps.Count; i++)
        {
            _stepsPanel.Children.Add(new Border
            {
                Background = Brush("#FAF8F3"),
                BorderBrush = Brush("#E3DDD0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(10, 7),
                Child = new TextBlock
                {
                    Text = $"{i + 1}. {steps[i]}",
                    TextWrapping = TextWrapping.Wrap,
                },
            });
        }

        _shortcutsPanel.Children.Clear();
        if (topic.Shortcuts.Count > 0)
        {
            _shortcutsSection.IsVisible = true;
            foreach (var key in topic.Shortcuts)
            {
                _shortcutsPanel.Children.Add(new Border
                {
                    Background = Brush("#EEF1F6"),
                    BorderBrush = Brush("#CBD2DF"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 3),
                    Margin = new Thickness(0, 0, 6, 4),
                    Child = new TextBlock { Text = key, FontWeight = FontWeight.SemiBold, FontSize = 12 },
                });
            }
        }
        else
        {
            _shortcutsSection.IsVisible = false;
        }

        _tipsList.Children.Clear();
        var tips = topic.TipsFor(Language);
        _tipsSection.IsVisible = tips.Count > 0;
        foreach (var tip in tips)
        {
            _tipsList.Children.Add(new TextBlock { Text = $"• {tip}", TextWrapping = TextWrapping.Wrap });
        }
    }

    private async Task ExportHtmlAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = UiStrings.T("HTML Yardım Dosyasını Kaydet / Aç…"),
            SuggestedFileName = Language == UiLanguage.English ? "yardim-en.html" : "yardim.html",
            DefaultExtension = "html",
            FileTypeChoices = [new FilePickerFileType("HTML") { Patterns = ["*.html"] }],
        });
        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            HelpContent.ExportStandaloneHtml(path, Language);
            _statusText.Foreground = Brush("#2E6F40");
            _statusText.Text = $"{UiStrings.T("HTML kılavuz kaydedildi")}: {path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _statusText.Foreground = Brush("#C0392B");
            _statusText.Text = ex.Message;
        }
    }

    private static Control WrapTop(Control control)
    {
        DockPanel.SetDock(control, Avalonia.Controls.Dock.Top);
        return control;
    }

    private static Control WrapRight(Control control)
    {
        DockPanel.SetDock(control, Avalonia.Controls.Dock.Right);
        return control;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    private sealed record TopicEntry(HelpTopic Topic, UiLanguage Lang)
    {
        public override string ToString() => Topic.TitleFor(Lang);
    }
}
