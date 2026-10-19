using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>The nine basic shapes after box, cylinder and sphere (F-LIB-01): exact volumes, sizes, clean meshes.</summary>
public class MoreShapesTests
{
    private static Dictionary<string, double> P(params (string, double)[] values)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in values) result[name] = value; // a name given twice: the last value
        return result;
    }

    private static readonly string[] NewIds = ["hemisphere", "cone", "frustum", "pyramid", "prism", "wedge", "torus", "tube", "capsule"];

    private static MeshData Make(string id, params (string, double)[] values) => ShapeLibrary.Find(id)!.Generate(P(values));

    // ---- the library -----------------------------------------------------------------------------

    [Fact]
    public void The_library_has_all_twelve_basic_shapes_with_distinct_ids_and_names()
    {
        Assert.Equal(12, ShapeLibrary.All.Count);
        foreach (var id in NewIds) Assert.NotNull(ShapeLibrary.Find(id.ToUpperInvariant()));
        Assert.Equal(12, ShapeLibrary.All.Select(g => g.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(12, ShapeLibrary.All.Select(g => g.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(ShapeLibrary.All, g => Assert.False(string.IsNullOrWhiteSpace(g.DisplayName)));
    }

    [Fact]
    public void Every_new_shape_is_closed_points_outward_rests_on_the_plate_and_is_centered()
    {
        foreach (var id in NewIds)
        {
            var mesh = Make(id);
            var bounds = mesh.ComputeBounds();

            Assert.True(mesh.IsWatertight(), id);
            Assert.True(mesh.ComputeVolume() > 0, $"{id}: normals must point outward");
            Assert.Equal(0, bounds.Min.Z, 4);
            Assert.Equal(0, bounds.Center.X, 3);
            Assert.Equal(0, bounds.Center.Y, 3);
        }
    }

    [Fact]
    public void Every_new_shape_has_a_properties_panel_with_its_own_valid_parameters()
    {
        foreach (var id in NewIds)
        {
            var generator = ShapeLibrary.Find(id)!;
            Assert.NotEmpty(generator.Parameters);
            Assert.Equal(generator.Parameters.Count, generator.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(generator.Parameters, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Label));
                Assert.InRange(p.Default, p.Min, p.Max);
            });
        }
    }

    // ---- volumes and sizes -----------------------------------------------------------------------

    [Fact]
    public void Hemisphere_has_two_thirds_of_the_sphere_volume_and_half_the_height()
    {
        var mesh = Make("hemisphere", ("diameter", 20), ("segments", 96), ("rings", 48));
        var expected = 2.0 / 3.0 * Math.PI * 1000;

        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(20, size.X, 3);
        Assert.Equal(20, size.Y, 3);
        Assert.Equal(10, size.Z, 3);
        Assert.Equal(0, mesh.Vertices.ToArray().Min(v => v.Z), 4);
    }

    [Fact]
    public void Cone_volume_is_a_third_of_the_cylinder_and_its_tip_is_at_the_height()
    {
        var mesh = Make("cone", ("diameter", 20), ("height", 30), ("segments", 128));
        var expected = Math.PI * 100 * 30 / 3;

        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        Assert.Equal(30, mesh.ComputeBounds().Size.Z, 4);
        Assert.Equal(2 * 128, mesh.TriangleCount); // side and base, one triangle per segment each
    }

    [Theory]
    [InlineData(20, 10, 20)]
    [InlineData(10, 20, 15)]  // wider at the top: an upside-down cone stump
    [InlineData(20, 20, 30)]  // equal diameters: a cylinder
    public void Frustum_volume_matches_the_formula(double bottom, double top, double height)
    {
        var mesh = Make("frustum", ("bottomDiameter", bottom), ("topDiameter", top), ("height", height), ("segments", 128));
        double r = bottom / 2, t = top / 2;
        var expected = Math.PI * height / 3 * (r * r + r * t + t * t);

        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(Math.Max(bottom, top), size.X, 3);
        Assert.Equal(height, size.Z, 4);
    }

    [Fact]
    public void Pyramid_volume_and_size_are_exact()
    {
        var mesh = Make("pyramid", ("width", 30), ("depth", 20), ("height", 12));

        Assert.Equal(30 * 20 * 12 / 3.0, mesh.ComputeVolume(), 3);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(30, size.X, 4);
        Assert.Equal(20, size.Y, 4);
        Assert.Equal(12, size.Z, 4);
        Assert.Equal(6, mesh.TriangleCount);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    [InlineData(12)]
    public void Prism_volume_is_the_area_of_the_regular_polygon_times_the_height(int sides)
    {
        var mesh = Make("prism", ("diameter", 20), ("height", 15), ("sides", sides));
        var area = sides / 2.0 * 100 * Math.Sin(2 * Math.PI / sides);

        Assert.Equal(area * 15, mesh.ComputeVolume(), 2);
        Assert.Equal(4 * sides, mesh.TriangleCount);
        Assert.Equal(15, mesh.ComputeBounds().Size.Z, 4);
    }

    [Fact]
    public void A_hexagon_prism_is_as_wide_as_its_diameter_across_the_corners()
    {
        var size = Make("prism", ("diameter", 20), ("sides", 6)).ComputeBounds().Size;

        Assert.Equal(20, size.X, 3);
        Assert.Equal(20 * Math.Sqrt(3) / 2, size.Y, 3);
    }

    [Fact]
    public void Wedge_is_half_a_box_with_a_vertical_back()
    {
        var mesh = Make("wedge", ("width", 20), ("depth", 30), ("height", 10));

        Assert.Equal(20 * 30 * 10 / 2.0, mesh.ComputeVolume(), 3);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(20, size.X, 4);
        Assert.Equal(30, size.Y, 4);
        Assert.Equal(10, size.Z, 4);
        Assert.Equal(8, mesh.TriangleCount);
        // the high edge is at the back (+Y): every vertex with the full height has y = +depth/2
        Assert.All(mesh.Vertices.ToArray().Where(v => v.Z > 9.99f), v => Assert.Equal(15, v.Y, 4));
    }

    [Fact]
    public void Torus_volume_matches_the_formula_and_the_size_is_outer_diameter_and_tube_diameter()
    {
        var mesh = Make("torus", ("outerDiameter", 40), ("tubeDiameter", 10), ("segments", 128), ("sides", 48));
        double major = 15, minor = 5;
        var expected = 2 * Math.PI * Math.PI * major * minor * minor;

        Assert.InRange(mesh.ComputeVolume(), expected * 0.99, expected);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(40, size.X, 3);
        Assert.Equal(40, size.Y, 3);
        Assert.Equal(10, size.Z, 3);
    }

    [Fact]
    public void Tube_volume_is_the_ring_area_times_the_height_and_the_hole_goes_through()
    {
        var mesh = Make("tube", ("outerDiameter", 20), ("wall", 2), ("height", 30), ("segments", 128));
        var expected = Math.PI * (100 - 64) * 30;

        Assert.InRange(mesh.ComputeVolume(), expected * 0.995, expected);
        Assert.Equal(20, mesh.ComputeBounds().Size.X, 3);
        Assert.Equal(8 * 128, mesh.TriangleCount);
        // the inner wall is at radius 8
        Assert.Contains(mesh.Vertices.ToArray(), v => Math.Abs(Math.Sqrt(v.X * v.X + v.Y * v.Y) - 8) < 1e-3);
    }

    [Fact]
    public void Capsule_volume_is_cylinder_plus_sphere_and_the_height_is_the_whole_length()
    {
        var mesh = Make("capsule", ("diameter", 20), ("height", 50), ("segments", 96), ("rings", 24));
        var expected = Math.PI * 100 * 30 + 4.0 / 3.0 * Math.PI * 1000;

        Assert.InRange(mesh.ComputeVolume(), expected * 0.99, expected);
        var size = mesh.ComputeBounds().Size;
        Assert.Equal(20, size.X, 3);
        Assert.Equal(50, size.Z, 3);
    }

    // ---- clean meshes at any resolution ----------------------------------------------------------

    [Theory]
    [InlineData("hemisphere", "segments", 3, "rings", 2)]
    [InlineData("hemisphere", "segments", 7, "rings", 5)]
    [InlineData("hemisphere", "segments", 128, "rings", 64)]
    [InlineData("cone", "segments", 3, "segments", 3)]
    [InlineData("cone", "segments", 512, "segments", 512)]
    [InlineData("frustum", "segments", 3, "segments", 3)]
    [InlineData("frustum", "segments", 100, "segments", 100)]
    [InlineData("prism", "sides", 3, "sides", 3)]
    [InlineData("prism", "sides", 64, "sides", 64)]
    [InlineData("torus", "segments", 3, "sides", 3)]
    [InlineData("torus", "segments", 17, "sides", 9)]
    [InlineData("torus", "segments", 256, "sides", 96)]
    [InlineData("tube", "segments", 3, "segments", 3)]
    [InlineData("tube", "segments", 200, "segments", 200)]
    [InlineData("capsule", "segments", 3, "rings", 1)]
    [InlineData("capsule", "segments", 11, "rings", 7)]
    [InlineData("capsule", "segments", 128, "rings", 48)]
    public void Shapes_are_closed_with_outward_normals_for_any_resolution(string id, string first, double a, string second, double b)
    {
        var mesh = Make(id, (first, a), (second, b));

        Assert.True(mesh.IsWatertight(), $"{id} {first}={a} {second}={b}");
        Assert.True(mesh.ComputeVolume() > 0);
    }

    // ---- impossible combinations are corrected, not rejected -------------------------------------

    [Fact]
    public void A_capsule_shorter_than_it_is_wide_becomes_a_sphere_shape_instead_of_an_error()
    {
        var mesh = Make("capsule", ("diameter", 40), ("height", 10));

        Assert.True(mesh.IsWatertight());
        Assert.Equal(40, mesh.ComputeBounds().Size.Z, 3); // as high as it is wide
    }

    [Fact]
    public void A_torus_tube_wider_than_the_ring_is_shrunk_to_fit()
    {
        var mesh = Make("torus", ("outerDiameter", 20), ("tubeDiameter", 30));

        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0);
        Assert.Equal(20, mesh.ComputeBounds().Size.X, 2);
        Assert.True(mesh.ComputeBounds().Size.Z <= 20);
    }

    [Fact]
    public void A_tube_with_a_wall_thicker_than_its_radius_keeps_a_small_hole()
    {
        var mesh = Make("tube", ("outerDiameter", 10), ("wall", 50));

        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0);
        Assert.Equal(10, mesh.ComputeBounds().Size.X, 2);
    }

    // ---- values that are really wrong ------------------------------------------------------------

    [Theory]
    [InlineData("hemisphere", "diameter", 0)]
    [InlineData("hemisphere", "rings", 1)]
    [InlineData("cone", "height", -1)]
    [InlineData("cone", "segments", 2)]
    [InlineData("frustum", "topDiameter", 0)]
    [InlineData("frustum", "bottomDiameter", double.NaN)]
    [InlineData("pyramid", "width", double.PositiveInfinity)]
    [InlineData("pyramid", "height", 0)]
    [InlineData("prism", "sides", 2)]
    [InlineData("prism", "sides", 65)]
    [InlineData("wedge", "depth", 0)]
    [InlineData("torus", "segments", 2)]
    [InlineData("torus", "tubeDiameter", 0)]
    [InlineData("tube", "wall", 0)]
    [InlineData("tube", "outerDiameter", -5)]
    [InlineData("capsule", "rings", 0)]
    [InlineData("capsule", "height", 0)]
    public void Invalid_values_are_rejected_with_a_readable_message(string id, string parameter, double value)
    {
        var error = Assert.Throws<ArgumentException>(() => Make(id, (parameter, value)));

        Assert.Contains("must be between", error.Message);
    }

    // ---- speed and scale bindings ----------------------------------------------------------------

    // F-LIB-01: changing a dimension updates the shape in well under 100 ms.
    [Fact]
    public void Regenerating_any_new_shape_is_fast_even_at_high_resolution()
    {
        foreach (var id in NewIds)
        {
            var generator = ShapeLibrary.Find(id)!;
            var high = generator.Parameters.Where(p => p.IsInteger).ToDictionary(p => p.Name, p => Math.Min(p.Max, p.Default * 2), StringComparer.OrdinalIgnoreCase);
            generator.Generate(high); // warm up
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (var i = 0; i < 5; i++) generator.Generate(high);

            Assert.True(watch.ElapsedMilliseconds / 5.0 < 100, $"{id}: {watch.ElapsedMilliseconds / 5.0} ms");
        }
    }

    [Fact]
    public void Details_are_sizes_that_set_no_extent_and_need_no_scale_binding()
    {
        var tube = ShapeLibrary.Find("tube")!;

        var wall = tube.Parameters.Single(p => p.Name == "wall");
        Assert.True(wall.IsDetail);
        Assert.DoesNotContain(tube.ScaleBindings, b => b.Parameter == "wall");
        Assert.Equal(["outerDiameter", "height"], tube.ScaleBindings.Select(b => b.Parameter).ToArray());
        // no first or old shape uses details
        Assert.All(new[] { "box", "cylinder", "sphere" }, id => Assert.DoesNotContain(ShapeLibrary.Find(id)!.Parameters, p => p.IsDetail));
    }

    [Fact]
    public void A_frustum_scales_both_diameters_with_the_plan_axes()
    {
        var frustum = ShapeLibrary.Find("frustum")!;

        Assert.Equal(ScaleAxes.X | ScaleAxes.Y, frustum.ScaleBindings.Single(b => b.Parameter == "bottomDiameter").Axes);
        Assert.Equal(ScaleAxes.X | ScaleAxes.Y, frustum.ScaleBindings.Single(b => b.Parameter == "topDiameter").Axes);
        Assert.Equal(ScaleAxes.Z, frustum.ScaleBindings.Single(b => b.Parameter == "height").Axes);
    }
}
