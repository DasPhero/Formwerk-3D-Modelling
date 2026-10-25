using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>The file cannot be read as a Formwerk project. The message is meant to be shown to the user.</summary>
public sealed class ProjectFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// Reads and writes <c>.fwp</c> project files (spec section 6, schema in <c>docs/project-format.md</c>):
/// a ZIP container with <c>document.json</c>. Writing is deterministic (same document, same bytes) and atomic.
/// Reading treats the file as untrusted (N-SEC-01/02).
/// </summary>
public static class ProjectFile
{
    public const int CurrentSchemaVersion = 1;
    public const string DocumentEntryName = "document.json";
    public const string Extension = ".fwp";

    /// <summary>Hard limits that protect against damaged or malicious files.</summary>
    public static class Limits
    {
        public const long MaxFileBytes = 256L * 1024 * 1024;
        /// <summary>Largest <c>document.json</c> (and any other part that is not a mesh).</summary>
        public const long MaxDocumentBytes = 64L * 1024 * 1024;

        /// <summary>Largest single mesh part (20 million triangles need about 360 MB).</summary>
        public const long MaxMeshEntryBytes = 400L * 1024 * 1024;

        /// <summary>Largest total of all parts after unpacking.</summary>
        public const long MaxUncompressedBytes = 1024L * 1024 * 1024;
        public const int MaxEntries = 4096;
        public const int MaxCompressionRatio = 200;
        public const int MaxNodes = 100_000;
        public const int MaxDepth = 64;
        public const int MaxVariables = 1000;
        public const int MaxParametersPerNode = 64;
        public const int MaxTextLength = 2000;
    }

    private static readonly DateTimeOffset FixedTime = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // ---- writing -------------------------------------------------------------------------------

    /// <param name="meshes">Where the meshes of imported objects come from; they are written as <c>meshes/&lt;id&gt;.bin</c>. Null writes none.</param>
    public static void Write(Stream stream, FormwerkDocument document, MeshStore? meshes = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var entry = zip.CreateEntry(DocumentEntryName, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTime;
        using (var entryStream = entry.Open()) WriteJson(entryStream, document);

        if (meshes is null) return;

        // Only meshes that an object refers to are saved, in a fixed order, so that the same document gives the same bytes.
        foreach (var node in document.Root.Descendants().OfType<MeshNode>().DistinctBy(n => n.MeshId).OrderBy(n => n.MeshId))
        {
            if (!meshes.TryGet(node.MeshId, out var mesh))
                throw new InvalidOperationException($"The object '{node.Name}' has no mesh data and cannot be saved.");

            var meshEntry = zip.CreateEntry(MeshEntryName(node.MeshId), CompressionLevel.Optimal);
            meshEntry.LastWriteTime = FixedTime;
            using var meshStream = meshEntry.Open();
            MeshFormat.Write(meshStream, mesh);
        }
    }

    internal static string MeshEntryName(Guid id) => $"meshes/{id:D}.bin";

    /// <summary>Writes to a temporary file and renames it, so a crash never destroys an existing project (N-ROB-02).</summary>
    public static void SaveAtomic(string path, FormwerkDocument document, MeshStore? meshes = null) =>
        SaveAtomic(path, stream => Write(stream, document, meshes));

    public static void SaveAtomic(string path, Action<Stream> writer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writer(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void WriteJson(Stream stream, FormwerkDocument document)
    {
        var options = new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using var json = new Utf8JsonWriter(stream, options);

        json.WriteStartObject();
        json.WriteNumber("schemaVersion", CurrentSchemaVersion);
        json.WriteString("application", "Formwerk");

        json.WriteStartObject("settings");
        WriteVector(json, "buildVolume", document.Settings.BuildVolume);
        json.WriteNumber("nozzleDiameter", document.Settings.NozzleDiameter);
        json.WriteNumber("tolerance", document.Settings.Tolerance);
        json.WriteEndObject();

        // An array, not an object: the order matters when variables refer to earlier ones.
        json.WriteStartArray("variables");
        foreach (var (name, expression) in document.Variables)
        {
            json.WriteStartObject();
            json.WriteString("name", name);
            json.WriteString("expression", expression);
            json.WriteEndObject();
        }

        json.WriteEndArray();

        json.WriteStartArray("nodes");
        foreach (var node in document.Root.Children) WriteNode(json, node);
        json.WriteEndArray();

        json.WriteEndObject();
    }

    private static void WriteNode(Utf8JsonWriter json, Node node)
    {
        json.WriteStartObject();
        json.WriteString("type", TypeName(node));
        json.WriteString("id", node.Id);
        json.WriteString("name", node.Name);
        json.WriteString("role", node.Role == NodeRole.Hole ? "hole" : "solid");
        json.WriteString("color", "#" + node.Color.ToString("X8", CultureInfo.InvariantCulture));
        json.WriteBoolean("visible", node.Visible);
        json.WriteBoolean("locked", node.Locked);
        WriteVector(json, "position", node.Transform.Position);
        json.WriteStartArray("rotation");
        json.WriteNumberValue(node.Transform.Rotation.X);
        json.WriteNumberValue(node.Transform.Rotation.Y);
        json.WriteNumberValue(node.Transform.Rotation.Z);
        json.WriteNumberValue(node.Transform.Rotation.W);
        json.WriteEndArray();
        WriteVector(json, "scale", node.Transform.Scale);
        if (node.Element is { } element)
        {
            json.WriteStartObject("element");
            json.WriteString("id", element.ElementId);
            json.WriteStartObject("values");
            foreach (var (name, expression) in element.Values.OrderBy(v => v.Key, StringComparer.Ordinal)) json.WriteString(name, expression);
            json.WriteEndObject();
            json.WriteEndObject();
        }

        switch (node)
        {
            case PrimitiveNode primitive:
                json.WriteString("generator", primitive.GeneratorId);
                if (primitive.GrowAnchor != PrimitiveNode.DefaultGrowAnchor) WriteVector(json, "growAnchor", primitive.GrowAnchor);
                WriteParameters(json, primitive);
                break;
            case PatternNode pattern:
                json.WriteString("kind", pattern.Kind.ToString());
                WriteParameters(json, pattern);
                json.WriteStartArray("skipped");
                foreach (var number in pattern.Skipped) json.WriteNumberValue(number);
                json.WriteEndArray();
                break;
            case MirrorNode mirror:
                WriteParameters(json, mirror);
                break;
            case MeshNode mesh:
                json.WriteString("meshId", mesh.MeshId);
                break;
            case BooleanNode boolean:
                json.WriteString("operation", boolean.Operation.ToString());
                break;
        }

        if (node.CanHaveChildren)
        {
            json.WriteStartArray("children");
            foreach (var child in node.Children) WriteNode(json, child);
            json.WriteEndArray();
        }

        json.WriteEndObject();
    }

    private static void WriteParameters(Utf8JsonWriter json, ParametricNode node)
    {
        json.WriteStartObject("parameters");
        foreach (var (key, value) in node.Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) json.WriteString(key, value);
        json.WriteEndObject();
    }

    private static void WriteVector(Utf8JsonWriter json, string name, Vector3 v)
    {
        json.WriteStartArray(name);
        json.WriteNumberValue(v.X);
        json.WriteNumberValue(v.Y);
        json.WriteNumberValue(v.Z);
        json.WriteEndArray();
    }

    private static string TypeName(Node node) => node switch
    {
        PrimitiveNode => "primitive",
        MeshNode => "mesh",
        GroupNode => "group",
        BooleanNode => "boolean",
        MergeNode => "merge",
        PatternNode => "pattern",
        MirrorNode => "mirror",
        _ => throw new NotSupportedException($"Node type {node.GetType().Name} cannot be saved."),
    };

    // ---- reading -------------------------------------------------------------------------------

    /// <param name="meshes">Receives the meshes of imported objects; null ignores them.</param>
    public static FormwerkDocument Load(string path, MeshStore? meshes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new ProjectFormatException("The file does not exist.");
            if (info.Length > Limits.MaxFileBytes) throw new ProjectFormatException("The file is too large to be a Formwerk project.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Read(stream, meshes);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ProjectFormatException("Formwerk is not allowed to read this file.", ex);
        }
        catch (IOException ex)
        {
            throw new ProjectFormatException($"The file could not be read: {ex.Message}", ex);
        }
    }

    /// <param name="meshes">Receives the meshes of imported objects; null ignores them (the objects then have no geometry).</param>
    public static FormwerkDocument Read(Stream stream, MeshStore? meshes = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        try
        {
            return ReadCore(stream, meshes);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            // The ZIP directory is read lazily, so a damaged container can fail at any point.
            throw new ProjectFormatException("This is not a valid Formwerk project (the container is damaged).", ex);
        }
    }

    private static FormwerkDocument ReadCore(Stream stream, MeshStore? meshes)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        ValidateEntries(zip);

        var documentEntry = zip.GetEntry(DocumentEntryName)
                            ?? throw new ProjectFormatException("This is not a Formwerk project (document.json is missing).");
        byte[] jsonBytes;
        using (var entryStream = documentEntry.Open()) jsonBytes = ReadBounded(entryStream, Limits.MaxDocumentBytes);

        var document = ParseDocument(jsonBytes);
        if (meshes is not null) LoadMeshes(zip, document, meshes);
        return document;
    }

    private static FormwerkDocument ParseDocument(byte[] jsonBytes)
    {
        JsonObject root;
        try
        {
            var parsed = JsonNode.Parse(jsonBytes, nodeOptions: null,
                documentOptions: new JsonDocumentOptions { MaxDepth = 4 * Limits.MaxDepth + 16, CommentHandling = JsonCommentHandling.Disallow });
            root = parsed as JsonObject ?? throw new ProjectFormatException("The file is damaged: the project data has the wrong shape.");
        }
        catch (JsonException ex)
        {
            throw new ProjectFormatException("The file is damaged: the project data cannot be read.", ex);
        }

        try
        {
            root = ProjectMigrations.ToCurrent(root);
            return BuildDocument(root);
        }
        catch (ProjectFormatException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException or JsonException or ArgumentException or KeyNotFoundException)
        {
            throw new ProjectFormatException("The file is damaged: a value has an unexpected type or is out of range.", ex);
        }
    }

    private static void ValidateEntries(ZipArchive zip)
    {
        if (zip.Entries.Count > Limits.MaxEntries) throw new ProjectFormatException("The file is damaged: it contains too many parts.");

        long total = 0;
        foreach (var entry in zip.Entries)
        {
            // Zip-slip: reject paths that could leave the target folder, even though nothing is extracted to disk.
            var name = entry.FullName;
            if (name.StartsWith('/') || name.StartsWith('\\') || name.Contains(':') || name.Split('/', '\\').Contains(".."))
                throw new ProjectFormatException("The file is unsafe: it contains an invalid path.");

            var isMesh = name.StartsWith("meshes/", StringComparison.Ordinal);
            total += entry.Length;
            if (entry.Length > (isMesh ? Limits.MaxMeshEntryBytes : Limits.MaxDocumentBytes) || total > Limits.MaxUncompressedBytes)
                throw new ProjectFormatException("The file is unsafe: its content is unreasonably large.");
            if (entry.Length > 1024 * 1024 && entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > Limits.MaxCompressionRatio)
                throw new ProjectFormatException("The file is unsafe: its compression ratio is implausible.");
        }
    }

    /// <summary>Loads the mesh of every imported object. A missing or damaged mesh makes the file unusable (no silent data loss).</summary>
    private static void LoadMeshes(ZipArchive zip, FormwerkDocument document, MeshStore meshes)
    {
        foreach (var node in document.Root.Descendants().OfType<MeshNode>().DistinctBy(n => n.MeshId))
        {
            var entry = zip.GetEntry(MeshEntryName(node.MeshId))
                        ?? throw new ProjectFormatException($"The file is damaged: the mesh of the object '{node.Name}' is missing.");
            byte[] bytes;
            using (var entryStream = entry.Open()) bytes = ReadBounded(entryStream, Limits.MaxMeshEntryBytes);
            meshes.Add(node.MeshId, MeshFormat.Read(bytes));
        }
    }

    /// <summary>Reads at most <paramref name="limit"/> bytes; declared sizes in a ZIP can lie, the real count cannot.</summary>
    private static byte[] ReadBounded(Stream stream, long limit)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) throw new ProjectFormatException("The file is unsafe: its content is unreasonably large.");
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private sealed class ReadContext
    {
        public int NodeCount;
        public HashSet<Guid> Ids { get; } = [];
    }

    private static FormwerkDocument BuildDocument(JsonObject root)
    {
        var document = new FormwerkDocument();

        if (root["settings"] is JsonObject settings)
        {
            if (settings["buildVolume"] is not null)
            {
                var v = ReadVector(settings, "buildVolume");
                if (v.X <= 0 || v.Y <= 0 || v.Z <= 0) throw new ProjectFormatException("The file is damaged: the build volume must be positive.");
                document.Settings.BuildVolume = v;
            }

            if (settings["nozzleDiameter"] is not null) document.Settings.NozzleDiameter = ReadPositive(settings, "nozzleDiameter");
            if (settings["tolerance"] is not null) document.Settings.Tolerance = ReadFinite(settings, "tolerance");
        }

        if (root["variables"] is JsonArray variables)
        {
            if (variables.Count > Limits.MaxVariables) throw new ProjectFormatException("The file is damaged: too many variables.");
            foreach (var item in variables)
            {
                var o = item as JsonObject ?? throw new ProjectFormatException("The file is damaged: a variable has the wrong shape.");
                var name = ReadText(o, "name", required: true);
                if (name.Length == 0) throw new ProjectFormatException("The file is damaged: a variable has no name.");
                if (document.Variables.ContainsKey(name)) throw new ProjectFormatException($"The file is damaged: the variable '{name}' appears twice.");
                document.Variables[name] = ReadText(o, "expression", required: true);
            }
        }

        var context = new ReadContext();
        var nodes = root["nodes"] as JsonArray ?? throw new ProjectFormatException("The file is damaged: the list of objects is missing.");
        foreach (var item in nodes)
        {
            var node = ReadNode(item, depth: 1, context);
            document.Root.InsertChild(document.Root.Children.Count, node);
        }

        return document;
    }

    private static ElementLink ReadElement(JsonObject element)
    {
        var id = ReadText(element, "id", required: true);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (element["values"] is JsonObject saved)
        {
            if (saved.Count > Limits.MaxParametersPerNode) throw new ProjectFormatException("The file is damaged: an element has too many values.");
            foreach (var (key, value) in saved)
            {
                if (key.Length is 0 or > 100) throw new ProjectFormatException("The file is damaged: a value name of an element is invalid.");
                var text = value?.GetValue<string>() ?? throw new ProjectFormatException("The file is damaged: a value of an element is missing.");
                if (text.Length > Limits.MaxTextLength) throw new ProjectFormatException("The file is damaged: a value of an element is too long.");
                values[key] = text;
            }
        }

        return new ElementLink(id, values);
    }

    private static Node ReadNode(JsonNode? json, int depth, ReadContext context)
    {
        if (depth > Limits.MaxDepth) throw new ProjectFormatException("The file is damaged: objects are nested too deeply.");
        if (++context.NodeCount > Limits.MaxNodes) throw new ProjectFormatException("The file is damaged: it contains too many objects.");
        var o = json as JsonObject ?? throw new ProjectFormatException("The file is damaged: an object has the wrong shape.");

        var type = ReadText(o, "type", required: true);
        var name = ReadText(o, "name", required: false);
        var id = ReadGuid(o, "id");
        if (!context.Ids.Add(id)) throw new ProjectFormatException("The file is damaged: two objects have the same id.");
        Node node = type switch
        {
            "primitive" => new PrimitiveNode(ReadText(o, "generator", required: true), name) { Id = id },
            "mesh" => new MeshNode(name, ReadGuid(o, "meshId")) { Id = id },
            "group" => new GroupNode(name) { Id = id },
            "boolean" => new BooleanNode(name, ReadOperation(o)) { Id = id },
            "merge" => new MergeNode(name) { Id = id },
            "pattern" => new PatternNode(name, ReadPatternKind(o)) { Id = id },
            "mirror" => new MirrorNode(name) { Id = id },
            _ => throw new ProjectFormatException($"The project contains an object type ('{type}') that this version of Formwerk does not know."),
        };

        node.Role = ReadText(o, "role", required: false) switch
        {
            "hole" => NodeRole.Hole,
            "solid" or "" => NodeRole.Solid,
            var other => throw new ProjectFormatException($"The file is damaged: unknown role '{other}'."),
        };
        if (o["color"] is not null) node.Color = ReadColor(ReadText(o, "color", required: true));
        if (o["visible"] is not null) node.Visible = ReadBool(o, "visible");
        if (o["locked"] is not null) node.Locked = ReadBool(o, "locked");

        var position = o["position"] is null ? Vector3.Zero : ReadVector(o, "position");
        var scale = o["scale"] is null ? Vector3.One : ReadVector(o, "scale");
        var rotation = Quaternion.Identity;
        if (o["rotation"] is JsonArray r)
        {
            if (r.Count != 4) throw new ProjectFormatException("The file is damaged: a rotation needs four numbers.");
            rotation = new Quaternion(Finite(r[0]), Finite(r[1]), Finite(r[2]), Finite(r[3]));
            var length = rotation.Length();
            if (length < 1e-6f) throw new ProjectFormatException("The file is damaged: a rotation is invalid.");
            rotation = Quaternion.Normalize(rotation);
        }

        node.Transform = new Transform(position, rotation, scale);
        if (o["element"] is JsonObject element) node.Element = ReadElement(element);
        if (node is PrimitiveNode shape && o["growAnchor"] is not null)
        {
            var anchor = ReadVector(o, "growAnchor");
            if (anchor.X is not (-1f or 0f or 1f) || anchor.Y is not (-1f or 0f or 1f) || anchor.Z is not (-1f or 0f or 1f))
                throw new ProjectFormatException("The file is damaged: a shape grows from an invalid place.");
            shape.GrowAnchor = anchor;
        }

        if (node is ParametricNode parametric && o["parameters"] is JsonObject parameters)
        {
            if (parameters.Count > Limits.MaxParametersPerNode) throw new ProjectFormatException("The file is damaged: an object has too many parameters.");
            foreach (var (key, value) in parameters)
            {
                if (key.Length is 0 or > 100) throw new ProjectFormatException("The file is damaged: a parameter name is invalid.");
                var text = value?.GetValue<string>() ?? throw new ProjectFormatException("The file is damaged: a parameter value is missing.");
                if (text.Length > Limits.MaxTextLength) throw new ProjectFormatException("The file is damaged: a parameter value is too long.");
                parametric.Parameters[key] = text;
            }
        }

        if (node is PatternNode pattern && o["skipped"] is JsonArray skipped)
        {
            if (skipped.Count > PatternNode.MaxInstances) throw new ProjectFormatException("The file is damaged: a pattern skips too many instances.");
            foreach (var item in skipped)
            {
                var number = item is JsonValue value && value.TryGetValue<int>(out var parsed) ? parsed : 0;
                if (number is < 1 or > PatternNode.MaxInstances) throw new ProjectFormatException("The file is damaged: a skipped instance number is invalid.");
                pattern.Skipped.Add(number);
            }
        }

        if (o["children"] is JsonArray children)
        {
            if (!node.CanHaveChildren && children.Count > 0)
                throw new ProjectFormatException($"The file is damaged: a '{type}' object cannot contain other objects.");
            foreach (var child in children) node.InsertChild(node.Children.Count, ReadNode(child, depth + 1, context));
        }

        return node;
    }

    private static PatternKind ReadPatternKind(JsonObject o)
    {
        var text = ReadText(o, "kind", required: true);
        return Enum.TryParse<PatternKind>(text, ignoreCase: false, out var kind) && Enum.IsDefined(kind)
            ? kind
            : throw new ProjectFormatException($"The file is damaged: unknown pattern kind '{text}'.");
    }

    private static BooleanOperation ReadOperation(JsonObject o)
    {
        var text = ReadText(o, "operation", required: true);
        return Enum.TryParse<BooleanOperation>(text, ignoreCase: false, out var operation) && Enum.IsDefined(operation)
            ? operation
            : throw new ProjectFormatException($"The file is damaged: unknown operation '{text}'.");
    }

    private static string ReadText(JsonObject o, string key, bool required)
    {
        var node = o[key];
        if (node is null)
        {
            if (required) throw new ProjectFormatException($"The file is damaged: '{key}' is missing.");
            return "";
        }

        var text = node.GetValue<string>();
        if (text.Length > Limits.MaxTextLength) throw new ProjectFormatException($"The file is damaged: '{key}' is too long.");
        return text;
    }

    private static Guid ReadGuid(JsonObject o, string key) =>
        Guid.TryParse(ReadText(o, key, required: true), out var id)
            ? id
            : throw new ProjectFormatException($"The file is damaged: '{key}' is not a valid id.");

    private static bool ReadBool(JsonObject o, string key) => o[key]!.GetValue<bool>();

    private static float Finite(JsonNode? node)
    {
        var value = node!.GetValue<double>();
        if (!double.IsFinite(value) || Math.Abs(value) > 1e9) throw new ProjectFormatException("The file is damaged: a number is out of range.");
        return (float)value;
    }

    private static double ReadFinite(JsonObject o, string key)
    {
        var value = o[key]!.GetValue<double>();
        return double.IsFinite(value) ? value : throw new ProjectFormatException($"The file is damaged: '{key}' is not a valid number.");
    }

    private static double ReadPositive(JsonObject o, string key)
    {
        var value = ReadFinite(o, key);
        return value > 0 ? value : throw new ProjectFormatException($"The file is damaged: '{key}' must be positive.");
    }

    private static Vector3 ReadVector(JsonObject o, string key)
    {
        var array = o[key] as JsonArray;
        if (array is null || array.Count != 3) throw new ProjectFormatException($"The file is damaged: '{key}' needs three numbers.");
        return new Vector3(Finite(array[0]), Finite(array[1]), Finite(array[2]));
    }

    private static uint ReadColor(string text)
    {
        var hex = text.StartsWith('#') ? text[1..] : text;
        if (hex.Length == 6) hex = "FF" + hex;
        return hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var color)
            ? color
            : throw new ProjectFormatException("The file is damaged: a color is invalid.");
    }
}

/// <summary>Upgrades older schema versions step by step. Every schema change adds a step here and a test with a saved old file.</summary>
public static class ProjectMigrations
{
    public static JsonObject ToCurrent(JsonObject root)
    {
        var versionNode = root["schemaVersion"];
        int version;
        try
        {
            version = versionNode is null ? 0 : versionNode.GetValue<int>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            throw new ProjectFormatException("The file is damaged: the file format version is invalid.", ex);
        }

        if (version < 1) throw new ProjectFormatException("This is not a Formwerk project (the file format version is missing).");
        if (version > ProjectFile.CurrentSchemaVersion)
            throw new ProjectFormatException(
                $"This project was saved by a newer version of Formwerk (file format {version}; this version understands up to {ProjectFile.CurrentSchemaVersion}). Please update Formwerk.");

        // Example for the future:  if (version == 1) { root = V1ToV2(root); version = 2; }
        return root;
    }
}
