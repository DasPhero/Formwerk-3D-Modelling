using System.Numerics;
using System.Text;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>Writes binary STL (E-03). Coordinates are millimetres, Z up.</summary>
public static class StlWriter
{
    private const int HeaderSize = 80;
    private const int TriangleSize = 50;

    public static void WriteBinary(Stream stream, IEnumerable<MeshData> meshes, string headerText = "Formwerk")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(meshes);
        var list = meshes.ToList();

        long triangleCount = list.Sum(m => (long)m.TriangleCount);
        if (triangleCount > uint.MaxValue) throw new InvalidOperationException("The model has too many triangles for STL.");

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var header = new byte[HeaderSize];
        Encoding.ASCII.GetBytes(headerText.AsSpan(0, Math.Min(headerText.Length, HeaderSize)), header);
        writer.Write(header);
        writer.Write((uint)triangleCount);

        foreach (var mesh in list)
        {
            var v = mesh.Vertices;
            var idx = mesh.Indices;
            for (var t = 0; t < idx.Length; t += 3)
            {
                var a = v[idx[t]];
                var b = v[idx[t + 1]];
                var c = v[idx[t + 2]];
                var normal = Vector3.Cross(b - a, c - a);
                normal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.Zero;
                Write(writer, normal);
                Write(writer, a);
                Write(writer, b);
                Write(writer, c);
                writer.Write((ushort)0);
            }
        }
    }

    /// <summary>Size in bytes of a binary STL with the given number of triangles.</summary>
    public static long SizeFor(long triangles) => HeaderSize + 4 + triangles * TriangleSize;

    private static void Write(BinaryWriter writer, Vector3 v)
    {
        writer.Write(v.X);
        writer.Write(v.Y);
        writer.Write(v.Z);
    }
}
