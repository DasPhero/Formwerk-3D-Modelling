using System.Numerics;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

public sealed class UserSettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-settings-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private UserSettingsStore Store(string name = "settings.json") => new(Path.Combine(_folder, name));

    [Fact]
    public void Without_a_file_the_defaults_are_used()
    {
        var settings = Store().Load();

        Assert.Equal(new Vector3(256, 256, 256), settings.DefaultBuildVolume);
        Assert.Equal(0.4, settings.DefaultNozzleDiameter);
        Assert.Equal(0.2, settings.DefaultTolerance);
        Assert.True(settings.SnapToGrid);
        Assert.Equal(1, settings.GridStep);
        Assert.True(settings.SnapAngles);
        Assert.Equal(15, settings.AngleStep);
        Assert.True(settings.SnapToObjects);
        Assert.Equal(10, settings.SnapTolerancePixels);
        Assert.Equal(2, settings.AutosaveMinutes);
        Assert.False(settings.AlwaysOnFloor);
        Assert.False(settings.LightTheme);
        Assert.Null(settings.SlicerPath);
    }

    // F-SET-02 acceptance criterion: settings are kept after a restart.
    [Fact]
    public void Saved_settings_come_back_exactly()
    {
        var store = Store();
        var settings = new UserSettings
        {
            DefaultBuildVolume = new Vector3(180, 220, 250),
            DefaultNozzleDiameter = 0.6,
            DefaultTolerance = 0.35,
            SnapToGrid = false,
            GridStep = 5,
            SnapAngles = false,
            AngleStep = 45,
            SnapToObjects = false,
            SnapTolerancePixels = 14,
            AutosaveMinutes = 7,
            AlwaysOnFloor = true,
            LightTheme = true,
            SlicerPath = @"C:\Program Files\Slicer\slicer.exe",
        };

        store.Save(settings);
        var loaded = Store().Load(); // a new store object, like a new program start

        Assert.Equal(settings, loaded);
    }

    [Fact]
    public void The_library_favorites_are_kept_in_order_and_cleaned()
    {
        var store = Store();
        store.Save(new UserSettings { FavoriteLibraryItems = new IdList(["hex-nut-m3", " washer-m3 ", "HEX-NUT-M3", "", "box"]) });

        var loaded = store.Load();

        Assert.Equal(["hex-nut-m3", "washer-m3", "box"], loaded.FavoriteLibraryItems.Ids);
        Assert.Equal(loaded, Store().Load());
        Assert.Empty(new UserSettings().FavoriteLibraryItems.Ids);
    }

    [Fact]
    public void Wrong_favorites_in_the_file_are_ignored()
    {
        Directory.CreateDirectory(_folder);
        var store = Store();
        File.WriteAllText(store.Path, "{ \"favoriteLibraryItems\": [\"box\", 5, null, \"cone\"] }");
        Assert.Equal(["box", "cone"], store.Load().FavoriteLibraryItems.Ids);

        File.WriteAllText(store.Path, "{ \"favoriteLibraryItems\": \"box\" }");
        Assert.Empty(store.Load().FavoriteLibraryItems.Ids);
    }

    [Fact]
    public void The_always_on_floor_option_is_written_and_a_wrong_type_gives_the_default()
    {
        var store = Store();
        store.Save(new UserSettings { AlwaysOnFloor = true });
        Assert.Contains("\"alwaysOnFloor\": true", File.ReadAllText(store.Path));
        Assert.True(store.Load().AlwaysOnFloor);

        File.WriteAllText(store.Path, "{ \"alwaysOnFloor\": \"yes\" }");
        Assert.False(store.Load().AlwaysOnFloor);
    }

    [Fact]
    public void The_light_theme_is_written_and_a_wrong_type_gives_the_dark_default()
    {
        var store = Store();
        store.Save(new UserSettings { LightTheme = true });
        Assert.Contains("\"lightTheme\": true", File.ReadAllText(store.Path));
        Assert.True(store.Load().LightTheme);

        File.WriteAllText(store.Path, "{ \"lightTheme\": 7 }");
        Assert.False(store.Load().LightTheme);
    }

    [Fact]
    public void Saving_creates_the_folder_and_leaves_no_temp_file()
    {
        var store = Store();
        store.Save(new UserSettings());

        Assert.True(File.Exists(store.Path));
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void The_file_is_readable_json_with_stable_content()
    {
        var store = Store();
        store.Save(new UserSettings());
        var first = File.ReadAllText(store.Path);
        store.Save(new UserSettings());

        Assert.Equal(first, File.ReadAllText(store.Path));
        Assert.Contains("\"gridStep\": 1", first);
        Assert.Contains("\"defaultBuildVolume\"", first);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"gridStep\": ")]
    [InlineData("null")]
    public void A_damaged_file_gives_the_defaults_instead_of_an_error(string content)
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, content);

        Assert.Equal(new UserSettings(), store.Load());
    }

    [Fact]
    public void Wrong_types_in_single_values_only_lose_those_values()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, """
            { "gridStep": "five", "angleStep": 45, "snapToGrid": 3, "autosaveMinutes": 9,
              "defaultBuildVolume": [100, "x", 100], "unknownFutureKey": { "a": 1 } }
            """);

        var loaded = store.Load();

        Assert.Equal(1, loaded.GridStep);                       // unusable: default
        Assert.Equal(45, loaded.AngleStep);                     // fine: kept
        Assert.True(loaded.SnapToGrid);                         // unusable: default
        Assert.Equal(9, loaded.AutosaveMinutes);
        Assert.Equal(new Vector3(256, 256, 256), loaded.DefaultBuildVolume);
    }

    [Fact]
    public void Values_outside_their_range_are_brought_back_into_it()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllText(store.Path, """
            { "defaultBuildVolume": [1, 99999, 300], "defaultNozzleDiameter": 50, "defaultTolerance": -1,
              "gridStep": 0, "angleStep": 1000, "snapTolerancePixels": 1000, "autosaveMinutes": 0.01 }
            """);

        var loaded = store.Load();

        Assert.Equal(new Vector3(UserSettings.MinBuildSize, UserSettings.MaxBuildSize, 300), loaded.DefaultBuildVolume);
        Assert.Equal(2, loaded.DefaultNozzleDiameter);
        Assert.Equal(0, loaded.DefaultTolerance);
        Assert.Equal(0.01, loaded.GridStep);
        Assert.Equal(180, loaded.AngleStep);
        Assert.Equal(50, loaded.SnapTolerancePixels);
        Assert.Equal(1, loaded.AutosaveMinutes);
    }

    [Fact]
    public void Normalizing_replaces_non_numbers_and_cleans_the_slicer_path()
    {
        var normalized = new UserSettings
        {
            GridStep = double.NaN,
            AngleStep = double.PositiveInfinity,
            DefaultBuildVolume = new Vector3(float.NaN, 100, 100),
            SlicerPath = "   ",
        }.Normalized();

        Assert.Equal(1, normalized.GridStep);
        Assert.Equal(15, normalized.AngleStep); // infinity is not a usable number: default
        Assert.Equal(256, normalized.DefaultBuildVolume.X);
        Assert.Null(normalized.SlicerPath);
        Assert.Equal(@"C:\x\y.exe", new UserSettings { SlicerPath = @"  C:\x\y.exe " }.Normalized().SlicerPath);
    }

    [Fact]
    public void A_file_that_is_far_too_large_is_ignored()
    {
        var store = Store();
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(store.Path, new byte[1_500_000]);

        Assert.Equal(new UserSettings(), store.Load());
    }

    [Fact]
    public void Saving_values_out_of_range_writes_the_corrected_values()
    {
        var store = Store();
        store.Save(new UserSettings { GridStep = -5, DefaultBuildVolume = new Vector3(5, 5, 5) });

        var loaded = store.Load();
        Assert.Equal(0.01, loaded.GridStep);
        Assert.Equal(new Vector3(UserSettings.MinBuildSize), loaded.DefaultBuildVolume);
    }
}
