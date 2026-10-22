using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>The distance of two points in mm with its parts along X, Y and Z (signed, from the first point to the second).</summary>
public readonly record struct PointDistance(float Length, Vector3 Delta);

/// <summary>
/// How two faces lie to each other: parallel (then <see cref="Distance"/> is the distance of their planes) or not (then
/// <see cref="InsideAngle"/> is the angle between them through the material, as a protractor would show it where they meet).
/// </summary>
public readonly record struct FaceRelation(bool Parallel, float Distance, float InsideAngle);

/// <summary>The arithmetic of the measuring tool (E-07).</summary>
public static class MeasureMath
{
    /// <summary>Faces whose normals differ by less than this angle (degrees) count as parallel.</summary>
    private const float ParallelDegrees = 0.2f;

    public static PointDistance Distance(Vector3 a, Vector3 b)
    {
        var delta = b - a;
        return new PointDistance(delta.Length(), delta);
    }

    /// <summary>
    /// The relation of two faces, each given by a point on it and its outward normal (any length). Parallel faces, facing each other or
    /// the same way, have the distance of their planes (measured across, not along the line between the two points).
    /// </summary>
    public static FaceRelation Faces(Vector3 point1, Vector3 normal1, Vector3 point2, Vector3 normal2)
    {
        var n1 = Vector3.Normalize(normal1);
        var n2 = Vector3.Normalize(normal2);
        var dot = Math.Clamp(Vector3.Dot(n1, n2), -1f, 1f);
        if (MathF.Abs(dot) >= MathF.Cos(ParallelDegrees * MathF.PI / 180)) return new FaceRelation(true, MathF.Abs(Vector3.Dot(n1, point2 - point1)), 0);

        var between = MathF.Acos(dot) * 180 / MathF.PI; // the angle between the normals
        return new FaceRelation(false, 0, 180 - between);
    }

    /// <summary>The angle in degrees at <paramref name="vertex"/> between the lines to <paramref name="a"/> and <paramref name="c"/> (0 when a line has no length).</summary>
    public static float Angle(Vector3 a, Vector3 vertex, Vector3 c)
    {
        Vector3 u = a - vertex, v = c - vertex;
        if (u.LengthSquared() < 1e-12f || v.LengthSquared() < 1e-12f) return 0;
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(u), Vector3.Normalize(v)), -1f, 1f)) * 180 / MathF.PI;
    }
}
