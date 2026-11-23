using Ake.Core.Model;
using Ake.Library;

namespace Ake.Editor.Tests;

public sealed class PresetAndCustomPropertiesTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Directory.Build.props")))
        {
            dir = dir.Parent;
        }

        return dir!.FullName;
    }

    [Fact]
    public void Project_presets_create_starter_structure_pages_and_connection_kinds()
    {
        var session = new EditorSession();
        var today = new DateOnly(2026, 11, 21);

        Assert.True(session.NewProject(new NewProjectInfo("Makine", Preset: ProjectPreset.IndustrialMachine), today).IsSuccess);
        var p1 = session.Project!;
        Assert.Contains(p1.Structure, n => n.Name == "GUC");
        Assert.Contains(p1.Structure, n => n.Name == "KUM");
        Assert.Contains(p1.Pages, p => p.Kind == PageKind.SingleLineDiagram);
        Assert.Contains(p1.Pages, p => p.Kind == PageKind.CommunicationDiagram);
        Assert.Contains(p1.ConnectionKinds, k => k.Name == "24VDC Kontrol");

        Assert.True(session.NewProject(new NewProjectInfo("Haberleşme", Preset: ProjectPreset.CommunicationTopology), today).IsSuccess);
        Assert.Contains(session.Project!.ConnectionKinds, k => k.Name == "PROFINET");

        Assert.True(session.NewProject(new NewProjectInfo("Pnömatik", Preset: ProjectPreset.Pneumatic), today).IsSuccess);
        Assert.Equal("pneumatic", session.Project!.Properties["discipline"]);
    }

    [Fact]
    public void Custom_properties_edit_undoes_and_dxf_import_works_in_session()
    {
        var session = new EditorSession();
        var today = new DateOnly(2026, 11, 21);
        Assert.True(session.NewProject(new NewProjectInfo("Edit Test"), today).IsSuccess);

        var macro = StandardMacros.All.First(m => m.Name == "Motor çıkışı");
        Assert.True(session.InsertMacro(macro, 1, valueSetName: "4.0 kW / 8.5 A (400 V)").IsSuccess);

        var placement = session.Project!.Placements.First();
        var data = session.EditorDataOf(placement.Id)!;
        var nextCustom = new Dictionary<string, string>(data.CustomProperties ?? new Dictionary<string, string>())
        {
            ["Güvenlik"] = "SIL 3",
            ["KatalogNo"] = "2026-A",
        };
        var edited = new DeviceEdits(
            data.Device,
            data.ProductTag,
            data.Description,
            data.Manufacturer,
            data.OrderCode,
            data.TypeDesignation,
            data.FunctionNode,
            data.LocationNode,
            [.. data.Functions.SelectMany(f => f.Points.Select(p => new PointEdit(p.Id, p.Name, p.Attributes)))],
            [.. data.Functions.Select(f => (f.Id, f.Description))],
            data.Address,
            CustomProperties: nextCustom);
        Assert.True(session.ApplyDeviceEdits(edited).IsSuccess);
        var updatedDevice = session.Project.FindDevice(data.Device)!;
        Assert.Equal("SIL 3", updatedDevice.Properties["Güvenlik"]);

        Assert.True(session.Undo().IsSuccess);
        Assert.False(updatedDevice.Properties.ContainsKey("Güvenlik"));

        var pacDxf = Path.Combine(FindRepoRoot(), "examples", "dxf", "pacdrive-lxm62.dxf");
        var imported = session.ImportDxf(pacDxf);
        Assert.True(imported.IsSuccess, imported.Error?.Message);
        Assert.Equal(1, imported.Value);
        Assert.NotEmpty(session.Project.Graphics);
    }
}
