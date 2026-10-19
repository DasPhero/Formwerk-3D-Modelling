using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Snapping a move to other objects, the floor and the bed center (F-SNP-02, F-SNP-06).</summary>
public class ObjectSnappingTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static GizmoDrag Move(GizmoHandleKind handle, float x, float y, float z, double worldPerPixel = 0.1, bool suppressed = false) =>
        new(handle, new Vector3(x, y, z), 0, suppressed, WorldPerPixel: worldPerPixel);

    /// <summary>
    /// Box A (20 x 20 x 20) at x = 0 and box B (width 25) at x = -30, B selected: B spans x -42.5 to -17.5, A spans -10 to 10.
    /// With the default 1 mm grid, flush with A would be x = -22.5, which the grid alone cannot produce.
    /// </summary>
    private static async Task<MainViewModel> TwoBoxes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "25";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(-30, Child(vm, 1).Transform.Position.X);
        return vm;
    }

    private static async Task<Node> DragAndCommit(MainViewModel vm, GizmoDrag drag)
    {
        Assert.True(vm.BeginDrag(drag.Handle));
        vm.UpdateDrag(drag);
        await vm.WaitForEvaluationAsync();
        vm.CommitDrag();
        return vm.Document.Root.Children[1];
    }

    // ---- snapping to other objects ---------------------------------------------------------------

    [UiFact]
    public async Task An_edge_snaps_flush_to_the_edge_of_another_object()
    {
        var vm = await TwoBoxes();

        var b = await DragAndCommit(vm, Move(GizmoHandleKind.MoveX, 7.3f, 0, 0)); // B's right edge would be at -10.2

        Assert.Equal(-22.5, b.Transform.Position.X, 3); // exactly flush with A's left edge at -10
    }

    [UiFact]
    public async Task The_guide_is_published_while_dragging_and_removed_afterwards()
    {
        var vm = await TwoBoxes();
        Assert.Null(vm.SnapGuides);

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));

        var guide = Assert.Single(vm.SnapGuides!);
        Assert.Equal(0, guide.Axis);
        Assert.Equal(-10, guide.Coordinate, 3);

        vm.CommitDrag();
        Assert.Null(vm.SnapGuides);
    }

    [UiFact]
    public async Task Cancelling_a_drag_removes_the_guides_too()
    {
        var vm = await TwoBoxes();
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));
        Assert.NotNull(vm.SnapGuides);

        vm.CancelDrag();

        Assert.Null(vm.SnapGuides);
    }

    // F-SNP-02 acceptance criterion: two boxes can be put exactly centered on each other without typing numbers.
    [UiFact]
    public async Task Two_boxes_can_be_centered_on_each_other_by_dragging()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "50.5"; // A: center at x = 50.5
        await vm.WaitForEvaluationAsync();
        await Add(vm, "box"); // B: first free spot, x = 0
        vm.Properties!.Parameters[0].Text = "30"; // wider than A, so only the centers can match
        await vm.WaitForEvaluationAsync();
        Assert.Equal(0, Child(vm, 1).Transform.Position.X);

        var b = await DragAndCommit(vm, Move(GizmoHandleKind.MoveX, 50.2f, 0, 0)); // B's center would be at 50.2

        Assert.Equal(50.5, b.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Alt_switches_all_snapping_off_so_only_what_was_dragged_remains()
    {
        var vm = await TwoBoxes();
        vm.SnapToGrid = false;

        var b = await DragAndCommit(vm, Move(GizmoHandleKind.MoveX, 7.3f, 0, 0, suppressed: true));

        Assert.Equal(-22.7, b.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task With_objects_switched_off_the_grid_applies_as_before()
    {
        var vm = await TwoBoxes();
        vm.SnapToObjects = false;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));

        Assert.Equal(-23, Child(vm, 1).Transform.Position.X, 3); // -22.7 rounded to the 1 mm grid
        Assert.Null(vm.SnapGuides);
        vm.CommitDrag();
    }

    // F-SNP-06: the tolerance is in screen pixels, so it follows the zoom.
    [UiFact]
    public async Task The_tolerance_is_measured_in_pixels_not_in_millimetres()
    {
        var vm = await TwoBoxes();

        // 0.5 mm from flush. Zoomed out (1 pixel = 0.1 mm): 10 px = 1 mm tolerance, it snaps.
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.0f, 0, 0, worldPerPixel: 0.1));
        Assert.Equal(-22.5, Child(vm, 1).Transform.Position.X, 3);
        vm.CancelDrag();

        // Zoomed in (1 pixel = 0.02 mm): the same 0.5 mm are 25 px, it does not snap and the grid applies.
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.0f, 0, 0, worldPerPixel: 0.02));
        Assert.Equal(-23, Child(vm, 1).Transform.Position.X, 3);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task Without_a_known_pixel_size_nothing_snaps_to_objects()
    {
        var vm = await TwoBoxes();

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0, worldPerPixel: 0));

        Assert.Equal(-23, Child(vm, 1).Transform.Position.X, 3);
        vm.CancelDrag();
    }

    // Object snap has priority over the grid; the other axis still uses the grid.
    [UiFact]
    public async Task A_plane_move_snaps_one_axis_to_an_object_and_the_other_to_the_grid()
    {
        var vm = await TwoBoxes();

        vm.BeginDrag(GizmoHandleKind.MovePlaneXY);
        vm.UpdateDrag(Move(GizmoHandleKind.MovePlaneXY, 7.3f, 3.4f, 0));

        var p = Child(vm, 1).Transform.Position;
        Assert.Equal(-22.5, p.X, 3); // flush with A
        Assert.Equal(3, p.Y, 3);     // nothing to snap to in Y: grid
        vm.CommitDrag();
    }

    [UiFact]
    public async Task Both_axes_of_a_plane_move_can_snap_and_show_two_guides()
    {
        var vm = await TwoBoxes();
        Child(vm, 1).Transform = Child(vm, 1).Transform with { Position = new Vector3(-30, 40, 0) };
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        vm.BeginDrag(GizmoHandleKind.MovePlaneXY);
        // X: flush with A as before. Y: B's center (y = 40 + 0.3) would be 0.3 mm from A's center line y = 0 after moving by -39.7 ...
        vm.UpdateDrag(Move(GizmoHandleKind.MovePlaneXY, 7.3f, -39.7f, 0));

        var p = Child(vm, 1).Transform.Position;
        Assert.Equal(-22.5, p.X, 3);
        Assert.Equal(0, p.Y, 3);
        Assert.Equal(2, vm.SnapGuides!.Count);
        vm.CommitDrag();
    }

    // ---- floor and bed center ----------------------------------------------------------------------

    [UiFact]
    public async Task Lifting_an_object_snaps_its_bottom_onto_the_floor()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[2].Text = "5.5";
        await vm.WaitForEvaluationAsync();

        // bottom would be at z = 0.6: the grid would round the position to 1, the floor wins
        vm.BeginDrag(GizmoHandleKind.MoveZ);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveZ, 0, 0, -4.9f));

        Assert.Equal(0, Child(vm, 0).Transform.Position.Z, 3);
        var guide = Assert.Single(vm.SnapGuides!);
        Assert.Equal(2, guide.Axis);
        vm.CommitDrag();
    }

    [UiFact]
    public async Task The_center_snaps_onto_the_bed_center_lines()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "30.7";
        await vm.WaitForEvaluationAsync();
        vm.SnapToGrid = false;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, -30.4f, 0, 0)); // center would be at x = 0.3

        Assert.Equal(0, Child(vm, 0).Transform.Position.X, 3);
        vm.CommitDrag();
    }

    // ---- what counts as a target ---------------------------------------------------------------------

    [UiFact]
    public async Task Hidden_objects_are_not_snap_targets()
    {
        var vm = await TwoBoxes();
        Child(vm, 0).Visible = false;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));

        Assert.Equal(-23, Child(vm, 1).Transform.Position.X, 3); // plain grid
        vm.CancelDrag();
    }

    [UiFact]
    public async Task An_object_does_not_snap_to_itself()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "40.3";
        await vm.WaitForEvaluationAsync();
        vm.SnapToGrid = false;

        // moving by 0.4: its own start edges are 0.4 mm away, but they are not targets
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 0.4f, 0, 0));

        Assert.Equal(40.7, Child(vm, 0).Transform.Position.X, 3);
        Assert.Null(vm.SnapGuides);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task Children_of_a_group_are_targets_for_objects_outside_it()
    {
        var vm = await TwoBoxes();
        // put A into a group: its box is still a target
        vm.SelectOnly([Child(vm, 0).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var b = vm.Document.Root.Children.OfType<PrimitiveNode>().Single();
        vm.SelectOnly([b.Id]);

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));

        Assert.Equal(-22.5, b.Transform.Position.X, 3);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task A_selection_of_several_objects_snaps_by_its_common_bounds()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");
        await Add(vm, "box");
        var a = Child(vm, 0); // x = 0 .. spans -10..10
        var b = Child(vm, 1); // x = -30
        var c = Child(vm, 2); // x = 30
        vm.SelectOnly([b.Id]);
        vm.Properties!.Position[1].Text = "100"; // move B far away in Y so only C and the pair matter
        vm.SelectOnly([a.Id]);
        vm.Properties!.Position[1].Text = "100";
        await vm.WaitForEvaluationAsync();
        // A and B (y = 100) form the selection: their union spans x -40..10; C spans x 20..40 at y = 0
        vm.SelectOnly([a.Id, b.Id]);

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 9.6f, 0, 0)); // the union's right edge (10 + 9.6 = 19.6) is 0.4 from C's left edge (20)

        Assert.Equal(10, a.Transform.Position.X, 3);   // both moved by exactly 10
        Assert.Equal(-20, b.Transform.Position.X, 3);
        vm.CancelDrag();
        Assert.Equal(30, c.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Rotating_and_scaling_do_not_use_object_snapping()
    {
        var vm = await TwoBoxes();
        vm.BeginDrag(GizmoHandleKind.RotateZ);
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.RotateZ, Vector3.Zero, 10, false, WorldPerPixel: 0.1));
        Assert.Null(vm.SnapGuides);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task Moving_with_the_gizmo_stays_one_undo_step_with_object_snapping()
    {
        var vm = await TwoBoxes();
        var entries = vm.HistoryEntries.Count;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        for (var i = 1; i <= 8; i++) vm.UpdateDrag(Move(GizmoHandleKind.MoveX, i, 0, 0));
        vm.CommitDrag();

        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        vm.UndoCommand.Execute(null);
        Assert.Equal(-30, Child(vm, 1).Transform.Position.X, 3);
    }

    [UiFact]
    public void Object_snapping_is_on_by_default()
    {
        Assert.True(NewViewModel().SnapToObjects);
    }

    // ---- snap point markers (F-SNP-01) -------------------------------------------------------------

    [UiFact]
    public async Task A_marker_shows_the_snap_point_while_dragging_and_goes_away_afterwards()
    {
        var vm = await TwoBoxes();
        Assert.Null(vm.SnapMarkers);

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0)); // B's right edge snaps to A's left edge at x = -10

        var marker = Assert.Single(vm.SnapMarkers!);
        Assert.Equal(-10, marker.X, 3);

        vm.CommitDrag();
        Assert.Null(vm.SnapMarkers);
    }

    [UiFact]
    public async Task Cancelling_a_drag_removes_the_marker_too()
    {
        var vm = await TwoBoxes();
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0));
        Assert.NotNull(vm.SnapMarkers);

        vm.CancelDrag();

        Assert.Null(vm.SnapMarkers);
    }

    [UiFact]
    public async Task Without_a_point_of_another_object_there_is_no_marker()
    {
        var vm = await TwoBoxes();
        vm.BeginDrag(GizmoHandleKind.MoveZ);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveZ, 0, 0, 50.2f)); // nothing else up there; no floor snap either

        Assert.Null(vm.SnapMarkers);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task The_marker_is_not_shown_when_snapping_is_suppressed()
    {
        var vm = await TwoBoxes();
        vm.BeginDrag(GizmoHandleKind.MoveX);

        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 7.3f, 0, 0, suppressed: true));

        Assert.Null(vm.SnapMarkers);
        vm.CancelDrag();
    }
}
