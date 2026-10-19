using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Repair;

/// <summary>Healthy shapes and ways to damage them, for the analysis and repair tests.</summary>
internal static class MeshDefects
{
    public static MeshData Box(double w = 10, double d = 20, double h = 30) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = w, ["depth"] = d, ["height"] = h });

    public static MeshData Sphere(double diameter = 20) =>
        new SphereGenerator().Generate(new Dictionary<string, double> { ["diameter"] = diameter });

    public static MeshData Cylinder(double diameter = 20, double height = 30) =>
        new CylinderGenerator().Generate(new Dictionary<string, double> { ["diameter"] = diameter, ["height"] = height });

    public static MeshData Move(MeshData mesh, float x, float y, float z) => mesh.Transformed(Matrix4x4.CreateTranslation(x, y, z));

    /// <summary>The mesh without the given triangles (a hole).</summary>
    public static MeshData Without(MeshData mesh, params int[] triangles)
    {
        var skip = triangles.ToHashSet();
        var indices = new List<int>();
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            if (skip.Contains(t)) continue;
            indices.Add(mesh.Indices[t * 3]);
            indices.Add(mesh.Indices[t * 3 + 1]);
            indices.Add(mesh.Indices[t * 3 + 2]);
        }

        return new MeshData(mesh.Vertices.ToArray(), [.. indices]);
    }

    /// <summary>The given triangles turned around.</summary>
    public static MeshData Flipped(MeshData mesh, params int[] triangles)
    {
        var indices = mesh.Indices.ToArray();
        foreach (var t in triangles) (indices[t * 3 + 1], indices[t * 3 + 2]) = (indices[t * 3 + 2], indices[t * 3 + 1]);
        return new MeshData(mesh.Vertices.ToArray(), indices);
    }

    public static MeshData FlippedAll(MeshData mesh) => Flipped(mesh, Enumerable.Range(0, mesh.TriangleCount).ToArray());

    /// <summary>Every triangle with its own three vertices, each moved by up to <paramref name="jitter"/> in every direction.</summary>
    public static MeshData Unwelded(MeshData mesh, float jitter = 0f, int seed = 1)
    {
        var random = new Random(seed);
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var index in mesh.Indices)
        {
            var v = mesh.Vertices[index];
            if (jitter > 0) v += new Vector3(Noise(random, jitter), Noise(random, jitter), Noise(random, jitter));
            indices.Add(vertices.Count);
            vertices.Add(v);
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    private static float Noise(Random random, float amount) => (float)(random.NextDouble() * 2 - 1) * amount;

    public static MeshData WithTriangle(MeshData mesh, int a, int b, int c) =>
        new(mesh.Vertices.ToArray(), [.. mesh.Indices.ToArray(), a, b, c]);

    public static MeshData WithDuplicate(MeshData mesh, int triangle) =>
        WithTriangle(mesh, mesh.Indices[triangle * 3], mesh.Indices[triangle * 3 + 1], mesh.Indices[triangle * 3 + 2]);

    /// <summary>The same triangle again, but turned around: an internal wall without thickness.</summary>
    public static MeshData WithMirroredDuplicate(MeshData mesh, int triangle) =>
        WithTriangle(mesh, mesh.Indices[triangle * 3], mesh.Indices[triangle * 3 + 2], mesh.Indices[triangle * 3 + 1]);

    public static MeshData Combine(MeshData first, MeshData second)
    {
        var offset = first.VertexCount;
        return new MeshData(
            [.. first.Vertices.ToArray(), .. second.Vertices.ToArray()],
            [.. first.Indices.ToArray(), .. second.Indices.ToArray().Select(i => i + offset)]);
    }

    /// <summary>A small closed tetrahedron: a speck.</summary>
    public static MeshData Speck(float x, float y, float z, float size = 0.2f) =>
        new([new Vector3(x, y, z), new Vector3(x + size, y, z), new Vector3(x, y + size, z), new Vector3(x, y, z + size)],
            [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);

    /// <summary>Rotates the mesh so that it is no longer axis aligned (holes then are not planar by luck).</summary>
    public static MeshData Turned(MeshData mesh) => mesh.Transformed(Matrix4x4.CreateFromYawPitchRoll(0.4f, 0.3f, 0.2f));
}
