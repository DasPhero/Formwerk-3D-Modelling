using System.Numerics;

namespace Formwerk.Repair;

/// <summary>Union-find with path halving.</summary>
internal sealed class UnionFind
{
    private readonly int[] _parent;

    public UnionFind(int count)
    {
        _parent = new int[count];
        for (var i = 0; i < count; i++) _parent[i] = i;
    }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]];
            x = _parent[x];
        }

        return x;
    }

    public void Union(int a, int b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (ra != rb) _parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
    }
}

/// <summary>
/// All edges of a triangle list, sorted so that the triangles sharing an edge are next to each other. Memory is two flat arrays,
/// which keeps one million triangles in a few dozen megabytes.
/// </summary>
internal sealed class EdgeTable
{
    /// <summary>Edge key: smaller vertex index in the high half, larger in the low half.</summary>
    public ulong[] Keys { get; }

    /// <summary>Per edge: triangle index times two, plus one when the triangle runs the edge from the smaller to the larger index.</summary>
    public int[] Payload { get; }

    private EdgeTable(ulong[] keys, int[] payload)
    {
        Keys = keys;
        Payload = payload;
    }

    public int Count => Keys.Length;

    public static int TriangleOf(int payload) => payload >> 1;

    public static bool RunsUp(int payload) => (payload & 1) != 0;

    public static int Low(ulong key) => (int)(key >> 32);

    public static int High(ulong key) => (int)(key & 0xFFFFFFFF);

    /// <param name="indices">Triangle corner indices, three per triangle.</param>
    /// <param name="skip">Triangles that must not contribute edges; null for none.</param>
    public static EdgeTable Build(int[] indices, bool[]? skip = null)
    {
        var triangles = indices.Length / 3;
        var included = 0;
        for (var t = 0; t < triangles; t++)
        {
            if (skip is null || !skip[t]) included++;
        }

        var keys = new ulong[included * 3];
        var payload = new int[included * 3];
        var n = 0;
        for (var t = 0; t < triangles; t++)
        {
            if (skip is not null && skip[t]) continue;
            for (var e = 0; e < 3; e++)
            {
                var from = indices[t * 3 + e];
                var to = indices[t * 3 + (e + 1) % 3];
                var up = from < to;
                var low = up ? from : to;
                var high = up ? to : from;
                keys[n] = ((ulong)(uint)low << 32) | (uint)high;
                payload[n] = t * 2 + (up ? 1 : 0);
                n++;
            }
        }

        Array.Sort(keys, payload);
        return new EdgeTable(keys, payload);
    }

    /// <summary>Length of the run of equal keys that starts at <paramref name="start"/>.</summary>
    public int GroupLength(int start)
    {
        var end = start + 1;
        while (end < Keys.Length && Keys[end] == Keys[start]) end++;
        return end - start;
    }
}

/// <summary>Finds vertices that lie within a tolerance of an earlier vertex, with a hash of grid cells.</summary>
internal static class VertexWelder
{
    /// <summary>Distance below which two vertices count as the same point: a micron on a 100 mm part.</summary>
    public static float Tolerance(Vector3 boundsMin, Vector3 boundsMax)
    {
        var diagonal = (boundsMax - boundsMin).Length();
        return Math.Max(diagonal * 1e-5f, 1e-6f);
    }

    /// <summary>
    /// For every vertex the index of the first vertex within the tolerance (itself when there is none). Only vertices marked in
    /// <paramref name="used"/> take part.
    /// </summary>
    public static int[] Map(Vector3[] vertices, bool[] used, float tolerance)
    {
        var map = new int[vertices.Length];
        var head = new Dictionary<(long, long, long), int>();
        var next = new int[vertices.Length];
        var toleranceSquared = (double)tolerance * tolerance;

        for (var i = 0; i < vertices.Length; i++)
        {
            map[i] = i;
            if (!used[i]) continue;

            var v = vertices[i];
            var cx = (long)Math.Floor(v.X / tolerance);
            var cy = (long)Math.Floor(v.Y / tolerance);
            var cz = (long)Math.Floor(v.Z / tolerance);

            var found = -1;
            for (var dx = -1; dx <= 1 && found < 0; dx++)
            {
                for (var dy = -1; dy <= 1 && found < 0; dy++)
                {
                    for (var dz = -1; dz <= 1 && found < 0; dz++)
                    {
                        if (!head.TryGetValue((cx + dx, cy + dy, cz + dz), out var candidate)) continue;
                        for (; candidate >= 0; candidate = next[candidate])
                        {
                            if (candidate == i) continue;
                            var d = vertices[candidate] - v;
                            if ((double)d.X * d.X + (double)d.Y * d.Y + (double)d.Z * d.Z <= toleranceSquared)
                            {
                                found = candidate;
                                break;
                            }
                        }
                    }
                }
            }

            if (found >= 0)
            {
                map[i] = map[found];
                continue;
            }

            var cell = (cx, cy, cz);
            next[i] = head.TryGetValue(cell, out var first) ? first : -1;
            head[cell] = i;
        }

        return map;
    }
}
