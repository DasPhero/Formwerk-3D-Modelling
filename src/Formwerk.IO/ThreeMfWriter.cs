using System.Globalization;
using System.IO.Compression;
using System.Text;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>One object of a 3MF export: its name, its mesh in world coordinates (mm) and its color (ARGB).</summary>
public sealed record ThreeMfObject(string Name, MeshData Mesh, uint ColorArgb);

/// <summary>
/// Writes 3MF files (E-03): a ZIP package with the core model part, one object per mesh with name and color, in millimetres.
/// Only what slicers need is written; the format follows the 3MF core specification 1.x.
/// </summary>
public static class ThreeMfWriter
{
    public const string Extension = ".3mf";

    private const int MaxNameLength = 200;
    private const string ModelPath = "3D/3dmodel.model";
    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <exception cref="ArgumentException">There is no object with geometry.</exception>
    /// <exception cref="InvalidOperationException">A mesh contains coordinates that are not numbers.</exception>
    public static void Write(Stream stream, IReadOnlyList<ThreeMfObject> objects)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var parts = Validate(objects);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        WriteEntry(zip, "[Content_Types].xml", writer => writer.Write(
            """<?xml version="1.0" encoding="UTF-8"?>""" + "\n" +
            """<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">""" +
            """<Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>""" +
            """<Default Extension="model" ContentType="application/vnd.ms-package.3dmanufacturing-3dmodel+xml"/>""" +
            "</Types>\n"));
        WriteEntry(zip, "_rels/.rels", writer => writer.Write(
            """<?xml version="1.0" encoding="UTF-8"?>""" + "\n" +
            """<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">""" +
            "<Relationship Target=\"/" + ModelPath + "\" Id=\"rel0\" Type=\"http://schemas.microsoft.com/3dmanufacturing/2013/01/3dmodel\"/>" +
            "</Relationships>\n"));
        WriteEntry(zip, ModelPath, writer => WriteModel(writer, parts));
    }

    /// <summary>Writes to a temporary file and renames it, so a failed export never destroys an existing file (N-ROB-02).</summary>
    public static void SaveAtomic(string path, IReadOnlyList<ThreeMfObject> objects)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Validate(objects); // before anything is touched on disk
        ProjectFile.SaveAtomic(path, stream => Write(stream, objects));
    }

    private static List<ThreeMfObject> Validate(IReadOnlyList<ThreeMfObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        var parts = objects.Where(o => o.Mesh is { IsEmpty: false }).ToList();
        if (parts.Count == 0) throw new ArgumentException("There is nothing to export.", nameof(objects));
        return parts;
    }

    private static void WriteEntry(ZipArchive zip, string name, Action<StreamWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, Utf8, bufferSize: 1 << 16) { NewLine = "\n" };
        write(writer);
    }

    private static void WriteModel(StreamWriter w, List<ThreeMfObject> parts)
    {
        // Equal colors share one base material; resource ids: 1 = materials, objects start at 2.
        var colors = new List<uint>();
        var colorIndex = new Dictionary<uint, int>();
        foreach (var part in parts)
        {
            if (colorIndex.ContainsKey(part.ColorArgb)) continue;
            colorIndex[part.ColorArgb] = colors.Count;
            colors.Add(part.ColorArgb);
        }

        w.Write("""<?xml version="1.0" encoding="UTF-8"?>""");
        w.Write('\n');
        w.Write("""<model unit="millimeter" xml:lang="en-US" xmlns="http://schemas.microsoft.com/3dmanufacturing/core/2015/02">""");
        w.Write("""<metadata name="Application">Formwerk</metadata>""");
        w.Write("<resources>");

        w.Write("""<basematerials id="1">""");
        for (var i = 0; i < colors.Count; i++)
        {
            var c = colors[i];
            w.Write($"""<base name="Color {i + 1}" displaycolor="#{(c >> 16) & 0xFF:X2}{(c >> 8) & 0xFF:X2}{c & 0xFF:X2}{c >> 24:X2}"/>""");
        }

        w.Write("</basematerials>");

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            var id = i + 2;
            w.Write($"""<object id="{id}" type="model" pid="1" pindex="{colorIndex[part.ColorArgb]}" name="{SafeName(part.Name, i + 1)}">""");
            WriteMesh(w, part.Mesh, part.Name);
            w.Write("</object>");
        }

        w.Write("</resources><build>");
        for (var i = 0; i < parts.Count; i++) w.Write($"""<item objectid="{i + 2}"/>""");
        w.Write("</build></model>\n");
    }

    private static void WriteMesh(StreamWriter w, MeshData mesh, string name)
    {
        Span<char> buffer = stackalloc char[32];
        w.Write("<mesh><vertices>");
        foreach (var v in mesh.Vertices)
        {
            if (!float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z))
                throw new InvalidOperationException($"The object '{name}' contains coordinates that are not numbers and cannot be exported.");

            w.Write("<vertex x=\"");
            WriteNumber(w, v.X, buffer);
            w.Write("\" y=\"");
            WriteNumber(w, v.Y, buffer);
            w.Write("\" z=\"");
            WriteNumber(w, v.Z, buffer);
            w.Write("\"/>");
        }

        w.Write("</vertices><triangles>");
        var indices = mesh.Indices;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            w.Write("<triangle v1=\"");
            WriteInt(w, indices[i], buffer);
            w.Write("\" v2=\"");
            WriteInt(w, indices[i + 1], buffer);
            w.Write("\" v3=\"");
            WriteInt(w, indices[i + 2], buffer);
            w.Write("\"/>");
        }

        w.Write("</triangles></mesh>");
    }

    /// <summary>Shortest exact text of the float, always with a dot; values that would need an exponent are written fixed-point (up to 6 decimals).</summary>
    private static void WriteNumber(StreamWriter w, float value, Span<char> buffer)
    {
        value.TryFormat(buffer, out var length, default, CultureInfo.InvariantCulture);
        if (buffer[..length].IndexOfAny('E', 'e') >= 0) value.TryFormat(buffer, out length, "0.######", CultureInfo.InvariantCulture);
        ReadOnlySpan<char> text = buffer[..length];
        if (text.SequenceEqual("-0")) text = "0"; // a tiny negative value rounded to zero
        w.Write(text);
    }

    private static void WriteInt(StreamWriter w, int value, Span<char> buffer)
    {
        value.TryFormat(buffer, out var length, default, CultureInfo.InvariantCulture);
        w.Write(buffer[..length]);
    }

    /// <summary>XML-safe attribute text: control characters removed, special characters escaped, shortened, never empty.</summary>
    private static string SafeName(string? name, int number)
    {
        var builder = new StringBuilder();
        var text = (name ?? "").Trim();
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                if (builder.Length + 2 > MaxNameLength) break;
                builder.Append(c).Append(text[++i]);
                continue;
            }

            if (c < ' ' || char.IsSurrogate(c) || c is '￾' or '￿') continue;
            if (builder.Length + 1 > MaxNameLength) break;
            builder.Append(c);
        }

        var clean = builder.ToString().Trim();
        if (clean.Length == 0) clean = $"Object {number}";

        return clean.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&apos;");
    }
}
