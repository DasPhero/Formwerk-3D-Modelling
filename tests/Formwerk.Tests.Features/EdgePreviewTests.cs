using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>The outline of the edge that a click would take, also for edges that cannot be cut (F-EDG-02, hover).</summary>
public class EdgePreviewTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    [Fact]
    public void A_straight_edge_is_previewed_as_a_line_between_its_ends()
    {
        var box = new BoxGenerator().Generate(P(("width", 20), ("depth", 30), ("height", 10)));

        var result = EdgeFinder.Find(box, new Vector3(0, 13, 10), Vector3.UnitZ);

        var preview = Assert.IsType<EdgePreview>(result.Preview);
        Assert.False(preview.Closed);
        Assert.Equal(2, preview.Points.Count);
        Assert.Contains(preview.Points, p => Near(p, new Vector3(-10, 15, 10)));
        Assert.Contains(preview.Points, p => Near(p, new Vector3(10, 15, 10)));
    }

    [Fact]
    public void A_rim_is_previewed_as_the_whole_circle()
    {
        var cylinder = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 10), ("segments", 64)));

        var preview = EdgeFinder.Find(cylinder, new Vector3(9, 1, 10), Vector3.UnitZ).Preview!;

        Assert.True(preview.Closed);
        Assert.Equal(64, preview.Points.Count);
        Assert.All(preview.Points, p => Assert.Equal(10, new Vector2(p.X, p.Y).Length(), 1e-3));
        Assert.All(preview.Points, p => Assert.Equal(10, p.Z, 1e-3));
    }

    [Fact]
    public void An_edge_that_cannot_be_cut_still_has_a_preview_so_that_it_can_be_shown_grey()
    {
        // an inner edge of the L-shaped slab: the vertical edge at (10, 10)
        var result = EdgeFinder.Find(EdgeFinderTests.LSlab(), new Vector3(10, 11, 5), Vector3.UnitX);

        Assert.Equal(EdgeProblem.Concave, result.Problem);
        var preview = Assert.IsType<EdgePreview>(result.Preview);
        Assert.Contains(preview.Points, p => Near(p, new Vector3(10, 10, 0)));
        Assert.Contains(preview.Points, p => Near(p, new Vector3(10, 10, 10)));
    }

    [Fact]
    public void A_slanted_rim_is_previewed_as_a_circle_although_it_cannot_be_cut()
    {
        var frustum = new FrustumGenerator().Generate(P(("bottomDiameter", 20), ("topDiameter", 10), ("height", 10), ("segments", 64)));

        var result = EdgeFinder.Find(frustum, new Vector3(4.6f, 0, 10), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.NotRightAngle, result.Problem);
        Assert.True(result.Preview!.Closed);
        Assert.Equal(64, result.Preview.Points.Count);
    }

    [Fact]
    public void Almost_flat_and_curved_edges_have_a_preview_too()
    {
        var sphere = new SphereGenerator().Generate(P());
        var vertices = sphere.Vertices.ToArray();
        var indices = sphere.Indices.ToArray();
        var center = (vertices[indices[300]] + vertices[indices[301]] + vertices[indices[302]]) / 3;
        var normal = Vector3.Normalize(Vector3.Cross(vertices[indices[301]] - vertices[indices[300]], vertices[indices[302]] - vertices[indices[300]]));

        var result = EdgeFinder.Find(sphere, center, normal);

        Assert.NotEqual(EdgeProblem.None, result.Problem);
        Assert.NotNull(result.Preview);
        Assert.True(result.Preview!.Points.Count >= 2);
    }

    [Fact]
    public void Without_a_face_there_is_nothing_to_preview()
    {
        var box = new BoxGenerator().Generate(P());

        var result = EdgeFinder.Find(box, new Vector3(50, 50, 50), Vector3.UnitZ);

        Assert.Equal(EdgeProblem.NoFace, result.Problem);
        Assert.Null(result.Preview);
    }

    [Fact]
    public void The_preview_of_a_chain_of_pieces_goes_from_end_to_end()
    {
        var edge = EdgeFinder.Find(EdgeFinderTests.LSlab(), new Vector3(1, 15, 10), Vector3.UnitZ);

        var preview = edge.Preview!;
        Assert.Contains(preview.Points, p => Near(p, new Vector3(0, 0, 10)));
        Assert.Contains(preview.Points, p => Near(p, new Vector3(0, 20, 10)));
    }
}
