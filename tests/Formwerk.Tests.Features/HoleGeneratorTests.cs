using Formwerk.Features.Shapes;

namespace Formwerk.Tests.Features;

/// <summary>The hole shapes (F-HOL-02): four types, a cutter whose axis is +Z, whose surface is Z = 0 and which reaches 1 mm above it.</summary>
public class HoleGeneratorTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static readonly HoleGenerator Generator = new();

    /// <summary>Volume of a prism with a regular polygon of 'segments' corners and the circumscribed radius r.</summary>
    private static double PolygonArea(double r, int segments) => segments / 2.0 * r * r * Math.Sin(2 * Math.PI / segments);

    [Fact]
    public void The_hole_is_found_by_its_id_but_is_not_a_shape_of_the_shape_menu()
    {
        Assert.Same(Generator.GetType(), ShapeLibrary.Find("hole")!.GetType());
        Assert.DoesNotContain(ShapeLibrary.All, g => g.Id == "hole");
    }

    [Fact]
    public void All_sizes_are_details_so_that_the_scale_handles_leave_the_hole_alone()
    {
        Assert.All(Generator.Parameters.Where(p => !p.IsInteger), p => Assert.True(p.IsDetail, p.Name));
        Assert.Empty(Generator.ScaleBindings);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Every_type_is_a_closed_solid_with_outward_normals(int type)
    {
        var mesh = Generator.Generate(P(("type", type)));
        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0);
    }

    [Fact]
    public void A_cylindrical_hole_goes_from_one_millimetre_above_the_surface_down_to_the_depth()
    {
        var mesh = Generator.Generate(P(("type", 0), ("diameter", 3.4), ("depth", 10), ("segments", 64)));

        var bounds = mesh.ComputeBounds();
        Assert.Equal(-10, bounds.Min.Z, 4);
        Assert.Equal(1, bounds.Max.Z, 4);
        Assert.Equal(3.4, bounds.Size.X, 2);
        Assert.Equal(PolygonArea(1.7, 64) * 11, mesh.ComputeVolume(), 2);
    }

    [Fact]
    public void A_conical_hole_narrows_from_the_diameter_at_the_surface_to_the_end_diameter()
    {
        var mesh = Generator.Generate(P(("type", 1), ("diameter", 6), ("endDiameter", 2), ("depth", 8), ("segments", 64)));

        // collar of 1 mm with the full diameter, then a frustum of 8 mm: V = pi*h/3 * (R^2 + R*r + r^2)
        var area = PolygonArea(3, 64) / (Math.PI * 9); // ratio of the polygon to the circle
        var frustum = Math.PI * 8 / 3 * (9 + 3 + 1);
        var collar = Math.PI * 9 * 1;
        Assert.Equal((frustum + collar) * area, mesh.ComputeVolume(), 1);
        Assert.Equal(-8, mesh.ComputeBounds().Min.Z, 4);
    }

    [Fact]
    public void A_conical_hole_with_end_diameter_zero_ends_in_a_point()
    {
        var mesh = Generator.Generate(P(("type", 1), ("diameter", 6), ("endDiameter", 0), ("depth", 8)));

        Assert.True(mesh.IsWatertight());
        Assert.Equal(-8, mesh.ComputeBounds().Min.Z, 4);
    }

    [Fact]
    public void A_countersink_widens_the_hole_at_the_surface_with_the_given_angle()
    {
        // d = 3.4, head 6.4, 90 degrees: the cone is (6.4 - 3.4) / 2 = 1.5 mm deep
        var profile = Generator.Profile(P(("type", 2), ("diameter", 3.4), ("sinkDiameter", 6.4), ("sinkAngle", 90), ("depth", 10)));

        Assert.Contains(profile, p => Math.Abs(p.R - 3.2) < 1e-4 && Math.Abs(p.Z) < 1e-4);
        Assert.Contains(profile, p => Math.Abs(p.R - 1.7) < 1e-4 && Math.Abs(p.Z + 1.5) < 1e-4);
        Assert.Contains(profile, p => Math.Abs(p.R - 1.7) < 1e-4 && Math.Abs(p.Z + 10) < 1e-4);
    }

    [Fact]
    public void A_narrower_angle_makes_the_countersink_deeper()
    {
        var deep = Generator.Profile(P(("type", 2), ("diameter", 3.4), ("sinkDiameter", 6.4), ("sinkAngle", 60), ("depth", 10)));

        // tan(30 degrees) = 0.57735: 1.5 / 0.57735 = 2.598 mm
        var wall = deep.Single(p => Math.Abs(p.R - 1.7) < 1e-4 && p.Z < -1 && p.Z > -9);
        Assert.Equal(-2.598, wall.Z, 2);
    }

    [Fact]
    public void A_countersink_deeper_than_the_hole_is_cut_off_at_the_depth()
    {
        var mesh = Generator.Generate(P(("type", 2), ("diameter", 3), ("sinkDiameter", 30), ("sinkAngle", 90), ("depth", 2)));

        Assert.True(mesh.IsWatertight());
        Assert.Equal(-2, mesh.ComputeBounds().Min.Z, 4);
        Assert.True(mesh.ComputeVolume() > 0);
    }

    [Fact]
    public void A_counterbore_has_a_wide_part_of_the_given_depth_above_the_narrow_one()
    {
        var mesh = Generator.Generate(P(("type", 3), ("diameter", 3.4), ("sinkDiameter", 6), ("sinkDepth", 3), ("depth", 10), ("segments", 64)));

        var expected = PolygonArea(3, 64) * 4 + PolygonArea(1.7, 64) * 7; // 3 deep plus the 1 mm above, then 7 mm of the narrow part
        Assert.Equal(expected, mesh.ComputeVolume(), 2);
        Assert.Equal(6, mesh.ComputeBounds().Size.X, 2);
    }

    [Fact]
    public void A_counterbore_deeper_than_the_hole_leaves_only_the_wide_part()
    {
        var mesh = Generator.Generate(P(("type", 3), ("diameter", 3.4), ("sinkDiameter", 6), ("sinkDepth", 30), ("depth", 5)));

        Assert.True(mesh.IsWatertight());
        Assert.Equal(-5, mesh.ComputeBounds().Min.Z, 4);
    }

    [Fact]
    public void A_sink_not_wider_than_the_hole_gives_a_plain_cylinder()
    {
        var plain = Generator.Generate(P(("type", 0), ("diameter", 5), ("depth", 10)));
        Assert.Equal(plain.ComputeVolume(), Generator.Generate(P(("type", 2), ("diameter", 5), ("sinkDiameter", 4), ("depth", 10))).ComputeVolume(), 3);
        Assert.Equal(plain.ComputeVolume(), Generator.Generate(P(("type", 3), ("diameter", 5), ("sinkDiameter", 5), ("depth", 10))).ComputeVolume(), 3);
    }

    [Fact]
    public void The_profile_starts_and_ends_on_the_axis_and_lies_in_front_of_it()
    {
        foreach (var type in new[] { 0, 1, 2, 3 })
        {
            var profile = Generator.Profile(P(("type", type)));
            Assert.Equal(0, profile[0].R);
            Assert.Equal(0, profile[^1].R);
            Assert.All(profile, p => Assert.True(p.R >= 0));
            Assert.Equal(1, profile[0].Z, 4);
        }
    }

    [Theory]
    [InlineData("type", 4)]
    [InlineData("diameter", 0)]
    [InlineData("depth", 0)]
    [InlineData("sinkAngle", 5)]
    public void Values_outside_the_range_are_rejected(string name, double value) =>
        Assert.Throws<ArgumentException>(() => Generator.Generate(P((name, value))));
}
