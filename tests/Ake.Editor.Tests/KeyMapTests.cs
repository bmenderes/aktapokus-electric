namespace Ake.Editor.Tests;

/// <summary>Kısayol profili: varsayılan set, değiştirme, çakışma, JSON ve dosya (Faz 6.3, madde 14).</summary>
public sealed class KeyMapTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-keymap-");

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public void Default_set_comes_from_the_command_definitions_in_one_canonical_form_without_conflicts()
    {
        var map = KeyMap.Default();

        foreach (var command in EditorCommands.All)
        {
            Assert.Equal(command.Gesture, map.GestureOf(command.Id));
        }

        var bound = EditorCommands.All.Select(command => command.Gesture).Where(gesture => gesture is not null).ToList();
        Assert.Equal(bound.Count, bound.Distinct().Count());
        Assert.Equal(bound.Count, map.Bindings.Count);
        Assert.DoesNotContain(EditorCommands.All, command => map.IsCustom(command.Id));
    }

    [Fact]
    public void A_command_takes_a_new_shortcut_in_canonical_form_and_the_old_one_is_free_again()
    {
        var map = KeyMap.Default();

        Assert.True(map.Set(EditorCommands.Save, "ctrl+shift+k").IsSuccess);

        Assert.Equal("Ctrl+Shift+K", map.GestureOf(EditorCommands.Save));
        Assert.True(map.IsCustom(EditorCommands.Save));
        Assert.True(map.Set(EditorCommands.Open, "Ctrl+S").IsSuccess);
        Assert.Equal("Ctrl+S", map.GestureOf(EditorCommands.Open));
    }

    [Fact]
    public void A_shortcut_already_used_by_another_command_is_refused_and_nothing_changes()
    {
        var map = KeyMap.Default();

        var result = map.Set(EditorCommands.Open, "Ctrl+S");

        Assert.Equal("kisayol.cakisma", result.Error!.Code);
        Assert.Contains("Kaydet", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal("Ctrl+O", map.GestureOf(EditorCommands.Open));
        Assert.True(map.Set(EditorCommands.Save, "Ctrl+S").IsSuccess);
    }

    [Fact]
    public void Invalid_shortcuts_and_unknown_commands_are_refused_and_null_removes_a_shortcut()
    {
        var map = KeyMap.Default();

        Assert.Equal("kisayol.gecersiz", map.Set(EditorCommands.Save, "Ctrl+Hiçbirşey").Error!.Code);
        Assert.Equal("kisayol.komut_yok", map.Set("yok.komut", "Ctrl+Q").Error!.Code);
        Assert.Equal("Ctrl+S", map.GestureOf(EditorCommands.Save));

        Assert.True(map.Set(EditorCommands.Save, null).IsSuccess);
        Assert.Null(map.GestureOf(EditorCommands.Save));
        Assert.DoesNotContain(map.Bindings, binding => binding.Command == EditorCommands.Save);
    }

    [Fact]
    public void Reset_gives_the_default_back_even_when_another_command_took_it()
    {
        var map = KeyMap.Default();
        Assert.True(map.Set(EditorCommands.Save, null).IsSuccess);
        Assert.True(map.Set(EditorCommands.Open, "Ctrl+S").IsSuccess);

        map.Reset(EditorCommands.Save);

        Assert.Equal("Ctrl+S", map.GestureOf(EditorCommands.Save));
        Assert.Null(map.GestureOf(EditorCommands.Open));
        map.ResetAll();
        Assert.Equal("Ctrl+O", map.GestureOf(EditorCommands.Open));
        Assert.DoesNotContain(EditorCommands.All, command => map.IsCustom(command.Id));
    }

    [Fact]
    public void Json_holds_only_what_differs_from_the_default_and_reads_back_the_same()
    {
        var map = KeyMap.Default();
        Assert.True(map.Set(EditorCommands.Save, "Ctrl+Shift+K").IsSuccess);
        Assert.True(map.Set(EditorCommands.DrawWire, null).IsSuccess);

        var json = map.ToJson();
        var read = KeyMap.FromJson(json).Value;

        Assert.Contains("\"proje.kaydet\": \"Ctrl+Shift+K\"", json, StringComparison.Ordinal);
        Assert.Contains("\"baglanti.tel_cek\": null", json, StringComparison.Ordinal);
        Assert.DoesNotContain("proje.ac", json, StringComparison.Ordinal);
        Assert.Equal(map.ToJson(full: true), read.ToJson(full: true));
        Assert.Equal("Ctrl+O", read.GestureOf(EditorCommands.Open));
        Assert.Equal(KeyMap.Default().ToJson(), KeyMap.Default().ToJson());
    }

    [Fact]
    public void A_swap_between_two_commands_in_the_file_is_not_a_conflict()
    {
        var json = """{ "surum": 1, "kisayollar": { "proje.kaydet": "Ctrl+O", "proje.ac": "Ctrl+S" } }""";

        var map = KeyMap.FromJson(json).Value;

        Assert.Equal("Ctrl+O", map.GestureOf(EditorCommands.Save));
        Assert.Equal("Ctrl+S", map.GestureOf(EditorCommands.Open));
    }

    [Theory]
    [InlineData("""{ "surum": 1, "kisayollar": { "yok.komut": "Ctrl+Q" } }""", "kisayol.komut_yok")]
    [InlineData("""{ "surum": 1, "kisayollar": { "proje.kaydet": "Ctrl+Hiçbirşey" } }""", "kisayol.gecersiz")]
    [InlineData("""{ "surum": 1, "kisayollar": { "proje.kaydet": "Ctrl+X" } }""", "kisayol.cakisma")]
    [InlineData("""{ "surum": 1, "kisayollar": { "proje.kaydet": 5 } }""", "kisayol.gecersiz")]
    [InlineData("""{ "surum": 2, "kisayollar": {} }""", "kisayol.surum")]
    [InlineData("""{ "surum": 1 }""", "kisayol.json_gecersiz")]
    [InlineData("bu json değil", "kisayol.json_gecersiz")]
    public void A_broken_file_is_refused_as_a_whole(string json, string code) =>
        Assert.Equal(code, KeyMap.FromJson(json).Error!.Code);

    [Fact]
    public void Missing_file_gives_the_default_and_save_then_load_returns_the_same_profile()
    {
        var path = Path.Combine(_directory.FullName, "alt klasör", "kisayollar.json");
        Assert.Equal(KeyMap.Default().ToJson(full: true), KeyMap.Load(path).Value.ToJson(full: true));

        var map = KeyMap.Default();
        Assert.True(map.Set(EditorCommands.Undo, "Ctrl+Shift+Z").IsSuccess);
        Assert.True(map.Save(path).IsSuccess);

        Assert.Equal(map.ToJson(full: true), KeyMap.Load(path).Value.ToJson(full: true));
        File.WriteAllText(path, "{ bozuk");
        Assert.Equal("kisayol.json_gecersiz", KeyMap.Load(path).Error!.Code);
    }

    [Fact]
    public void Clone_is_independent_of_the_original()
    {
        var map = KeyMap.Default();
        var copy = map.Clone();

        Assert.True(copy.Set(EditorCommands.Save, "Ctrl+Shift+K").IsSuccess);

        Assert.Equal("Ctrl+S", map.GestureOf(EditorCommands.Save));
    }

    [Fact]
    public void The_documented_default_profile_matches_the_code()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var documented = File.ReadAllText(Path.Combine(root!.FullName, "docs", "kisayollar.varsayilan.json"));

        Assert.Equal(KeyMap.Default().ToJson(full: true).ReplaceLineEndings(), documented.ReplaceLineEndings());
    }
}
