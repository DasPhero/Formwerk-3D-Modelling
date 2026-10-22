using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>A round wall that is part of a cylinder: a point on its axis (at the height of the click), the axis, the radius, and whether the material is outside the circle (the wall of a hole).</summary>
public sealed record CylinderInfo(Vector3 Center, Vector3 Axis, float Radius, bool IsHole);

/// <summary>
/// Finds the cylinder that a clicked piece of a round wall belongs to (E-07, diameter): the pieces that hang together smoothly around
/// the clicked one are collected, the axis is the direction that all their normals are perpendicular to, and a circle is fitted
/// through their corners. A flat face, a cone, a sphere or a wall of a few big flat pieces gives nothing.
/// </summary>
public static class CylinderFinder
{
    private const float SeedDirection = 0.999f;
    private const float SeedPlane = 0.05f;
    private const float SmoothCos = 0.9063f; // neighbours that turn by less than 25 degrees belong to the same curve
    private const float MinTurnSin = 0.1736f; // the piece that turns away most (sideways) must differ by 10 degrees at least, else it is flat
    private const float AxisTolerance = 0.03f; // |dot(normal, axis)| of every piece
    private const int MaxTriangles = 60_000;
    private const float MinCoverageDegrees = 20;

    public static CylinderInfo? Find(MeshData mesh, Vector3 point, Vector3 normal)
    {
        if (mesh.IsEmpty || normal.LengthSquared() < 1e-12f) return null;
        normal = Vector3.Normalize(normal);
        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        var count = mesh.TriangleCount;

        // vertices at the same position are the same vertex
        var keys = new Dictionary<(int, int, int), int>();
        var positions = new List<Vector3>();
        int Id(Vector3 v)
        {
            var key = ((int)MathF.Round(v.X * 1000), (int)MathF.Round(v.Y * 1000), (int)MathF.Round(v.Z * 1000));
            if (!keys.TryGetValue(key, out var id))
            {
                id = positions.Count;
                keys[key] = id;
                positions.Add(v);
            }

            return id;
        }

        var corners = new int[count][];
        var normals = new Vector3[count];
        var byEdge = new Dictionary<long, List<int>>();
        static long Key(int a, int b) => (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
        for (var t = 0; t < count; t++)
        {
            var c = new[] { Id(vertices[indices[3 * t]]), Id(vertices[indices[3 * t + 1]]), Id(vertices[indices[3 * t + 2]]) };
            corners[t] = c;
            if (c[0] == c[1] || c[1] == c[2] || c[0] == c[2]) continue;
            var cross = Vector3.Cross(vertices[indices[3 * t + 1]] - vertices[indices[3 * t]], vertices[indices[3 * t + 2]] - vertices[indices[3 * t]]);
            if (cross.LengthSquared() < 1e-18f) continue;
            normals[t] = Vector3.Normalize(cross);
            for (var k = 0; k < 3; k++)
            {
                var key = Key(c[k], c[(k + 1) % 3]);
                if (!byEdge.TryGetValue(key, out var list)) byEdge[key] = list = [];
                list.Add(t);
            }
        }

        // the clicked piece
        var seed = -1;
        var best = float.MaxValue;
        for (var t = 0; t < count; t++)
        {
            if (normals[t] == Vector3.Zero || Vector3.Dot(normals[t], normal) < SeedDirection) continue;
            var a = vertices[indices[3 * t]];
            var distance = MathF.Abs(Vector3.Dot(point - a, normals[t]));
            if (distance > SeedPlane || distance >= best) continue;
            if (!FaceFinder.Inside(point, a, vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], 1e-3f)) continue;
            seed = t;
            best = distance;
        }

        if (seed < 0) return null;

        // everything that hangs together with it smoothly
        var region = new List<int> { seed };
        var inRegion = new HashSet<int> { seed };
        for (var next = 0; next < region.Count; next++)
        {
            var t = region[next];
            for (var k = 0; k < 3; k++)
            {
                if (!byEdge.TryGetValue(Key(corners[t][k], corners[t][(k + 1) % 3]), out var neighbours)) continue;
                foreach (var neighbour in neighbours)
                {
                    if (inRegion.Contains(neighbour) || Vector3.Dot(normals[t], normals[neighbour]) < SmoothCos) continue;
                    inRegion.Add(neighbour);
                    region.Add(neighbour);
                    if (region.Count > MaxTriangles) return null;
                }
            }
        }

        // the axis: perpendicular to the clicked normal and to the one that differs most from it
        var seedNormal = normals[seed];
        var other = region.OrderByDescending(t => Vector3.Cross(seedNormal, normals[t]).LengthSquared()).First();
        var axis = Vector3.Cross(seedNormal, normals[other]);
        if (axis.Length() < MinTurnSin) return null; // flat: nothing turns away from the clicked piece
        axis = Vector3.Normalize(axis);
        if (region.Any(t => MathF.Abs(Vector3.Dot(normals[t], axis)) > AxisTolerance)) return null; // a cone, a sphere: not round about one axis

        // a circle through the corners, in the plane perpendicular to the axis
        var helper = MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(axis, helper));
        var v = Vector3.Cross(axis, u);
        var ids = region.SelectMany(t => corners[t]).Distinct().ToList();
        var points = ids.Select(i => (X: (double)Vector3.Dot(positions[i], u), Y: (double)Vector3.Dot(positions[i], v))).ToList();
        if (!FitCircle(points, out var cx, out var cy, out var radius)) return null;
        if (points.Any(p => Math.Abs(Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy)) - radius) > 0.01 * radius + 1e-3)) return null;
        if (Coverage(points, cx, cy) < MinCoverageDegrees) return null;

        var center = u * (float)cx + v * (float)cy + axis * Vector3.Dot(point, axis);
        var isHole = Vector3.Dot(seedNormal, point - center) < 0;
        return new CylinderInfo(center, axis, (float)radius, isHole);
    }

    /// <summary>The least squares circle (Kasa): <c>x² + y² + D x + E y + F = 0</c>, solved with the points moved to their middle.</summary>
    private static bool FitCircle(List<(double X, double Y)> points, out double cx, out double cy, out double radius)
    {
        cx = cy = radius = 0;
        if (points.Count < 3) return false;
        double mx = points.Average(p => p.X), my = points.Average(p => p.Y);
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
        foreach (var (px, py) in points)
        {
            double x = px - mx, y = py - my, z = x * x + y * y;
            sxx += x * x; sxy += x * y; syy += y * y; sx += x; sy += y; sxz += x * z; syz += y * z; sz += z;
        }

        // [sxx sxy sx; sxy syy sy; sx sy n] [D E F]ᵀ = -[sxz syz sz]ᵀ
        double n = points.Count;
        double[,] a = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
        double[] b = [-sxz, -syz, -sz];
        if (!Solve(a, b, out var solution)) return false;
        double d = solution[0], e = solution[1], f = solution[2];
        cx = -d / 2 + mx;
        cy = -e / 2 + my;
        var squared = d * d / 4 + e * e / 4 - f;
        if (squared <= 0) return false;
        radius = Math.Sqrt(squared);
        return true;
    }

    /// <summary>Solves a 3 x 3 system by elimination with the biggest pivot; false when it has no unique solution.</summary>
    private static bool Solve(double[,] a, double[] b, out double[] x)
    {
        x = new double[3];
        for (var column = 0; column < 3; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < 3; row++)
            {
                if (Math.Abs(a[row, column]) > Math.Abs(a[pivot, column])) pivot = row;
            }

            if (Math.Abs(a[pivot, column]) < 1e-12) return false;
            if (pivot != column)
            {
                for (var k = 0; k < 3; k++) (a[column, k], a[pivot, k]) = (a[pivot, k], a[column, k]);
                (b[column], b[pivot]) = (b[pivot], b[column]);
            }

            for (var row = column + 1; row < 3; row++)
            {
                var factor = a[row, column] / a[column, column];
                for (var k = column; k < 3; k++) a[row, k] -= factor * a[column, k];
                b[row] -= factor * b[column];
            }
        }

        for (var row = 2; row >= 0; row--)
        {
            var sum = b[row];
            for (var k = row + 1; k < 3; k++) sum -= a[row, k] * x[k];
            x[row] = sum / a[row, row];
        }

        return true;
    }

    /// <summary>How many degrees around the center the points cover (360 minus the biggest gap).</summary>
    private static double Coverage(List<(double X, double Y)> points, double cx, double cy)
    {
        var angles = points.Select(p => Math.Atan2(p.Y - cy, p.X - cx) * 180 / Math.PI).OrderBy(a => a).ToList();
        var gap = 360 + angles[0] - angles[^1];
        for (var i = 1; i < angles.Count; i++) gap = Math.Max(gap, angles[i] - angles[i - 1]);
        return 360 - gap;
    }
}
