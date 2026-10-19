using System.Numerics;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The hatching of hole ghosts (F-VIS-04): stripes in world space, the same size at any zoom and for any object.</summary>
public class GhostHatchTests
{
    [Fact]
    public void One_period_along_the_stripe_normal_is_one_texture_unit()
    {
        var normal = Vector3.Normalize(new Vector3(1, 1, 1));
        var a = new Vector3(3, -7, 11);

        var shifted = a + normal * GhostHatch.PeriodMm;

        Assert.Equal(GhostHatch.Coordinate(a) + 1, GhostHatch.Coordinate(shifted), 4);
    }

    [Fact]
    public void Points_in_a_plane_across_the_stripe_direction_have_the_same_coordinate()
    {
        // x + y + z = 10 for all three: they lie on one stripe
        var coordinates = new[] { new Vector3(10, 0, 0), new Vector3(0, 10, 0), new Vector3(2, 3, 5), new Vector3(-5, 5, 10) }
            .Select(GhostHatch.Coordinate).ToList();

        Assert.All(coordinates, c => Assert.Equal(coordinates[0], c, 4));
    }

    [Fact]
    public void The_pattern_does_not_depend_on_the_size_of_the_object()
    {
        // the same world point gets the same coordinate whatever mesh it belongs to
        Assert.Equal(GhostHatch.Coordinate(new Vector3(1, 2, 3)), GhostHatch.Coordinate(new Vector3(1, 2, 3)));
        Assert.NotEqual(GhostHatch.Coordinate(new Vector3(0, 0, 0)), GhostHatch.Coordinate(new Vector3(1, 0, 0)));
    }

    // WPF 3D does not repeat texture coordinates outside 0..1 (it clamps them): the coordinates of one object are mapped
    // to 0..1 and the brush holds that many stripes inside that range.

    [Fact]
    public void The_layout_maps_the_coordinates_of_an_object_onto_zero_to_one()
    {
        var layout = GhostHatch.LayoutFor([2f, 7f, 12f]);

        Assert.Equal(0f, layout.Map(2f), 5);
        Assert.Equal(1f, layout.Map(12f), 5);
        Assert.Equal(0.5f, layout.Map(7f), 5);
    }

    [Fact]
    public void The_number_of_stripes_follows_the_size_of_the_object()
    {
        Assert.Equal(10, GhostHatch.LayoutFor([0f, 10f]).Periods);
        Assert.Equal(3, GhostHatch.LayoutFor([5f, 8.2f]).Periods);
    }

    [Fact]
    public void A_very_large_object_gets_wider_stripes_instead_of_more_of_them()
    {
        Assert.Equal(GhostHatch.MaxPeriods, GhostHatch.LayoutFor([0f, 5000f]).Periods);
    }

    [Fact]
    public void A_tiny_or_flat_object_gets_one_stripe_and_the_middle_of_it()
    {
        var flat = GhostHatch.LayoutFor([4f, 4f, 4f]);
        var tiny = GhostHatch.LayoutFor([0f, 0.2f]);

        Assert.Equal(1, flat.Periods);
        Assert.Equal(0.5f, flat.Map(4f));
        Assert.Equal(1, tiny.Periods);
    }

    [Fact]
    public void Without_coordinates_the_layout_is_harmless()
    {
        var layout = GhostHatch.LayoutFor([]);

        Assert.Equal(1, layout.Periods);
        Assert.Equal(0.5f, layout.Map(123f));
    }

    [Fact]
    public void Mapped_coordinates_never_leave_the_range()
    {
        var layout = GhostHatch.LayoutFor([-3f, 40f]);

        foreach (var c in new[] { -3f, 0f, 17.5f, 40f })
            Assert.InRange(layout.Map(c), 0f, 1f);
    }
}
