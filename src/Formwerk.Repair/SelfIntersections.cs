using System.Numerics;

namespace Formwerk.Repair;

public static partial class MeshAnalyzer
{
    /// <summary>Meshes with more triangles are not checked for self-intersections (the check would take too long on the import).</summary>
    public const int MaxIntersectionTriangles = 300_000;

    private const float HitEpsilon = 1e-6f;

    /// <summary>
    /// Pairs of triangles that cut through each other, with the line where they do (<see cref="Segment"/>). Triangles that share a vertex,
    /// that only touch and coplanar triangles are not counted: a hit of an edge must lie strictly inside the other triangle. At most
    /// <paramref name="limit"/> pairs are returned; <paramref name="more"/> says there were more.
    /// </summary>
    internal static List<(int A, int B, Vector3 From, Vector3 To)> FindSelfIntersections(Vector3[] vertices, int[] indices, bool[] repeated, int limit, out bool more)
    {
        more = false;
        var triangleCount = indices.Length / 3;
        var result = new List<(int, int, Vector3, Vector3)>();
        if (triangleCount < 2 || triangleCount > MaxIntersectionTriangles) return result;

        // bounds of every triangle and the grid cell size (about the size of a typical triangle)
        var min = new Vector3[triangleCount];
        var max = new Vector3[triangleCount];
        var lo = new Vector3(float.MaxValue);
        var hi = new Vector3(float.MinValue);
        double extentSum = 0;
        var counted = 0;
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
            min[t] = Vector3.Min(a, Vector3.Min(b, c));
            max[t] = Vector3.Max(a, Vector3.Max(b, c));
            lo = Vector3.Min(lo, min[t]);
            hi = Vector3.Max(hi, max[t]);
            var size = max[t] - min[t];
            extentSum += Math.Max(size.X, Math.Max(size.Y, size.Z));
            counted++;
        }

        if (counted < 2) return result;
        var diagonal = (hi - lo).Length();
        var cell = (float)Math.Max(extentSum / counted * 1.5, diagonal / 256.0);
        if (cell <= 0) return result;

        var grid = new Dictionary<long, List<int>>();
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            var x0 = (int)Math.Floor((min[t].X - lo.X) / cell);
            var y0 = (int)Math.Floor((min[t].Y - lo.Y) / cell);
            var z0 = (int)Math.Floor((min[t].Z - lo.Z) / cell);
            var x1 = (int)Math.Floor((max[t].X - lo.X) / cell);
            var y1 = (int)Math.Floor((max[t].Y - lo.Y) / cell);
            var z1 = (int)Math.Floor((max[t].Z - lo.Z) / cell);
            for (var x = x0; x <= x1; x++)
            for (var y = y0; y <= y1; y++)
            for (var z = z0; z <= z1; z++)
            {
                var key = ((long)x << 42) ^ ((long)y << 21) ^ z;
                if (!grid.TryGetValue(key, out var list)) grid[key] = list = [];
                list.Add(t);
            }
        }

        var tested = new HashSet<long>();
        var hits = new List<Vector3>(6);
        foreach (var list in grid.Values)
        {
            for (var i = 0; i < list.Count; i++)
            for (var j = i + 1; j < list.Count; j++)
            {
                int a = list[i], b = list[j];
                if (!tested.Add((long)a * triangleCount + b)) continue;
                if (!Overlap(min[a], max[a], min[b], max[b]) || SharesVertex(indices, a, b)) continue;
                if (!Intersect(vertices, indices, a, b, hits, out var from, out var to)) continue;

                if (result.Count >= limit)
                {
                    more = true;
                    return result;
                }

                result.Add((Math.Min(a, b), Math.Max(a, b), from, to));
            }
        }

        return result;
    }

    private static bool Overlap(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB) =>
        minA.X <= maxB.X && maxA.X >= minB.X && minA.Y <= maxB.Y && maxA.Y >= minB.Y && minA.Z <= maxB.Z && maxA.Z >= minB.Z;

    private static bool SharesVertex(int[] indices, int a, int b)
    {
        for (var i = 0; i < 3; i++)
        for (var j = 0; j < 3; j++)
        {
            if (indices[a * 3 + i] == indices[b * 3 + j]) return true;
        }

        return false;
    }

    /// <summary>Do the two triangles pierce each other, and where (the two hits that are farthest apart)?</summary>
    private static bool Intersect(Vector3[] vertices, int[] indices, int a, int b, List<Vector3> hits, out Vector3 from, out Vector3 to)
    {
        hits.Clear();
        Vector3 a0 = vertices[indices[a * 3]], a1 = vertices[indices[a * 3 + 1]], a2 = vertices[indices[a * 3 + 2]];
        Vector3 b0 = vertices[indices[b * 3]], b1 = vertices[indices[b * 3 + 1]], b2 = vertices[indices[b * 3 + 2]];
        EdgeHits(a0, a1, a2, b0, b1, b2, hits);
        EdgeHits(b0, b1, b2, a0, a1, a2, hits);
        from = to = default;
        if (hits.Count == 0) return false;

        from = hits[0];
        to = hits[0];
        var best = -1f;
        for (var i = 0; i < hits.Count; i++)
        for (var j = i + 1; j < hits.Count; j++)
        {
            var d = Vector3.DistanceSquared(hits[i], hits[j]);
            if (d <= best) continue;
            best = d;
            from = hits[i];
            to = hits[j];
        }

        return true;
    }

    /// <summary>The points where the three edges of the first triangle go through the second one.</summary>
    private static void EdgeHits(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 a, Vector3 b, Vector3 c, List<Vector3> hits)
    {
        if (SegmentThroughTriangle(p0, p1, a, b, c, out var h)) hits.Add(h);
        if (SegmentThroughTriangle(p1, p2, a, b, c, out h)) hits.Add(h);
        if (SegmentThroughTriangle(p2, p0, a, b, c, out h)) hits.Add(h);
    }

    /// <summary>Moeller-Trumbore for a segment; the hit must be strictly inside the triangle and strictly between the ends of the segment.</summary>
    private static bool SegmentThroughTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, out Vector3 hit)
    {
        hit = default;
        var d = q - p;
        var e1 = b - a;
        var e2 = c - a;
        var h = Vector3.Cross(d, e2);
        var det = Vector3.Dot(e1, h);
        if (MathF.Abs(det) < 1e-12f * Math.Max(d.Length() * e1.Length() * e2.Length(), 1e-30f)) return false; // parallel or coplanar

        var f = 1f / det;
        var s = p - a;
        var u = f * Vector3.Dot(s, h);
        if (u <= HitEpsilon || u >= 1 - HitEpsilon) return false;
        var qv = Vector3.Cross(s, e1);
        var v = f * Vector3.Dot(d, qv);
        if (v <= HitEpsilon || u + v >= 1 - HitEpsilon) return false;
        var t = f * Vector3.Dot(e2, qv);
        if (t <= HitEpsilon || t >= 1 - HitEpsilon) return false;

        hit = p + d * t;
        return true;
    }
}
