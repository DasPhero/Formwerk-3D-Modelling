using System.Diagnostics;
using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Xunit.Abstractions;

namespace Formwerk.Tests.Kernel;

public class ManifoldKernelTests(ITestOutputHelper output)
{
    private readonly ManifoldKernel _kernel = new();

    private static MeshData Box(double w, double d, double h, Vector3? at = null) =>
        Place(new BoxGenerator().Generate(P(("width", w), ("depth", d), ("height", h))), at);

    private static MeshData Cylinder(double diameter, double height, int segments = 128, Vector3? at = null) =>
        Place(new CylinderGenerator().Generate(P(("diameter", diameter), ("height", height), ("segments", segments))), at);

    private static MeshData Place(MeshData mesh, Vector3? at) =>
        at is null ? mesh : mesh.Transformed(Matrix4x4.CreateTranslation(at.Value));

    private static Dictionary<string, double> P(params (string, double)[] v) =>
        v.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.OrdinalIgnoreCase);

    private MeshData Run(BooleanOperation op, params MeshData[] operands) =>
        _kernel.Boolean(op, operands, CancellationToken.None);

    [Fact]
    public void Difference_of_box_and_through_hole_has_the_analytic_volume()
    {
        var box = Box(40, 40, 10);
        var hole = Cylinder(10, 30, at: new Vector3(0, 0, -10));

        var result = Run(BooleanOperation.Difference, box, hole);

        var expected = 40 * 40 * 10 - Math.PI * 25 * 10;
        Assert.InRange(result.ComputeVolume(), expected - 5, expected + 1);
        Assert.True(result.IsWatertight());
        Assert.True(result.ComputeVolume() > 0);
    }

    [Fact]
    public void Union_of_overlapping_boxes_subtracts_the_overlap()
    {
        var a = Box(10, 10, 10);
        var b = Box(10, 10, 10, new Vector3(5, 0, 0));

        var result = Run(BooleanOperation.Union, a, b);

        Assert.Equal(1500, result.ComputeVolume(), 2);
        Assert.True(result.IsWatertight());
    }

    [Fact]
    public void Intersection_keeps_only_the_overlap()
    {
        var result = Run(BooleanOperation.Intersection, Box(10, 10, 10), Box(10, 10, 10, new Vector3(5, 0, 0)));
        Assert.Equal(500, result.ComputeVolume(), 2);
        Assert.True(result.IsWatertight());
    }

    [Fact]
    public void Difference_with_several_operands_subtracts_all_of_them()
    {
        var box = Box(30, 10, 10);
        var left = Box(5, 10, 10, new Vector3(-10, 0, 0));
        var right = Box(5, 10, 10, new Vector3(10, 0, 0));

        var result = Run(BooleanOperation.Difference, box, left, right);

        Assert.Equal(30 * 10 * 10 - 2 * 500, result.ComputeVolume(), 2);
    }

    [Fact]
    public void Disjoint_union_keeps_both_shells()
    {
        var result = Run(BooleanOperation.Union, Box(10, 10, 10), Box(10, 10, 10, new Vector3(50, 0, 0)));
        Assert.Equal(2000, result.ComputeVolume(), 2);
        Assert.True(result.IsWatertight());
    }

    [Fact]
    public void Fully_subtracted_body_gives_an_empty_mesh()
    {
        var result = Run(BooleanOperation.Difference, Box(10, 10, 10), Box(20, 20, 20, new Vector3(0, 0, -5)));
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Zero_and_one_operand_are_passed_through()
    {
        var box = Box(10, 10, 10);
        Assert.True(Run(BooleanOperation.Union).IsEmpty);
        Assert.Same(box, Run(BooleanOperation.Union, box));
    }

    // Spec section 10 property: A minus B never has more volume than A.
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 3, 2)]
    [InlineData(-12, 5, 8)]
    [InlineData(100, 0, 0)]
    public void Difference_never_exceeds_the_minuend(float x, float y, float z)
    {
        var a = Box(20, 20, 20);
        var b = Cylinder(15, 15, 32, new Vector3(x, y, z));
        Assert.True(Run(BooleanOperation.Difference, a, b).ComputeVolume() <= a.ComputeVolume() + 1e-3);
    }

    [Fact]
    public void Result_does_not_depend_on_where_the_scene_is_placed()
    {
        var a = Box(20, 20, 20);
        var b = Cylinder(10, 40, 64, new Vector3(3, 2, -10));
        var shift = Matrix4x4.CreateFromYawPitchRoll(0.4f, 0.9f, -0.3f) * Matrix4x4.CreateTranslation(100, -40, 7);

        var plain = Run(BooleanOperation.Difference, a, b).ComputeVolume();
        var moved = Run(BooleanOperation.Difference, a.Transformed(shift), b.Transformed(shift)).ComputeVolume();

        Assert.Equal(plain, moved, 1);
    }

    // F-BOO-05: a failed operation is a readable GeometryException, never a crash.
    [Fact]
    public void Open_mesh_is_rejected_with_a_readable_message()
    {
        var box = Box(10, 10, 10);
        var open = new MeshData(box.Vertices.ToArray(), box.Indices[..^3].ToArray());

        var ex = Assert.Throws<GeometryException>(() => Run(BooleanOperation.Difference, box, open));

        Assert.Contains("Object 2", ex.Message);
        Assert.Contains("watertight", ex.Message);
    }

    [Fact]
    public void Cancelled_token_stops_the_operation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            _kernel.Boolean(BooleanOperation.Union, [Box(1, 1, 1), Box(2, 2, 2)], cts.Token));
    }

    // Smoke test only: a warm run needs ~0.3 s, the first (JIT) run ~0.9 s, and xUnit runs other test classes at the
    // same time. The 200 ms goal of N-PRF-02 is tracked in docs/open-questions.md (Q-03), not asserted here.
    [Fact]
    public void Fifty_holes_in_a_plate_finish_quickly()
    {
        var plate = Box(200, 100, 5);
        var holes = new List<MeshData>();
        for (var i = 0; i < 50; i++)
        {
            var x = -90 + (i % 10) * 20;
            var y = -40 + (i / 10) * 20;
            holes.Add(Cylinder(6, 20, 32, new Vector3(x, y, -5)));
        }

        _ = Run(BooleanOperation.Difference, Box(10, 10, 10), Cylinder(4, 20, 16, new Vector3(0, 0, -5))); // warm up

        var sw = Stopwatch.StartNew();
        var allHoles = _kernel.Boolean(BooleanOperation.Union, holes, CancellationToken.None);
        var result = Run(BooleanOperation.Difference, plate, allHoles);
        sw.Stop();
        output.WriteLine($"50 holes: {sw.ElapsedMilliseconds} ms, {result.TriangleCount} triangles");

        var expected = 200 * 100 * 5 - 50 * Math.PI * 9 * 5;
        Assert.InRange(result.ComputeVolume(), expected * 0.98, expected * 1.001);
        Assert.True(result.IsWatertight());
        Assert.True(sw.ElapsedMilliseconds < 8000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Boolean_of_two_detailed_spheres_reports_its_speed()
    {
        var a = new SphereGenerator().Generate(P(("diameter", 40), ("segments", 256), ("rings", 128)));
        var b = a.Transformed(Matrix4x4.CreateTranslation(10, 0, 5));

        var sw = Stopwatch.StartNew();
        var result = Run(BooleanOperation.Intersection, a, b);
        sw.Stop();

        output.WriteLine($"{a.TriangleCount} + {b.TriangleCount} triangles -> {result.TriangleCount}: {sw.ElapsedMilliseconds} ms");
        Assert.True(result.IsWatertight());
        Assert.True(result.ComputeVolume() > 0);
    }
}
