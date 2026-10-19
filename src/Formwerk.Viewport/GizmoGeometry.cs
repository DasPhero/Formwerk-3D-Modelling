using System.Numerics;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>Builds the meshes of the move arrows, plane handles and rotation rings (unit size: radius/length 1).</summary>
internal static class GizmoGeometry
{
    private static Point3D P(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Two unit vectors that are perpendicular to <paramref name="direction"/> and to each other.</summary>
    internal static (Vector3 U, Vector3 V) Perpendicular(Vector3 direction)
    {
        var helper = Math.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(direction, helper));
        return (u, Vector3.Cross(direction, u));
    }

    public static void AddTube(MeshGeometry3D mesh, Vector3 from, Vector3 to, float radius, int segments = 12)
    {
        var direction = Vector3.Normalize(to - from);
        var (u, v) = Perpendicular(direction);
        var start = mesh.Positions.Count;
        for (var i = 0; i < segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            var offset = (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            mesh.Positions.Add(P(from + offset));
            mesh.Positions.Add(P(to + offset));
        }

        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % segments;
            Triangle(mesh, start + 2 * i, start + 2 * j, start + 2 * j + 1);
            Triangle(mesh, start + 2 * i, start + 2 * j + 1, start + 2 * i + 1);
        }
    }

    public static void AddCone(MeshGeometry3D mesh, Vector3 baseCenter, Vector3 tip, float radius, int segments = 16)
    {
        var direction = Vector3.Normalize(tip - baseCenter);
        var (u, v) = Perpendicular(direction);
        var start = mesh.Positions.Count;
        for (var i = 0; i < segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            mesh.Positions.Add(P(baseCenter + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius));
        }

        var tipIndex = mesh.Positions.Count;
        mesh.Positions.Add(P(tip));
        var centerIndex = mesh.Positions.Count;
        mesh.Positions.Add(P(baseCenter));
        for (var i = 0; i < segments; i++)
        {
            var j = (i + 1) % segments;
            Triangle(mesh, start + i, start + j, tipIndex);
            Triangle(mesh, centerIndex, start + j, start + i);
        }
    }

    /// <summary>A ring of the given radius in the plane perpendicular to <paramref name="axis"/>, with a round cross-section.</summary>
    public static void AddRing(MeshGeometry3D mesh, Vector3 axis, float radius, float tubeRadius, int segments = 64, int tubeSegments = 8)
    {
        var (u, v) = Perpendicular(axis);
        var start = mesh.Positions.Count;
        for (var i = 0; i < segments; i++)
        {
            var angle = 2 * MathF.PI * i / segments;
            var radial = u * MathF.Cos(angle) + v * MathF.Sin(angle);
            for (var k = 0; k < tubeSegments; k++)
            {
                var phi = 2 * MathF.PI * k / tubeSegments;
                mesh.Positions.Add(P(radial * radius + (radial * MathF.Cos(phi) + axis * MathF.Sin(phi)) * tubeRadius));
            }
        }

        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % segments;
            for (var k = 0; k < tubeSegments; k++)
            {
                var kn = (k + 1) % tubeSegments;
                var a = start + i * tubeSegments + k;
                var b = start + next * tubeSegments + k;
                var c = start + next * tubeSegments + kn;
                var d = start + i * tubeSegments + kn;
                Triangle(mesh, a, b, c);
                Triangle(mesh, a, c, d);
            }
        }
    }

    /// <summary>A square in the plane spanned by <paramref name="u"/> and <paramref name="v"/>, from <paramref name="min"/> to <paramref name="max"/> along both.</summary>
    public static void AddSquare(MeshGeometry3D mesh, Vector3 u, Vector3 v, float min, float max)
    {
        var start = mesh.Positions.Count;
        mesh.Positions.Add(P(u * min + v * min));
        mesh.Positions.Add(P(u * max + v * min));
        mesh.Positions.Add(P(u * max + v * max));
        mesh.Positions.Add(P(u * min + v * max));
        Triangle(mesh, start, start + 1, start + 2);
        Triangle(mesh, start, start + 2, start + 3);
    }

    /// <summary>A cube with the given half edge length, turned by <paramref name="rotation"/>.</summary>
    public static void AddBox(MeshGeometry3D mesh, Vector3 center, System.Numerics.Quaternion rotation, float half)
    {
        var start = mesh.Positions.Count;
        for (var i = 0; i < 8; i++)
        {
            var corner = new Vector3((i & 1) != 0 ? half : -half, (i & 2) != 0 ? half : -half, (i & 4) != 0 ? half : -half);
            mesh.Positions.Add(P(center + Vector3.Transform(corner, rotation)));
        }

        // pairs of triangles for the six faces (corner index bits: 1 = x, 2 = y, 4 = z)
        int[][] faces = [[0, 1, 3, 2], [4, 6, 7, 5], [0, 4, 5, 1], [2, 3, 7, 6], [0, 2, 6, 4], [1, 5, 7, 3]];
        foreach (var f in faces)
        {
            Triangle(mesh, start + f[0], start + f[1], start + f[2]);
            Triangle(mesh, start + f[0], start + f[2], start + f[3]);
        }
    }

    private static void Triangle(MeshGeometry3D mesh, int a, int b, int c)
    {
        mesh.TriangleIndices.Add(a);
        mesh.TriangleIndices.Add(b);
        mesh.TriangleIndices.Add(c);
    }
}
