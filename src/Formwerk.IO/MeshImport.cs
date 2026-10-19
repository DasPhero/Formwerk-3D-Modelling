using System.Buffers.Binary;
using System.Buffers.Text;
using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>A model file cannot be imported. The message is meant to be shown to the user.</summary>
public sealed class MeshImportException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Limits that protect against damaged or hostile model files (N-SEC-01).</summary>
public static class MeshImportLimits
{
    public const long MaxFileBytes = 1L << 30;
    public const int MaxTriangles = 20_000_000;
    public const int MaxVertices = 20_000_000;
}

/// <summary>
/// The result of reading a model file: an indexed, welded mesh and its name. For STL and OBJ the mesh is in the unit of the file
/// (<see cref="UnitKnown"/> is false: the user is asked); a 3MF file states its unit, so its meshes are already in millimetres
/// (<see cref="UnitKnown"/> is true) and keep the arrangement they have in the file. <see cref="ColorArgb"/> is the color of the
/// object's material when the file has one.
/// </summary>
public sealed record ImportedMesh(MeshData Mesh, string Name, uint? ColorArgb = null, bool UnitKnown = false);

/// <summary>Reads STL, OBJ and 3MF files (F-IMP-01).</summary>
public static class MeshImport
{
    public const string DialogFilter = "3D models (*.stl;*.obj;*.3mf)|*.stl;*.obj;*.3mf|STL files (*.stl)|*.stl|OBJ files (*.obj)|*.obj|3MF files (*.3mf)|*.3mf";

    public static bool IsSupported(string path) =>
        Path.GetExtension(path).Equals(".stl", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(".obj", StringComparison.OrdinalIgnoreCase)
        || Path.GetExtension(path).Equals(ThreeMfReader.Extension, StringComparison.OrdinalIgnoreCase);

    /// <summary>One mesh for the whole file; a 3MF file with several objects is merged into one mesh (use <see cref="LoadAll"/> to keep them apart).</summary>
    /// <exception cref="MeshImportException">The file cannot be read as a model.</exception>
    public static ImportedMesh Load(string path)
    {
        var all = LoadAll(path);
        return all.Count == 1 ? all[0] : new ImportedMesh(Merge(all.Select(m => m.Mesh)), Path.GetFileNameWithoutExtension(path), UnitKnown: all[0].UnitKnown);
    }

    /// <summary>All meshes of the file: one for STL and OBJ, one per printable object for 3MF.</summary>
    /// <exception cref="MeshImportException">The file cannot be read as a model.</exception>
    public static IReadOnlyList<ImportedMesh> LoadAll(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".stl" or ".obj" or ThreeMfReader.Extension)) throw new MeshImportException("Formwerk can import STL, OBJ and 3MF files.");

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new MeshImportException("The file does not exist.");
            if (info.Length > MeshImportLimits.MaxFileBytes) throw new MeshImportException("The file is too large to import.");

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var stem = Path.GetFileNameWithoutExtension(path);
            if (extension == ThreeMfReader.Extension)
            {
                var items = ThreeMfReader.Read(stream);
                return [.. items.Select((item, i) => new ImportedMesh(
                    item.Mesh,
                    !string.IsNullOrWhiteSpace(item.Name) ? item.Name : items.Count == 1 ? stem : $"{stem} {i + 1}",
                    item.ColorArgb,
                    UnitKnown: true))];
            }

            var mesh = extension == ".stl" ? StlReader.Read(stream) : ObjReader.Read(stream);
            return [new ImportedMesh(mesh, stem)];
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new MeshImportException("Formwerk is not allowed to read this file.", ex);
        }
        catch (IOException ex)
        {
            throw new MeshImportException($"The file could not be read: {ex.Message}", ex);
        }
    }

    private static MeshData Merge(IEnumerable<MeshData> meshes)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var mesh in meshes)
        {
            var offset = vertices.Count;
            vertices.AddRange(mesh.Vertices.ToArray());
            foreach (var index in mesh.Indices) indices.Add(index + offset);
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    internal static byte[] ReadAll(Stream stream)
    {
        if (stream.CanSeek && stream.Length - stream.Position > MeshImportLimits.MaxFileBytes)
            throw new MeshImportException("The file is too large to import.");

        using var buffer = stream.CanSeek ? new MemoryStream((int)Math.Min(stream.Length - stream.Position, int.MaxValue)) : new MemoryStream();
        var chunk = new byte[1 << 16];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MeshImportLimits.MaxFileBytes) throw new MeshImportException("The file is too large to import.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>Exact position as key: two corners with identical coordinates become one vertex.</summary>
    internal readonly record struct VertexKey(int X, int Y, int Z)
    {
        public static VertexKey Of(Vector3 v)
        {
            // -0 and +0 are the same position
            var x = v.X == 0 ? 0f : v.X;
            var y = v.Y == 0 ? 0f : v.Y;
            var z = v.Z == 0 ? 0f : v.Z;
            return new VertexKey(BitConverter.SingleToInt32Bits(x), BitConverter.SingleToInt32Bits(y), BitConverter.SingleToInt32Bits(z));
        }
    }
}

public static class StlReader
{
    private const int HeaderSize = 80;
    private const int TriangleSize = 50;

    /// <summary>Binary or ASCII STL, detected from the content (not from the file name).</summary>
    public static MeshData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var data = MeshImport.ReadAll(stream);
        if (data.Length == 0) throw new MeshImportException("The file is empty.");

        if (IsBinary(data, out var count)) return ReadBinary(data, count);
        if (LooksLikeAscii(data)) return ReadAscii(data);
        throw new MeshImportException("This is not a valid STL file.");
    }

    private static bool IsBinary(byte[] data, out long count)
    {
        count = 0;
        if (data.Length < HeaderSize + 4) return false;
        count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(HeaderSize));
        var expected = HeaderSize + 4 + count * TriangleSize;
        if (expected == data.Length) return true;

        // A binary file with trailing bytes is accepted unless it starts like an ASCII file.
        return expected < data.Length && !LooksLikeAscii(data);
    }

    private static bool LooksLikeAscii(byte[] data)
    {
        var start = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) start = 3; // BOM
        while (start < data.Length && data[start] <= (byte)' ') start++;
        var head = data.AsSpan(start, Math.Min(5, data.Length - start));
        if (!head.SequenceEqual("solid"u8)) return false;
        return data.AsSpan(0, Math.Min(data.Length, 4096)).IndexOf("facet"u8) >= 0;
    }

    private static MeshData ReadBinary(byte[] data, long count)
    {
        if (count == 0) throw new MeshImportException("The file contains no triangles.");
        if (count > MeshImportLimits.MaxTriangles) throw new MeshImportException("The model has too many triangles to import.");
        if (HeaderSize + 4 + count * TriangleSize > data.Length)
            throw new MeshImportException("The file is damaged: it is shorter than its triangle count says.");

        var triangles = (int)count;
        var indices = new int[triangles * 3];
        var vertices = new List<Vector3>(Math.Min(triangles * 3 / 2 + 16, MeshImportLimits.MaxVertices));
        var map = new Dictionary<MeshImport.VertexKey, int>(vertices.Capacity);

        ReadOnlySpan<byte> span = data;
        for (var t = 0; t < triangles; t++)
        {
            var offset = HeaderSize + 4 + t * TriangleSize + 12; // skip the normal
            for (var c = 0; c < 3; c++, offset += 12)
            {
                var v = new Vector3(
                    BinaryPrimitives.ReadSingleLittleEndian(span[offset..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 4)..]),
                    BinaryPrimitives.ReadSingleLittleEndian(span[(offset + 8)..]));
                indices[t * 3 + c] = Weld(v, vertices, map);
            }
        }

        return new MeshData([.. vertices], indices);
    }

    private static MeshData ReadAscii(byte[] data)
    {
        var vertices = new List<Vector3>();
        var map = new Dictionary<MeshImport.VertexKey, int>();
        var indices = new List<int>();
        ReadOnlySpan<byte> span = data;
        var corners = 0;

        var position = 0;
        while (position < span.Length)
        {
            var end = span[position..].IndexOf((byte)'\n');
            var line = end < 0 ? span[position..] : span.Slice(position, end);
            position = end < 0 ? span.Length : position + end + 1;

            line = Trim(line);
            if (!line.StartsWith("vertex"u8)) continue;

            var rest = line["vertex".Length..];
            var v = new Vector3(NextFloat(ref rest), NextFloat(ref rest), NextFloat(ref rest));
            indices.Add(Weld(v, vertices, map));
            corners++;
            if (corners / 3 > MeshImportLimits.MaxTriangles) throw new MeshImportException("The model has too many triangles to import.");
        }

        if (corners == 0) throw new MeshImportException("The file contains no triangles.");
        if (corners % 3 != 0) throw new MeshImportException("The file is damaged: a facet does not have three corners.");
        return new MeshData([.. vertices], [.. indices]);
    }

    private static int Weld(Vector3 v, List<Vector3> vertices, Dictionary<MeshImport.VertexKey, int> map)
    {
        if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
            throw new MeshImportException("The file is damaged: a coordinate is not a valid number.");

        var key = MeshImport.VertexKey.Of(v);
        if (map.TryGetValue(key, out var index)) return index;
        if (vertices.Count >= MeshImportLimits.MaxVertices) throw new MeshImportException("The model has too many points to import.");
        index = vertices.Count;
        vertices.Add(v);
        map[key] = index;
        return index;
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && line[start] <= (byte)' ') start++;
        while (end > start && line[end - 1] <= (byte)' ') end--;
        return line[start..end];
    }

    private static float NextFloat(ref ReadOnlySpan<byte> text)
    {
        var start = 0;
        while (start < text.Length && text[start] <= (byte)' ') start++;
        var end = start;
        while (end < text.Length && text[end] > (byte)' ') end++;
        if (end == start || !Utf8Parser.TryParse(text[start..end], out float value, out var consumed) || consumed != end - start)
            throw new MeshImportException("The file is damaged: a coordinate could not be read.");
        text = text[end..];
        return value;
    }
}

public static class ObjReader
{
    /// <summary>
    /// Reads the vertices and faces of an OBJ file. Faces with more than three corners are split into triangles;
    /// all objects and groups of the file become one mesh. Normals, texture coordinates and materials are ignored.
    /// </summary>
    public static MeshData Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var data = MeshImport.ReadAll(stream);
        if (data.Length == 0) throw new MeshImportException("The file is empty.");

        var positions = new List<Vector3>();
        var triangles = new List<int>();
        var corner = new List<int>();
        ReadOnlySpan<byte> span = data;

        var position = 0;
        while (position < span.Length)
        {
            var end = span[position..].IndexOf((byte)'\n');
            var line = end < 0 ? span[position..] : span.Slice(position, end);
            position = end < 0 ? span.Length : position + end + 1;

            line = Trim(line);
            if (line.Length < 2 || line[0] == (byte)'#') continue;

            if (line[0] == (byte)'v' && line[1] <= (byte)' ')
            {
                var rest = line[1..];
                if (positions.Count >= MeshImportLimits.MaxVertices) throw new MeshImportException("The model has too many points to import.");
                var v = new Vector3(NextFloat(ref rest), NextFloat(ref rest), NextFloat(ref rest));
                if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                    throw new MeshImportException("The file is damaged: a coordinate is not a valid number.");
                positions.Add(v);
            }
            else if (line[0] == (byte)'f' && line[1] <= (byte)' ')
            {
                corner.Clear();
                var rest = line[1..];
                while (TryNextToken(ref rest, out var token))
                {
                    var slash = token.IndexOf((byte)'/');
                    var number = slash < 0 ? token : token[..slash];
                    if (!Utf8Parser.TryParse(number, out int index, out var consumed) || consumed != number.Length || index == 0)
                        throw new MeshImportException("The file is damaged: a face refers to an invalid point.");
                    var resolved = index > 0 ? index - 1 : positions.Count + index; // negative: counted from the end
                    if (resolved < 0 || resolved >= positions.Count)
                        throw new MeshImportException("The file is damaged: a face refers to a point that does not exist.");
                    corner.Add(resolved);
                }

                if (corner.Count < 3) continue; // lines and points are not surfaces
                for (var i = 1; i + 1 < corner.Count; i++)
                {
                    if (triangles.Count / 3 >= MeshImportLimits.MaxTriangles) throw new MeshImportException("The model has too many triangles to import.");
                    triangles.Add(corner[0]);
                    triangles.Add(corner[i]);
                    triangles.Add(corner[i + 1]);
                }
            }
        }

        if (triangles.Count == 0) throw new MeshImportException("The file contains no faces.");
        return WeldAndCompact(positions, triangles);
    }

    /// <summary>Merges points at the same position and drops points that no face uses (they would distort the bounds).</summary>
    private static MeshData WeldAndCompact(List<Vector3> positions, List<int> triangles)
    {
        var welded = new List<Vector3>();
        var map = new Dictionary<MeshImport.VertexKey, int>();
        var remap = new int[positions.Count];
        Array.Fill(remap, -1);

        var indices = new int[triangles.Count];
        for (var i = 0; i < triangles.Count; i++)
        {
            var old = triangles[i];
            if (remap[old] < 0)
            {
                var key = MeshImport.VertexKey.Of(positions[old]);
                if (!map.TryGetValue(key, out var index))
                {
                    index = welded.Count;
                    welded.Add(positions[old]);
                    map[key] = index;
                }

                remap[old] = index;
            }

            indices[i] = remap[old];
        }

        return new MeshData([.. welded], indices);
    }

    private static ReadOnlySpan<byte> Trim(ReadOnlySpan<byte> line)
    {
        var start = 0;
        var end = line.Length;
        while (start < end && line[start] <= (byte)' ') start++;
        while (end > start && line[end - 1] <= (byte)' ') end--;
        return line[start..end];
    }

    private static bool TryNextToken(ref ReadOnlySpan<byte> text, out ReadOnlySpan<byte> token)
    {
        var start = 0;
        while (start < text.Length && text[start] <= (byte)' ') start++;
        var end = start;
        while (end < text.Length && text[end] > (byte)' ') end++;
        token = text[start..end];
        text = text[end..];
        return end > start;
    }

    private static float NextFloat(ref ReadOnlySpan<byte> text)
    {
        if (!TryNextToken(ref text, out var token) || !Utf8Parser.TryParse(token, out float value, out var consumed) || consumed != token.Length)
            throw new MeshImportException("The file is damaged: a coordinate could not be read.");
        return value;
    }
}
