namespace Ake.Render.Tests;

/// <summary>
/// Tel yolu kuralları. Hepsi aynı şeyi korur: çizime bakan kişi, olmayan bir bağlantıyı var sanmamalı.
/// </summary>
public class WireRouterTests
{
    private static readonly Box Area = new(0, 0, 200, 150);

    private static RouterPin Down(double x, double y) => new(x, y, 0, 1);

    private static RouterPin Up(double x, double y) => new(x, y, 0, -1);

    private static WireRouter Router(IReadOnlyList<Box> obstacles, params (RouterPin Pin, int Net)[] pins) =>
        new(Area, obstacles, pins);

    private static IEnumerable<((double X, double Y) A, (double X, double Y) B)> Segments(
        IReadOnlyList<(double X, double Y)> path) =>
        path.Zip(path.Skip(1), (a, b) => (a, b));

    private static bool Crosses(((double X, double Y) A, (double X, double Y) B) segment, Box box)
    {
        var minX = Math.Min(segment.A.X, segment.B.X);
        var maxX = Math.Max(segment.A.X, segment.B.X);
        var minY = Math.Min(segment.A.Y, segment.B.Y);
        var maxY = Math.Max(segment.A.Y, segment.B.Y);
        return maxX > box.X && minX < box.Right && maxY > box.Y && minY < box.Bottom;
    }

    /// <summary>İki yolun aynı hat üzerinde üst üste bindiği toplam uzunluk.</summary>
    private static double Overlap(IReadOnlyList<(double X, double Y)> first, IReadOnlyList<(double X, double Y)> second)
    {
        var total = 0.0;
        foreach (var a in Segments(first))
        {
            foreach (var b in Segments(second))
            {
                var aVertical = Math.Abs(a.A.X - a.B.X) < 0.001;
                var bVertical = Math.Abs(b.A.X - b.B.X) < 0.001;
                if (aVertical && bVertical && Math.Abs(a.A.X - b.A.X) < 0.001)
                {
                    total += Shared(a.A.Y, a.B.Y, b.A.Y, b.B.Y);
                }
                else if (!aVertical && !bVertical && Math.Abs(a.A.Y - b.A.Y) < 0.001)
                {
                    total += Shared(a.A.X, a.B.X, b.A.X, b.B.X);
                }
            }
        }

        return total;

        static double Shared(double a1, double a2, double b1, double b2) =>
            Math.Max(0, Math.Min(Math.Max(a1, a2), Math.Max(b1, b2)) - Math.Max(Math.Min(a1, a2), Math.Min(b1, b2)));
    }

    [Fact]
    public void Pins_in_line_are_joined_by_a_single_straight_segment()
    {
        var from = Down(50, 20);
        var to = Up(50, 80);

        var path = Router([], (from, 1), (to, 1)).Route(from, to, 1);

        Assert.Equal([(50, 20), (50, 80)], path);
    }

    [Fact]
    public void Path_is_made_of_horizontal_and_vertical_segments_only()
    {
        var from = Down(30, 20);
        var to = Up(110, 90);

        var path = Router([], (from, 1), (to, 1)).Route(from, to, 1)!;

        Assert.Equal((30, 20), path[0]);
        Assert.Equal((110, 90), path[^1]);
        Assert.All(Segments(path), s => Assert.True(
            Math.Abs(s.A.X - s.B.X) < 0.001 || Math.Abs(s.A.Y - s.B.Y) < 0.001));
    }

    [Fact]
    public void Wire_goes_around_a_symbol_that_stands_in_the_way()
    {
        var from = Down(50, 20);
        var to = Up(50, 110);
        var obstacle = new Box(40, 55, 20, 20);

        var path = Router([obstacle], (from, 1), (to, 1)).Route(from, to, 1)!;

        Assert.DoesNotContain(Segments(path), segment => Crosses(segment, obstacle));
        Assert.True(path.Count > 2);
    }

    [Fact]
    public void Wires_of_different_nets_never_run_on_top_of_each_other()
    {
        // İki tel de aynı yatay koridoru ister: uçları aynı yükseklikte, yan yana.
        var a1 = Down(20, 20);
        var a2 = Down(120, 20);
        var b1 = Down(40, 20);
        var b2 = Down(140, 20);
        var router = Router([], (a1, 1), (a2, 1), (b1, 2), (b2, 2));

        var first = router.Route(a1, a2, 1)!;
        var second = router.Route(b1, b2, 2)!;

        Assert.Equal(0, Overlap(first, second), 6);
    }

    [Fact]
    public void Wires_of_different_nets_may_cross_at_a_right_angle_without_a_junction()
    {
        var a1 = Down(20, 60);
        var a2 = Down(160, 60);
        var b1 = Down(90, 20);
        var b2 = Up(90, 130);
        var router = Router([], (a1, 1), (a2, 1), (b1, 2), (b2, 2));

        var horizontal = router.Route(a1, a2, 1);
        var vertical = router.Route(b1, b2, 2);

        Assert.NotNull(horizontal);
        Assert.Equal([(90, 20), (90, 130)], vertical);
        Assert.Empty(router.Junctions());
    }

    [Fact]
    public void Wires_of_the_same_net_share_a_trunk_and_branch_at_a_junction()
    {
        var source = Down(50, 20);
        var first = Up(50, 100);
        var second = Up(90, 100);
        var router = Router([], (source, 1), (first, 1), (second, 1));

        var trunk = router.Route(source, first, 1)!;
        var branch = router.Route(source, second, 1)!;

        Assert.True(Overlap(trunk, branch) > 0);
        var junction = Assert.Single(router.Junctions());
        Assert.Equal(50, junction.X);
        Assert.InRange(junction.Y, 25, 95);
    }

    [Fact]
    public void Wire_does_not_pass_over_the_end_of_another_nets_stub()
    {
        // Yabancı ucun çıkış parçası tam bizim düz yolumuzun üzerinde bitiyor; üzerinden geçmek
        // "T" görüntüsü verir ve bağlıymış gibi okunur.
        var from = Down(20, 20);
        var to = Down(160, 20);
        var stranger = Up(90, 30);
        var router = Router([], (from, 1), (to, 1), (stranger, 2));

        var path = router.Route(from, to, 1)!;

        Assert.DoesNotContain(Segments(path), segment =>
            Math.Abs(segment.A.Y - 25) < 0.001 && Math.Abs(segment.B.Y - 25) < 0.001
            && Math.Min(segment.A.X, segment.B.X) < 90 && Math.Max(segment.A.X, segment.B.X) > 90);
    }

    [Fact]
    public void Enclosed_pin_has_no_route()
    {
        var from = Down(50, 20);
        var to = Up(150, 100);
        Box[] walls =
        [
            new(30, 30, 40, 4), new(30, 0, 4, 34), new(66, 0, 4, 34),
        ];

        Assert.Null(Router(walls, (from, 1), (to, 1)).Route(from, to, 1));
    }

    [Fact]
    public void Off_grid_pins_are_reached_exactly()
    {
        var from = Down(33.3, 21.7);
        var to = Up(121.9, 97.1);

        var path = Router([], (from, 1), (to, 1)).Route(from, to, 1)!;

        Assert.Equal((33.3, 21.7), path[0]);
        Assert.Equal((121.9, 97.1), path[^1]);
    }

    [Fact]
    public void Routing_is_deterministic()
    {
        static IReadOnlyList<(double X, double Y)> Run()
        {
            var from = Down(30, 20);
            var to = Up(110, 90);
            return Router([new Box(60, 40, 20, 20)], (from, 1), (to, 1)).Route(from, to, 1)!;
        }

        Assert.Equal(Run(), Run());
    }
}
