using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Repair;
using static Formwerk.Tests.Repair.MeshDefects;

namespace Formwerk.Tests.Repair;

/// <summary>Places where a surface cuts through itself: found, shown and, for overlapping bodies, united (F-REP-01, F-REP-02).</summary>
public class SelfIntersectionTests
{
    private static readonly ManifoldKernel Kernel = new();

    // not symmetric on purpose: edges that run exactly along the diagonal of a face are a touch, not a clear cut
    private static MeshData Overlapping() => Combine(Box(10, 10, 10), Move(Box(10, 10, 10), 4, 3, 5.5f));

    [Fact]
    public void Healthy_shapes_do_not_cut_through_themselves()
    {
        foreach (var mesh in new[] { Box(), Sphere(), Cylinder() })
            Assert.Equal(0, MeshAnalyzer.Analyze(mesh).SelfIntersections);
    }

    [Fact]
    public void Two_bodies_that_overlap_cut_through_each_other()
    {
        var report = MeshAnalyzer.Analyze(Overlapping());

        Assert.True(report.SelfIntersections > 0, $"{report}");
        Assert.False(report.IsHealthy);
        Assert.True(report.HasRepairableProblems);
    }

    [Fact]
    public void Two_bodies_that_are_apart_or_only_touch_face_to_face_do_not()
    {
        Assert.Equal(0, MeshAnalyzer.Analyze(Combine(Box(10, 10, 10), Move(Box(10, 10, 10), 50, 0, 0))).SelfIntersections);
        Assert.Equal(0, MeshAnalyzer.Analyze(Combine(Box(10, 10, 10), Move(Box(10, 10, 10), 10, 0, 0))).SelfIntersections); // a shared wall
    }

    [Fact]
    public void A_body_inside_another_without_touching_it_is_no_intersection()
    {
        var mesh = Combine(Box(40, 40, 40), Move(Box(10, 10, 10), 5, 5, 5));

        Assert.Equal(0, MeshAnalyzer.Analyze(mesh).SelfIntersections);
    }

    [Fact]
    public void A_corner_pulled_through_the_opposite_face_of_one_body_is_found()
    {
        var box = Box(10, 10, 10);
        var vertices = box.Vertices.ToArray();
        var lowest = Array.IndexOf(vertices, vertices.MinBy(v => v.X + v.Y + v.Z));
        vertices[lowest] = new Vector3(8, 8, 8); // into the body, through the far side of its faces
        var mesh = new MeshData(vertices, box.Indices.ToArray());

        Assert.True(MeshAnalyzer.Analyze(mesh).SelfIntersections > 0);
    }

    [Fact]
    public void The_places_are_the_lines_where_the_surfaces_cut_each_other()
    {
        var places = MeshAnalyzer.Locate(Overlapping(), ProblemKind.SelfIntersections);

        Assert.NotEmpty(places.Segments);
        Assert.Empty(places.Points);
        // the bodies are x, y from -5 to 5 (z 0 to 10) and x -1 to 9, y -2 to 8 (z 5.5 to 15.5): the cut runs in the overlap
        Assert.All(places.Segments.SelectMany(s => new[] { s.From, s.To }), p =>
        {
            Assert.InRange(p.X, -1.01f, 5.01f);
            Assert.InRange(p.Y, -2.01f, 5.01f);
            Assert.InRange(p.Z, 5.49f, 10.01f);
        });
    }

    [Fact]
    public void A_healthy_mesh_has_no_places_and_the_limit_is_kept()
    {
        Assert.Empty(MeshAnalyzer.Locate(Box(), ProblemKind.SelfIntersections).Segments);

        var limited = MeshAnalyzer.Locate(Overlapping(), ProblemKind.SelfIntersections, limit: 2);
        Assert.Equal(2, limited.Segments.Count);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public void Overlapping_bodies_are_united_by_the_repair()
    {
        var mesh = Overlapping();

        var result = MeshRepair.Repair(mesh, kernel: Kernel);

        Assert.True(result.Changed);
        Assert.True(result.After.IsHealthy, $"{result.After}");
        Assert.Equal(0, result.After.SelfIntersections);
        Assert.Equal(2000 - 6 * 7 * 4.5, result.Mesh.ComputeVolume(), 1); // the overlap of 6 x 7 x 4.5 counts once
        Assert.Equal(1, result.Actions.UnitedBodies);
        Assert.True(result.Mesh.IsWatertight());
    }

    [Fact]
    public void Without_a_kernel_nothing_is_united_and_the_problem_stays_in_the_report()
    {
        var result = MeshRepair.Repair(Overlapping());

        Assert.True(result.After.SelfIntersections > 0);
        Assert.Equal(0, result.Actions.UnitedBodies);
    }

    [Fact]
    public void Three_bodies_in_a_row_become_one()
    {
        var mesh = Combine(Combine(Box(10, 10, 10), Move(Box(10, 10, 10), 6, 1, 0.5f)), Move(Box(10, 10, 10), 12, 2, 1));

        var result = MeshRepair.Repair(mesh, kernel: Kernel);

        Assert.True(result.After.IsHealthy);
        Assert.Equal(3000 - 2 * 4 * 9 * 9.5, result.Mesh.ComputeVolume(), 1); // the first and the third body do not touch
    }

    [Fact]
    public void One_body_that_cuts_through_itself_is_not_changed_and_nothing_breaks()
    {
        var box = Box(10, 10, 10);
        var vertices = box.Vertices.ToArray();
        var lowest = Array.IndexOf(vertices, vertices.MinBy(v => v.X + v.Y + v.Z));
        vertices[lowest] = new Vector3(8, 8, 8);
        var mesh = new MeshData(vertices, box.Indices.ToArray());

        var result = MeshRepair.Repair(mesh, kernel: Kernel);

        Assert.True(result.After.SelfIntersections > 0);
        Assert.Equal(0, result.Actions.UnitedBodies);
    }

    [Fact]
    public void Bodies_with_a_hole_are_not_united_because_the_kernel_needs_closed_bodies()
    {
        var mesh = Combine(Without(Box(10, 10, 10), 0), Move(Box(10, 10, 10), 4, 3, 5.5f));

        var result = MeshRepair.Repair(mesh, kernel: Kernel);

        // the hole is closed first, so the bodies are closed and the overlap can be united
        Assert.True(result.After.IsHealthy);
    }
}
