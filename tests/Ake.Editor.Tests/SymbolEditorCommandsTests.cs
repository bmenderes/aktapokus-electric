using Avalonia.Input;

namespace Ake.Editor.Tests;

/// <summary>Blok editörünün komut kaydı ve şeridi: tutarlı, tekil ve her komut erişilebilir.</summary>
public class SymbolEditorCommandsTests
{
    private static IEnumerable<string> RibbonIds(RibbonDefinition definition) =>
        definition.Tabs.SelectMany(tab => tab.Groups).SelectMany(group => group.Items).Select(item => item.CommandId);

    [Fact]
    public void Command_ids_labels_and_shortcuts_are_unique_and_shortcuts_parse()
    {
        var all = SymbolEditorCommands.All;

        Assert.Equal(all.Count, all.Select(command => command.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(command => command.Label).Distinct().Count());
        var gestures = all.Where(command => command.Gesture is not null).Select(command => KeyGesture.Parse(command.Gesture!).ToString()).ToList();
        Assert.Equal(gestures.Count, gestures.Distinct().Count());
    }

    [Fact]
    public void Ribbon_quick_access_and_file_menu_refer_only_to_defined_commands()
    {
        var definition = SymbolEditorCommands.Ribbon;
        var used = RibbonIds(definition).Concat(definition.QuickAccess).Concat(definition.FileMenu).ToList();

        Assert.All(used, id => Assert.NotNull(definition.Get(id)));
        Assert.Throws<ArgumentException>(() => definition.Get("yok.komut"));
    }

    [Fact]
    public void Every_command_can_be_reached_from_the_ribbon_the_file_menu_or_a_shortcut()
    {
        var definition = SymbolEditorCommands.Ribbon;
        var reachable = RibbonIds(definition).Concat(definition.FileMenu).ToHashSet();

        Assert.All(SymbolEditorCommands.All, command => Assert.True(
            reachable.Contains(command.Id) || command.Gesture is not null, $"{command.Id} hiçbir yerden çalıştırılamıyor."));
    }

    [Fact]
    public void Tools_and_pin_directions_cover_every_toggle_and_every_tool()
    {
        var toggles = SymbolEditorCommands.All.Where(command => command.Toggle).Select(command => command.Id).Order().ToList();
        var mapped = SymbolEditorCommands.Tools.Select(tool => tool.Command).Concat(SymbolEditorCommands.Directions.Select(direction => direction.Command)).Order().ToList();

        Assert.Equal(toggles, mapped);
        Assert.Equal(Enum.GetValues<SymbolTool>().Length, SymbolEditorCommands.Tools.Select(tool => tool.Tool).Distinct().Count());
        Assert.Equal(4, SymbolEditorCommands.Directions.Select(direction => (direction.X, direction.Y)).Distinct().Count());
        Assert.All(SymbolEditorCommands.Directions, direction => Assert.Equal(1, Math.Abs(direction.X) + Math.Abs(direction.Y)));
    }

    [Fact]
    public void Tool_shortcuts_keep_the_single_letters_the_editor_always_had()
    {
        Assert.Equal(
            ["S", "L", "D", "C", "T", "U"],
            SymbolEditorCommands.Tools.Select(tool => SymbolEditorCommands.All.Single(command => command.Id == tool.Command).Gesture));
        Assert.Equal("Ctrl+Z", SymbolEditorCommands.All.Single(command => command.Id == SymbolEditorCommands.Undo).Gesture);
        Assert.Equal("Ctrl+S", SymbolEditorCommands.All.Single(command => command.Id == SymbolEditorCommands.SaveToProject).Gesture);
    }

    [Fact]
    public void The_main_window_ribbon_definition_is_the_same_as_before()
    {
        var main = EditorCommands.Main;

        Assert.Same(EditorCommands.All, main.Commands);
        Assert.Same(EditorCommands.Ribbon, main.Tabs);
        Assert.Same(EditorCommands.FileMenu, main.FileMenu);
        Assert.Equal(EditorCommands.Get(EditorCommands.Save), main.Get(EditorCommands.Save));
    }
}
