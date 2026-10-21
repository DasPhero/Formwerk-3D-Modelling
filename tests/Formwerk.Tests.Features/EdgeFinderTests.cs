using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>Finding the edge that a click on a face means (F-EDG-02).</summary>
public class EdgeFinderTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box(double w = 20, double d = 30, double h = 10) =>
        new BoxGenerator().Generate(P(("width", w), ("depth", d), ("height", h)));

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) < tolerance;

    /// <summary>Both ends of the edge, whichever way round.</summary>
    private static void AssertEnds(StraightEdge edge, Vector3 a, Vector3 b) =>
        Assert.True((Near(edge.Start, a) && Near(edge.End, b)) || (Near(edge.Start, b) && Near(edge.End, a)), $"{edge.Start} - {edge.End}");

    // ---- straight edges between two flat faces ----------------------------------------------------------

    [Fact]
    public void A_click_on_the_top_of_a_box_near_a_side_finds_the_edge_there()
    {
        var result = EdgeFinder.Find(Box(), new Vector3(0, 13, 10), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.None, result.Problem);
        var edge = Assert.IsType<StraightEdge>(result.Straight);
        AssertEnds(edge, new Vector3(-10, 15, 10), new Vector3(10, 15, 10));
        Assert.Equal(90, edge.InnerAngle, 0.01);
    }

    [Fact]
    public void The_nearest_edge_of_the_clicked_face_wins()
    {
        var edge = EdgeFinder.Find(Box(), new Vector3(8, 0, 10), Vector3.UnitZ).Straight!;
        AssertEnds(edge, new Vector3(10, -15, 10), new Vector3(10, 15, 10));

        edge = EdgeFinder.Find(Box(), new Vector3(-8, 0, 10), Vector3.UnitZ).Straight!;
        AssertEnds(edge, new Vector3(-10, -15, 10), new Vector3(-10, 15, 10));

        edge = EdgeFinder.Find(Box(), new Vector3(0, -14, 10), Vector3.UnitZ).Straight!;
        AssertEnds(edge, new Vector3(-10, -15, 10), new Vector3(10, -15, 10));
    }

    [Fact]
    public void The_two_normals_belong_to_the_two_faces_of_the_edge()
    {
        var edge = EdgeFinder.Find(Box(), new Vector3(0, 13, 10), Vector3.UnitZ).Straight!;

        var normals = new[] { edge.NormalA, edge.NormalB };
        Assert.Contains(normals, n => Near(n, Vector3.UnitZ));
        Assert.Contains(normals, n => Near(n, Vector3.UnitY));
        Assert.True(Near(edge.NormalA, Vector3.UnitZ)); // A is the face that was clicked
    }

    [Fact]
    public void A_click_on_a_side_finds_a_vertical_edge_or_the_top_edge()
    {
        var vertical = EdgeFinder.Find(Box(), new Vector3(10, 14, 5), Vector3.UnitX).Straight!;
        AssertEnds(vertical, new Vector3(10, 15, 0), new Vector3(10, 15, 10));

        var top = EdgeFinder.Find(Box(), new Vector3(10, 0, 9), Vector3.UnitX).Straight!;
        AssertEnds(top, new Vector3(10, -15, 10), new Vector3(10, 15, 10));
    }

    [Fact]
    public void The_angle_is_the_angle_inside_the_material()
    {
        // the vertical edge of a hexagonal prism: the inner angle of a regular hexagon is 120 degrees
        var prism = new PrismGenerator().Generate(P(("sides", 6)));
        var top = prism.Vertices.ToArray().Where(v => MathF.Abs(v.Z - 20) < 1e-3f).ToArray();
        var corner = top[0];
        var neighbour = top.OrderBy(v => Vector3.Distance(v, corner)).Skip(1).First();
        var middle = (corner + neighbour) / 2;
        var side = Vector3.Normalize(Vector3.Cross(neighbour - corner, Vector3.UnitZ));
        var wall = Vector3.Dot(side, middle) > 0 ? side : -side; // looks away from the axis

        // a point on that wall, close to the corner and half way up
        var point = new Vector3(corner.X + (neighbour.X - corner.X) * 0.1f, corner.Y + (neighbour.Y - corner.Y) * 0.1f, 10);
        var edge = EdgeFinder.Find(prism, point, wall).Straight;

        Assert.NotNull(edge);
        Assert.Equal(120, edge!.InnerAngle, 0.1);
        Assert.Equal(20, Vector3.Distance(edge.Start, edge.End), 0.01); // the whole vertical edge
    }
    [Fact]
    public void Collinear_pieces_of_an_edge_are_one_edge()
    {
        // an L-shaped slab: the left side has an extra vertex in the middle, so it is made of two segments
        var slab = LSlab();

        var edge = EdgeFinder.Find(slab, new Vector3(1, 15, 10), Vector3.UnitZ).Straight!;

        AssertEnds(edge, new Vector3(0, 0, 10), new Vector3(0, 20, 10));
    }

    // ---- problems -------------------------------------------------------------------------------------

    [Fact]
    public void A_concave_edge_is_refused_with_its_reason()
    {
        var slab = LSlab();

        // the wall at x = 10 (facing +X, y from 10 to 20), next to the inner corner at (10, 10)
        var result = EdgeFinder.Find(slab, new Vector3(10, 11, 5), Vector3.UnitX);

        Assert.Equal(EdgeProblem.Concave, result.Problem);
        Assert.Null(result.Straight);
        Assert.Null(result.Rim);
    }

    [Fact]
    public void A_click_that_hits_no_face_finds_nothing()
    {
        var result = EdgeFinder.Find(Box(), new Vector3(50, 50, 50), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.NoFace, result.Problem);
    }

    [Fact]
    public void An_empty_mesh_or_a_zero_normal_finds_nothing()
    {
        Assert.Equal(EdgeProblem.NoFace, EdgeFinder.Find(MeshData.Empty, Vector3.Zero, Vector3.UnitZ).Problem);
        Assert.Equal(EdgeProblem.NoFace, EdgeFinder.Find(Box(), new Vector3(0, 0, 10), Vector3.Zero).Problem);
    }

    [Fact]
    public void An_edge_that_is_almost_flat_is_refused()
    {
        // a sphere is made of small flat pieces that meet at angles of a few degrees
        var sphere = new SphereGenerator().Generate(P());

        var top = sphere.Vertices.ToArray().OrderByDescending(v => v.Z).First();
        var result = EdgeFinder.Find(sphere, top * 0.999f, Vector3.Normalize(top));

        Assert.NotEqual(EdgeProblem.None, result.Problem);
    }

    // ---- round edges ----------------------------------------------------------------------------------

    [Fact]
    public void The_rim_of_a_cylinder_is_found_as_a_circle()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));

        var result = EdgeFinder.Find(cylinder, new Vector3(9, 1, 10), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.None, result.Problem);
        var rim = Assert.IsType<RimEdge>(result.Rim);
        Assert.True(Near(rim.Center, new Vector3(0, 0, 10)));
        Assert.Equal(10, rim.Radius, 0.01);
        Assert.True(Near(rim.Axis, Vector3.UnitZ));
        Assert.False(rim.IsHole);
        Assert.Null(result.Straight);
    }

    [Fact]
    public void The_rim_knows_its_corners_so_that_a_cutter_can_start_where_the_wall_does()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 48)));

        var rim = EdgeFinder.Find(cylinder, new Vector3(9, 1, 10), Vector3.UnitZ).Rim!;

        Assert.Equal(48, rim.Segments);
        Assert.Equal(1, rim.Reference.Length(), 1e-4);
        Assert.Equal(0, Vector3.Dot(rim.Reference, rim.Axis), 1e-4);
        // the reference points to a corner of the wall
        var corner = rim.Center + rim.Reference * rim.Radius;
        Assert.True(cylinder.Vertices.ToArray().Any(v => Near(v, corner, 1e-2f)), corner.ToString());
    }
    [Fact]
    public void The_bottom_rim_looks_down()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));

        var rim = EdgeFinder.Find(cylinder, new Vector3(0, 9, 0), -Vector3.UnitZ).Rim!;

        Assert.True(Near(rim.Center, Vector3.Zero));
        Assert.True(Near(rim.Axis, -Vector3.UnitZ));
        Assert.Equal(10, rim.Radius, 0.01);
    }

    [Fact]
    public void The_rim_of_a_hole_in_a_face_is_a_round_edge_with_the_material_outside()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var result = EdgeFinder.Find(tube, new Vector3(8.4f, 0, 10), Vector3.UnitZ);

        var rim = Assert.IsType<RimEdge>(result.Rim);
        Assert.True(rim.IsHole);
        Assert.Equal(8, rim.Radius, 0.01);
    }

    [Fact]
    public void The_outer_rim_of_a_tube_is_a_boss_rim()
    {
        var tube = new TubeGenerator().Generate(P(("outerDiameter", 20), ("wall", 2), ("height", 10), ("segments", 64)));

        var rim = EdgeFinder.Find(tube, new Vector3(9.8f, 0, 10), Vector3.UnitZ).Rim!;

        Assert.False(rim.IsHole);
        Assert.Equal(10, rim.Radius, 0.01);
    }

    [Fact]
    public void A_rim_next_to_a_slanted_wall_is_refused()
    {
        var frustum = new FrustumGenerator().Generate(P(("bottomDiameter", 20), ("topDiameter", 10), ("height", 10), ("segments", 64)));

        var result = EdgeFinder.Find(frustum, new Vector3(4.6f, 0, 10), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.NotRightAngle, result.Problem);
    }

    [Fact]
    public void A_rim_with_few_corners_is_a_polygon_and_gives_straight_edges()
    {
        var hexagon = new PrismGenerator().Generate(P(("sides", 6)));
        var top = hexagon.Vertices.ToArray().Where(v => MathF.Abs(v.Z - 20) < 1e-3f).ToArray();
        var middle = (top[0] + top[1]) / 2;

        var result = EdgeFinder.Find(hexagon, middle * 0.97f + new Vector3(0, 0, 20 * 0.03f), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.None, result.Problem);
        Assert.NotNull(result.Straight);
        Assert.Null(result.Rim);
    }

    // ---- helpers --------------------------------------------------------------------------------------

    /// <summary>
    /// An L-shaped slab, 10 mm high: the outline (0,0) (20,0) (20,10) (10,10) (10,20) (0,20), with one more vertex (0,10) on the long
    /// left side. The corner at (10,10) is a concave vertical edge.
    /// </summary>
    private static MeshData LSlab()
    {
        Vector2[] outline = [new(0, 0), new(20, 0), new(20, 10), new(10, 10), new(10, 20), new(0, 20), new(0, 10)];
        var n = outline.Length;
        var vertices = new List<Vector3>();
        foreach (var p in outline) vertices.Add(new Vector3(p.X, p.Y, 0));
        foreach (var p in outline) vertices.Add(new Vector3(p.X, p.Y, 10));

        var indices = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            indices.AddRange([i, j, n + j, i, n + j, n + i]); // walls, outward for a counter-clockwise outline
        }

        // two rectangles, split so that no vertex lies on the edge of a triangle
        int[][] top =
        [
            [0, 1, 2], [0, 2, 3], [0, 3, 6], // lower part with the points (10,10) and (0,10) on its upper edge
            [6, 3, 4], [6, 4, 5],
        ];
        foreach (var t in top)
        {
            indices.AddRange([n + t[0], n + t[1], n + t[2]]);
            indices.AddRange([t[0], t[2], t[1]]);
        }

        return new MeshData([.. vertices], [.. indices]);
    }
}
