using Ake.Ai;
using Ake.Core;
using Ake.Core.Commands;

namespace Ake.Editor.Tests;

/// <summary>Yapay zeka ile değişiklik, oturum tarafı (ADR-0044): öneri projeyi değiştirmez, onaylanınca tek adımda uygulanır ve geri alınır.</summary>
public sealed class AiChangeTests : IDisposable
{
    private const string Reply = """
        { "description": "K7 ekle", "commands": [
          { "op": "addDevice", "tag": "=M1+P1-K7", "functions": [ { "kind": "Coil", "connectionPoints": ["A1", "A2"] } ] },
          { "op": "connect", "from": "=M1+P1-K7:A2", "to": "-T1:-" }
        ] }
        """;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-ai-");
    private readonly EditorSession _session = new("deneme");

    public AiChangeTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
        _session.ChatProviderFactory = () => new Scripted(Reply);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    [Fact]
    public async Task A_proposal_changes_nothing_until_it_is_applied_then_applies_as_one_undoable_ai_step()
    {
        var project = _session.Project!;
        var (devices, connections) = (project.Devices.Count, project.Connections.Count);

        var proposal = await _session.ProposeAiChangeAsync("K7 ekle");

        Assert.True(proposal.IsSuccess, proposal.Error?.Message);
        Assert.Equal((devices, connections), (project.Devices.Count, project.Connections.Count));
        Assert.False(_session.CanUndo);
        Assert.Equal(["Cihaz ekle: =M1+P1-K7", "Bağla: =M1+P1-K7:A2 → -T1:-"], PatchSummary.Describe(proposal.Value.PatchJson));

        Assert.True(_session.ApplyAiProposal(proposal.Value).IsSuccess);
        Assert.Equal((devices + 1, connections + 1), (project.Devices.Count, project.Connections.Count));
        Assert.True(_session.IsDirty);

        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal((devices, connections), (project.Devices.Count, project.Connections.Count));
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public async Task An_unreachable_model_is_reported_and_leaves_the_project_alone()
    {
        _session.ChatProviderFactory = () => new Scripted();

        var proposal = await _session.ProposeAiChangeAsync("bir şey");

        Assert.Equal("ai.sunucuya_ulasilamadi", proposal.Error!.Code);
        Assert.False(_session.CanUndo);
    }

    [Fact]
    public async Task Without_a_project_there_is_nothing_to_propose()
    {
        var empty = new EditorSession("deneme");

        Assert.Equal("editor.proje_yok", (await empty.ProposeAiChangeAsync("x")).Error!.Code);
        Assert.Equal("editor.proje_yok", empty.ApplyAiProposal((await _session.ProposeAiChangeAsync("K7 ekle")).Value).Error!.Code);
    }

    [Fact]
    public void The_chosen_model_is_checked_and_kept_in_the_user_profile_file()
    {
        var path = Path.Combine(_directory.FullName, "yapay-zeka.json");
        _session.UseAiSettingsFile(path);
        Assert.Equal(AiSettings.Default, _session.AiSettings);

        var chosen = new AiSettings(AiSettings.Ollama, "http://127.0.0.1:11434", "deepseek-r1:8b");
        Assert.True(_session.SetAiSettings(chosen).IsSuccess);
        Assert.Equal(chosen, AiSettings.Load(path));

        var other = new EditorSession("baska");
        other.UseAiSettingsFile(path);
        Assert.Equal(chosen, other.AiSettings);

        Assert.Equal("ai.model_bos", _session.SetAiSettings(chosen with { Model = " " }).Error!.Code);
        Assert.Equal(chosen, _session.AiSettings);
    }

    [Fact]
    public async Task A_proposal_request_with_settings_selects_and_keeps_that_model()
    {
        var chosen = new AiSettings(AiSettings.Ollama, "http://127.0.0.1:1", "olmayan-model");
        _session.ChatProviderFactory = null;

        var proposal = await _session.ProposeAiChangeAsync("K7 ekle", chosen);

        Assert.Equal("ai.sunucuya_ulasilamadi", proposal.Error!.Code);
        Assert.Equal(chosen, _session.AiSettings);
        Assert.Equal("ai.model_bos", (await _session.ProposeAiChangeAsync("x", chosen with { Model = "" })).Error!.Code);
    }

    [Fact]
    public void The_ribbon_offers_the_ai_command_in_the_tools_tab()
    {
        var group = EditorCommands.Main.Tabs.Single(tab => tab.Label == "Araçlar").Groups.Single(group => group.Label == "Yapay zeka");

        Assert.Contains(group.Items, item => item.CommandId == EditorCommands.AiChange);
    }

    private sealed class Scripted(params string[] replies) : IChatProvider
    {
        private int _next;

        public Task<Result<string>> CompleteAsync(ChatRequest request, CancellationToken cancellation = default) =>
            Task.FromResult(_next < replies.Length
                ? Result.Success(replies[_next++])
                : Result.Failure<string>(new Error("ai.sunucuya_ulasilamadi", "sunucu yok")));
    }

    [Fact]
    public void A_cover_a_contents_page_and_a_report_page_are_added_as_generated_pages_in_one_undoable_step()
    {
        var project = _session.Project!;
        var count = project.Pages.Count;

        var cover = _session.AddPages(Ake.Core.Model.PageKind.Cover, "Kapak", afterIndex: null);
        Assert.True(cover.IsSuccess, cover.Error?.Message);
        Assert.Equal(count, cover.Value);
        Assert.Equal((Ake.Core.Model.PageKind.Cover, "6", "Kapak"), (project.Pages[^1].Kind, project.Pages[^1].Number, project.Pages[^1].Title));
        Assert.Empty(_session.SceneFor(count, new DateOnly(2026, 11, 19)).Value.Warnings);

        var parts = _session.AddPages(Ake.Core.Model.PageKind.PartsList, "Malzeme listesi", afterIndex: 0);
        Assert.True(parts.IsSuccess, parts.Error?.Message);
        Assert.Equal(1, parts.Value);
        Assert.Equal("1.1", project.Pages[1].Number); // etkin sayfadan sonra alt numara
        Assert.Equal(Ake.Core.Model.PageKind.PartsList, project.Pages[1].Kind);

        Assert.True(_session.Undo().IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.Equal(count, project.Pages.Count);
    }

    [Fact]
    public void A_long_table_adds_continuation_pages_with_sub_numbers_and_a_report_page_needs_a_template()
    {
        var project = _session.Project!;
        // Çok satırlı içindekiler: sayfa sayısını tek sayfaya sığmayacak kadar artır.
        var rows = Ake.Render.SheetRenderer.RowsPerPage(project.SheetTemplates[0]);
        while (project.Pages.Count <= rows + 2)
        {
            Assert.True(_session.AppendPage().IsSuccess);
        }

        var added = _session.AddPages(Ake.Core.Model.PageKind.Contents, "İçindekiler", afterIndex: null);

        Assert.True(added.IsSuccess, added.Error?.Message);
        var contents = project.Pages.Where(page => page.Kind == Ake.Core.Model.PageKind.Contents).ToList();
        Assert.Equal(2, contents.Count);
        Assert.Equal([0, 1], contents.Select(page => page.Part));
        Assert.EndsWith(".1", contents[1].Number, StringComparison.Ordinal);
        Assert.Empty(_session.SceneFor(project.Pages.ToList().IndexOf(contents[0]), new DateOnly(2026, 11, 19)).Value.Warnings);

        Assert.True(_session.Undo().IsSuccess); // iki sayfa tek adımda geri alınır
        Assert.DoesNotContain(project.Pages, page => page.Kind == Ake.Core.Model.PageKind.Contents);
    }
}
