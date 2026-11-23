using System.Globalization;
using Ake.Core;
using Ake.Core.Model;
using Ake.Render;
using Ake.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

using DockSide = Avalonia.Controls.Dock;

namespace Ake.Editor;

/// <summary>
/// Blok editörü: sembolü çizgi, dikdörtgen, daire, yazı ve bağlantı uçlarıyla çizer. Komutlar ana pencereyle aynı yapıdaki bir
/// komut şeridindedir (<see cref="SymbolEditorCommands"/>). Taslak <see cref="SymbolDraft"/>'tadır; pencere yalnızca gösterir ve
/// çağırır. Projeye kaydetme oturum üzerinden bir command'dır.
/// </summary>
public sealed class SymbolEditorWindow : Window
{
    private readonly EditorSession _session;
    private readonly SymbolDraft _draft;
    private readonly Function? _function;
    private readonly RibbonView _ribbon = new(SymbolEditorCommands.Ribbon);
    private readonly SceneView _canvas = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(10, 4), TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
    private readonly TextBox _name;
    private readonly TextBox _text;
    private readonly CheckBox _assign;
    private readonly TextBox _deviceName;
    private readonly TextBox _tagLetter;
    private readonly TextBox _pinName = new() { Width = 150 };
    private readonly ComboBox _pinKind = PointAttributeLabels.Combo(PointAttributeLabels.Kinds, PinKind.Unspecified, 150);
    private readonly ComboBox _pinConnector = PointAttributeLabels.Combo(PointAttributeLabels.Connectors, ConnectorType.Unspecified, 150);
    private readonly ComboBox _pinContact = PointAttributeLabels.Combo(PointAttributeLabels.Contacts, ContactType.Unspecified, 150);
    private readonly TextBox _pinCode = new() { Width = 172, MaxLength = PointAttributes.MaxConnectorCodeLength, PlaceholderText = "örn. A-kod 4 pin" };
    private readonly TextBox _editText = new() { Width = 172, AcceptsReturn = false, PlaceholderText = "Etiket metni" };
    private readonly TextBox _editTextSize = new() { Width = 172, PlaceholderText = "örn. 3.5" };
    private readonly ComboBox _editTextAlign = new() { ItemsSource = new[] { "Sol", "Orta", "Sağ" }, SelectedIndex = 0, Width = 172 };
    private readonly TextBox _editTextRotation = new() { Width = 172, PlaceholderText = "0, -45, -90…" };
    private bool _syncing;
    private StackPanel? _pinPanel;
    private StackPanel? _textPanel;
    private bool _libraryOverwrite;
    private ComboBox? _grid;
    private TextBox? _surfacePins;
    private (double X, double Y)? _press;
    private string? _notice;

    public SymbolEditorWindow(EditorSession session, Function? function)
        : this(session, function, null, false)
    {
    }

    /// <summary>Kütüphanedeki bir sembolü düzenler; <paramref name="overwriteLibrary"/> yalnızca kullanıcının kendi sembolü içindir.</summary>
    public SymbolEditorWindow(EditorSession session, SymbolDefinition start, bool overwriteLibrary)
        : this(session, null, start, overwriteLibrary)
    {
    }

    private SymbolEditorWindow(EditorSession session, Function? function, SymbolDefinition? start, bool overwriteLibrary)
    {
        ArgumentNullException.ThrowIfNull(session);

        _session = session;
        _function = function;
        _libraryOverwrite = overwriteLibrary;
        _draft = new SymbolDraft(start ?? session.StartingSymbolFor(function));

        Title = UiStrings.T("Blok editörü");
        Width = 1100;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new Avalonia.Media.FontFamily("fonts:Inter#Inter");
        FontSize = 14;

        _name = new TextBox { Text = _draft.Name, Width = 260 };
        _name.TextChanged += (_, _) => _draft.Name = _name.Text ?? string.Empty;
        _text = new TextBox { Text = _draft.Text, Width = 90 };
        _text.TextChanged += (_, _) => _draft.Text = _text.Text ?? string.Empty;
        _assign = new CheckBox
        {
            Content = function is null ? "Function'a ata" : $"Seçili function'a ata ({function.ConnectionPoints.Count} uç)",
            IsChecked = function is not null,
            IsEnabled = function is not null,
        };

        _deviceName = new TextBox { Text = _draft.DeviceName, Width = 200, PlaceholderText = "örn. Endüktif sensör" };
        _deviceName.TextChanged += (_, _) => _draft.DeviceName = _deviceName.Text;
        _tagLetter = new TextBox { Text = _draft.TagLetter, Width = 56, MaxLength = 3, PlaceholderText = "B" };
        _tagLetter.TextChanged += (_, _) => _draft.TagLetter = _tagLetter.Text;
        foreach (var box in new[] { _pinName, _pinCode })
        {
            box.TextChanged += (_, _) => ApplyPinFields();
        }

        foreach (var combo in new[] { _pinKind, _pinConnector, _pinContact })
        {
            combo.SelectionChanged += (_, _) => ApplyPinFields();
        }

        _editText.TextChanged += (_, _) => ApplyTextFields();
        _editTextSize.TextChanged += (_, _) => ApplyTextFields();
        _editTextRotation.TextChanged += (_, _) => ApplyTextFields();
        _editTextAlign.SelectionChanged += (_, _) => ApplyTextFields();

        _ribbon.Bind(Actions(), CanRun, IsChecked);

        // Ad, yazı aracının metni ve function'a atama: şeritte yeri olmayan girdiler altta, tek satırda.
        var bottom = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
        bottom.Children.Add(Label("Ad"));
        bottom.Children.Add(_name);
        bottom.Children.Add(Label("Yazı aracının metni"));
        bottom.Children.Add(_text);
        bottom.Children.Add(Label("Yüzeye eklenecek uç sayısı"));
        _surfacePins = new TextBox { Text = "6", Width = 56 };
        bottom.Children.Add(_surfacePins);
        bottom.Children.Add(Label("Izgara"));
        _grid = new ComboBox { ItemsSource = new[] { "5 mm", "2,5 mm", "1,25 mm", "0,5 mm" }, SelectedIndex = 1, Width = 100 };
        _grid.SelectionChanged += (_, _) =>
        {
            _draft.Grid = _grid.SelectedIndex switch { 0 => 5, 2 => 1.25, 3 => 0.5, _ => SymbolDraft.GridMm };
            _canvas.GridStepMm = _draft.Grid;
            Redraw();
        };
        bottom.Children.Add(_grid);
        bottom.Children.Add(Label("Cihaz adı"));
        bottom.Children.Add(_deviceName);
        bottom.Children.Add(Label("Tag harfi"));
        bottom.Children.Add(_tagLetter);
        bottom.Children.Add(_assign);

        foreach (var child in bottom.Children)
        {
            child.Margin = new Thickness(0, 0, 8, 4);
        }

        // Seçili öğe özellikleri: sağda; yazı seçiliyken "Seçili yazı", diğer durumlarda "Seçili uç" paneli görünür.
        var pinPanel = new StackPanel { Spacing = 6 };
        pinPanel.Children.Add(new TextBlock { Text = UiStrings.T("Seçili uç"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        foreach (var (text, field) in new (string, Control)[] { ("Ad", _pinName), ("Uç türü", _pinKind), ("Bağlantı", _pinConnector), ("Kontak", _pinContact), ("Kod / pin", _pinCode) })
        {
            pinPanel.Children.Add(new TextBlock { Text = UiStrings.T(text), FontSize = 12, Opacity = 0.6 });
            pinPanel.Children.Add(field);
        }

        var angleButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var angle in new[] { "0", "-45", "-90", "90" })
        {
            var btn = new Button { Content = $"{angle}°", Padding = new Thickness(6, 2), FontSize = 11 };
            btn.Click += (_, _) => _editTextRotation.Text = angle;
            angleButtons.Children.Add(btn);
        }

        var textPanel = new StackPanel { Spacing = 6, IsVisible = false };
        textPanel.Children.Add(new TextBlock { Text = UiStrings.T("Seçili yazı"), FontWeight = Avalonia.Media.FontWeight.SemiBold });
        textPanel.Children.Add(new TextBlock { Text = UiStrings.T("Metin"), FontSize = 12, Opacity = 0.6 });
        textPanel.Children.Add(_editText);
        textPanel.Children.Add(new TextBlock { Text = UiStrings.T("Boyut (mm)"), FontSize = 12, Opacity = 0.6 });
        textPanel.Children.Add(_editTextSize);
        textPanel.Children.Add(new TextBlock { Text = UiStrings.T("Hizalama"), FontSize = 12, Opacity = 0.6 });
        textPanel.Children.Add(_editTextAlign);
        textPanel.Children.Add(new TextBlock { Text = UiStrings.T("Açı (derece)"), FontSize = 12, Opacity = 0.6 });
        textPanel.Children.Add(_editTextRotation);
        textPanel.Children.Add(angleButtons);
        textPanel.Children.Add(new TextBlock
        {
            Text = UiStrings.T("İpucu: Çift tıklayarak da metni düzenleyebilirsiniz."),
            FontSize = 11,
            Opacity = 0.65,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });

        var inspector = new ScrollViewer
        {
            Width = 196,
            Content = new StackPanel
            {
                Margin = new Thickness(10),
                Children = { pinPanel, textPanel },
            },
        };

        var layout = new DockPanel();
        DockPanel.SetDock(_ribbon, DockSide.Top);
        DockPanel.SetDock(_status, DockSide.Bottom);
        DockPanel.SetDock(bottom, DockSide.Bottom);
        DockPanel.SetDock(inspector, DockSide.Right);
        layout.Children.Add(_ribbon);
        layout.Children.Add(_status);
        layout.Children.Add(bottom);
        layout.Children.Add(inspector);
        _pinPanel = pinPanel;
        _textPanel = textPanel;
        layout.Children.Add(_canvas);
        Content = layout;

        _canvas.GridStepMm = SymbolDraft.GridMm;
        _canvas.ContentPressed = Pressed;
        _canvas.ContentDragged += Dragged;
        _canvas.ContentReleased += Released;
        _canvas.ContentDoubleClicked += (x, y) => _ = DoubleClickedAsync(x, y);
        _canvas.SheetPointerMoved += (x, y) =>
        {
            var (sx, sy) = ToDraft(x, y);
            ShowStatus(string.Create(CultureInfo.InvariantCulture, $"{_draft.Round(sx):0.##} , {_draft.Round(sy):0.##} mm"));
        };

        AddHandler(KeyDownEvent, OnKey, RoutingStrategies.Tunnel);
        SetTool(_draft.Shapes.Count > 0 || _draft.Pins.Count > 0 ? SymbolTool.Select : SymbolTool.Line);
        var initialScene = _draft.ToScene();
        _canvas.Scene = initialScene;
        _canvas.GridArea = new Box(0, 0, initialScene.WidthMm, initialScene.HeightMm);
    }

    /// <summary>Projeye en az bir kez kaydedildi mi (ana pencere yenilenir).</summary>
    public bool Saved { get; private set; }

    /// <summary>Kütüphaneye en son kaydedilen sembolün kimliği (varsa).</summary>
    public SymbolId? LastSavedLibrarySymbolId { get; private set; }

    /// <summary>Kütüphaneye en az bir kez kaydedildi mi (palet yenilenir).</summary>
    public bool SavedToLibrary { get; private set; }

    /// <summary>Komut şeridi (görüntü aracı ve testler için).</summary>
    public RibbonView Ribbon => _ribbon;

    /// <summary>Şu anki çizim aracı (testler için).</summary>
    public SymbolTool CurrentTool => _draft.Tool;

    // Komutların eylemleri; şerit, Dosya menüsü, hızlı erişim ve kısayollar hep buradan çalışır.
    private Dictionary<string, Action> Actions()
    {
        var actions = new Dictionary<string, Action>
        {
            [SymbolEditorCommands.Delete] = () => Show(_draft.DeleteSelected()),
            [SymbolEditorCommands.Deselect] = () =>
            {
                _draft.ClearSelection();
                SetTool(SymbolTool.Select);
            },
            [SymbolEditorCommands.Undo] = () =>
            {
                _draft.Undo();
                Show(Result.Success());
            },
            [SymbolEditorCommands.Redo] = () =>
            {
                _draft.Redo();
                Show(Result.Success());
            },
            [SymbolEditorCommands.TurnPin] = () => Show(_draft.TurnSelectedPin()),
            [SymbolEditorCommands.FirstPin] = () => Show(_draft.MakeSelectedPinFirst()),
            [SymbolEditorCommands.SaveToProject] = SaveToProject,
            [SymbolEditorCommands.SaveToFile] = () => _ = SaveToFile(),
            [SymbolEditorCommands.SaveToLibrary] = SaveToLibrary,
            [SymbolEditorCommands.AddPinsOnSurface] = () =>
            {
                if (!int.TryParse(_surfacePins?.Text, out var count))
                {
                    Show(Result.Failure(new Error("sembol.uc_sayisi", "Uç sayısı bir sayı olmalı.")));
                    return;
                }

                Show(_draft.AddPinsOnSelectedSurface(count));
            },
            [SymbolEditorCommands.Close] = Close,
        };
        foreach (var (command, tool) in SymbolEditorCommands.Tools)
        {
            actions[command] = () => SetTool(tool);
        }

        foreach (var (command, x, y) in SymbolEditorCommands.Directions)
        {
            actions[command] = () =>
            {
                _draft.PinDirection = (x, y);
                _ribbon.Refresh();
            };
        }

        return actions;
    }

    // Komutların ne zaman yapılabileceği.
    private bool CanRun(string id) => id switch
    {
        SymbolEditorCommands.Delete or SymbolEditorCommands.Deselect => _draft.Selected is not null,
        SymbolEditorCommands.TurnPin or SymbolEditorCommands.FirstPin => _draft.Selected is { IsPin: true },
        SymbolEditorCommands.AddPinsOnSurface => _draft.SelectedIsSurface,
        SymbolEditorCommands.Undo => _draft.CanUndo,
        SymbolEditorCommands.Redo => _draft.CanRedo,
        _ => true,
    };

    private bool IsChecked(string id) =>
        SymbolEditorCommands.Tools.Any(tool => tool.Command == id && tool.Tool == _draft.Tool)
        || SymbolEditorCommands.Directions.Any(direction => direction.Command == id && (direction.X, direction.Y) == _draft.PinDirection);

    private (double X, double Y) ToDraft(double x, double y) =>
        (x - _draft.SceneOriginX, y - _draft.SceneOriginY);

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private bool Pressed(double x, double y)
    {
        var (dx, dy) = ToDraft(x, y);
        _press = (dx, dy);
        if (_draft.Tool == SymbolTool.Select)
        {
            _draft.SelectAt(dx, dy);
            Redraw();
            return _draft.Selected is not null;
        }

        return true;
    }

    private void Dragged(double dx, double dy)
    {
        if (_press is not { } start || _draft.Tool is SymbolTool.Text or SymbolTool.Pin)
        {
            return;
        }

        var (cx, cy) = (_draft.SceneOriginX, _draft.SceneOriginY);
        var (x1, y1) = (_draft.Round(start.X), _draft.Round(start.Y));
        var (x2, y2) = (_draft.Round(start.X + dx), _draft.Round(start.Y + dy));
        if (_draft.Tool == SymbolTool.Select && _draft.SelectedBox() is { } box)
        {
            _canvas.Ghost = box with { X = box.X + (x2 - x1), Y = box.Y + (y2 - y1) };
            return;
        }

        if (_draft.Tool == SymbolTool.Circle)
        {
            var r = Math.Sqrt(((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1)));
            _canvas.Ghost = new Box(cx + x1 - r, cy + y1 - r, 2 * r, 2 * r);
            return;
        }

        _canvas.Ghost = new Box(cx + Math.Min(x1, x2), cy + Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    private void Released(double dx, double dy)
    {
        _canvas.Ghost = null;
        if (_press is not { } start)
        {
            return;
        }

        _press = null;
        Show(_draft.Gesture(start.X, start.Y, start.X + dx, start.Y + dy));
    }

    // Kısayollar komut tanımlarından gelir; metin kutusuna yazarken devreye girmez. Backspace de Delete gibi siler.
    private void OnKey(object? sender, KeyEventArgs e)
    {
        // Esc seçimi bırakır ve Seç aracına geçer; uç paneli ya da bir metin kutusu odaktayken de çalışır ve odağı çizim alanına verir.
        if (e.Key == Key.Escape)
        {
            _draft.ClearSelection();
            SetTool(SymbolTool.Select);
            _canvas.Focus();
            e.Handled = true;
            return;
        }

        if (e.Source is TextBox)
        {
            return;
        }

        if (e.Key == Key.Back && _ribbon.Run(SymbolEditorCommands.Delete))
        {
            e.Handled = true;
            return;
        }

        foreach (var command in SymbolEditorCommands.All.Where(command => command.Gesture is not null))
        {
            if (KeyGesture.Parse(command.Gesture!).Matches(e) && _ribbon.Run(command.Id))
            {
                e.Handled = true;
                return;
            }
        }
    }

    private void SetTool(SymbolTool tool)
    {
        _draft.Tool = tool;
        if (tool != SymbolTool.Select)
        {
            _draft.ClearSelection();
        }

        Redraw();
    }

    private void SaveToProject()
    {
        var definition = _draft.ToDefinition();
        if (definition.IsFailure)
        {
            Show(Result.Failure(definition.Error!));
            return;
        }

        var assignTo = _assign.IsChecked == true ? _function?.Id : null;
        var saved = _session.SaveSymbol(definition.Value, assignTo);
        if (saved.IsSuccess)
        {
            _draft.MarkSaved();
            Saved = true;
            _notice = assignTo is null ? "Projeye kaydedildi" : "Projeye kaydedildi ve function'a atandı";
            Redraw();
            return;
        }

        Show(saved);
    }

    private void SaveToLibrary()
    {
        var definition = _draft.ToDefinition();
        if (definition.IsFailure)
        {
            Show(Result.Failure(definition.Error!));
            return;
        }

        var saved = _session.SaveSymbolToLibrary(definition.Value, _libraryOverwrite);
        if (saved.IsFailure)
        {
            Show(Result.Failure(saved.Error!));
            return;
        }

        // Asıl sembolün kopyası kaydedildiyse sonraki kayıtlar o kopyayı günceller.
        _draft.AdoptId(saved.Value);
        _libraryOverwrite = true;
        _draft.MarkSaved();
        LastSavedLibrarySymbolId = saved.Value;
        SavedToLibrary = true;
        _notice = "Sembol kütüphaneye kaydedildi";
        Redraw();
    }

    private async Task SaveToFile()
    {
        var definition = _draft.ToDefinition();
        if (definition.IsFailure)
        {
            Show(Result.Failure(definition.Error!));
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Sembolü dosyaya kaydet",
            SuggestedFileName = $"{definition.Value.Name}.sembol.json",
            FileTypeChoices = [new FilePickerFileType("Sembol") { Patterns = ["*.sembol.json"] }],
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            var saved = SymbolJson.Save(definition.Value, path);
            _notice = saved.IsSuccess ? $"Yazıldı: {path}" : saved.Error!.Message;
            Redraw();
        }
    }

    private void Show(Result result)
    {
        _notice = result.IsFailure ? result.Error!.Message : null;
        Redraw();
    }

    // Panel alanlarındaki değişikliği seçili uca yazar.
    private void ApplyPinFields()
    {
        if (_syncing || _draft.SelectedPin is null)
        {
            return;
        }

        var attributes = new PointAttributes(
            PointAttributeLabels.Selected(_pinKind, PointAttributeLabels.Kinds),
            PointAttributeLabels.Selected(_pinConnector, PointAttributeLabels.Connectors),
            PointAttributeLabels.Selected(_pinContact, PointAttributeLabels.Contacts),
            _pinCode.Text);
        var result = _draft.SetSelectedPin(_pinName.Text, attributes);
        _notice = result.IsFailure ? result.Error!.Message : null;
        UpdateCanvasScene();
        ShowStatus(null);
        _ribbon.Refresh();
    }

    // Panel alanlarındaki değişikliği seçili yazıya yazar.
    private void ApplyTextFields()
    {
        if (_syncing || _draft.SelectedText is not { } current)
        {
            return;
        }

        var rawSize = (_editTextSize.Text ?? string.Empty).Trim().Replace(',', '.');
        var rawRotation = (_editTextRotation.Text ?? string.Empty).Trim().Replace(',', '.');
        if (!double.TryParse(rawSize, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
        {
            _notice = "Yazı boyutu için geçerli bir sayı girin (örn. 3.5).";
            ShowStatus(null);
            return;
        }

        var rotation = current.RotationDeg;
        if (rawRotation.Length == 0)
        {
            rotation = 0;
        }
        else if (!double.TryParse(rawRotation, NumberStyles.Float, CultureInfo.InvariantCulture, out rotation))
        {
            return;
        }

        var align = _editTextAlign.SelectedIndex switch
        {
            1 => TextAlign.Center,
            2 => TextAlign.Right,
            _ => TextAlign.Left,
        };

        var result = _draft.SetSelectedText(_editText.Text, size, align, rotation);
        _notice = result.IsFailure ? result.Error!.Message : null;
        UpdateCanvasScene();
        ShowStatus(null);
        _ribbon.Refresh();
    }

    private async Task DoubleClickedAsync(double x, double y)
    {
        var (dx, dy) = ToDraft(x, y);
        _draft.Tool = SymbolTool.Select;
        _draft.SelectAt(dx, dy);
        Redraw();

        if (_draft.SelectedText is { } text)
        {
            var edited = await new TextPromptDialog("Yazıyı düzenle", text.Text).ShowDialog<string?>(this);
            if (edited is not null)
            {
                Show(_draft.SetSelectedText(edited, text.SizeMm, text.Align, text.RotationDeg));
            }
        }
        else if (_draft.SelectedPin is { } pin)
        {
            var edited = await new TextPromptDialog("Uç adını düzenle", pin.Name ?? string.Empty).ShowDialog<string?>(this);
            if (edited is not null)
            {
                Show(_draft.SetSelectedPin(edited, pin.Attributes));
            }
        }
    }

    // Seçili uç veya yazının değerlerini sağ panele yazar.
    private void SyncInspectorFields()
    {
        _syncing = true;
        var pin = _draft.SelectedPin;
        var text = _draft.SelectedText;

        if (_textPanel is not null && text is not null)
        {
            _textPanel.IsVisible = true;
            _textPanel.IsEnabled = true;
            if (_pinPanel is not null)
            {
                _pinPanel.IsVisible = false;
            }

            _editText.Text = text.Text;
            _editTextSize.Text = text.SizeMm.ToString("0.##", CultureInfo.InvariantCulture);
            _editTextAlign.SelectedIndex = text.Align switch
            {
                TextAlign.Center => 1,
                TextAlign.Right => 2,
                _ => 0,
            };
            _editTextRotation.Text = text.RotationDeg.ToString("0.##", CultureInfo.InvariantCulture);
            _syncing = false;
            return;
        }

        if (_textPanel is not null)
        {
            _textPanel.IsVisible = false;
        }

        if (_pinPanel is not null)
        {
            _pinPanel.IsVisible = true;
            _pinPanel.IsEnabled = pin is not null;
        }

        _pinName.Text = pin?.Name;
        _pinCode.Text = pin?.Attributes.ConnectorCode;
        _pinKind.SelectedIndex = Math.Max(0, PointAttributeLabels.Kinds.ToList().FindIndex(item => item.Value == (pin?.Attributes.Kind ?? default)));
        _pinConnector.SelectedIndex = Math.Max(0, PointAttributeLabels.Connectors.ToList().FindIndex(item => item.Value == (pin?.Attributes.Connector ?? default)));
        _pinContact.SelectedIndex = Math.Max(0, PointAttributeLabels.Contacts.ToList().FindIndex(item => item.Value == (pin?.Attributes.Contact ?? default)));
        _syncing = false;
    }

    private void UpdateCanvasScene()
    {
        var scene = _draft.ToScene();
        _canvas.UpdateScene(scene);
        _canvas.GridArea = new Box(0, 0, scene.WidthMm, scene.HeightMm);
        _canvas.Highlight = _draft.SelectedBox();
    }

    private void Redraw()
    {
        SyncInspectorFields();
        UpdateCanvasScene();
        ShowStatus(null);
        _ribbon.Refresh();
    }

    private void ShowStatus(string? pointer)
    {
        var summary = $"{_draft.Shapes.Count} öğe  ·  {_draft.Pins.Count} uç"
            + (_function is null ? string.Empty : $"  ·  function {_function.ConnectionPoints.Count} uç bekliyor")
            + (_draft.IsDirty ? "  ·  kaydedilmedi" : string.Empty);
        _status.Text = string.Join("  ·  ", new[] { _notice, summary, pointer }.Where(part => !string.IsNullOrEmpty(part)));
    }
}
