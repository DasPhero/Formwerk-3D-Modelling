using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>The points of a face that a measurement snaps to, and the boundary they come from (E-07).</summary>
public class FaceFeaturesTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box() => new BoxGenerator().Generate(P()); // 20 x 20 x 20, the top at z = 20

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) < tolerance;

    // ---- the boundary of a face -----------------------------------------------------------------------

    [Fact]
    public void The_boundary_of_the_top_of_a_box_is_its_four_sides()
    {
        var face = FaceFinder.Find(Box(), new Vector3(0, 0, 20), Vector3.UnitZ)!;

        var boundary = face.Boundary();

        Assert.Equal(4, boundary.Count);
        Assert.All(boundary, e => Assert.Equal(20, e.A.Z, 1e-4));
        Assert.Equal(80, boundary.Sum(e => Vector3.Distance(e.A, e.B)), 1e-3); // the diagonal inside is no boundary
    }

    [Fact]
    public void A_face_with_a_hole_has_a_boundary_around_the_hole_too()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var face = FaceFinder.Find(tube, new Vector3(9, 0, 10), Vector3.UnitZ)!;

        Assert.Equal(128, face.Boundary().Count);
    }

    // ---- snapping -------------------------------------------------------------------------------------

    [Fact]
    public void A_click_near_a_corner_snaps_to_the_corner()
    {
        var snapped = FaceFeatures.Snap(Box(), new Vector3(8.6f, 9.1f, 20), Vector3.UnitZ);

        Assert.Equal(SnapKind.Corner, snapped.Kind);
        Assert.True(Near(snapped.Point, new Vector3(10, 10, 20)));
    }

    [Fact]
    public void A_click_near_the_middle_of_an_edge_snaps_to_it()
    {
        var snapped = FaceFeatures.Snap(Box(), new Vector3(0.7f, 8.9f, 20), Vector3.UnitZ);

        Assert.Equal(SnapKind.EdgeMiddle, snapped.Kind);
        Assert.True(Near(snapped.Point, new Vector3(0, 10, 20)));
    }

    [Fact]
    public void A_click_near_the_center_snaps_to_the_center()
    {
        var snapped = FaceFeatures.Snap(Box(), new Vector3(1.2f, -0.8f, 20), Vector3.UnitZ);

        Assert.Equal(SnapKind.Center, snapped.Kind);
        Assert.True(Near(snapped.Point, new Vector3(0, 0, 20)));
    }

    [Fact]
    public void A_click_far_from_every_point_stays_where_it_is()
    {
        var click = new Vector3(5, 0, 20); // between the center (5 mm away) and the middle of the edge at x = 10 (5 mm away)

        var snapped = FaceFeatures.Snap(Box(), click, Vector3.UnitZ);

        Assert.Equal(SnapKind.None, snapped.Kind);
        Assert.Equal(click, snapped.Point);
    }

    [Fact]
    public void The_nearest_point_wins()
    {
        // 1 mm from the corner, 2 mm from the middle of the edge: the corner
        var snapped = FaceFeatures.Snap(Box(), new Vector3(9, 9, 20), Vector3.UnitZ);

        Assert.Equal(SnapKind.Corner, snapped.Kind);
    }

    [Fact]
    public void The_snapped_point_lies_on_the_face()
    {
        var snapped = FaceFeatures.Snap(Box(), new Vector3(10, 7.8f, 12), Vector3.UnitX); // the side at x = 10

        Assert.Equal(10, snapped.Point.X, 1e-4);
    }

    [Fact]
    public void A_click_that_is_on_no_face_stays_where_it_is()
    {
        var click = new Vector3(40, 40, 40);

        var snapped = FaceFeatures.Snap(Box(), click, Vector3.UnitZ);

        Assert.Equal(SnapKind.None, snapped.Kind);
        Assert.Equal(click, snapped.Point);
    }

    [Fact]
    public void The_corners_of_a_round_hole_edge_are_snap_points_too()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var snapped = FaceFeatures.Snap(tube, new Vector3(8.1f, 0.02f, 10), Vector3.UnitZ);

        Assert.Equal(SnapKind.Corner, snapped.Kind);
        Assert.True(Near(snapped.Point, new Vector3(8, 0, 10)));
    }

    [Fact]
    public void The_reach_of_a_snap_grows_with_the_face_but_stays_between_1_and_6_mm()
    {
        var small = new BoxGenerator().Generate(P(("width", 4), ("depth", 4), ("height", 4)));
        var big = new BoxGenerator().Generate(P(("width", 400), ("depth", 400), ("height", 400)));

        // the small box: face diagonal 5.7, 12 % is 0.7, so the minimum of 1 mm applies; this click is 1.2 mm or more from every point
        Assert.Equal(SnapKind.None, FaceFeatures.Snap(small, new Vector3(1.2f, 0.9f, 4), Vector3.UnitZ).Kind);
        // 5 mm from the corner of the big box (12 % would be 68 mm: the maximum of 6 mm applies): snap
        Assert.Equal(SnapKind.Corner, FaceFeatures.Snap(big, new Vector3(195.2f, 197f, 400), Vector3.UnitZ).Kind);
        // 8 mm away: no snap
        Assert.Equal(SnapKind.None, FaceFeatures.Snap(big, new Vector3(192f, 200f, 400), Vector3.UnitZ).Kind);
    }
}
