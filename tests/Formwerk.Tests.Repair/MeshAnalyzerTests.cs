using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Repair;
using static Formwerk.Tests.Repair.MeshDefects;

namespace Formwerk.Tests.Repair;

/// <summary>Analysis of a mesh (F-REP-01): exact numbers for known defects.</summary>
public class MeshAnalyzerTests
{
    [Fact]
    public void Healthy_shapes_have_no_problems()
    {
        foreach (var mesh in new[] { Box(), Sphere(), Cylinder() })
        {
            var report = MeshAnalyzer.Analyze(mesh);

            Assert.True(report.IsHealthy, $"{report}");
            Assert.Equal(0, report.ProblemCount);
            Assert.Equal(mesh.TriangleCount, report.Triangles);
            Assert.Equal(mesh.VertexCount, report.Vertices);
            Assert.False(report.HasRepairableProblems);
        }
    }

    [Fact]
    public void An_empty_mesh_is_not_healthy_but_has_no_problems_either()
    {
        var report = MeshAnalyzer.Analyze(MeshData.Empty);

        Assert.False(report.IsHealthy);
        Assert.Equal(0, report.ProblemCount);
        Assert.Equal(0, report.Triangles);
    }

    [Fact]
    public void One_missing_triangle_leaves_a_hole_with_three_open_edges()
    {
        var report = MeshAnalyzer.Analyze(Without(Box(), 0));

        Assert.Equal(3, report.OpenEdges);
        Assert.Equal(1, report.OpenLoops);
        Assert.Equal(0, report.NonManifoldEdges);
        Assert.Equal(0, report.InconsistentEdges);
        Assert.False(report.IsHealthy);
        Assert.True(report.HasRepairableProblems);
    }

    [Fact]
    public void A_missing_face_is_one_hole_with_four_open_edges()
    {
        // the box has two triangles per face; removing a face's two triangles opens a square
        var box = Box();
        var report = MeshAnalyzer.Analyze(Without(box, 0, 1));

        Assert.Equal(4, report.OpenEdges);
        Assert.Equal(1, report.OpenLoops);
    }

    [Fact]
    public void Two_separate_holes_are_counted_separately()
    {
        var report = MeshAnalyzer.Analyze(Without(Box(), 0, 1, 2, 3)); // bottom and top: two opposite faces

        Assert.Equal(8, report.OpenEdges);
        Assert.Equal(2, report.OpenLoops);
    }

    [Fact]
    public void A_triangle_turned_around_disagrees_with_its_three_neighbours()
    {
        var report = MeshAnalyzer.Analyze(Flipped(Box(), 0));

        Assert.Equal(3, report.InconsistentEdges);
        Assert.Equal(0, report.OpenEdges);
        Assert.Equal(0, report.InvertedParts); // not a clean piece, so no verdict about its volume
        Assert.True(report.HasRepairableProblems);
    }

    [Fact]
    public void A_mesh_that_is_turned_inside_out_is_recognised()
    {
        var report = MeshAnalyzer.Analyze(FlippedAll(Box()));

        Assert.Equal(1, report.InvertedParts);
        Assert.Equal(0, report.InconsistentEdges);
        Assert.Equal(0, report.OpenEdges);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public void A_cavity_inside_a_body_is_meant_to_face_inwards_and_is_not_a_problem()
    {
        var hollow = Combine(Box(40, 40, 40), Move(FlippedAll(Box(10, 10, 10)), 0, 0, 10));

        var report = MeshAnalyzer.Analyze(hollow);

        Assert.Equal(0, report.InvertedParts);
        Assert.Equal(1, report.LooseParts); // two shells
        Assert.True(report.IsHealthy, $"{report}");
    }

    [Fact]
    public void An_inverted_body_next_to_a_good_one_is_still_inverted()
    {
        var report = MeshAnalyzer.Analyze(Combine(Box(), Move(FlippedAll(Box(10, 10, 10)), 100, 0, 0)));

        Assert.Equal(1, report.InvertedParts);
    }

    [Fact]
    public void A_triangle_used_a_third_time_makes_non_manifold_edges_and_a_duplicate()
    {
        var report = MeshAnalyzer.Analyze(WithDuplicate(Box(), 0));

        Assert.Equal(3, report.NonManifoldEdges);
        Assert.Equal(1, report.DuplicateTriangles);
        Assert.True(report.HasRepairableProblems); // the extra copy can be removed
    }

    [Fact]
    public void A_triangle_and_its_mirror_image_count_as_a_duplicate()
    {
        var report = MeshAnalyzer.Analyze(WithMirroredDuplicate(Box(), 0));

        Assert.Equal(1, report.DuplicateTriangles);
    }

    [Fact]
    public void Degenerate_triangles_are_counted_by_repeated_vertex_and_by_missing_area()
    {
        var withRepeat = WithTriangle(Box(), 0, 0, 1);
        Assert.Equal(1, MeshAnalyzer.Analyze(withRepeat).DegenerateTriangles);

        // three different vertices on one line: no area
        var vertices = new List<Vector3>(Box().Vertices.ToArray()) { new(0, 0, 100), new(0, 0, 101), new(0, 0, 102) };
        var flat = new MeshData([.. vertices], [.. Box().Indices.ToArray(), 8, 9, 10]);
        Assert.Equal(1, MeshAnalyzer.Analyze(flat).DegenerateTriangles);
    }

    [Fact]
    public void Vertices_that_are_not_shared_are_found_as_duplicates_and_leave_open_edges()
    {
        var box = Box();
        var report = MeshAnalyzer.Analyze(Unwelded(box));

        Assert.Equal(36, report.Vertices);
        Assert.Equal(36 - 8, report.DuplicateVertices);
        Assert.Equal(36, report.OpenEdges);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public void Vertices_that_differ_by_a_hair_count_as_duplicates()
    {
        var report = MeshAnalyzer.Analyze(Unwelded(Box(), jitter: 1e-5f));

        Assert.Equal(28, report.DuplicateVertices);
    }

    [Fact]
    public void Vertices_that_are_really_apart_are_not_duplicates()
    {
        // a 0.01 mm gap on a 30 mm part is far above the tolerance of about 0.0005 mm
        var report = MeshAnalyzer.Analyze(Unwelded(Box(), jitter: 0.01f));

        Assert.True(report.DuplicateVertices < 28);
    }

    [Fact]
    public void Several_bodies_are_not_an_error_but_specks_are()
    {
        var two = Combine(Box(), Move(Box(), 100, 0, 0));
        var bodies = MeshAnalyzer.Analyze(two);
        Assert.Equal(1, bodies.LooseParts);
        Assert.Equal(0, bodies.TinyLooseParts);
        Assert.True(bodies.IsHealthy);

        var withSpeck = MeshAnalyzer.Analyze(Combine(Box(), Speck(50, 50, 50)));
        Assert.Equal(1, withSpeck.LooseParts);
        Assert.Equal(1, withSpeck.TinyLooseParts);
        Assert.False(withSpeck.IsHealthy);
    }

    [Fact]
    public void The_biggest_piece_is_never_a_speck()
    {
        // a single tiny part is the model, not a speck
        var report = MeshAnalyzer.Analyze(Speck(0, 0, 0, 0.5f));

        Assert.Equal(0, report.TinyLooseParts);
        Assert.True(report.IsHealthy);
    }

    [Fact]
    public void A_mesh_that_is_too_large_is_reported_as_skipped()
    {
        var vertices = new Vector3[MeshAnalyzer.MaxTriangles + 1 + 2];
        var indices = new int[(MeshAnalyzer.MaxTriangles + 1) * 3];
        var report = MeshAnalyzer.Analyze(new MeshData(vertices, indices));

        Assert.True(report.Skipped);
        Assert.False(report.IsHealthy);
        Assert.False(report.HasRepairableProblems);
    }

    [Fact]
    public void Analysing_does_not_change_the_mesh()
    {
        var mesh = Without(Box(), 0);
        var indices = mesh.Indices.ToArray();

        MeshAnalyzer.Analyze(mesh);

        Assert.Equal(indices, mesh.Indices.ToArray());
    }

    [Fact]
    public void The_report_adds_up_to_the_problem_count()
    {
        var damaged = Combine(Without(Flipped(Box(), 5), 0), Speck(60, 60, 60));
        var report = MeshAnalyzer.Analyze(damaged);

        Assert.Equal(
            report.OpenLoops + report.NonManifoldEdges + report.InconsistentEdges + report.DegenerateTriangles + report.DuplicateTriangles
            + report.DuplicateVertices + report.TinyLooseParts + report.InvertedParts,
            report.ProblemCount);
        Assert.True(report.ProblemCount >= 3);
    }

    [Fact]
    public void A_sphere_with_random_damage_is_reported_as_damaged_in_every_category_it_has()
    {
        var sphere = Sphere();
        var damaged = Without(Flipped(sphere, 3, 10), 20, 21);

        var report = MeshAnalyzer.Analyze(damaged);

        Assert.True(report.OpenLoops >= 1);
        Assert.True(report.InconsistentEdges >= 1);
        Assert.False(report.IsHealthy);
    }

    [Fact]
    public void A_one_million_triangle_mesh_is_analysed_in_reasonable_time()
    {
        var big = Grid(710);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var report = MeshAnalyzer.Analyze(big);

        watch.Stop();
        Assert.True(big.TriangleCount > 1_000_000);
        Assert.False(report.Skipped);
        Assert.True(report.OpenEdges > 0); // a flat sheet is open
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }

    internal static MeshData Grid(int n)
    {
        var vertices = new Vector3[n * n];
        for (var y = 0; y < n; y++)
        {
            for (var x = 0; x < n; x++) vertices[y * n + x] = new Vector3(x * 0.1f, y * 0.1f, MathF.Sin(x * 0.05f) * MathF.Cos(y * 0.05f));
        }

        var indices = new List<int>((n - 1) * (n - 1) * 6);
        for (var y = 0; y < n - 1; y++)
        {
            for (var x = 0; x < n - 1; x++)
            {
                int a = y * n + x, b = a + 1, c = a + n, d = c + 1;
                indices.AddRange([a, b, d, a, d, c]);
            }
        }

        return new MeshData(vertices, [.. indices]);
    }
}
