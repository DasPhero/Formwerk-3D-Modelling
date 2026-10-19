using System.Text.Json;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>What the user can recover after a crash.</summary>
public sealed record RecoveryInfo(DateTime SavedUtc, string? OriginalPath);

/// <summary>
/// Keeps one autosave copy of the open project (E-05). The file is written atomically, so a crash while saving
/// never leaves a half-written autosave. It is deleted when the user saves or deliberately discards the work;
/// if it is still there at the next start, the program was not closed normally and offers recovery.
/// </summary>
public sealed class AutosaveStore(string directory)
{
    private const string ProjectName = "autosave" + ProjectFile.Extension;
    private const string MetaName = "autosave.json";

    /// <summary>Per-user folder, no admin rights needed.</summary>
    public static string DefaultDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Formwerk", "autosave");

    public string Directory { get; } = directory;

    private string ProjectPath => Path.Combine(Directory, ProjectName);

    private string MetaPath => Path.Combine(Directory, MetaName);

    /// <summary>Writes the autosave. <paramref name="snapshot"/> must not be modified meanwhile (use <c>Snapshot()</c>).</summary>
    public void Write(FormwerkDocument snapshot, string? originalPath, MeshStore? meshes = null)
    {
        System.IO.Directory.CreateDirectory(Directory);
        ProjectFile.SaveAtomic(ProjectPath, snapshot, meshes);
        ProjectFile.SaveAtomic(MetaPath, stream =>
        {
            using var json = new Utf8JsonWriter(stream);
            json.WriteStartObject();
            json.WriteString("savedUtc", DateTime.UtcNow);
            if (originalPath is not null) json.WriteString("originalPath", originalPath);
            json.WriteEndObject();
        });
    }

    public bool TryGetRecovery(out RecoveryInfo info)
    {
        info = new RecoveryInfo(default, null);
        if (!File.Exists(ProjectPath)) return false;

        var saved = File.GetLastWriteTimeUtc(ProjectPath);
        string? original = null;
        try
        {
            if (File.Exists(MetaPath))
            {
                using var meta = JsonDocument.Parse(File.ReadAllBytes(MetaPath));
                if (meta.RootElement.TryGetProperty("savedUtc", out var time) && time.TryGetDateTime(out var parsed)) saved = parsed.ToUniversalTime();
                if (meta.RootElement.TryGetProperty("originalPath", out var path) && path.ValueKind == JsonValueKind.String) original = path.GetString();
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The metadata is only a convenience; the autosave itself is still usable.
        }

        info = new RecoveryInfo(saved, original);
        return true;
    }

    /// <exception cref="ProjectFormatException">The autosave cannot be read.</exception>
    public FormwerkDocument Load(MeshStore? meshes = null) => ProjectFile.Load(ProjectPath, meshes);

    public void Clear()
    {
        foreach (var path in new[] { ProjectPath, MetaPath, ProjectPath + ".tmp", MetaPath + ".tmp" })
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
