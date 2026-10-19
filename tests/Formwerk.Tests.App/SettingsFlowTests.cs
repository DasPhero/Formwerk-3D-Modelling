using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The settings dialog, project and program settings, and the "outside the build area" marking (F-SET-01, F-SET-02, F-VIS-05, F-SNP-06).</summary>
public sealed class SettingsFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-settings-app-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private UserSettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private MainViewModel NewViewModel(UserSettingsStore? store = null) =>
        new(new ManifoldKernel(), _dialogs, userSettings: store ?? Store());

    private static DocumentSettings Project(float x, float y, float z, double nozzle = 0.4, double tolerance = 0.2) =>
        new() { BuildVolume = new Vector3(x, y, z), NozzleDiameter = nozzle, Tolerance = tolerance };

    private static async Task AddBox(MainViewModel vm)
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
    }

    // ---- the dialog's view model -----------------------------------------------------------------

    [UiFact]
    public void The_dialog_starts_with_the_current_values()
    {
        var vm = new SettingsViewModel(Project(180, 200, 220, 0.6, 0.3), new UserSettings { GridStep = 5, AngleStep = 45, AutosaveMinutes = 7 });

        Assert.Equal("180", vm.BuildX.Text);
        Assert.Equal("200", vm.BuildY.Text);
        Assert.Equal("220", vm.BuildZ.Text);
        Assert.Equal("0.6", vm.Nozzle.Text);
        Assert.Equal("0.3", vm.Tolerance.Text);
        Assert.Equal("5", vm.GridStep.Text);
        Assert.Equal("45", vm.AngleStep.Text);
        Assert.Equal("7", vm.AutosaveMinutes.Text);
        Assert.True(vm.IsValid);
        Assert.NotNull(vm.Result());
    }

    [UiFact]
    public void Unchanged_values_give_back_the_same_settings()
    {
        var user = new UserSettings { GridStep = 2, SlicerPath = null };
        var result = new SettingsViewModel(Project(256, 256, 256), user).Result()!;

        Assert.True(result.Project.SameAs(Project(256, 256, 256)));
        Assert.Equal(user, result.User);
        Assert.False(result.UseForNewProjects);
    }

    [UiFact]
    public void Numbers_can_be_expressions_and_use_the_dot()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.BuildX.Text = "100 + 50";
        vm.Nozzle.Text = "0.25";

        var result = vm.Result()!;
        Assert.Equal(150, result.Project.BuildVolume.X);
        Assert.Equal(0.25, result.Project.NozzleDiameter);
    }

    [UiFact]
    public void An_invalid_value_is_explained_and_blocks_the_result()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.BuildY.Text = "abc";

        Assert.True(vm.BuildY.HasError);
        Assert.False(vm.IsValid);
        Assert.Null(vm.Result());

        vm.BuildY.Text = "210";
        Assert.False(vm.BuildY.HasError);
        Assert.True(vm.IsValid);
        Assert.Equal(210, vm.Result()!.Project.BuildVolume.Y);
    }

    [UiFact]
    public void The_comma_is_not_a_decimal_separator()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.Nozzle.Text = "0,6";

        Assert.True(vm.Nozzle.HasError);
        Assert.False(vm.IsValid);
    }

    [Theory]
    [InlineData("5")]       // build volume at least 10
    [InlineData("5000")]    // and at most 2000
    [InlineData("-20")]
    public void A_build_volume_outside_the_allowed_range_is_refused(string text)
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.BuildZ.Text = text;

        Assert.True(vm.BuildZ.HasError);
        Assert.Contains("10", vm.BuildZ.Error);
        Assert.Contains("2000", vm.BuildZ.Error);
        Assert.Null(vm.Result());
    }

    [UiFact]
    public void Each_field_has_its_own_range()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.Nozzle.Text = "5";
        vm.AngleStep.Text = "0";
        vm.SnapTolerance.Text = "100";
        vm.AutosaveMinutes.Text = "0";
        vm.Tolerance.Text = "0"; // 0 is allowed here

        Assert.True(vm.Nozzle.HasError);
        Assert.True(vm.AngleStep.HasError);
        Assert.True(vm.SnapTolerance.HasError);
        Assert.True(vm.AutosaveMinutes.HasError);
        Assert.False(vm.Tolerance.HasError);
    }

    [UiFact]
    public void A_preset_fills_the_three_build_volume_fields()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());
        var preset = vm.Presets.First(p => p.Volume == new Vector3(250, 210, 220));

        vm.ApplyPresetCommand.Execute(preset);

        Assert.Equal(["250", "210", "220"], new[] { vm.BuildX.Text, vm.BuildY.Text, vm.BuildZ.Text });
        Assert.Equal(new Vector3(250, 210, 220), vm.Result()!.Project.BuildVolume);
        Assert.Equal("250 × 210 × 220 mm", preset.Name);
    }

    [UiFact]
    public void A_preset_repairs_a_field_that_was_invalid()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());
        vm.BuildX.Text = "oops";

        vm.ApplyPresetCommand.Execute(vm.Presets[0]);

        Assert.True(vm.IsValid);
    }

    [UiFact]
    public void The_slicer_path_may_be_empty_but_must_exist_when_given()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());
        Assert.True(vm.IsValid);

        vm.SlicerPath = Path.Combine(_folder, "missing.exe");
        Assert.True(vm.HasSlicerError);
        Assert.False(vm.IsValid);
        Assert.Null(vm.Result());

        Directory.CreateDirectory(_folder);
        var exe = Path.Combine(_folder, "slicer.exe");
        File.WriteAllText(exe, "x");
        vm.SlicerPath = "  " + exe + " ";
        Assert.False(vm.HasSlicerError);
        Assert.Equal(exe, vm.Result()!.User.SlicerPath);

        vm.SlicerPath = "";
        Assert.True(vm.IsValid);
        Assert.Null(vm.Result()!.User.SlicerPath);
    }

    [UiFact]
    public void The_checkboxes_and_the_new_project_option_come_back_in_the_result()
    {
        var vm = new SettingsViewModel(Project(256, 256, 256), new UserSettings());

        vm.SnapToGrid = false;
        vm.SnapAngles = false;
        vm.SnapToObjects = false;
        vm.UseForNewProjects = true;

        var result = vm.Result()!;
        Assert.False(result.User.SnapToGrid);
        Assert.False(result.User.SnapAngles);
        Assert.False(result.User.SnapToObjects);
        Assert.True(result.UseForNewProjects);
    }

    // ---- project settings in the main view model -------------------------------------------------

    // F-SET-01 acceptance criterion: a changed build volume shows immediately.
    [UiFact]
    public void Confirming_the_dialog_changes_the_project_and_tells_the_viewport()
    {
        var vm = NewViewModel();
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string?>(); // raised from background continuations too
        vm.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);
        _dialogs.SettingsAnswer = new SettingsResult(Project(180, 200, 220, 0.6, 0.3), vm.CurrentUserSettings(), false);

        vm.OpenSettingsCommand.Execute(null);

        Assert.Single(_dialogs.SettingsPrompts);
        Assert.Equal(new Vector3(180, 200, 220), vm.BuildVolume);
        Assert.Equal(0.6, vm.Document.Settings.NozzleDiameter);
        Assert.Equal(0.3, vm.Document.Settings.Tolerance);
        Assert.Contains(nameof(MainViewModel.BuildVolume), changed);
        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public void The_dialog_gets_a_copy_of_the_current_settings()
    {
        var vm = NewViewModel();
        _dialogs.SettingsAnswer = null;

        vm.OpenSettingsCommand.Execute(null);

        var (project, user) = Assert.Single(_dialogs.SettingsPrompts);
        Assert.NotSame(vm.Document.Settings, project);
        Assert.True(project.SameAs(vm.Document.Settings));
        Assert.Equal(vm.CurrentUserSettings(), user);
    }

    [UiFact]
    public void Cancelling_the_dialog_changes_nothing()
    {
        var vm = NewViewModel();
        _dialogs.SettingsAnswer = null;

        vm.OpenSettingsCommand.Execute(null);

        Assert.Equal(new Vector3(256, 256, 256), vm.BuildVolume);
        Assert.False(vm.IsDirty);
        Assert.False(File.Exists(Store().Path));
    }

    [UiFact]
    public void A_project_settings_change_is_one_undo_step()
    {
        var vm = NewViewModel();
        vm.ApplySettings(new SettingsResult(Project(150, 150, 150), vm.CurrentUserSettings(), false));
        Assert.Equal(new Vector3(150, 150, 150), vm.BuildVolume);

        vm.UndoCommand.Execute(null);
        Assert.Equal(new Vector3(256, 256, 256), vm.BuildVolume);
        Assert.False(vm.IsDirty);

        vm.RedoCommand.Execute(null);
        Assert.Equal(new Vector3(150, 150, 150), vm.BuildVolume);
        Assert.Contains(vm.HistoryEntries, e => e.Name == "Change settings");
    }

    [UiFact]
    public void Confirming_without_a_change_adds_no_history_entry()
    {
        var vm = NewViewModel();
        var before = vm.HistoryEntries.Count;

        vm.ApplySettings(new SettingsResult(Project(256, 256, 256), vm.CurrentUserSettings(), false));

        Assert.Equal(before, vm.HistoryEntries.Count);
        Assert.False(vm.IsDirty);
    }

    [UiFact]
    public void Project_settings_are_saved_in_the_project_file()
    {
        var vm = NewViewModel();
        vm.ApplySettings(new SettingsResult(Project(210, 220, 230, 0.5, 0.25), vm.CurrentUserSettings(), false));
        var path = Path.Combine(_folder, "p.fwp");
        _dialogs.SavePath = path;
        Assert.True(vm.Save());

        var other = NewViewModel();
        Assert.True(other.OpenPath(path));

        Assert.Equal(new Vector3(210, 220, 230), other.BuildVolume);
        Assert.Equal(0.5, other.Document.Settings.NozzleDiameter);
        Assert.Equal(0.25, other.Document.Settings.Tolerance);
    }

    [UiFact]
    public void Opening_a_project_shows_its_own_build_volume()
    {
        var vm = NewViewModel();
        vm.ApplySettings(new SettingsResult(Project(120, 130, 140), vm.CurrentUserSettings(), false));
        var path = Path.Combine(_folder, "q.fwp");
        _dialogs.SavePath = path;
        vm.Save();

        var other = NewViewModel();
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string?>(); // raised from background continuations too
        other.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);
        other.OpenPath(path);

        Assert.Contains(nameof(MainViewModel.BuildVolume), changed);
        Assert.Equal(new Vector3(120, 130, 140), other.BuildVolume);
    }

    // ---- program settings ------------------------------------------------------------------------

    // F-SET-02 acceptance criterion: settings are kept after a restart.
    [UiFact]
    public void Program_settings_from_the_dialog_survive_a_restart()
    {
        var vm = NewViewModel();
        var user = vm.CurrentUserSettings() with
        {
            GridStep = 2.5, AngleStep = 30, SnapTolerancePixels = 18, AutosaveMinutes = 9,
            SnapToGrid = false, SnapAngles = false, SnapToObjects = false,
        };

        vm.ApplySettings(new SettingsResult(Project(256, 256, 256), user, false));

        var restarted = NewViewModel();
        Assert.Equal(2.5, restarted.GridStep);
        Assert.Equal(30, restarted.AngleStep);
        Assert.Equal(18, restarted.SnapTolerancePixels);
        Assert.Equal(9, restarted.AutosaveMinutes);
        Assert.False(restarted.SnapToGrid);
        Assert.False(restarted.SnapAngles);
        Assert.False(restarted.SnapToObjects);
    }

    [UiFact]
    public void The_status_bar_toggles_are_remembered_too()
    {
        var vm = NewViewModel();

        vm.SnapToObjects = false;
        vm.GridStep = 5;
        vm.AngleStep = 45;

        var stored = Store().Load();
        Assert.False(stored.SnapToObjects);
        Assert.Equal(5, stored.GridStep);
        Assert.Equal(45, stored.AngleStep);
    }

    [UiFact]
    public void Starting_does_not_write_a_settings_file_by_itself()
    {
        _ = NewViewModel();

        Assert.False(File.Exists(Store().Path));
    }

    [UiFact]
    public void A_damaged_settings_file_gives_the_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store().Path, "{ not json");

        var vm = NewViewModel();

        Assert.True(vm.SnapToGrid);
        Assert.Equal(1, vm.GridStep);
        Assert.Equal(10, vm.SnapTolerancePixels);
        Assert.Equal(2, vm.AutosaveMinutes);
    }

    [UiFact]
    public void A_grid_step_from_the_dialog_appears_in_the_status_bar_choices()
    {
        var vm = NewViewModel();
        Assert.DoesNotContain(2.5, vm.GridSteps);

        vm.ApplySettings(new SettingsResult(Project(256, 256, 256), vm.CurrentUserSettings() with { GridStep = 2.5 }, false));

        Assert.Contains(2.5, vm.GridSteps);
        Assert.Equal(2.5, vm.GridStep);
        Assert.Equal(vm.GridSteps.OrderBy(x => x), vm.GridSteps); // inserted in order
    }

    [UiFact]
    public async Task The_snap_distance_setting_is_used_while_dragging()
    {
        var vm = NewViewModel();
        await AddBox(vm);
        await AddBox(vm);
        vm.SnapToGrid = false;

        // Box 2 is at x = -30 (spans -40..-20), box 1 at 0 (spans -10..10). Moving box 2 by +9.7 puts its right edge 0.3 mm
        // from box 1's left edge; at 0.1 mm per pixel that is 3 px away.
        vm.SelectOnly([vm.Document.Root.Children[1].Id]);
        var drag = new GizmoDrag(GizmoHandleKind.MoveX, new Vector3(9.7f, 0, 0), 0, false, WorldPerPixel: 0.1);

        vm.SnapTolerancePixels = 2; // too small to reach
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(drag);
        var withSmall = vm.Document.Root.Children[1].Transform.Position.X;
        vm.CancelDrag();

        vm.SnapTolerancePixels = 10; // 1 mm: reaches
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(drag);
        var withLarge = vm.Document.Root.Children[1].Transform.Position.X;
        vm.CancelDrag();

        Assert.Equal(-30 + 9.7, withSmall, 2);
        Assert.Equal(-30 + 10, withLarge, 2);
    }

    // ---- new projects ----------------------------------------------------------------------------

    [UiFact]
    public void New_projects_take_the_defaults_when_asked_to()
    {
        _dialogs.UnsavedAnswer = UnsavedChoice.Discard; // the settings change made the project dirty
        var vm = NewViewModel();
        vm.ApplySettings(new SettingsResult(Project(180, 190, 200, 0.6, 0.3), vm.CurrentUserSettings(), UseForNewProjects: true));

        vm.NewCommand.Execute(null);

        Assert.Equal(new Vector3(180, 190, 200), vm.BuildVolume);
        Assert.Equal(0.6, vm.Document.Settings.NozzleDiameter);
        Assert.Equal(0.3, vm.Document.Settings.Tolerance);

        var restarted = NewViewModel(); // the defaults are remembered, so the next session starts with them too
        Assert.Equal(new Vector3(180, 190, 200), restarted.BuildVolume);
    }

    [UiFact]
    public void New_projects_keep_the_old_defaults_when_not_asked()
    {
        _dialogs.UnsavedAnswer = UnsavedChoice.Discard;
        var vm = NewViewModel();
        vm.ApplySettings(new SettingsResult(Project(180, 190, 200), vm.CurrentUserSettings(), UseForNewProjects: false));

        vm.NewCommand.Execute(null);

        Assert.Equal(new Vector3(256, 256, 256), vm.BuildVolume);
    }

    // ---- objects outside the build area (F-VIS-05) -----------------------------------------------

    [UiFact]
    public async Task An_object_inside_the_build_area_is_not_marked()
    {
        var vm = NewViewModel();
        await AddBox(vm);

        Assert.Equal(0, vm.OutsideCount);
        Assert.All(vm.ViewportItems, i => Assert.False(i.Outside));
        Assert.False(vm.Nodes[0].IsOutside);
        Assert.DoesNotContain("Outside", vm.StatusText);
    }

    [UiFact]
    public async Task A_smaller_build_volume_marks_objects_that_no_longer_fit()
    {
        var vm = NewViewModel();
        await AddBox(vm);
        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(95, 0, 0)); // spans 85..105
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        Assert.Equal(0, vm.OutsideCount);

        vm.ApplySettings(new SettingsResult(Project(200, 200, 200), vm.CurrentUserSettings(), false)); // plate spans -100..100
        await vm.WaitForEvaluationAsync();

        Assert.Equal(1, vm.OutsideCount);
        Assert.True(vm.Nodes[0].IsOutside);
        Assert.Contains(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid && i.Outside);
        Assert.Contains("Outside the build area: 1", vm.StatusText);
    }

    [UiFact]
    public async Task Undoing_the_settings_change_removes_the_mark()
    {
        var vm = NewViewModel();
        await AddBox(vm);
        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(95, 0, 0));
        vm.Document.NotifyChanged();
        vm.ApplySettings(new SettingsResult(Project(200, 200, 200), vm.CurrentUserSettings(), false));
        await vm.WaitForEvaluationAsync();
        Assert.Equal(1, vm.OutsideCount);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(0, vm.OutsideCount);
        Assert.False(vm.Nodes[0].IsOutside);
    }

    [UiFact]
    public async Task An_object_below_the_plate_or_above_the_height_is_outside()
    {
        var vm = NewViewModel();
        await AddBox(vm);

        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(0, 0, -5));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        Assert.Equal(1, vm.OutsideCount);

        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(0, 0, 250)); // top at 270 > 256
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        Assert.Equal(1, vm.OutsideCount);

        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(0, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        Assert.Equal(0, vm.OutsideCount);
    }

    [UiFact]
    public async Task Touching_the_border_exactly_is_not_outside()
    {
        var vm = NewViewModel();
        await AddBox(vm); // 20 x 20 x 20 at the center, resting on the plate

        vm.ApplySettings(new SettingsResult(Project(20, 20, 20), vm.CurrentUserSettings(), false));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(0, vm.OutsideCount);
    }

    [UiFact]
    public async Task A_hole_is_never_marked_as_outside()
    {
        var vm = NewViewModel();
        await AddBox(vm);
        vm.ToggleHole();
        vm.Document.Root.Children[0].Transform = Transform.FromPosition(new Vector3(500, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        Assert.Equal(0, vm.OutsideCount);
    }
}
