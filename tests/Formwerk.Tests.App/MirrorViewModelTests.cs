using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Mirroring in the program (F-PAT-04): a linked mirror, an independent mirrored copy, and both at a face.</summary>
public class MirrorViewModelTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    /// <summary>A 20 mm box 30 mm to the right of the origin, selected.</summary>
    private static async Task<(MainViewModel Vm, Node Box)> BoxAtX30()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.Properties!.Position[0].Text = "30";
        await vm.WaitForEvaluationAsync();
        return (vm, box);
    }

    private static List<Vector3> SolidCenters(MainViewModel vm) =>
        [.. vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds().Center).OrderBy(c => c.X).ThenBy(c => c.Z)];

    private static MirrorNode TheMirror(MainViewModel vm) => vm.Document.Root.Descendants().OfType<MirrorNode>().Single();

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    [UiFact]
    public async Task The_selection_gets_a_linked_mirror_in_one_undo_step()
    {
        var (vm, box) = await BoxAtX30();
        var steps = vm.HistoryEntries.Count;

        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var mirror = TheMirror(vm);
        Assert.Same(box, Assert.Single(mirror.Children));
        Assert.Equal("0", mirror.Parameters["axis"]);
        Assert.Equal("0", mirror.Parameters["copyOnly"]);
        Assert.Equal([mirror.Id], vm.SelectedIds);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        var centers = SolidCenters(vm);
        Assert.Equal(2, centers.Count);
        Assert.True(Near(centers[0], new Vector3(-30, 0, 10)), centers[0].ToString()); // the plane is the one through the origin
        Assert.True(Near(centers[1], new Vector3(30, 0, 10)));

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
        Assert.Single(SolidCenters(vm));
    }

    [UiFact]
    public async Task Changing_the_original_changes_the_linked_image()
    {
        var (vm, box) = await BoxAtX30();
        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.SelectOnly([box.Id]);
        var height = ShapeLibrary.Find("box")!.Parameters.ToList().FindIndex(p => p.Name == "height");
        vm.Properties!.Parameters[height].Text = "50";
        await vm.WaitForEvaluationAsync();

        Assert.All(vm.ViewportItems, i => Assert.Equal(50, i.Mesh.ComputeBounds().Size.Z, 3));
    }

    /// <summary>A plate 100 x 30 x 10 with a hole 20 mm to the right of its center, in a group. The hole is selected.</summary>
    private static async Task<(MainViewModel Vm, Node Hole)> PlateWithHole()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var plate = vm.Document.Root.Children[0];
        vm.SelectOnly([plate.Id]);
        vm.Properties!.Parameters[0].Text = "100";
        vm.Properties.Parameters[1].Text = "30";
        vm.Properties.Parameters[2].Text = "10";
        vm.Properties.Position[0].Text = "50"; // the plate stands from 0 to 100
        await vm.WaitForEvaluationAsync();
        vm.PlaceHole(new FacePick(plate.Id, new Vector3(70, 0, 10), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        var hole = vm.Document.Root.Descendants().Single(n => n is PrimitiveNode { GeneratorId: "hole" });
        vm.SelectOnly([hole.Id]);
        return (vm, hole);
    }

    [UiFact]
    public async Task The_plane_goes_through_the_middle_of_the_body_next_to_the_source()
    {
        var (vm, hole) = await PlateWithHole();
        double Volume() => vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (double)i.Mesh.ComputeVolume());
        var oneHole = 100 * 30 * 10 - Volume();

        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(new Vector3(50, 0, 0), TheMirror(vm).Transform.Position); // the middle of the plate
        var ghosts = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Ghost).Select(i => MathF.Round(i.Mesh.ComputeBounds().Center.X)).Order().ToArray();
        Assert.Equal([30f, 70f], ghosts); // symmetric about the middle of the plate
        Assert.InRange((100 * 30 * 10 - Volume()) / oneHole, 1.9, 2.1); // two holes now
        Assert.True(Near(TransformOps.WorldPosition(hole), new Vector3(70, 0, 10)), "the hole did not move");
    }

    [UiFact]
    public async Task A_mirrored_copy_leaves_the_original_alone_and_does_not_follow_it()
    {
        var (vm, box) = await BoxAtX30();
        var steps = vm.HistoryEntries.Count;

        vm.MirroredCopyCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var mirror = TheMirror(vm);
        Assert.Equal("1", mirror.Parameters["copyOnly"]);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Same(box, vm.Document.Root.Children[0]); // the original stays where it was in the tree
        Assert.NotEqual(box.Id, Assert.Single(mirror.Children).Id); // a private copy
        Assert.Equal([mirror.Id], vm.SelectedIds);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        var centers = SolidCenters(vm);
        Assert.True(Near(centers[0], new Vector3(-30, 0, 10)));
        Assert.True(Near(centers[1], new Vector3(30, 0, 10)));

        vm.SelectOnly([box.Id]);
        var height = ShapeLibrary.Find("box")!.Parameters.ToList().FindIndex(p => p.Name == "height");
        vm.Properties!.Parameters[height].Text = "50";
        await vm.WaitForEvaluationAsync();

        var heights = vm.ViewportItems.Select(i => Math.Round(i.Mesh.ComputeBounds().Size.Z)).Order().ToArray();
        Assert.Equal([20.0, 50.0], heights); // only the original grew

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
    }

    [UiFact]
    public async Task Both_commands_need_a_selection_with_one_parent()
    {
        var vm = NewViewModel();
        Assert.False(vm.LinkedMirrorCommand.CanExecute(null));
        Assert.False(vm.MirroredCopyCommand.CanExecute(null));

        var (vm2, _) = await BoxAtX30();
        Assert.True(vm2.LinkedMirrorCommand.CanExecute(null));
        Assert.True(vm2.MirroredCopyCommand.CanExecute(null));
    }

    [UiFact]
    public async Task The_properties_show_the_plane_and_whether_the_original_is_shown()
    {
        var (vm, _) = await BoxAtX30();
        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var props = vm.Properties!;

        Assert.True(props.IsMirror);
        Assert.Equal(0, props.MirrorAxis);
        Assert.True(props.MirrorShowOriginal);

        props.MirrorAxis = 1; // the plane perpendicular to Y
        await vm.WaitForEvaluationAsync();
        Assert.Equal("1", TheMirror(vm).Parameters["axis"]);

        vm.Properties!.MirrorShowOriginal = false;
        await vm.WaitForEvaluationAsync();
        Assert.Equal("1", TheMirror(vm).Parameters["copyOnly"]);
        Assert.Single(vm.ViewportItems);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(2, vm.ViewportItems.Count);
    }

    [UiFact]
    public async Task The_parameter_section_is_shown_for_a_mirror_although_it_has_no_number_fields()
    {
        var (vm, _) = await BoxAtX30();
        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.Properties!.Parameters);
        Assert.True(vm.Properties.HasParameters); // the section holds the plane and "show the original"
    }

    [UiFact]
    public async Task Other_nodes_have_no_mirror_controls()
    {
        var (vm, _) = await BoxAtX30();

        Assert.False(vm.Properties!.IsMirror);
    }

    [UiFact]
    public async Task A_click_on_the_image_or_the_source_selects_the_mirror()
    {
        var (vm, box) = await BoxAtX30();
        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var mirror = TheMirror(vm);
        vm.SelectOnly([]);

        vm.PickCommand.Execute(new PickRequest(box.Id, false));

        Assert.Equal([mirror.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_mirror_shows_in_the_object_list_with_its_own_glyph()
    {
        var (vm, box) = await BoxAtX30();
        vm.LinkedMirrorCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var item = vm.Nodes.Single(n => n.Id == TheMirror(vm).Id);

        Assert.NotEqual(vm.Nodes.Single(n => n.Id == box.Id).Glyph, item.Glyph);
        Assert.NotEqual(new GroupNode("g").GetType().Name, item.Glyph);
    }

    // ---- at a face ----

    [UiFact]
    public async Task The_mode_for_a_face_is_a_one_shot_mode_that_excludes_the_other_face_modes()
    {
        var (vm, _) = await BoxAtX30();

        vm.LinkedMirrorAtFaceCommand.Execute(null);
        Assert.True(vm.IsPickingMirrorFace);
        Assert.True(vm.IsFacePickActive);
        Assert.Equal(Formwerk.App.Resources.Texts.Get("Hint.MirrorFace"), vm.HintText);

        vm.FaceOnFloorCommand.Execute(null); // another mode takes over
        Assert.False(vm.IsPickingMirrorFace);
        Assert.True(vm.IsPickingFace);

        vm.MirroredCopyAtFaceCommand.Execute(null);
        Assert.True(vm.IsPickingMirrorFace);
        Assert.False(vm.IsPickingFace);

        vm.FacePickedCommand.Execute(null); // Esc
        Assert.False(vm.IsPickingMirrorFace);
        Assert.False(vm.IsFacePickActive);
    }

    [UiFact]
    public async Task A_linked_mirror_at_a_side_face_puts_the_plane_on_that_face()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box"); // 20 mm, centered on the origin: its right face is at X = 10
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.LinkedMirrorAtFaceCommand.Execute(null);

        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(10, 0, 10), Vector3.UnitX));
        await vm.WaitForEvaluationAsync();

        var mirror = TheMirror(vm);
        Assert.False(vm.IsPickingMirrorFace); // one shot
        Assert.Same(box, Assert.Single(mirror.Children));
        Assert.True(Near(TransformOps.WorldPosition(box), Vector3.Zero), "the original did not move");
        var centers = SolidCenters(vm);
        Assert.Equal(2, centers.Count);
        Assert.True(Near(centers[0], new Vector3(0, 0, 10)));
        Assert.True(Near(centers[1], new Vector3(20, 0, 10))); // mirrored at the face: next to it, touching
    }

    [UiFact]
    public async Task A_linked_mirror_at_the_top_face_turns_the_plane_so_that_it_is_horizontal()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.LinkedMirrorAtFaceCommand.Execute(null);

        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(2, 3, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();

        Assert.True(Near(TransformOps.WorldPosition(box), Vector3.Zero), "the original did not move");
        var centers = SolidCenters(vm);
        Assert.True(Near(centers[0], new Vector3(0, 0, 10)));
        Assert.True(Near(centers[1], new Vector3(0, 0, 30))); // the image stands on top
        Assert.All(vm.ViewportItems, i => Assert.Equal(20, i.Mesh.ComputeBounds().Size.Z, 3));
    }

    [UiFact]
    public async Task A_mirrored_copy_at_a_face_leaves_the_original_and_adds_only_the_image()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.MirroredCopyAtFaceCommand.Execute(null);
        var steps = vm.HistoryEntries.Count;

        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(10, 0, 10), Vector3.UnitX));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal("1", TheMirror(vm).Parameters["copyOnly"]);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Equal(2, SolidCenters(vm).Count);
    }

    [UiFact]
    public async Task A_face_of_a_locked_or_unknown_object_is_ignored_and_the_mode_goes_on()
    {
        var (vm, _) = await BoxAtX30();
        vm.LinkedMirrorAtFaceCommand.Execute(null);
        var steps = vm.HistoryEntries.Count;

        vm.FacePickedCommand.Execute(new FacePick(Guid.NewGuid(), Vector3.Zero, Vector3.UnitX));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.True(vm.IsPickingMirrorFace);
    }

    [UiFact]
    public async Task The_mirror_survives_save_and_load()
    {
        var (vm, _) = await BoxAtX30();
        vm.MirroredCopyCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var path = Path.Combine(Path.GetTempPath(), $"mir-{Guid.NewGuid():N}.fwp");

        ProjectFile.SaveAtomic(path, vm.Document);
        var loaded = ProjectFile.Load(path);
        File.Delete(path);

        var copy = loaded.Root.Descendants().OfType<MirrorNode>().Single();
        Assert.Equal("1", copy.Parameters["copyOnly"]);
        Assert.Single(copy.Children);
    }
}
