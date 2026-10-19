using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Xml;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>Limits that protect against hostile 3MF files (N-SEC-01/02). The defaults suit real files; tests pass small values.</summary>
public sealed class ThreeMfLimits
{
    /// <summary>Most entries a package may contain.</summary>
    public int MaxEntries { get; init; } = 5000;

    /// <summary>Largest model part, after decompression.</summary>
    public long MaxModelBytes { get; init; } = 1L << 30;

    /// <summary>All model parts together, after decompression.</summary>
    public long MaxTotalBytes { get; init; } = 2L << 30;

    /// <summary>Parts at least this large are checked against <see cref="MaxCompressionRatio"/>.</summary>
    public long RatioCheckFromBytes { get; init; } = 64L << 20;

    /// <summary>Real model files compress about 5 to 20 times; a zip bomb compresses 1000 times or more.</summary>
    public double MaxCompressionRatio { get; init; } = 150;

    /// <summary>Triangles after all component references are expanded.</summary>
    public int MaxTriangles { get; init; } = MeshImportLimits.MaxTriangles;

    public int MaxVertices { get; init; } = MeshImportLimits.MaxVertices;

    /// <summary>How deep components may reference components.</summary>
    public int MaxDepth { get; init; } = 32;
}

/// <summary>One printable object of a 3MF file: world coordinates in millimetres, welded, with the color of its material if it has one.</summary>
public sealed record ThreeMfItem(string Name, MeshData Mesh, uint? ColorArgb);

/// <summary>
/// Reads 3MF files (F-IMP-01) as untrusted input: the package is never extracted to disk, XML is read without DTDs or external
/// entities (N-SEC-03), sizes, entry names and reference chains are limited (N-SEC-02). Supports meshes, components with transforms,
/// the production extension's <c>path</c> references to other model parts (as Bambu Studio and OrcaSlicer write them), units,
/// build item transforms and object colors from base materials.
/// </summary>
public static class ThreeMfReader
{
    public const string Extension = ".3mf";

    private const string ProductionNamespace = "http://schemas.microsoft.com/3dmanufacturing/production/2015/06";
    private const string DefaultModelPath = "3D/3dmodel.model";
    private const string RelationshipsPath = "_rels/.rels";

    /// <exception cref="MeshImportException">The file is not a usable 3MF file. The message is meant for the user.</exception>
    public static IReadOnlyList<ThreeMfItem> Read(Stream stream, ThreeMfLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return new Session(stream, limits ?? new ThreeMfLimits()).Run();
        }
        catch (MeshImportException)
        {
            throw;
        }
        catch (XmlException ex)
        {
            throw new MeshImportException("The 3MF file is damaged or contains XML that is not allowed (for example a DOCTYPE).", ex);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or OverflowException
                                       or FormatException or InvalidOperationException or NotSupportedException)
        {
            throw new MeshImportException("This is not a valid 3MF file.", ex);
        }
    }

    private sealed record ComponentRef(int ObjectId, string? Path, Matrix4x4 Transform);

    private sealed class ObjectDef(int id, string type, string name, int pid, int pindex)
    {
        public int Id { get; } = id;

        public string Type { get; } = type;

        public string Name { get; } = name;

        public int Pid { get; } = pid;

        public int Pindex { get; } = pindex;

        public Vector3[]? Vertices { get; set; }

        public int[]? Indices { get; set; }

        public List<ComponentRef> Components { get; } = [];
    }

    private sealed class ModelPart(string path)
    {
        public string Path { get; } = path;

        public Dictionary<int, ObjectDef> Objects { get; } = [];

        public Dictionary<int, List<uint?>> Materials { get; } = [];

        public List<ComponentRef> Build { get; } = [];

        public double UnitScale { get; set; } = 1;
    }

    private sealed class Session(Stream stream, ThreeMfLimits limits)
    {
        private readonly Dictionary<string, ZipArchiveEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModelPart> _parts = new(StringComparer.OrdinalIgnoreCase);
        private long _totalBytes;
        private long _expandedTriangles;
        private long _vertexCount;

        public IReadOnlyList<ThreeMfItem> Run()
        {
            using var zip = OpenPackage();
            var main = LoadPart(FindModelPath(), isMain: true);
            var scale = Matrix4x4.CreateScale((float)main.UnitScale);

            var roots = main.Build.Count > 0 ? main.Build : TopLevelObjects(main);
            var items = new List<ThreeMfItem>();
            foreach (var root in roots)
            {
                var part = root.Path is null ? main : LoadPart(NormalizePartPath(root.Path), isMain: false);
                if (!part.Objects.TryGetValue(root.ObjectId, out var def))
                    throw new MeshImportException($"The 3MF file builds an object (id {root.ObjectId}) that does not exist.");
                if (def.Type is not ("model" or "solidsupport")) continue; // support structures and other helper objects are not model geometry

                var builder = new MeshBuilder();
                Emit(part, def, root.Transform, scale, builder, depth: 0, stack: []);
                if (builder.TriangleCount == 0) continue;
                items.Add(new ThreeMfItem(def.Name, builder.ToMesh(), builder.Color));
            }

            if (items.Count == 0) throw new MeshImportException("The 3MF file contains nothing to import.");
            return items;
        }

        // ---- package ------------------------------------------------------------------------------

        private ZipArchive OpenPackage()
        {
            var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true); // InvalidDataException when it is no ZIP file
            try
            {
                var count = 0;
                foreach (var entry in zip.Entries)
                {
                    if (++count > limits.MaxEntries) throw new MeshImportException("The 3MF file contains too many entries.");
                    if (IsUnsafeName(entry.FullName))
                        throw new MeshImportException("The 3MF file contains an unsafe entry name and was not opened.");
                    _entries.TryAdd(entry.FullName, entry);
                }

                return zip;
            }
            catch
            {
                zip.Dispose();
                throw;
            }
        }

        /// <summary>Absolute paths, drive letters, UNC paths and ".." never occur in a legitimate package.</summary>
        private static bool IsUnsafeName(string name)
        {
            if (name.Length == 0) return true;
            if (name[0] is '/' or '\\' || name.Contains(':')) return true;
            return name.Split('/', '\\').Any(segment => segment == "..");
        }

        /// <summary>The part name for a relationship target or component path, or null when it points outside the package.</summary>
        private static string? TryNormalizePartPath(string? target)
        {
            if (string.IsNullOrWhiteSpace(target)) return null;
            var path = target.Trim();
            if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.Contains(':')) return null;
            path = path.Replace('\\', '/').TrimStart('/');
            if (path.Length == 0 || path.Split('/').Any(segment => segment == "..")) return null;
            return path;
        }

        private static string NormalizePartPath(string target) =>
            TryNormalizePartPath(target) ?? throw new MeshImportException($"The 3MF file refers to a part outside the package ('{target}').");

        private string FindModelPath()
        {
            if (_entries.TryGetValue(RelationshipsPath, out var rels))
            {
                using var raw = OpenEntry(rels, maxBytes: 1 << 20);
                using var reader = XmlReader.Create(raw, ReaderSettings(1 << 20));
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "Relationship") continue;
                    var type = reader.GetAttribute("Type") ?? "";
                    if (!type.EndsWith("/3dmodel", StringComparison.OrdinalIgnoreCase)) continue;
                    var target = TryNormalizePartPath(reader.GetAttribute("Target"));
                    if (target is not null && _entries.ContainsKey(target)) return target;
                }
            }

            if (_entries.ContainsKey(DefaultModelPath)) return DefaultModelPath;
            throw new MeshImportException("The 3MF file contains no 3D model.");
        }

        private Stream OpenEntry(ZipArchiveEntry entry, long maxBytes)
        {
            if (entry.Length > maxBytes) throw new MeshImportException("A part of the 3MF file is too large to import.");
            if (entry.Length >= limits.RatioCheckFromBytes
                && (entry.CompressedLength == 0 || (double)entry.Length / entry.CompressedLength > limits.MaxCompressionRatio))
                throw new MeshImportException("The 3MF file is compressed suspiciously strongly and was not opened.");

            _totalBytes += entry.Length;
            if (_totalBytes > limits.MaxTotalBytes) throw new MeshImportException("The 3MF file is too large to import.");

            // The sizes in the header are only claims: the bytes that really come out are counted as well.
            return new CappedStream(entry.Open(), Math.Min(maxBytes, Math.Max(entry.Length, 0)) + 1024);
        }

        private static XmlReaderSettings ReaderSettings(long maxCharacters) => new()
        {
            DtdProcessing = DtdProcessing.Prohibit, // no DOCTYPE at all: no external entities, no entity bombs (N-SEC-03)
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = maxCharacters,
            MaxCharactersFromEntities = 0,
            CloseInput = false,
        };

        // ---- model parts --------------------------------------------------------------------------

        private ModelPart LoadPart(string path, bool isMain)
        {
            if (_parts.TryGetValue(path, out var cached)) return cached;
            if (!_entries.TryGetValue(path, out var entry)) throw new MeshImportException($"The 3MF file refers to a missing part ('{path}').");

            var part = new ModelPart(path);
            _parts[path] = part;
            using var raw = OpenEntry(entry, limits.MaxModelBytes);
            using var reader = XmlReader.Create(raw, ReaderSettings(limits.MaxModelBytes));
            ParseModel(reader, part, isMain);
            return part;
        }

        private void ParseModel(XmlReader reader, ModelPart part, bool isMain)
        {
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "model") throw new MeshImportException("The 3MF file contains no valid model.");
            if (isMain) part.UnitScale = UnitScale(reader.GetAttribute("unit"));

            ObjectDef? current = null;
            List<Vector3>? vertices = null;
            List<int>? indices = null;
            List<uint?>? materials = null;

            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    switch (reader.LocalName)
                    {
                        case "object" when current is not null:
                            FinishObject(part, current, vertices, indices);
                            current = null;
                            break;
                        case "basematerials":
                            materials = null;
                            break;
                    }

                    continue;
                }

                if (reader.NodeType != XmlNodeType.Element) continue;
                switch (reader.LocalName)
                {
                    case "basematerials":
                        materials = [];
                        part.Materials[RequiredInt(reader, "id")] = materials;
                        break;
                    case "base" when materials is not null:
                        materials.Add(ParseColor(reader.GetAttribute("displaycolor")));
                        break;
                    case "object":
                        current = new ObjectDef(
                            RequiredInt(reader, "id"),
                            (reader.GetAttribute("type") ?? "model").ToLowerInvariant(),
                            (reader.GetAttribute("name") ?? "").Trim(),
                            OptionalInt(reader, "pid"),
                            OptionalInt(reader, "pindex"));
                        vertices = null;
                        indices = null;
                        if (reader.IsEmptyElement)
                        {
                            FinishObject(part, current, vertices, indices);
                            current = null;
                        }

                        break;
                    case "vertices" when current is not null:
                        vertices = [];
                        break;
                    case "vertex" when current is not null && vertices is not null:
                        if (++_vertexCount > limits.MaxVertices) throw new MeshImportException("The 3MF file contains too many vertices.");
                        vertices.Add(new Vector3(Coordinate(reader, "x"), Coordinate(reader, "y"), Coordinate(reader, "z")));
                        break;
                    case "triangles" when current is not null:
                        indices = [];
                        break;
                    case "triangle" when current is not null && indices is not null:
                        if (indices.Count / 3 >= limits.MaxTriangles) throw new MeshImportException("The 3MF file contains too many triangles.");
                        indices.Add(RequiredInt(reader, "v1"));
                        indices.Add(RequiredInt(reader, "v2"));
                        indices.Add(RequiredInt(reader, "v3"));
                        break;
                    case "component" when current is not null:
                        current.Components.Add(new ComponentRef(
                            RequiredInt(reader, "objectid"), reader.GetAttribute("path", ProductionNamespace), ParseTransform(reader.GetAttribute("transform"))));
                        break;
                    case "item" when isMain:
                        part.Build.Add(new ComponentRef(
                            RequiredInt(reader, "objectid"), reader.GetAttribute("path", ProductionNamespace), ParseTransform(reader.GetAttribute("transform"))));
                        break;
                }
            }
        }

        private static void FinishObject(ModelPart part, ObjectDef def, List<Vector3>? vertices, List<int>? indices)
        {
            if (vertices is not null && indices is not null)
            {
                foreach (var index in indices)
                {
                    if (index < 0 || index >= vertices.Count) throw new MeshImportException("The 3MF file contains a triangle that refers to a missing vertex.");
                }

                def.Vertices = [.. vertices];
                def.Indices = [.. indices];
            }

            if (!part.Objects.TryAdd(def.Id, def)) throw new MeshImportException($"The 3MF file contains the object id {def.Id} twice.");
        }

        private static double UnitScale(string? unit) => (unit ?? "millimeter").Trim().ToLowerInvariant() switch
        {
            "micron" => 0.001,
            "millimeter" => 1,
            "centimeter" => 10,
            "inch" => 25.4,
            "foot" => 304.8,
            "meter" => 1000,
            var other => throw new MeshImportException($"The 3MF file uses an unknown unit ('{other}')."),
        };

        // ---- attribute parsing --------------------------------------------------------------------

        private static int RequiredInt(XmlReader reader, string name)
        {
            if (!int.TryParse(reader.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                throw new MeshImportException($"The 3MF file has a missing or invalid number ('{name}').");
            return value;
        }

        private static int OptionalInt(XmlReader reader, string name) =>
            int.TryParse(reader.GetAttribute(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;

        private static float Coordinate(XmlReader reader, string name)
        {
            if (!float.TryParse(reader.GetAttribute(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
                throw new MeshImportException("The 3MF file contains a coordinate that is not a number.");
            return value;
        }

        private static Matrix4x4 ParseTransform(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Matrix4x4.Identity;
            var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var m = new float[12];
            if (parts.Length != 12) throw new MeshImportException("The 3MF file contains a transform that does not have twelve numbers.");
            for (var i = 0; i < 12; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out m[i]) || !float.IsFinite(m[i]))
                    throw new MeshImportException("The 3MF file contains a transform with a value that is not a number.");
            }

            return new Matrix4x4(m[0], m[1], m[2], 0, m[3], m[4], m[5], 0, m[6], m[7], m[8], 0, m[9], m[10], m[11], 1);
        }

        /// <summary>"#RRGGBB" or "#RRGGBBAA" to ARGB; null when it cannot be read.</summary>
        private static uint? ParseColor(string? text)
        {
            if (text is null || text.Length is not (7 or 9) || text[0] != '#') return null;
            if (!uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return null;
            return text.Length == 7 ? 0xFF000000u | value : (value << 24) | (value >> 8);
        }

        // ---- flattening ---------------------------------------------------------------------------

        private static List<ComponentRef> TopLevelObjects(ModelPart main)
        {
            var referenced = main.Objects.Values.SelectMany(o => o.Components).Where(c => c.Path is null).Select(c => c.ObjectId).ToHashSet();
            return [.. main.Objects.Values.Where(o => !referenced.Contains(o.Id)).Select(o => new ComponentRef(o.Id, null, Matrix4x4.Identity))];
        }

        private void Emit(ModelPart part, ObjectDef def, Matrix4x4 transform, Matrix4x4 unit, MeshBuilder builder, int depth, HashSet<(string, int)> stack)
        {
            if (depth > limits.MaxDepth) throw new MeshImportException("The 3MF file nests objects too deeply.");
            if (!stack.Add((part.Path, def.Id))) throw new MeshImportException("The 3MF file contains a reference loop between its objects.");

            if (def.Vertices is not null && def.Indices is not null)
            {
                _expandedTriangles += def.Indices.Length / 3;
                if (_expandedTriangles > limits.MaxTriangles) throw new MeshImportException("The 3MF file expands to too many triangles.");
                var full = transform * unit;
                builder.Color ??= ColorOf(part, def);
                builder.Add(def.Vertices, def.Indices, full, mirrored: Determinant3(full) < 0);
            }

            foreach (var component in def.Components)
            {
                var target = component.Path is null ? part : LoadPart(NormalizePartPath(component.Path), isMain: false);
                if (!target.Objects.TryGetValue(component.ObjectId, out var child))
                    throw new MeshImportException($"The 3MF file refers to an object (id {component.ObjectId}) that does not exist.");
                Emit(target, child, component.Transform * transform, unit, builder, depth + 1, stack);
            }

            stack.Remove((part.Path, def.Id));
        }

        private static uint? ColorOf(ModelPart part, ObjectDef def) =>
            def.Pid >= 0 && def.Pindex >= 0 && part.Materials.TryGetValue(def.Pid, out var list) && def.Pindex < list.Count ? list[def.Pindex] : null;

        private static float Determinant3(Matrix4x4 m) =>
            m.M11 * (m.M22 * m.M33 - m.M23 * m.M32) - m.M12 * (m.M21 * m.M33 - m.M23 * m.M31) + m.M13 * (m.M21 * m.M32 - m.M22 * m.M31);
    }

    /// <summary>Collects the transformed pieces of one object into one welded mesh.</summary>
    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _vertices = [];
        private readonly List<int> _indices = [];
        private readonly Dictionary<MeshImport.VertexKey, int> _map = [];

        public uint? Color { get; set; }

        public int TriangleCount => _indices.Count / 3;

        public void Add(Vector3[] vertices, int[] indices, Matrix4x4 transform, bool mirrored)
        {
            var mapped = new int[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var v = Vector3.Transform(vertices[i], transform);
                var key = MeshImport.VertexKey.Of(v);
                if (!_map.TryGetValue(key, out var index))
                {
                    index = _vertices.Count;
                    _vertices.Add(v);
                    _map[key] = index;
                }

                mapped[i] = index;
            }

            for (var t = 0; t + 2 < indices.Length; t += 3)
            {
                int a = mapped[indices[t]], b = mapped[indices[t + 1]], c = mapped[indices[t + 2]];
                if (a == b || b == c || a == c) continue; // degenerate after welding
                if (mirrored) (b, c) = (c, b); // a mirroring transform turns the triangles inside out
                _indices.Add(a);
                _indices.Add(b);
                _indices.Add(c);
            }
        }

        public MeshData ToMesh() => new([.. _vertices], [.. _indices]);
    }

    /// <summary>Hands out at most a fixed number of bytes: the real size is checked even if the header lied.</summary>
    private sealed class CappedStream(Stream inner, long cap) : Stream
    {
        private long _read;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            _read += read;
            if (_read > cap) throw new MeshImportException("A part of the 3MF file is larger than its header says and was not read further.");
            return read;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
