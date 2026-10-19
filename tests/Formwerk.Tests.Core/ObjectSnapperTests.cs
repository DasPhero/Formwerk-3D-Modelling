using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class ObjectSnapperTests
{
    // Boxes sit high above the floor (z 100 to 110) so that the floor never interferes in the tests that do not mean it.
    private static SnapBox Box(float x0, float x1, float y0 = 0, float y1 = 10, float z0 = 100, float z1 = 110) =>
        new(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1));

    private static readonly Vector3 OnlyX = Vector3.UnitX;

    private static SnapResult Snap(SnapBox own, Vector3 delta, Vector3 free, float tolerance, params SnapBox[] targets) =>
        ObjectSnapper.Snap(own, delta, free, targets, tolerance, includeBed: false);

    // ---- other objects -------------------------------------------------------------------------

    [Fact]
    public void Edges_snap_flush()
    {
        // own [0, 10], target [20, 30]: pushing the right edge to 19.5 snaps it onto the target's left edge at 20
        var result = Snap(Box(0, 10), new Vector3(9.5f, 0, 0), OnlyX, 1, Box(20, 30));

        Assert.Equal(10, result.Delta.X, 4);
        Assert.Equal(Vector3.UnitX, result.SnappedAxes);
        var guide = Assert.Single(result.Guides);
        Assert.Equal(0, guide.Axis);
        Assert.Equal(20, guide.Coordinate, 4);
    }

    [Fact]
    public void Centers_snap_exactly_on_each_other()
    {
        // own center 5, target center 25; moving by 19.7 puts the own center at 24.7
        var result = Snap(Box(0, 10), new Vector3(19.7f, 0, 0), OnlyX, 1, Box(10, 40));

        Assert.Equal(20, result.Delta.X, 4);
        Assert.Equal(25, Box(0, 10).Translated(result.Delta).Center.X, 4);
    }

    [Fact]
    public void Left_edge_snaps_to_the_right_edge_of_a_neighbour()
    {
        var result = Snap(Box(50, 60), new Vector3(-19.4f, 0, 0), OnlyX, 1, Box(20, 30)); // own left edge 30.6 -> 30

        Assert.Equal(-20, result.Delta.X, 4);
    }

    [Fact]
    public void Negative_movements_work_too()
    {
        // own [50, 60] moved by -29.6 is [20.4, 30.4]; its left edge is 0.4 mm right of the target's right edge at 20
        var result = Snap(Box(50, 60), new Vector3(-29.6f, 0, 0), OnlyX, 1, Box(0, 20));

        Assert.Equal(-30, result.Delta.X, 4);
    }

    [Fact]
    public void Nothing_snaps_outside_the_tolerance()
    {
        var result = Snap(Box(0, 10), new Vector3(7.9f, 0, 0), OnlyX, 1, Box(20, 30)); // 2.1 mm away from flush

        Assert.Equal(7.9f, result.Delta.X, 4);
        Assert.Equal(Vector3.Zero, result.SnappedAxes);
        Assert.Empty(result.Guides);
    }

    [Theory]
    [InlineData(1f, 9f, true)]     // exactly at the tolerance: snaps
    [InlineData(0.5f, 9f, false)]  // 1 mm away, tolerance 0.5: does not
    [InlineData(0f, 10f, true)]    // zero tolerance accepts an exact match only
    [InlineData(0f, 9.99f, false)]
    public void The_tolerance_is_inclusive(float tolerance, float delta, bool shouldSnap)
    {
        var result = Snap(Box(0, 10), new Vector3(delta, 0, 0), OnlyX, tolerance, Box(20, 30));

        Assert.Equal(shouldSnap, result.SnappedAxes.X == 1);
    }

    [Fact]
    public void The_closest_of_several_candidates_wins()
    {
        // right edge at 19.8: target A's left edge at 20 (0.2 away), target B's left edge at 19 (0.8 away)
        var result = Snap(Box(0, 10), new Vector3(9.8f, 0, 0), OnlyX, 1, Box(19, 25), Box(20, 30));

        Assert.Equal(10, result.Delta.X, 4);
    }

    [Theory]
    [InlineData(3f)]
    [InlineData(-3f)]
    [InlineData(0.01f)]
    [InlineData(0f)]
    public void After_snapping_the_edge_sits_exactly_on_the_target_edge(float offset)
    {
        var own = Box(0, 20);
        var result = Snap(own, new Vector3(80 + offset, 0, 0), OnlyX, 4, Box(100, 120)); // right edge `offset` away from 100 (no other feature is within 4 mm)

        Assert.Equal(100, own.Translated(result.Delta).Max.X, 3);
    }

    // ---- axes ----------------------------------------------------------------------------------

    [Fact]
    public void Axes_that_the_handle_does_not_move_along_are_never_snapped()
    {
        // would snap in Y as well, but only X is free
        var result = Snap(Box(0, 10, 0, 10), new Vector3(9.8f, 0.3f, 0), OnlyX, 1, Box(20, 30, 0, 10));

        Assert.Equal(0.3f, result.Delta.Y, 4);
        Assert.Equal(0, result.SnappedAxes.Y);
    }

    [Fact]
    public void Plane_moves_snap_both_axes_independently()
    {
        // X: right edge to 20. Y: own center 5 + 14.8 = 19.8 to the target's center 20
        var result = Snap(Box(0, 10, 0, 10), new Vector3(9.8f, 14.8f, 0), new Vector3(1, 1, 0), 1, Box(20, 30, 15, 25));

        Assert.Equal(10, result.Delta.X, 4);
        Assert.Equal(15, result.Delta.Y, 4);
        Assert.Equal(new Vector3(1, 1, 0), result.SnappedAxes);
        Assert.Equal(2, result.Guides.Count);
    }

    [Fact]
    public void One_axis_can_snap_while_the_other_does_not()
    {
        var result = Snap(Box(0, 10, 0, 10), new Vector3(9.8f, 3f, 0), new Vector3(1, 1, 0), 1, Box(20, 30, 40, 50));

        Assert.Equal(10, result.Delta.X, 4);
        Assert.Equal(3, result.Delta.Y, 4);
        Assert.Equal(new Vector3(1, 0, 0), result.SnappedAxes);
    }

    [Fact]
    public void The_guide_spans_both_objects_in_the_other_axes_and_sits_at_the_snapped_coordinate()
    {
        var result = Snap(Box(0, 10, 0, 10, 100, 110), new Vector3(9.8f, 0, 0), OnlyX, 1, Box(20, 30, 40, 50, 120, 130));

        var guide = Assert.Single(result.Guides);
        Assert.Equal(20, guide.Min.X);
        Assert.Equal(20, guide.Max.X);
        Assert.Equal(0, guide.Min.Y);   // own starts at 0 ...
        Assert.Equal(50, guide.Max.Y);  // ... target ends at 50
        Assert.Equal(100, guide.Min.Z);
        Assert.Equal(130, guide.Max.Z);
    }

    [Fact]
    public void Without_targets_nothing_changes()
    {
        var delta = new Vector3(3, 4, 5);
        var result = ObjectSnapper.Snap(Box(0, 10), delta, Vector3.One, [], 5, includeBed: false);

        Assert.Equal(delta, result.Delta);
        Assert.Empty(result.Guides);
    }

    // ---- floor and bed center ----------------------------------------------------------------------

    [Fact]
    public void The_lowest_point_snaps_onto_the_floor()
    {
        var own = Box(0, 10, 0, 10, 3, 13);
        var result = ObjectSnapper.Snap(own, new Vector3(0, 0, -2.8f), Vector3.UnitZ, [], 1);

        Assert.Equal(-3, result.Delta.Z, 4);
        Assert.Equal(0, own.Translated(result.Delta).Min.Z, 4);
        var guide = Assert.Single(result.Guides);
        Assert.Equal(2, guide.Axis);
        Assert.Equal(0, guide.Coordinate);
    }

    [Fact]
    public void Only_the_lowest_point_is_attracted_by_the_floor_not_the_top_or_the_middle()
    {
        var own = Box(0, 10, 0, 10, 50, 60);
        // the top edge would end up 0.5 above the floor: must not snap
        var result = ObjectSnapper.Snap(own, new Vector3(0, 0, -59.5f), Vector3.UnitZ, [], 1);

        Assert.Equal(-59.5f, result.Delta.Z, 4);
    }

    [Fact]
    public void The_center_snaps_onto_the_bed_center_lines()
    {
        var own = Box(0, 10, 0, 10, 100, 110); // center x = 5, y = 5
        var result = ObjectSnapper.Snap(own, new Vector3(-4.7f, -5.3f, 0), new Vector3(1, 1, 0), [], 1);

        Assert.Equal(-5, result.Delta.X, 4);
        Assert.Equal(-5, result.Delta.Y, 4);
    }

    [Fact]
    public void The_bed_can_be_switched_off()
    {
        var own = Box(0, 10, 0, 10, 3, 13);
        var result = ObjectSnapper.Snap(own, new Vector3(0, 0, -2.8f), Vector3.UnitZ, [], 1, includeBed: false);

        Assert.Equal(-2.8f, result.Delta.Z, 4);
    }

    [Fact]
    public void Objects_and_the_floor_compete_by_distance()
    {
        // after the raw movement the own box spans z 0.3 to 10.3 (center 5.3).
        // floor: 0.3 away. The target's top edge at 5.5 is 0.2 away from the own center: the object wins.
        var own = Box(0, 10, 0, 10, 5, 15);
        var target = Box(30, 40, 0, 10, 0, 5.5f);

        var result = ObjectSnapper.Snap(own, new Vector3(0, 0, -4.7f), Vector3.UnitZ, [target], 1);

        Assert.Equal(-4.5f, result.Delta.Z, 4);
        Assert.Equal(5.5f, Assert.Single(result.Guides).Coordinate, 4);
    }

    // ---- snap points and priorities (F-SNP-01, F-SNP-06) ------------------------------------------

    private static SnapBox Cube(float x, float y, float z, float size = 10) =>
        new(new Vector3(x, y, z), new Vector3(x + size, y + size, z + size));

    [Fact]
    public void Every_box_has_27_snap_points_corners_edge_middles_face_centers_and_the_center()
    {
        var points = ObjectSnapper.Lattice(Cube(0, 0, 0, 10));

        Assert.Equal(27, points.Count);
        Assert.Equal(27, points.Distinct().Count());
        Assert.Contains(new Vector3(5, 5, 5), points);       // center
        Assert.Contains(new Vector3(0, 0, 0), points);       // corner
        Assert.Contains(new Vector3(10, 10, 10), points);    // corner
        Assert.Contains(new Vector3(5, 0, 0), points);       // edge middle
        Assert.Contains(new Vector3(5, 5, 10), points);      // face center
        Assert.Equal(8, points.Count(p => p.X != 5 && p.Y != 5 && p.Z != 5));
        Assert.Equal(12, points.Count(p => new[] { p.X, p.Y, p.Z }.Count(c => c == 5) == 1));
        Assert.Equal(6, points.Count(p => new[] { p.X, p.Y, p.Z }.Count(c => c == 5) == 2));
    }

    [Fact]
    public void A_point_of_one_object_wins_over_closer_matches_with_different_objects()
    {
        // moving own [0,10]^3 by (9.2, 9.2, 0) puts it 0.8 short of the corner (20, 20) of the cube B at (20..30, 20..30);
        // another cube A offers an X edge only 0.1 away (own right edge at 19.2 -> 19.3 would be A's left edge), a closer
        // match on one axis; the corner of B is a whole point and has priority
        var own = Cube(0, 0, 100);
        var a = Cube(19.3f, 60, 300);
        var b = Cube(20, 20, 100);

        var result = Snap(own, new Vector3(9.2f, 9.2f, 0), new Vector3(1, 1, 0), 1, a, b);

        Assert.Equal(10, result.Delta.X, 4); // own max X = 20 = B's min X
        Assert.Equal(10, result.Delta.Y, 4);
        Assert.Equal(new Vector3(1, 1, 0), result.SnappedAxes);
        Assert.NotNull(result.Marker);
        Assert.Equal(20, result.Marker!.Value.X, 4);
        Assert.Equal(20, result.Marker.Value.Y, 4);
    }

    [Fact]
    public void The_marker_is_the_point_that_snapped()
    {
        var result = Snap(Cube(0, 0, 100), new Vector3(10.4f, 0, 0), OnlyX, 1, Cube(20, 0, 100));

        Assert.NotNull(result.Marker); // both cubes have the same Y and Z ranges: their points line up
        Assert.Equal(20, result.Marker!.Value.X, 4);
    }

    [Fact]
    public void Without_a_whole_point_the_axes_snap_one_by_one_as_before_and_there_is_no_marker()
    {
        // Y of the target is far away: only X can align
        var result = Snap(Cube(0, 0, 100), new Vector3(9.6f, 0, 0), OnlyX, 1, Cube(20, 500, 100));

        Assert.Equal(10, result.Delta.X, 4);
        Assert.Null(result.Marker);
        Assert.Single(result.Guides);
    }

    [Fact]
    public void A_free_axis_that_has_no_match_means_no_point_but_the_other_axis_still_snaps()
    {
        // both X and Y are free; Y is 5 mm off (more than the tolerance), X is 0.5 off
        var result = Snap(Cube(0, 0, 100), new Vector3(9.5f, 5, 0), new Vector3(1, 1, 0), 1, Cube(20, 20, 100));

        Assert.Equal(10, result.Delta.X, 4);
        Assert.Equal(5, result.Delta.Y, 4);
        Assert.Null(result.Marker);
        Assert.Equal(new Vector3(1, 0, 0), result.SnappedAxes);
    }

    [Fact]
    public void A_not_free_axis_that_does_not_line_up_prevents_a_point_snap()
    {
        // moving along X only; the cubes are 5 mm apart in Z, which the handle cannot change
        var result = Snap(Cube(0, 0, 100), new Vector3(9.7f, 0, 0), OnlyX, 1, Cube(20, 0, 103));

        Assert.Null(result.Marker);
    }

    [Fact]
    public void Planar_snaps_to_the_floor_never_make_a_marker()
    {
        var result = ObjectSnapper.Snap(Cube(0, 0, 1), new Vector3(0, 0, -0.6f), Vector3.UnitZ, [], 1);

        Assert.Equal(Vector3.UnitZ, result.SnappedAxes);
        Assert.Null(result.Marker);
    }
}
