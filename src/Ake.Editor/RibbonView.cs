using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using FluentIcons.Avalonia;

using DockSide = Avalonia.Controls.Dock;

namespace Ake.Editor;

/// <summary>
/// Komut şeridi: solda Dosya düğmesi, sekmeler, sağda hızlı erişim; altta seçili sekmenin ikonlu
/// grupları. Düzen <see cref="EditorCommands"/>'tan gelir; komutların ne yaptığı <see cref="Bind"/> ile verilir.
/// </summary>
public sealed class RibbonView : Border
{
    private const int SmallPerColumn = 3;

    // Sabit düğmeler (Dosya menüsü, hızlı erişim) ve seçili sekmenin düğmeleri ayrı tutulur; sekme
    // değişince yalnızca ikincisi yeniden kurulur.
    private readonly List<(string Id, Control Control)> _fixed = [];
    private readonly List<(string Id, Control Control)> _tab = [];
    private readonly List<Button> _tabHeaders = [];
    private readonly ContentControl _body = new();
    private readonly Border _bodyFrame = new() { Classes = { "ribbon-body" } };
    private readonly StackPanel _quickAccess = new() { Orientation = Orientation.Horizontal, Spacing = 2 };
    private readonly Button _file = new() { Content = UiStrings.T("Dosya"), Classes = { "ribbon-file" } };
    private IReadOnlyDictionary<string, Action> _actions = new Dictionary<string, Action>();
    private Func<string, bool> _isEnabled = _ => true;
    private Func<string, bool> _isChecked = _ => false;
    private readonly RibbonDefinition _definition;
    private Func<string, string?> _gestureOf;
    private int _selected;

    public RibbonView()
        : this(EditorCommands.Main)
    {
    }

    public RibbonView(RibbonDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _gestureOf = id => _definition.Get(id).Gesture;
        Classes.Add("ribbon");

        var tabs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom };
        for (var i = 0; i < _definition.Tabs.Count; i++)
        {
            var index = i;
            var header = new Button { Content = UiStrings.T(_definition.Tabs[i].Label), Classes = { "ribbon-tab" } };
            header.Click += (_, _) =>
            {
                Collapsed = false;
                SelectTab(index);
            };
            _tabHeaders.Add(header);
            tabs.Children.Add(header);
        }

        foreach (var id in _definition.QuickAccess)
        {
            _quickAccess.Children.Add(Register(_fixed, id, SmallButton(_definition.Get(id), showLabel: false)));
        }

        var menu = new MenuFlyout();
        foreach (var id in _definition.FileMenu)
        {
            var command = _definition.Get(id);
            var item = new MenuItem
            {
                Header = UiStrings.T(command.Label),
                Icon = new SymbolIcon { Symbol = command.Icon, FontSize = 16 },
                InputGesture = Gesture(_gestureOf(id)),
            };
            item.Click += (_, _) => Run(id);
            menu.Items.Add(Register(_fixed, id, item));
        }

        _file.Flyout = menu;

        var top = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_file, DockSide.Left);
        DockPanel.SetDock(tabs, DockSide.Left);
        DockPanel.SetDock(_quickAccess, DockSide.Right);
        top.Children.Add(_file);
        top.Children.Add(tabs);
        top.Children.Add(_quickAccess);

        _bodyFrame.Child = _body;
        var root = new DockPanel();
        DockPanel.SetDock(top, DockSide.Top);
        root.Children.Add(top);
        root.Children.Add(_bodyFrame);
        Child = root;

        SelectTab(0);
    }

    /// <summary>Daraltılmış şeritte yalnızca sekme başlıkları görünür; bir sekmeye tıklamak şeridi açar.</summary>
    public bool Collapsed
    {
        get => !_bodyFrame.IsVisible;
        set => _bodyFrame.IsVisible = !value;
    }

    /// <summary>Seçili sekmenin adı (görüntü aracı ve testler için).</summary>
    public string SelectedTab => _definition.Tabs[_selected].Label;

    /// <summary>Komutların eylemlerini, etkinlik ve açık/kapalı durumlarını bağlar.</summary>
    public void Bind(IReadOnlyDictionary<string, Action> actions, Func<string, bool> isEnabled, Func<string, bool> isChecked)
    {
        _actions = actions;
        _isEnabled = isEnabled;
        _isChecked = isChecked;
        Refresh();
    }

    /// <summary>
    /// Komutların kısayolları (kısayol profili). Verilince menü ve düğme ipuçları ile görünür sekme yenilenir.
    /// </summary>
    public Func<string, string?> GestureOf
    {
        set
        {
            _gestureOf = value;
            foreach (var (id, control) in _fixed)
            {
                switch (control)
                {
                    case MenuItem item:
                        item.InputGesture = Gesture(_gestureOf(id));
                        break;
                    case Button button:
                        ToolTip.SetTip(button, Tip(_definition.Get(id)));
                        break;
                }
            }

            SelectTab(_selected);
        }
    }

    /// <summary>Görüntüleme dili (TR / EN) değiştiğinde Dosya menüsünü, sekmeleri ve etkin sekme düğmelerini günceller.</summary>
    public void ApplyLanguage()
    {
        _file.Content = UiStrings.T("Dosya");
        for (var i = 0; i < _tabHeaders.Count && i < _definition.Tabs.Count; i++)
        {
            _tabHeaders[i].Content = UiStrings.T(_definition.Tabs[i].Label);
        }

        foreach (var (id, control) in _fixed)
        {
            var command = _definition.Get(id);
            switch (control)
            {
                case MenuItem item:
                    item.Header = UiStrings.T(command.Label);
                    break;
                case Button button:
                    ToolTip.SetTip(button, Tip(command));
                    break;
            }
        }

        SelectTab(_selected);
    }

    private string Tip(CommandInfo command)
    {
        var label = UiStrings.T(command.Label);
        return _gestureOf(command.Id) is { } gesture ? $"{label}  ({gesture})" : label;
    }

    private static Avalonia.Input.KeyGesture? Gesture(string? text) => text is null ? null : Avalonia.Input.KeyGesture.Parse(text);

    /// <summary>Komutu, etkinse çalıştırır. Kısayollar da buradan geçer.</summary>
    public bool Run(string id)
    {
        if (!_isEnabled(id) || !_actions.TryGetValue(id, out var action))
        {
            return false;
        }

        action();
        return true;
    }

    /// <summary>Bütün düğmelerin etkinliğini ve açık/kapalı durumunu yeniler.</summary>
    public void Refresh()
    {
        foreach (var (id, control) in _fixed.Concat(_tab))
        {
            control.IsEnabled = _isEnabled(id) && _actions.ContainsKey(id);
            if (control is ToggleButton toggle)
            {
                toggle.IsChecked = _isChecked(id);
            }
        }
    }

    public void SelectTab(int index)
    {
        _selected = index;
        for (var i = 0; i < _tabHeaders.Count; i++)
        {
            _tabHeaders[i].Classes.Set("selected", i == index);
        }

        _body.Content = BuildTab(_definition.Tabs[index]);
        Refresh();
    }

    private StackPanel BuildTab(RibbonTab tab)
    {
        _tab.Clear();
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var group in tab.Groups)
        {
            panel.Children.Add(BuildGroup(group));
        }

        return panel;
    }

    private Border BuildGroup(RibbonGroup group)
    {
        var items = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        StackPanel? column = null;
        foreach (var item in group.Items)
        {
            var command = _definition.Get(item.CommandId);
            if (item.Large)
            {
                column = null;
                items.Children.Add(Register(_tab, command.Id, LargeButton(command)));
                continue;
            }

            if (column is null || column.Children.Count == SmallPerColumn)
            {
                column = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Top };
                items.Children.Add(column);
            }

            column.Children.Add(Register(_tab, command.Id, SmallButton(command, showLabel: true)));
        }

        var label = new TextBlock { Text = UiStrings.T(group.Label), Classes = { "ribbon-group-label" } };
        var layout = new DockPanel();
        DockPanel.SetDock(label, DockSide.Bottom);
        layout.Children.Add(label);
        layout.Children.Add(items);
        return new Border { Classes = { "ribbon-group" }, Child = layout };
    }

    private Control LargeButton(CommandInfo command)
    {
        var content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new SymbolIcon { Symbol = command.Icon, FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center });
        content.Children.Add(new TextBlock
        {
            Text = UiStrings.T(command.Label),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 76,
        });
        return Decorate(command, content, "ribbon-large");
    }

    private Control SmallButton(CommandInfo command, bool showLabel)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new SymbolIcon { Symbol = command.Icon, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        if (showLabel)
        {
            content.Children.Add(new TextBlock { Text = UiStrings.T(command.Label), VerticalAlignment = VerticalAlignment.Center });
        }

        return Decorate(command, content, showLabel ? "ribbon-small" : "ribbon-quick");
    }

    private Control Decorate(CommandInfo command, Control content, string style)
    {
        Button button = command.Toggle ? new ToggleButton() : new Button();
        button.Content = content;
        button.Classes.Add(style);
        ToolTip.SetTip(button, Tip(command));
        button.Click += (_, _) => Run(command.Id);
        return button;
    }

    private static Control Register(List<(string Id, Control Control)> registry, string id, Control control)
    {
        registry.Add((id, control));
        return control;
    }
}
