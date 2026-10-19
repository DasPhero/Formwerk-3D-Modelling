using System.Numerics;
using System.Windows.Media.Media3D;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The geometry and colors of edge lines (F-VIS-02).</summary>
public class EdgeLinesTests
{
    private static EdgeSet Segments(params (Vector3, Vector3)[] segments) => new([.. segments.SelectMany(s => new[] { s.Item1, s.Item2 })]);

    private static Vector3 V(Point3D p) => new((float)p.X, (float)p.Y, (float)p.Z);

    // ---- geometry --------------------------------------------------------------------------------

    [Fact]
    public void One_edge_is_two_crossing_quads()
    {
        var geometry = EdgeGeometry.Build([Segments((new Vector3(0, 0, 0), new Vector3(10, 0, 0)))], halfWidth: 0.1);

        Assert.Equal(8, geometry.Positions.Count);
        Assert.Equal(12, geometry.TriangleIndices.Count); // four triangles
        Assert.All(geometry.TriangleIndices, i => Assert.InRange(i, 0, 7));
    }

    [Fact]
    public void The_lines_run_from_one_end_of_the_edge_to_the_other_and_have_the_given_width()
    {
        var a = new Vector3(2, 3, 4);
        var b = new Vector3(12, 3, 4);
        var geometry = EdgeGeometry.Build([Segments((a, b))], halfWidth: 0.25);

        var points = geometry.Positions.Select(V).ToList();
        Assert.Equal(2f, points.Min(p => p.X), 4);
        Assert.Equal(12f, points.Max(p => p.X), 4);
        // seen from the side the line is 2 x half width wide in both crossing directions
        Assert.Equal(0.5f, points.Max(p => p.Y) - points.Min(p => p.Y), 4);
        Assert.Equal(0.5f, points.Max(p => p.Z) - points.Min(p => p.Z), 4);
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(0, 1, 0)]
    [InlineData(3, -2, 7)]
    public void An_edge_in_any_direction_gets_a_line_of_the_same_thickness(float x, float y, float z)
    {
        var direction = Vector3.Normalize(new Vector3(x, y, z));
        var geometry = EdgeGeometry.Build([Segments((Vector3.Zero, direction * 10))], halfWidth: 0.2);

        foreach (var p in geometry.Positions.Select(V))
        {
            var along = Vector3.Dot(p, direction);
            var distance = (p - direction * along).Length();
            Assert.Equal(0.2f, distance, 3); // every corner is half a width away from the edge
        }
    }

    [Fact]
    public void Several_edge_sets_are_put_into_one_geometry_and_zero_length_edges_are_skipped()
    {
        var first = Segments((Vector3.Zero, new Vector3(5, 0, 0)), (new Vector3(1, 1, 1), new Vector3(1, 1, 1)));
        var second = Segments((Vector3.Zero, new Vector3(0, 5, 0)), (Vector3.Zero, new Vector3(0, 0, 5)));

        var geometry = EdgeGeometry.Build([first, second], halfWidth: 0.1);

        Assert.Equal(3 * 8, geometry.Positions.Count);
        Assert.Equal(3 * 12, geometry.TriangleIndices.Count);
    }

    [Fact]
    public void No_edges_give_an_empty_geometry()
    {
        var geometry = EdgeGeometry.Build([], halfWidth: 0.1);

        Assert.Empty(geometry.Positions);
        Assert.Empty(geometry.TriangleIndices);
    }

    [Fact]
    public void The_geometry_can_be_frozen_for_use_on_any_thread()
    {
        var geometry = EdgeGeometry.Build([Segments((Vector3.Zero, Vector3.UnitX))], halfWidth: 0.1);

        Assert.True(geometry.IsFrozen);
    }

    // ---- width -----------------------------------------------------------------------------------

    [Fact]
    public void The_half_width_is_half_the_line_width_in_pixels_times_the_pixel_size()
    {
        Assert.Equal(0.4, EdgeGeometry.HalfWidth(worldPerPixel: 0.5, pixels: 1.6), 6);
        Assert.Equal(0, EdgeGeometry.HalfWidth(worldPerPixel: 0, pixels: 1.6));
    }

    [Theory]
    [InlineData(1.0, 1.05, false)]
    [InlineData(1.0, 1.19, false)]
    [InlineData(1.0, 1.25, true)]
    [InlineData(1.0, 0.75, true)]
    [InlineData(1.0, 0.85, false)]
    [InlineData(0.0, 1.0, true)]
    public void The_lines_are_rebuilt_only_when_the_zoom_changed_noticeably(double built, double wanted, bool rebuild)
    {
        Assert.Equal(rebuild, EdgeGeometry.NeedsRebuild(built, wanted));
    }

    // ---- colors ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(0xFFB0B8C4u)]  // the default grey
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0xFFFFD84Cu)]
    public void Light_objects_get_dark_edges(uint color)
    {
        var edge = EdgeColors.For(color, selected: false);

        Assert.True(Luminance(edge) < 80, $"edge luminance {Luminance(edge)}");
    }

    [Theory]
    [InlineData(0xFF101418u)]
    [InlineData(0xFF000000u)]
    [InlineData(0xFF1F3A8Au)]
    public void Dark_objects_get_light_edges(uint color)
    {
        var edge = EdgeColors.For(color, selected: false);

        Assert.True(Luminance(edge) > 170, $"edge luminance {Luminance(edge)}");
    }

    [Theory]
    [InlineData(0xFFB0B8C4u)]
    [InlineData(0xFF101418u)]
    [InlineData(0xFFE53935u)]
    public void Edges_stand_out_from_the_surface_by_a_good_margin(uint color)
    {
        foreach (var selected in new[] { false, true })
        {
            var surface = Luminance((byte)(color >> 16), (byte)(color >> 8), (byte)color);
            var edge = EdgeColors.For(color, selected);

            Assert.True(Math.Abs(Luminance(edge) - surface) > 70, $"color {color:X8} selected {selected}: surface {surface:0}, edge {Luminance(edge):0}");
        }
    }

    [Theory]
    [InlineData(0xFFB0B8C4u)]
    [InlineData(0xFF101418u)]
    public void In_the_wireframe_the_lines_are_light_whatever_the_object_color(uint color)
    {
        Assert.True(Luminance(EdgeColors.For(color, selected: false, seeThrough: true)) > 170);
        Assert.True(Luminance(EdgeColors.For(color, selected: true, seeThrough: true)) > 170);
    }

    [Fact]
    public void Selected_objects_get_a_blue_tinted_edge()
    {
        var plain = EdgeColors.For(0xFFB0B8C4u, selected: false);
        var selected = EdgeColors.For(0xFFB0B8C4u, selected: true);

        Assert.NotEqual(plain, selected);
        Assert.True(selected.B >= selected.R);
    }

    private static double Luminance((byte R, byte G, byte B) c) => Luminance(c.R, c.G, c.B);

    private static double Luminance(byte r, byte g, byte b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;
}
