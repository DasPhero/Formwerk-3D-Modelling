using System.IO;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The global option "keep on floor" (F-FLR-01): objects rest on the plate again after every change.</summary>
public sealed class AlwaysOnFloorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-floor-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static MainViewModel NewViewModel(UserSettingsStore? store = null) =>
        new(new ManifoldKernel(), new FakeDialogs(), userSettings: store);

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task Idle(MainViewModel vm) => await vm.WaitForEvaluationAsync();

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static float Bottom(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Min(i => i.Mesh.ComputeBounds().Min.Z);

    private static async Task SetField(MainViewModel vm, FieldViewModel field, string text)
    {
        field.Text = text;
        await Idle(vm);
    }

    /// <summary>A box on the plate (option as asked) that has been turned by 30 degrees about X, which sinks its front edge.</summary>
    private static async Task<Node> TurnedBox(MainViewModel vm)
    {
        await Add(vm, "box");
        await SetField(vm, vm.Properties!.Rotation[0], "30");
        return Child(vm, 0);
    }

    // ---- the acceptance criterion ----------------------------------------------------------------

    [UiFact]
    public async Task After_turning_by_30_degrees_the_object_lies_on_the_floor_again()
    {
        var vm = NewViewModel();
        vm.AlwaysOnFloor = true;
        await Idle(vm);

        var box = await TurnedBox(vm);

        Assert.Equal(0, Bottom(vm, box.Id), 3);
        Assert.True(box.Transform.Position.Z > 4, "the box was lifted by about 5 mm");
    }

    [UiFact]
    public async Task The_turn_and_the_correction_are_one_undo_step()
    {
        var vm = NewViewModel();
        vm.AlwaysOnFloor = true;
        await Add(vm, "box");
        var before = vm.HistoryEntries.Count;

        await SetField(vm, vm.Properties!.Rotation[0], "30");
        var box = Child(vm, 0);
        Assert.Equal(before + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await Idle(vm);

        Assert.Equal(0, box.Transform.Position.Z, 3);
        Assert.Equal("0", vm.Properties!.Rotation[0].Text);
        Assert.Equal(0, Bottom(vm, box.Id), 3);
        Assert.Equal(before, vm.HistoryEntries.Count(e => !e.IsFuture));

        vm.RedoCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(0, Bottom(vm, box.Id), 3);
        Assert.True(box.Transform.Position.Z > 4);
    }

    [UiFact]
    public async Task Without_the_option_the_turned_object_sinks_into_the_plate()
    {
        var vm = NewViewModel();

        var box = await TurnedBox(vm);

        Assert.True(Bottom(vm, box.Id) < -4);
        Assert.Equal(0, box.Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task A_typed_height_goes_back_to_the_plate_while_the_option_is_on()
    {
        var vm = NewViewModel();
        vm.AlwaysOnFloor = true;
        await Add(vm, "box");

        await SetField(vm, vm.Properties!.Position[2], "10");

        Assert.Equal(0, Child(vm, 0).Transform.Position.Z, 3);
        Assert.Equal(0, Bottom(vm, Child(vm, 0).Id), 3);
    }

    [UiFact]
    public async Task Objects_that_already_rest_on_the_plate_cause_no_extra_history_entry()
    {
        var vm = NewViewModel();
        vm.AlwaysOnFloor = true;

        await Add(vm, "box");
        await Add(vm, "sphere");

        Assert.Equal(3, vm.HistoryEntries.Count); // initial state, add box, add sphere
    }

    [UiFact]
    public async Task Turning_by_90_degrees_keeps_the_object_on_the_floor()
    {
        var vm = NewViewModel();
        vm.AlwaysOnFloor = true;
        await Add(vm, "wedge");
        var wedge = Child(vm, 0);

        vm.Rotate90Command.Execute("X");
        await Idle(vm);
        vm.Rotate90Command.Execute("Y");
        await Idle(vm);

        Assert.Equal(0, Bottom(vm, wedge.Id), 3);
    }

    // ---- switching it on -------------------------------------------------------------------------

    [UiFact]
    public async Task Switching_the_option_on_lowers_floating_and_lifts_sunken_objects_in_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await SetField(vm, vm.Properties!.Position[2], "10");
        await Add(vm, "sphere");
        await SetField(vm, vm.Properties!.Position[2], "-4");
        var box = Child(vm, 0);
        var sphere = Child(vm, 1);
        var before = vm.HistoryEntries.Count;

        vm.AlwaysOnFloor = true;
        await Idle(vm);

        Assert.Equal(0, Bottom(vm, box.Id), 3);
        Assert.Equal(0, Bottom(vm, sphere.Id), 3);
        Assert.Equal(before + 1, vm.HistoryEntries.Count);
        Assert.Equal(Texts.Get("Name.DropToFloor"), vm.HistoryEntries[^1].Name);

        vm.UndoCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(10, box.Transform.Position.Z, 3);
        Assert.Equal(-4, sphere.Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task Switching_the_option_on_with_everything_on_the_plate_changes_nothing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var before = vm.HistoryEntries.Count;

        vm.AlwaysOnFloor = true;
        await Idle(vm);

        Assert.Equal(before, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task Undo_back_to_a_floating_state_is_left_alone()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await SetField(vm, vm.Properties!.Position[2], "10");
        vm.AlwaysOnFloor = true;
        await Idle(vm);
        var box = Child(vm, 0);

        vm.UndoCommand.Execute(null);
        await Idle(vm);

        Assert.Equal(10, box.Transform.Position.Z, 3);
        Assert.True(vm.RedoCommand.CanExecute(null), "no new entry may cut off the redo branch");
    }

    // ---- what stays where it is ------------------------------------------------------------------

    [UiFact]
    public async Task Locked_objects_are_not_moved()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await SetField(vm, vm.Properties!.Position[2], "10");
        var box = Child(vm, 0);
        box.Locked = true;

        vm.AlwaysOnFloor = true;
        await Idle(vm);

        Assert.Equal(10, box.Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task A_hole_that_cuts_nothing_stays_where_it_is()
    {
        var vm = NewViewModel();
        await Add(vm, "cylinder");
        vm.Properties!.IsHole = true;
        await Idle(vm);
        await SetField(vm, vm.Properties!.Position[2], "10");
        var hole = Child(vm, 0);

        vm.AlwaysOnFloor = true;
        await Idle(vm);

        Assert.Equal(10, hole.Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task A_group_comes_down_as_a_whole()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        vm.GroupCommand.Execute(null);
        await Idle(vm);
        var group = Child(vm, 0);
        vm.SelectOnly([group.Id]);
        await SetField(vm, vm.Properties!.Position[2], "15");
        var inner = group.Children.Select(c => Bottom(vm, c.Id)).ToList();
        Assert.True(inner.Min() > 14);

        vm.AlwaysOnFloor = true;
        await Idle(vm);

        Assert.Equal(0, group.Children.Min(c => Bottom(vm, c.Id)), 3);
        Assert.Equal(inner.Max() - inner.Min(), group.Children.Max(c => Bottom(vm, c.Id)) - group.Children.Min(c => Bottom(vm, c.Id)), 3);
        Assert.Single(vm.Document.Root.Children);
    }

    // ---- the setting -----------------------------------------------------------------------------

    [UiFact]
    public void The_option_is_off_by_default()
    {
        Assert.False(NewViewModel().AlwaysOnFloor);
        Assert.False(NewViewModel().CurrentUserSettings().AlwaysOnFloor);
    }

    [UiFact]
    public void The_option_is_remembered_between_sessions()
    {
        var store = new UserSettingsStore(Path.Combine(_folder, "settings.json"));
        var vm = NewViewModel(store);

        vm.AlwaysOnFloor = true;
        Assert.True(store.Load().AlwaysOnFloor);

        var next = NewViewModel(new UserSettingsStore(store.Path));
        Assert.True(next.AlwaysOnFloor);
    }

    [UiFact]
    public async Task The_settings_dialog_can_switch_the_option_and_it_takes_effect_at_once()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await SetField(vm, vm.Properties!.Position[2], "10");

        var dialog = new SettingsViewModel(vm.Document.Settings.Clone(), vm.CurrentUserSettings());
        Assert.False(dialog.AlwaysOnFloor);
        dialog.AlwaysOnFloor = true;
        var result = dialog.Result()!;
        Assert.True(result.User.AlwaysOnFloor);

        vm.ApplySettings(result);
        await Idle(vm);

        Assert.True(vm.AlwaysOnFloor);
        Assert.Equal(0, Bottom(vm, Child(vm, 0).Id), 3);
    }
}
