using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Formwerk.IO;

/// <summary>
/// Settings of the program (not of one project), remembered between sessions (F-SET-02). The first three values are the
/// defaults for new projects; the project itself keeps its own copy (build volume, nozzle, tolerance) in the project file.
/// </summary>
public sealed record UserSettings
{
    /// <summary>The ids of the library elements marked as favorites (F-LIB-02).</summary>
    public IdList FavoriteLibraryItems { get; init; } = IdList.None;

    public Vector3 DefaultBuildVolume { get; init; } = new(256, 256, 256);

    public double DefaultNozzleDiameter { get; init; } = 0.4;

    public double DefaultTolerance { get; init; } = 0.2;

    public bool SnapToGrid { get; init; } = true;

    /// <summary>Grid size in mm.</summary>
    public double GridStep { get; init; } = 1;

    public bool SnapAngles { get; init; } = true;

    /// <summary>Angle step in degrees.</summary>
    public double AngleStep { get; init; } = 15;

    public bool SnapToObjects { get; init; } = true;

    /// <summary>How close in screen pixels something must come to snap to it (F-SNP-06).</summary>
    public double SnapTolerancePixels { get; init; } = 10;

    public double AutosaveMinutes { get; init; } = 2;

    /// <summary>Fold angle in degrees from which the edge between two faces is drawn as a line (F-VIS-02).</summary>
    public double EdgeAngleDegrees { get; init; } = 30;

    /// <summary>Keep every object resting on the build plate after each change (F-FLR-01).</summary>
    public bool AlwaysOnFloor { get; init; }

    /// <summary>The light theme instead of the dark one (F-VIS-07).</summary>
    public bool LightTheme { get; init; }

    /// <summary>Scaling keeps the sizes of holes, chamfers and other details (F-SCL-05).</summary>
    public bool KeepDetailsWhenScaling { get; init; } = true;

    /// <summary>Edge length in pixels of the pictures in the library panel.</summary>
    public double LibraryThumbnailSize { get; init; } = 44;

    /// <summary>Program that "Open in Slicer" starts; null when not set.</summary>
    public string? SlicerPath { get; init; }

    public const float MinBuildSize = 10;
    public const float MaxBuildSize = 2000;
    public const double MinThumbnailSize = 28;
    public const double MaxThumbnailSize = 110;

    /// <summary>The same settings with every value brought into its allowed range (a hand-edited file cannot break the program).</summary>
    public UserSettings Normalized() => this with
    {
        DefaultBuildVolume = new Vector3(
            Clamp(DefaultBuildVolume.X, MinBuildSize, MaxBuildSize, 256),
            Clamp(DefaultBuildVolume.Y, MinBuildSize, MaxBuildSize, 256),
            Clamp(DefaultBuildVolume.Z, MinBuildSize, MaxBuildSize, 256)),
        DefaultNozzleDiameter = Clamp(DefaultNozzleDiameter, 0.1, 2, 0.4),
        DefaultTolerance = Clamp(DefaultTolerance, 0, 2, 0.2),
        GridStep = Clamp(GridStep, 0.01, 100, 1),
        AngleStep = Clamp(AngleStep, 0.1, 180, 15),
        SnapTolerancePixels = Clamp(SnapTolerancePixels, 2, 50, 10),
        AutosaveMinutes = Clamp(AutosaveMinutes, 1, 60, 2),
        EdgeAngleDegrees = Clamp(EdgeAngleDegrees, 1, 90, 30),
        LibraryThumbnailSize = Clamp(LibraryThumbnailSize, MinThumbnailSize, MaxThumbnailSize, 44),
        SlicerPath = string.IsNullOrWhiteSpace(SlicerPath) ? null : SlicerPath.Trim(),
        FavoriteLibraryItems = (FavoriteLibraryItems ?? IdList.None).Cleaned(),
    };

    private static float Clamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static double Clamp(double value, double min, double max, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
}

/// <summary>
/// Reads and writes <see cref="UserSettings"/> as a small JSON file. Reading never fails: a missing, damaged or hand-edited
/// file gives defaults for what cannot be used. Writing is atomic, like for projects.
/// </summary>
public sealed class UserSettingsStore(string path)
{
    /// <summary>Per-user, roams with the account, no admin rights needed.</summary>
    public static string DefaultPath { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Formwerk", "settings.json");

    public string Path { get; } = path;

    public UserSettings Load()
    {
        try
        {
            if (!File.Exists(Path)) return new UserSettings();
            if (new FileInfo(Path).Length > 1_000_000) return new UserSettings(); // not a settings file

            if (JsonNode.Parse(File.ReadAllBytes(Path)) is not JsonObject root) return new UserSettings();
            var defaults = new UserSettings();
            return new UserSettings
            {
                DefaultBuildVolume = ReadVector(root, "defaultBuildVolume") ?? defaults.DefaultBuildVolume,
                DefaultNozzleDiameter = ReadNumber(root, "defaultNozzleDiameter") ?? defaults.DefaultNozzleDiameter,
                DefaultTolerance = ReadNumber(root, "defaultTolerance") ?? defaults.DefaultTolerance,
                SnapToGrid = ReadBool(root, "snapToGrid") ?? defaults.SnapToGrid,
                GridStep = ReadNumber(root, "gridStep") ?? defaults.GridStep,
                SnapAngles = ReadBool(root, "snapAngles") ?? defaults.SnapAngles,
                AngleStep = ReadNumber(root, "angleStep") ?? defaults.AngleStep,
                SnapToObjects = ReadBool(root, "snapToObjects") ?? defaults.SnapToObjects,
                SnapTolerancePixels = ReadNumber(root, "snapTolerancePixels") ?? defaults.SnapTolerancePixels,
                AutosaveMinutes = ReadNumber(root, "autosaveMinutes") ?? defaults.AutosaveMinutes,
                EdgeAngleDegrees = ReadNumber(root, "edgeAngleDegrees") ?? defaults.EdgeAngleDegrees,
                AlwaysOnFloor = ReadBool(root, "alwaysOnFloor") ?? defaults.AlwaysOnFloor,
                LightTheme = ReadBool(root, "lightTheme") ?? defaults.LightTheme,
                KeepDetailsWhenScaling = ReadBool(root, "keepDetailsWhenScaling") ?? defaults.KeepDetailsWhenScaling,
                LibraryThumbnailSize = ReadNumber(root, "libraryThumbnailSize") ?? defaults.LibraryThumbnailSize,
                SlicerPath = ReadText(root, "slicerPath"),
                FavoriteLibraryItems = ReadIds(root, "favoriteLibraryItems"),
            }.Normalized();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return new UserSettings();
        }
    }

    /// <exception cref="IOException">The file could not be written (the caller decides whether to tell the user).</exception>
    public void Save(UserSettings settings)
    {
        var s = settings.Normalized();
        var directory = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        ProjectFile.SaveAtomic(Path, stream =>
        {
            using var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" });
            json.WriteStartObject();
            json.WriteStartArray("defaultBuildVolume");
            json.WriteNumberValue(s.DefaultBuildVolume.X);
            json.WriteNumberValue(s.DefaultBuildVolume.Y);
            json.WriteNumberValue(s.DefaultBuildVolume.Z);
            json.WriteEndArray();
            json.WriteNumber("defaultNozzleDiameter", s.DefaultNozzleDiameter);
            json.WriteNumber("defaultTolerance", s.DefaultTolerance);
            json.WriteBoolean("snapToGrid", s.SnapToGrid);
            json.WriteNumber("gridStep", s.GridStep);
            json.WriteBoolean("snapAngles", s.SnapAngles);
            json.WriteNumber("angleStep", s.AngleStep);
            json.WriteBoolean("snapToObjects", s.SnapToObjects);
            json.WriteNumber("snapTolerancePixels", s.SnapTolerancePixels);
            json.WriteNumber("autosaveMinutes", s.AutosaveMinutes);
            json.WriteNumber("edgeAngleDegrees", s.EdgeAngleDegrees);
            json.WriteBoolean("alwaysOnFloor", s.AlwaysOnFloor);
            json.WriteBoolean("lightTheme", s.LightTheme);
            json.WriteBoolean("keepDetailsWhenScaling", s.KeepDetailsWhenScaling);
            json.WriteNumber("libraryThumbnailSize", s.LibraryThumbnailSize);
            if (s.SlicerPath is not null) json.WriteString("slicerPath", s.SlicerPath);
            if (s.FavoriteLibraryItems.Ids.Count > 0)
            {
                json.WriteStartArray("favoriteLibraryItems");
                foreach (var id in s.FavoriteLibraryItems.Ids) json.WriteStringValue(id);
                json.WriteEndArray();
            }

            json.WriteEndObject();
        });
    }

    private static double? ReadNumber(JsonObject o, string key)
    {
        try { return o[key]?.GetValue<double>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static bool? ReadBool(JsonObject o, string key)
    {
        try { return o[key]?.GetValue<bool>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static string? ReadText(JsonObject o, string key)
    {
        try { return o[key]?.GetValue<string>(); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static IdList ReadIds(JsonObject o, string key)
    {
        if (o[key] is not JsonArray array) return IdList.None;
        var ids = new List<string>();
        foreach (var node in array)
        {
            try
            {
                if (node?.GetValue<string>() is { } id) ids.Add(id);
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException)
            {
                // an entry that is not text is skipped
            }
        }

        return new IdList(ids);
    }

    private static Vector3? ReadVector(JsonObject o, string key)
    {
        if (o[key] is not JsonArray { Count: 3 } array) return null;
        try { return new Vector3(array[0]!.GetValue<float>(), array[1]!.GetValue<float>(), array[2]!.GetValue<float>()); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }
}

/// <summary>A list of ids (text) that compares by its content, so that settings with the same favorites are equal.</summary>
public sealed record IdList(IReadOnlyList<string> Ids)
{
    public const int MaxCount = 1000;

    public static IdList None { get; } = new([]);

    public bool Equals(IdList? other) => other is not null && Ids.SequenceEqual(other.Ids, StringComparer.Ordinal);

    public override int GetHashCode() => Ids.Count;

    /// <summary>Blank entries and doubles (any case) removed, the order kept, at most <see cref="MaxCount"/> entries.</summary>
    public IdList Cleaned()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cleaned = Ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Where(seen.Add).Take(MaxCount).ToList();
        return cleaned.Count == 0 ? None : new IdList(cleaned);
    }
}
