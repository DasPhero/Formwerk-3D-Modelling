using System.Numerics;
using System.Runtime.InteropServices;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>
/// Binary format of <c>meshes/&lt;id&gt;.bin</c> in a project file: the bytes "FWM1", the vertex count and the index count
/// (int32 each), then the vertices (three float32 each) and the triangle indices (int32 each), all little endian.
/// The ZIP container compresses it.
/// </summary>
public static class MeshFormat
{
    private static readonly byte[] Magic = "FWM1"u8.ToArray();
    private const int HeaderSize = 12;

    public static void Write(Stream stream, MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mesh);
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Mesh files are little endian.");

        Span<byte> header = stackalloc byte[HeaderSize];
        Magic.CopyTo(header);
        BitConverter.TryWriteBytes(header[4..], mesh.VertexCount);
        BitConverter.TryWriteBytes(header[8..], mesh.Indices.Length);
        stream.Write(header);
        stream.Write(MemoryMarshal.AsBytes(mesh.Vertices));
        stream.Write(MemoryMarshal.AsBytes(mesh.Indices));
    }

    /// <exception cref="ProjectFormatException">The data is damaged.</exception>
    public static MeshData Read(ReadOnlySpan<byte> data)
    {
        if (!BitConverter.IsLittleEndian) throw new PlatformNotSupportedException("Mesh files are little endian.");
        if (data.Length < HeaderSize || !data[..4].SequenceEqual(Magic))
            throw new ProjectFormatException("The file is damaged: a mesh has an unknown format.");

        var vertexCount = BitConverter.ToInt32(data[4..]);
        var indexCount = BitConverter.ToInt32(data[8..]);
        if (vertexCount < 0 || vertexCount > MeshImportLimits.MaxVertices
            || indexCount < 0 || indexCount > MeshImportLimits.MaxTriangles * 3L || indexCount % 3 != 0)
            throw new ProjectFormatException("The file is damaged: a mesh has an invalid size.");

        var expected = HeaderSize + (long)vertexCount * 12 + (long)indexCount * 4;
        if (expected != data.Length) throw new ProjectFormatException("The file is damaged: a mesh is incomplete.");

        var vertices = new Vector3[vertexCount];
        data.Slice(HeaderSize, vertexCount * 12).CopyTo(MemoryMarshal.AsBytes(vertices.AsSpan()));
        var indices = new int[indexCount];
        data.Slice(HeaderSize + vertexCount * 12, indexCount * 4).CopyTo(MemoryMarshal.AsBytes(indices.AsSpan()));

        foreach (var v in vertices)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                throw new ProjectFormatException("The file is damaged: a mesh contains an invalid number.");
        }

        try
        {
            return new MeshData(vertices, indices);
        }
        catch (ArgumentException ex)
        {
            throw new ProjectFormatException("The file is damaged: a mesh refers to points that do not exist.", ex);
        }
    }
}
