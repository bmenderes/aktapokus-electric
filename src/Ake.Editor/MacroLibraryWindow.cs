using Ake.Core.Macros;
using Ake.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace Ake.Editor;

/// <summary>Hazır makro kütüphanesinden veya dosyadan seçilen makro ve değer kümesi (ADR-0058).</summary>
public sealed record MacroSelectionResult(Macro Macro, string? ValueSetName);

/// <summary>
/// Hazır makro kütüphanesini değer kümeleriyle birlikte sunan seçim penceresi (ADR-0058).
/// Kullanıcı ayrıca tek bir <c>.makro.json</c> dosyası da seçebilir.
/// </summary>
public sealed class MacroLibraryWindow : Window
{
    private readonly IReadOnlyList<Macro> _macros;
    private readonly ListBox _macroList;
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8 };
    private readonly ComboBox _valueSetCombo = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _valuePreview = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        Height = 110,
        FontFamily = new FontFamily("Consolas, monospace"),
    };

    public MacroLibraryWindow(IReadOnlyList<Macro> macros)
    {
        ArgumentNullException.ThrowIfNull(macros);
        _macros = macros;

        Title = "Makro Kütüphanesi";
        Width = 620;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _macroList = new ListBox
        {
            ItemsSource = _macros.Select(m => m.Name).ToList(),
            Width = 210,
        };
        _macroList.SelectionChanged += (_, _) => OnMacroSelected();
        _valueSetCombo.SelectionChanged += (_, _) => UpdateValuePreview();

        var fileButton = new Button { Content = "Dosyadan aç… (*.makro.json)" };
        fileButton.Click += async (_, _) => await BrowseMacroFileAsync();

        var insertButton = new Button { Content = "Yerleştir", Classes = { "accent" }, IsDefault = true };
        insertButton.Click += (_, _) => CloseSelection();

        var cancelButton = new Button { Content = "Vazgeç", IsCancel = true };
        cancelButton.Click += (_, _) => Close(null);

        var detailPanel = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "Açıklama", FontWeight = FontWeight.SemiBold },
                _description,
                new TextBlock { Text = "Değer kümesi", FontWeight = FontWeight.SemiBold },
                _valueSetCombo,
                new TextBlock { Text = "Değer kümesi atamaları", FontWeight = FontWeight.SemiBold },
                _valuePreview,
            },
        };

        var split = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("220,16,*"),
        };
        Grid.SetColumn(_macroList, 0);
        Grid.SetColumn(detailPanel, 2);
        split.Children.AddRange([_macroList, detailPanel]);

        var buttons = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(fileButton, Avalonia.Controls.Dock.Left);
        var rightButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { cancelButton, insertButton } };
        DockPanel.SetDock(rightButtons, Avalonia.Controls.Dock.Right);
        buttons.Children.AddRange([fileButton, rightButtons]);

        var root = new DockPanel { Margin = new Thickness(16), LastChildFill = true };
        DockPanel.SetDock(buttons, Avalonia.Controls.Dock.Bottom);
        root.Children.AddRange([buttons, split]);
        Content = root;

        if (_macros.Count > 0)
        {
            _macroList.SelectedIndex = 0;
        }
    }

    private void OnMacroSelected()
    {
        var macro = SelectedMacro();
        if (macro is null)
        {
            _description.Text = string.Empty;
            _valueSetCombo.ItemsSource = Array.Empty<string>();
            _valuePreview.Text = string.Empty;
            return;
        }

        var desc = string.IsNullOrWhiteSpace(macro.Description) ? macro.Name : macro.Description;
        _description.Text = $"{desc} ({macro.Devices.Count} cihaz, {macro.Connections.Count} bağlantı)";

        var names = new List<string> { "(Varsayılan değerler)" };
        if (macro.ValueSets is not null)
        {
            names.AddRange(macro.ValueSets.Select(v => v.Name));
        }

        _valueSetCombo.ItemsSource = names;
        _valueSetCombo.SelectedIndex = macro.ValueSets is { Count: > 0 } ? 1 : 0;
        UpdateValuePreview();
    }

    private void UpdateValuePreview()
    {
        var macro = SelectedMacro();
        if (macro is null || _valueSetCombo.SelectedIndex <= 0 || macro.ValueSets is null)
        {
            _valuePreview.Text = "(Değer kümesi seçilmedi — makro kendi varsayılanlarıyla yerleşir.)";
            return;
        }

        var idx = _valueSetCombo.SelectedIndex - 1;
        if (idx < 0 || idx >= macro.ValueSets.Count)
        {
            _valuePreview.Text = string.Empty;
            return;
        }

        var set = macro.ValueSets[idx];
        _valuePreview.Text = string.Join(
            Environment.NewLine,
            set.Values.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key} = {kv.Value}"));
    }

    private Macro? SelectedMacro() =>
        _macroList.SelectedIndex >= 0 && _macroList.SelectedIndex < _macros.Count ? _macros[_macroList.SelectedIndex] : null;

    private void CloseSelection()
    {
        var macro = SelectedMacro();
        if (macro is null)
        {
            return;
        }

        string? valueSetName = _valueSetCombo.SelectedIndex > 0 && macro.ValueSets is { } sets && _valueSetCombo.SelectedIndex - 1 < sets.Count
            ? sets[_valueSetCombo.SelectedIndex - 1].Name
            : null;
        Close(new MacroSelectionResult(macro, valueSetName));
    }

    private async Task BrowseMacroFileAsync()
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null)
        {
            return;
        }

        var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Makro dosyası aç",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Makro dosyaları") { Patterns = ["*.makro.json", "*.json"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0 || files[0].TryGetLocalPath() is not { Length: > 0 } path)
        {
            return;
        }

        var read = MacroJson.Load(path);
        if (read.IsSuccess)
        {
            Close(new MacroSelectionResult(read.Value, read.Value.ValueSets?.FirstOrDefault()?.Name));
        }
        else
        {
            _description.Text = $"Dosya okunamadı: {read.Error?.Message}";
        }
    }
}
