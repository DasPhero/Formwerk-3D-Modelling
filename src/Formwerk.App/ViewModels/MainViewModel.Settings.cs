using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Settings (F-SET-01, F-SET-02): the project's own settings live in the document and go through undo; the program's
/// settings are remembered in a file in the user's profile. Also marks objects outside the build volume (F-VIS-05).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Tolerance for "outside": float noise and a surface that exactly touches the border must not count.</summary>
    private const float OutsideEpsilon = 0.01f;

    private readonly UserSettingsStore? _settingsStore;
    private UserSettings _user = new();
    private bool _applyingUser;
    private HashSet<Guid> _outside = [];

    /// <summary>How close in screen pixels an edge or center must come to another object to snap to it (F-SNP-06).</summary>
    [ObservableProperty]
    public partial double SnapTolerancePixels { get; set; } = 10;

    /// <summary>The light theme instead of the dark one (F-VIS-07). The window applies it; it is a program setting, not part of a project.</summary>
    [ObservableProperty]
    public partial bool LightTheme { get; set; }

    /// <summary>View > Light Theme.</summary>
    [RelayCommand]
    private void ToggleTheme() => LightTheme = !LightTheme;

    /// <summary>Every object rests on the build plate again after each change (F-FLR-01).</summary>
    [ObservableProperty]
    public partial bool AlwaysOnFloor { get; set; }

    /// <summary>How often unsaved work is written to the autosave file (E-05).</summary>
    [ObservableProperty]
    public partial double AutosaveMinutes { get; set; } = 2;

    /// <summary>Program for "Open in Slicer"; stored here until that feature exists.</summary>
    public string? SlicerPath => _user.SlicerPath;

    /// <summary>The program settings as they are right now (status bar toggles included).</summary>
    public UserSettings CurrentUserSettings() => _user with
    {
        SnapToGrid = SnapToGrid,
        GridStep = GridStep,
        SnapAngles = SnapAngles,
        AngleStep = AngleStep,
        SnapToObjects = SnapToObjects,
        SnapTolerancePixels = SnapTolerancePixels,
        AutosaveMinutes = AutosaveMinutes,
        EdgeAngleDegrees = EdgeAngle,
        AlwaysOnFloor = AlwaysOnFloor,
        LightTheme = LightTheme,
        KeepDetailsWhenScaling = KeepDetailsWhenScaling,
        LibraryThumbnailSize = LibraryThumbnailSize,
        FavoriteLibraryItems = new IdList([.. _favoriteIds]),
    };

    /// <summary>Number of objects that reach outside the build volume (F-VIS-05).</summary>
    public int OutsideCount => _outside.Count;

    private void LoadUserSettings() => ApplyUserSettings(_settingsStore?.Load() ?? new UserSettings());

    private void ApplyUserSettings(UserSettings settings)
    {
        _user = settings.Normalized();
        EnsureChoice(GridSteps, _user.GridStep); // before the selection, so the combo box never sees a value it does not list
        EnsureChoice(AngleSteps, _user.AngleStep);
        _applyingUser = true;
        try
        {
            SnapToGrid = _user.SnapToGrid;
            GridStep = _user.GridStep;
            SnapAngles = _user.SnapAngles;
            AngleStep = _user.AngleStep;
            SnapToObjects = _user.SnapToObjects;
            SnapTolerancePixels = _user.SnapTolerancePixels;
            AutosaveMinutes = _user.AutosaveMinutes;
            EdgeAngle = _user.EdgeAngleDegrees;
            AlwaysOnFloor = _user.AlwaysOnFloor;
            LightTheme = _user.LightTheme;
            KeepDetailsWhenScaling = _user.KeepDetailsWhenScaling;
            LibraryThumbnailSize = _user.LibraryThumbnailSize;
        }
        finally
        {
            _applyingUser = false;
        }

        ApplyFavorites(_user.FavoriteLibraryItems.Ids);
        OnPropertyChanged(nameof(SlicerPath));
    }

    /// <summary>Adds a value to a status bar choice list (sorted) when the settings dialog introduced one that is not there.</summary>
    private static void EnsureChoice(ObservableCollection<double> choices, double value)
    {
        if (choices.Contains(value)) return;
        var index = 0;
        while (index < choices.Count && choices[index] < value) index++;
        choices.Insert(index, value);
    }

    private void PersistUserSettings()
    {
        if (_applyingUser) return;
        _user = CurrentUserSettings().Normalized();
        SaveUserSettings();
    }

    private void SaveUserSettings()
    {
        try
        {
            _settingsStore?.Save(_user);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetNotice(Texts.Format("Msg.SettingsNotSaved", ex.Message)); // the settings still apply for this session
        }
    }

    partial void OnSnapToGridChanged(bool value) => PersistUserSettings();

    partial void OnGridStepChanged(double value) => PersistUserSettings();

    partial void OnSnapAnglesChanged(bool value) => PersistUserSettings();

    partial void OnAngleStepChanged(double value) => PersistUserSettings();

    partial void OnSnapToObjectsChanged(bool value) => PersistUserSettings();

    partial void OnSnapTolerancePixelsChanged(double value) => PersistUserSettings();

    partial void OnAutosaveMinutesChanged(double value) => PersistUserSettings();

    partial void OnEdgeAngleChanged(double value) => PersistUserSettings();

    partial void OnLightThemeChanged(bool value) => PersistUserSettings();

    partial void OnAlwaysOnFloorChanged(bool value)
    {
        PersistUserSettings();
        if (value) RequestFloorCheck(); // switching it on puts everything on the plate at once
    }

    // ---- defaults for new projects -------------------------------------------------------------

    private FormwerkDocument NewDocument()
    {
        var document = new FormwerkDocument();
        ApplyDefaultsTo(document);
        return document;
    }

    private void ApplyDefaultsTo(FormwerkDocument document)
    {
        document.Settings.BuildVolume = _user.DefaultBuildVolume;
        document.Settings.NozzleDiameter = _user.DefaultNozzleDiameter;
        document.Settings.Tolerance = _user.DefaultTolerance;
    }

    // ---- the settings dialog -------------------------------------------------------------------

    [RelayCommand]
    private void OpenSettings()
    {
        var result = _dialogs.ShowSettings(_document.Settings.Clone(), CurrentUserSettings());
        if (result is not null) ApplySettings(result);
    }

    /// <summary>
    /// Takes over what the settings dialog returned: program settings are remembered at once; project settings become one
    /// undoable step (a change that changes nothing adds no history entry).
    /// </summary>
    public void ApplySettings(SettingsResult result)
    {
        var user = result.User;
        if (result.UseForNewProjects)
        {
            user = user with
            {
                DefaultBuildVolume = result.Project.BuildVolume,
                DefaultNozzleDiameter = result.Project.NozzleDiameter,
                DefaultTolerance = result.Project.Tolerance,
            };
        }

        ApplyUserSettings(user);
        SaveUserSettings();

        var command = new SetSettingsCommand(_document, result.Project);
        if (command.HasEffect) _history.Execute(command);
    }

    // ---- objects outside the build volume (F-VIS-05) -------------------------------------------

    private HashSet<Guid> FindOutside(EvaluationResult result)
    {
        var volume = _document.Settings.BuildVolume;
        var half = new Vector2(volume.X, volume.Y) / 2;
        var outside = new HashSet<Guid>();
        foreach (var part in result.Parts)
        {
            if (part.Kind != PartKind.Solid || part.Mesh.IsEmpty) continue;
            var b = part.Mesh.ComputeBounds();
            if (b.Min.X < -half.X - OutsideEpsilon || b.Max.X > half.X + OutsideEpsilon
                || b.Min.Y < -half.Y - OutsideEpsilon || b.Max.Y > half.Y + OutsideEpsilon
                || b.Min.Z < -OutsideEpsilon || b.Max.Z > volume.Z + OutsideEpsilon)
                outside.Add(part.Source.Id);
        }

        return outside;
    }

    private string WithOutsideWarning(string text) =>
        _outside.Count == 0 ? text : $"{text}   ⚠ {Texts.Format("Status.Outside", _outside.Count)}";
}
