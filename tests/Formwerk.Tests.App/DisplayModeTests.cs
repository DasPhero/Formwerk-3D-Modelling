using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Display modes and the edge angle (F-VIS-02, F-VIS-03, F-SET-02).</summary>
public sealed class DisplayModeTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-display-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private UserSettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs, userSettings: Store());

    private static DocumentSettings Project() => new();

    // ---- display mode ----------------------------------------------------------------------------

    [UiFact]
    public void Shaded_with_edges_is_the_default()
    {
        var vm = NewViewModel();

        Assert.Equal(ViewportDisplayMode.ShadedWithEdges, vm.DisplayMode);
        Assert.True(vm.IsShadedWithEdgesMode);
        Assert.False(vm.IsShadedMode);
        Assert.False(vm.IsWireframeMode);
        Assert.False(vm.IsXRayMode);
    }

    [UiFact]
    public void The_key_cycles_through_all_four_modes_and_starts_over()
    {
        var vm = NewViewModel();
        var seen = new List<ViewportDisplayMode>();

        for (var i = 0; i < 5; i++)
        {
            vm.CycleDisplayModeCommand.Execute(null);
            seen.Add(vm.DisplayMode);
        }

        Assert.Equal(
            [ViewportDisplayMode.Wireframe, ViewportDisplayMode.XRay, ViewportDisplayMode.Shaded, ViewportDisplayMode.ShadedWithEdges, ViewportDisplayMode.Wireframe],
            seen);
    }

    [UiFact]
    public void The_menu_items_follow_the_mode_and_set_it()
    {
        var vm = NewViewModel();
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        vm.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        vm.IsXRayMode = true;

        Assert.Equal(ViewportDisplayMode.XRay, vm.DisplayMode);
        Assert.True(vm.IsXRayMode);
        Assert.False(vm.IsShadedWithEdgesMode);
        Assert.Contains(nameof(MainViewModel.IsShadedWithEdgesMode), changed); // the old item is unchecked
        Assert.Contains(nameof(MainViewModel.IsXRayMode), changed);
    }

    [UiFact]
    public void Clicking_the_checked_item_again_keeps_the_mode_and_announces_it_so_the_check_mark_returns()
    {
        var vm = NewViewModel();
        vm.IsWireframeMode = true;
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string?>();
        vm.PropertyChanged += (_, e) => changed.Enqueue(e.PropertyName);

        vm.IsWireframeMode = false; // what unchecking the checked menu item does

        Assert.Equal(ViewportDisplayMode.Wireframe, vm.DisplayMode);
        Assert.Contains(nameof(MainViewModel.IsWireframeMode), changed);
    }

    [UiFact]
    public void The_display_mode_does_not_touch_the_document_or_the_history()
    {
        var vm = NewViewModel();
        var history = vm.HistoryEntries.Count;

        vm.CycleDisplayModeCommand.Execute(null);

        Assert.Equal(history, vm.HistoryEntries.Count);
        Assert.False(vm.IsDirty);
    }

    // ---- edge angle ------------------------------------------------------------------------------

    [UiFact]
    public void The_edge_angle_is_30_degrees_by_default()
    {
        Assert.Equal(30, NewViewModel().EdgeAngle);
        Assert.Equal(30, new UserSettings().EdgeAngleDegrees);
    }

    [UiFact]
    public void A_changed_edge_angle_is_remembered_after_a_restart()
    {
        var vm = NewViewModel();

        vm.EdgeAngle = 45;

        Assert.Equal(45, Store().Load().EdgeAngleDegrees);
        Assert.Equal(45, NewViewModel().EdgeAngle);
    }

    [UiFact]
    public void The_settings_dialog_changes_the_edge_angle()
    {
        var vm = NewViewModel();
        var dialog = new SettingsViewModel(Project(), vm.CurrentUserSettings());
        Assert.Equal("30", dialog.EdgeAngle.Text);

        dialog.EdgeAngle.Text = "12.5";
        vm.ApplySettings(dialog.Result()!);

        Assert.Equal(12.5, vm.EdgeAngle);
        Assert.Equal(12.5, Store().Load().EdgeAngleDegrees);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.5")]
    [InlineData("91")]
    [InlineData("-5")]
    [InlineData("abc")]
    public void An_edge_angle_outside_1_to_90_degrees_is_refused(string text)
    {
        var dialog = new SettingsViewModel(Project(), new UserSettings());

        dialog.EdgeAngle.Text = text;

        Assert.True(dialog.EdgeAngle.HasError);
        Assert.Null(dialog.Result());
    }

    [UiFact]
    public void The_edge_angle_is_part_of_the_current_program_settings()
    {
        var vm = NewViewModel();
        vm.EdgeAngle = 60;

        Assert.Equal(60, vm.CurrentUserSettings().EdgeAngleDegrees);
    }

    // ---- the settings file -----------------------------------------------------------------------

    [UiFact]
    public void The_edge_angle_survives_saving_and_loading_the_settings_file()
    {
        var store = Store();

        store.Save(new UserSettings { EdgeAngleDegrees = 22.5 });

        Assert.Equal(22.5, Store().Load().EdgeAngleDegrees);
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(500.0, 90.0)]
    [InlineData(double.NaN, 30.0)]
    public void An_edge_angle_outside_its_range_is_brought_back_into_it(double value, double expected)
    {
        Assert.Equal(expected, new UserSettings { EdgeAngleDegrees = value }.Normalized().EdgeAngleDegrees);
    }

    [UiFact]
    public void An_old_settings_file_without_an_edge_angle_gets_the_default()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Store().Path, "{ \"gridStep\": 5 }");

        var loaded = Store().Load();

        Assert.Equal(5, loaded.GridStep);
        Assert.Equal(30, loaded.EdgeAngleDegrees);
    }
}
