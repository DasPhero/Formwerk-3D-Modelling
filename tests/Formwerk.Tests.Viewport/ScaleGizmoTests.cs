using System.Numerics;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

public class ScaleGizmoTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    private static ScaleGizmoBox Box(Quaternion? rotation = null) =>
        new(new Vector3(100, 0, 0), rotation ?? Quaternion.Identity, new Vector3(-10, -20, 0), new Vector3(10, 20, 30));

    // ---- handle bookkeeping ---------------------------------------------------------------------

    [Fact]
    public void Face_handles_know_their_axis_and_side()
    {
        Assert.Equal(0, GizmoHandles.ScaleAxisIndex(GizmoHandleKind.ScaleXMin));
        Assert.Equal(0, GizmoHandles.ScaleAxisIndex(GizmoHandleKind.ScaleXMax));
        Assert.Equal(1, GizmoHandles.ScaleAxisIndex(GizmoHandleKind.ScaleYMax));
        Assert.Equal(2, GizmoHandles.ScaleAxisIndex(GizmoHandleKind.ScaleZMin));
        Assert.Equal(-1, GizmoHandles.ScaleSide(GizmoHandleKind.ScaleYMin));
        Assert.Equal(1, GizmoHandles.ScaleSide(GizmoHandleKind.ScaleZMax));
        Assert.Equal(GizmoHandleKind.ScaleYMax, GizmoHandles.ScaleFace(1, +1));
        Assert.Equal(GizmoHandleKind.ScaleZMin, GizmoHandles.ScaleFace(2, -1));
    }

    [Fact]
    public void Handle_classification_is_exclusive()
    {
        foreach (var handle in Enum.GetValues<GizmoHandleKind>())
        {
            var kinds = new[] { GizmoHandles.IsScaleFace(handle), GizmoHandles.IsScaleCorner(handle), GizmoHandles.IsRotation(handle), GizmoHandles.IsPlane(handle) };
            Assert.True(kinds.Count(k => k) <= 1, handle.ToString());
            Assert.Equal(GizmoHandles.IsScaleFace(handle) || GizmoHandles.IsScaleCorner(handle), GizmoHandles.IsScale(handle));
        }

        Assert.Equal(6, Enum.GetValues<GizmoHandleKind>().Count(GizmoHandles.IsScaleFace));
        Assert.Equal(8, Enum.GetValues<GizmoHandleKind>().Count(GizmoHandles.IsScaleCorner));
    }

    [Fact]
    public void Corner_signs_follow_the_bit_pattern_and_opposites_are_inverted()
    {
        Assert.Equal(new Vector3(-1, -1, -1), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleCorner0));
        Assert.Equal(new Vector3(1, -1, -1), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleCorner1));
        Assert.Equal(new Vector3(-1, 1, -1), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleCorner2));
        Assert.Equal(new Vector3(1, 1, 1), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleCorner7));

        foreach (var corner in Enum.GetValues<GizmoHandleKind>().Where(GizmoHandles.IsScaleCorner))
        {
            var opposite = GizmoHandles.OppositeCorner(corner);
            Assert.Equal(-GizmoHandles.ScaleSigns(corner), GizmoHandles.ScaleSigns(opposite));
            Assert.Equal(corner, GizmoHandles.OppositeCorner(opposite));
        }
    }

    [Fact]
    public void Face_signs_are_signed_unit_vectors()
    {
        Assert.Equal(new Vector3(-1, 0, 0), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleXMin));
        Assert.Equal(new Vector3(0, 1, 0), GizmoHandles.ScaleSigns(GizmoHandleKind.ScaleYMax));
        Assert.Equal(Vector3.Zero, GizmoHandles.ScaleSigns(GizmoHandleKind.MoveX));
    }

    // ---- the box -------------------------------------------------------------------------------

    [Fact]
    public void Face_handles_sit_in_the_middle_of_their_face()
    {
        var box = Box();
        AssertNear(new Vector3(10, 0, 15), box.HandleLocal(GizmoHandleKind.ScaleXMax));
        AssertNear(new Vector3(-10, 0, 15), box.HandleLocal(GizmoHandleKind.ScaleXMin));
        AssertNear(new Vector3(0, 20, 15), box.HandleLocal(GizmoHandleKind.ScaleYMax));
        AssertNear(new Vector3(0, 0, 0), box.HandleLocal(GizmoHandleKind.ScaleZMin));
        AssertNear(new Vector3(0, 0, 30), box.HandleLocal(GizmoHandleKind.ScaleZMax));
    }

    [Fact]
    public void Corner_handles_sit_on_the_corners()
    {
        var box = Box();
        AssertNear(new Vector3(-10, -20, 0), box.HandleLocal(GizmoHandleKind.ScaleCorner0));
        AssertNear(new Vector3(10, -20, 0), box.HandleLocal(GizmoHandleKind.ScaleCorner1));
        AssertNear(new Vector3(10, 20, 30), box.HandleLocal(GizmoHandleKind.ScaleCorner7));
        Assert.Equal(8, Enum.GetValues<GizmoHandleKind>().Where(GizmoHandles.IsScaleCorner).Select(box.HandleLocal).Distinct().Count());
    }

    [Fact]
    public void The_fixed_point_is_the_opposite_face_center_or_opposite_corner()
    {
        var box = Box();
        AssertNear(new Vector3(-10, 0, 15), box.OppositeLocal(GizmoHandleKind.ScaleXMax));
        AssertNear(new Vector3(0, 0, 30), box.OppositeLocal(GizmoHandleKind.ScaleZMin));
        AssertNear(new Vector3(10, 20, 30), box.OppositeLocal(GizmoHandleKind.ScaleCorner0));
        AssertNear(new Vector3(-10, -20, 0), box.OppositeLocal(GizmoHandleKind.ScaleCorner7));
    }

    [Fact]
    public void A_turned_box_puts_its_handles_in_the_turned_directions()
    {
        var box = Box(Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2));

        // local +X now points to world +Y
        AssertNear(Vector3.UnitY, box.AxisWorld(0));
        AssertNear(new Vector3(100, 10, 15), box.ToWorld(box.HandleLocal(GizmoHandleKind.ScaleXMax)));
        AssertNear(new Vector3(100 - 20, 0, 15), box.ToWorld(box.HandleLocal(GizmoHandleKind.ScaleYMax)));
    }

    [Fact]
    public void Size_and_center_are_derived_from_min_and_max()
    {
        var box = Box();
        AssertNear(new Vector3(20, 40, 30), box.Size);
        AssertNear(new Vector3(0, 0, 15), box.CenterLocal);
    }

    // ---- factors -------------------------------------------------------------------------------

    [Theory]
    [InlineData(20, +1, 10, false, 1.5)]   // pulling the plus face outwards by 10
    [InlineData(20, +1, -10, false, 0.5)]  // pushing it inwards
    [InlineData(20, -1, -10, false, 1.5)]  // the minus face moves in the negative direction when pulled outwards
    [InlineData(20, -1, 10, false, 0.5)]
    [InlineData(20, +1, 10, true, 2.0)]    // from the center: both faces move
    [InlineData(20, -1, -5, true, 1.5)]
    public void Face_factor_follows_the_pulled_distance(double extent, int side, double drag, bool fromCenter, double expected)
    {
        Assert.Equal(expected, ScaleMath.FaceFactor(extent, side, drag, fromCenter), 6);
    }

    [Fact]
    public void Dragging_past_the_opposite_side_clamps_instead_of_mirroring()
    {
        Assert.Equal(ScaleMath.MinFactor, ScaleMath.FaceFactor(20, +1, -50, false), 9);
        Assert.Equal(ScaleMath.MaxFactor, ScaleMath.FaceFactor(20, +1, 1e9, false), 9);
        Assert.Equal(1, ScaleMath.FaceFactor(0, +1, 5, false));
    }

    [Theory]
    [InlineData(1.5, 1.0, 1.5)]
    [InlineData(0.5, 1.0, 0.5)]
    [InlineData(3.0, 1.5, 2.0)]
    [InlineData(-1.0, 1.0, ScaleMath.MinFactor)]
    public void Corner_factor_is_the_ratio_along_the_diagonal(double now, double start, double expected)
    {
        Assert.Equal(expected, ScaleMath.CornerFactor(now, start), 9);
    }

    [Fact]
    public void Corner_factor_without_a_usable_start_is_neutral()
    {
        Assert.Equal(1, ScaleMath.CornerFactor(2, 0));
    }

    // The full chain for a face handle: project, drag with the mouse, get the right factor.
    [Fact]
    public void Dragging_a_face_handle_on_screen_gives_the_expected_factor()
    {
        var camera = new OrbitCamera { YawDegrees = -50, PitchDegrees = 30, Distance = 250, Target = new Vector3(100, 0, 15) };
        var box = Box();
        var handle = box.ToWorld(box.HandleLocal(GizmoHandleKind.ScaleXMax));
        var axis = box.AxisWorld(0);

        var start = camera.Project(handle, 1000, 700)!.Value;
        var end = camera.Project(handle + axis * 10, 1000, 700)!.Value; // pulled outwards by 10 mm
        var t0 = GizmoMath.ClosestParameterOnLine(camera.CreateRay(start.X, start.Y, 1000, 700), handle, axis)!.Value;
        var t1 = GizmoMath.ClosestParameterOnLine(camera.CreateRay(end.X, end.Y, 1000, 700), handle, axis)!.Value;

        var factor = ScaleMath.FaceFactor(box.Size.X, +1, t1 - t0, fromCenter: false);

        Assert.Equal(1.5, factor, 2); // 20 mm -> 30 mm
    }
}
