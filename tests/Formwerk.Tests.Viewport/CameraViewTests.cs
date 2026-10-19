using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>Zoom to cursor, orthographic projection and animated view changes (F-VIS-06).</summary>
public class CameraViewTests
{
    private const double Width = 1000;
    private const double Height = 800;

    private static OrbitCamera Camera(bool orthographic) => new()
    {
        Target = new Vector3(10, 20, 30),
        Distance = 200,
        YawDegrees = -55,
        PitchDegrees = 28,
        IsOrthographic = orthographic,
    };

    /// <summary>The point of the plane through the target (perpendicular to the view direction) that appears at the pixel.</summary>
    private static Vector3 FocusPlanePoint(OrbitCamera camera, double x, double y)
    {
        var ray = camera.CreateRay(x, y, Width, Height);
        var t = Vector3.Dot(camera.Target - ray.Origin, camera.Forward) / Vector3.Dot(ray.Direction, camera.Forward);
        return ray.Origin + ray.Direction * t;
    }

    // ---- orthographic ------------------------------------------------------------------------------

    [Fact]
    public void Perspective_is_the_default()
    {
        Assert.False(new OrbitCamera().IsOrthographic);
    }

    [Fact]
    public void Orthographic_rays_run_parallel_from_different_places()
    {
        var camera = Camera(orthographic: true);

        var a = camera.CreateRay(100, 100, Width, Height);
        var b = camera.CreateRay(900, 700, Width, Height);

        Assert.True(Vector3.Dot(a.Direction, camera.Forward) > 0.99999f);
        Assert.True(Vector3.Dot(b.Direction, camera.Forward) > 0.99999f);
        Assert.NotEqual(a.Origin, b.Origin);
        Assert.Equal(0f, Vector3.Dot(a.Origin - b.Origin, camera.Forward), 3); // both start in the same plane
    }

    [Fact]
    public void Perspective_rays_still_start_at_the_camera()
    {
        var camera = Camera(orthographic: false);

        Assert.Equal(camera.Position, camera.CreateRay(100, 100, Width, Height).Origin);
        Assert.Equal(camera.Position, camera.CreateRay(900, 700, Width, Height).Origin);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_target_appears_in_the_middle_of_the_view(bool orthographic)
    {
        var camera = Camera(orthographic);

        var (x, y) = camera.Project(camera.Target, Width, Height)!.Value;

        Assert.Equal(Width / 2, x, 3);
        Assert.Equal(Height / 2, y, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Project_is_the_inverse_of_the_ray(bool orthographic)
    {
        var camera = Camera(orthographic);

        foreach (var (px, py) in new[] { (100.0, 100.0), (900.0, 700.0), (500.0, 400.0), (50.0, 750.0) })
        {
            var ray = camera.CreateRay(px, py, Width, Height);
            var projected = camera.Project(ray.Origin + ray.Direction * 150, Width, Height)!.Value;

            Assert.Equal(px, projected.X, 2);
            Assert.Equal(py, projected.Y, 2);
        }
    }

    [Fact]
    public void An_object_at_the_target_has_the_same_size_in_both_projections()
    {
        // so that switching the projection does not make the picture jump
        var side = Camera(false).Right * 15;
        var perspective = Camera(false);
        var orthographic = Camera(true);

        var a = perspective.Project(perspective.Target + side, Width, Height)!.Value;
        var b = orthographic.Project(orthographic.Target + side, Width, Height)!.Value;

        Assert.Equal(a.X, b.X, 3);
        Assert.Equal(a.Y, b.Y, 3);
    }

    [Fact]
    public void In_the_orthographic_view_the_size_does_not_depend_on_the_distance_from_the_camera()
    {
        var camera = Camera(orthographic: true);
        var near = camera.Target - camera.Forward * 50;
        var far = camera.Target + camera.Forward * 300;

        Assert.Equal(camera.WorldPerPixelAt(near, Width), camera.WorldPerPixelAt(far, Width), 6);
        Assert.Equal(Camera(false).WorldPerPixelAt(camera.Target, Width), camera.WorldPerPixelAt(far, Width), 6);

        var a = camera.Project(near + camera.Right * 10, Width, Height)!.Value;
        var b = camera.Project(far + camera.Right * 10, Width, Height)!.Value;
        Assert.Equal(a.X, b.X, 3);
    }

    [Fact]
    public void In_the_perspective_view_the_size_shrinks_with_the_distance()
    {
        var camera = Camera(orthographic: false);

        Assert.True(camera.WorldPerPixelAt(camera.Target + camera.Forward * 300, Width) > camera.WorldPerPixelAt(camera.Target - camera.Forward * 50, Width));
    }

    [Fact]
    public void Points_behind_the_camera_plane_are_not_projected_in_either_projection()
    {
        foreach (var orthographic in new[] { false, true })
        {
            var camera = Camera(orthographic);

            Assert.Null(camera.Project(camera.Position - camera.Forward * 10, Width, Height));
        }
    }

    // ---- zoom to cursor ----------------------------------------------------------------------------

    [Theory]
    [InlineData(false, 3, 200, 150)]
    [InlineData(true, 3, 200, 150)]
    [InlineData(false, -2, 850, 700)]
    [InlineData(true, -2, 850, 700)]
    [InlineData(false, 1, 500, 400)]
    [InlineData(true, 5, 20, 780)]
    public void The_point_under_the_cursor_stays_under_the_cursor(bool orthographic, double steps, double x, double y)
    {
        var camera = Camera(orthographic);
        var point = FocusPlanePoint(camera, x, y);

        camera.ZoomAt(steps, x, y, Width, Height);

        var after = camera.Project(point, Width, Height)!.Value;
        Assert.Equal(x, after.X, 2);
        Assert.Equal(y, after.Y, 2);
    }

    [Fact]
    public void Zooming_in_at_the_cursor_shortens_the_distance_like_a_plain_zoom()
    {
        var plain = Camera(false);
        var atCursor = Camera(false);

        plain.Zoom(2);
        atCursor.ZoomAt(2, 123, 456, Width, Height);

        Assert.Equal(plain.Distance, atCursor.Distance, 6);
    }

    [Fact]
    public void Zooming_at_the_center_of_the_view_does_not_move_the_target()
    {
        var camera = Camera(false);
        var target = camera.Target;

        camera.ZoomAt(3, Width / 2, Height / 2, Width, Height);

        Assert.Equal(target.X, camera.Target.X, 3);
        Assert.Equal(target.Y, camera.Target.Y, 3);
        Assert.Equal(target.Z, camera.Target.Z, 3);
    }

    [Fact]
    public void Zooming_in_moves_the_target_towards_the_cursor_and_zooming_out_away_from_it()
    {
        var right = Camera(false);
        var pointRight = FocusPlanePoint(right, 900, 400);
        var startDistance = Vector3.Distance(right.Target, pointRight);

        right.ZoomAt(2, 900, 400, Width, Height);
        Assert.True(Vector3.Distance(right.Target, pointRight) < startDistance);

        var out2 = Camera(false);
        out2.ZoomAt(-2, 900, 400, Width, Height);
        Assert.True(Vector3.Distance(out2.Target, pointRight) > startDistance);
    }

    [Fact]
    public void At_the_limit_of_the_distance_the_target_does_not_drift()
    {
        var camera = Camera(false);
        camera.Distance = 1; // the smallest allowed
        var target = camera.Target;

        camera.ZoomAt(5, 100, 100, Width, Height);

        Assert.Equal(1, camera.Distance, 6);
        Assert.Equal(target, camera.Target);
    }

    [Fact]
    public void Zoom_without_a_view_size_falls_back_to_a_plain_zoom()
    {
        var camera = Camera(false);
        var target = camera.Target;

        camera.ZoomAt(2, 10, 10, 0, 0);

        Assert.Equal(target, camera.Target);
        Assert.True(camera.Distance < 200);
    }

    // ---- states and animation ----------------------------------------------------------------------

    [Fact]
    public void The_state_of_the_camera_can_be_read_and_set()
    {
        var camera = Camera(false);
        var state = camera.State;

        camera.State = new CameraState(new Vector3(1, 2, 3), 50, 10, 20);

        Assert.Equal(new Vector3(1, 2, 3), camera.Target);
        Assert.Equal(50, camera.Distance);
        Assert.Equal(10, camera.YawDegrees);
        Assert.Equal(20, camera.PitchDegrees);
        camera.State = state;
        Assert.Equal(state, camera.State);
    }

    [Fact]
    public void A_preset_state_changes_only_the_direction()
    {
        var camera = Camera(false);

        var top = camera.PresetState(ViewPreset.Top);

        Assert.Equal(camera.Target, top.Target);
        Assert.Equal(camera.Distance, top.Distance);
        Assert.True(top.PitchDegrees > 89);
        Assert.Equal(28, camera.PitchDegrees); // the camera itself is untouched
    }

    [Fact]
    public void SetPreset_goes_to_the_preset_state()
    {
        var a = Camera(false);
        var b = Camera(false);

        a.SetPreset(ViewPreset.Left);
        b.State = b.PresetState(ViewPreset.Left);

        Assert.Equal(a.State, b.State);
    }

    [Fact]
    public void A_framed_state_is_what_Frame_does()
    {
        var bounds = new Bounds(new Vector3(-50, -50, 0), new Vector3(50, 50, 100));
        var a = Camera(false);
        var b = Camera(false);

        var framed = a.FramedState(bounds);
        b.Frame(bounds);

        Assert.Equal(b.State, framed);
        Assert.Equal(-55, framed.YawDegrees); // the viewing direction is kept
    }

    private static readonly CameraState From = new(new Vector3(0, 0, 0), 100, -90, 10);
    private static readonly CameraState To = new(new Vector3(40, -20, 10), 300, 0, 80);

    [Fact]
    public void The_animation_starts_at_the_first_state_and_ends_at_the_second()
    {
        Assert.Equal(From, CameraAnimation.Interpolate(From, To, 0));
        Assert.Equal(To, CameraAnimation.Interpolate(From, To, 1));
        Assert.Equal(From, CameraAnimation.Interpolate(From, To, -3));
        Assert.Equal(To, CameraAnimation.Interpolate(From, To, 7));
    }

    [Fact]
    public void The_animation_runs_steadily_forward_and_slows_down_at_the_end()
    {
        var distances = new[] { 0.0, 0.25, 0.5, 0.75, 1.0 }.Select(t => CameraAnimation.Interpolate(From, To, t).Distance).ToList();

        for (var i = 1; i < distances.Count; i++) Assert.True(distances[i] > distances[i - 1]);
        Assert.True(distances[1] - distances[0] > distances[4] - distances[3], "ease out: fast at the start, slow at the end");
    }

    [Fact]
    public void The_animation_turns_the_short_way_round()
    {
        var a = new CameraState(Vector3.Zero, 100, 170, 0);
        var b = new CameraState(Vector3.Zero, 100, -170, 0);

        // 170 -> -170 is a turn of 20 degrees through 180, not of 340 degrees back through 0
        foreach (var t in new[] { 0.1, 0.3, 0.5, 0.7, 0.9 })
        {
            var yaw = CameraAnimation.Interpolate(a, b, t).YawDegrees;
            Assert.InRange(yaw, 170, 190);
        }
    }

    [Fact]
    public void The_animation_ends_exactly_on_the_goal_even_when_it_turned_across_the_seam()
    {
        var a = new CameraState(Vector3.Zero, 100, 170, 0);
        var b = new CameraState(Vector3.Zero, 100, -170, 0);

        var end = CameraAnimation.Interpolate(a, b, 1);

        Assert.Equal(b, end);
    }

    [Fact]
    public void Progress_follows_the_time_and_stops_at_one()
    {
        Assert.Equal(0, CameraAnimation.Progress(0));
        Assert.Equal(0.5, CameraAnimation.Progress(CameraAnimation.DurationSeconds / 2), 6);
        Assert.Equal(1, CameraAnimation.Progress(CameraAnimation.DurationSeconds));
        Assert.Equal(1, CameraAnimation.Progress(10));
        Assert.Equal(0, CameraAnimation.Progress(-1));
    }

    [Fact]
    public void A_state_that_is_already_there_needs_no_animation()
    {
        Assert.False(CameraAnimation.IsMoving(From, From));
        Assert.True(CameraAnimation.IsMoving(From, To));
        Assert.True(CameraAnimation.IsMoving(From, From with { PitchDegrees = 10.5 }));
    }
}
