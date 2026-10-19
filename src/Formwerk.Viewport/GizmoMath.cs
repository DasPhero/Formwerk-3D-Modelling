using System.Numerics;

namespace Formwerk.Viewport;

/// <summary>A half-line from the camera through a point on the screen.</summary>
public readonly record struct Ray(Vector3 Origin, Vector3 Direction);

/// <summary>Geometry of dragging handles: where a mouse ray touches an axis or a plane, and the angle around an axis.</summary>
public static class GizmoMath
{
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Parameter t of the point <c>linePoint + t * lineDirection</c> that is closest to the ray.
    /// Null when the ray is parallel to the line (no usable answer).
    /// </summary>
    public static float? ClosestParameterOnLine(Ray ray, Vector3 linePoint, Vector3 lineDirection)
    {
        var d1 = ray.Direction;
        var d2 = lineDirection;
        var w0 = ray.Origin - linePoint;
        var a = Vector3.Dot(d1, d1);
        var b = Vector3.Dot(d1, d2);
        var c = Vector3.Dot(d2, d2);
        var d = Vector3.Dot(d1, w0);
        var e = Vector3.Dot(d2, w0);
        var denominator = a * c - b * b;
        if (Math.Abs(denominator) < Epsilon * a * c) return null;
        return (a * e - b * d) / denominator;
    }

    /// <summary>Where the ray meets the plane. False when it is parallel to the plane or the plane is behind the camera.</summary>
    public static bool IntersectPlane(Ray ray, Vector3 planePoint, Vector3 planeNormal, out Vector3 hit)
    {
        hit = default;
        var denominator = Vector3.Dot(planeNormal, ray.Direction);
        if (Math.Abs(denominator) < Epsilon) return false;
        var t = Vector3.Dot(planeNormal, planePoint - ray.Origin) / denominator;
        if (t < 0) return false;
        hit = ray.Origin + ray.Direction * t;
        return true;
    }

    /// <summary>Signed angle in degrees from <paramref name="from"/> to <paramref name="to"/> around <paramref name="axis"/> (right-handed, -180 to 180).</summary>
    public static double SignedAngleDegrees(Vector3 from, Vector3 to, Vector3 axis)
    {
        var sine = Vector3.Dot(axis, Vector3.Cross(from, to));
        var cosine = Vector3.Dot(from, to);
        return Math.Atan2(sine, cosine) * 180 / Math.PI;
    }

    /// <summary>The smaller angle that turns <paramref name="current"/> into the same direction modulo 360 as <paramref name="previous"/>.</summary>
    public static double Unwrap(double previous, double current)
    {
        var angle = current;
        while (angle - previous > 180) angle -= 360;
        while (angle - previous < -180) angle += 360;
        return angle;
    }
}
