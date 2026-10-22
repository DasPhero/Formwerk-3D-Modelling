using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Repair;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Splitting objects at the plane of the section view into two closed parts (F-SPL-01).</summary>
public sealed class SplitAtPlaneTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<Node> Add(MainViewModel vm, string shape, Vector3? position = null)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = vm.Document.Root.Children[^1];
        if (position is { } p)
        {
            node.Transform = Transform.FromPosition(p);
            vm.Document.NotifyChanged();
            await vm.WaitForEvaluationAsync();
        }

        return node;
    }

    // While the section view is on the viewport only shows what is behind the plane, so the parts are measured from their meshes
    // (the test objects are only moved, so the world position is the mesh plus the position of the node).
    private static MeshData? MeshOf(MainViewModel vm, Guid id) =>
        vm.Document.FindNode(id) is MeshNode node ? vm.Meshes.Get(node.MeshId)?.Transformed(Matrix4x4.CreateTranslation(node.Transform.Position)) : null;

    private static IEnumerable<MeshData> WorldMeshes(MainViewModel vm, Guid id) =>
        MeshOf(vm, id) is { } mesh ? [mesh] : vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh);

    private static double VolumeOf(MainViewModel vm, Guid id) => WorldMeshes(vm, id).Sum(m => m.ComputeVolume());

    private static Bounds BoundsOf(MainViewModel vm, params Guid[] ids)
    {
        var parts = ids.SelectMany(id => WorldMeshes(vm, id)).Select(m => m.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    /// <summary>Switches the section view on across an axis at a world coordinate.</summary>
    private static async Task Plane(MainViewModel vm, int axis, double position)
    {
        vm.IsSectionActive = true;
        vm.SectionAxis = axis;
        vm.SectionPosition = position;
        await vm.WaitForEvaluationAsync();
    }

    private static async Task SplitHere(MainViewModel vm)
    {
        await vm.SplitAtPlaneCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
    }

    private static void Pick(MainViewModel vm, Guid id, Vector3 point, Vector3 normal) =>
        vm.FacePickedCommand.Execute(new FacePick(id, point, normal));

    // ---- the acceptance case ---------------------------------------------------------------------

    [UiFact]
    public async Task A_box_split_across_the_middle_gives_two_closed_halves_named_A_and_B()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var volume = VolumeOf(vm, box.Id);
        await Plane(vm, 0, 0); // across X at the middle of the box
        vm.SelectOnly([box.Id]);

        await SplitHere(vm);

        var nodes = vm.Document.Root.Children;
        Assert.Equal(["Box A", "Box B"], nodes.Select(n => n.Name).ToArray());
        Assert.All(nodes, n => Assert.IsType<MeshNode>(n));
        foreach (var node in nodes)
        {
            Assert.All(WorldMeshes(vm, node.Id), m => Assert.True(m.IsWatertight()));
            Assert.Equal(volume / 2, VolumeOf(vm, node.Id), volume * 0.001);
        }

        Assert.Equal(0, BoundsOf(vm, nodes[0].Id).Max.X, 3); // A is behind the plane: the smaller x
        Assert.Equal(0, BoundsOf(vm, nodes[1].Id).Min.X, 3);
        Assert.Equal(nodes.Select(n => n.Id).Order().ToArray(), vm.SelectedIds.Order().ToArray());
        Assert.True(vm.IsSectionActive);
    }

    [UiFact]
    public async Task Undo_brings_the_box_back_in_one_step_and_redo_splits_again()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        await Plane(vm, 2, 10);
        vm.SelectOnly([box.Id]);
        var steps = vm.HistoryEntries.Count;

        await SplitHere(vm);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.StartsWith("Split", vm.HistoryEntries[^1].Name);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(box, Assert.Single(vm.Document.Root.Children));

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task The_color_and_the_place_are_kept()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box", new Vector3(40, 10, 0));
        box.Color = 0xFF336699;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var before = BoundsOf(vm, box.Id);
        await Plane(vm, 0, 40);
        vm.SelectOnly([box.Id]);

        await SplitHere(vm);

        var ids = vm.Document.Root.Children.Select(n => n.Id).ToArray();
        Assert.All(vm.Document.Root.Children, n => Assert.Equal(0xFF336699u, n.Color));
        var after = BoundsOf(vm, ids);
        Assert.Equal(before.Min.X, after.Min.X, 3);
        Assert.Equal(before.Max.X, after.Max.X, 3);
        Assert.Equal(before.Min.Z, after.Min.Z, 3);
        Assert.Equal(before.Max.Y, after.Max.Y, 3);
    }

    // ---- which objects ---------------------------------------------------------------------------

    [UiFact]
    public async Task Without_a_selection_everything_the_plane_goes_through_is_split()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box", new Vector3(0, 0, 0));
        var far = await Add(vm, "box", new Vector3(200, 0, 0));
        var b = await Add(vm, "cylinder", new Vector3(0, 40, 0));
        await Plane(vm, 0, 0);
        vm.SelectOnly([]);

        await SplitHere(vm);

        var names = vm.Document.Root.Children.Select(n => n.Name).ToList();
        Assert.Contains(far, vm.Document.Root.Children); // the plane misses it
        Assert.DoesNotContain(a, vm.Document.Root.Children);
        Assert.DoesNotContain(b, vm.Document.Root.Children);
        Assert.Equal(5, names.Count);
    }

    [UiFact]
    public async Task Only_the_selected_objects_are_split()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box", new Vector3(0, 0, 0));
        var b = await Add(vm, "box", new Vector3(0, 40, 0));
        await Plane(vm, 0, 0);
        vm.SelectOnly([a.Id]);

        await SplitHere(vm);

        Assert.Contains(b, vm.Document.Root.Children);
        Assert.Equal(3, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task A_locked_object_is_not_split()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        box.Locked = true;
        await Plane(vm, 0, 0);
        vm.SelectOnly([box.Id]);

        await SplitHere(vm);

        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
    }

    [UiFact]
    public async Task When_the_plane_misses_everything_nothing_changes_and_a_notice_says_so()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        await Plane(vm, 2, 100);
        vm.SelectOnly([box.Id]);
        var steps = vm.HistoryEntries.Count;

        await SplitHere(vm);

        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Contains("does not go through", vm.StatusText);
    }

    [UiFact]
    public async Task A_plane_through_the_corners_of_a_round_body_gives_parts_without_problems()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder");
        await Plane(vm, 0, 0); // goes exactly through corners of the 64-sided cylinder
        vm.SelectOnly([cylinder.Id]);

        await SplitHere(vm);

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.All(vm.Document.Root.Children, n => Assert.True(MeshAnalyzer.Analyze(MeshOf(vm, n.Id)!).IsHealthy));
    }

    [UiFact]
    public async Task The_flip_decides_which_side_is_A()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        await Plane(vm, 0, 5);
        vm.SectionFlip = true;
        vm.SelectOnly([box.Id]);

        await SplitHere(vm);

        // flipped, the kept side (A) is the one with the larger x: from 5 to 10, 5 mm of 20
        Assert.Equal(5 * 20 * 20, VolumeOf(vm, vm.Document.Root.Children.Single(n => n.Name == "Box A").Id), 3);
        Assert.Equal(15 * 20 * 20, VolumeOf(vm, vm.Document.Root.Children.Single(n => n.Name == "Box B").Id), 3);
    }

    // ---- the command and the planes --------------------------------------------------------------

    [UiFact]
    public async Task The_command_needs_the_section_view_and_something_to_split()
    {
        var vm = NewViewModel();
        Assert.False(vm.SplitAtPlaneCommand.CanExecute(null));

        await Add(vm, "box");
        Assert.False(vm.SplitAtPlaneCommand.CanExecute(null)); // no section view yet

        vm.IsSectionActive = true;
        Assert.True(vm.SplitAtPlaneCommand.CanExecute(null));
        vm.IsSectionActive = false;
        Assert.False(vm.SplitAtPlaneCommand.CanExecute(null));
    }

    [UiFact]
    public async Task A_plane_on_a_clicked_face_splits_there()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.SelectOnly([box.Id]);
        vm.SectionAtFaceCommand.Execute(null);

        Pick(vm, box.Id, new Vector3(10, 0, 5), Vector3.UnitX); // the right face of a 20 mm box: x = 10
        await vm.WaitForEvaluationAsync();
        Assert.Equal(10, vm.SectionPosition, 3);
        vm.SectionPosition = 0;
        await vm.WaitForEvaluationAsync();
        await SplitHere(vm);

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.All(vm.Document.Root.Children, n => Assert.Equal(4000, VolumeOf(vm, n.Id), 2));
    }

    [UiFact]
    public async Task Three_clicked_points_make_the_plane()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.SelectOnly([box.Id]);

        vm.SectionAtPointsCommand.Execute(null);
        Assert.True(vm.IsPickingSectionFace);
        Pick(vm, box.Id, new Vector3(0, 0, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 10, 5), Vector3.UnitY);
        Assert.False(vm.IsSectionActive);
        Pick(vm, box.Id, new Vector3(0, 0, 15), Vector3.UnitY);
        await vm.WaitForEvaluationAsync();

        Assert.True(vm.IsSectionActive);
        Assert.False(vm.IsPickingSectionFace);
        Assert.Equal(3, vm.SectionAxis);
        Assert.Equal(0, vm.SectionPosition, 3);

        await SplitHere(vm);

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.All(vm.Document.Root.Children, n => Assert.Equal(4000, VolumeOf(vm, n.Id), 2));
    }

    [UiFact]
    public async Task Three_points_in_a_line_are_refused_and_the_clicking_starts_again()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.SectionAtPointsCommand.Execute(null);

        Pick(vm, box.Id, new Vector3(0, 0, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 5, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 10, 5), Vector3.UnitY);

        Assert.False(vm.IsSectionActive);
        Assert.True(vm.IsPickingSectionFace);
        Assert.Contains("one line", vm.StatusText);

        // a good set afterwards works
        Pick(vm, box.Id, new Vector3(0, 0, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 10, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 0, 15), Vector3.UnitY);
        Assert.True(vm.IsSectionActive);
    }

    [UiFact]
    public async Task Ending_the_mode_forgets_the_points_clicked_so_far()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");

        vm.SectionAtPointsCommand.Execute(null);
        Pick(vm, box.Id, new Vector3(0, 0, 5), Vector3.UnitY);
        Pick(vm, box.Id, new Vector3(0, 10, 5), Vector3.UnitY);
        vm.SectionAtPointsCommand.Execute(null); // off
        Assert.False(vm.IsPickingSectionFace);

        vm.SectionAtPointsCommand.Execute(null);
        Pick(vm, box.Id, new Vector3(0, 0, 15), Vector3.UnitY); // the first point of the new round
        Assert.False(vm.IsSectionActive);
    }

    [UiFact]
    public async Task The_parts_survive_saving_and_loading_the_project()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        await Plane(vm, 0, 0);
        vm.SelectOnly([box.Id]);
        await SplitHere(vm);
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        Assert.Equal(2, back.Root.Children.Count);
        Assert.All(back.Root.Children, n => Assert.IsType<MeshNode>(n));
    }
}
