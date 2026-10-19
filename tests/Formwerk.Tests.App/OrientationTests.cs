using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>90 degree rotations and "face on floor" (F-ROT-04).</summary>
public class OrientationTests
{
    private readonly FakeDialogs _dialogs = new();

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static ViewportItem SolidOf(MainViewModel vm, Guid id) => vm.ViewportItems.First(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid);

    private static Bounds BoundsOf(MainViewModel vm, params Guid[] ids)
    {
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static void AssertSize(Bounds bounds, float x, float y, float z, int digits = 3)
    {
        Assert.Equal(x, bounds.Size.X, digits);
        Assert.Equal(y, bounds.Size.Y, digits);
        Assert.Equal(z, bounds.Size.Z, digits);
    }

    /// <summary>A click on the triangle of the object whose outward direction is <paramref name="wanted"/>.</summary>
    private static (FacePick Pick, int Triangle) FaceOf(MainViewModel vm, Guid nodeId, Vector3 wanted)
    {
        var mesh = SolidOf(vm, nodeId).Mesh;
        var direction = Vector3.Normalize(wanted);
        for (var t = 0; t < mesh.TriangleCount; t++)
        {
            var a = mesh.Vertices[mesh.Indices[t * 3]];
            var b = mesh.Vertices[mesh.Indices[t * 3 + 1]];
            var c = mesh.Vertices[mesh.Indices[t * 3 + 2]];
            var normal = FaceMath.TriangleNormal(a, b, c);
            if (Vector3.Dot(normal, direction) > 0.999f) return (new FacePick(nodeId, (a + b + c) / 3, normal), t);
        }

        throw new InvalidOperationException("no such face");
    }

    private static Vector3[] Triangle(MainViewModel vm, Guid nodeId, int triangle)
    {
        var mesh = SolidOf(vm, nodeId).Mesh;
        return [mesh.Vertices[mesh.Indices[triangle * 3]], mesh.Vertices[mesh.Indices[triangle * 3 + 1]], mesh.Vertices[mesh.Indices[triangle * 3 + 2]]];
    }

    // ---- 90 degree rotations ---------------------------------------------------------------------

    [UiFact]
    public async Task Turning_about_Z_by_90_degrees_swaps_the_footprint_and_four_turns_come_back()
    {
        var vm = NewViewModel();
        await Add(vm, "box"); // 20 x 20 x 20 by default
        vm.Properties!.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "20";
        vm.Properties.Parameters[2].Text = "30";
        await vm.WaitForEvaluationAsync();
        var box = Child(vm, 0);
        var start = BoundsOf(vm, box.Id);
        var entries = vm.HistoryEntries.Count;

        vm.Rotate90Command.Execute("Z");
        await vm.WaitForEvaluationAsync();
        AssertSize(BoundsOf(vm, box.Id), 20, 10, 30);
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Contains("90", vm.HistoryEntries[^1].Name);

        for (var i = 0; i < 3; i++)
        {
            vm.Rotate90Command.Execute("Z");
            await vm.WaitForEvaluationAsync();
        }

        var back = BoundsOf(vm, box.Id);
        AssertSize(back, 10, 20, 30);
        Assert.Equal(start.Center.X, back.Center.X, 3);
        Assert.Equal(start.Center.Y, back.Center.Y, 3);
        Assert.Equal(start.Center.Z, back.Center.Z, 3);
    }

    [UiFact]
    public async Task Turning_about_X_and_Y_swaps_the_matching_sizes_about_the_center()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "20";
        vm.Properties.Parameters[2].Text = "30";
        await vm.WaitForEvaluationAsync();
        var box = Child(vm, 0);
        var center = BoundsOf(vm, box.Id).Center;

        vm.Rotate90Command.Execute("X");
        await vm.WaitForEvaluationAsync();
        AssertSize(BoundsOf(vm, box.Id), 10, 30, 20);
        vm.Rotate90Command.Execute("-X");
        await vm.WaitForEvaluationAsync();
        AssertSize(BoundsOf(vm, box.Id), 10, 20, 30);

        vm.Rotate90Command.Execute("Y");
        await vm.WaitForEvaluationAsync();
        AssertSize(BoundsOf(vm, box.Id), 30, 20, 10);
        Assert.Equal(center.X, BoundsOf(vm, box.Id).Center.X, 3);
        Assert.Equal(center.Z, BoundsOf(vm, box.Id).Center.Z, 3);
    }

    [UiFact]
    public async Task The_opposite_direction_undoes_the_turn()
    {
        var vm = NewViewModel();
        await Add(vm, "wedge");
        var wedge = Child(vm, 0);
        var before = BoundsOf(vm, wedge.Id);

        vm.Rotate90Command.Execute("Y");
        vm.Rotate90Command.Execute("-Y");
        await vm.WaitForEvaluationAsync();

        var after = BoundsOf(vm, wedge.Id);
        Assert.Equal(before.Min.X, after.Min.X, 3);
        Assert.Equal(before.Max.Z, after.Max.Z, 3);
    }

    [UiFact]
    public async Task Several_objects_turn_together_about_the_center_of_the_selection_in_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box"); // at x = -30
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        vm.SelectOnly([a.Id, b.Id]);
        await vm.WaitForEvaluationAsync();
        var before = BoundsOf(vm, a.Id, b.Id);
        var entries = vm.HistoryEntries.Count;

        vm.Rotate90Command.Execute("Z");
        await vm.WaitForEvaluationAsync();

        var after = BoundsOf(vm, a.Id, b.Id);
        AssertSize(after, before.Size.Y, before.Size.X, before.Size.Z); // the row along X is a row along Y now
        Assert.Equal(before.Center.X, after.Center.X, 2);
        Assert.Equal(before.Center.Y, after.Center.Y, 2);
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        AssertSize(BoundsOf(vm, a.Id, b.Id), before.Size.X, before.Size.Y, before.Size.Z);
    }

    [UiFact]
    public async Task Locked_objects_stay_and_without_a_selection_or_with_a_wrong_axis_nothing_happens()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var entries = vm.HistoryEntries.Count;
        box.Locked = true;

        vm.Rotate90Command.Execute("Z");
        Assert.Equal(entries, vm.HistoryEntries.Count);

        box.Locked = false;
        vm.Rotate90Command.Execute("W");
        vm.Rotate90Command.Execute(null);
        vm.Rotate90Command.Execute("");
        Assert.Equal(entries, vm.HistoryEntries.Count);

        vm.SelectOnly([]);
        Assert.False(vm.Rotate90Command.CanExecute("Z"));
    }

    [UiFact]
    public async Task An_object_inside_a_group_turns_in_place()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var box = Child(vm, 0);
        vm.SelectOnly([box.Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([box.Id]);
        await vm.WaitForEvaluationAsync();
        var before = BoundsOf(vm, box.Id);

        vm.Rotate90Command.Execute("Z");
        await vm.WaitForEvaluationAsync();

        var after = BoundsOf(vm, box.Id);
        Assert.Equal(before.Center.X, after.Center.X, 2);
        Assert.Equal(before.Center.Y, after.Center.Y, 2);
    }

    // ---- face on floor ---------------------------------------------------------------------------

    [UiFact]
    public async Task A_clicked_side_face_of_a_box_ends_up_flat_on_the_floor()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "20";
        vm.Properties.Parameters[2].Text = "30";
        await vm.WaitForEvaluationAsync();
        var box = Child(vm, 0);
        var (pick, triangle) = FaceOf(vm, box.Id, new Vector3(1, 0, 0)); // the +X face, 20 x 30
        var entries = vm.HistoryEntries.Count;

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        var bounds = BoundsOf(vm, box.Id);
        AssertSize(bounds, 30, 20, 10);
        Assert.Equal(0, bounds.Min.Z, 4);
        Assert.All(Triangle(vm, box.Id, triangle), v => Assert.Equal(0, v.Z, 4)); // the face is exactly on Z = 0
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Lay Box on a face", vm.HistoryEntries[^1].Name);
        Assert.Equal([box.Id], vm.Selection.ToArray());
    }

    [UiFact]
    public async Task The_clicked_point_stays_where_it_was_on_the_plate()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "40";
        vm.Properties.Position[1].Text = "-25";
        await vm.WaitForEvaluationAsync();
        var box = Child(vm, 0);
        var (pick, triangle) = FaceOf(vm, box.Id, new Vector3(0, 1, 0));

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        var centroid = Triangle(vm, box.Id, triangle).Aggregate(Vector3.Zero, (sum, v) => sum + v) / 3;
        Assert.Equal(pick.Point.X, centroid.X, 3);
        Assert.Equal(pick.Point.Y, centroid.Y, 3);
        Assert.Equal(0, centroid.Z, 4);
    }

    [UiFact]
    public async Task A_tilted_object_is_laid_on_its_tilted_face_exactly()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Rotation[1].Text = "30";
        vm.Properties.Rotation[2].Text = "20";
        vm.Properties.Position[2].Text = "12";
        await vm.WaitForEvaluationAsync();
        var box = Child(vm, 0);
        var mesh = SolidOf(vm, box.Id).Mesh;
        // the face that looks most to the right
        var best = Enumerable.Range(0, mesh.TriangleCount)
            .Select(t => (T: t, N: FaceMath.TriangleNormal(mesh.Vertices[mesh.Indices[t * 3]], mesh.Vertices[mesh.Indices[t * 3 + 1]], mesh.Vertices[mesh.Indices[t * 3 + 2]])))
            .OrderByDescending(x => x.N.X).First();
        var (pick, triangle) = FaceOf(vm, box.Id, best.N);

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        Assert.All(Triangle(vm, box.Id, triangle), v => Assert.Equal(0, v.Z, 3));
        var normalNow = FaceMath.TriangleNormal(Triangle(vm, box.Id, triangle)[0], Triangle(vm, box.Id, triangle)[1], Triangle(vm, box.Id, triangle)[2]);
        Assert.Equal(-1, normalNow.Z, 3); // looks straight down
    }

    [UiFact]
    public async Task The_top_face_is_turned_to_the_bottom_and_the_bottom_face_needs_nothing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var (bottom, _) = FaceOf(vm, box.Id, new Vector3(0, 0, -1));
        var entries = vm.HistoryEntries.Count;

        vm.LayFaceOnFloor(bottom);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Contains("already", vm.StatusText);

        var (top, triangle) = FaceOf(vm, box.Id, new Vector3(0, 0, 1));
        vm.LayFaceOnFloor(top);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.All(Triangle(vm, box.Id, triangle), v => Assert.Equal(0, v.Z, 4));
        Assert.Equal(0, BoundsOf(vm, box.Id).Min.Z, 4);
    }

    [UiFact]
    public async Task The_slope_of_a_wedge_can_be_laid_on_the_floor()
    {
        var vm = NewViewModel();
        await Add(vm, "wedge");
        var wedge = Child(vm, 0);
        var (pick, triangle) = FaceOf(vm, wedge.Id, new Vector3(0, -1, 1)); // the slope looks forward and up

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        Assert.All(Triangle(vm, wedge.Id, triangle), v => Assert.Equal(0, v.Z, 3));
        Assert.Equal(0, BoundsOf(vm, wedge.Id).Min.Z, 3);
    }

    [UiFact]
    public async Task Undo_brings_the_object_back_to_where_and_how_it_was()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var transform = box.Transform;
        var (pick, _) = FaceOf(vm, box.Id, new Vector3(1, 0, 0));
        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();
        Assert.NotEqual(transform, box.Transform);

        vm.UndoCommand.Execute(null);

        Assert.Equal(transform, box.Transform);
    }

    [UiFact]
    public async Task A_face_of_an_object_in_a_group_turns_the_whole_group()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var box = Child(vm, 0);
        vm.SelectOnly([box.Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        var boxTransform = box.Transform;
        var (pick, triangle) = FaceOf(vm, box.Id, new Vector3(1, 0, 0));

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(boxTransform, box.Transform); // the part keeps its place in the group
        Assert.NotEqual(Transform.Identity.Rotation, group.Transform.Rotation);
        Assert.All(Triangle(vm, box.Id, triangle), v => Assert.Equal(0, v.Z, 3));
        Assert.Equal([group.Id], vm.Selection.ToArray());
    }

    [UiFact]
    public async Task A_boolean_operand_turns_with_its_boolean()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Union);
        await vm.WaitForEvaluationAsync();
        var boolean = Child(vm, 0);
        var operand = boolean.Children[0];
        var (pick, _) = FaceOf(vm, boolean.Id, new Vector3(1, 0, 0));
        var operandTransform = operand.Transform;

        vm.LayFaceOnFloor(pick with { NodeId = operand.Id });
        await vm.WaitForEvaluationAsync();

        Assert.Equal(operandTransform, operand.Transform);
        Assert.Equal([boolean.Id], vm.Selection.ToArray());
    }

    [UiFact]
    public async Task A_locked_object_is_not_turned()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var (pick, _) = FaceOf(vm, box.Id, new Vector3(1, 0, 0));
        box.Locked = true;
        var entries = vm.HistoryEntries.Count;

        vm.LayFaceOnFloor(pick);

        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Contains("locked", vm.StatusText);
    }

    [UiFact]
    public async Task A_mesh_object_can_be_laid_on_a_face_too()
    {
        var vm = NewViewModel();
        await Add(vm, "wedge");
        await vm.FlattenCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
        var mesh = Child(vm, 0);
        var (pick, triangle) = FaceOf(vm, mesh.Id, new Vector3(0, -1, 1));

        vm.LayFaceOnFloor(pick);
        await vm.WaitForEvaluationAsync();

        Assert.All(Triangle(vm, mesh.Id, triangle), v => Assert.Equal(0, v.Z, 3));
    }

    // ---- the mode --------------------------------------------------------------------------------

    [UiFact]
    public async Task The_command_switches_the_pick_mode_on_and_off_with_a_hint()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Assert.False(vm.IsPickingFace);

        vm.FaceOnFloorCommand.Execute(null);
        Assert.True(vm.IsPickingFace);
        Assert.Contains("face", vm.HintText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Esc", vm.HintText);

        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsPickingFace);
        Assert.DoesNotContain("Click the face", vm.HintText); // the usual hint is back
    }

    [UiFact]
    public async Task Esc_leaves_the_mode_and_a_successful_pick_ends_it()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.FaceOnFloorCommand.Execute(null);

        vm.FacePickedCommand.Execute(null); // what the viewport reports for Esc
        Assert.False(vm.IsPickingFace);

        vm.FaceOnFloorCommand.Execute(null);
        var (pick, _) = FaceOf(vm, box.Id, new Vector3(1, 0, 0));
        vm.FacePickedCommand.Execute(pick);
        await vm.WaitForEvaluationAsync();
        Assert.False(vm.IsPickingFace);
        Assert.Equal(0, BoundsOf(vm, box.Id).Min.Z, 4);
    }

    [UiFact]
    public async Task A_pick_of_something_that_is_not_there_is_ignored_and_the_mode_stays()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.FaceOnFloorCommand.Execute(null);
        var entries = vm.HistoryEntries.Count;

        vm.FacePickedCommand.Execute(new FacePick(Guid.NewGuid(), Vector3.Zero, Vector3.UnitX));

        Assert.True(vm.IsPickingFace);
        Assert.Equal(entries, vm.HistoryEntries.Count);
    }

    [UiFact]
    public void Without_objects_there_is_nothing_to_lay_down()
    {
        var vm = NewViewModel();

        Assert.False(vm.FaceOnFloorCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Typing_or_selecting_something_else_does_not_leave_the_mode_by_accident()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.FaceOnFloorCommand.Execute(null);

        vm.SelectOnly([]);

        Assert.True(vm.IsPickingFace);
    }
}
