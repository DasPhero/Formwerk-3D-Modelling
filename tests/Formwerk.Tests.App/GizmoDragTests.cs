using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>What dragging the move and rotate handles does to the document (F-TRF-01, F-ROT-01/02, F-SNP-05, F-FLR-01).</summary>
public class GizmoDragTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static GizmoDrag Move(GizmoHandleKind handle, float x, float y, float z, bool suppressed = false) =>
        new(handle, new Vector3(x, y, z), 0, suppressed);

    private static GizmoDrag Turn(GizmoHandleKind handle, double degrees, bool suppressed = false) =>
        new(handle, Vector3.Zero, degrees, suppressed);

    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    private static Vector3 WorldCenter(MainViewModel vm, Guid id)
    {
        var b = vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();
        return b.Center;
    }

    // ---- pivot -----------------------------------------------------------------------------------

    [UiFact]
    public async Task Pivot_is_the_center_of_the_selected_object()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        AssertNear(new Vector3(0, 0, 10), vm.GizmoPivot!.Value);
    }

    [UiFact]
    public async Task Pivot_is_the_center_of_all_selected_objects_and_disappears_without_selection()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        vm.SelectOnly([a.Id, b.Id]);
        var center = (WorldCenter(vm, a.Id) + WorldCenter(vm, b.Id)) / 2;

        var bounds = vm.GizmoPivot!.Value;
        Assert.Equal(center.X, bounds.X, 3); // symmetric boxes: the middle between both centres
        Assert.Equal(center.Z, bounds.Z, 3);

        vm.SelectOnly([]);
        Assert.Null(vm.GizmoPivot);
    }

    [UiFact]
    public async Task Pivot_follows_a_change_of_the_object()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Position[0].Text = "+50";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(50, vm.GizmoPivot!.Value.X, 3);
    }

    // ---- moving ----------------------------------------------------------------------------------

    [UiFact]
    public async Task Dragging_an_arrow_moves_live_without_a_history_entry_until_release()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var entries = vm.HistoryEntries.Count;
        var startX = box.Transform.Position.X;

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        var text = vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 12.4f, 0, 0));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(startX + 12, box.Transform.Position.X, 3); // snapped to the 1 mm grid
        Assert.Equal(0, box.Transform.Position.Y);
        Assert.Equal(0, box.Transform.Position.Z);
        Assert.Equal(entries, vm.HistoryEntries.Count); // still a preview
        Assert.NotNull(vm.PreviewTransforms); // the view shows it by moving the meshes it has (see DragPreviewTests)
        Assert.Contains("X 12", text);
        Assert.EndsWith("mm", text);

        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Equal(startX + 12, WorldCenter(vm, box.Id).X, 2); // the calculated result has it
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Move Box", vm.HistoryEntries[^1].Name);
        Assert.Equal(startX + 12, box.Transform.Position.X, 3);
        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public async Task A_whole_drag_is_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.BeginDrag(GizmoHandleKind.MoveY);
        for (var i = 1; i <= 20; i++) vm.UpdateDrag(Move(GizmoHandleKind.MoveY, 0, i, 0));
        vm.CommitDrag();
        Assert.Equal(20, box.Transform.Position.Y, 3);

        vm.UndoCommand.Execute(null);

        Assert.Equal(0, box.Transform.Position.Y, 3);
        Assert.Single(vm.Document.Root.Children);
        Assert.Equal("Add Box", vm.HistoryEntries[vm.HistorySelectedIndex].Name);
    }

    [Theory]
    [InlineData(12.4f, 1.0, false, 12.0)]
    [InlineData(12.6f, 1.0, false, 13.0)]
    [InlineData(12.4f, 5.0, false, 10.0)]
    [InlineData(13.0f, 5.0, false, 15.0)]
    [InlineData(1.34f, 0.5, false, 1.5)]
    [InlineData(12.4f, 1.0, true, 12.4)]   // Alt held: no snapping
    public async Task Moves_snap_to_the_grid_unless_suppressed(float delta, double step, bool suppressed, double expected)
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.GridStep = step;
        vm.BeginDrag(GizmoHandleKind.MoveX);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, delta, 0, 0, suppressed));

        Assert.Equal(expected, Child(vm, 0).Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Switching_the_grid_off_gives_exact_values()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.SnapToGrid = false;
        vm.BeginDrag(GizmoHandleKind.MoveX);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.345f, 0, 0));

        Assert.Equal(7.345, Child(vm, 0).Transform.Position.X, 3);
    }

    // F-SNP-05: with a 5 mm grid the positions are multiples of 5, whatever the starting position was.
    [UiFact]
    public async Task Snapping_applies_to_the_resulting_position_not_to_the_distance()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "2.3";
        await vm.WaitForEvaluationAsync();
        vm.GridStep = 5;
        vm.BeginDrag(GizmoHandleKind.MoveX);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 11, 0, 0)); // 2.3 + 11 = 13.3 -> 15

        Assert.Equal(15, Child(vm, 0).Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Plane_handles_move_in_two_axes_and_never_in_the_third()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.BeginDrag(GizmoHandleKind.MovePlaneXY);

        vm.UpdateDrag(Move(GizmoHandleKind.MovePlaneXY, 3.2f, -4.7f, 9f));

        var p = Child(vm, 0).Transform.Position;
        Assert.Equal(3, p.X, 3);
        Assert.Equal(-5, p.Y, 3);
        Assert.Equal(0, p.Z, 3);
    }

    [UiFact]
    public async Task Axis_handles_only_snap_their_own_axis()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[1].Text = "0.37";
        await vm.WaitForEvaluationAsync();
        vm.BeginDrag(GizmoHandleKind.MoveX);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 5, 0, 0));

        Assert.Equal(0.37, Child(vm, 0).Transform.Position.Y, 3); // Y was not snapped to the grid
    }

    [UiFact]
    public async Task Several_selected_objects_move_together_as_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        var ax = a.Transform.Position.X;
        var bx = b.Transform.Position.X;
        vm.SelectOnly([a.Id, b.Id]);
        var entries = vm.HistoryEntries.Count;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 10, 0, 0));
        vm.CommitDrag();

        Assert.Equal(ax + 10, a.Transform.Position.X, 3);
        Assert.Equal(bx + 10, b.Transform.Position.X, 3);
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Move 2 objects", vm.HistoryEntries[^1].Name);

        vm.UndoCommand.Execute(null);
        Assert.Equal(ax, a.Transform.Position.X, 3);
        Assert.Equal(bx, b.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Escape_restores_the_start_without_a_history_entry()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var entries = vm.HistoryEntries.Count;
        var before = Child(vm, 0).Transform;

        vm.BeginDrag(GizmoHandleKind.MoveZ);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveZ, 0, 0, 33));
        Assert.NotEqual(before, Child(vm, 0).Transform);
        vm.CancelDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Equal(before, Child(vm, 0).Transform);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Equal(0, WorldCenter(vm, Child(vm, 0).Id).X, 3);
        Assert.Equal(10, WorldCenter(vm, Child(vm, 0).Id).Z, 3);
    }

    [UiFact]
    public async Task A_drag_that_changes_nothing_adds_no_history_entry()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var entries = vm.HistoryEntries.Count;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 0.2f, 0, 0)); // snaps back to 0
        vm.CommitDrag();

        Assert.Equal(entries, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task Locked_objects_and_empty_selections_cannot_be_dragged()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Child(vm, 0).Locked = true;
        Assert.False(vm.BeginDrag(GizmoHandleKind.MoveX));

        Child(vm, 0).Locked = false;
        vm.SelectOnly([]);
        Assert.False(vm.BeginDrag(GizmoHandleKind.MoveX));
    }

    [UiFact]
    public async Task The_object_list_is_left_alone_while_dragging()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var changes = 0;
        vm.Nodes.CollectionChanged += (_, _) => changes++;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        for (var i = 1; i <= 10; i++) vm.UpdateDrag(Move(GizmoHandleKind.MoveX, i, 0, 0));
        Assert.Equal(0, changes);
        Assert.Equal([Child(vm, 0).Id], vm.SelectedIds);
        vm.CommitDrag();
    }

    [UiFact]
    public async Task Properties_panel_shows_the_position_while_dragging()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 8, 0, 0));

        Assert.Equal("8", vm.Properties!.Position[0].Text);
        vm.CommitDrag();
    }

    // The handle moves along the world axis even when the object sits inside a turned group.
    [UiFact]
    public async Task Objects_inside_a_turned_group_move_along_the_world_axes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.SelectOnly([box.Id]);
        vm.GroupCommand.Execute(null);
        var group = Child(vm, 0);
        vm.SelectOnly([group.Id]);
        vm.Properties!.Rotation[2].Text = "90";
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([box.Id]);
        var before = TransformOps.WorldPosition(box);

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 10, 0, 0));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        AssertNear(before + new Vector3(10, 0, 0), TransformOps.WorldPosition(box), digits: 2);
        Assert.NotEqual(10, box.Transform.Position.X, 2); // the local value changed differently because the group is turned
    }

    // ---- rotating --------------------------------------------------------------------------------

    [Theory]
    [InlineData(40.0, 15.0, false, 45.0)]
    [InlineData(40.0, 15.0, true, 40.0)]   // Shift held: no snapping
    [InlineData(40.0, 45.0, false, 45.0)]
    [InlineData(20.0, 45.0, false, 0.0)]
    [InlineData(-100.0, 90.0, false, -90.0)]
    [InlineData(37.3, 1.0, false, 37.0)]
    public async Task Rotations_snap_to_the_angle_step_unless_suppressed(double dragged, double step, bool suppressed, double expected)
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.AngleStep = step;
        vm.BeginDrag(GizmoHandleKind.RotateZ);

        var text = vm.UpdateDrag(Turn(GizmoHandleKind.RotateZ, dragged, suppressed));

        Assert.Equal(expected, EulerAngles.FromQuaternion(Child(vm, 0).Transform.Rotation).Z, 2);
        Assert.Equal(expected.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "°", text);
    }

    [UiFact]
    public async Task Angle_snapping_can_be_switched_off()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.SnapAngles = false;
        vm.BeginDrag(GizmoHandleKind.RotateX);

        vm.UpdateDrag(Turn(GizmoHandleKind.RotateX, 12.34));

        Assert.Equal(12.34, EulerAngles.FromQuaternion(Child(vm, 0).Transform.Rotation).X, 2);
    }

    [UiFact]
    public async Task Rotating_commits_one_entry_and_shows_the_angle_in_the_properties()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var entries = vm.HistoryEntries.Count;

        vm.BeginDrag(GizmoHandleKind.RotateZ);
        vm.UpdateDrag(Turn(GizmoHandleKind.RotateZ, 44));
        vm.CommitDrag();

        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Rotate Box", vm.HistoryEntries[^1].Name);
        Assert.Equal("45", vm.Properties!.Rotation[2].Text);
    }

    [UiFact]
    public async Task A_single_object_turns_in_place_around_its_center()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "40";
        vm.Properties.Parameters[0].Text = "30"; // width 30, depth 20: turning changes the footprint, not the center
        await vm.WaitForEvaluationAsync();
        var center = WorldCenter(vm, Child(vm, 0).Id);

        vm.BeginDrag(GizmoHandleKind.RotateZ);
        vm.UpdateDrag(Turn(GizmoHandleKind.RotateZ, 90));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        AssertNear(center, WorldCenter(vm, Child(vm, 0).Id), digits: 2);
        var size = vm.ViewportItems.Single().Mesh.ComputeBounds().Size;
        Assert.Equal(20, size.X, 2);
        Assert.Equal(30, size.Y, 2);
    }

    [UiFact]
    public async Task Several_objects_turn_around_their_common_center()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        vm.SelectOnly([a.Id]);
        vm.Properties!.Position[0].Text = "-30";
        vm.SelectOnly([b.Id]);
        vm.Properties!.Position[0].Text = "30";
        vm.SelectOnly([a.Id, b.Id]);
        await vm.WaitForEvaluationAsync();
        var pivot = vm.GizmoPivot!.Value;
        Assert.Equal(-30, TransformOps.WorldPosition(a).X, 2);
        Assert.Equal(30, TransformOps.WorldPosition(b).X, 2);
        Assert.Equal(0, pivot.X, 2);

        Assert.True(vm.BeginDrag(GizmoHandleKind.RotateZ));
        vm.UpdateDrag(Turn(GizmoHandleKind.RotateZ, 90));
        vm.CommitDrag();

        // a was left of the pivot, b right of it: after a quarter turn they are below and above it
        AssertNear(new Vector3(pivot.X, pivot.Y - 30, 0), TransformOps.WorldPosition(a), digits: 2);
        AssertNear(new Vector3(pivot.X, pivot.Y + 30, 0), TransformOps.WorldPosition(b), digits: 2);
        Assert.Equal("Rotate 2 objects", vm.HistoryEntries[^1].Name);
    }

    // ---- tools -----------------------------------------------------------------------------------

    [UiFact]
    public void Move_is_the_default_tool_and_the_tool_flags_are_exclusive()
    {
        var vm = NewViewModel();
        Assert.Equal(ViewportTool.Move, vm.ActiveTool);
        Assert.True(vm.IsMoveTool);

        vm.IsRotateTool = true;
        Assert.Equal(ViewportTool.Rotate, vm.ActiveTool);
        Assert.False(vm.IsMoveTool);
        Assert.False(vm.IsSelectTool);

        vm.SetToolCommand.Execute(ViewportTool.Select);
        Assert.True(vm.IsSelectTool);
    }

    [UiFact]
    public void Unchecking_the_active_tool_keeps_it_active()
    {
        var vm = NewViewModel();
        var raised = new System.Collections.Concurrent.ConcurrentQueue<string?>(); // the first evaluation reports from a background thread
        vm.PropertyChanged += (_, e) => raised.Enqueue(e.PropertyName);

        vm.IsMoveTool = false; // what clicking an already checked toggle button does

        Assert.Equal(ViewportTool.Move, vm.ActiveTool);
        Assert.Contains(nameof(MainViewModel.IsMoveTool), raised); // the button is checked again
    }

    [UiFact]
    public async Task The_view_model_is_its_own_gizmo_target()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Assert.Same(vm, vm.GizmoTargetObject);
    }

    // ---- drop to floor ---------------------------------------------------------------------------

    [UiFact]
    public async Task Drop_to_floor_puts_the_lowest_point_on_the_plate()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[2].Text = "15";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(15, vm.ViewportItems.Single().Mesh.ComputeBounds().Min.Z, 3);

        vm.DropToFloorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(0, vm.ViewportItems.Single().Mesh.ComputeBounds().Min.Z, 3);
        Assert.Equal("Drop to floor", vm.HistoryEntries[^1].Name);
        vm.UndoCommand.Execute(null);
        Assert.Equal(15, Child(vm, 0).Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task Drop_to_floor_works_after_rotating()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Rotation[0].Text = "45"; // a tilted box: its lowest corner is now below the plate
        await vm.WaitForEvaluationAsync();
        Assert.True(vm.ViewportItems.Single().Mesh.ComputeBounds().Min.Z < -1);

        vm.DropToFloorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(0, vm.ViewportItems.Single().Mesh.ComputeBounds().Min.Z, 2);
    }

    [UiFact]
    public async Task Drop_to_floor_handles_every_selected_object_in_one_step_and_skips_those_already_down()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        vm.SelectOnly([a.Id]);
        vm.Properties!.Position[2].Text = "10";
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([a.Id, b.Id]);
        var entries = vm.HistoryEntries.Count;

        vm.DropToFloorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.All(vm.ViewportItems, i => Assert.Equal(0, i.Mesh.ComputeBounds().Min.Z, 3));
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal(0, b.Transform.Position.Z, 3); // untouched
    }

    [UiFact]
    public async Task Drop_to_floor_without_anything_to_do_adds_no_entry()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var entries = vm.HistoryEntries.Count;

        vm.DropToFloorCommand.Execute(null);

        Assert.Equal(entries, vm.HistoryEntries.Count);
    }

    [UiFact]
    public void Drop_to_floor_needs_a_selection()
    {
        var vm = NewViewModel();
        Assert.False(vm.DropToFloorCommand.CanExecute(null));
    }
}
