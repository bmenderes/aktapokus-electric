using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Editor.Tests;

/// <summary>Sayfa ağacı, filtre ve klemens/PLC/kablo navigator'ları (Faz 6.1.5 ve 6.3.13).</summary>
public sealed class NavigatorTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-navigators-");
    private readonly EditorSession _session = new("deneme");

    public NavigatorTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private static IEnumerable<PageTreeNode> Flatten(IEnumerable<PageTreeNode> nodes) =>
        nodes.SelectMany(node => new[] { node }.Concat(Flatten(node.Children)));

    [Fact]
    public void Page_tree_groups_pages_by_function_then_by_location_and_lists_every_page_once()
    {
        var tree = _session.PageTree();

        Assert.Equal(["=C1", "=E1", "=G1", "=M1", "=M2"], tree.Select(node => node.Label));
        var motor = tree.Single(node => node.Label == "=M1");
        Assert.Equal(["+P1"], motor.Children.Select(node => node.Label));
        var page = Assert.Single(motor.Children[0].Children);
        Assert.Equal((1, "2  Motor 1"), (page.PageIndex, page.Label));

        var pages = Flatten(tree).Where(node => node.PageIndex is not null).Select(node => node.PageIndex!.Value).Order().ToList();
        Assert.Equal(Enumerable.Range(0, _session.Project!.Pages.Count), pages);
    }

    [Fact]
    public void Filter_keeps_matching_pages_with_their_groups_and_ignores_case_and_turkish_letters()
    {
        var byTitle = _session.PageTree("ACIL");
        var byGroup = _session.PageTree("=m1");

        Assert.Equal(["=E1"], byTitle.Select(node => node.Label));
        Assert.Equal([4], Flatten(byTitle).Where(node => node.PageIndex is not null).Select(node => node.PageIndex!.Value));
        Assert.Equal(["=M1"], byGroup.Select(node => node.Label));
        Assert.Equal([1], Flatten(byGroup).Where(node => node.PageIndex is not null).Select(node => node.PageIndex!.Value));
        Assert.Empty(_session.PageTree("yok böyle bir şey"));
        Assert.Equal(_session.PageTree().Count, _session.PageTree("  ").Count);
        Assert.Equal(["=G1"], _session.PageTree("güç").Select(node => node.Label));
    }

    [Fact]
    public void Pages_without_structure_stay_at_the_root_and_a_nested_path_nests_its_groups()
    {
        var project = _session.Project!;
        var parent = project.Structure.First(node => node.Aspect == Aspect.Function);
        var child = new StructureNode(StructureNodeId.New(), Aspect.Function, "ALT", parent.Id);
        var bare = new Page(PageId.New(), "9", "Yapısız");
        var nested = new Page(PageId.New(), "10", "İç içe", functionNodeId: child.Id);
        // Yapı düğümü ve sayfalar doğrudan model üzerinden eklenir; testin amacı ağacın biçimi.
        var edited = Project.Create(
            project.Id,
            project.Name,
            [.. project.Structure, child],
            project.Devices,
            pages: [.. project.Pages, bare, nested],
            sheetTemplates: project.SheetTemplates).Value;
        using var session = new EditorSession("test");
        var path = Path.Combine(_directory.FullName, "yapi.json");
        Assert.True(Ake.Serialization.ProjectFile.Save(edited, path).IsSuccess);
        Assert.True(session.Open(path).IsSuccess);

        var tree = session.PageTree();

        Assert.Contains(tree, node => node.PageIndex == 5 && node.Label == "9  Yapısız");
        var group = Flatten(tree).Single(node => node.Label == $"={parent.Name}.ALT");
        Assert.Equal("10  İç içe", Assert.Single(group.Children).Label);
        Assert.Contains(Flatten(tree), node => node.Label == $"={parent.Name}" && node.Children.Any(c => c.Label == $"={parent.Name}.ALT"));
    }

    [Fact]
    public void Page_rows_flatten_the_tree_in_order_with_depth_and_open_state()
    {
        var rows = _session.PageTreeRows(null, new HashSet<string>());

        Assert.Equal(["=C1", "+P1", "3  Kontrol", "=E1", "+P1", "5  Acil stop"], rows.Take(6).Select(row => row.Label));
        Assert.Equal([0, 1, 2, 0, 1, 2], rows.Take(6).Select(row => row.Depth));
        Assert.Equal([true, true, false, true, true, false], rows.Take(6).Select(row => row.IsGroup));
        Assert.Equal(2, rows[2].PageIndex);
        Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct().Count());
        Assert.Equal(_session.Project!.Pages.Count, rows.Count(row => row.PageIndex is not null));
    }

    [Fact]
    public void A_collapsed_group_hides_what_is_under_it_and_a_filter_opens_everything()
    {
        var all = _session.PageTreeRows(null, new HashSet<string>());
        var motor = all.First(row => row.Label == "=M1");

        var collapsed = _session.PageTreeRows(null, new HashSet<string> { motor.Key });
        var filtered = _session.PageTreeRows("motor", new HashSet<string> { motor.Key });

        Assert.False(collapsed.First(row => row.Key == motor.Key).Expanded);
        Assert.DoesNotContain(collapsed, row => row.Label == "2  Motor 1");
        Assert.Contains(collapsed, row => row.Label == "4  Motor 2");
        Assert.Equal(all.Count - 2, collapsed.Count);
        Assert.Contains(filtered, row => row.Label == "2  Motor 1");
        Assert.Contains("▸ ", collapsed.First(row => row.Key == motor.Key).ToString(), StringComparison.Ordinal);
        Assert.Contains("▾ ", all.First(row => row.Key == motor.Key).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Row_text_is_indented_by_depth()
    {
        var rows = _session.PageTreeRows(null, new HashSet<string>());

        Assert.StartsWith("      ", rows[2].ToString(), StringComparison.Ordinal);
        Assert.EndsWith("3  Kontrol", rows[2].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Terminal_navigator_lists_each_strip_with_its_terminals_and_where_they_are_drawn()
    {
        var project = _session.Project!;
        var strips = project.Devices.Where(device => device.Kind == DeviceKind.TerminalStrip).ToList();
        Assert.NotEmpty(strips);

        var rows = _session.TerminalRows();

        Assert.Equal(strips.Count + strips.Sum(strip => strip.Functions.Count), rows.Count);
        var first = rows[0];
        Assert.Contains($"{strips[0].Functions.Count} klemens", first.Label, StringComparison.Ordinal);
        Assert.Equal(strips[0].Id, first.Device);
        Assert.All(rows.Skip(1).Take(strips[0].Functions.Count), row => Assert.Contains("bağlantı", row.Label, StringComparison.Ordinal));
        Assert.Contains(rows, row => row.PageIndex is not null);
    }

    [Fact]
    public void Plc_navigator_lists_controllers_networks_nodes_modules_and_channels_with_their_assignment()
    {
        var project = _session.Project!;
        var controller = project.Controllers[0];

        var rows = _session.PlcRows();

        Assert.StartsWith(controller.Name, rows[0].Label, StringComparison.Ordinal);
        var channels = controller.Networks.SelectMany(n => n.Nodes).SelectMany(n => n.Modules).SelectMany(m => m.Channels).ToList();
        Assert.Equal(channels.Count, rows.Count(row => row.Label.StartsWith("        ", StringComparison.Ordinal)));
        Assert.Contains(rows, row => row.Label.Contains("Modül", StringComparison.Ordinal));
        Assert.Equal(
            channels.Count(channel => channel.FunctionId is null),
            rows.Count(row => row.Label.Contains("atanmamış", StringComparison.Ordinal)));
        Assert.Contains(rows, row => row.Label.Contains("sayfa", StringComparison.Ordinal) || row.Label.Contains("yerleştirilmemiş", StringComparison.Ordinal));
    }

    [Fact]
    public void Cable_navigator_lists_each_cable_and_its_cores_with_the_ends_they_connect()
    {
        var project = _session.Project!;
        Assert.NotEmpty(project.Cables);

        var rows = _session.CableRows();

        Assert.Equal(project.Cables.Count + project.Cables.Sum(cable => cable.Cores.Count), rows.Count);
        var cable = project.Cables.OrderBy(c => c.ProductTag, StringComparer.Ordinal).First();
        Assert.StartsWith($"-{cable.ProductTag}", rows[0].Label, StringComparison.Ordinal);
        Assert.Contains($"{cable.Cores.Count} damar", rows[0].Label, StringComparison.Ordinal);
        var connected = project.Connections.Count(connection => connection.CoreId is not null);
        Assert.Equal(connected, rows.Count(row => row.Label.Contains('→', StringComparison.Ordinal)));
    }

    [Fact]
    public void Navigators_are_empty_without_a_project()
    {
        using var empty = new EditorSession("boş");

        Assert.Empty(empty.PageTree());
        Assert.Empty(empty.TerminalRows());
        Assert.Empty(empty.PlcRows());
        Assert.Empty(empty.CableRows());
    }
}
