using Avalonia.Input;

namespace Ake.Editor.Tests;

/// <summary>Komut kaydı ve şerit düzeni (ADR-0023): tutarlı, tekil ve her komut erişilebilir.</summary>
public class EditorCommandsTests
{
    [Fact]
    public void Command_ids_and_labels_are_unique()
    {
        Assert.Equal(EditorCommands.All.Count, EditorCommands.All.Select(command => command.Id).Distinct().Count());
        Assert.Equal(EditorCommands.All.Count, EditorCommands.All.Select(command => command.Label).Distinct().Count());
    }

    [Fact]
    public void Ribbon_quick_access_and_file_menu_refer_only_to_defined_commands()
    {
        var used = EditorCommands.Ribbon
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Items)
            .Select(item => item.CommandId)
            .Concat(EditorCommands.QuickAccess)
            .Concat(EditorCommands.FileMenu)
            .ToList();

        Assert.All(used, id => Assert.NotNull(EditorCommands.Get(id)));
    }

    [Fact]
    public void Every_command_can_be_reached_from_the_ribbon_the_file_menu_or_a_shortcut()
    {
        var reachable = EditorCommands.Ribbon
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Items)
            .Select(item => item.CommandId)
            .Concat(EditorCommands.FileMenu)
            .ToHashSet();

        Assert.All(EditorCommands.All, command => Assert.True(
            reachable.Contains(command.Id) || command.Gesture is not null, $"{command.Id} hiçbir yerden çalıştırılamıyor."));
    }

    [Fact]
    public void Shortcuts_parse_and_no_two_commands_share_one()
    {
        var gestures = EditorCommands.All
            .Where(command => command.Gesture is not null)
            .Select(command => KeyGesture.Parse(command.Gesture!))
            .ToList();

        Assert.Equal(gestures.Count, gestures.Distinct().Count());
        Assert.Contains(KeyGesture.Parse("Ctrl+S"), gestures);
    }

    [Fact]
    public void Ribbon_has_the_agreed_tabs_and_no_empty_group()
    {
        Assert.Equal(
            ["Başlangıç", "Ekle", "Bağlantı", "Sayfa", "Görünüm", "Araçlar"],
            EditorCommands.Ribbon.Select(tab => tab.Label));
        Assert.All(EditorCommands.Ribbon.SelectMany(tab => tab.Groups), group => Assert.NotEmpty(group.Items));
    }

    [Fact]
    public void Unknown_command_is_a_programming_error()
    {
        Assert.Throws<ArgumentException>(() => EditorCommands.Get("yok"));
    }
}
