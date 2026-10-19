using System.Numerics;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

public class PixelSizeTests
{
    // F-SNP-06: a snap tolerance in pixels needs to know what a pixel is worth in the scene.
    [Theory]
    [InlineData(100, 800)]
    [InlineData(300, 1000)]
    [InlineData(1000, 1600)]
    public void Two_points_one_pixel_size_apart_are_one_pixel_apart_on_screen(double distance, double width)
    {
        var camera = new OrbitCamera { Distance = distance, YawDegrees = -40, PitchDegrees = 25, Target = new Vector3(10, 5, 20) };
        var wpp = camera.WorldPerPixelAt(camera.Target, width);

        // two points at the depth of the target, 100 pixels' worth apart, across the view
        var a = camera.Target;
        var b = camera.Target + camera.Right * (float)(wpp * 100);
        var pa = camera.Project(a, width, width * 0.6)!.Value;
        var pb = camera.Project(b, width, width * 0.6)!.Value;

        Assert.Equal(100, Math.Abs(pb.X - pa.X), 1);
        Assert.Equal(0, pb.Y - pa.Y, 1);
    }

    [Fact]
    public void Farther_away_means_more_world_per_pixel_and_zooming_in_means_less()
    {
        var camera = new OrbitCamera { Distance = 200 };
        var near = camera.WorldPerPixelAt(camera.Target, 1000);
        camera.Zoom(-3);
        var far = camera.WorldPerPixelAt(camera.Target, 1000);

        Assert.True(far > near);
    }

    [Fact]
    public void A_point_behind_the_camera_or_a_zero_width_view_has_no_pixel_size()
    {
        var camera = new OrbitCamera { Distance = 100 };
        Assert.Equal(0, camera.WorldPerPixelAt(camera.Position - camera.Forward * 5, 800));
        Assert.Equal(0, camera.WorldPerPixelAt(camera.Target, 0));
    }

    [Fact]
    public void The_drag_report_defaults_to_an_unknown_pixel_size()
    {
        var drag = new GizmoDrag(GizmoHandleKind.MoveX, Vector3.UnitX, 0, false);
        Assert.Equal(0, drag.WorldPerPixel);
        Assert.Equal(1, drag.ScaleFactor);
    }
}
