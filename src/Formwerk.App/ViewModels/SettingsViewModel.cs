using System.Globalization;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>What the settings dialog hands back when the user confirms.</summary>
/// <param name="Project">Build volume, nozzle and tolerance of this project.</param>
/// <param name="User">The program settings.</param>
/// <param name="UseForNewProjects">The project values also become the defaults for new projects.</param>
public sealed record SettingsResult(DocumentSettings Project, UserSettings User, bool UseForNewProjects);

/// <summary>A typical printer build volume the user can pick instead of typing three numbers.</summary>
public sealed record PrinterPreset(string Name, Vector3 Volume)
{
    public PrinterPreset(Vector3 volume) : this(string.Create(CultureInfo.InvariantCulture, $"{volume.X:0.#} × {volume.Y:0.#} × {volume.Z:0.#} mm"), volume)
    {
    }
}

/// <summary>
/// The settings dialog (F-SET-01, F-SET-02). Every number is a text field that accepts expressions; a field with an
/// invalid value shows why, and <see cref="IsValid"/> keeps the OK button disabled until everything is usable.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly UserSettings _user;
    private readonly Dictionary<FieldViewModel, double> _values = [];
    private readonly List<FieldViewModel> _fields = [];

    public SettingsViewModel(DocumentSettings project, UserSettings user)
    {
        _user = user;
        BuildX = Field(project.BuildVolume.X, UserSettings.MinBuildSize, UserSettings.MaxBuildSize);
        BuildY = Field(project.BuildVolume.Y, UserSettings.MinBuildSize, UserSettings.MaxBuildSize);
        BuildZ = Field(project.BuildVolume.Z, UserSettings.MinBuildSize, UserSettings.MaxBuildSize);
        Nozzle = Field(project.NozzleDiameter, 0.1, 2);
        Tolerance = Field(project.Tolerance, 0, 2);
        GridStep = Field(user.GridStep, 0.01, 100);
        AngleStep = Field(user.AngleStep, 0.1, 180);
        SnapTolerance = Field(user.SnapTolerancePixels, 2, 50);
        AutosaveMinutes = Field(user.AutosaveMinutes, 1, 60);
        EdgeAngle = Field(user.EdgeAngleDegrees, 1, 90);

        SnapToGrid = user.SnapToGrid;
        SnapAngles = user.SnapAngles;
        SnapToObjects = user.SnapToObjects;
        AlwaysOnFloor = user.AlwaysOnFloor;
        LightTheme = user.LightTheme;
        _slicerPath = user.SlicerPath ?? "";
        SlicerError = ValidateSlicer(_slicerPath);
        Presets = [new(new Vector3(256, 256, 256)), new(new Vector3(250, 210, 220)), new(new Vector3(220, 220, 250)),
                   new(new Vector3(180, 180, 180)), new(new Vector3(300, 300, 300))];
    }

    public FieldViewModel BuildX { get; }

    public FieldViewModel BuildY { get; }

    public FieldViewModel BuildZ { get; }

    public FieldViewModel Nozzle { get; }

    public FieldViewModel Tolerance { get; }

    public FieldViewModel GridStep { get; }

    public FieldViewModel AngleStep { get; }

    public FieldViewModel SnapTolerance { get; }

    public FieldViewModel AutosaveMinutes { get; }

    public FieldViewModel EdgeAngle { get; }

    public IReadOnlyList<PrinterPreset> Presets { get; }

    [ObservableProperty]
    public partial bool SnapToGrid { get; set; }

    [ObservableProperty]
    public partial bool SnapAngles { get; set; }

    [ObservableProperty]
    public partial bool SnapToObjects { get; set; }

    [ObservableProperty]
    public partial bool AlwaysOnFloor { get; set; }

    [ObservableProperty]
    public partial bool LightTheme { get; set; }

    [ObservableProperty]
    public partial bool UseForNewProjects { get; set; }

    private string _slicerPath;

    /// <summary>Path of the slicer program; may be empty.</summary>
    public string SlicerPath
    {
        get => _slicerPath;
        set
        {
            if (!SetProperty(ref _slicerPath, value)) return;
            SlicerError = ValidateSlicer(value);
            OnPropertyChanged(nameof(IsValid));
        }
    }

    [ObservableProperty]
    public partial string? SlicerError { get; set; }

    public bool HasSlicerError => !string.IsNullOrEmpty(SlicerError);

    partial void OnSlicerErrorChanged(string? value) => OnPropertyChanged(nameof(HasSlicerError));

    /// <summary>True when every field holds a usable value.</summary>
    public bool IsValid => _fields.All(f => !f.HasError) && SlicerError is null;

    /// <summary>Fills the three build volume fields from a preset.</summary>
    [RelayCommand]
    private void ApplyPreset(PrinterPreset? preset)
    {
        if (preset is null) return;
        BuildX.Text = Format(preset.Volume.X);
        BuildY.Text = Format(preset.Volume.Y);
        BuildZ.Text = Format(preset.Volume.Z);
    }

    /// <summary>The values of the dialog, or null while something is invalid.</summary>
    public SettingsResult? Result()
    {
        if (!IsValid) return null;
        var project = new DocumentSettings
        {
            BuildVolume = new Vector3((float)_values[BuildX], (float)_values[BuildY], (float)_values[BuildZ]),
            NozzleDiameter = _values[Nozzle],
            Tolerance = _values[Tolerance],
        };
        var user = _user with
        {
            SnapToGrid = SnapToGrid,
            GridStep = _values[GridStep],
            SnapAngles = SnapAngles,
            AngleStep = _values[AngleStep],
            SnapToObjects = SnapToObjects,
            SnapTolerancePixels = _values[SnapTolerance],
            AutosaveMinutes = _values[AutosaveMinutes],
            EdgeAngleDegrees = _values[EdgeAngle],
            AlwaysOnFloor = AlwaysOnFloor,
            LightTheme = LightTheme,
            SlicerPath = string.IsNullOrWhiteSpace(SlicerPath) ? null : SlicerPath.Trim(),
        };
        return new SettingsResult(project, user, UseForNewProjects);
    }

    private FieldViewModel Field(double initial, double min, double max)
    {
        FieldViewModel? field = null;
        field = new FieldViewModel("", "", text =>
        {
            var error = Parse(text, min, max, out var value);
            if (error is null) _values[field!] = value;
            return error;
        });
        field.SetFromModel(Format(initial));
        _values[field] = initial;
        _fields.Add(field);
        field.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FieldViewModel.Error)) OnPropertyChanged(nameof(IsValid));
        };
        return field;
    }

    private static string? Parse(string text, double min, double max, out double value)
    {
        value = 0;
        var parsed = ExpressionParser.Evaluate(text);
        if (!parsed.IsSuccess) return parsed.Error;
        if (!double.IsFinite(parsed.Value) || parsed.Value < min || parsed.Value > max)
            return Texts.Format("Settings.Range", Format(min), Format(max));
        value = parsed.Value;
        return null;
    }

    private static string? ValidateSlicer(string path) =>
        string.IsNullOrWhiteSpace(path) || File.Exists(path.Trim()) ? null : Texts.Get("Settings.SlicerMissing");

    private static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
