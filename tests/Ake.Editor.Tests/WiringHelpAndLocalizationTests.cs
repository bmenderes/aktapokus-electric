using Ake.Core.Model;
using Ake.Render;

namespace Ake.Editor.Tests;

/// <summary>
/// Kablo renk kartelası, çizgi biçimi (düz/kesik/çizgi-nokta/noktalı), hızlı PLC ekleme + CSV,
/// görselli/aranabilir Yardım kılavuzu ve TR/EN arayüz çevirileri testleri.
/// </summary>
public sealed class WiringHelpAndLocalizationTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 11, 23);

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-wiring-help-");

    public void Dispose() => _directory.Delete(recursive: true);

    private string CopyRefMachine()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        return path;
    }

    [Fact]
    public void AssignConnectionAppearance_applies_color_and_dashed_line_style_and_undoes_cleanly()
    {
        var session = new EditorSession("deneme");
        Assert.True(session.Open(CopyRefMachine()).IsSuccess);

        var (pageIndex, connectionId) = Enumerable.Range(0, session.Project!.Pages.Count)
            .SelectMany(i => session.WiresFor(i).Select(w => (i, w.Connection)))
            .First();
        var connection = session.Project.FindConnection(connectionId)!;
        var previousWire = connection.Wire;
        var previousKind = connection.KindId;

        var page = session.Project.Pages[pageIndex];
        var applied = session.AssignConnectionAppearance(
            connection.Id,
            "W99",
            2.5m,
            "RD",
            coreId: null,
            lineStyle: LineStyle.Dashed);
        Assert.True(applied.IsSuccess);

        var updated = session.Project.FindConnection(connection.Id)!;
        Assert.Equal("W99", updated.Wire?.Number);
        Assert.Equal(2.5m, updated.Wire?.CrossSectionMm2);
        Assert.Equal("RD", updated.Wire?.Color);
        Assert.NotNull(updated.KindId);
        var kind = session.Project.FindConnectionKind(updated.KindId!.Value)!;
        Assert.Equal("#D62828", kind.Color);
        Assert.Equal(LineStyle.Dashed, kind.Style);

        var scene = SheetRenderer.Render(session.Project, page, Today);
        Assert.True(scene.IsSuccess);
        Assert.Contains(scene.Value.Items.OfType<LineItem>(), l =>
            l.Style == LineStyle.Dashed && string.Equals(l.Color, "#D62828", StringComparison.OrdinalIgnoreCase));

        Assert.True(session.Undo().IsSuccess);
        var reverted = session.Project.FindConnection(connection.Id)!;
        Assert.Equal(previousWire, reverted.Wire);
        Assert.Equal(previousKind, reverted.KindId);
    }

    [Fact]
    public void Cable_core_color_renders_wire_in_matching_color()
    {
        var session = new EditorSession("deneme");
        Assert.True(session.Open(CopyRefMachine()).IsSuccess);

        var addedCable = session.AddCableItem("W77", "2x1.5", [("1", 1.5m, "BU")]);
        Assert.True(addedCable.IsSuccess);

        var cable = session.Project!.Cables.Single(c => c.Id == addedCable.Value);
        var core = cable.Cores[0];
        var (pageIndex, connectionId) = Enumerable.Range(0, session.Project.Pages.Count)
            .SelectMany(i => session.WiresFor(i).Select(w => (i, w.Connection)))
            .First();
        var connection = session.Project.FindConnection(connectionId)!;
        var page = session.Project.Pages[pageIndex];

        var applied = session.AssignConnectionAppearance(
            connection.Id,
            wireNumber: null,
            crossSectionMm2: null,
            color: null,
            coreId: core.Id,
            lineStyle: LineStyle.Solid);
        Assert.True(applied.IsSuccess);

        var updated = session.Project.FindConnection(connection.Id)!;
        Assert.Equal(core.Id, updated.CoreId);
        Assert.Null(updated.Wire);
        Assert.Null(updated.KindId);

        var scene = SheetRenderer.Render(session.Project, page, Today);
        Assert.True(scene.IsSuccess);
        Assert.Contains(scene.Value.Items.OfType<LineItem>(), l =>
            l.Style == LineStyle.Solid && string.Equals(l.Color, "#1F4FD8", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Plc_quick_add_and_csv_import_work_on_empty_project()
    {
        var sessionQuick = new EditorSession("deneme");
        Assert.True(sessionQuick.NewProject("Bos PLC").IsSuccess);
        Assert.Empty(sessionQuick.Project!.Controllers);

        var quick = sessionQuick.CreateQuickPlcController();
        Assert.True(quick.IsSuccess);
        Assert.Single(sessionQuick.Project.Controllers);
        Assert.Equal(16, sessionQuick.PlcCrossReferenceRows(false, null).Count);

        var sessionCsv = new EditorSession("deneme");
        Assert.True(sessionCsv.NewProject("Bos CSV").IsSuccess);
        var csv = "%I0.0, bStart, Sistem baslat\n%Q0.0, qMotor, Ana motor";
        var imported = sessionCsv.ImportPlcAddressesCsv(csv);
        Assert.True(imported.IsSuccess);
        Assert.Equal(2, imported.Value);
        Assert.Single(sessionCsv.Project!.Controllers);

        var rows = sessionCsv.PlcCrossReferenceRows(false, null);
        Assert.Contains(rows, r => r.Address == "%I0.0" && r.SymbolName == "bStart");
        Assert.Contains(rows, r => r.Address == "%Q0.0" && r.SymbolName == "qMotor");
    }

    [Fact]
    public void Help_content_searches_in_both_languages_and_exports_standalone_html_with_svgs()
    {
        var trMatches = HelpContent.Search("kartela", UiLanguage.Turkish);
        Assert.Contains(trMatches, t => t.Id == "baglanti-renk");

        var enMatches = HelpContent.Search("claude", UiLanguage.English);
        Assert.Contains(enMatches, t => t.Id == "tanima-ai");

        Assert.All(HelpContent.Topics, topic =>
        {
            var scene = topic.BuildIllustration(UiLanguage.Turkish);
            Assert.NotEmpty(scene.Items);
        });

        var htmlPath = Path.Combine(_directory.FullName, "yardim.html");
        HelpContent.ExportStandaloneHtml(htmlPath, UiLanguage.Turkish);

        var html = File.ReadAllText(htmlPath);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("<input id=\"search\"", html, StringComparison.Ordinal);
        Assert.All(HelpContent.Topics, topic =>
        {
            Assert.Contains($"id=\"{topic.Id}\"", html, StringComparison.Ordinal);
            Assert.Contains(topic.TitleFor(UiLanguage.Turkish).Replace("&", "&amp;", StringComparison.Ordinal), html, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void UiStrings_translates_all_editor_and_symbol_commands_to_english()
    {
        var allCommands = EditorCommands.All.Concat(SymbolEditorCommands.All).ToList();
        Assert.NotEmpty(allCommands);

        var translatedCount = 0;
        foreach (var cmd in allCommands)
        {
            var english = UiStrings.T(cmd.Label, UiLanguage.English);
            Assert.False(string.IsNullOrWhiteSpace(english));
            if (!string.Equals(english, cmd.Label, StringComparison.Ordinal))
            {
                translatedCount++;
            }
        }

        Assert.True(translatedCount * 10 >= allCommands.Count * 9);
        Assert.Equal("Renumber All", UiStrings.T("Baştan numarala", UiLanguage.English));
        Assert.Equal("User Guide (Help)", UiStrings.T("Kullanım kılavuzu (Yardım)", UiLanguage.English));
        Assert.Equal("Language: EN / TR", UiStrings.T("Dil: TR / EN", UiLanguage.English));
        Assert.Equal("Baştan numarala", UiStrings.T("Baştan numarala", UiLanguage.Turkish));
    }
}
