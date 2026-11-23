using Ake.Core.Commands;
using Ake.Core.Editing;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

/// <summary>Çoklu seçim üzerinde taşıma, döndürme ve silme (ADR-0026).</summary>
public class SelectionTests
{
    private static List<PlacementId> PlacementsOf(Project project, string tag)
    {
        var functions = project.Device(tag).Functions.Select(f => f.Id).ToHashSet();
        return [.. project.Placements.Where(p => functions.Contains(p.FunctionId)).Select(p => p.Id)];
    }

    [Fact]
    public void Several_symbols_move_together_in_one_step()
    {
        var project = TestData.RefMachine();
        var selected = PlacementsOf(project, "K1").Concat(PlacementsOf(project, "F1")).ToList();
        var before = project.Placements.Where(p => selected.Contains(p.Id)).Select(p => (p.X, p.Y)).ToList();

        CommandAssert.RoundTrips(project, Selection.Move(project, selected, 10, -5));

        var after = project.Placements.Where(p => selected.Contains(p.Id)).Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(before.Select(p => (p.X + 10, p.Y - 5)), after);
    }

    [Fact]
    public void Several_symbols_rotate_each_around_its_own_point()
    {
        var project = TestData.RefMachine();
        var selected = PlacementsOf(project, "F1");

        CommandAssert.RoundTrips(project, Selection.Rotate(project, selected));

        Assert.All(project.Placements.Where(p => selected.Contains(p.Id)), p => Assert.Equal(90, p.RotationDeg));
    }

    [Fact]
    public void Deleting_the_only_symbol_of_a_device_deletes_the_device_and_its_connections()
    {
        var project = TestData.RefMachine();
        var motor = project.Device("M1");
        var points = motor.Functions.SelectMany(f => f.ConnectionPoints).Select(p => p.Id).ToHashSet();
        Assert.Contains(project.Connections, c => points.Contains(c.From) || points.Contains(c.To));

        CommandAssert.RoundTrips(project, Selection.Delete(project, PlacementsOf(project, "M1")));

        Assert.DoesNotContain(project.Devices, d => d.Id == motor.Id);
        Assert.DoesNotContain(project.Connections, c => points.Contains(c.From) || points.Contains(c.To));
    }

    [Fact]
    public void Deleting_some_symbols_of_a_device_keeps_the_device()
    {
        var project = TestData.RefMachine();
        var k1 = project.Device("K1");
        var coil = k1.Functions.Single(f => f.Kind == FunctionKind.Coil);
        var coilPlacement = project.Placements.Single(p => p.FunctionId == coil.Id).Id;

        CommandAssert.RoundTrips(project, Selection.Delete(project, [coilPlacement]));

        Assert.Contains(project.Devices, d => d.Id == k1.Id);
        Assert.DoesNotContain(project.Placements, p => p.FunctionId == coil.Id);
        Assert.Contains(project.Placements, p => k1.Functions.Any(f => f.Id == p.FunctionId));
    }

    [Fact]
    public void Plc_card_is_never_deleted_only_its_symbols()
    {
        var project = TestData.RefMachine();
        var card = project.Devices.First(d => project.Controllers
            .SelectMany(c => c.Networks).SelectMany(n => n.Nodes).SelectMany(n => n.Modules).Any(m => m.DeviceId == d.Id)
            && project.Placements.Any(p => d.Functions.Any(f => f.Id == p.FunctionId)));

        CommandAssert.RoundTrips(project, Selection.Delete(project, PlacementsOf(project, card.ProductTag)));

        Assert.Contains(project.Devices, d => d.Id == card.Id);
        Assert.Empty(PlacementsOf(project, card.ProductTag));
    }

    [Fact]
    public void Remove_placement_round_trips_and_rejects_an_unknown_one()
    {
        var project = TestData.RefMachine();

        CommandAssert.RoundTrips(project, new RemovePlacement(project.Placements[3].Id));
        CommandAssert.Rejected(project, new RemovePlacement(PlacementId.New()), "placement.yok");
    }
}
