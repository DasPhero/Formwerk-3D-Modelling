using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>One edge of a <see cref="ConvexSolid"/>: its two ends and the two faces that meet there (indices of the faces, outward unit normals).</summary>
internal sealed record ConvexEdge(Vector3 A, Vector3 B, int Face1, int Face2, Vector3 Normal1, Vector3 Normal2);

/// <summary>
/// A convex solid made of flat faces, for chamfer and fillet on the shapes with flat sides (F-EDG-01: prism, wedge). Every treated edge
/// becomes one or more half-spaces (a chamfer: one plane through the points at the distance <c>size</c> on both faces; a fillet: planes
/// tangent to the arc that touches both faces, around the arc), and the solid is cut by them one after the other. All new points are
/// computed the same way from both sides of an edge, so the faces still fit together and the mesh is watertight.
/// </summary>
internal sealed class ConvexSolid
{
    /// <summary>A cut may use at most this much of the width of each face next to the edge.</summary>
    private const float Margin = 0.45f;

    private const float MinInnerAngle = 15; // degrees: an edge sharper than this, or flatter than the other limit, is left as it is
    private const float MaxInnerAngle = 172;
    private const float MinCornerAngle = 20; // an edge that ends at a corner of a face sharper than this (a needle) is left as it is
    private const int QuarterSteps = 8;
    private const float OnPlane = 1e-5f;

    private List<List<Vector3>> _faces;
    private readonly List<ConvexEdge> _edges;

    /// <param name="faces">The faces as polygons that are counter-clockwise seen from outside.</param>
    public ConvexSolid(IEnumerable<IReadOnlyList<Vector3>> faces)
    {
        _faces = [.. faces.Select(f => f.ToList())];
        _edges = FindEdges(_faces);
    }

    public IReadOnlyList<ConvexEdge> Edges => _edges;

    /// <summary>
    /// Cuts every edge that <paramref name="select"/> picks, with a chamfer or a fillet of <paramref name="size"/> (too big a size is cut
    /// down to what the faces next to the edge allow). Edges that are almost flat or very sharp are left alone.
    /// </summary>
    public void Treat(Func<ConvexEdge, bool> select, float size, EdgeKind kind)
    {
        // all cuts are worked out on the original solid, then applied
        var planes = new List<(Vector3 Normal, float Offset)>();
        foreach (var edge in _edges.Where(select)) planes.AddRange(PlanesOf(edge, size, kind));
        foreach (var (normal, offset) in planes) Clip(normal, offset);
    }

    private List<(Vector3 Normal, float Offset)> PlanesOf(ConvexEdge edge, float size, EdgeKind kind)
    {
        var e = Vector3.Normalize(edge.B - edge.A);
        Vector3 d1 = Inward(_faces[edge.Face1], edge.A, e), d2 = Inward(_faces[edge.Face2], edge.A, e);
        var alpha = MathF.Acos(Math.Clamp(Vector3.Dot(d1, d2), -1f, 1f)) * 180 / MathF.PI; // the angle between the faces inside the material
        if (alpha < MinInnerAngle || alpha > MaxInnerAngle) return [];

        // at the end of a needle (a corner of a face of only a few degrees) the cuts of neighbouring edges would meet in nothing
        foreach (var face in new[] { _faces[edge.Face1], _faces[edge.Face2] })
        {
            if (CornerAngle(face, edge.A) < MinCornerAngle || CornerAngle(face, edge.B) < MinCornerAngle) return [];
        }

        var width = MathF.Min(Width(_faces[edge.Face1], edge.A, d1), Width(_faces[edge.Face2], edge.A, d2));
        var halfAngle = alpha * MathF.PI / 360;

        if (kind == EdgeKind.Chamfer)
        {
            var s = MathF.Min(size, Margin * width);
            var normal = Vector3.Normalize(edge.Normal1 + edge.Normal2);
            return [(normal, Vector3.Dot(normal, edge.A + s * d1))];
        }

        // a circle of the radius r that touches both faces: its tangent points are r / tan(angle / 2) from the edge
        var radius = MathF.Min(size, Margin * width * MathF.Tan(halfAngle));
        var tangent = radius / MathF.Tan(halfAngle);
        var center = edge.A + tangent * d1 - radius * edge.Normal1;
        var turn = MathF.Acos(Math.Clamp(Vector3.Dot(edge.Normal1, edge.Normal2), -1f, 1f));
        var steps = Math.Max(2, (int)MathF.Ceiling(QuarterSteps * turn / (MathF.PI / 2)));
        var result = new List<(Vector3, float)>(steps);
        for (var k = 0; k < steps; k++)
        {
            // planes touching the arc in the middle of each of its pieces: together they lie around the arc
            var f = (k + 0.5f) / steps;
            var normal = (MathF.Sin((1 - f) * turn) * edge.Normal1 + MathF.Sin(f * turn) * edge.Normal2) / MathF.Sin(turn);
            result.Add((normal, Vector3.Dot(normal, center) + radius));
        }

        return result;
    }

    /// <summary>The angle in degrees that the face has at its corner at <paramref name="corner"/>.</summary>
    private static float CornerAngle(List<Vector3> face, Vector3 corner)
    {
        var index = face.FindIndex(v => Vector3.DistanceSquared(v, corner) < 1e-8f);
        if (index < 0) return 180;
        var a = face[(index + face.Count - 1) % face.Count] - face[index];
        var b = face[(index + 1) % face.Count] - face[index];
        return MathF.Acos(Math.Clamp(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(b)), -1f, 1f)) * 180 / MathF.PI;
    }

    /// <summary>The direction in the face that leads from the edge (through <paramref name="onEdge"/>, along <paramref name="edgeDirection"/>) into the face.</summary>
    private static Vector3 Inward(List<Vector3> face, Vector3 onEdge, Vector3 edgeDirection)
    {
        var center = face.Aggregate(Vector3.Zero, (sum, v) => sum + v) / face.Count;
        var toCenter = center - onEdge;
        return Vector3.Normalize(toCenter - edgeDirection * Vector3.Dot(toCenter, edgeDirection));
    }

    /// <summary>How far the face reaches away from the edge, in the direction <paramref name="inward"/>.</summary>
    private static float Width(List<Vector3> face, Vector3 onEdge, Vector3 inward) => face.Max(v => Vector3.Dot(v - onEdge, inward));

    // ---- cutting --------------------------------------------------------------------------------------

    /// <summary>Keeps what is on the side of the plane where <c>dot(normal, p) &lt;= offset</c>.</summary>
    private void Clip(Vector3 normal, float offset)
    {
        var result = new List<List<Vector3>>();
        var onPlane = new List<Vector3>();
        var cut = false; // is anything in front of the plane?
        foreach (var face in _faces)
        {
            var kept = new List<Vector3>(face.Count + 2);
            for (var i = 0; i < face.Count; i++)
            {
                Vector3 a = face[i], b = face[(i + 1) % face.Count];
                float da = Vector3.Dot(normal, a) - offset, db = Vector3.Dot(normal, b) - offset;
                bool aIn = da <= OnPlane, bIn = db <= OnPlane;
                if (!aIn) cut = true;
                if (aIn) kept.Add(a);
                if (aIn != bIn) kept.Add(Intersect(a, da, b, db));
            }

            var clean = Clean(kept);
            if (clean.Count < 3) continue;
            result.Add(clean);
            foreach (var v in clean)
            {
                if (MathF.Abs(Vector3.Dot(normal, v) - offset) <= OnPlane * 10) onPlane.Add(v);
            }
        }

        if (!cut) return; // the plane does not touch the solid: nothing changes

        // something was cut away: the opening is closed with a face in the plane
        if (onPlane.Count >= 3)
        {
            var cap = Cap(onPlane, normal);
            if (cap.Count >= 3) result.Add(cap);
        }

        _faces = result;
    }

    /// <summary>The point where the edge a-b crosses the plane; computed from the same end first, whichever way round the edge is given, so that both faces of the edge get the same point.</summary>
    private static Vector3 Intersect(Vector3 a, float da, Vector3 b, float db)
    {
        if (Compare(b, a) < 0) (a, da, b, db) = (b, db, a, da);
        var t = Math.Clamp(da / (da - db), 0f, 1f);
        return a + (b - a) * t;
    }

    private static int Compare(Vector3 a, Vector3 b)
    {
        var c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        return c != 0 ? c : a.Z.CompareTo(b.Z);
    }

    /// <summary>The corners without neighbours that are the same point.</summary>
    private static List<Vector3> Clean(List<Vector3> polygon)
    {
        var clean = new List<Vector3>(polygon.Count);
        foreach (var p in polygon)
        {
            if (clean.Count == 0 || Vector3.DistanceSquared(clean[^1], p) > 1e-10f) clean.Add(p);
        }

        while (clean.Count > 1 && Vector3.DistanceSquared(clean[0], clean[^1]) <= 1e-10f) clean.RemoveAt(clean.Count - 1);
        return clean;
    }

    /// <summary>The face that closes the cut: the points in the plane, once each, in counter-clockwise order around the normal.</summary>
    private static List<Vector3> Cap(List<Vector3> points, Vector3 normal)
    {
        var unique = new List<Vector3>();
        foreach (var p in points)
        {
            if (!unique.Any(u => Vector3.DistanceSquared(u, p) <= 1e-8f)) unique.Add(p);
        }

        if (unique.Count < 3) return unique;
        var center = unique.Aggregate(Vector3.Zero, (sum, v) => sum + v) / unique.Count;
        var helper = MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(normal, helper));
        var v = Vector3.Cross(normal, u);
        return [.. unique.OrderBy(p => MathF.Atan2(Vector3.Dot(p - center, v), Vector3.Dot(p - center, u)))];
    }

    // ---- edges and mesh -------------------------------------------------------------------------------

    private static (int, int, int) Key(Vector3 v) => ((int)MathF.Round(v.X * 1e4f), (int)MathF.Round(v.Y * 1e4f), (int)MathF.Round(v.Z * 1e4f));

    private static Vector3 NormalOf(List<Vector3> face)
    {
        var sum = Vector3.Zero; // Newell: robust for any convex polygon
        for (var i = 0; i < face.Count; i++)
        {
            Vector3 a = face[i], b = face[(i + 1) % face.Count];
            sum += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }

        return Vector3.Normalize(sum);
    }

    private static List<ConvexEdge> FindEdges(List<List<Vector3>> faces)
    {
        var seen = new Dictionary<((int, int, int), (int, int, int)), (int Face, Vector3 A, Vector3 B)>();
        var edges = new List<ConvexEdge>();
        for (var f = 0; f < faces.Count; f++)
        {
            var face = faces[f];
            for (var i = 0; i < face.Count; i++)
            {
                Vector3 a = face[i], b = face[(i + 1) % face.Count];
                if (seen.Remove((Key(b), Key(a)), out var other))
                    edges.Add(new ConvexEdge(other.B, other.A, other.Face, f, NormalOf(faces[other.Face]), NormalOf(face)));
                else seen[(Key(a), Key(b))] = (f, a, b);
            }
        }

        return edges;
    }

    /// <summary>The solid as one mesh: points at the same place are one vertex, every face is split into triangles around its middle.</summary>
    public MeshData ToMesh()
    {
        // points closer than WeldDistance are one vertex (the same point worked out along different edges differs in the last digits)
        const float WeldDistance = 1e-4f, Cell = 1e-3f;
        var cells = new Dictionary<(int, int, int), List<int>>();
        var vertices = new List<Vector3>();
        int Id(Vector3 v)
        {
            int cx = (int)MathF.Floor(v.X / Cell), cy = (int)MathF.Floor(v.Y / Cell), cz = (int)MathF.Floor(v.Z / Cell);
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (!cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var list)) continue;
                foreach (var id in list)
                {
                    if (Vector3.DistanceSquared(vertices[id], v) <= WeldDistance * WeldDistance) return id;
                }
            }

            var added = vertices.Count;
            vertices.Add(v);
            if (!cells.TryGetValue((cx, cy, cz), out var own)) cells[(cx, cy, cz)] = own = [];
            own.Add(added);
            return added;
        }

        var indices = new List<int>();
        foreach (var face in _faces)
        {
            // merge corners that the welding makes the same, then split into triangles
            var corners = new List<int>();
            foreach (var v in face)
            {
                var id = Id(v);
                if (corners.Count == 0 || corners[^1] != id) corners.Add(id);
            }

            while (corners.Count > 1 && corners[0] == corners[^1]) corners.RemoveAt(corners.Count - 1);
            if (corners.Count < 3) continue;

            if (corners.Count == 3)
            {
                indices.AddRange(corners);
                continue;
            }

            var middle = vertices.Count;
            vertices.Add(corners.Aggregate(Vector3.Zero, (sum, c) => sum + vertices[c]) / corners.Count);
            for (var i = 0; i < corners.Count; i++) indices.AddRange([middle, corners[i], corners[(i + 1) % corners.Count]]);
        }

        return new MeshData([.. vertices], [.. indices]);
    }
}
