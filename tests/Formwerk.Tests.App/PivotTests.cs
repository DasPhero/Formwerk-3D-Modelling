using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The pivot of the Rotate tool: center, corner of the bounding box, a picked point or the world origin (F-ROT-03).</summary>
public class PivotTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    /// <summary>A box (x and y from -10 to 10, z from 0 to 20) with the Rotate tool, selected.</summary>
    private static async Task<(MainViewModel Vm, Node Box)> RotateWithBox()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.ActiveTool = ViewportTool.Rotate;
        vm.SelectOnly([box.Id]);
        return (vm, box);
    }

    private static async Task MoveTo(MainViewModel vm, Node node, Vector3 position)
    {
        node.Transform = Transform.FromPosition(position);
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
    }

    private static void AssertNear(Vector3 expected, Vector3? actual, float tolerance = 1e-3f)
    {
        Assert.NotNull(actual);
        Assert.True(Vector3.Distance(expected, actual.Value) < tolerance, $"expected {expected}, got {actual}");
    }

    private static Bounds WorldBounds(MainViewModel vm, Node node)
    {
        var parts = vm.ViewportItems.Where(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static async Task Rotate(MainViewModel vm, GizmoHandleKind handle, double degrees)
    {
        Assert.True(vm.BeginDrag(handle));
        vm.UpdateDrag(new GizmoDrag(handle, Vector3.Zero, degrees, true));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();
    }

    // ---- the choices -----------------------------------------------------------------------------

    [UiFact]
    public async Task By_default_the_pivot_is_the_center_of_the_selection()
    {
        var (vm, _) = await RotateWithBox();

        Assert.Equal(0, vm.PivotMode);
        AssertNear(new Vector3(0, 0, 10), vm.GizmoPivot);
        Assert.Equal(4, vm.PivotModes.Count);
        Assert.Equal(8, vm.PivotCorners.Count);
    }

    [UiFact]
    public async Task Every_corner_of_the_bounding_box_can_be_the_pivot()
    {
        var (vm, _) = await RotateWithBox();
        vm.PivotMode = 1;

        for (var corner = 0; corner < 8; corner++)
        {
            vm.PivotCorner = corner;
            AssertNear(PivotChoice.Corner(new Vector3(-10, -10, 0), new Vector3(10, 10, 20), corner), vm.GizmoPivot);
        }
    }

    [UiFact]
    public async Task The_world_origin_can_be_the_pivot()
    {
        var (vm, box) = await RotateWithBox();
        await MoveTo(vm, box, new Vector3(30, 0, 0));

        vm.PivotMode = 3;

        AssertNear(Vector3.Zero, vm.GizmoPivot);
    }

    [UiFact]
    public async Task The_choice_counts_only_for_the_rotate_tool()
    {
        var (vm, box) = await RotateWithBox();
        await MoveTo(vm, box, new Vector3(30, 0, 0));
        vm.PivotMode = 3;

        vm.ActiveTool = ViewportTool.Move;
        AssertNear(new Vector3(30, 0, 10), vm.GizmoPivot);
        Assert.False(vm.IsPivotVisible);

        vm.ActiveTool = ViewportTool.Rotate;
        AssertNear(Vector3.Zero, vm.GizmoPivot);
        Assert.True(vm.IsPivotVisible);
    }

    [UiFact]
    public async Task The_panel_needs_the_rotate_tool_and_a_selection()
    {
        var (vm, _) = await RotateWithBox();
        Assert.True(vm.IsPivotVisible);

        vm.SelectOnly([]);

        Assert.False(vm.IsPivotVisible);
        Assert.Null(vm.GizmoPivot);
    }

    // ---- a picked point --------------------------------------------------------------------------

    [UiFact]
    public async Task Choosing_a_point_starts_the_picking_and_a_click_sets_the_snapped_point()
    {
        var (vm, box) = await RotateWithBox();

        vm.PivotMode = 2;

        Assert.True(vm.IsPickingPivot);
        Assert.True(vm.IsFacePickActive);
        AssertNear(new Vector3(0, 0, 10), vm.GizmoPivot); // no point yet: the center

        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(-9.5f, -9.4f, 20), Vector3.UnitZ));

        Assert.False(vm.IsPickingPivot);
        AssertNear(new Vector3(-10, -10, 20), vm.CustomPivot);
        AssertNear(new Vector3(-10, -10, 20), vm.GizmoPivot); // the corner of the top face
    }

    [UiFact]
    public async Task Alt_takes_the_point_exactly_as_clicked()
    {
        var (vm, box) = await RotateWithBox();
        vm.PivotMode = 2;

        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(-9.5f, -9.4f, 20), Vector3.UnitZ, NoSnap: true));

        AssertNear(new Vector3(-9.5f, -9.4f, 20), vm.GizmoPivot);
    }

    [UiFact]
    public async Task The_picked_point_stays_when_another_choice_is_made_and_chosen_again()
    {
        var (vm, box) = await RotateWithBox();
        vm.PivotMode = 2;
        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(-9.5f, -9.4f, 20), Vector3.UnitZ));

        vm.PivotMode = 0;
        AssertNear(new Vector3(0, 0, 10), vm.GizmoPivot);
        vm.PivotMode = 2;

        Assert.False(vm.IsPickingPivot); // a point is known, nothing to pick
        AssertNear(new Vector3(-10, -10, 20), vm.GizmoPivot);
    }

    [UiFact]
    public async Task Esc_a_new_choice_or_another_tool_end_the_picking()
    {
        var (vm, _) = await RotateWithBox();

        vm.PivotMode = 2;
        vm.FacePickedCommand.Execute(null);
        Assert.False(vm.IsPickingPivot);
        AssertNear(new Vector3(0, 0, 10), vm.GizmoPivot); // still no point

        vm.PivotMode = 0;
        vm.PivotMode = 2;
        Assert.True(vm.IsPickingPivot);
        vm.PivotMode = 1;
        Assert.False(vm.IsPickingPivot);

        vm.PivotMode = 2;
        Assert.True(vm.IsPickingPivot);
        vm.ActiveTool = ViewportTool.Move;
        Assert.False(vm.IsPickingPivot);
    }

    [UiFact]
    public async Task Another_pick_mode_ends_the_pivot_picking_and_the_other_way_round()
    {
        var (vm, _) = await RotateWithBox();
        vm.PivotMode = 2;
        Assert.True(vm.IsPickingPivot);

        vm.MeasureCommand.Execute(null);
        Assert.False(vm.IsPickingPivot);
        Assert.True(vm.IsMeasuring);

        vm.PivotPickCommand.Execute(null);
        Assert.True(vm.IsPickingPivot);
        Assert.False(vm.IsMeasuring);
    }

    // ---- turning ---------------------------------------------------------------------------------

    [UiFact]
    public async Task A_rotation_about_a_corner_leaves_that_corner_where_it_was()
    {
        var (vm, box) = await RotateWithBox();
        vm.PivotMode = 1;
        vm.PivotCorner = 0; // x min, y min, z min: (-10, -10, 0)
        var steps = vm.HistoryEntries.Count;

        await Rotate(vm, GizmoHandleKind.RotateZ, 90);

        var vertices = vm.ViewportItems.Where(i => i.NodeId == box.Id).SelectMany(i => i.Mesh.Vertices.ToArray()).ToList();
        Assert.Contains(vertices, v => Vector3.Distance(v, new Vector3(-10, -10, 0)) < 1e-3f); // the corner did not move
        var center = WorldBounds(vm, box).Center;
        Assert.Equal(-20, center.X, 2); // the middle swung round the corner
        Assert.Equal(0, center.Y, 2);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(0, WorldBounds(vm, box).Center.X, 2);
    }

    [UiFact]
    public async Task A_rotation_about_the_world_origin_swings_the_object_round_it()
    {
        var (vm, box) = await RotateWithBox();
        await MoveTo(vm, box, new Vector3(30, 0, 0));
        vm.PivotMode = 3;

        await Rotate(vm, GizmoHandleKind.RotateZ, 90);

        var center = WorldBounds(vm, box).Center;
        Assert.Equal(0, center.X, 2);
        Assert.Equal(30, center.Y, 2);
    }

    [UiFact]
    public async Task A_rotation_about_a_picked_point_keeps_that_point()
    {
        var (vm, box) = await RotateWithBox();
        vm.PivotMode = 2;
        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(9.5f, 9.4f, 20), Vector3.UnitZ)); // snaps to the corner (10, 10, 20)

        await Rotate(vm, GizmoHandleKind.RotateZ, 180);

        var vertices = vm.ViewportItems.Where(i => i.NodeId == box.Id).SelectMany(i => i.Mesh.Vertices.ToArray()).ToList();
        Assert.Contains(vertices, v => Vector3.Distance(v, new Vector3(10, 10, 20)) < 1e-3f);
        Assert.Equal(20, WorldBounds(vm, box).Center.X, 2);
        Assert.Equal(20, WorldBounds(vm, box).Center.Y, 2);
    }
}
