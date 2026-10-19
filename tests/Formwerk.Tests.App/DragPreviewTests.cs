using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>
/// While a move or rotate handle is dragged the view moves the meshes it already has (ADR 0005). Objects that sit in a group keep the
/// live recalculation because their shape depends on their place.
/// </summary>
public class DragPreviewTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static GizmoDrag Move(GizmoHandleKind handle, float x, float y, float z) => new(handle, new Vector3(x, y, z), 0, false);

    private static GizmoDrag Turn(GizmoHandleKind handle, double degrees) => new(handle, Vector3.Zero, degrees, false);

    private static Vector3 ItemCenter(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds().Center;

    /// <summary>Where the view shows the center of an object: the center of its mesh moved by the preview matrix.</summary>
    private static Vector3 ShownCenter(MainViewModel vm, Guid id)
    {
        var center = ItemCenter(vm, id);
        return vm.PreviewTransforms is { } preview && preview.TryGetValue(id, out var matrix) ? Vector3.Transform(center, matrix) : center;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    // ---- moving ----------------------------------------------------------------------------------

    [UiFact]
    public async Task Dragging_an_object_in_the_scene_moves_what_the_view_shows_without_calculating_the_scene()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var itemsBefore = vm.ViewportItems;
        var start = ItemCenter(vm, box.Id);

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 12.4f, 0, 0));
        await vm.WaitForEvaluationAsync();

        Assert.Same(itemsBefore, vm.ViewportItems);                       // nothing was recalculated, the view got no new meshes
        Assert.NotNull(vm.PreviewTransforms);
        AssertNear(start + new Vector3(12, 0, 0), ShownCenter(vm, box.Id)); // snapped to the 1 mm grid
        Assert.Equal(12, box.Transform.Position.X, 3);                     // the document is up to date
    }

    [UiFact]
    public async Task The_handles_and_the_position_fields_follow_the_object_during_the_drag()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var pivot = vm.GizmoPivot!.Value;

        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 20, 0, 0));

        AssertNear(pivot + new Vector3(20, 0, 0), vm.GizmoPivot!.Value);
        Assert.Equal("20", vm.Properties!.Position[0].Text);
    }

    [UiFact]
    public async Task After_the_release_the_result_replaces_the_preview()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var entries = vm.HistoryEntries.Count;
        var start = ItemCenter(vm, box.Id);
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 30, 0, 0));

        vm.CommitDrag();
        Assert.NotNull(vm.PreviewTransforms); // still on screen until the result is there, so the object does not jump back
        await vm.WaitForEvaluationAsync();

        Assert.Null(vm.PreviewTransforms);
        AssertNear(start + new Vector3(30, 0, 0), ItemCenter(vm, box.Id)); // the new meshes contain the move
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        AssertNear(start, ItemCenter(vm, box.Id));
    }

    [UiFact]
    public async Task Cancelling_takes_the_preview_away_and_leaves_everything_as_it_was()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        var pivot = vm.GizmoPivot!.Value;
        var entries = vm.HistoryEntries.Count;
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 30, 0, 0));

        vm.CancelDrag();

        Assert.Null(vm.PreviewTransforms);
        Assert.Equal(0, box.Transform.Position.X, 3);
        AssertNear(pivot, vm.GizmoPivot!.Value);
        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Equal("0", vm.Properties!.Position[0].Text);
    }

    [UiFact]
    public async Task A_drag_without_a_change_still_clears_the_preview()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 0, 0, 0));

        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Null(vm.PreviewTransforms);
    }

    // ---- rotating --------------------------------------------------------------------------------

    [UiFact]
    public async Task Rotating_turns_what_the_view_shows_around_the_pivot()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box"); // at x = -30
        vm.SelectOnly(vm.Document.Root.Children.Select(c => c.Id));
        await vm.WaitForEvaluationAsync();
        var a = Child(vm, 0);
        var b = Child(vm, 1);
        var pivot = vm.GizmoPivot!.Value;
        var startA = ItemCenter(vm, a.Id);

        vm.BeginDrag(GizmoHandleKind.RotateZ);
        vm.UpdateDrag(Turn(GizmoHandleKind.RotateZ, 90));

        var offset = startA - pivot;
        AssertNear(pivot + new Vector3(-offset.Y, offset.X, 0), ShownCenter(vm, a.Id), digits: 2); // a quarter turn about the pivot
        AssertNear(pivot, vm.GizmoPivot!.Value);                                                    // the pivot itself stays
        Assert.NotNull(vm.PreviewTransforms);
        Assert.True(vm.PreviewTransforms!.ContainsKey(b.Id));
    }

    // ---- several objects and groups --------------------------------------------------------------

    [UiFact]
    public async Task All_selected_objects_move_together_and_a_group_takes_its_children_along()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        var box = Child(vm, 0);
        var sphere = Child(vm, 1);
        var cylinder = Child(vm, 2);
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        vm.SelectOnly([group.Id, cylinder.Id]);
        await vm.WaitForEvaluationAsync();
        var startBox = ItemCenter(vm, box.Id);
        var startCylinder = ItemCenter(vm, cylinder.Id);

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveY));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveY, 0, 7, 0));

        AssertNear(startBox + new Vector3(0, 7, 0), ShownCenter(vm, box.Id));
        AssertNear(startCylinder + new Vector3(0, 7, 0), ShownCenter(vm, cylinder.Id));
        Assert.True(vm.PreviewTransforms!.ContainsKey(sphere.Id));
        Assert.True(vm.PreviewTransforms.ContainsKey(group.Id));
    }

    [UiFact]
    public async Task A_dragged_group_with_a_hole_inside_moves_as_a_whole_with_the_hole_ghost()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        var box = Child(vm, 0);
        var hole = Child(vm, 1);
        vm.SelectOnly([hole.Id]);
        vm.ToggleHole();
        vm.SelectOnly([box.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        vm.SelectOnly([group.Id]);
        await vm.WaitForEvaluationAsync();
        var itemsBefore = vm.ViewportItems;

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 10, 0, 0));
        await vm.WaitForEvaluationAsync();

        Assert.Same(itemsBefore, vm.ViewportItems); // the whole group is rigid: cut result and hole ghost just move
        Assert.True(vm.PreviewTransforms!.ContainsKey(hole.Id));
        Assert.True(vm.PreviewTransforms.ContainsKey(box.Id));
    }

    // ---- where the preview must not be used ------------------------------------------------------

    [UiFact]
    public async Task An_object_inside_a_group_is_recalculated_live_because_its_shape_depends_on_its_place()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        var box = Child(vm, 0);
        var hole = Child(vm, 1);
        vm.SelectOnly([hole.Id]);
        vm.ToggleHole();
        vm.SelectOnly([box.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([hole.Id]); // the hole inside the group, selected through the list
        await vm.WaitForEvaluationAsync();
        var itemsBefore = vm.ViewportItems;

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 5, 0, 0));
        await vm.WaitForEvaluationAsync();

        Assert.Null(vm.PreviewTransforms);
        Assert.NotSame(itemsBefore, vm.ViewportItems); // a new result with the cut in its new place
        vm.CancelDrag();
    }

    [UiFact]
    public async Task A_new_drag_that_starts_before_the_result_of_the_last_one_is_there_calculates_live()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.BeginDrag(GizmoHandleKind.MoveX);
        vm.UpdateDrag(Move(GizmoHandleKind.MoveX, 10, 0, 0));
        vm.CommitDrag(); // the result is on its way, the preview is still shown

        Assert.NotNull(vm.PreviewTransforms);
        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveY));
        vm.UpdateDrag(Move(GizmoHandleKind.MoveY, 0, 5, 0));
        await vm.WaitForEvaluationAsync();

        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();
        Assert.Null(vm.PreviewTransforms);
        AssertNear(new Vector3(10, 5, box.Transform.Position.Z), box.Transform.Position);
        Assert.Equal(10, ItemCenter(vm, box.Id).X, 2);
        Assert.Equal(5, ItemCenter(vm, box.Id).Y, 2);
    }

    [UiFact]
    public async Task Scaling_keeps_the_old_way_because_the_shape_changes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.ActiveTool = ViewportTool.Scale;
        await vm.WaitForEvaluationAsync();

        Assert.True(vm.BeginDrag(GizmoHandleKind.ScaleXMax));
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.ScaleXMax, Vector3.Zero, 0, false, ScaleFactor: 1.5));
        await vm.WaitForEvaluationAsync();

        Assert.Null(vm.PreviewTransforms);
        vm.CancelDrag();
    }

    [UiFact]
    public async Task Locked_objects_cannot_be_dragged_and_get_no_preview()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Child(vm, 0).Locked = true;

        Assert.False(vm.BeginDrag(GizmoHandleKind.MoveX));
        Assert.Null(vm.PreviewTransforms);
    }

    [UiFact]
    public async Task An_imported_mesh_is_dragged_with_the_preview_and_nothing_is_calculated_meanwhile()
    {
        var folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"formwerk-dragpreview-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(folder);
        try
        {
            var path = System.IO.Path.Combine(folder, "ball.stl");
            var sphere = new Formwerk.Features.Shapes.SphereGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 40 });
            using (var stream = System.IO.File.Create(path)) Formwerk.IO.StlWriter.WriteBinary(stream, [sphere]);
            var vm = NewViewModel();
            await vm.ImportFilesAsync([path]);
            await vm.WaitForEvaluationAsync();
            var itemsBefore = vm.ViewportItems;
            var statusBefore = vm.StatusText;

            Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
            for (var i = 1; i <= 10; i++) vm.UpdateDrag(Move(GizmoHandleKind.MoveX, i * 2f, 0, 0));
            await vm.WaitForEvaluationAsync();

            Assert.False(vm.IsBusy);
            Assert.Same(itemsBefore, vm.ViewportItems);
            Assert.Equal(statusBefore, vm.StatusText); // no "Computing..." while the mouse moves
            Assert.NotNull(vm.PreviewTransforms);
            vm.CancelDrag();
        }
        finally
        {
            System.IO.Directory.Delete(folder, recursive: true);
        }
    }
}
