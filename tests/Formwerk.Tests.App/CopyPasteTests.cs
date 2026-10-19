using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Copy, paste and duplicate with the last offset (F-CPY-01).</summary>
public class CopyPasteTests
{
    private readonly FakeDialogs _dialogs = new();

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static Vector3 World(Node node) => TransformOps.WorldPosition(node);

    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    private static GizmoDrag Move(GizmoHandleKind handle, float x, float y, float z) => new(handle, new Vector3(x, y, z), 0, false);

    private static async Task DragCommit(MainViewModel vm, GizmoHandleKind handle, float x, float y, float z)
    {
        Assert.True(vm.BeginDrag(handle));
        vm.UpdateDrag(Move(handle, x, y, z));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();
    }

    // ---- Ctrl+D: the acceptance criterion --------------------------------------------------------

    [UiFact]
    public async Task After_one_move_by_15_mm_three_duplicates_make_a_row_with_15_mm_spacing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "15"; // the one move

        for (var i = 0; i < 3; i++)
        {
            vm.DuplicateCommand.Execute(null);
            await vm.WaitForEvaluationAsync();
        }

        var xs = vm.Document.Root.Children.Select(n => World(n).X).Order().ToArray();
        Assert.Equal([15f, 30f, 45f, 60f], xs);
    }

    [UiFact]
    public async Task Without_any_move_the_offset_is_15_mm_along_x()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.DuplicateCommand.Execute(null);
        vm.DuplicateCommand.Execute(null);

        Assert.Equal([0f, 15f, 30f], vm.Document.Root.Children.Select(n => World(n).X).Order().ToArray());
    }

    [UiFact]
    public async Task The_offset_of_a_drag_is_used_in_all_three_directions()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await DragCommit(vm, GizmoHandleKind.MoveY, 0, 22, 0);
        Assert.Equal(22, World(Child(vm, 0)).Y, 3);

        vm.DuplicateCommand.Execute(null);
        await vm.WaitForEvaluationAsync(); // the handles need the evaluated copy
        AssertNear(new Vector3(0, 44, 0), World(Child(vm, 1)));

        await DragCommit(vm, GizmoHandleKind.MoveZ, 0, 0, 7);
        vm.DuplicateCommand.Execute(null);
        AssertNear(new Vector3(0, 44, 14), World(Child(vm, 2))); // the copy of the moved copy, 7 higher than it
    }

    [UiFact]
    public async Task A_typed_position_counts_as_a_move_too()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[1].Text = "-8";

        vm.DuplicateCommand.Execute(null);

        AssertNear(new Vector3(0, -16, 0), World(Child(vm, 1)));
    }

    [UiFact]
    public async Task A_copy_lands_at_the_same_distance_as_the_one_before_even_after_other_work()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "40";
        vm.DuplicateCommand.Execute(null);
        vm.DuplicateCommand.Execute(null);

        Assert.Equal([40f, 80f, 120f], vm.Document.Root.Children.Select(n => World(n).X).Order().ToArray());
    }

    [UiFact]
    public async Task Rotating_scaling_dropping_to_the_floor_and_cancelled_drags_do_not_change_the_offset()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "12"; // the offset is 12
        vm.Properties.Rotation[2].Text = "30";
        vm.Properties.Position[2].Text = "5";   // a typed move: the offset is (0,0,5) now...
        vm.Properties.Position[2].Text = "0";   // ...and (0,0,-5) now
        vm.Properties.Position[0].Text = "12";  // unchanged value: no move

        // a drag that is cancelled
        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 99, 0, 0));
        vm.CancelDrag();
        vm.DropToFloorCommand.Execute(null);

        vm.DuplicateCommand.Execute(null);

        AssertNear(World(Child(vm, 0)) + new Vector3(0, 0, -5), World(Child(vm, 1)));
    }

    [UiFact]
    public async Task A_move_by_zero_keeps_the_previous_offset()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await DragCommit(vm, GizmoHandleKind.MoveX, 20, 0, 0);
        await DragCommit(vm, GizmoHandleKind.MoveX, 0, 0, 0);

        vm.DuplicateCommand.Execute(null);

        Assert.Equal(40, World(Child(vm, 1)).X, 3);
    }

    [UiFact]
    public async Task All_selected_objects_are_copied_by_the_same_world_offset_and_the_copies_are_selected()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        vm.SelectOnly([a.Id, b.Id]);

        vm.DuplicateCommand.Execute(null);

        Assert.Equal(4, vm.Document.Root.Children.Count);
        var copies = vm.Document.Root.Children.Where(n => n != a && n != b).ToList();
        Assert.Equal(2, copies.Count);
        AssertNear(World(a) + new Vector3(15, 0, 0), World(copies.Single(c => c.Name.StartsWith("Box"))));
        AssertNear(World(b) + new Vector3(15, 0, 0), World(copies.Single(c => c.Name.StartsWith("Sphere"))));
        Assert.Equal(copies.Select(c => c.Id).Order(), vm.Selection.Order());
    }

    [UiFact]
    public async Task The_offset_is_a_world_offset_even_inside_a_turned_group()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.SelectOnly([box.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        group.Transform = new Transform(new Vector3(10, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One);
        vm.Document.NotifyChanged();
        vm.SelectOnly([box.Id]);
        var start = World(box);

        vm.DuplicateCommand.Execute(null);

        var copy = group.Children.Single(c => c != box);
        AssertNear(start + new Vector3(15, 0, 0), World(copy), 2); // 15 mm to the right on screen, whatever the group's turn
    }

    [UiFact]
    public async Task The_copies_are_named_copy_copy_2_copy_3_and_a_copy_is_not_named_copy_copy()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        for (var i = 0; i < 3; i++) vm.DuplicateCommand.Execute(null);

        Assert.Equal(["Box", "Box copy", "Box copy 2", "Box copy 3"], vm.Document.Root.Children.Select(n => n.Name).ToArray());
    }

    [UiFact]
    public async Task Duplicating_is_one_undo_step_per_press()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var entries = vm.HistoryEntries.Count;

        vm.DuplicateCommand.Execute(null);
        vm.DuplicateCommand.Execute(null);
        Assert.Equal(entries + 2, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    // ---- copy and paste --------------------------------------------------------------------------

    [UiFact]
    public async Task Copy_changes_nothing_and_paste_adds_a_copy_next_to_the_original()
    {
        var vm = NewViewModel();
        await Add(vm, "cylinder");
        var original = Child(vm, 0);
        var entries = vm.HistoryEntries.Count;

        vm.CopyCommand.Execute(null);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        vm.PasteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, vm.Document.Root.Children.Count);
        var pasted = Child(vm, 1);
        Assert.NotEqual(original.Id, pasted.Id);
        Assert.Equal("Cylinder copy", pasted.Name);
        AssertNear(World(original) + new Vector3(15, 0, 0), World(pasted));
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal([pasted.Id], vm.Selection.ToArray());
        Assert.Equal(2, vm.ViewportItems.Count(i => i.Kind == ViewportItemKind.Solid));
    }

    [UiFact]
    public async Task Pasting_again_moves_one_more_offset_on_and_copying_again_starts_over()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Position[0].Text = "10";
        vm.CopyCommand.Execute(null);

        vm.PasteCommand.Execute(null);
        vm.PasteCommand.Execute(null);
        vm.PasteCommand.Execute(null);

        Assert.Equal([10f, 20f, 30f, 40f], vm.Document.Root.Children.Select(n => World(n).X).Order().ToArray());

        vm.SelectOnly([Child(vm, 3).Id]); // the copy at 40
        vm.CopyCommand.Execute(null);
        vm.PasteCommand.Execute(null);

        Assert.Equal(50, World(Child(vm, 4)).X, 3);
    }

    [UiFact]
    public async Task A_whole_group_is_pasted_with_its_children_parameters_and_holes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "33";
        await Add(vm, "cylinder");
        vm.ToggleHole();
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.CopyCommand.Execute(null);
        vm.PasteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var copy = Assert.IsType<GroupNode>(Child(vm, 1));
        Assert.Equal(2, copy.Children.Count);
        Assert.Equal("33", ((PrimitiveNode)copy.Children[0]).Parameters["width"]);
        Assert.Equal(NodeRole.Hole, copy.Children[1].Role);
        var originalIds = Child(vm, 0).Descendants().Select(d => d.Id).ToHashSet();
        Assert.DoesNotContain(copy.Children[0].Id, originalIds); // new ids: a real copy
    }

    [UiFact]
    public async Task An_object_copied_from_inside_a_group_is_pasted_into_the_scene_at_the_same_world_place()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.SelectOnly([box.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        group.Transform = new Transform(new Vector3(30, 20, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.6f), Vector3.One);
        vm.Document.NotifyChanged();
        vm.SelectOnly([box.Id]);
        var worldBefore = World(box);
        var rotationBefore = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(TransformOps.LocalToWorld(box)));

        vm.CopyCommand.Execute(null);
        vm.PasteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var pasted = vm.Document.Root.Children.Last();
        Assert.Same(vm.Document.Root, pasted.Parent);
        AssertNear(worldBefore + new Vector3(15, 0, 0), World(pasted), 2);
        var rotationAfter = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(TransformOps.LocalToWorld(pasted)));
        Assert.True(Math.Abs(Quaternion.Dot(rotationBefore, rotationAfter)) > 0.9999f); // the same turn
    }

    [UiFact]
    public async Task The_clipboard_works_after_the_original_is_deleted_and_after_a_new_project()
    {
        var vm = NewViewModel();
        await Add(vm, "sphere");
        vm.CopyCommand.Execute(null);
        vm.DeleteCommand.Execute(null);
        _dialogs.UnsavedAnswer = UnsavedChoice.Discard;
        vm.NewCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);

        vm.PasteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Sphere copy", Child(vm, 0).Name);
        Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
    }

    [UiFact]
    public async Task A_mesh_object_keeps_its_mesh_through_a_new_project()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await vm.FlattenCommand.ExecuteAsync(null); // a mesh object whose mesh lives in the store
        await vm.WaitForEvaluationAsync();
        vm.CopyCommand.Execute(null);
        _dialogs.UnsavedAnswer = UnsavedChoice.Discard;
        vm.NewCommand.Execute(null); // clears the mesh store
        await vm.WaitForEvaluationAsync();

        vm.PasteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var pasted = Assert.IsType<MeshNode>(Child(vm, 0));
        Assert.True(vm.Meshes.Contains(pasted.MeshId));
        Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
        Assert.Equal(12, vm.Meshes.Get(pasted.MeshId)!.TriangleCount);
    }

    [UiFact]
    public async Task Paste_is_one_undo_step_and_redo_brings_it_back()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly(vm.Document.Root.Children.Select(c => c.Id));
        vm.CopyCommand.Execute(null);
        var entries = vm.HistoryEntries.Count;

        vm.PasteCommand.Execute(null);
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal(4, vm.Document.Root.Children.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        vm.RedoCommand.Execute(null);
        Assert.Equal(4, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task The_commands_are_only_available_when_they_make_sense()
    {
        var vm = NewViewModel();
        Assert.False(vm.CopyCommand.CanExecute(null));
        Assert.False(vm.PasteCommand.CanExecute(null));

        await Add(vm, "box");
        Assert.True(vm.CopyCommand.CanExecute(null));
        Assert.False(vm.PasteCommand.CanExecute(null)); // nothing copied yet

        vm.CopyCommand.Execute(null);
        Assert.True(vm.PasteCommand.CanExecute(null));

        vm.SelectOnly([]);
        Assert.False(vm.CopyCommand.CanExecute(null));
        Assert.True(vm.PasteCommand.CanExecute(null)); // paste does not need a selection
    }

    [UiFact]
    public async Task What_is_pasted_is_a_snapshot_of_the_moment_of_copying()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.CopyCommand.Execute(null);
        vm.Properties!.Parameters[0].Text = "50"; // changed after copying
        await vm.WaitForEvaluationAsync();

        vm.PasteCommand.Execute(null);

        Assert.Equal("20", ((PrimitiveNode)Child(vm, 1)).Parameters["width"]);
    }
}
