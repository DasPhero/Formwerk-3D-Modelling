using System.Diagnostics;
using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Repair;
using static Formwerk.Tests.Repair.MeshDefects;

namespace Formwerk.Tests.Repair;

/// <summary>One-click repair (F-REP-02) on a corpus of damaged meshes made from known shapes.</summary>
public class MeshRepairTests
{
    private static MeshData Repaired(MeshData damaged)
    {
        var result = MeshRepair.Repair(damaged);
        Assert.True(result.After.IsHealthy, $"not healthy after repair: {result.After}");
        Assert.True(result.Mesh.IsWatertight(), "not watertight after repair");
        return result.Mesh;
    }

    private static int[] TrianglesAtZ(MeshData mesh, float z) =>
        [.. Enumerable.Range(0, mesh.TriangleCount).Where(t => Enumerable.Range(0, 3).All(c => Math.Abs(mesh.Vertices[mesh.Indices[t * 3 + c]].Z - z) < 1e-4f))];

    private static int[] FanOf(MeshData mesh, int vertex) =>
        [.. Enumerable.Range(0, mesh.TriangleCount).Where(t => Enumerable.Range(0, 3).Any(c => mesh.Indices[t * 3 + c] == vertex))];

    // ---- nothing to do ---------------------------------------------------------------------------

    [Fact]
    public void A_healthy_mesh_comes_back_unchanged()
    {
        var box = Box();

        var result = MeshRepair.Repair(box);

        Assert.Same(box, result.Mesh);
        Assert.False(result.Changed);
        Assert.True(result.Before.IsHealthy);
        Assert.True(result.After.IsHealthy);
    }

    [Fact]
    public void Repairing_twice_changes_nothing_the_second_time()
    {
        var first = MeshRepair.Repair(Without(Sphere(), 5, 6));
        var second = MeshRepair.Repair(first.Mesh);

        Assert.True(first.Changed);
        Assert.False(second.Changed);
        Assert.Same(first.Mesh, second.Mesh);
    }

    [Fact]
    public void An_empty_mesh_is_returned_as_it_is()
    {
        var result = MeshRepair.Repair(MeshData.Empty);

        Assert.Same(MeshData.Empty, result.Mesh);
        Assert.False(result.Changed);
    }

    // ---- holes -----------------------------------------------------------------------------------

    [Fact]
    public void A_missing_triangle_is_put_back()
    {
        var result = MeshRepair.Repair(Without(Box(), 0));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(12, result.Mesh.TriangleCount);
        Assert.Equal(6000, result.Mesh.ComputeVolume(), 3);
        Assert.Equal(1, result.Actions.FilledHoles);
        Assert.Equal(1, result.Actions.AddedTriangles);
    }

    [Fact]
    public void A_missing_face_is_closed_with_the_right_volume()
    {
        var mesh = Repaired(Without(Box(), 0, 1));

        Assert.Equal(6000, mesh.ComputeVolume(), 3);
        Assert.Equal(12, mesh.TriangleCount);
    }

    [Fact]
    public void Two_holes_are_both_closed()
    {
        var result = MeshRepair.Repair(Without(Box(), 0, 1, 2, 3));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(2, result.Actions.FilledHoles);
        Assert.Equal(6000, result.Mesh.ComputeVolume(), 3);
    }

    [Fact]
    public void An_open_cylinder_gets_a_flat_lid_of_64_corners()
    {
        var cylinder = Cylinder(20, 30);
        var volume = cylinder.ComputeVolume();

        var mesh = Repaired(Without(cylinder, TrianglesAtZ(cylinder, 30)));

        Assert.Equal(volume, mesh.ComputeVolume(), 2);
    }

    [Fact]
    public void A_turned_box_with_a_missing_face_is_closed_exactly()
    {
        var turned = Turned(Box());
        var mesh = Repaired(Without(turned, 8, 9)); // a side face, not aligned with any axis

        Assert.Equal(6000, mesh.ComputeVolume(), 2);
    }

    [Fact]
    public void A_hole_in_a_curved_surface_is_closed_with_nearly_the_same_volume()
    {
        var sphere = Sphere(20);
        var fan = FanOf(sphere, sphere.Indices[0]);
        var volume = sphere.ComputeVolume();

        var mesh = Repaired(Without(sphere, fan));

        Assert.InRange(mesh.ComputeVolume(), volume * 0.97, volume * 1.03);
    }

    [Fact]
    public void Two_holes_that_only_touch_in_a_corner_are_both_closed()
    {
        var sphere = Sphere(20);
        var v = sphere.Indices[0];
        var fan = FanOf(sphere, v);
        var first = fan[0];
        var others = new HashSet<int>(Enumerable.Range(0, 3).Select(c => sphere.Indices[first * 3 + c])) { };
        others.Remove(v);
        // a triangle around the same vertex that shares no edge with the first one
        var second = fan.First(t => t != first && !Enumerable.Range(0, 3).Any(c => others.Contains(sphere.Indices[t * 3 + c])));

        var result = MeshRepair.Repair(Without(sphere, first, second));

        Assert.True(result.After.IsHealthy, $"{result.After}");
        Assert.Equal(sphere.TriangleCount, result.Mesh.TriangleCount);
    }

    [Fact]
    public void A_huge_hole_is_closed_too()
    {
        // the whole lower half of a sphere is missing: one loop of many corners
        var sphere = Sphere(20);
        var lower = Enumerable.Range(0, sphere.TriangleCount)
            .Where(t => Enumerable.Range(0, 3).All(c => sphere.Vertices[sphere.Indices[t * 3 + c]].Z <= 10.001f)).ToArray();

        var mesh = Repaired(Without(sphere, lower));

        Assert.InRange(mesh.ComputeVolume(), sphere.ComputeVolume() * 0.45, sphere.ComputeVolume() * 0.55);
    }

    // ---- orientation -----------------------------------------------------------------------------

    [Fact]
    public void One_triangle_turned_around_is_turned_back()
    {
        var result = MeshRepair.Repair(Flipped(Box(), 0));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(6000, result.Mesh.ComputeVolume(), 3);
        Assert.Equal(1, result.Actions.FlippedTriangles);
    }

    [Fact]
    public void Many_flipped_triangles_on_a_sphere_are_fixed()
    {
        var sphere = Sphere(20);
        var random = new Random(7);
        var flips = Enumerable.Range(0, sphere.TriangleCount).Where(_ => random.NextDouble() < 0.3).ToArray();

        var mesh = Repaired(Flipped(sphere, flips));

        Assert.Equal(sphere.ComputeVolume(), mesh.ComputeVolume(), 3);
    }

    [Fact]
    public void A_mesh_turned_inside_out_is_turned_back()
    {
        var result = MeshRepair.Repair(FlippedAll(Box()));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(6000, result.Mesh.ComputeVolume(), 3);
        Assert.Equal(1, result.Actions.FlippedParts);
    }

    [Fact]
    public void Half_flipped_and_inside_out_together_still_end_up_right()
    {
        var sphere = Sphere(20);
        var half = Enumerable.Range(0, sphere.TriangleCount / 2).ToArray();

        var mesh = Repaired(Flipped(FlippedAll(sphere), half));

        Assert.Equal(sphere.ComputeVolume(), mesh.ComputeVolume(), 3);
    }

    [Fact]
    public void A_cavity_stays_a_cavity()
    {
        var hollow = Combine(Box(40, 40, 40), Move(FlippedAll(Box(10, 10, 10)), 0, 0, 10));

        var result = MeshRepair.Repair(hollow);

        Assert.True(result.After.IsHealthy);
        Assert.Equal(0, result.Actions.FlippedParts);
        Assert.Equal(64000 - 1000, result.Mesh.ComputeVolume(), 2);
    }

    // ---- vertices and duplicates -----------------------------------------------------------------

    [Fact]
    public void Vertices_that_are_not_shared_are_welded()
    {
        var result = MeshRepair.Repair(Unwelded(Box()));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(8, result.Mesh.VertexCount);
        Assert.Equal(12, result.Mesh.TriangleCount);
        Assert.Equal(28, result.Actions.WeldedVertices);
    }

    [Fact]
    public void Vertices_that_differ_by_a_hair_are_welded_too()
    {
        foreach (var mesh in new[] { Box(), Sphere(), Cylinder() })
        {
            var result = MeshRepair.Repair(Unwelded(mesh, jitter: 1e-5f, seed: 11));

            Assert.True(result.After.IsHealthy, $"{result.After}");
            Assert.Equal(mesh.TriangleCount, result.Mesh.TriangleCount);
            Assert.InRange(result.Mesh.ComputeVolume(), mesh.ComputeVolume() * 0.9999, mesh.ComputeVolume() * 1.0001); // welding moves points by about the jitter
        }
    }

    [Fact]
    public void A_triangle_that_exists_twice_is_kept_once()
    {
        var result = MeshRepair.Repair(WithDuplicate(Box(), 3));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(12, result.Mesh.TriangleCount);
        Assert.Equal(1, result.Actions.RemovedDuplicateTriangles);
    }

    [Fact]
    public void A_triangle_with_its_mirror_image_is_removed_and_the_gap_is_closed_again()
    {
        var mesh = Repaired(WithMirroredDuplicate(Box(), 0));

        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(6000, mesh.ComputeVolume(), 3);
    }

    [Fact]
    public void A_floating_double_sided_wall_inside_a_body_disappears()
    {
        var box = Box(40, 40, 40);
        var vertices = new List<Vector3>(box.Vertices.ToArray()) { new(-3, -3, 15), new(3, -3, 15), new(3, 3, 15), new(-3, 3, 15) };
        var indices = new List<int>(box.Indices.ToArray()) { 8, 9, 10, 8, 10, 11, 8, 10, 9, 8, 11, 10 };

        var result = MeshRepair.Repair(new MeshData([.. vertices], [.. indices]));

        Assert.True(result.After.IsHealthy, $"{result.After}");
        Assert.Equal(12, result.Mesh.TriangleCount);
    }

    [Fact]
    public void Degenerate_triangles_are_removed()
    {
        var vertices = new List<Vector3>(Box().Vertices.ToArray()) { new(0, 0, 100), new(0, 0, 101), new(0, 0, 102) };
        var indices = new List<int>(Box().Indices.ToArray()) { 0, 0, 1, 8, 9, 10 };

        var result = MeshRepair.Repair(new MeshData([.. vertices], [.. indices]));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(12, result.Mesh.TriangleCount);
        Assert.Equal(2, result.Actions.RemovedDegenerateTriangles);
        Assert.Equal(8, result.Mesh.VertexCount); // the vertices of the junk are gone as well
    }

    // ---- loose parts -----------------------------------------------------------------------------

    [Fact]
    public void A_speck_next_to_the_model_is_removed()
    {
        var result = MeshRepair.Repair(Combine(Box(), Speck(50, 50, 50)));

        Assert.True(result.After.IsHealthy);
        Assert.Equal(1, result.Actions.RemovedLooseParts);
        Assert.Equal(12, result.Mesh.TriangleCount);
    }

    [Fact]
    public void A_second_real_body_is_kept()
    {
        var two = Combine(Box(), Move(Box(), 100, 0, 0));

        var result = MeshRepair.Repair(two);

        Assert.False(result.Changed);
        Assert.Equal(24, result.Mesh.TriangleCount);
    }

    // ---- things that cannot be fixed -------------------------------------------------------------

    [Fact]
    public void An_edge_shared_by_three_triangles_is_reported_and_nothing_crashes()
    {
        var box = Box();
        var vertices = new List<Vector3>(box.Vertices.ToArray()) { new(50, 50, 50) };
        var edge = (box.Indices[0], box.Indices[1]);
        var indices = new List<int>(box.Indices.ToArray()) { edge.Item1, edge.Item2, 8 };

        var result = MeshRepair.Repair(new MeshData([.. vertices], [.. indices]));

        Assert.True(result.After.NonManifoldEdges > 0 || result.After.OpenEdges > 0);
        Assert.True(result.Mesh.TriangleCount >= 12);
        Assert.All(result.Mesh.Indices.ToArray(), i => Assert.InRange(i, 0, result.Mesh.VertexCount - 1));
    }

    [Fact]
    public void Random_triangle_soup_never_crashes_and_gives_a_valid_mesh()
    {
        for (var seed = 0; seed < 25; seed++)
        {
            var random = new Random(seed);
            var vertices = Enumerable.Range(0, 120).Select(_ => new Vector3((float)random.NextDouble() * 50, (float)random.NextDouble() * 50, (float)random.NextDouble() * 50)).ToArray();
            var indices = Enumerable.Range(0, 3 * 300).Select(_ => random.Next(vertices.Length)).ToArray();

            var result = MeshRepair.Repair(new MeshData(vertices, indices));

            Assert.All(result.Mesh.Indices.ToArray(), i => Assert.InRange(i, 0, result.Mesh.VertexCount - 1));
            Assert.Equal(0, result.Mesh.Indices.Length % 3);
        }
    }

    [Fact]
    public void A_mesh_of_only_junk_ends_up_empty_without_crashing()
    {
        var vertices = new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(2, 0, 0) };

        var result = MeshRepair.Repair(new MeshData(vertices, [0, 1, 2, 0, 0, 1]));

        Assert.True(result.Mesh.IsEmpty);
        Assert.Equal(2, result.Actions.RemovedDegenerateTriangles);
    }

    [Fact]
    public void A_cancelled_repair_stops()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Throws<OperationCanceledException>(() => MeshRepair.Repair(Without(Sphere(), 3), cancelled.Token));
    }

    [Fact]
    public void A_mesh_that_is_too_large_is_handed_back_untouched()
    {
        var vertices = new Vector3[MeshAnalyzer.MaxTriangles + 1 + 2];
        var indices = new int[(MeshAnalyzer.MaxTriangles + 1) * 3];
        var mesh = new MeshData(vertices, indices);

        var result = MeshRepair.Repair(mesh);

        Assert.Same(mesh, result.Mesh);
        Assert.True(result.Before.Skipped);
    }

    // ---- the corpus: combined damage -------------------------------------------------------------

    [Fact]
    public void Combined_damage_on_several_shapes_is_repaired_in_at_least_95_percent_of_the_cases()
    {
        var bases = new[] { Box(), Sphere(), Cylinder(), Turned(Box()), Turned(Sphere()), Turned(Cylinder()) };
        var healthy = 0;
        var total = 0;
        var failures = new List<string>();

        foreach (var (shape, index) in bases.Select((m, i) => (m, i)))
        {
            for (var seed = 0; seed < 10; seed++)
            {
                var random = new Random(seed * 31 + index);
                var removed = Enumerable.Range(0, shape.TriangleCount).Where(_ => random.NextDouble() < 0.03).ToArray();
                var flipped = Enumerable.Range(0, shape.TriangleCount).Where(_ => random.NextDouble() < 0.10).ToArray();
                var damaged = Unwelded(Without(Flipped(shape, flipped), removed), jitter: 1e-5f, seed: seed);
                damaged = Combine(damaged, Speck(1000, 1000, 1000));

                var result = MeshRepair.Repair(damaged);
                total++;
                if (result.After.IsHealthy && result.Mesh.IsWatertight()) healthy++;
                else failures.Add($"shape {index} seed {seed}: {result.After}");
            }
        }

        Assert.True(healthy >= total * 0.95, $"{healthy} of {total} repaired; failures: {string.Join(" | ", failures.Take(3))}");
    }

    [Fact]
    public void The_volume_stays_close_to_the_original_after_combined_damage()
    {
        var sphere = Sphere(20);
        var random = new Random(5);
        var removed = Enumerable.Range(0, sphere.TriangleCount).Where(_ => random.NextDouble() < 0.02).ToArray();
        var flipped = Enumerable.Range(0, sphere.TriangleCount).Where(_ => random.NextDouble() < 0.1).ToArray();

        var result = MeshRepair.Repair(Unwelded(Without(Flipped(sphere, flipped), removed), jitter: 1e-5f));

        Assert.True(result.After.IsHealthy, $"{result.After}");
        Assert.InRange(result.Mesh.ComputeVolume(), sphere.ComputeVolume() * 0.97, sphere.ComputeVolume() * 1.03);
    }

    // ---- speed -----------------------------------------------------------------------------------

    [Fact]
    public void A_mesh_with_a_million_triangles_and_a_few_holes_is_repaired_in_reasonable_time()
    {
        var torus = Torus(710);
        var holes = new[] { 100, 5000, 20000, 400000, 800000 };
        var damaged = Without(torus, holes);
        var watch = Stopwatch.StartNew();

        var result = MeshRepair.Repair(damaged);

        watch.Stop();
        Assert.True(torus.TriangleCount > 1_000_000);
        Assert.True(result.After.IsHealthy, $"{result.After}");
        Assert.Equal(torus.TriangleCount, result.Mesh.TriangleCount);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(30), $"took {watch.Elapsed.TotalSeconds:0.0} s");
    }

    /// <summary>A closed doughnut made of a grid that wraps around: n x n quads, 2 n^2 triangles, consistent and manifold.</summary>
    private static MeshData Torus(int n)
    {
        var vertices = new Vector3[n * n];
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                var u = i * MathF.Tau / n;
                var v = j * MathF.Tau / n;
                var r = 30 + 10 * MathF.Cos(v);
                vertices[i * n + j] = new Vector3(r * MathF.Cos(u), r * MathF.Sin(u), 10 * MathF.Sin(v));
            }
        }

        var indices = new List<int>(n * n * 6);
        for (var i = 0; i < n; i++)
        {
            for (var j = 0; j < n; j++)
            {
                int a = i * n + j, b = ((i + 1) % n) * n + j, c = ((i + 1) % n) * n + (j + 1) % n, d = i * n + (j + 1) % n;
                indices.AddRange([a, b, c, a, c, d]);
            }
        }

        return new MeshData(vertices, [.. indices]);
    }
}
