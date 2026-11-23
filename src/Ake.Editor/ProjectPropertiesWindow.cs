using Ake.Core;
using Ake.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Ake.Editor;

/// <summary>Proje özellikleri penceresinin sonucu: yeni proje adı ve alan değerleri (boş değer alanı siler).</summary>
public sealed record ProjectInfoEdit(string Name, IReadOnlyDictionary<string, string> Values);

/// <summary>
/// Proje özellikleri penceresi (ADR-0047, Faz 6): Özellikler sekmesinde proje adı ve kapak/antet bilgileri, Yapı sekmesinde
/// = fonksiyon ve + yer düğümü yönetimi; İstatistik ve Durum sekmeleri salt okunurdur. Tamam Özellikler düzenlemesini,
/// İptal ya da Esc <c>null</c> döner; Yapı sekmesinde yapılan değişiklikler <see cref="StructureChanged"/> ile bildirilir.
/// </summary>
public sealed class ProjectPropertiesWindow : Window
{
    private sealed record StructureRow(
        StructureNodeId Id, Aspect Aspect, string Name, StructureNodeId? ParentId, string? Description, string Display)
    {
        public override string ToString() => Display;
    }

    private readonly EditorSession _session;
    private readonly TextBox _name = new();
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly TextBlock _message = new() { Foreground = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#c0392b")), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ListBox _structureList = new() { Height = 165 };
    private readonly SelectableTextBlock _structureSummary = new()
    {
        FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace"),
        FontSize = 12,
    };

    private readonly ComboBox _nodeAspect = new()
    {
        ItemsSource = new[] { "= Fonksiyon", "+ Yer" },
        SelectedIndex = 0,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    private readonly ComboBox _nodeParent = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _nodeName = new() { PlaceholderText = "örn. M3 veya P2" };
    private readonly TextBox _nodeDescription = new() { PlaceholderText = "İsteğe bağlı açıklama" };
    private readonly Button _updateNodeButton = new() { Content = "Seçiliyi güncelle", MinWidth = 110, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly Button _removeNodeButton = new() { Content = "Seçiliyi sil", MinWidth = 96, HorizontalContentAlignment = HorizontalAlignment.Center };
    private readonly TabItem _statsTab;
    private readonly TabItem _statusTab;
    private bool _syncingStructure;

    /// <summary>Yapı sekmesinden düğüm eklendi, güncellendi veya silindiyse <c>true</c>.</summary>
    public bool StructureChanged { get; private set; }

    /// <summary>Kullanıcı onayladıktan sonra doğrudan Antet düzenleyiciye geçmek istediyse <c>true</c>.</summary>
    public bool OpenTitleBlockAfter { get; private set; }

    public ProjectPropertiesWindow(EditorSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        var project = session.Project ?? throw new ArgumentException("Proje açık değil.", nameof(session));

        Title = $"Proje özellikleri: {project.Name}";
        Width = 660;
        Height = 640;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _name.Text = project.Name;
        _name.TextChanged += (_, _) => _message.Text = string.IsNullOrWhiteSpace(_name.Text) ? "Projenin adı boş olamaz." : string.Empty;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*"), RowSpacing = 8, Margin = new Thickness(16) };
        var rows = new List<(string Label, Control Field)> { ("Proje adı", _name) };
        foreach (var (key, label) in EditorSession.ProjectFields)
        {
            var box = new TextBox { Text = project.Properties.GetValueOrDefault(key, string.Empty) };
            _fields[key] = box;
            rows.Add((label, box));
        }

        var logoBox = new TextBox
        {
            Text = project.Properties.GetValueOrDefault("companyLogo", string.Empty),
            PlaceholderText = "Kapak logosu (.png, .jpg)",
        };
        _fields["companyLogo"] = logoBox;

        var pickLogoButton = new Button { Content = "Seç…" };
        pickLogoButton.Click += async (_, _) =>
        {
            var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
            if (provider is null)
            {
                return;
            }

            var picked = await provider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Firma logosu seç",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new Avalonia.Platform.Storage.FilePickerFileType("Resim dosyaları") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"] },
                    Avalonia.Platform.Storage.FilePickerFileTypes.All,
                ],
            });

            if (picked.Count > 0 && Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(picked[0]) is { Length: > 0 } localPath)
            {
                logoBox.Text = localPath;
            }
        };

        var clearLogoButton = new Button { Content = "×", Width = 30 };
        clearLogoButton.Click += (_, _) => logoBox.Text = string.Empty;

        var logoRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 6 };
        Grid.SetColumn(logoBox, 0);
        Grid.SetColumn(pickLogoButton, 1);
        Grid.SetColumn(clearLogoButton, 2);
        logoRow.Children.AddRange([logoBox, pickLogoButton, clearLogoButton]);
        rows.Add(("Firma logosu (antet ve kapak)", logoRow));

        var languageBox = new TextBox
        {
            Text = project.Properties.GetValueOrDefault("language", string.Empty),
            PlaceholderText = "tr, en, de (ör. metin: tr=Giriş;en=Input)",
        };
        _fields["language"] = languageBox;
        rows.Add(("Proje dili", languageBox));

        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(label, i);
            Grid.SetRow(rows[i].Field, i);
            Grid.SetColumn(rows[i].Field, 1);
            grid.Children.Add(label);
            grid.Children.Add(rows[i].Field);
        }

        _statsTab = new TabItem { Header = "İstatistik", FontSize = 14, Content = Table(session.ProjectStatistics()) };
        _statusTab = new TabItem { Header = "Durum", FontSize = 14, Content = Table(session.ProjectStatus()) };

        var tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Özellikler", FontSize = 14, Content = new ScrollViewer { Content = grid } },
                _statsTab,
                new TabItem { Header = "Yapı", FontSize = 14, Content = BuildStructureTab() },
                _statusTab,
            },
        };

        var editTitleBlock = new Button { Content = "Anteti düzenle…", MinWidth = 128, HorizontalContentAlignment = HorizontalAlignment.Center };
        var ok = new Button { Content = "Tamam", IsDefault = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        var cancel = new Button { Content = "İptal", IsCancel = true, MinWidth = 88, HorizontalContentAlignment = HorizontalAlignment.Center };
        editTitleBlock.Click += (_, _) =>
        {
            if (BuildEdit() is not { } result)
            {
                return;
            }

            OpenTitleBlockAfter = true;
            Close(result);
        };
        ok.Click += (_, _) =>
        {
            if (BuildEdit() is { } result)
            {
                Close(result);
            }
        };
        cancel.Click += (_, _) => Close(null);

        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { ok, cancel } };
        var buttons = new DockPanel { LastChildFill = false, Margin = new Thickness(16, 8) };
        DockPanel.SetDock(editTitleBlock, Avalonia.Controls.Dock.Left);
        DockPanel.SetDock(rightButtons, Avalonia.Controls.Dock.Right);
        buttons.Children.Add(editTitleBlock);
        buttons.Children.Add(rightButtons);

        ProjectInfoEdit? BuildEdit()
        {
            if (string.IsNullOrWhiteSpace(_name.Text))
            {
                _message.Text = "Projenin adı boş olamaz.";
                return null;
            }

            return new ProjectInfoEdit(_name.Text.Trim(), _fields.ToDictionary(entry => entry.Key, entry => entry.Value.Text ?? string.Empty));
        }

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        DockPanel.SetDock(_message, Avalonia.Controls.Dock.Bottom);
        _message.Margin = new Thickness(16, 0);
        root.Children.Add(buttons);
        root.Children.Add(_message);
        root.Children.Add(tabs);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    private Control BuildStructureTab()
    {
        var addButton = new Button { Content = "Düğüm ekle", MinWidth = 100, HorizontalContentAlignment = HorizontalAlignment.Center };
        var clearButton = new Button { Content = "Temizle", MinWidth = 80, HorizontalContentAlignment = HorizontalAlignment.Center };

        _nodeAspect.SelectionChanged += (_, _) =>
        {
            if (!_syncingStructure)
            {
                RefreshParentChoices(null);
            }
        };

        _structureList.SelectionChanged += (_, _) =>
        {
            if (_structureList.SelectedItem is not StructureRow row)
            {
                _updateNodeButton.IsEnabled = false;
                _removeNodeButton.IsEnabled = false;
                return;
            }

            _syncingStructure = true;
            _nodeAspect.SelectedIndex = row.Aspect == Aspect.Function ? 0 : 1;
            RefreshParentChoices(row.ParentId);
            _nodeName.Text = row.Name;
            _nodeDescription.Text = row.Description ?? string.Empty;
            _syncingStructure = false;
            _updateNodeButton.IsEnabled = true;
            _removeNodeButton.IsEnabled = true;
            _message.Text = string.Empty;
        };

        addButton.Click += (_, _) =>
        {
            var aspect = _nodeAspect.SelectedIndex == 0 ? Aspect.Function : Aspect.Location;
            var parentId = (_nodeParent.SelectedItem as NodeChoice)?.Id;
            var added = _session.AddStructureNode(aspect, _nodeName.Text ?? string.Empty, parentId, _nodeDescription.Text);
            if (added.IsFailure)
            {
                _message.Text = added.Error!.Message;
                return;
            }

            StructureChanged = true;
            _message.Text = string.Empty;
            RefreshStructure(selectId: added.Value);
        };

        _updateNodeButton.Click += (_, _) =>
        {
            if (_structureList.SelectedItem is not StructureRow row)
            {
                return;
            }

            var updated = _session.UpdateStructureNode(row.Id, _nodeName.Text ?? string.Empty, _nodeDescription.Text);
            if (updated.IsFailure)
            {
                _message.Text = updated.Error!.Message;
                return;
            }

            StructureChanged = true;
            _message.Text = string.Empty;
            RefreshStructure(selectId: row.Id);
        };

        _removeNodeButton.Click += (_, _) =>
        {
            if (_structureList.SelectedItem is not StructureRow row)
            {
                return;
            }

            var removed = _session.RemoveStructureNode(row.Id);
            if (removed.IsFailure)
            {
                _message.Text = removed.Error!.Message;
                return;
            }

            StructureChanged = true;
            _message.Text = string.Empty;
            _nodeName.Text = string.Empty;
            _nodeDescription.Text = string.Empty;
            RefreshStructure(selectId: null);
        };

        clearButton.Click += (_, _) =>
        {
            _structureList.SelectedItem = null;
            _nodeParent.SelectedIndex = 0;
            _nodeName.Text = string.Empty;
            _nodeDescription.Text = string.Empty;
            _message.Text = string.Empty;
        };

        var form = new Grid { ColumnDefinitions = new ColumnDefinitions("120,*,120,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 8, ColumnSpacing = 8 };
        var aspectLabel = new TextBlock { Text = "Yapı türü", VerticalAlignment = VerticalAlignment.Center };
        var parentLabel = new TextBlock { Text = "Üst düğüm", VerticalAlignment = VerticalAlignment.Center };
        var nameLabel = new TextBlock { Text = "Kod / Ad", VerticalAlignment = VerticalAlignment.Center };
        var descLabel = new TextBlock { Text = "Açıklama", VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(aspectLabel, 0);
        Grid.SetColumn(aspectLabel, 0);
        Grid.SetRow(_nodeAspect, 0);
        Grid.SetColumn(_nodeAspect, 1);
        Grid.SetRow(parentLabel, 0);
        Grid.SetColumn(parentLabel, 2);
        Grid.SetRow(_nodeParent, 0);
        Grid.SetColumn(_nodeParent, 3);
        Grid.SetRow(nameLabel, 1);
        Grid.SetColumn(nameLabel, 0);
        Grid.SetRow(_nodeName, 1);
        Grid.SetColumn(_nodeName, 1);
        Grid.SetRow(descLabel, 1);
        Grid.SetColumn(descLabel, 2);
        Grid.SetRow(_nodeDescription, 1);
        Grid.SetColumn(_nodeDescription, 3);
        form.Children.Add(aspectLabel);
        form.Children.Add(_nodeAspect);
        form.Children.Add(parentLabel);
        form.Children.Add(_nodeParent);
        form.Children.Add(nameLabel);
        form.Children.Add(_nodeName);
        form.Children.Add(descLabel);
        form.Children.Add(_nodeDescription);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { addButton, _updateNodeButton, _removeNodeButton, clearButton },
        };

        var summaryBox = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#D0CBC0")),
            Padding = new Thickness(10, 8),
            Height = 95,
            Child = new ScrollViewer { Content = _structureSummary },
        };

        RefreshStructure(selectId: null);

        return new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 10,
                Children =
                {
                    new TextBlock { Text = "Yapı düğümleri (= Fonksiyon / + Yer)", FontWeight = Avalonia.Media.FontWeight.SemiBold },
                    _structureList,
                    form,
                    actions,
                    summaryBox,
                },
            },
        };
    }

    private void RefreshParentChoices(StructureNodeId? selectedParent)
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var aspect = _nodeAspect.SelectedIndex == 0 ? Aspect.Function : Aspect.Location;
        var choices = new List<NodeChoice> { new(null, "— (kök)") };
        choices.AddRange(EditorSession.StructureChoices(project, aspect).Skip(1));
        _nodeParent.ItemsSource = choices;
        var index = choices.FindIndex(choice => choice.Id == selectedParent);
        _nodeParent.SelectedIndex = Math.Max(0, index);
    }

    private void RefreshStructure(StructureNodeId? selectId)
    {
        if (_session.Project is not { } project)
        {
            return;
        }

        var rows = new List<StructureRow>();
        foreach (var aspect in new[] { Aspect.Function, Aspect.Location })
        {
            var prefix = aspect == Aspect.Function ? '=' : '+';
            foreach (var node in project.Structure
                .Where(candidate => candidate.Aspect == aspect)
                .OrderBy(candidate => project.StructurePath(candidate.Id), StringComparer.Ordinal))
            {
                var path = project.StructurePath(node.Id)!;
                var pages = project.Pages.Count(page => (aspect == Aspect.Function ? page.FunctionNodeId : page.LocationNodeId) == node.Id);
                var devices = project.Devices.Count(device => (aspect == Aspect.Function ? device.FunctionNodeId : device.LocationNodeId) == node.Id);
                var detail = string.IsNullOrWhiteSpace(node.Description) ? string.Empty : $" — {node.Description}";
                rows.Add(new StructureRow(node.Id, aspect, node.Name, node.ParentId, node.Description, $"{prefix}{path}{detail}  ({pages} sayfa, {devices} cihaz)"));
            }
        }

        _structureList.ItemsSource = rows;
        var match = selectId is { } id ? rows.FirstOrDefault(row => row.Id == id) : null;
        _structureList.SelectedItem = match;
        _updateNodeButton.IsEnabled = match is not null;
        _removeNodeButton.IsEnabled = match is not null;

        if (match is null)
        {
            RefreshParentChoices(null);
        }

        _structureSummary.Text = string.Join("\n", _session.ProjectStructureLines());
        _statsTab.Content = Table(_session.ProjectStatistics());
        _statusTab.Content = Table(_session.ProjectStatus());
    }

    // Etiket/değer çiftlerini iki sütunlu, seçilip kopyalanabilir salt okunur tablo olarak gösterir.
    private static Control Table(IReadOnlyList<(string Label, string Value)> rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*"), RowSpacing = 6, Margin = new Thickness(16) };
        for (var i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, Opacity = 0.75 };
            var value = new SelectableTextBlock { Text = rows[i].Value, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return new ScrollViewer { Content = grid };
    }

    private static Control Lines(IReadOnlyList<string> lines) => new ScrollViewer
    {
        Content = new SelectableTextBlock
        {
            Text = string.Join("\n", lines),
            Margin = new Thickness(16),
            FontFamily = new Avalonia.Media.FontFamily("Consolas, Menlo, monospace"),
            FontSize = 13,
        },
    };
}
