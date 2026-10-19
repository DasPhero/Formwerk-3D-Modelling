using Formwerk.Features.Shapes;

namespace Formwerk.Tests.Features;

public class ShapeGeneratorTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Library_contains_the_first_shapes()
    {
        Assert.NotNull(ShapeLibrary.Find("box"));
        Assert.NotNull(ShapeLibrary.Find("CYLINDER"));
        Assert.NotNull(ShapeLibrary.Find("sphere"));
        Assert.Null(ShapeLibrary.Find("unknown"));
    }

    [Fact]
    public void Every_shape_with_default_values_is_watertight_and_rests_on_the_floor()
    {
        foreach (var generator in ShapeLibrary.All)
        {
            var mesh = generator.Generate(P());
            Assert.True(mesh.IsWatertight(), generator.Id);
            Assert.True(mesh.ComputeVolume() > 0, $"{generator.Id}: normals must point outward");
            Assert.Equal(0, mesh.ComputeBounds().Min.Z, 4);
        }
    }

    [Fact]
    public void Box_volume_and_size_are_exact()
    {
        var mesh = new BoxGenerator().Generate(P(("width", 20), ("depth", 30), ("height", 10)));
        Assert.Equal(6000, mesh.ComputeVolume(), 3);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(20, size.X, 4);
        Assert.Equal(30, size.Y, 4);
        Assert.Equal(10, size.Z, 4);
        Assert.Equal(2 * (600 + 200 + 300), mesh.ComputeSurfaceArea(), 2);
    }

    // Section 10: cylinder r = 10, h = 20 with 128 segments within 0.5% of pi*r^2*h.
    [Fact]
    public void Cylinder_volume_matches_the_analytic_value()
    {
        var mesh = new CylinderGenerator().Generate(P(("diameter", 20), ("height", 20), ("segments", 128)));
        var expected = Math.PI * 10 * 10 * 20;
        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        Assert.True(mesh.IsWatertight());
    }

    [Fact]
    public void Sphere_volume_matches_the_analytic_value()
    {
        var mesh = new SphereGenerator().Generate(P(("diameter", 20), ("segments", 96), ("rings", 48)));
        var expected = 4.0 / 3.0 * Math.PI * 1000;
        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        Assert.True(mesh.IsWatertight());
        Assert.Equal(20, mesh.ComputeBounds().Size.Z, 3);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(17)]
    [InlineData(512)]
    public void Cylinder_is_watertight_for_any_segment_count(int segments)
    {
        var mesh = new CylinderGenerator().Generate(P(("segments", segments)));
        Assert.True(mesh.IsWatertight());
        Assert.Equal(4 * segments, mesh.TriangleCount);
    }

    [Theory]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(128, 64)]
    public void Sphere_is_watertight_for_any_resolution(int segments, int rings)
    {
        var mesh = new SphereGenerator().Generate(P(("segments", segments), ("rings", rings)));
        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0);
    }

    [Fact]
    public void Cylinder_is_centered_on_the_origin_axis()
    {
        var b = new CylinderGenerator().Generate(P(("diameter", 10), ("segments", 64))).ComputeBounds();
        Assert.Equal(0, b.Center.X, 3);
        Assert.Equal(0, b.Center.Y, 3);
    }

    [Theory]
    [InlineData("box", "width", 0)]
    [InlineData("box", "height", -1)]
    [InlineData("box", "depth", double.NaN)]
    [InlineData("cylinder", "segments", 2)]
    [InlineData("sphere", "rings", 1)]
    public void Invalid_values_are_rejected_with_a_readable_message(string id, string parameter, double value)
    {
        var ex = Assert.Throws<ArgumentException>(() => ShapeLibrary.Find(id)!.Generate(P((parameter, value))));
        Assert.Contains("must be between", ex.Message);
    }

    // F-LIB-01: changing a dimension updates the shape in well under 100 ms.
    [Fact]
    public void Regenerating_a_typical_shape_is_fast()
    {
        var generator = new CylinderGenerator();
        generator.Generate(P()); // warm up the JIT
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 10; i++) generator.Generate(P(("diameter", 10 + i), ("segments", 128)));
        Assert.True(sw.ElapsedMilliseconds / 10.0 < 100, $"took {sw.ElapsedMilliseconds / 10.0} ms per shape");
    }

    // F-SCL-05: the scale gizmo resizes shapes through their dimensions. The bindings must tell the truth about the geometry.
    [Fact]
    public void Every_size_parameter_is_bound_exactly_once()
    {
        foreach (var generator in ShapeLibrary.All)
        {
            var sizes = generator.Parameters.Where(p => !p.IsInteger && !p.IsDetail).Select(p => p.Name).Order().ToList();
            var bound = generator.ScaleBindings.Select(b => b.Parameter).Order().ToList();
            Assert.Equal(sizes, bound);
            Assert.All(generator.ScaleBindings, b => Assert.NotEqual(ScaleAxes.None, b.Axes));
        }
    }

    [Fact]
    public void Changing_the_parameters_of_one_set_of_axes_scales_exactly_these_axes()
    {
        // Parameters bound to the same axes belong together (a frustum has two diameters): the scale gizmo changes them together.
        foreach (var generator in ShapeLibrary.All)
        foreach (var group in generator.ScaleBindings.GroupBy(b => b.Axes))
        {
            var defaults = generator.Parameters.ToDictionary(p => p.Name, p => p.Default, StringComparer.OrdinalIgnoreCase);
            var before = generator.Generate(defaults).ComputeBounds().Size;

            var scaled = new Dictionary<string, double>(defaults, StringComparer.OrdinalIgnoreCase);
            foreach (var binding in group) scaled[binding.Parameter] = defaults[binding.Parameter] * 2;
            var after = generator.Generate(scaled).ComputeBounds().Size;

            Assert.Equal(group.Key.HasFlag(ScaleAxes.X) ? 2.0 : 1.0, after.X / before.X, 3);
            Assert.Equal(group.Key.HasFlag(ScaleAxes.Y) ? 2.0 : 1.0, after.Y / before.Y, 3);
            Assert.Equal(group.Key.HasFlag(ScaleAxes.Z) ? 2.0 : 1.0, after.Z / before.Z, 3);
        }
    }

    [Fact]
    public void Every_axis_is_driven_by_at_least_one_parameter()
    {
        foreach (var generator in ShapeLibrary.All)
        {
            var all = generator.ScaleBindings.Aggregate(ScaleAxes.None, (acc, b) => acc | b.Axes);
            Assert.Equal(ScaleAxes.X | ScaleAxes.Y | ScaleAxes.Z, all);
        }
    }

    [Fact]
    public void Each_shape_declares_defaults_inside_its_range()
    {
        foreach (var generator in ShapeLibrary.All)
        foreach (var p in generator.Parameters)
        {
            Assert.InRange(p.Default, p.Min, p.Max);
        }
    }
}
