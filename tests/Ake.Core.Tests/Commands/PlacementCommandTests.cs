using Ake.Core.Commands;
using Ake.Core.Model;

namespace Ake.Core.Tests.Commands;

public class PlacementCommandTests
{
    [Fact]
    public void MovePlacement_changes_only_the_graphic_position()
    {
        var project = TestData.RefMachine();
        var placement = project.Placements[10];
        var potentials = Connectivity.Compute(project).Potentials.Count;

        CommandAssert.RoundTrips(project, new MovePlacement(placement.Id, 123.5, 77, 270));

        Assert.Equal((123.5, 77.0, 270), (placement.X, placement.Y, placement.RotationDeg));
        Assert.Equal(65, project.Connections.Count);
        Assert.Equal(potentials, Connectivity.Compute(project).Potentials.Count);
    }

    [Fact]
    public void MovePlacement_rejects_invalid_requests()
    {
        var project = TestData.RefMachine();
        var id = project.Placements[0].Id;

        CommandAssert.Rejected(project, new MovePlacement(PlacementId.New(), 10, 10, 0), "placement.yok");
        CommandAssert.Rejected(project, new MovePlacement(id, 10, 10, 45), "placement.donus_gecersiz");
        CommandAssert.Rejected(project, new MovePlacement(id, double.NaN, 10, 0), "placement.konum_gecersiz");
        CommandAssert.Rejected(project, new MovePlacement(id, 10, double.PositiveInfinity, 0), "placement.konum_gecersiz");
    }

    [Fact]
    public void SetPlacementRole_toggles_role_and_rejects_duplicate_main()
    {
        var project = TestData.RefMachine();
        var mainPlacement = project.Placements.First(p => p.Role == PlacementRole.Main);
        var refPlacement = new Placement(
            PlacementId.New(), mainPlacement.PageId, mainPlacement.FunctionId, PlacementRole.Reference, 10, 10);
        Assert.True(new AddPlacement(refPlacement).Execute(project).IsSuccess);

        CommandAssert.Rejected(project, new SetPlacementRole(refPlacement.Id, PlacementRole.Main), "placement.ana_tekrar");
        CommandAssert.Rejected(project, new SetPlacementRole(PlacementId.New(), PlacementRole.Reference), "placement.yok");

        CommandAssert.RoundTrips(project, new SetPlacementRole(mainPlacement.Id, PlacementRole.Reference));
        Assert.Equal(PlacementRole.Reference, mainPlacement.Role);

        CommandAssert.RoundTrips(project, new SetPlacementRole(refPlacement.Id, PlacementRole.Main));
        Assert.Equal(PlacementRole.Main, refPlacement.Role);
    }
}
