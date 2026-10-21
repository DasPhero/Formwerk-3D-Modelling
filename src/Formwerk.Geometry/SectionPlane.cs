using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>
/// The cutting plane of the section view (E-06): the points with <c>dot(p, Normal) = Offset</c>. What is in front of it (positive
/// <see cref="Distance"/>, where <see cref="Normal"/> points) is cut away; the part behind it is kept.
/// </summary>
public readonly record struct SectionPlane(Vector3 Normal, float Offset)
{
    /// <summary>Signed distance of a point from the plane in mm: positive in front of it (cut away), negative behind it (kept).</summary>
    public float Distance(Vector3 point) => Vector3.Dot(point, Normal) - Offset;

    /// <summary>The same plane that keeps the other side.</summary>
    public SectionPlane Flipped => new(-Normal, -Offset);

    /// <summary>A plane across an axis (0 X, 1 Y, 2 Z) at a world coordinate; the part with the smaller coordinate is kept.</summary>
    public static SectionPlane Axis(int axis, float position) =>
        new(axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ }, position);

    /// <summary>The plane through <paramref name="point"/> that is perpendicular to <paramref name="normal"/>; the side the normal points to is cut away.</summary>
    public static SectionPlane FromNormal(Vector3 normal, Vector3 point)
    {
        var unit = Vector3.Normalize(normal);
        return new SectionPlane(unit, Vector3.Dot(unit, point));
    }
}

/// <summary>Cuts a mesh at a <see cref="SectionPlane"/> for the section view (E-06), without closing the cut: that is <c>SectionBuilder</c>'s cap.</summary>
public static class MeshClipper
{
    private const float Tolerance = 1e-5f;

    /// <summary>
    /// The part of <paramref name="mesh"/> behind the plane. Triangles across the plane are cut, so the new corners lie in the plane. The
    /// mesh itself is returned when nothing is in front of the plane, and an empty one when nothing is behind it. Vertices that the
    /// triangles shared stay shared (also the new ones on a cut edge), so the shading stays smooth. Any triangle mesh works, a closed
    /// one is not needed.
    /// </summary>
    public static MeshData Clip(MeshData mesh, SectionPlane plane)
    {
        if (mesh.IsEmpty) return mesh;
        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();

        var distance = new float[vertices.Length];
        float max = float.MinValue, min = float.MaxValue;
        for (var i = 0; i < vertices.Length; i++)
        {
            distance[i] = plane.Distance(vertices[i]);
            max = Math.Max(max, distance[i]);
            min = Math.Min(min, distance[i]);
        }

        if (max <= Tolerance) return mesh;          // nothing in front of the plane
        if (min >= -Tolerance) return MeshData.Empty; // nothing behind it

        var outVertices = new List<Vector3>();
        var outIndices = new List<int>(indices.Length);
        var kept = new Dictionary<int, int>();
        var cuts = new Dictionary<long, int>();

        int Keep(int index)
        {
            if (!kept.TryGetValue(index, out var id))
            {
                id = outVertices.Count;
                outVertices.Add(vertices[index]);
                kept[index] = id;
            }

            return id;
        }

        // the point where the edge a-b crosses the plane (the same one for both triangles that share the edge)
        int Cut(int a, int b)
        {
            var key = (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
            if (cuts.TryGetValue(key, out var id)) return id;
            int from = Math.Min(a, b), to = Math.Max(a, b);
            var t = Math.Clamp(distance[from] / (distance[from] - distance[to]), 0f, 1f);
            id = outVertices.Count;
            outVertices.Add(vertices[from] + (vertices[to] - vertices[from]) * t);
            cuts[key] = id;
            return id;
        }

        var polygon = new List<int>(4);
        for (var t = 0; t < indices.Length; t += 3)
        {
            int a = indices[t], b = indices[t + 1], c = indices[t + 2];
            float da = distance[a], db = distance[b], dc = distance[c];

            if (da <= Tolerance && db <= Tolerance && dc <= Tolerance)
            {
                outIndices.AddRange([Keep(a), Keep(b), Keep(c)]);
                continue;
            }

            if (da >= -Tolerance && db >= -Tolerance && dc >= -Tolerance) continue; // in front of the plane, or only touching it

            // across the plane: keep the polygon behind it (Sutherland-Hodgman on one plane), then split it into triangles
            polygon.Clear();
            int[] corner = [a, b, c];
            for (var k = 0; k < 3; k++)
            {
                int p = corner[k], q = corner[(k + 1) % 3];
                bool pBehind = distance[p] <= Tolerance, qBehind = distance[q] <= Tolerance;
                if (pBehind) polygon.Add(Keep(p));
                if (pBehind != qBehind) polygon.Add(Cut(p, q));
            }

            for (var k = 1; k + 1 < polygon.Count; k++) outIndices.AddRange([polygon[0], polygon[k], polygon[k + 1]]);
        }

        return outIndices.Count == 0 ? MeshData.Empty : new MeshData([.. outVertices], [.. outIndices]);
    }
}
