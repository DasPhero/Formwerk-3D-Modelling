using System.Numerics;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Face to face (F-SNP-03): click a face of the object that moves, then the face of the other one; a gap can be set.</summary>
public class FaceToFaceViewModelTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    /// <summary>Box A (20 mm cube, centered on the origin, X from -10 to 10) and box B (20 mm cube at X = 100, from 90 to 110).</summary>
    private static async Task<(MainViewModel Vm, Node A, Node B)> TwoBoxes()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var a = vm.Document.Root.Children[0];
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var b = vm.Document.Root.Children[1];
        vm.SelectOnly([b.Id]);
        vm.Properties!.Position[0].Text = "100";
        vm.Properties.Position[1].Text = "0";
        await vm.WaitForEvaluationAsync();
        return (vm, a, b);
    }

    private static Bounds BoxOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Select(i => i.Mesh.ComputeBounds())
            .Aggregate((x, y) => new Bounds(Vector3.Min(x.Min, y.Min), Vector3.Max(x.Max, y.Max)));

    private static FacePick RightOf(Node a) => new(a.Id, new Vector3(10, 0, 10), Vector3.UnitX);

    private static FacePick LeftOf(Node b) => new(b.Id, new Vector3(90, 0, 10), -Vector3.UnitX);

    private static async Task Join(MainViewModel vm, FacePick first, FacePick second)
    {
        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(first);
        vm.FacePickedCommand.Execute(second);
        await vm.WaitForEvaluationAsync();
    }

    [UiFact]
    public async Task The_mode_starts_with_the_first_step_and_excludes_the_other_face_modes()
    {
        var (vm, _, _) = await TwoBoxes();

        vm.FaceToFaceCommand.Execute(null);

        Assert.True(vm.IsPickingFaceToFace);
        Assert.True(vm.IsFacePickActive);
        Assert.False(vm.FaceToFaceHasFirst);
        Assert.Equal(Texts.Get("Hint.FaceToFaceFirst"), vm.HintText);

        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsPickingFaceToFace);

        vm.FaceToFaceCommand.Execute(null);
        vm.DrillCommand.Execute(null);
        Assert.False(vm.IsPickingFaceToFace);

        vm.FaceToFaceCommand.Execute(null);
        vm.LinkedMirrorAtFaceCommand.Execute(null);
        Assert.False(vm.IsPickingFaceToFace);
    }

    [UiFact]
    public async Task The_first_click_asks_for_the_second_face()
    {
        var (vm, a, _) = await TwoBoxes();
        vm.FaceToFaceCommand.Execute(null);

        vm.FacePickedCommand.Execute(RightOf(a));

        Assert.True(vm.IsPickingFaceToFace);
        Assert.True(vm.FaceToFaceHasFirst);
        Assert.Equal(Texts.Get("Hint.FaceToFaceSecond"), vm.HintText);
    }

    [UiFact]
    public async Task Esc_ends_the_mode_in_both_steps()
    {
        var (vm, a, _) = await TwoBoxes();
        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(null);
        Assert.False(vm.IsPickingFaceToFace);

        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(RightOf(a));
        vm.FacePickedCommand.Execute(null);

        Assert.False(vm.IsPickingFaceToFace);
        Assert.False(vm.FaceToFaceHasFirst);
        Assert.False(vm.IsFacePickActive);
    }

    [UiFact]
    public async Task Two_clicks_put_the_faces_flush_in_one_undo_step()
    {
        var (vm, a, b) = await TwoBoxes();
        var steps = vm.HistoryEntries.Count;

        await Join(vm, RightOf(a), LeftOf(b));

        Assert.Equal(BoxOf(vm, b).Min.X, BoxOf(vm, a).Max.X, 3); // flush
        Assert.Equal(80, BoxOf(vm, a).Center.X, 3);
        Assert.False(vm.IsPickingFaceToFace); // one shot
        Assert.Equal([a.Id], vm.SelectedIds); // the object that moved is selected
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal(100, BoxOf(vm, b).Center.X, 3); // the other object did not move

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(0, BoxOf(vm, a).Center.X, 3);
    }

    [UiFact]
    public async Task A_gap_of_0_2_is_kept_exactly()
    {
        var (vm, a, b) = await TwoBoxes();
        vm.FaceGap.Text = "0.2";

        await Join(vm, RightOf(a), LeftOf(b));

        Assert.Equal(0.2, BoxOf(vm, b).Min.X - BoxOf(vm, a).Max.X, 4);
    }

    [UiFact]
    public async Task A_negative_gap_is_an_overlap()
    {
        var (vm, a, b) = await TwoBoxes();
        vm.FaceGap.Text = "-1.5";

        await Join(vm, RightOf(a), LeftOf(b));

        Assert.Equal(-1.5, BoxOf(vm, b).Min.X - BoxOf(vm, a).Max.X, 4);
    }

    [UiFact]
    public async Task The_gap_may_be_a_formula_with_the_print_tolerance()
    {
        var (vm, a, b) = await TwoBoxes();
        vm.FaceGap.Text = "Tolerance";

        await Join(vm, RightOf(a), LeftOf(b));

        Assert.Equal(0.2, BoxOf(vm, b).Min.X - BoxOf(vm, a).Max.X, 4); // the default print tolerance
    }

    [UiFact]
    public async Task The_gap_stays_for_the_next_time()
    {
        var (vm, _, _) = await TwoBoxes();
        vm.FaceGap.Text = "0.3";

        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(null);

        Assert.Equal("0.3", vm.FaceGap.Text);
    }

    [UiFact]
    public async Task A_gap_that_is_not_a_number_is_refused_and_nothing_moves()
    {
        var (vm, a, b) = await TwoBoxes();
        var steps = vm.HistoryEntries.Count;
        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(RightOf(a));
        vm.FaceGap.Text = "wide"; // typed in the panel while the mode is on

        vm.FacePickedCommand.Execute(LeftOf(b));
        await vm.WaitForEvaluationAsync();

        Assert.NotNull(vm.FaceGap.Error);
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal(0, BoxOf(vm, a).Center.X, 3);
        Assert.True(vm.IsPickingFaceToFace); // the user can correct the gap and click again
    }

    [UiFact]
    public async Task A_face_of_the_other_kind_turns_the_object()
    {
        var (vm, a, b) = await TwoBoxes();

        // the top face of A (looks up) onto the left face of B (looks to -X): A is turned so that its top looks to +X
        await Join(vm, new FacePick(a.Id, new Vector3(0, 0, 20), Vector3.UnitZ), LeftOf(b));

        Assert.Equal(90, BoxOf(vm, a).Max.X, 3); // flush with the face at X = 90
        Assert.Equal(10, BoxOf(vm, a).Center.Z, 2); // the clicked point (the middle of the top) is now at the height 10 of the target point
    }

    [UiFact]
    public async Task The_second_click_on_the_same_object_is_not_a_target_and_says_so()
    {
        var (vm, a, _) = await TwoBoxes();
        vm.FaceToFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(RightOf(a));
        var steps = vm.HistoryEntries.Count;

        vm.FacePickedCommand.Execute(new FacePick(a.Id, new Vector3(-10, 0, 10), -Vector3.UnitX));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.True(vm.IsPickingFaceToFace);
        Assert.Contains(Texts.Get("Msg.FaceToFaceSame"), vm.StatusText);
    }

    [UiFact]
    public async Task A_locked_object_does_not_move_and_the_mode_ends()
    {
        var (vm, a, b) = await TwoBoxes();
        a.Locked = true;
        var steps = vm.HistoryEntries.Count;

        await Join(vm, RightOf(a), LeftOf(b));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.False(vm.IsPickingFaceToFace);
        Assert.Contains(a.Name, vm.StatusText);
    }

    [UiFact]
    public async Task A_part_of_a_group_moves_the_whole_group()
    {
        var (vm, a, b) = await TwoBoxes();
        vm.AddShapeCommand.Execute("sphere");
        await vm.WaitForEvaluationAsync();
        var sphere = vm.Document.Root.Children[2];
        vm.SelectOnly([a.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        var sphereBefore = BoxOf(vm, sphere).Center;

        await Join(vm, RightOf(a), LeftOf(b)); // the face belongs to the box inside the group

        Assert.Equal(BoxOf(vm, b).Min.X, BoxOf(vm, a).Max.X, 3);
        Assert.Equal(80, group.Transform.Position.X, 3);
        Assert.Equal(sphereBefore.X + 80, BoxOf(vm, sphere).Center.X, 3); // the sphere came along
        Assert.Equal([group.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task A_face_of_something_that_is_not_in_the_scene_is_ignored()
    {
        var (vm, a, _) = await TwoBoxes();
        vm.FaceToFaceCommand.Execute(null);

        vm.FacePickedCommand.Execute(new FacePick(Guid.NewGuid(), Vector3.Zero, Vector3.UnitX));
        Assert.False(vm.FaceToFaceHasFirst);

        vm.FacePickedCommand.Execute(RightOf(a));
        vm.FacePickedCommand.Execute(new FacePick(Guid.NewGuid(), Vector3.Zero, -Vector3.UnitX));

        Assert.True(vm.IsPickingFaceToFace);
        Assert.True(vm.FaceToFaceHasFirst);
    }

    [UiFact]
    public void The_command_needs_something_in_the_scene()
    {
        var vm = NewViewModel();

        Assert.False(vm.FaceToFaceCommand.CanExecute(null));
    }

    [UiFact]
    public async Task The_command_again_leaves_the_mode()
    {
        var (vm, _, _) = await TwoBoxes();

        vm.FaceToFaceCommand.Execute(null);
        vm.FaceToFaceCommand.Execute(null);

        Assert.False(vm.IsPickingFaceToFace);
    }

    [UiFact]
    public async Task Clicks_near_the_middles_of_the_faces_put_the_faces_exactly_in_the_middle_of_each_other()
    {
        var (vm, a, b) = await TwoBoxes();

        // both clicks are a few millimetres beside the middle of their face: they snap to the middle
        await Join(vm, new FacePick(a.Id, new Vector3(10, 3, 12), Vector3.UnitX), new FacePick(b.Id, new Vector3(90, -2, 8), -Vector3.UnitX));

        Assert.Equal(BoxOf(vm, b).Center.Y, BoxOf(vm, a).Center.Y, 3);
        Assert.Equal(BoxOf(vm, b).Center.Z, BoxOf(vm, a).Center.Z, 3);
    }

    [UiFact]
    public async Task Without_the_snap_the_clicked_points_are_put_onto_each_other()
    {
        var (vm, a, b) = await TwoBoxes();

        // Alt or Shift while clicking: the point (10, 3, 12) of A goes to the point (90, -2, 8) of B
        await Join(vm, new FacePick(a.Id, new Vector3(10, 3, 12), Vector3.UnitX, NoSnap: true), new FacePick(b.Id, new Vector3(90, -2, 8), -Vector3.UnitX, NoSnap: true));

        Assert.Equal(-5, BoxOf(vm, a).Center.Y, 3); // moved by -2 - 3
        Assert.Equal(6, BoxOf(vm, a).Center.Z, 3); // 10 + (8 - 12)
    }

    [UiFact]
    public async Task A_click_far_from_the_middle_keeps_its_place()
    {
        var (vm, a, b) = await TwoBoxes();

        await Join(vm, new FacePick(a.Id, new Vector3(10, 8, 10), Vector3.UnitX), new FacePick(b.Id, new Vector3(90, 0, 10), -Vector3.UnitX));

        Assert.Equal(-8, BoxOf(vm, a).Center.Y, 3); // the point 8 mm from the middle is brought to the middle of B: A moves by -8
    }
}
