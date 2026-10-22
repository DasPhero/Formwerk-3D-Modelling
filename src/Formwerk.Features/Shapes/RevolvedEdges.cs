using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>
/// Chamfer and fillet on shapes that are a profile turned around the Z axis (F-EDG-01): frustum, cone, hemisphere, tube. The profile is
/// the outline of the cross section (radius, height); the chosen corners of it are cut (chamfer: straight at the distance <c>size</c>
/// along both edges; fillet: an arc of the radius <c>size</c> that touches both edges, for any angle between them), then the outline is
/// turned around the axis with the same corners around as the plain shape has.
/// </summary>
internal static class RevolvedEdges
{
    /// <summary>A corner may use at most this much of each of its two edges (neighbouring cuts must not meet).</summary>
    private const float Margin = 0.45f;

    private const int QuarterSteps = 8;
    private const float AxisRadius = 1e-6f;

    /// <summary>
    /// The solid made by <paramref name="profile"/> (counter-clockwise, radius to the right and height up; a point with radius 0 is on the
    /// axis) with the corners <paramref name="treat"/> cut. A corner that is straight, reflex or on the axis is left alone.
    /// <paramref name="closed"/>: the outline is a loop that does not touch the axis (a tube); otherwise it starts and ends on the axis.
    /// </summary>
    public static MeshData Build(IReadOnlyList<Vector2> profile, IReadOnlyList<bool> treat, bool closed, float size, EdgeKind kind, int segments)
    {
        var outline = new List<Vector2>();
        for (var i = 0; i < profile.Count; i++)
        {
            var corner = profile[i];
            var edge = closed || (i > 0 && i < profile.Count - 1);
            if (!treat[i] || !edge || corner.X <= AxisRadius)
            {
                outline.Add(corner);
                continue;
            }

            Vector2 previous = profile[(i + profile.Count - 1) % profile.Count], next = profile[(i + 1) % profile.Count];
            outline.AddRange(Cut(previous, corner, next, size, kind));
        }

        return Revolve(outline, closed, segments);
    }

    /// <summary>The points that replace the corner <paramref name="p"/> between its neighbours (the corner itself when there is nothing to cut).</summary>
    private static List<Vector2> Cut(Vector2 previous, Vector2 p, Vector2 next, float size, EdgeKind kind)
    {
        Vector2 a = previous - p, b = next - p;
        float la = a.Length(), lb = b.Length();
        if (la < 1e-6f || lb < 1e-6f) return [p];

        Vector2 u1 = a / la, u2 = b / lb;
        var alpha = MathF.Acos(Math.Clamp(Vector2.Dot(u1, u2), -1f, 1f)); // the angle inside the material
        var convex = (p.X - previous.X) * (next.Y - p.Y) - (p.Y - previous.Y) * (next.X - p.X) > 0; // a left turn of the counter-clockwise outline
        if (!convex || alpha < 0.05f || alpha > MathF.PI - 0.05f) return [p];

        var tangent = kind == EdgeKind.Chamfer ? size : size / MathF.Tan(alpha / 2);
        tangent = MathF.Min(tangent, Margin * MathF.Min(la, lb));
        Vector2 t1 = p + tangent * u1, t2 = p + tangent * u2;
        if (kind == EdgeKind.Chamfer) return [t1, t2];

        var radius = tangent * MathF.Tan(alpha / 2);
        var center = p + Vector2.Normalize(u1 + u2) * (radius / MathF.Sin(alpha / 2));
        Vector2 d1 = (t1 - center) / radius, d2 = (t2 - center) / radius;
        var turn = MathF.Acos(Math.Clamp(Vector2.Dot(d1, d2), -1f, 1f));
        var steps = Math.Max(2, (int)MathF.Ceiling(QuarterSteps * turn / (MathF.PI / 2)));
        var arc = new List<Vector2>(steps + 1) { t1 };
        for (var k = 1; k < steps; k++)
        {
            var f = (float)k / steps;
            var direction = (MathF.Sin((1 - f) * turn) * d1 + MathF.Sin(f * turn) * d2) / MathF.Sin(turn);
            arc.Add(center + radius * direction);
        }

        arc.Add(t2);
        return arc;
    }

    /// <summary>Turns the outline around the axis: rings of <paramref name="segments"/> corners, a single vertex for a point on the axis.</summary>
    internal static MeshData Revolve(IReadOnlyList<Vector2> outline, bool closed, int segments)
    {
        var n = segments;
        var count = outline.Count;
        var first = new int[count];
        var vertices = new List<Vector3>();
        for (var i = 0; i < count; i++)
        {
            first[i] = vertices.Count;
            if (outline[i].X <= AxisRadius)
            {
                vertices.Add(new Vector3(0, 0, outline[i].Y));
                continue;
            }

            for (var k = 0; k < n; k++)
            {
                var (sin, cos) = MathF.SinCos(2 * MathF.PI * k / n);
                vertices.Add(new Vector3(outline[i].X * cos, outline[i].X * sin, outline[i].Y));
            }
        }

        var indices = new List<int>();
        var edges = closed ? count : count - 1;
        for (var i = 0; i < edges; i++)
        {
            int j = (i + 1) % count;
            bool aAxis = outline[i].X <= AxisRadius, bAxis = outline[j].X <= AxisRadius;
            if (aAxis && bAxis) continue;
            for (var k = 0; k < n; k++)
            {
                var l = (k + 1) % n;
                if (aAxis) indices.AddRange([first[i], first[j] + l, first[j] + k]);
                else if (bAxis) indices.AddRange([first[j], first[i] + k, first[i] + l]);
                else indices.AddRange([first[i] + k, first[i] + l, first[j] + l, first[i] + k, first[j] + l, first[j] + k]);
            }
        }

        return new MeshData([.. vertices], [.. indices]);
    }
}
