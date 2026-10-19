using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Resizing with the scale handles (F-SCL-01/02/03/05/06).</summary>
public class ScaleDragTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task<MainViewModel> WithScaleTool(string shape = "box")
    {
        var vm = NewViewModel();
        await Add(vm, shape);
        vm.ActiveTool = ViewportTool.Scale;
        return vm;
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static PrimitiveNode Prim(MainViewModel vm, int index = 0) => (PrimitiveNode)Child(vm, index);

    /// <summary>Drags a handle by the given factor, waits for the preview and returns the readout text.</summary>
    private static async Task<string> Drag(MainViewModel vm, GizmoHandleKind handle, double factor, bool fromCenter = false, bool suppressed = false, bool commit = true)
    {
        Assert.True(vm.BeginDrag(handle));
        var text = vm.UpdateDrag(new GizmoDrag(handle, Vector3.Zero, 0, suppressed, factor, fromCenter));
        await vm.WaitForEvaluationAsync();
        if (commit) vm.CommitDrag();
        return text;
    }

    private static Geometry.Bounds BoundsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();

    private static void AssertBounds(Geometry.Bounds bounds, (float X, float Y, float Z) min, (float X, float Y, float Z) max, int digits = 2)
    {
        Assert.Equal(min.X, bounds.Min.X, digits);
        Assert.Equal(min.Y, bounds.Min.Y, digits);
        Assert.Equal(min.Z, bounds.Min.Z, digits);
        Assert.Equal(max.X, bounds.Max.X, digits);
        Assert.Equal(max.Y, bounds.Max.Y, digits);
        Assert.Equal(max.Z, bounds.Max.Z, digits);
    }

    // ---- the box ---------------------------------------------------------------------------------

    [UiFact]
    public async Task Scale_tool_shows_the_box_of_the_selected_object()
    {
        var vm = await WithScaleTool();

        var box = vm.GizmoScaleBox!.Value;
        Assert.Equal(new Vector3(-10, -10, 0), box.Min);
        Assert.Equal(new Vector3(10, 10, 20), box.Max);
        Assert.Equal(Vector3.Zero, box.Origin);
        Assert.True(vm.IsScaleTool);
    }

    [UiFact]
    public async Task Other_tools_and_multi_selections_have_no_scale_box()
    {
        var vm = await WithScaleTool();
        vm.ActiveTool = ViewportTool.Move;
        Assert.Null(vm.GizmoScaleBox);

        vm.ActiveTool = ViewportTool.Scale;
        await Add(vm, "sphere");
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));

        Assert.Null(vm.GizmoScaleBox);
        Assert.Contains("one object at a time", vm.HintText);
        Assert.False(vm.BeginDrag(GizmoHandleKind.ScaleXMax));

        vm.SelectOnly([Child(vm, 0).Id]);
        Assert.NotNull(vm.GizmoScaleBox);
        Assert.DoesNotContain("one object at a time", vm.HintText);
    }

    [UiFact]
    public async Task The_box_is_oriented_like_the_object()
    {
        var vm = await WithScaleTool();
        vm.Properties!.Parameters[0].Text = "40"; // width 40, depth 20
        vm.Properties.Rotation[2].Text = "90";
        await vm.WaitForEvaluationAsync();

        var box = vm.GizmoScaleBox!.Value;

        Assert.Equal(40, box.Size.X, 2); // still 40 along the object's own X axis
        Assert.Equal(20, box.Size.Y, 2);
        var xAxis = box.AxisWorld(0);
        Assert.Equal(1f, xAxis.Y, 3); // which now points along world Y
    }

    [UiFact]
    public async Task A_hole_that_is_only_shown_as_a_ghost_can_be_scaled()
    {
        var vm = await WithScaleTool();
        vm.ToggleHoleCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.DoesNotContain(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
        vm.ActiveTool = ViewportTool.Move;
        vm.ActiveTool = ViewportTool.Scale;

        Assert.NotNull(vm.GizmoScaleBox);
        Assert.True(vm.BeginDrag(GizmoHandleKind.ScaleXMax));
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.ScaleXMax, Vector3.Zero, 0, false, 2));
        vm.CommitDrag();

        Assert.Equal("40", Prim(vm).Parameters["width"]);
    }

    // ---- faces: which side stays ----------------------------------------------------------------

    [UiFact]
    public async Task Pulling_the_plus_face_keeps_the_minus_side_in_place()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("30", Prim(vm).Parameters["width"]);
        Assert.Equal("20", Prim(vm).Parameters["depth"]);
        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-10, -10, 0), (20, 10, 20));
        Assert.Equal(5, Child(vm, 0).Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Pulling_the_minus_face_keeps_the_plus_side_in_place()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleXMin, 1.5);
        await vm.WaitForEvaluationAsync();

        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-20, -10, 0), (10, 10, 20));
        Assert.Equal(-5, Child(vm, 0).Transform.Position.X, 3);
    }

    // F-SCL-03: Ctrl scales from the center.
    [UiFact]
    public async Task Ctrl_scales_from_the_center()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5, fromCenter: true);
        await vm.WaitForEvaluationAsync();

        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-15, -10, 0), (15, 10, 20));
        Assert.Equal(0, Child(vm, 0).Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Y_and_Z_faces_change_depth_and_height()
    {
        var vm = await WithScaleTool();
        await Drag(vm, GizmoHandleKind.ScaleYMax, 1.5);
        await vm.WaitForEvaluationAsync();
        Assert.Equal("30", Prim(vm).Parameters["depth"]);

        vm.ActiveTool = ViewportTool.Scale;
        await Drag(vm, GizmoHandleKind.ScaleZMax, 2);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("40", Prim(vm).Parameters["height"]);
        var b = BoundsOf(vm, Child(vm, 0).Id);
        Assert.Equal(0, b.Min.Z, 2); // the bottom stays on the plate
        Assert.Equal(40, b.Max.Z, 2);
    }

    [UiFact]
    public async Task Pulling_the_bottom_face_keeps_the_top_in_place()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleZMin, 2);
        await vm.WaitForEvaluationAsync();

        var b = BoundsOf(vm, Child(vm, 0).Id);
        Assert.Equal(-20, b.Min.Z, 2);
        Assert.Equal(20, b.Max.Z, 2);
        Assert.Equal(-20, Child(vm, 0).Transform.Position.Z, 3);
    }

    // ---- corners and proportional mode ------------------------------------------------------------

    // F-SCL-01: the corner handle always scales proportionally.
    [UiFact]
    public async Task A_corner_scales_all_three_axes_and_keeps_the_opposite_corner()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("40", Prim(vm).Parameters["width"]);
        Assert.Equal("40", Prim(vm).Parameters["depth"]);
        Assert.Equal("40", Prim(vm).Parameters["height"]);
        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-10, -10, 0), (30, 30, 40));
    }

    [UiFact]
    public async Task The_corner_opposite_to_the_dragged_one_stays_whichever_corner_is_used()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleCorner0, 2); // min, min, min: the max corner (10, 10, 20) must stay
        await vm.WaitForEvaluationAsync();

        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-30, -30, -20), (10, 10, 20));
    }

    [UiFact]
    public async Task Corner_with_Ctrl_scales_around_the_center()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2, fromCenter: true);
        await vm.WaitForEvaluationAsync();

        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-20, -20, -10), (20, 20, 30));
    }

    // F-SCL-02: in proportional mode a face handle scales all axes together.
    [UiFact]
    public async Task Proportional_mode_makes_a_face_handle_scale_everything()
    {
        var vm = await WithScaleTool();
        vm.ScaleLinked = true;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("40", Prim(vm).Parameters["width"]);
        Assert.Equal("40", Prim(vm).Parameters["depth"]);
        Assert.Equal("40", Prim(vm).Parameters["height"]);
        // the dragged axis keeps its opposite side, the other axes grow around the center
        AssertBounds(BoundsOf(vm, Child(vm, 0).Id), (-10, -20, -10), (30, 20, 30));
    }

    // ---- shapes ------------------------------------------------------------------------------------

    // F-SCL-05: a cylinder is resized through diameter and height.
    [UiFact]
    public async Task A_cylinder_has_one_diameter_for_both_horizontal_axes()
    {
        var vm = await WithScaleTool("cylinder");

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("30", Prim(vm).Parameters["diameter"]);
        Assert.Equal("20", Prim(vm).Parameters["height"]);
        var b = BoundsOf(vm, Child(vm, 0).Id);
        Assert.Equal(-10, b.Min.X, 1); // the dragged axis keeps its far side ...
        Assert.Equal(-15, b.Min.Y, 1); // ... the circle grows symmetrically in the other horizontal axis
        Assert.Equal(15, b.Max.Y, 1);
        Assert.Equal(0, b.Min.Z, 2);
        Assert.Equal(20, b.Max.Z, 2);
    }

    [UiFact]
    public async Task A_cylinder_changes_its_height_independently()
    {
        var vm = await WithScaleTool("cylinder");

        await Drag(vm, GizmoHandleKind.ScaleZMax, 2);

        Assert.Equal("40", Prim(vm).Parameters["height"]);
        Assert.Equal("20", Prim(vm).Parameters["diameter"]);
    }

    [UiFact]
    public async Task A_sphere_always_stays_a_sphere()
    {
        var vm = await WithScaleTool("sphere");

        await Drag(vm, GizmoHandleKind.ScaleYMax, 1.5);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("30", Prim(vm).Parameters["diameter"]);
        var size = BoundsOf(vm, Child(vm, 0).Id).Size;
        Assert.Equal(30, size.Z, 1);
        Assert.Equal(size.X, size.Y, 1);
        Assert.Equal(size.Y, size.Z, 1);
    }

    [UiFact]
    public async Task Scaling_a_shape_keeps_its_scale_value_at_one()
    {
        var vm = await WithScaleTool();
        await Drag(vm, GizmoHandleKind.ScaleXMax, 3);
        Assert.Equal(Vector3.One, Child(vm, 0).Transform.Scale);
    }

    [UiFact]
    public async Task Other_parameters_are_left_alone_including_expressions_and_the_segment_count()
    {
        var vm = await WithScaleTool("cylinder");
        vm.Document.Variables["Hoehe"] = "15";
        vm.Properties!.Parameters[1].Text = "Hoehe*2"; // height as an expression
        await vm.WaitForEvaluationAsync();
        vm.ActiveTool = ViewportTool.Scale;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2); // only the diameter

        Assert.Equal("Hoehe*2", Prim(vm).Parameters["height"]);
        Assert.Equal("64", Prim(vm).Parameters["segments"]);
        Assert.Equal("40", Prim(vm).Parameters["diameter"]);
    }

    [UiFact]
    public async Task A_scaled_dimension_replaces_its_variable_expression_by_a_number()
    {
        var vm = await WithScaleTool();
        vm.Document.Variables["Wand"] = "2";
        vm.Properties!.Parameters[0].Text = "Wand*10"; // width 20
        await vm.WaitForEvaluationAsync();
        vm.ActiveTool = ViewportTool.Scale;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5);

        Assert.Equal("30", Prim(vm).Parameters["width"]);
    }

    // ---- groups and booleans -----------------------------------------------------------------------

    [UiFact]
    public async Task A_group_is_scaled_through_its_scale_value_and_keeps_the_fixed_side()
    {
        var vm = await WithScaleTool();
        vm.SelectOnly([Child(vm, 0).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        vm.ActiveTool = ViewportTool.Move;
        vm.ActiveTool = ViewportTool.Scale;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, group.Transform.Scale.X, 3);
        Assert.Equal(1, group.Transform.Scale.Y, 3);
        AssertBounds(BoundsOf(vm, ((GroupNode)group).Children[0].Id), (-10, -10, 0), (30, 10, 20));
        Assert.Equal("Scale Group", vm.HistoryEntries[^1].Name);
    }

    [UiFact]
    public async Task A_shape_inside_a_scaled_group_scales_by_the_same_ratio_in_world_space()
    {
        var vm = await WithScaleTool();
        vm.SelectOnly([Child(vm, 0).Id]);
        vm.GroupCommand.Execute(null);
        var group = (GroupNode)Child(vm, 0);
        vm.SelectOnly([group.Id]);
        vm.Properties!.Position[0].Text = "0";
        // scale the group uniformly by 2 first (through its handle), then scale the inner shape
        vm.ActiveTool = ViewportTool.Scale;
        await vm.WaitForEvaluationAsync();
        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        await vm.WaitForEvaluationAsync();

        vm.SelectOnly([group.Children[0].Id]);
        vm.ActiveTool = ViewportTool.Move;
        vm.ActiveTool = ViewportTool.Scale;
        var before = BoundsOf(vm, group.Children[0].Id);
        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5);
        await vm.WaitForEvaluationAsync();

        var after = BoundsOf(vm, group.Children[0].Id);
        Assert.Equal(before.Size.X * 1.5f, after.Size.X, 1);
        Assert.Equal(before.Min.X, after.Min.X, 1); // the far side did not move in world space
        Assert.Equal(before.Size.Y, after.Size.Y, 1);
    }

    // ---- turned objects ----------------------------------------------------------------------------

    [UiFact]
    public async Task A_turned_box_is_scaled_along_its_own_axes()
    {
        var vm = await WithScaleTool();
        vm.Properties!.Rotation[2].Text = "90"; // local X now points along world Y
        await vm.WaitForEvaluationAsync();
        var before = BoundsOf(vm, Child(vm, 0).Id);
        vm.ActiveTool = ViewportTool.Move;
        vm.ActiveTool = ViewportTool.Scale;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2); // local X is the width
        await vm.WaitForEvaluationAsync();

        Assert.Equal("40", Prim(vm).Parameters["width"]);
        var after = BoundsOf(vm, Child(vm, 0).Id);
        Assert.Equal(before.Size.X, after.Size.X, 2);         // world X is the depth: unchanged
        Assert.Equal(before.Size.Y * 2, after.Size.Y, 2);     // world Y is the width: doubled
        Assert.Equal(before.Min.Y, after.Min.Y, 2);           // the minus side (in world: lower Y) stayed
    }

    // ---- snapping and limits ----------------------------------------------------------------------

    [Theory]
    [InlineData(1.234, 1.0, false, "25")]     // 24.68 -> 25
    [InlineData(1.234, 1.0, true, "24.68")]   // Alt held: exact
    [InlineData(1.234, 5.0, false, "25")]
    [InlineData(1.1, 5.0, false, "20")]       // 22 -> 20
    [InlineData(1.3, 0.5, false, "26")]
    public async Task The_new_size_snaps_to_the_grid_unless_suppressed(double factor, double step, bool suppressed, string expectedWidth)
    {
        var vm = await WithScaleTool();
        vm.GridStep = step;

        await Drag(vm, GizmoHandleKind.ScaleXMax, factor, suppressed: suppressed);

        Assert.Equal(expectedWidth, Prim(vm).Parameters["width"]);
    }

    [UiFact]
    public async Task Snapping_can_be_switched_off()
    {
        var vm = await WithScaleTool();
        vm.SnapToGrid = false;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.1234);

        Assert.Equal("22.468", Prim(vm).Parameters["width"]);
    }

    [UiFact]
    public async Task Objects_cannot_be_dragged_smaller_than_the_minimum_size()
    {
        var vm = await WithScaleTool();
        vm.SnapToGrid = false;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 0.0001);

        Assert.Equal("0.1", Prim(vm).Parameters["width"]);
    }

    [UiFact]
    public async Task With_the_grid_on_the_smallest_size_is_one_grid_step()
    {
        var vm = await WithScaleTool();
        vm.GridStep = 5;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 0.0001);

        Assert.Equal("5", Prim(vm).Parameters["width"]);
    }

    [UiFact]
    public async Task Sizes_are_limited_to_the_range_of_the_parameter()
    {
        var vm = await WithScaleTool();
        vm.SnapToGrid = false;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 900); // 18000 mm is more than the allowed 10000

        Assert.Equal("10000", Prim(vm).Parameters["width"]);
    }

    // ---- readout, preview, undo ------------------------------------------------------------------

    // F-SCL-06
    [UiFact]
    public async Task The_readout_shows_width_depth_and_height_with_one_decimal()
    {
        var vm = await WithScaleTool();

        var text = await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5, commit: false);

        Assert.Equal("30.0 × 20.0 × 20.0 mm", text);
        vm.CommitDrag();
    }

    [UiFact]
    public async Task The_readout_follows_every_axis_in_proportional_mode()
    {
        var vm = await WithScaleTool();

        var text = await Drag(vm, GizmoHandleKind.ScaleCorner7, 1.5, commit: false);

        Assert.Equal("30.0 × 30.0 × 30.0 mm", text);
        vm.CommitDrag();
    }

    [UiFact]
    public async Task The_properties_panel_and_the_view_show_the_size_while_dragging_without_a_history_entry()
    {
        var vm = await WithScaleTool();
        var entries = vm.HistoryEntries.Count;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.5, commit: false);

        Assert.Equal("30", vm.Properties!.Parameters[0].Text);
        Assert.Equal(30, BoundsOf(vm, Child(vm, 0).Id).Size.X, 2);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        vm.CommitDrag();
    }

    [UiFact]
    public async Task A_whole_scale_drag_is_one_undo_step_that_restores_size_and_position()
    {
        var vm = await WithScaleTool();
        var entries = vm.HistoryEntries.Count;
        var startTransform = Child(vm, 0).Transform;

        Assert.True(vm.BeginDrag(GizmoHandleKind.ScaleXMax));
        for (var i = 1; i <= 10; i++) vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.ScaleXMax, Vector3.Zero, 0, false, 1 + i * 0.1));
        vm.CommitDrag();

        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Scale Box", vm.HistoryEntries[^1].Name);
        Assert.Equal("40", Prim(vm).Parameters["width"]);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("20", Prim(vm).Parameters["width"]);
        Assert.Equal(startTransform, Child(vm, 0).Transform);
        Assert.Equal(20, BoundsOf(vm, Child(vm, 0).Id).Size.X, 2);

        vm.RedoCommand.Execute(null);
        Assert.Equal("40", Prim(vm).Parameters["width"]);
    }

    [UiFact]
    public async Task Escape_restores_size_and_position_without_a_history_entry()
    {
        var vm = await WithScaleTool();
        var entries = vm.HistoryEntries.Count;
        var startTransform = Child(vm, 0).Transform;

        await Drag(vm, GizmoHandleKind.ScaleXMin, 2, commit: false);
        Assert.Equal("40", Prim(vm).Parameters["width"]);
        vm.CancelDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Equal("20", Prim(vm).Parameters["width"]);
        Assert.Equal(startTransform, Child(vm, 0).Transform);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Equal(20, BoundsOf(vm, Child(vm, 0).Id).Size.X, 2);
    }

    [UiFact]
    public async Task A_drag_that_ends_where_it_started_adds_no_entry()
    {
        var vm = await WithScaleTool();
        var entries = vm.HistoryEntries.Count;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 1.0);

        Assert.Equal(entries, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task Locked_objects_and_broken_expressions_cannot_be_scaled()
    {
        var vm = await WithScaleTool();
        Child(vm, 0).Locked = true;
        Assert.False(vm.BeginDrag(GizmoHandleKind.ScaleXMax));

        Child(vm, 0).Locked = false;
        Prim(vm).Parameters["width"] = "1/0";
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        vm.ActiveTool = ViewportTool.Move;
        vm.ActiveTool = ViewportTool.Scale;
        Assert.False(vm.BeginDrag(GizmoHandleKind.ScaleXMax));
    }

    [UiFact]
    public async Task The_scale_box_follows_the_object_after_scaling_and_moving()
    {
        var vm = await WithScaleTool();

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2);
        await vm.WaitForEvaluationAsync();

        var box = vm.GizmoScaleBox!.Value;
        Assert.Equal(40, box.Size.X, 2);
        Assert.Equal(-10, box.Origin.X + box.Min.X, 2); // the minus side did not move in world space
        Assert.Equal(30, box.Origin.X + box.Max.X, 2);
        Assert.Equal(10, box.ToWorld(box.CenterLocal).X, 2);
    }

    [UiFact]
    public void The_tool_flags_include_scale()
    {
        var vm = NewViewModel();
        vm.IsScaleTool = true;
        Assert.Equal(ViewportTool.Scale, vm.ActiveTool);
        Assert.False(vm.IsMoveTool);
        vm.IsScaleTool = false; // unchecking the active tool keeps it
        Assert.Equal(ViewportTool.Scale, vm.ActiveTool);
    }
}
