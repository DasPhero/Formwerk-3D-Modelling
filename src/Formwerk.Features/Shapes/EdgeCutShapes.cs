using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>
/// The cross section of the body that a chamfer or fillet takes away from a corner (F-EDG-02): the faces leave the <c>apex</c> in the
/// directions <c>d1</c> and <c>d2</c> (unit vectors), their outward normals are <c>n1</c> and <c>n2</c>. The polygon goes a little
/// beyond the faces (<c>margin</c>), so that cutting it out never leaves a skin of material on the faces.
/// </summary>
internal static class CutProfile
{
    /// <summary>Chamfer: the quadrilateral behind the chamfer line. Fillet: tangent points, the arc, and the outside of the faces ("wedge minus cylinder"). Counter-clockwise.</summary>
    public static List<Vector2> Corner(Vector2 apex, Vector2 d1, Vector2 d2, Vector2 n1, Vector2 n2, float size, EdgeKind kind, float margin)
    {
        // where the two faces moved outwards by the margin meet
        var outer = apex + margin * (n1 + n2) / (1 + Vector2.Dot(n1, n2));
        List<Vector2> polygon;
        if (kind == EdgeKind.Chamfer)
        {
            var b = apex + size * d1;
            var c = apex + size * d2;
            polygon = [b, b + margin * n1, outer, c + margin * n2, c];
        }
        else
        {
            // the circle of radius "size" touches both faces: its tangent points are size / tan(angle / 2) from the apex
            var half = MathF.Acos(Math.Clamp(Vector2.Dot(d1, d2), -1f, 1f)) / 2;
            var tangent = size / MathF.Tan(half);
            var t1 = apex + tangent * d1;
            var t2 = apex + tangent * d2;
            var center = t1 - size * n1;
            polygon = [t1, t1 + margin * n1, outer, t2 + margin * n2, t2];

            // the arc from the second tangent point back to the first, through the point nearest to the apex
            var turn = MathF.Acos(Math.Clamp(Vector2.Dot(n1, n2), -1f, 1f));
            var steps = Math.Max(2, (int)MathF.Ceiling(8 * turn / (MathF.PI / 2)));
            for (var k = 1; k < steps; k++)
            {
                var f = (float)k / steps;
                var direction = (MathF.Sin((1 - f) * turn) * n2 + MathF.Sin(f * turn) * n1) / MathF.Sin(turn);
                polygon.Add(center + size * direction);
            }
        }

        if (SignedArea(polygon) < 0) polygon.Reverse();
        return polygon;
    }

    public static float SignedArea(IReadOnlyList<Vector2> polygon)
    {
        var sum = 0f;
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }

    /// <summary>Splits a simple counter-clockwise polygon into triangles (ear clipping); the indices refer to the polygon.</summary>
    public static List<int> Triangulate(IReadOnlyList<Vector2> polygon)
    {
        var remaining = Enumerable.Range(0, polygon.Count).ToList();
        var triangles = new List<int>();

        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

        for (var guard = 0; remaining.Count > 3 && guard < polygon.Count * polygon.Count; guard++)
        {
            var clipped = false;
            for (var i = 0; i < remaining.Count && !clipped; i++)
            {
                int prev = remaining[(i + remaining.Count - 1) % remaining.Count], current = remaining[i], next = remaining[(i + 1) % remaining.Count];
                Vector2 a = polygon[prev], b = polygon[current], c = polygon[next];
                if (Cross(a, b, c) <= 1e-7f) continue; // a reflex corner or a straight one is no ear

                var covers = false;
                foreach (var j in remaining)
                {
                    if (j == prev || j == current || j == next) continue;
                    var p = polygon[j];
                    if (Cross(a, b, p) > 1e-7f && Cross(b, c, p) > 1e-7f && Cross(c, a, p) > 1e-7f)
                    {
                        covers = true;
                        break;
                    }
                }

                if (covers) continue;
                triangles.AddRange([prev, current, next]);
                remaining.RemoveAt(i);
                clipped = true;
            }

            if (!clipped) remaining.RemoveAt(0); // numerical trouble (a straight corner): drop a vertex, the area stays the same
        }

        if (remaining.Count == 3) triangles.AddRange(remaining);
        return triangles;
    }
}

/// <summary>
/// The negative body of a chamfer or fillet on a straight edge (F-EDG-02): a wedge (chamfer) or a wedge minus a cylinder (fillet)
/// along the edge, a little longer than the edge. Local frame: the edge runs along X from 0 to <c>length</c> through the origin, Z is
/// the direction that leaves the material between the two faces (the bisector), the faces leave the edge at the angle <c>angle</c> to
/// each other. The cutter is a hole object (it cuts the solids of its group); the tool puts it on the edge.
/// </summary>
public sealed class EdgeCutGenerator : ShapeGenerator
{
    /// <summary>How far the cutter goes beyond the ends of the edge, in mm.</summary>
    public const float EndExtension = 0.05f;

    /// <summary>The cutter goes beyond the faces by this much plus a part of the size, in mm.</summary>
    public const float MarginBase = 0.25f;

    public const float MarginPerSize = 0.05f;

    public static float Margin(float size) => MarginBase + MarginPerSize * size;

    public override string Id => "edgecut";

    public override string DisplayName => "Edge cut";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("length", "Length", 20, 0.01, 10000),
        new("angle", "Angle between faces", 90, 20, 170, IsDetail: true, Unit: "°"),
        new("edgeSize", "Edge size", 1, 0.01, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true), // 0 chamfer, 1 fillet
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } = [new("length", ScaleAxes.X)];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var length = (float)p["length"];
        var size = (float)p["edgeSize"];
        var half = (float)p["angle"] * MathF.PI / 360;
        var (sin, cos) = MathF.SinCos(half);
        var polygon = CutProfile.Corner(
            Vector2.Zero, new Vector2(sin, -cos), new Vector2(-sin, -cos), new Vector2(cos, sin), new Vector2(-cos, sin), size, (EdgeKind)(int)p["edgeType"], Margin(size));

        // extrude the cross section (Y, Z) along X
        var n = polygon.Count;
        float x0 = -EndExtension, x1 = length + EndExtension;
        var vertices = new Vector3[2 * n];
        for (var i = 0; i < n; i++)
        {
            vertices[i] = new Vector3(x0, polygon[i].X, polygon[i].Y);
            vertices[n + i] = new Vector3(x1, polygon[i].X, polygon[i].Y);
        }

        var indices = new List<int>();
        for (var i = 0; i < n; i++)
        {
            var j = (i + 1) % n;
            indices.AddRange([i, j, n + j, i, n + j, n + i]);
        }

        var ears = CutProfile.Triangulate(polygon);
        for (var t = 0; t < ears.Count; t += 3)
        {
            indices.AddRange([n + ears[t], n + ears[t + 1], n + ears[t + 2]]); // end at x1 faces +X
            indices.AddRange([ears[t], ears[t + 2], ears[t + 1]]);             // start at x0 faces -X
        }

        return new MeshData(vertices, [.. indices]);
    }
}

/// <summary>
/// The negative body of a chamfer or fillet on the round edge of a flat face next to a cylinder wall (F-EDG-02): the cross section is
/// turned around the axis. Local frame: the axis is Z, the face (the circle of the edge) lies in the plane Z = 0 and looks along +Z,
/// the first of <c>segments</c> corners is on the X axis. <c>inside</c> is 0 for the rim of a boss or a cylinder (the material is inside
/// the circle) and 1 for the rim of a hole (the material is outside).
/// </summary>
public sealed class EdgeRingGenerator : ShapeGenerator
{
    public override string Id => "edgering";

    public override string DisplayName => "Edge ring";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("radius", "Rim radius", 10, 0.01, 10000),
        new("edgeSize", "Edge size", 1, 0.01, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true), // 0 chamfer, 1 fillet
        new("inside", "Material outside", 0, 0, 1, IsInteger: true),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } = [new("radius", ScaleAxes.X | ScaleAxes.Y)];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var radius = (float)p["radius"];
        var hole = p["inside"] > 0;
        // a boss cannot be cut deeper than its radius; the margin must stay inside the radius for the rim of a hole
        var size = hole ? (float)p["edgeSize"] : MathF.Min((float)p["edgeSize"], radius * 0.99f);
        var margin = MathF.Min(EdgeCutGenerator.Margin(size), radius * 0.5f);
        var n = (int)p["segments"];

        var apex = new Vector2(radius, 0);
        var polygon = CutProfile.Corner(
            apex, new Vector2(hole ? 1 : -1, 0), new Vector2(0, -1), new Vector2(0, 1), new Vector2(hole ? -1 : 1, 0), size, (EdgeKind)(int)p["edgeType"], margin);

        // turn the cross section (radius, height) around Z
        var count = polygon.Count;
        var vertices = new Vector3[count * n];
        for (var i = 0; i < count; i++)
        {
            for (var k = 0; k < n; k++)
            {
                var (sin, cos) = MathF.SinCos(2 * MathF.PI * k / n);
                vertices[i * n + k] = new Vector3(polygon[i].X * cos, polygon[i].X * sin, polygon[i].Y);
            }
        }

        var indices = new List<int>(count * n * 6);
        for (var i = 0; i < count; i++)
        {
            int a = i * n, b = (i + 1) % count * n;
            for (var k = 0; k < n; k++)
            {
                var l = (k + 1) % n;
                indices.AddRange([a + k, b + l, b + k, a + k, a + l, b + l]);
            }
        }

        return new MeshData(vertices, [.. indices]);
    }
}
