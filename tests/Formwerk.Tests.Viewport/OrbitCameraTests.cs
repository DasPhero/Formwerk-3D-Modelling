using System.Numerics;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

public class OrbitCameraTests
{
    private static OrbitCamera Camera() => new() { Target = new Vector3(0, 0, 10), Distance = 100 };

    [Theory]
    [InlineData(ViewPreset.Front, 0, -100, 10)]
    [InlineData(ViewPreset.Back, 0, 100, 10)]
    [InlineData(ViewPreset.Left, -100, 0, 10)]
    [InlineData(ViewPreset.Right, 100, 0, 10)]
    public void Side_presets_look_from_the_expected_side(ViewPreset preset, float x, float y, float z)
    {
        var camera = Camera();
        camera.SetPreset(preset);
        Assert.Equal(x, camera.Position.X, 1);
        Assert.Equal(y, camera.Position.Y, 1);
        Assert.Equal(z, camera.Position.Z, 1);
    }

    [Fact]
    public void Top_view_looks_straight_down()
    {
        var camera = Camera();
        camera.SetPreset(ViewPreset.Top);
        Assert.True(camera.Position.Z > 105);
        Assert.True(camera.Forward.Z < -0.99f);
    }

    [Fact]
    public void Camera_always_looks_at_the_target()
    {
        var camera = Camera();
        camera.Orbit(137, -42);
        var toTarget = Vector3.Normalize(camera.Target - camera.Position);
        Assert.True(Vector3.Dot(toTarget, camera.Forward) > 0.9999f);
        Assert.Equal(100f, Vector3.Distance(camera.Position, camera.Target), 2);
    }

    [Fact]
    public void Pitch_is_clamped_so_the_view_never_flips()
    {
        var camera = Camera();
        camera.Orbit(0, 100000);
        Assert.True(camera.PitchDegrees <= 89.5);
        camera.Orbit(0, -1000000);
        Assert.True(camera.PitchDegrees >= -89.5);
    }

    [Fact]
    public void Zoom_in_shortens_and_zoom_out_lengthens_the_distance()
    {
        var camera = Camera();
        camera.Zoom(1);
        Assert.True(camera.Distance < 100);
        camera.Zoom(-3);
        Assert.True(camera.Distance > 100);
        camera.Zoom(-1000);
        Assert.True(camera.Distance <= 100000);
    }

    [Fact]
    public void Pan_moves_the_target_parallel_to_the_screen()
    {
        var camera = Camera();
        var forward = camera.Forward;
        camera.Pan(100, 50, 1000);
        Assert.Equal(0f, Vector3.Dot(camera.Target - new Vector3(0, 0, 10), forward), 3);
        Assert.NotEqual(new Vector3(0, 0, 10), camera.Target);
    }

    [Fact]
    public void Frame_centers_the_bounds_and_keeps_them_in_view()
    {
        var camera = Camera();
        var bounds = new Bounds(new Vector3(-50, -50, 0), new Vector3(50, 50, 100));
        camera.Frame(bounds);

        Assert.Equal(bounds.Center, camera.Target);
        var radius = bounds.Size.Length() / 2;
        Assert.True(camera.Distance > radius);
    }
}
