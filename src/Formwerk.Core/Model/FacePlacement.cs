using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>Placing an object on a face: its base on a point of the face and its up axis (+Z) along the normal of the face (F-LIB-04).</summary>
public static class FacePlacement
{
    /// <summary>
    /// The transform that puts the local origin on <paramref name="point"/> and turns the local +Z axis onto <paramref name="normal"/> by the
    /// shortest turn (so a cylinder dragged onto a slanted face stands perpendicular on it). A normal that is not usable gives an upright
    /// placement; the opposite direction (a downward face) is a half turn about X.
    /// </summary>
    public static Transform OnFace(Vector3 point, Vector3 normal)
    {
        var length = normal.Length();
        if (!float.IsFinite(length) || length < 1e-6f) return Transform.FromPosition(point);

        var n = normal / length;
        var dot = Math.Clamp(Vector3.Dot(Vector3.UnitZ, n), -1f, 1f);
        Quaternion rotation;
        if (dot > 1 - 1e-7f) rotation = Quaternion.Identity;
        else if (dot < -1 + 1e-7f) rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        else rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, n)), MathF.Acos(dot));

        return Transform.FromPosition(point) with { Rotation = Quaternion.Normalize(rotation) };
    }
}
