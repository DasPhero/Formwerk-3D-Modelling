using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>Chamfer (straight cut) or fillet (round) of an edge (F-EDG-01).</summary>
public enum EdgeKind
{
    Chamfer = 0,
    Fillet = 1,
}

/// <summary>Which edges of a box or cylinder are treated: all of them, only the ones at the top, or only the ones at the bottom.</summary>
public enum EdgeSide
{
    All = 0,
    Top = 1,
    Bottom = 2,
}

/// <summary>
/// Meshes of boxes and cylinders with chamfered or rounded edges (F-EDG-01), built directly (no boolean kernel). The shapes are convex
/// and watertight, the origin stays in the middle of the bottom face, and flat faces stay flat. A size that is too big for the shape is
/// cut down to just below the largest one that works (like the wall of a tube).
/// </summary>
public static class EdgeMeshes
{
    /// <summary>Segments of a quarter circle in a fillet along an edge.</summary>
    private const int QuarterSteps = 8;

    /// <summary>The largest size is this fraction of what would close the flat face to nothing, so no triangle degenerates.</summary>
    private const float Margin = 0.995f;

    // ---- box ------------------------------------------------------------------------------------------

    /// <summary>A box of <paramref name="width"/> x <paramref name="depth"/> x <paramref name="height"/> with the edges treated.</summary>
    public static MeshData Box(float width, float depth, float height, float size, EdgeKind kind, EdgeSide side)
    {
        float hx = width / 2, hy = depth / 2;
        if (side == EdgeSide.All) return RoundedBox(hx, hy, height, MathF.Min(size, MathF.Min(MathF.Min(hx, hy), height / 2) * Margin), kind);

        var top = TopTreatedBox(hx, hy, height, MathF.Min(size, MathF.Min(MathF.Min(hx, hy), height) * Margin), kind);
        return side == EdgeSide.Top ? top : MirrorHeight(top, height);
    }

    /// <summary>
    /// All twelve edges. The surface of the box is subdivided; every point <c>p</c> is pushed away from the inner box (the box made
    /// smaller by the size) to <c>q + s * (p - q) / |p - q|</c>, with the Euclidean length for a fillet (round edges, spherical corners)
    /// and the sum of the absolute components for a chamfer (flat cuts, a triangle in each corner). That is the inner box grown by a ball
    /// or an octahedron.
    /// </summary>
    private static MeshData RoundedBox(float hx, float hy, float height, float s, EdgeKind kind)
    {
        var steps = kind == EdgeKind.Fillet ? QuarterSteps / 2 : 1;
        var weights = new float[steps + 1];
        for (var i = 0; i <= steps; i++) weights[i] = kind == EdgeKind.Fillet ? MathF.Tan(MathF.PI / 4 * i / steps) : (float)i / steps;
        weights[steps] = 1; // exactly: the stations meet the neighbouring face there

        float[] Stations(float lo, float hi)
        {
            var list = new float[2 * (steps + 1)];
            for (var i = 0; i <= steps; i++)
            {
                list[i] = lo + s - s * weights[steps - i];
                list[steps + 1 + i] = hi - s + s * weights[i];
            }

            return list;
        }

        float[][] lists = [Stations(-hx, hx), Stations(-hy, hy), Stations(0, height)];
        var innerMin = new Vector3(-hx + s, -hy + s, s);
        var innerMax = new Vector3(hx - s, hy - s, height - s);

        var vertices = new List<Vector3>();
        var ids = new Dictionary<int, int>();
        var indices = new List<int>();
        var last = lists[0].Length - 1;

        int Vertex(int[] index)
        {
            var key = index[0] + 16 * (index[1] + 16 * index[2]);
            if (ids.TryGetValue(key, out var id)) return id;
            var p = new Vector3(lists[0][index[0]], lists[1][index[1]], lists[2][index[2]]);
            var q = Vector3.Clamp(p, innerMin, innerMax);
            var v = p - q;
            var norm = kind == EdgeKind.Fillet ? v.Length() : MathF.Abs(v.X) + MathF.Abs(v.Y) + MathF.Abs(v.Z);
            id = vertices.Count;
            vertices.Add(q + v * (s / norm));
            ids[key] = id;
            return id;
        }

        for (var axis = 0; axis < 3; axis++)
        {
            int u = (axis + 1) % 3, w = (axis + 2) % 3; // (u, w, axis) is right-handed, so u -> w turns counter-clockwise seen from +axis
            foreach (var positive in new[] { false, true })
            {
                int V(int iu, int iw)
                {
                    var index = new int[3];
                    index[axis] = positive ? last : 0;
                    index[u] = iu;
                    index[w] = iw;
                    return Vertex(index);
                }

                for (var iu = 0; iu < last; iu++)
                {
                    for (var iw = 0; iw < last; iw++)
                    {
                        int a = V(iu, iw), b = V(iu + 1, iw), c = V(iu + 1, iw + 1), d = V(iu, iw + 1);
                        if (positive) indices.AddRange([a, b, c, a, c, d]);
                        else indices.AddRange([a, c, b, a, d, c]);
                    }
                }
            }
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    /// <summary>
    /// Only the four top edges. The box is the height field <c>z = min(fx(x), fy(y))</c> over its base: the profile of a chamfer or
    /// fillet in X and in Y, so the two meet in a mitre at each top corner and the vertical edges stay sharp. Both profiles use the
    /// same stations, so the mitre runs along the diagonals of the grid cells and the surface is exact at its vertices.
    /// </summary>
    private static MeshData TopTreatedBox(float hx, float hy, float height, float s, EdgeKind kind)
    {
        var steps = kind == EdgeKind.Fillet ? QuarterSteps : 1;

        // distance from the start of the treatment at each station of the profile, and how far the top drops there
        var offsets = new float[steps + 1];
        for (var k = 0; k <= steps; k++) offsets[k] = kind == EdgeKind.Fillet ? s * MathF.Sin(MathF.PI / 2 * k / steps) : s * k / steps;
        offsets[steps] = s;
        float Drop(float e) => kind == EdgeKind.Fillet ? s - MathF.Sqrt(MathF.Max(0, s * s - e * e)) : e;

        var count = 2 * (steps + 1);
        var offsetAt = new float[count]; // the same for X and Y
        var dropAt = new float[count];
        for (var i = 0; i < count; i++)
        {
            offsetAt[i] = i <= steps ? offsets[steps - i] : offsets[i - steps - 1];
            dropAt[i] = Drop(offsetAt[i]);
        }

        float[] Stations(float half)
        {
            var list = new float[count];
            for (var i = 0; i < count; i++) list[i] = i <= steps ? -(half - s) - offsetAt[i] : half - s + offsetAt[i];
            return list;
        }

        var xs = Stations(hx);
        var ys = Stations(hy);
        var vertices = new Vector3[2 * count * count];
        int Cap(int i, int j) => i * count + j;
        int Bottom(int i, int j) => count * count + i * count + j;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                vertices[Cap(i, j)] = new Vector3(xs[i], ys[j], height - MathF.Max(dropAt[i], dropAt[j]));
                vertices[Bottom(i, j)] = new Vector3(xs[i], ys[j], 0);
            }
        }

        var indices = new List<int>();
        for (var i = 0; i < count - 1; i++)
        {
            for (var j = 0; j < count - 1; j++)
            {
                int a = Cap(i, j), b = Cap(i + 1, j), c = Cap(i + 1, j + 1), d = Cap(i, j + 1);
                if ((xs[i] + xs[i + 1]) * (ys[j] + ys[j + 1]) >= 0) indices.AddRange([a, b, c, a, c, d]); // the mitre runs along this diagonal
                else indices.AddRange([a, b, d, b, c, d]);

                int e = Bottom(i, j), f = Bottom(i + 1, j), g = Bottom(i + 1, j + 1), h = Bottom(i, j + 1);
                indices.AddRange([e, g, f, e, h, g]);

                // the side walls: along the last row/column of the grid
                if (i == 0) indices.AddRange([e, Cap(i, j + 1), h, e, Cap(i, j), Cap(i, j + 1)]);
                if (i == count - 2) indices.AddRange([f, g, c, f, c, b]);
                if (j == 0) indices.AddRange([e, f, b, e, b, a]);
                if (j == count - 2) indices.AddRange([h, c, g, h, d, c]);
            }
        }

        return new MeshData(vertices, [.. indices]);
    }

    /// <summary>The mesh turned upside down inside its height (the bottom becomes the top); the triangles keep facing outward.</summary>
    private static MeshData MirrorHeight(MeshData mesh, float height)
    {
        var vertices = new Vector3[mesh.VertexCount];
        for (var i = 0; i < vertices.Length; i++) vertices[i] = new Vector3(mesh.Vertices[i].X, mesh.Vertices[i].Y, height - mesh.Vertices[i].Z);

        var indices = mesh.Indices.ToArray();
        for (var t = 0; t < indices.Length; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        return new MeshData(vertices, indices);
    }

    // ---- cylinder -------------------------------------------------------------------------------------

    /// <summary>A cylinder with the rims treated: the cross section (radius, height) turned around the axis.</summary>
    public static MeshData Cylinder(float radius, float height, int segments, float size, EdgeKind kind, EdgeSide side)
    {
        var s = MathF.Min(size, (side == EdgeSide.All ? MathF.Min(radius, height / 2) : MathF.Min(radius, height)) * Margin);
        var steps = kind == EdgeKind.Fillet ? QuarterSteps : 1;

        // the outline from the bottom to the top, without the points on the axis
        var profile = new List<(float R, float Z)>();
        if (side != EdgeSide.Top)
        {
            for (var k = 0; k <= steps; k++)
            {
                var angle = MathF.PI / 2 * k / steps;
                profile.Add(kind == EdgeKind.Fillet
                    ? (radius - s + s * MathF.Sin(angle), s - s * MathF.Cos(angle))
                    : (radius - s + s * k / steps, s * k / steps));
            }
        }
        else
        {
            profile.Add((radius, 0));
        }

        if (side != EdgeSide.Bottom)
        {
            for (var k = 0; k <= steps; k++)
            {
                var angle = MathF.PI / 2 * k / steps;
                profile.Add(kind == EdgeKind.Fillet
                    ? (radius - s + s * MathF.Cos(angle), height - s + s * MathF.Sin(angle))
                    : (radius - s * k / steps, height - s + s * k / steps));
            }
        }
        else
        {
            profile.Add((radius, height));
        }

        var n = segments;
        var vertices = new Vector3[profile.Count * n + 2];
        for (var p = 0; p < profile.Count; p++)
        {
            for (var k = 0; k < n; k++)
            {
                var (sin, cos) = MathF.SinCos(2 * MathF.PI * k / n);
                vertices[p * n + k] = new Vector3(profile[p].R * cos, profile[p].R * sin, profile[p].Z);
            }
        }

        var bottomCenter = profile.Count * n;
        var topCenter = bottomCenter + 1;
        vertices[bottomCenter] = Vector3.Zero;
        vertices[topCenter] = new Vector3(0, 0, height);

        var indices = new List<int>(profile.Count * n * 6);
        var lastRing = (profile.Count - 1) * n;
        for (var k = 0; k < n; k++)
        {
            var j = (k + 1) % n;
            indices.AddRange([bottomCenter, j, k]);
            indices.AddRange([topCenter, lastRing + k, lastRing + j]);
            for (var p = 0; p < profile.Count - 1; p++)
            {
                int a = p * n, b = (p + 1) * n;
                indices.AddRange([a + k, a + j, b + j, a + k, b + j, b + k]);
            }
        }

        return new MeshData(vertices, [.. indices]);
    }
}
