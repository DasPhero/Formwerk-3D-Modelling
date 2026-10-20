using Formwerk.App.ViewModels;
using Formwerk.Features.Shapes;

namespace Formwerk.Tests.App;

/// <summary>The cross-section picture of a hole (F-HOL-02): the body cut open with the hole in it.</summary>
public class HoleSectionTests
{
    private static HoleSection Section(params (string, double)[] values) =>
        HoleSection.From(ShapeLibrary.Hole.Profile(values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase)));

    private static double WidthAt(HoleSection section, double y)
    {
        var xs = section.HolePolygon.Where(p => Math.Abs(p.Y - y) < 0.01).Select(p => p.X).ToList();
        return xs.Count < 2 ? 0 : xs.Max() - xs.Min();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void The_hole_is_symmetric_about_the_axis_and_inside_the_picture(int type)
    {
        var section = Section(("type", type), ("depth", 12));

        var center = HoleSection.Width / 2;
        foreach (var point in section.HolePolygon)
        {
            Assert.InRange(point.X, 0, HoleSection.Width);
            Assert.InRange(point.Y, 0, HoleSection.Height);
            Assert.Contains(section.HolePolygon, other => Math.Abs((other.X - center) + (point.X - center)) < 0.01 && Math.Abs(other.Y - point.Y) < 0.01);
        }
    }

    [Fact]
    public void The_hole_starts_at_the_surface_of_the_body_not_above_it()
    {
        var section = Section(("type", 0));

        Assert.Equal(section.SurfaceY, section.HolePolygon.Min(p => p.Y), 3);
    }

    [Fact]
    public void The_body_is_a_block_around_the_hole_that_reaches_below_it()
    {
        var section = Section(("type", 0), ("depth", 10));

        Assert.True(section.BodyLeft < section.HolePolygon.Min(p => p.X));
        Assert.True(section.BodyRight > section.HolePolygon.Max(p => p.X));
        Assert.Equal(section.SurfaceY, section.BodyTop, 3);
        Assert.True(section.BodyBottom > section.HolePolygon.Max(p => p.Y));
        Assert.True(section.BodyBottom <= HoleSection.Height);
    }

    [Fact]
    public void A_counterbore_is_wider_at_the_top_than_at_the_bottom()
    {
        var section = Section(("type", 3), ("diameter", 3.4), ("sinkDiameter", 6.4), ("sinkDepth", 3), ("depth", 10));

        var bottom = section.HolePolygon.Max(p => p.Y);
        Assert.True(WidthAt(section, section.SurfaceY) > WidthAt(section, bottom) * 1.5);
    }

    [Fact]
    public void A_countersink_narrows_with_depth()
    {
        var section = Section(("type", 2), ("depth", 10));

        var ys = section.HolePolygon.Select(p => p.Y).Distinct().Order().ToList();
        Assert.True(WidthAt(section, ys[0]) > WidthAt(section, ys[^1]));
        Assert.True(ys.Count >= 3); // surface, end of the cone, bottom
    }

    [Fact]
    public void A_conical_hole_that_ends_in_a_point_has_no_width_at_the_bottom()
    {
        var section = Section(("type", 1), ("endDiameter", 0));

        Assert.Equal(1, section.HolePolygon.Count(p => Math.Abs(p.Y - section.HolePolygon.Max(q => q.Y)) < 0.01));
    }

    [Fact]
    public void A_deeper_hole_is_drawn_smaller_so_that_it_still_fits()
    {
        var shallow = Section(("type", 0), ("depth", 5));
        var deep = Section(("type", 0), ("depth", 50));

        Assert.True(WidthAt(deep, deep.SurfaceY) < WidthAt(shallow, shallow.SurfaceY));
        Assert.True(deep.BodyBottom <= HoleSection.Height);
    }

    [Fact]
    public void The_paths_are_written_with_a_dot_and_close_themselves()
    {
        var section = Section(("type", 2));

        foreach (var path in new[] { section.Body, section.Hole })
        {
            Assert.StartsWith("M", path);
            Assert.EndsWith("Z", path);
            Assert.DoesNotContain(';', path);
            Assert.Matches(@"^[MLZ0-9.,\- ]+$", path);
        }

        Assert.StartsWith("M", section.Axis);
    }

    [Fact]
    public void Without_an_outline_nothing_is_drawn()
    {
        var section = HoleSection.From([]);

        Assert.Empty(section.HolePolygon);
        Assert.Equal("", section.Hole);
    }
}
