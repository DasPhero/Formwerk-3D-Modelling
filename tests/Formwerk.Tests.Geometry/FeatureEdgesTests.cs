using System.Diagnostics;
using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

/// <summary>Sharp edges, boundary edges and the wireframe (F-VIS-02, F-VIS-03).</summary>
public class FeatureEdgesTests
{
    private static MeshData Box(float w = 10, float d = 20, float h = 30)
    {
        float hx = w / 2, hy = d / 2;
        Vector3[] v = [new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, hy, 0), new(-hx, hy, 0), new(-hx, -hy, h), new(hx, -hy, h), new(hx, hy, h), new(-hx, hy, h)];
        int[] i = [0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        return new MeshData(v, i);
    }

    /// <summary>Latitude/longitude sphere: neighbouring faces differ by about 11 degrees or less.</summary>
    private static MeshData Sphere(int rings = 16, int segments = 32, float radius = 10)
    {
        var vertices = new List<Vector3> { new(0, 0, radius) };
        for (var r = 1; r < rings; r++)
        {
            var phi = MathF.PI * r / rings;
            for (var s = 0; s < segments; s++)
            {
                var theta = MathF.Tau * s / segments;
                vertices.Add(new Vector3(radius * MathF.Sin(phi) * MathF.Cos(theta), radius * MathF.Sin(phi) * MathF.Sin(theta), radius * MathF.Cos(phi)));
            }
        }

        vertices.Add(new Vector3(0, 0, -radius));
        var south = vertices.Count - 1;
        var indices = new List<int>();
        for (var s = 0; s < segments; s++) indices.AddRange([0, 1 + (s + 1) % segments, 1 + s]);
        for (var r = 0; r < rings - 2; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                int a = 1 + r * segments + s, b = 1 + r * segments + (s + 1) % segments, c = a + segments, d = b + segments;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }

        var last = 1 + (rings - 2) * segments;
        for (var s = 0; s < segments; s++) indices.AddRange([south, last + s, last + (s + 1) % segments]);
        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>Cylinder with side quads, and caps fanned around a center vertex. Shares the rim vertices between side and caps.</summary>
    private static MeshData Cylinder(int sides = 64, float radius = 10, float height = 30)
    {
        var vertices = new List<Vector3>();
        for (var i = 0; i < sides; i++)
        {
            var a = MathF.Tau * i / sides;
            vertices.Add(new Vector3(radius * MathF.Cos(a), radius * MathF.Sin(a), 0));
        }

        for (var i = 0; i < sides; i++)
        {
            var a = MathF.Tau * i / sides;
            vertices.Add(new Vector3(radius * MathF.Cos(a), radius * MathF.Sin(a), height));
        }

        var bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0, 0, 0));
        var topCenter = vertices.Count;
        vertices.Add(new Vector3(0, 0, height));
        var indices = new List<int>();
        for (var i = 0; i < sides; i++)
        {
            int n = (i + 1) % sides;
            indices.AddRange([i, n, sides + n, i, sides + n, sides + i]); // side
            indices.AddRange([bottomCenter, n, i]);                       // bottom, facing down
            indices.AddRange([topCenter, sides + i, sides + n]);          // top, facing up
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    private static MeshData Unwelded(MeshData mesh)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var index in mesh.Indices)
        {
            indices.Add(vertices.Count);
            vertices.Add(mesh.Vertices[index]);
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>Two triangles that share an edge and are folded against each other by <paramref name="degrees"/>.</summary>
    private static MeshData Fold(double degrees)
    {
        var angle = (float)(degrees * Math.PI / 180);
        // shared edge along X; the first triangle lies in the XY plane, the second is turned up by the fold angle
        var a = new Vector3(0, 0, 0);
        var b = new Vector3(10, 0, 0);
        var c = new Vector3(5, -10, 0);
        var d = new Vector3(5, 10 * MathF.Cos(angle), 10 * MathF.Sin(angle));
        return new MeshData([a, b, c, d], [0, 2, 1, 0, 1, 3]);
    }

    private static List<float> Lengths(EdgeSet edges) =>
        [.. Enumerable.Range(0, edges.Count).Select(i => (edges.Segment(i).B - edges.Segment(i).A).Length()).Order()];

    // ---- sharp edges -----------------------------------------------------------------------------

    [Fact]
    public void A_box_has_its_twelve_edges_and_no_diagonals()
    {
        var edges = FeatureEdges.Extract(Box(), 30);

        Assert.Equal(12, edges.Count);
        var lengths = Lengths(edges);
        Assert.Equal(4, lengths.Count(l => Math.Abs(l - 10) < 1e-4));
        Assert.Equal(4, lengths.Count(l => Math.Abs(l - 20) < 1e-4));
        Assert.Equal(4, lengths.Count(l => Math.Abs(l - 30) < 1e-4));
    }

    [Fact]
    public void A_smooth_sphere_has_no_edges_at_30_degrees_but_many_at_a_small_angle()
    {
        var sphere = Sphere();

        Assert.Equal(0, FeatureEdges.Extract(sphere, 30).Count);
        Assert.True(FeatureEdges.Extract(sphere, 3).Count > 200);
    }

    [Fact]
    public void A_cylinder_has_the_two_rims_and_nothing_else()
    {
        var edges = FeatureEdges.Extract(Cylinder(), 30);

        Assert.Equal(128, edges.Count);
        Assert.All(Lengths(edges), l => Assert.Equal(2 * 10 * Math.Sin(Math.PI / 64), l, 3));
    }

    [Theory]
    [InlineData(20, 30, false)]
    [InlineData(29.5, 30, false)]
    [InlineData(30.5, 30, true)]
    [InlineData(60, 30, true)]
    [InlineData(80, 85, false)]
    [InlineData(90, 85, true)]
    public void An_inner_edge_shows_from_the_angle_on(double fold, double threshold, bool shown)
    {
        var mesh = Fold(fold);
        var edges = FeatureEdges.Extract(mesh, threshold);

        // the four outer edges are boundary edges and always show; the shared edge shows only when it is sharp enough
        Assert.Equal(shown ? 5 : 4, edges.Count);
    }

    [Fact]
    public void An_edge_exactly_at_the_angle_counts_as_sharp()
    {
        Assert.Equal(5, FeatureEdges.Extract(Fold(45), 45).Count);
    }

    [Fact]
    public void Boundary_edges_of_an_open_mesh_are_shown()
    {
        var box = Box();
        var open = new MeshData(box.Vertices.ToArray(), box.Indices.ToArray()[3..]); // the first bottom triangle is missing

        Assert.Equal(13, FeatureEdges.Extract(open, 30).Count); // 12 box edges plus the new rim along the bottom diagonal
    }

    [Fact]
    public void An_edge_shared_by_three_triangles_is_shown_once()
    {
        var mesh = new MeshData([new(0, 0, 0), new(10, 0, 0), new(5, -10, 0), new(5, 10, 0), new(5, 0, 10)], [0, 2, 1, 0, 1, 3, 0, 4, 1]);

        var edges = FeatureEdges.Extract(mesh, 30);
        var shared = Enumerable.Range(0, edges.Count).Count(i => Math.Abs((edges.Segment(i).B - edges.Segment(i).A).Length() - 10) < 1e-4);

        Assert.Equal(1, shared);
    }

    [Fact]
    public void Meshes_without_shared_vertices_give_the_same_edges()
    {
        var box = Box();

        Assert.Equal(FeatureEdges.Extract(box, 30).Count, FeatureEdges.Extract(Unwelded(box), 30).Count);
        Assert.Equal(0, FeatureEdges.Extract(Unwelded(Sphere()), 30).Count);
        Assert.Equal(128, FeatureEdges.Extract(Unwelded(Cylinder()), 30).Count);
    }

    [Fact]
    public void Degenerate_triangles_make_no_edges_of_their_own()
    {
        var box = Box();
        var vertices = new List<Vector3>(box.Vertices.ToArray()) { new(0, 0, 100), new(0, 0, 101), new(0, 0, 102) };
        var withJunk = new MeshData([.. vertices], [.. box.Indices.ToArray(), 8, 9, 10, 0, 0, 1]);

        Assert.Equal(12, FeatureEdges.Extract(withJunk, 30).Count);
    }

    [Fact]
    public void An_empty_mesh_has_no_edges()
    {
        Assert.Equal(0, FeatureEdges.Extract(MeshData.Empty, 30).Count);
        Assert.Equal(0, FeatureEdges.AllEdges(MeshData.Empty).Count);
    }

    // ---- wireframe -------------------------------------------------------------------------------

    [Fact]
    public void The_wireframe_of_a_box_has_every_triangle_edge_once()
    {
        Assert.Equal(18, FeatureEdges.AllEdges(Box()).Count); // 12 edges and one diagonal per face
        Assert.Equal(18, FeatureEdges.AllEdges(Unwelded(Box())).Count);
    }

    [Fact]
    public void The_wireframe_of_a_sphere_has_all_its_edges()
    {
        var sphere = Sphere(8, 16);
        var triangles = sphere.TriangleCount;

        // closed manifold: every edge belongs to two triangles, so edges = 3 * triangles / 2
        Assert.Equal(3 * triangles / 2, FeatureEdges.AllEdges(sphere).Count);
    }

    // ---- values and speed ------------------------------------------------------------------------

    [Fact]
    public void The_points_are_the_positions_of_the_mesh()
    {
        var edges = FeatureEdges.Extract(Box(), 30);
        var corners = new HashSet<Vector3>(Box().Vertices.ToArray());

        for (var i = 0; i < edges.Count; i++)
        {
            Assert.Contains(edges.Segment(i).A, corners);
            Assert.Contains(edges.Segment(i).B, corners);
        }
    }

    [Fact]
    public void A_million_triangles_are_processed_in_reasonable_time()
    {
        const int n = 710;
        var vertices = new Vector3[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++) vertices[y * n + x] = new Vector3(x * 0.1f, y * 0.1f, MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f));
        }

        var indices = new List<int>();
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }

        var mesh = new MeshData(vertices, [.. indices]);
        var watch = Stopwatch.StartNew();
        var sharp = FeatureEdges.Extract(mesh, 30);
        var all = FeatureEdges.AllEdges(mesh);
        watch.Stop();

        Assert.Equal(4 * (n - 1), sharp.Count); // the open border only: a gentle sheet has no sharp inner edge
        Assert.True(all.Count > 3_000_000 / 2);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }
}
