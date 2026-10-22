using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Repair;
using static Formwerk.Tests.Repair.MeshDefects;

namespace Formwerk.Tests.Repair;

/// <summary>Points where surfaces touch without sharing an edge: the analysis finds and shows them (F-REP-01).</summary>
public class NonManifoldVertexTests
{
    /// <summary>A closed tetrahedron: vertices 0 to 3, outward triangles.</summary>
    private static (Vector3[] Vertices, int[] Indices) Tetra(Vector3 origin, float size = 1) =>
        ([origin, origin + new Vector3(size, 0, 0), origin + new Vector3(0, size, 0), origin + new Vector3(0, 0, size)],
         [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);

    /// <summary>Two tetrahedra that share one vertex (the second one starts in the corner of the first): a bow tie.</summary>
    private static MeshData BowTie()
    {
        var (a, ia) = Tetra(Vector3.Zero);
        var (b, ib) = Tetra(new Vector3(1, 0, 0)); // its vertex 0 is the vertex 1 of the first
        var vertices = a.Concat(b.Skip(1)).ToArray(); // the shared vertex is kept once
        // the vertices of the second tetrahedron: 0 -> 1 (shared), 1 -> 4, 2 -> 5, 3 -> 6
        int Map(int i) => i == 0 ? 1 : i + 3;
        return new MeshData(vertices, [.. ia, .. ib.Select(Map)]);
    }

    [Fact]
    public void Healthy_shapes_have_no_such_points()
    {
        foreach (var mesh in new[] { Box(), Sphere(), Cylinder() })
        {
            var report = MeshAnalyzer.Analyze(mesh);

            Assert.Equal(0, report.NonManifoldVertices);
            Assert.True(report.IsHealthy);
        }
    }

    [Fact]
    public void Two_bodies_that_share_one_corner_have_one_such_point()
    {
        var report = MeshAnalyzer.Analyze(BowTie());

        Assert.Equal(1, report.NonManifoldVertices);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.Equal(0, report.OpenEdges);
        Assert.Equal(1, report.ProblemCount);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public void This_is_not_something_the_repair_can_fix_so_it_is_not_offered()
    {
        var report = MeshAnalyzer.Analyze(BowTie());

        Assert.False(report.HasRepairableProblems);
    }

    [Fact]
    public void A_hole_that_touches_itself_in_a_point_is_found_too()
    {
        // a box without two opposite-corner triangles... simpler: a mesh of two quads that meet only in one corner (a sheet pinched together)
        var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(-1, 0, 0), new Vector3(0, -1, 0) };
        var mesh = new MeshData(vertices, [0, 1, 2, 0, 3, 4]); // two triangles that meet in vertex 0 only

        var report = MeshAnalyzer.Analyze(mesh);

        Assert.Equal(1, report.NonManifoldVertices);
    }

    [Fact]
    public void The_place_is_the_shared_corner()
    {
        var places = MeshAnalyzer.Locate(BowTie(), ProblemKind.NonManifoldVertices);

        var point = Assert.Single(places.Points);
        Assert.True(Vector3.Distance(new Vector3(1, 0, 0), point) < 1e-5f);
        Assert.Empty(places.Segments);
    }

    [Fact]
    public void A_healthy_mesh_has_no_places()
    {
        Assert.Empty(MeshAnalyzer.Locate(Box(), ProblemKind.NonManifoldVertices).Points);
    }

    [Fact]
    public void A_fan_around_a_vertex_that_is_one_piece_is_fine_even_at_the_rim_of_a_hole()
    {
        var report = MeshAnalyzer.Analyze(Without(Box(), 0));

        Assert.Equal(0, report.NonManifoldVertices);
    }

    [Fact]
    public void Two_bodies_touching_along_an_edge_have_two_such_points_and_one_bad_edge()
    {
        var (a, ia) = Tetra(Vector3.Zero);
        // a second tetrahedron that shares the edge 0-1 of the first
        var vertices = new[] { a[0], a[1], a[2], a[3], new Vector3(0.5f, -1, 0), new Vector3(0.5f, 0, -1) };
        var second = new[] { 0, 1, 4, 5 }; // vertices used by the second body: the edge 0-1 and two new corners
        var tri = new[] { 0, 4, 1, 0, 1, 5, 1, 4, 5, 4, 0, 5 };
        _ = second;
        var mesh = new MeshData(vertices, [.. ia, .. tri]);

        var report = MeshAnalyzer.Analyze(mesh);

        Assert.Equal(2, report.NonManifoldVertices);
        Assert.True(report.NonManifoldEdges >= 1);
    }

    [Fact]
    public void The_repair_leaves_such_a_point_alone_and_the_report_after_it_still_lists_it()
    {
        var result = MeshRepair.Repair(BowTie());

        Assert.Equal(1, result.After.NonManifoldVertices);
    }
}
