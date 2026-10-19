using System.Numerics;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

public class GizmoMathTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    // ---- ray to axis ---------------------------------------------------------------------------

    [Fact]
    public void Ray_passing_above_the_axis_gives_the_nearest_point_on_the_axis()
    {
        var ray = new Ray(new Vector3(3, 2, 10), new Vector3(0, 0, -1));
        var t = GizmoMath.ClosestParameterOnLine(ray, Vector3.Zero, Vector3.UnitX);
        Assert.Equal(3f, t!.Value, 4);
    }

    [Fact]
    public void Axis_parameter_respects_the_line_origin_and_direction_length()
    {
        var ray = new Ray(new Vector3(8, 0, 5), new Vector3(0, 0, -1));
        var t = GizmoMath.ClosestParameterOnLine(ray, new Vector3(2, 0, 0), new Vector3(2, 0, 0));
        Assert.Equal(3f, t!.Value, 4); // 2 + 3 * 2 = 8
    }

    [Fact]
    public void Slanted_ray_still_finds_the_closest_point()
    {
        var origin = new Vector3(0, -10, 10);
        var target = new Vector3(6, 0, 0); // a point on the X axis
        var ray = new Ray(origin, Vector3.Normalize(target - origin));
        Assert.Equal(6f, GizmoMath.ClosestParameterOnLine(ray, Vector3.Zero, Vector3.UnitX)!.Value, 3);
    }

    [Fact]
    public void Ray_parallel_to_the_axis_has_no_answer()
    {
        var ray = new Ray(new Vector3(0, 5, 5), Vector3.UnitX);
        Assert.Null(GizmoMath.ClosestParameterOnLine(ray, Vector3.Zero, Vector3.UnitX));
    }

    // ---- ray to plane --------------------------------------------------------------------------

    [Fact]
    public void Ray_hits_a_plane()
    {
        var ray = new Ray(new Vector3(1, 2, 10), new Vector3(0, 0, -1));
        Assert.True(GizmoMath.IntersectPlane(ray, new Vector3(0, 0, 3), Vector3.UnitZ, out var hit));
        AssertNear(new Vector3(1, 2, 3), hit);
    }

    [Fact]
    public void Slanted_ray_hits_the_plane_at_the_expected_point()
    {
        var ray = new Ray(new Vector3(0, 0, 10), Vector3.Normalize(new Vector3(1, 1, -1)));
        Assert.True(GizmoMath.IntersectPlane(ray, Vector3.Zero, Vector3.UnitZ, out var hit));
        AssertNear(new Vector3(10, 10, 0), hit);
    }

    [Fact]
    public void Parallel_ray_or_plane_behind_the_camera_gives_no_hit()
    {
        Assert.False(GizmoMath.IntersectPlane(new Ray(new Vector3(0, 0, 5), Vector3.UnitX), Vector3.Zero, Vector3.UnitZ, out _));
        Assert.False(GizmoMath.IntersectPlane(new Ray(new Vector3(0, 0, 5), Vector3.UnitZ), Vector3.Zero, Vector3.UnitZ, out _));
    }

    // ---- angles --------------------------------------------------------------------------------

    [Fact]
    public void Signed_angle_is_right_handed()
    {
        Assert.Equal(90, GizmoMath.SignedAngleDegrees(Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ), 4);
        Assert.Equal(-90, GizmoMath.SignedAngleDegrees(Vector3.UnitX, Vector3.UnitY, -Vector3.UnitZ), 4);
        Assert.Equal(-90, GizmoMath.SignedAngleDegrees(Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ), 4);
        Assert.Equal(0, GizmoMath.SignedAngleDegrees(Vector3.UnitX, Vector3.UnitX, Vector3.UnitZ), 4);
        Assert.Equal(180, Math.Abs(GizmoMath.SignedAngleDegrees(Vector3.UnitX, -Vector3.UnitX, Vector3.UnitZ)), 4);
    }

    [Theory]
    [InlineData(170, -170, 190)]
    [InlineData(-170, 170, -190)]
    [InlineData(0, 10, 10)]
    [InlineData(350, -10, 350)]
    [InlineData(10, 350, -10)]
    public void Unwrap_keeps_the_angle_continuous(double previous, double current, double expected)
    {
        Assert.Equal(expected, GizmoMath.Unwrap(previous, current), 6);
    }

    [Fact]
    public void Dragging_around_a_full_circle_accumulates_to_360()
    {
        var total = 0.0;
        var last = 0.0;
        for (var degrees = 10; degrees <= 360; degrees += 10)
        {
            var from = Vector3.UnitX;
            var to = new Vector3((float)Math.Cos(degrees * Math.PI / 180), (float)Math.Sin(degrees * Math.PI / 180), 0);
            var raw = GizmoMath.SignedAngleDegrees(from, to, Vector3.UnitZ);
            total = GizmoMath.Unwrap(last, raw);
            last = total;
        }

        Assert.Equal(360, total, 2);
    }

    [Fact]
    public void Handles_know_their_axis_and_plane()
    {
        Assert.Equal(Vector3.UnitY, GizmoHandles.Axis(GizmoHandleKind.MoveY));
        Assert.Equal(Vector3.UnitZ, GizmoHandles.Axis(GizmoHandleKind.RotateZ));
        Assert.Equal(Vector3.Zero, GizmoHandles.Axis(GizmoHandleKind.MovePlaneXY));
        Assert.Equal(Vector3.UnitZ, GizmoHandles.PlaneNormal(GizmoHandleKind.MovePlaneXY));
        Assert.Equal(Vector3.UnitY, GizmoHandles.PlaneNormal(GizmoHandleKind.MovePlaneXZ));
        Assert.Equal(Vector3.UnitX, GizmoHandles.PlaneNormal(GizmoHandleKind.MovePlaneYZ));
        Assert.True(GizmoHandles.IsRotation(GizmoHandleKind.RotateX));
        Assert.True(GizmoHandles.IsPlane(GizmoHandleKind.MovePlaneYZ));
        Assert.False(GizmoHandles.IsPlane(GizmoHandleKind.MoveX));
    }

    // ---- camera rays ---------------------------------------------------------------------------

    [Fact]
    public void Ray_through_the_center_pixel_follows_the_view_direction()
    {
        var camera = new OrbitCamera { Target = new Vector3(5, 5, 5), Distance = 200 };
        var ray = camera.CreateRay(400, 300, 800, 600);

        AssertNear(camera.Position, ray.Origin);
        AssertNear(camera.Forward, ray.Direction, digits: 4);
    }

    [Fact]
    public void Ray_through_the_right_edge_is_half_the_field_of_view_away()
    {
        var camera = new OrbitCamera();
        var ray = camera.CreateRay(800, 300, 800, 600);

        var angle = Math.Acos(Vector3.Dot(ray.Direction, camera.Forward)) * 180 / Math.PI;
        Assert.Equal(OrbitCamera.FieldOfViewDegrees / 2, angle, 3);
        Assert.True(Vector3.Dot(ray.Direction, camera.Right) > 0);
    }

    [Fact]
    public void Ray_through_the_top_of_the_view_points_upwards()
    {
        var camera = new OrbitCamera();
        Assert.True(Vector3.Dot(camera.CreateRay(400, 0, 800, 600).Direction, camera.Up) > 0);
        Assert.True(Vector3.Dot(camera.CreateRay(400, 600, 800, 600).Direction, camera.Up) < 0);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(20, -15, 40)]
    [InlineData(-60, 30, 5)]
    [InlineData(100, 100, 100)]
    public void Projecting_a_point_and_casting_a_ray_through_that_pixel_meets_the_point(float x, float y, float z)
    {
        var camera = new OrbitCamera { YawDegrees = -30, PitchDegrees = 40, Distance = 300, Target = new Vector3(10, 0, 20) };
        var point = new Vector3(x, y, z);

        var pixel = camera.Project(point, 1000, 700);
        Assert.NotNull(pixel);
        var ray = camera.CreateRay(pixel.Value.X, pixel.Value.Y, 1000, 700);

        // distance of the point from the ray must be (almost) zero
        var toPoint = point - ray.Origin;
        var closest = ray.Origin + ray.Direction * Vector3.Dot(toPoint, ray.Direction);
        Assert.True(Vector3.Distance(closest, point) < 0.01f);
    }

    [Fact]
    public void A_point_behind_the_camera_has_no_pixel()
    {
        var camera = new OrbitCamera { Distance = 100 };
        var behind = camera.Position - camera.Forward * 10;
        Assert.Null(camera.Project(behind, 800, 600));
    }

    // The core of dragging an arrow: moving the mouse by some pixels moves the object by the matching distance on the axis.
    [Fact]
    public void Dragging_along_an_axis_moves_by_the_distance_between_the_two_mouse_rays()
    {
        var camera = new OrbitCamera { YawDegrees = -50, PitchDegrees = 30, Distance = 250, Target = Vector3.Zero };
        var start = new Vector3(0, 0, 0);
        var end = new Vector3(37, 0, 0); // the user drags the X arrow so that the cursor sits over this point

        var startPixel = camera.Project(start, 1000, 700)!.Value;
        var endPixel = camera.Project(end, 1000, 700)!.Value;
        var t0 = GizmoMath.ClosestParameterOnLine(camera.CreateRay(startPixel.X, startPixel.Y, 1000, 700), start, Vector3.UnitX)!.Value;
        var t1 = GizmoMath.ClosestParameterOnLine(camera.CreateRay(endPixel.X, endPixel.Y, 1000, 700), start, Vector3.UnitX)!.Value;

        Assert.Equal(37f, t1 - t0, 2);
    }

    [Fact]
    public void Dragging_on_a_plane_moves_by_the_vector_between_the_two_hit_points()
    {
        var camera = new OrbitCamera { YawDegrees = -50, PitchDegrees = 40, Distance = 250 };
        var start = new Vector3(5, 5, 0);
        var end = new Vector3(25, -10, 0);
        var a = camera.Project(start, 1000, 700)!.Value;
        var b = camera.Project(end, 1000, 700)!.Value;

        Assert.True(GizmoMath.IntersectPlane(camera.CreateRay(a.X, a.Y, 1000, 700), start, Vector3.UnitZ, out var hit0));
        Assert.True(GizmoMath.IntersectPlane(camera.CreateRay(b.X, b.Y, 1000, 700), start, Vector3.UnitZ, out var hit1));

        AssertNear(end - start, hit1 - hit0, digits: 2);
    }

    [Fact]
    public void Dragging_around_a_ring_gives_the_turn_angle()
    {
        var camera = new OrbitCamera { YawDegrees = -50, PitchDegrees = 40, Distance = 250 };
        var pivot = new Vector3(0, 0, 10);
        var from = pivot + new Vector3(30, 0, 0);
        var to = pivot + new Vector3(30 * (float)Math.Cos(Math.PI / 3), 30 * (float)Math.Sin(Math.PI / 3), 0); // 60° around Z

        var a = camera.Project(from, 1000, 700)!.Value;
        var b = camera.Project(to, 1000, 700)!.Value;
        Assert.True(GizmoMath.IntersectPlane(camera.CreateRay(a.X, a.Y, 1000, 700), pivot, Vector3.UnitZ, out var hit0));
        Assert.True(GizmoMath.IntersectPlane(camera.CreateRay(b.X, b.Y, 1000, 700), pivot, Vector3.UnitZ, out var hit1));

        Assert.Equal(60, GizmoMath.SignedAngleDegrees(hit0 - pivot, hit1 - pivot, Vector3.UnitZ), 2);
    }
}
