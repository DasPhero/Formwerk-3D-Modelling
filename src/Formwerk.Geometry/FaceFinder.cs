using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>
/// A flat face of a mesh: the triangles that lie in one plane and hang together, found around a clicked triangle. Used to snap
/// to the center of a face (F-HOL-01).
/// </summary>
public sealed class FaceInfo
{
    private const float PlaneTolerance = 0.05f;

    private readonly (Vector3 A, Vector3 B, Vector3 C)[] _triangles;

    internal FaceInfo((Vector3, Vector3, Vector3)[] triangles, Vector3 center, Vector3 normal, float extent)
    {
        _triangles = triangles;
        Center = center;
        Normal = normal;
        Extent = extent;
    }

    /// <summary>The center of mass of the face (a face with a hole has it in the middle of the material, not of the hole).</summary>
    public Vector3 Center { get; }

    /// <summary>The outward direction of the face.</summary>
    public Vector3 Normal { get; }

    /// <summary>The diagonal of the box around the face, in mm: how big the face is.</summary>
    public float Extent { get; }

    /// <summary>
    /// The edges of the outline of the face, also around its holes: the edges that only one of its triangles has (points at the same place
    /// are the same point), as pairs of end points.
    /// </summary>
    public IReadOnlyList<(Vector3 A, Vector3 B)> Boundary()
    {
        static (int, int, int) Key(Vector3 v) => ((int)MathF.Round(v.X * 1000), (int)MathF.Round(v.Y * 1000), (int)MathF.Round(v.Z * 1000));
        var edges = new Dictionary<((int, int, int), (int, int, int)), (Vector3 A, Vector3 B, int Count)>();

        void Add(Vector3 a, Vector3 b)
        {
            var ka = Key(a);
            var kb = Key(b);
            var key = ka.CompareTo(kb) <= 0 ? (ka, kb) : (kb, ka);
            edges[key] = edges.TryGetValue(key, out var seen) ? (seen.A, seen.B, seen.Count + 1) : (a, b, 1);
        }

        foreach (var (a, b, c) in _triangles)
        {
            Add(a, b);
            Add(b, c);
            Add(c, a);
        }

        return [.. edges.Values.Where(e => e.Count == 1).Select(e => (e.A, e.B))];
    }

    /// <summary>True when the point lies on the face (in its plane, within a triangle of it), not in a hole of it.</summary>
    public bool Contains(Vector3 point)
    {
        if (_triangles.Length == 0 || MathF.Abs(Vector3.Dot(point - _triangles[0].A, Normal)) > PlaneTolerance) return false;
        foreach (var (a, b, c) in _triangles)
        {
            if (FaceFinder.Inside(point, a, b, c, 1e-3f)) return true;
        }

        return false;
    }
}

public static class FaceFinder
{
    // how much the direction and the position of a triangle may differ from the face to count as part of it
    private const float SameDirection = 0.9995f; // about 1.8 degrees
    private const float SamePlane = 0.02f;
    private const float SeedPlane = 0.05f;

    /// <summary>
    /// The flat face that contains <paramref name="point"/> and looks in the direction <paramref name="normal"/>, or null when no
    /// triangle of the mesh lies there. Triangles count as one face when they point the same way, lie in the same plane and are
    /// joined by edges (vertices with the same position are the same vertex, so unwelded meshes work too).
    /// </summary>
    public static FaceInfo? Find(MeshData mesh, Vector3 point, Vector3 normal)
    {
        if (mesh.IsEmpty || normal.LengthSquared() < 1e-12f) return null;
        normal = Vector3.Normalize(normal);
        var vertices = mesh.Vertices.ToArray(); // arrays, because the local functions below cannot capture spans
        var indices = mesh.Indices.ToArray();
        var count = mesh.TriangleCount;

        Vector3 NormalOf(int t) => FaceNormal(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);

        // the clicked triangle: the one in front of the point, in its plane, closest to it
        var seed = -1;
        var best = float.MaxValue;
        for (var t = 0; t < count; t++)
        {
            var n = NormalOf(t);
            if (Vector3.Dot(n, normal) < SameDirection) continue;
            var a = vertices[indices[3 * t]];
            var distance = MathF.Abs(Vector3.Dot(point - a, n));
            if (distance > SeedPlane || distance >= best) continue;
            if (!Inside(point, a, vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], 1e-3f)) continue;
            seed = t;
            best = distance;
        }

        if (seed < 0) return null;
        var faceNormal = NormalOf(seed);
        var origin = vertices[indices[3 * seed]];

        // all triangles of the same plane and direction
        var inPlane = new List<int>();
        for (var t = 0; t < count; t++)
        {
            if (Vector3.Dot(NormalOf(t), faceNormal) < SameDirection) continue;
            var onPlane = true;
            for (var k = 0; k < 3 && onPlane; k++) onPlane = MathF.Abs(Vector3.Dot(vertices[indices[3 * t + k]] - origin, faceNormal)) <= SamePlane;
            if (onPlane) inPlane.Add(t);
        }

        // vertices at the same position are the same vertex
        var ids = new Dictionary<(int, int, int), int>();
        int Id(Vector3 v)
        {
            var key = ((int)MathF.Round(v.X * 1000), (int)MathF.Round(v.Y * 1000), (int)MathF.Round(v.Z * 1000));
            if (!ids.TryGetValue(key, out var id)) ids[key] = id = ids.Count;
            return id;
        }

        var corners = new Dictionary<int, int[]>(inPlane.Count);
        var byEdge = new Dictionary<long, List<int>>();
        foreach (var t in inPlane)
        {
            var c = new[] { Id(vertices[indices[3 * t]]), Id(vertices[indices[3 * t + 1]]), Id(vertices[indices[3 * t + 2]]) };
            corners[t] = c;
            for (var k = 0; k < 3; k++)
            {
                long a = c[k], b = c[(k + 1) % 3];
                var edge = Math.Min(a, b) << 32 | Math.Max(a, b);
                if (!byEdge.TryGetValue(edge, out var list)) byEdge[edge] = list = [];
                list.Add(t);
            }
        }

        // the triangles that hang together with the clicked one
        var face = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            var c = corners[queue.Dequeue()];
            for (var k = 0; k < 3; k++)
            {
                long a = c[k], b = c[(k + 1) % 3];
                foreach (var neighbour in byEdge[Math.Min(a, b) << 32 | Math.Max(a, b)])
                {
                    if (face.Add(neighbour)) queue.Enqueue(neighbour);
                }
            }
        }

        var triangles = new List<(Vector3, Vector3, Vector3)>(face.Count);
        var sum = Vector3.Zero;
        var area = 0f;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var t in face)
        {
            var (a, b, c) = (vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);
            triangles.Add((a, b, c));
            var size = Vector3.Cross(b - a, c - a).Length() / 2;
            sum += (a + b + c) / 3 * size;
            area += size;
            foreach (var v in new[] { a, b, c })
            {
                min = Vector3.Min(min, v);
                max = Vector3.Max(max, v);
            }
        }

        if (area <= 0) return null;
        return new FaceInfo([.. triangles], sum / area, faceNormal, Vector3.Distance(min, max));
    }

    private static Vector3 FaceNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var length = cross.Length();
        return length < 1e-12f ? Vector3.Zero : cross / length;
    }

    /// <summary>True when the point (in the plane of the triangle) lies inside it, with a tolerance in barycentric units.</summary>
    internal static bool Inside(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float tolerance)
    {
        var v0 = c - a;
        var v1 = b - a;
        var v2 = p - a;
        var d00 = Vector3.Dot(v0, v0);
        var d01 = Vector3.Dot(v0, v1);
        var d02 = Vector3.Dot(v0, v2);
        var d11 = Vector3.Dot(v1, v1);
        var d12 = Vector3.Dot(v1, v2);
        var denominator = d00 * d11 - d01 * d01;
        if (MathF.Abs(denominator) < 1e-12f) return false;
        var u = (d11 * d02 - d01 * d12) / denominator;
        var v = (d00 * d12 - d01 * d02) / denominator;
        return u >= -tolerance && v >= -tolerance && u + v <= 1 + tolerance;
    }
}
