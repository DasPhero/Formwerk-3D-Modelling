using System.Numerics;

namespace Formwerk.Viewport;

/// <summary>Which tool is active in the viewport.</summary>
public enum ViewportTool
{
    /// <summary>Clicking selects; no handles.</summary>
    Select,

    /// <summary>Axis arrows and plane handles (F-TRF-01).</summary>
    Move,

    /// <summary>Three rings (F-ROT-01).</summary>
    Rotate,

    /// <summary>A box with handles on its faces and corners (F-SCL-01).</summary>
    Scale,
}

public enum GizmoHandleKind
{
    MoveX,
    MoveY,
    MoveZ,
    MovePlaneXY,
    MovePlaneXZ,
    MovePlaneYZ,
    RotateX,
    RotateY,
    RotateZ,

    /// <summary>Face handles of the scale box: one axis, minus side or plus side.</summary>
    ScaleXMin,
    ScaleXMax,
    ScaleYMin,
    ScaleYMax,
    ScaleZMin,
    ScaleZMax,

    /// <summary>Corner handles. The index (0 to 7) has one bit per axis: 1 = X max, 2 = Y max, 4 = Z max.</summary>
    ScaleCorner0,
    ScaleCorner1,
    ScaleCorner2,
    ScaleCorner3,
    ScaleCorner4,
    ScaleCorner5,
    ScaleCorner6,
    ScaleCorner7,
}

/// <summary>Current state of a handle drag, relative to where the drag started. Values are not snapped.</summary>
/// <param name="TranslationDelta">World-space movement (moves only).</param>
/// <param name="AngleDegrees">Total turn around the handle's axis (rotations only), right-handed.</param>
/// <param name="SnapSuppressed">Alt (grid) or Shift (angle) is held: the receiver must not snap.</param>
/// <param name="ScaleFactor">Scale handles only: new size divided by old size along the handle's axis (all axes for corners).</param>
/// <param name="FromCenter">Scale handles only: Ctrl is held, the box grows from its center instead of from the opposite side.</param>
/// <param name="WorldPerPixel">Move handles only: how many mm one screen pixel is worth at the object, so that a snap tolerance in pixels works at every zoom (F-SNP-06). 0 when unknown.</param>
public readonly record struct GizmoDrag(
    GizmoHandleKind Handle, Vector3 TranslationDelta, double AngleDegrees, bool SnapSuppressed,
    double ScaleFactor = 1, bool FromCenter = false, double WorldPerPixel = 0);

/// <summary>
/// The box of the scale tool in world space: an origin and orientation (the object's own frame) and the extent of the
/// object in that frame, in millimetres. The box is oriented like the object, so it fits a turned object tightly.
/// </summary>
public readonly record struct ScaleGizmoBox(Vector3 Origin, Quaternion Rotation, Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 CenterLocal => (Min + Max) * 0.5f;

    public Vector3 ToWorld(Vector3 local) => Origin + Vector3.Transform(local, Rotation);

    public Vector3 AxisWorld(int axis) =>
        Vector3.Transform(axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }, Rotation);

    /// <summary>Position of a scale handle in the box's own coordinates.</summary>
    public Vector3 HandleLocal(GizmoHandleKind handle)
    {
        if (GizmoHandles.IsScaleCorner(handle))
        {
            var bits = handle - GizmoHandleKind.ScaleCorner0;
            return new Vector3((bits & 1) != 0 ? Max.X : Min.X, (bits & 2) != 0 ? Max.Y : Min.Y, (bits & 4) != 0 ? Max.Z : Min.Z);
        }

        var center = CenterLocal;
        var value = GizmoHandles.ScaleSide(handle) > 0 ? Max : Min;
        return GizmoHandles.ScaleAxisIndex(handle) switch
        {
            0 => new Vector3(value.X, center.Y, center.Z),
            1 => new Vector3(center.X, value.Y, center.Z),
            _ => new Vector3(center.X, center.Y, value.Z),
        };
    }

    /// <summary>The point that stays fixed when the handle is dragged: the opposite corner, or the center of the opposite face.</summary>
    public Vector3 OppositeLocal(GizmoHandleKind handle) => GizmoHandles.IsScaleCorner(handle)
        ? HandleLocal(GizmoHandles.OppositeCorner(handle))
        : HandleLocal(GizmoHandles.ScaleFace(GizmoHandles.ScaleAxisIndex(handle), -GizmoHandles.ScaleSide(handle)));
}

/// <summary>
/// Receiver of handle drags, implemented by the view model. The viewport only reports geometry;
/// what a drag does to the document (snapping, preview, undo) is decided by the receiver.
/// </summary>
public interface IGizmoTarget
{
    /// <summary>A drag starts. Returning false refuses it (for example when the selection is locked).</summary>
    bool BeginDrag(GizmoHandleKind handle);

    /// <summary>Called for every mouse move. Returns the text shown at the cursor (for example the new position or angle).</summary>
    string UpdateDrag(GizmoDrag drag);

    /// <summary>The mouse was released.</summary>
    void CommitDrag();

    /// <summary>Escape was pressed or the drag was interrupted.</summary>
    void CancelDrag();
}

public static class GizmoHandles
{
    /// <summary>Direction of the axis for axis moves and rotations; zero for plane handles.</summary>
    public static Vector3 Axis(GizmoHandleKind handle) => handle switch
    {
        GizmoHandleKind.MoveX or GizmoHandleKind.RotateX => Vector3.UnitX,
        GizmoHandleKind.MoveY or GizmoHandleKind.RotateY => Vector3.UnitY,
        GizmoHandleKind.MoveZ or GizmoHandleKind.RotateZ => Vector3.UnitZ,
        _ => Vector3.Zero,
    };

    /// <summary>Normal of the plane a plane handle moves in (XY plane has normal Z, and so on).</summary>
    public static Vector3 PlaneNormal(GizmoHandleKind handle) => handle switch
    {
        GizmoHandleKind.MovePlaneXY => Vector3.UnitZ,
        GizmoHandleKind.MovePlaneXZ => Vector3.UnitY,
        GizmoHandleKind.MovePlaneYZ => Vector3.UnitX,
        _ => Vector3.Zero,
    };

    public static bool IsScaleFace(GizmoHandleKind handle) => handle is >= GizmoHandleKind.ScaleXMin and <= GizmoHandleKind.ScaleZMax;

    public static bool IsScaleCorner(GizmoHandleKind handle) => handle is >= GizmoHandleKind.ScaleCorner0 and <= GizmoHandleKind.ScaleCorner7;

    public static bool IsScale(GizmoHandleKind handle) => IsScaleFace(handle) || IsScaleCorner(handle);

    /// <summary>Axis (0 = X, 1 = Y, 2 = Z) of a scale face handle.</summary>
    public static int ScaleAxisIndex(GizmoHandleKind handle) => (handle - GizmoHandleKind.ScaleXMin) / 2;

    /// <summary>+1 for the maximum side, -1 for the minimum side of a scale face handle.</summary>
    public static int ScaleSide(GizmoHandleKind handle) => (handle - GizmoHandleKind.ScaleXMin) % 2 == 0 ? -1 : 1;

    public static GizmoHandleKind ScaleFace(int axis, int side) => GizmoHandleKind.ScaleXMin + axis * 2 + (side > 0 ? 1 : 0);

    public static GizmoHandleKind OppositeCorner(GizmoHandleKind corner) =>
        GizmoHandleKind.ScaleCorner0 + (7 - (corner - GizmoHandleKind.ScaleCorner0));

    /// <summary>For a corner handle: -1 or +1 per axis (minimum or maximum side). For a face handle: the unit vector of its axis, signed. Otherwise zero.</summary>
    public static Vector3 ScaleSigns(GizmoHandleKind handle)
    {
        if (IsScaleCorner(handle))
        {
            var bits = handle - GizmoHandleKind.ScaleCorner0;
            return new Vector3((bits & 1) != 0 ? 1 : -1, (bits & 2) != 0 ? 1 : -1, (bits & 4) != 0 ? 1 : -1);
        }

        if (!IsScaleFace(handle)) return Vector3.Zero;
        var side = ScaleSide(handle);
        return ScaleAxisIndex(handle) switch { 0 => new Vector3(side, 0, 0), 1 => new Vector3(0, side, 0), _ => new Vector3(0, 0, side) };
    }

    public static bool IsRotation(GizmoHandleKind handle) =>
        handle is GizmoHandleKind.RotateX or GizmoHandleKind.RotateY or GizmoHandleKind.RotateZ;

    public static bool IsPlane(GizmoHandleKind handle) =>
        handle is GizmoHandleKind.MovePlaneXY or GizmoHandleKind.MovePlaneXZ or GizmoHandleKind.MovePlaneYZ;
}
