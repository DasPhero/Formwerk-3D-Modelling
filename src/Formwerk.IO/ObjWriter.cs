using System.Globalization;
using System.Text;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>One named mesh for an OBJ file.</summary>
public sealed record ObjObject(string Name, MeshData Mesh);

/// <summary>
/// Writes Wavefront OBJ (E-03): one <c>o</c> block per object with its points and triangles. Coordinates are millimetres, Z up, as
/// in the other formats. No materials or normals (colors are a 3MF feature).
/// </summary>
public static class ObjWriter
{
    public const string Extension = ".obj";

    /// <summary>Plain UTF-8 without a byte order mark: some importers read the mark as part of the first word.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes to a temporary file and renames it, so a failed export never destroys an existing file (N-ROB-02).</summary>
    public static void SaveAtomic(string path, IEnumerable<ObjObject> objects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(objects);
        ProjectFile.SaveAtomic(path, stream => Write(stream, objects));
    }

    public static void Write(Stream stream, IEnumerable<ObjObject> objects, string generator = "Formwerk")
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(objects);

        using var writer = new StreamWriter(stream, Utf8, bufferSize: 1 << 16, leaveOpen: true) { NewLine = "\n" };
        writer.WriteLine($"# {generator}");
        writer.WriteLine("# units: millimetres, Z up");

        var offset = 0; // points written so far; face indices count from 1 across the whole file
        foreach (var obj in objects)
        {
            var mesh = obj.Mesh;
            if (mesh.IsEmpty) continue;

            writer.WriteLine($"o {CleanName(obj.Name)}");
            foreach (var v in mesh.Vertices) writer.WriteLine($"v {Number(v.X)} {Number(v.Y)} {Number(v.Z)}");

            var indices = mesh.Indices;
            for (var t = 0; t < indices.Length; t += 3)
                writer.WriteLine($"f {indices[t] + 1 + offset} {indices[t + 1] + 1 + offset} {indices[t + 2] + 1 + offset}");
            offset += mesh.VertexCount;
        }
    }

    /// <summary>A name that stays on its line: control characters become spaces, the ends are trimmed; nothing left gives "Object".</summary>
    private static string CleanName(string name)
    {
        var cleaned = new StringBuilder(name.Length);
        foreach (var c in name) cleaned.Append(char.IsControl(c) ? ' ' : c);
        var text = cleaned.ToString().Trim();
        return text.Length == 0 ? "Object" : text;
    }

    /// <summary>A number with the dot, up to six decimals (a millionth of a millimetre) and no exponent; never "-0".</summary>
    private static string Number(float value)
    {
        var text = value.ToString("0.######", CultureInfo.InvariantCulture);
        return text == "-0" ? "0" : text;
    }
}
