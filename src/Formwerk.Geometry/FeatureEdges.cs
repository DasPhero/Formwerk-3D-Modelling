using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>Line segments in space: <c>Points[2 i]</c> to <c>Points[2 i + 1]</c> is segment <c>i</c>.</summary>
public sealed class EdgeSet(Vector3[] points)
{
    public static EdgeSet Empty { get; } = new([]);

    public Vector3[] Points { get; } = points;

    public int Count => Points.Length / 2;

    public (Vector3 A, Vector3 B) Segment(int index) => (Points[index * 2], Points[index * 2 + 1]);
}

/// <summary>
/// The lines a viewport draws on a shaded mesh (F-VIS-02) and the lines of the wireframe (F-VIS-03). Triangles are connected by the
/// position of their corners, not by vertex index, so a mesh that does not share its vertices gives the same lines.
/// </summary>
public static class FeatureEdges
{
    /// <summary>Tolerance when comparing the fold angle of an edge with the threshold, so that an edge exactly at the angle counts as sharp.</summary>
    private const double AngleToleranceDegrees = 1e-3;

    /// <summary>
    /// The edges that stand out: where two faces meet at an angle of at least <paramref name="angleDegrees"/>, the border of an open
    /// mesh, and edges that more than two triangles share. Triangles without area are ignored.
    /// </summary>
    public static EdgeSet Extract(MeshData mesh, double angleDegrees)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.IsEmpty) return EdgeSet.Empty;

        var (positions, corner) = WeldByPosition(mesh);
        var triangles = mesh.TriangleCount;
        var normals = new Vector3[triangles];
        var valid = new bool[triangles];
        var count = 0;
        for (var t = 0; t < triangles; t++)
        {
            int a = corner[t * 3], b = corner[t * 3 + 1], c = corner[t * 3 + 2];
            if (a == b || b == c || a == c) continue;
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            var length = normal.Length();
            if (length < 1e-12f) continue; // no area, no direction
            normals[t] = normal / length;
            valid[t] = true;
            count++;
        }

        var keys = new ulong[count * 3];
        var owners = new int[count * 3];
        var n = 0;
        for (var t = 0; t < triangles; t++)
        {
            if (!valid[t]) continue;
            for (var e = 0; e < 3; e++)
            {
                keys[n] = Key(corner[t * 3 + e], corner[t * 3 + (e + 1) % 3]);
                owners[n++] = t;
            }
        }

        Array.Sort(keys, owners);

        var threshold = Math.Cos((angleDegrees - AngleToleranceDegrees) * Math.PI / 180);
        var points = new List<Vector3>();
        for (var i = 0; i < keys.Length;)
        {
            var end = i + 1;
            while (end < keys.Length && keys[end] == keys[i]) end++;
            var group = end - i;

            var show = group != 2 || Vector3.Dot(normals[owners[i]], normals[owners[i + 1]]) <= threshold;
            if (show)
            {
                points.Add(positions[(int)(keys[i] >> 32)]);
                points.Add(positions[(int)(keys[i] & 0xFFFFFFFF)]);
            }

            i = end;
        }

        return new EdgeSet([.. points]);
    }

    /// <summary>Every edge of every triangle, each once (the wireframe).</summary>
    public static EdgeSet AllEdges(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.IsEmpty) return EdgeSet.Empty;

        var (positions, corner) = WeldByPosition(mesh);
        var keys = new List<ulong>(corner.Length);
        for (var t = 0; t < corner.Length / 3; t++)
        {
            int a = corner[t * 3], b = corner[t * 3 + 1], c = corner[t * 3 + 2];
            if (a == b || b == c || a == c) continue;
            keys.Add(Key(a, b));
            keys.Add(Key(b, c));
            keys.Add(Key(c, a));
        }

        var sorted = keys.ToArray();
        Array.Sort(sorted);
        var points = new List<Vector3>(sorted.Length);
        for (var i = 0; i < sorted.Length; i++)
        {
            if (i > 0 && sorted[i] == sorted[i - 1]) continue;
            points.Add(positions[(int)(sorted[i] >> 32)]);
            points.Add(positions[(int)(sorted[i] & 0xFFFFFFFF)]);
        }

        return new EdgeSet([.. points]);
    }

    private static ulong Key(int a, int b) => a < b ? ((ulong)(uint)a << 32) | (uint)b : ((ulong)(uint)b << 32) | (uint)a;

    /// <summary>Gives every distinct position one id and returns the triangle corners as ids.</summary>
    private static (List<Vector3> Positions, int[] Corner) WeldByPosition(MeshData mesh)
    {
        var vertices = mesh.Vertices;
        var ids = new int[vertices.Length];
        var positions = new List<Vector3>();
        var lookup = new Dictionary<Vector3, int>();
        for (var i = 0; i < vertices.Length; i++)
        {
            var v = vertices[i];
            if (v.X == 0) v.X = 0; // -0 and +0 are one position
            if (v.Y == 0) v.Y = 0;
            if (v.Z == 0) v.Z = 0;
            if (!lookup.TryGetValue(v, out var id))
            {
                id = positions.Count;
                positions.Add(v);
                lookup[v] = id;
            }

            ids[i] = id;
        }

        var indices = mesh.Indices;
        var corner = new int[indices.Length];
        for (var i = 0; i < indices.Length; i++) corner[i] = ids[indices[i]];
        return (positions, corner);
    }
}
