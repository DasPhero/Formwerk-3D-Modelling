using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Repair;
using static Formwerk.Tests.Repair.MeshDefects;

namespace Formwerk.Tests.Repair;

/// <summary>The places of the problems of a mesh, for the highlight in the view (F-REP-01).</summary>
public class MeshLocateTests
{
    private static bool IsEdgeOf(MeshData mesh, Vector3 a, Vector3 b)
    {
        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        bool Same(Vector3 x, Vector3 y) => Vector3.Distance(x, y) < 1e-4f;
        for (var t = 0; t < indices.Length; t += 3)
        {
            for (var e = 0; e < 3; e++)
            {
                var p = vertices[indices[t + e]];
                var q = vertices[indices[t + (e + 1) % 3]];
                if ((Same(p, a) && Same(q, b)) || (Same(p, b) && Same(q, a))) return true;
            }
        }

        return false;
    }

    [Fact]
    public void A_healthy_mesh_has_no_places_for_any_kind()
    {
        foreach (var kind in Enum.GetValues<ProblemKind>())
        {
            var places = MeshAnalyzer.Locate(Box(), kind);

            Assert.Empty(places.Segments);
            Assert.Empty(places.Points);
            Assert.False(places.Truncated);
        }
    }

    [Fact]
    public void Open_edges_are_the_rim_of_the_hole_and_as_many_as_the_report_counts()
    {
        var mesh = Without(Box(), 0);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.OpenEdges);

        Assert.Equal(MeshAnalyzer.Analyze(mesh).OpenEdges, places.Segments.Count);
        Assert.Equal(3, places.Segments.Count);
        Assert.All(places.Segments, s => Assert.True(IsEdgeOf(mesh, s.From, s.To)));
        Assert.Empty(places.Points);
    }

    [Fact]
    public void A_missing_face_gives_four_open_edges_that_form_its_outline()
    {
        var mesh = Without(Box(10, 20, 30), 0, 1);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.OpenEdges);

        Assert.Equal(4, places.Segments.Count);
        var points = places.Segments.SelectMany(s => new[] { s.From, s.To }).Distinct().ToList();
        Assert.Equal(4, points.Count); // four corners, every one used by two edges
    }

    [Fact]
    public void Wrongly_turned_triangles_mark_the_edges_where_neighbours_disagree()
    {
        var mesh = Flipped(Box(), 0);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.InconsistentEdges);

        Assert.Equal(MeshAnalyzer.Analyze(mesh).InconsistentEdges, places.Segments.Count);
        Assert.NotEmpty(places.Segments);
        Assert.All(places.Segments, s => Assert.True(IsEdgeOf(mesh, s.From, s.To)));
    }

    [Fact]
    public void A_third_triangle_on_an_edge_marks_the_non_manifold_edge()
    {
        var box = Box();
        var mesh = WithTriangle(box, box.Indices[0], box.Indices[1], box.VertexCount - 1);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.NonManifoldEdges);

        Assert.Equal(MeshAnalyzer.Analyze(mesh).NonManifoldEdges, places.Segments.Count);
        Assert.NotEmpty(places.Segments);
    }

    [Fact]
    public void A_doubled_triangle_is_marked_at_its_center()
    {
        var box = Box();
        var mesh = WithDuplicate(box, 3);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.DuplicateTriangles);

        var point = Assert.Single(places.Points);
        var v = box.Vertices;
        var i = box.Indices;
        var center = (v[i[9]] + v[i[10]] + v[i[11]]) / 3;
        Assert.True(Vector3.Distance(center, point) < 1e-4f);
        Assert.Equal(MeshAnalyzer.Analyze(mesh).DuplicateTriangles, places.Points.Count);
    }

    [Fact]
    public void A_triangle_without_area_is_marked_as_degenerate()
    {
        var box = Box();
        var mesh = WithTriangle(box, 0, 0, 1);

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.DegenerateTriangles);

        Assert.Equal(MeshAnalyzer.Analyze(mesh).DegenerateTriangles, places.Points.Count);
        Assert.NotEmpty(places.Points);
    }

    [Fact]
    public void Doubled_vertices_of_an_unwelded_mesh_are_marked()
    {
        var mesh = Unwelded(Box());

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.DuplicateVertices);

        Assert.Equal(MeshAnalyzer.Analyze(mesh).DuplicateVertices, places.Points.Count);
        Assert.NotEmpty(places.Points);
    }

    [Fact]
    public void A_speck_is_marked_at_its_center_and_the_big_body_is_not()
    {
        var mesh = Combine(Box(), Speck(100, 100, 100));

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.LooseSpecks);

        var point = Assert.Single(places.Points);
        Assert.True(Vector3.Distance(new Vector3(100, 100, 100), point) < 1);
    }

    [Fact]
    public void An_inside_out_body_is_marked_and_a_hollow_body_is_not()
    {
        var turned = FlippedAll(Box());
        var hollow = Combine(Box(40, 40, 40), FlippedAll(Move(Box(10, 10, 10), 15, 15, 15)));

        Assert.Single(MeshAnalyzer.Locate(turned, ProblemKind.InvertedParts).Points);
        Assert.Empty(MeshAnalyzer.Locate(hollow, ProblemKind.InvertedParts).Points);
    }

    [Fact]
    public void The_number_of_places_is_limited_and_the_result_says_so()
    {
        var mesh = Without(Sphere(), Enumerable.Range(0, 400).ToArray()); // a big hole with many open edges

        var places = MeshAnalyzer.Locate(mesh, ProblemKind.OpenEdges, limit: 10);

        Assert.Equal(10, places.Segments.Count);
        Assert.True(places.Truncated);
        Assert.False(MeshAnalyzer.Locate(mesh, ProblemKind.OpenEdges, limit: 100000).Truncated);
    }

    [Fact]
    public void An_empty_mesh_has_no_places()
    {
        Assert.Empty(MeshAnalyzer.Locate(MeshData.Empty, ProblemKind.OpenEdges).Segments);
    }
}
