using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Editor.Tests;

/// <summary>Sekmeli özellik penceresinin oturum tarafı: okuma ve tek adımda uygulama (Faz 6.2, madde 11).</summary>
public sealed class DevicePropertiesTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("ake-editor-props-");
    private readonly EditorSession _session = new("deneme");
    private readonly string _original;

    public DevicePropertiesTests()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        var path = Path.Combine(_directory.FullName, "makine.json");
        File.Copy(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json"), path);
        Assert.True(_session.Open(path).IsSuccess);
        _original = ProjectJson.Serialize(_session.Project!);
    }

    public void Dispose()
    {
        _session.Dispose();
        _directory.Delete(recursive: true);
    }

    private (Device Device, PlacementId Placement) Placed(string tag)
    {
        var device = _session.Project!.Devices.Single(d => d.ProductTag == tag);
        var function = device.Functions.Select(f => f.Id).ToHashSet();
        return (device, _session.Project.Placements.First(p => function.Contains(p.FunctionId)).Id);
    }

    private DeviceEdits Unchanged(DeviceEditorData data) => new(
        data.Device,
        data.ProductTag,
        data.Description,
        data.Manufacturer,
        data.OrderCode,
        data.TypeDesignation,
        data.FunctionNode,
        data.LocationNode,
        [.. data.Functions.SelectMany(function => function.Points)],
        [.. data.Functions.Select(function => (function.Id, function.Description))],
        data.Address);

    [Fact]
    public void Editor_data_lists_the_device_its_structure_choices_functions_and_points()
    {
        var (device, placement) = Placed("K1");

        var data = _session.EditorDataOf(placement)!;

        Assert.Equal(device.Id, data.Device);
        Assert.Equal(device.ProductTag, data.ProductTag);
        Assert.Equal(device.Functions.Count, data.Functions.Count);
        Assert.Equal(
            device.Functions.SelectMany(f => f.ConnectionPoints.Select(p => p.Name)),
            data.Functions.SelectMany(f => f.Points.Select(p => p.Name)));
        Assert.Null(data.FunctionNodes[0].Id);
        Assert.All(data.FunctionNodes.Skip(1), choice => Assert.NotNull(choice.Id));
        Assert.Equal(_session.Project!.Structure.Count(n => n.Aspect == Aspect.Function) + 1, data.FunctionNodes.Count);
        Assert.Null(_session.EditorDataOf(PlacementId.New()));
    }

    [Fact]
    public void Applying_edits_changes_tag_details_part_point_names_and_function_text_in_one_undoable_step()
    {
        var (device, placement) = Placed("K1");
        var data = _session.EditorDataOf(placement)!;
        var point = data.Functions[0].Points[0];
        var edits = Unchanged(data) with
        {
            ProductTag = "K11",
            Description = "Ana kontaktör",
            Manufacturer = "Schneider",
            OrderCode = "LC1D09BD",
            TypeDesignation = "TeSys D",
            Points = [new PointEdit(point.Id, "Z9"), .. data.Functions.SelectMany(f => f.Points).Skip(1)],
            FunctionDescriptions = [(data.Functions[0].Id, "Bobin")],
        };

        Assert.True(_session.ApplyDeviceEdits(edits).IsSuccess);

        Assert.Equal("K11", device.ProductTag);
        Assert.Equal("Ana kontaktör", device.Description);
        Assert.Equal(new PartRef("Schneider", "LC1D09BD", "TeSys D"), device.Part);
        Assert.Equal("Z9", device.Functions[0].ConnectionPoints[0].Name);
        Assert.Equal("Bobin", device.Functions[0].Description);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Unchanged_edits_leave_no_history_entry()
    {
        var (_, placement) = Placed("K1");

        Assert.True(_session.ApplyDeviceEdits(Unchanged(_session.EditorDataOf(placement)!)).IsSuccess);

        Assert.False(_session.CanUndo);
        Assert.False(_session.IsDirty);
    }

    [Fact]
    public void One_rejected_change_applies_nothing()
    {
        var (_, placement) = Placed("K1");
        var data = _session.EditorDataOf(placement)!;
        var points = data.Functions.SelectMany(f => f.Points).ToList();

        var duplicate = Unchanged(data) with
        {
            Description = "Yeni",
            Points = [points[0] with { Name = points[1].Name }, .. points.Skip(1)],
        };
        var halfPart = Unchanged(data) with { Description = "Yeni", Manufacturer = " ", OrderCode = "LC1D09" };
        var badTag = Unchanged(data) with { ProductTag = "K 1" };

        Assert.Equal("point.ad_tekrar", _session.ApplyDeviceEdits(duplicate).Error!.Code);
        Assert.Equal("device.urun_eksik", _session.ApplyDeviceEdits(halfPart).Error!.Code);
        Assert.Equal("tag.gecersiz", _session.ApplyDeviceEdits(badTag).Error!.Code);
        Assert.Equal("device.yok", _session.ApplyDeviceEdits(Unchanged(data) with { Device = DeviceId.New() }).Error!.Code);

        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Emptied_part_fields_remove_the_part()
    {
        var (device, placement) = Placed("K1");
        Assert.True(_session.ApplyDeviceEdits(Unchanged(_session.EditorDataOf(placement)!) with { Manufacturer = "ACME", OrderCode = "X1" }).IsSuccess);
        Assert.NotNull(device.Part);

        Assert.True(_session.ApplyDeviceEdits(Unchanged(_session.EditorDataOf(placement)!) with { Manufacturer = " ", OrderCode = "", TypeDesignation = null }).IsSuccess);

        Assert.Null(device.Part);
    }

    [Fact]
    public void Point_attributes_from_the_properties_window_are_applied_and_undone_with_the_rest()
    {
        var (device, placement) = Placed("K1");
        var data = _session.EditorDataOf(placement)!;
        var attributes = new PointAttributes(PinKind.Power, ConnectorType.Screw, ContactType.NormallyOpen, "M3");
        var first = data.Functions[0].Points[0];
        var edits = Unchanged(data) with
        {
            Points = [first with { Attributes = attributes }, .. data.Functions.SelectMany(f => f.Points).Skip(1)],
        };

        Assert.True(_session.ApplyDeviceEdits(edits).IsSuccess);
        Assert.Equal(attributes, device.Functions[0].ConnectionPoints[0].Attributes);
        Assert.Equal(attributes, _session.EditorDataOf(placement)!.Functions[0].Points[0].Attributes);

        // Aynı değerlerle yeniden uygulamak yeni komut üretmez.
        var again = _session.ApplyDeviceEdits(Unchanged(_session.EditorDataOf(placement)!));
        Assert.True(again.IsSuccess);

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void The_network_address_is_edited_in_the_properties_window_and_undone()
    {
        var (device, placement) = Placed("K1");
        var data = _session.EditorDataOf(placement)!;
        Assert.Null(data.Address);

        Assert.True(_session.ApplyDeviceEdits(Unchanged(data) with { Address = " 192.168.0.51 " }).IsSuccess);
        Assert.Equal("192.168.0.51", device.Address);
        Assert.Equal("192.168.0.51", _session.EditorDataOf(placement)!.Address);

        // Aynı adresle yeniden uygulamak yeni komut üretmez.
        Assert.True(_session.ApplyDeviceEdits(Unchanged(_session.EditorDataOf(placement)!)).IsSuccess);
        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }

    [Fact]
    public void Editor_data_includes_display_contact_mirror_and_existing_devices()
    {
        var (device, placement) = Placed("K1");
        var data = _session.EditorDataOf(placement)!;

        Assert.Equal(placement, data.PrimaryPlacement);
        Assert.NotNull(data.PrimaryFunction);
        Assert.NotNull(data.SymbolPreview);
        Assert.NotNull(data.ExistingDevices);
        Assert.Contains(data.ExistingDevices, existing => existing.ProductTag == "K1");
        Assert.NotNull(data.ContactMirror);
        Assert.Equal(device.Functions.Count, data.ContactMirror.Count);
        Assert.All(data.ContactMirror, row => Assert.False(string.IsNullOrWhiteSpace(row.Location)));
    }

    [Fact]
    public void Applying_display_and_role_edits_moves_placement_and_toggles_role_in_one_undoable_step()
    {
        var (_, placementId) = Placed("K1");
        var data = _session.EditorDataOf(placementId)!;
        var edits = Unchanged(data) with
        {
            Placement = placementId,
            X = data.X + 15,
            Y = data.Y + 10,
            RotationDeg = 90,
            Role = PlacementRole.Reference,
        };

        Assert.True(_session.ApplyDeviceEdits(edits).IsSuccess);
        var updated = _session.Project!.Placements.Single(p => p.Id == placementId);
        Assert.Equal((data.X + 15, data.Y + 10, 90, PlacementRole.Reference), (updated.X, updated.Y, updated.RotationDeg, updated.Role));

        Assert.True(_session.Undo().IsSuccess);
        Assert.False(_session.CanUndo);
        Assert.Equal(_original, ProjectJson.Serialize(_session.Project!));
    }
}
