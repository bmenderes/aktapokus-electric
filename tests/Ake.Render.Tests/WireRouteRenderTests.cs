using Ake.Core.Commands;
using Ake.Core.Model;
using Ake.Serialization;

namespace Ake.Render.Tests;

/// <summary>Köşe noktalı tellerin çizimi ve önizleme yolu (ADR-0029).</summary>
public class WireRouteRenderTests
{
    private static Project RefMachine()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props")))
        {
            root = root.Parent;
        }

        return ProjectFile.Load(Path.Combine(root!.FullName, "examples", "ref-machine", "ref-machine.json")).Value;
    }

    private static void AssertOrthogonal(IReadOnlyList<(double X, double Y)> path)
    {
        for (var i = 1; i < path.Count; i++)
        {
            Assert.True(
                Math.Abs(path[i].X - path[i - 1].X) < 0.001 || Math.Abs(path[i].Y - path[i - 1].Y) < 0.001,
                $"({path[i - 1].X},{path[i - 1].Y}) → ({path[i].X},{path[i].Y}) dik değil");
        }
    }

    [Fact]
    public void Preview_leaves_the_pin_along_its_direction_and_keeps_going_the_same_way_at_the_first_corner()
    {
        var start = new PlacedPinInfo(ConnectionPointId.New(), "1", 100, 100, 0, 1);

        var path = SheetRenderer.RoutePreview(start, [new WirePoint(130, 130)], (130, 160));

        Assert.Equal(
            [(100.0, 100.0), (100.0, 105.0), (100.0, 130.0), (130.0, 130.0), (130.0, 160.0)],
            path);
    }

    [Fact]
    public void Preview_turns_first_when_the_next_point_lies_behind_the_pin()
    {
        var start = new PlacedPinInfo(ConnectionPointId.New(), "1", 100, 100, 0, 1);

        var path = SheetRenderer.RoutePreview(start, [new WirePoint(130, 90)], (130, 60));

        Assert.Equal((100.0, 105.0), path[1]);
        Assert.Equal((130.0, 105.0), path[2]);
        AssertOrthogonal(path);
    }

    [Fact]
    public void Preview_without_corners_runs_from_the_pin_to_the_cursor_orthogonally()
    {
        var start = new PlacedPinInfo(ConnectionPointId.New(), "1", 100, 100, 1, 0);

        var path = SheetRenderer.RoutePreview(start, [], (140, 90));

        Assert.Equal((100.0, 100.0), path[0]);
        Assert.Equal((140.0, 90.0), path[^1]);
        AssertOrthogonal(path);
    }

    [Fact]
    public void Any_scattering_of_corner_points_gives_an_orthogonal_path_without_repeated_points()
    {
        var random = new Random(3);
        for (var run = 0; run < 200; run++)
        {
            var start = new PlacedPinInfo(ConnectionPointId.New(), "1", random.Next(0, 40) * 5, random.Next(0, 40) * 5, 0, random.Next(2) == 0 ? -1 : 1);
            var route = Enumerable.Range(0, random.Next(0, 6)).Select(_ => new WirePoint(random.Next(0, 40) * 5, random.Next(0, 40) * 5)).ToList();

            var path = SheetRenderer.RoutePreview(start, route, (random.Next(0, 40) * 5, random.Next(0, 40) * 5));

            AssertOrthogonal(path);
            for (var i = 1; i < path.Count; i++)
            {
                Assert.NotEqual(path[i - 1], path[i]);
            }
        }
    }

    [Fact]
    public void A_wire_with_corner_points_is_drawn_through_them_and_the_others_are_left_alone()
    {
        var project = RefMachine();
        var page = project.Pages[2];
        var before = SheetRenderer.WirePaths(project, page);
        var target = before.First(wire => wire.Path.Count >= 3);
        var corner = new WirePoint(target.Path[0].X + 35, target.Path[0].Y + 35);

        Assert.True(new CommandStack(project).Execute(new SetWireRoute(target.Connection, [corner]), ChangeSource.User, "t").IsSuccess);
        var after = SheetRenderer.WirePaths(project, page);

        var manual = after.Single(wire => wire.Connection == target.Connection);
        Assert.Contains(((double)corner.X, (double)corner.Y), manual.Path);
        AssertOrthogonal(manual.Path);
        Assert.Equal(target.Path[0], manual.Path[0]);
        Assert.Equal(target.Path[^1], manual.Path[^1]);
        Assert.Equal(before.Count, after.Count);

        var scene = SheetRenderer.Render(project, page, new DateOnly(2026, 11, 18)).Value;
        Assert.Contains(scene.Items.OfType<LineItem>(), line => (line.X2, line.Y2) == (corner.X, corner.Y) || (line.X1, line.Y1) == (corner.X, corner.Y));
    }

    [Fact]
    public void Clearing_the_route_gives_the_automatic_path_back()
    {
        var project = RefMachine();
        var page = project.Pages[2];
        var target = SheetRenderer.WirePaths(project, page).First(wire => wire.Path.Count >= 3);
        var stack = new CommandStack(project);
        Assert.True(stack.Execute(new SetWireRoute(target.Connection, [new WirePoint(target.Path[0].X + 35, target.Path[0].Y + 35)]), ChangeSource.User, "t").IsSuccess);
        Assert.True(stack.Execute(new SetWireRoute(target.Connection, null), ChangeSource.User, "t").IsSuccess);

        var again = SheetRenderer.WirePaths(project, page).Single(wire => wire.Connection == target.Connection);

        Assert.Equal(target.Path, again.Path);
    }
}
