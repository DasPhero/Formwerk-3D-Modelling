using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>A template file is damaged, too big or not a template at all.</summary>
public sealed class TemplateFormatException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>What a template is called and described as (F-TPL-01).</summary>
/// <param name="Size">Size in mm of what the template contains (saved on the plate), so inserting needs no calculation.</param>
public sealed record TemplateInfo(string Name, string Description, string Category, IReadOnlyList<string> Tags, Vector3 Size, DateTime CreatedUtc, int Version = 1);

/// <summary>Limits for template files: they come from a folder the user (or other programs) can fill, so they are treated like imports.</summary>
public static class TemplateLimits
{
    public const int MaxNameLength = 100;
    public const int MaxDescriptionLength = 2000;
    public const int MaxCategoryLength = 60;
    public const int MaxTags = 20;
    public const int MaxTagLength = 40;
    public const int MaxVersion = 99999;
    public const int MaxEntries = 8;
    public const long MaxInfoBytes = 64 * 1024;
    public const long MaxPreviewBytes = 2 * 1024 * 1024;
}

/// <summary>Rules for template names (F-TPL-01): required, one line, not too long; a taken name gets "Name (2)".</summary>
public static class TemplateNames
{
    /// <summary>Leading and trailing white space removed, runs of white space reduced to one blank.</summary>
    public static string Clean(string? name) => string.Join(' ', (name ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>The reason a name cannot be used, or null when it is fine.</summary>
    public static string? Validate(string? name)
    {
        var cleaned = Clean(name);
        if (cleaned.Length == 0) return "A template needs a name.";
        if (cleaned.Length > TemplateLimits.MaxNameLength) return $"The name can have at most {TemplateLimits.MaxNameLength} characters.";
        if (cleaned.Any(char.IsControl)) return "The name cannot contain control characters.";
        return null;
    }

    /// <summary>The name itself when it is free, otherwise "Name (2)", "Name (3)", ... (case does not matter; an existing number is replaced).</summary>
    public static string Suggest(string name, IEnumerable<string> taken)
    {
        var cleaned = Clean(name);
        var set = new HashSet<string>(taken.Select(Clean), StringComparer.OrdinalIgnoreCase);
        if (!set.Contains(cleaned)) return cleaned;

        var match = System.Text.RegularExpressions.Regex.Match(cleaned, @"^(?<base>.*\S)\s*\(\d+\)$");
        var stem = match.Success ? match.Groups["base"].Value : cleaned;
        for (var i = 2; ; i++)
        {
            var candidate = $"{stem} ({i})";
            if (!set.Contains(candidate)) return candidate;
        }
    }
}

/// <summary>
/// A template (F-TPL-01..03): a zip with <c>template.json</c> (the description), <c>preview.png</c> (optional) and
/// <c>project.fwp</c>, the normal project format holding the saved objects as children of the scene, so parameters, holes, groups and
/// boolean nodes stay editable after loading.
/// </summary>
public static class TemplateFile
{
    public const string Extension = ".fwt";
    public const int CurrentSchemaVersion = 1;
    private const string InfoEntry = "template.json";
    private const string PreviewEntry = "preview.png";
    private const string ProjectEntry = "project.fwp";

    // ---- writing -------------------------------------------------------------------------------

    /// <param name="document">The saved objects as children of its scene; its variables are saved too.</param>
    /// <param name="meshes">Where the meshes of imported objects come from.</param>
    public static void Write(Stream stream, FormwerkDocument document, MeshStore? meshes, TemplateInfo info, byte[]? previewPng)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(info);
        if (TemplateNames.Validate(info.Name) is { } problem) throw new ArgumentException(problem, nameof(info));
        if (!float.IsFinite(info.Size.X) || !float.IsFinite(info.Size.Y) || !float.IsFinite(info.Size.Z))
            throw new ArgumentException("The size of the template is not a number.", nameof(info));
        if (previewPng is { Length: > (int)TemplateLimits.MaxPreviewBytes }) throw new ArgumentException("The preview is too large.", nameof(previewPng));

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        var infoEntry = zip.CreateEntry(InfoEntry, CompressionLevel.Optimal);
        using (var s = infoEntry.Open()) WriteInfo(s, info);

        if (previewPng is { Length: > 0 })
        {
            var previewEntry = zip.CreateEntry(PreviewEntry, CompressionLevel.NoCompression); // a PNG is compressed already
            using var s = previewEntry.Open();
            s.Write(previewPng);
        }

        var projectEntry = zip.CreateEntry(ProjectEntry, CompressionLevel.NoCompression); // a zip inside a zip
        using (var s = projectEntry.Open()) ProjectFile.Write(s, document, meshes);
    }

    public static void SaveAtomic(string path, FormwerkDocument document, MeshStore? meshes, TemplateInfo info, byte[]? previewPng) =>
        ProjectFile.SaveAtomic(path, stream => Write(stream, document, meshes, info, previewPng));

    private static void WriteInfo(Stream stream, TemplateInfo info)
    {
        using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });
        json.WriteStartObject();
        json.WriteNumber("schema", CurrentSchemaVersion);
        json.WriteString("name", TemplateNames.Clean(info.Name));
        json.WriteString("description", info.Description ?? "");
        json.WriteString("category", TemplateNames.Clean(info.Category));
        json.WriteStartArray("tags");
        foreach (var tag in info.Tags) json.WriteStringValue(tag);
        json.WriteEndArray();
        json.WriteStartArray("size");
        json.WriteNumberValue(info.Size.X);
        json.WriteNumberValue(info.Size.Y);
        json.WriteNumberValue(info.Size.Z);
        json.WriteEndArray();
        json.WriteString("created", info.CreatedUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        json.WriteNumber("version", Math.Max(1, info.Version));
        json.WriteEndObject();
    }

    // ---- changing the description of a file ----------------------------------------------------------

    /// <summary>
    /// Writes a template like <paramref name="source"/> but with another description (name, description, category, tags, size, date):
    /// the preview and the objects are copied as they are. The source part must be a sound template.
    /// </summary>
    public static void WriteWithInfo(Stream source, Stream destination, TemplateInfo info)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(info);
        if (TemplateNames.Validate(info.Name) is { } problem) throw new ArgumentException(problem, nameof(info));

        byte[]? preview;
        byte[] project;
        try
        {
            using var zip = OpenZip(source);
            ParseInfo(ReadEntry(zip, InfoEntry, TemplateLimits.MaxInfoBytes, required: true)!);
            preview = ReadEntry(zip, PreviewEntry, TemplateLimits.MaxPreviewBytes, required: false);
            project = ReadEntry(zip, ProjectEntry, ProjectFile.Limits.MaxFileBytes, required: true)!;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw new TemplateFormatException("The file is not a valid template: " + ex.Message, ex);
        }

        using var outZip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
        using (var s = outZip.CreateEntry(InfoEntry, CompressionLevel.Optimal).Open()) WriteInfo(s, info);
        if (preview is { Length: > 0 })
        {
            using var s = outZip.CreateEntry(PreviewEntry, CompressionLevel.NoCompression).Open();
            s.Write(preview);
        }

        using (var s = outZip.CreateEntry(ProjectEntry, CompressionLevel.NoCompression).Open()) s.Write(project);
    }

    /// <summary>Writes the template at <paramref name="sourcePath"/> to <paramref name="destinationPath"/> with another description (atomically).</summary>
    public static void SaveCopy(string sourcePath, string destinationPath, TemplateInfo info)
    {
        var bytes = File.ReadAllBytes(sourcePath);
        ProjectFile.SaveAtomic(destinationPath, stream => WriteWithInfo(new MemoryStream(bytes), stream, info));
    }

    /// <summary>Gives the file another description in place (atomically).</summary>
    public static void ReplaceInfo(string path, TemplateInfo info) => SaveCopy(path, path, info);


    // ---- reading -------------------------------------------------------------------------------

    /// <summary>The description and the preview, without reading the objects (for the list).</summary>
    public static (TemplateInfo Info, byte[]? Preview) ReadInfo(Stream stream)
    {
        try
        {
            using var zip = OpenZip(stream);
            var info = ParseInfo(ReadEntry(zip, InfoEntry, TemplateLimits.MaxInfoBytes, required: true)!);
            var preview = ReadEntry(zip, PreviewEntry, TemplateLimits.MaxPreviewBytes, required: false);
            return (info, preview);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or InvalidOperationException or FormatException)
        {
            throw new TemplateFormatException("The file is not a valid template: " + ex.Message, ex);
        }
    }

    /// <summary>The saved objects as children of the scene of a new document; imported meshes go into <paramref name="meshes"/>.</summary>
    public static FormwerkDocument ReadDocument(Stream stream, MeshStore? meshes = null)
    {
        try
        {
            using var zip = OpenZip(stream);
            ParseInfo(ReadEntry(zip, InfoEntry, TemplateLimits.MaxInfoBytes, required: true)!); // the description must be sound, too
            var project = ReadEntry(zip, ProjectEntry, ProjectFile.Limits.MaxFileBytes, required: true)!;
            return ProjectFile.Read(new MemoryStream(project), meshes);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or JsonException or InvalidOperationException or FormatException or ProjectFormatException)
        {
            throw new TemplateFormatException("The file is not a valid template: " + ex.Message, ex);
        }
    }

    public static (TemplateInfo Info, byte[]? Preview) LoadInfo(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadInfo(stream);
    }

    public static FormwerkDocument Load(string path, MeshStore? meshes = null)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ReadDocument(stream, meshes);
    }

    private static ZipArchive OpenZip(Stream stream)
    {
        var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        if (zip.Entries.Count > TemplateLimits.MaxEntries)
        {
            zip.Dispose();
            throw new InvalidDataException("the file contains too many parts");
        }

        return zip;
    }

    private static byte[]? ReadEntry(ZipArchive zip, string name, long limit, bool required)
    {
        var entry = zip.GetEntry(name);
        if (entry is null)
        {
            if (required) throw new InvalidDataException($"the part '{name}' is missing");
            return null;
        }

        if (entry.Length > limit) throw new InvalidDataException($"the part '{name}' is too large");
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException($"the part '{name}' is too large"); // the header lied about the size
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static TemplateInfo ParseInfo(byte[] bytes)
    {
        var root = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { MaxDepth = 16, CommentHandling = JsonCommentHandling.Disallow }) as JsonObject
                   ?? throw new InvalidDataException("the description is not an object");

        if (root["schema"] is not JsonValue schema || !schema.TryGetValue<int>(out var version) || version != CurrentSchemaVersion)
            throw new InvalidDataException("this template has an unknown version");

        var name = TemplateNames.Clean(Text(root, "name", required: true));
        if (TemplateNames.Validate(name) is { } problem) throw new InvalidDataException(problem);
        var description = Text(root, "description", required: false);
        if (description.Length > TemplateLimits.MaxDescriptionLength) throw new InvalidDataException("the description is too long");
        var category = TemplateNames.Clean(Text(root, "category", required: false));
        if (category.Length > TemplateLimits.MaxCategoryLength || category.Any(char.IsControl)) throw new InvalidDataException("the category is not valid");

        var tags = new List<string>();
        if (root["tags"] is { } tagsNode)
        {
            if (tagsNode is not JsonArray array) throw new InvalidDataException("the tags are not a list");
            if (array.Count > TemplateLimits.MaxTags) throw new InvalidDataException("too many tags");
            foreach (var item in array)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out var tag) || tag.Length > TemplateLimits.MaxTagLength)
                    throw new InvalidDataException("a tag is not valid");
                tags.Add(tag);
            }
        }

        if (root["size"] is not JsonArray { Count: 3 } size) throw new InvalidDataException("the size is missing");
        var numbers = new float[3];
        for (var i = 0; i < 3; i++)
        {
            if (size[i] is not JsonValue v || !v.TryGetValue<double>(out var d) || !double.IsFinite(d) || d < 0 || d > 1e7)
                throw new InvalidDataException("the size is not valid");
            numbers[i] = (float)d;
        }

        var created = DateTime.MinValue;
        if (root["created"] is JsonValue c && c.TryGetValue<string>(out var text)
            && DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            created = parsed;

        var fileVersion = 1;
        if (root["version"] is { } versionNode)
        {
            if (versionNode is not JsonValue v || !v.TryGetValue<int>(out fileVersion) || fileVersion < 1 || fileVersion > TemplateLimits.MaxVersion)
                throw new InvalidDataException("the version is not valid");
        }

        return new TemplateInfo(name, description, category, tags, new Vector3(numbers[0], numbers[1], numbers[2]), created, fileVersion);
    }

    private static string Text(JsonObject o, string key, bool required)
    {
        if (o[key] is null)
        {
            if (required) throw new InvalidDataException($"'{key}' is missing");
            return "";
        }

        if (o[key] is not JsonValue v || !v.TryGetValue<string>(out var text)) throw new InvalidDataException($"'{key}' is not text");
        return text;
    }
}
