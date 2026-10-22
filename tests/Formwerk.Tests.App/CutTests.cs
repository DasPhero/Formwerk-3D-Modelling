using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Cutting objects with another object (F-GRP-03): a box cut by a sphere gives a box with a dent and a sphere segment.</summary>
public sealed class CutTests
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

    private static IEnumerable<ViewportItem> SolidsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid);

    private static double VolumeOf(MainViewModel vm, Guid id) => SolidsOf(vm, id).Sum(i => i.Mesh.ComputeVolume());

    private static double TotalVolume(MainViewModel vm) =>
        vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

    private static float HeightOf(MainViewModel vm, Guid id)
    {
        var bounds = SolidsOf(vm, id).Select(i => i.Mesh.ComputeBounds()).ToList();
        return bounds.Max(b => b.Max.Z) - bounds.Min(b => b.Min.Z);
    }

    /// <summary>A sphere whose center is on the top face of the default box (half of it is inside the box).</summary>
    private static async Task<Node> SphereOnTop(MainViewModel vm, float x = 0, float y = 0)
    {
        var sphere = await Add(vm, "sphere");
        var height = HeightOf(vm, sphere.Id);
        sphere.Transform = Transform.FromPosition(new Vector3(x, y, 20 - height / 2));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        return sphere;
    }

    private static async Task Cut(MainViewModel vm, params Node[] nodes)
    {
        vm.SelectOnly(nodes.Select(n => n.Id));
        await vm.CutCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
    }

    // ---- the acceptance case ---------------------------------------------------------------------

    [UiFact]
    public async Task A_box_cut_by_a_sphere_gives_a_box_with_a_dent_and_a_sphere_segment()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);
        var boxVolume = VolumeOf(vm, box.Id);
        var sphereVolume = VolumeOf(vm, sphere.Id);

        await Cut(vm, box, sphere);

        var nodes = vm.Document.Root.Children;
        Assert.Equal(2, nodes.Count);
        Assert.All(nodes, n => Assert.IsType<MeshNode>(n));
        var outside = nodes.Single(n => n.Name == "Box");
        var inside = nodes.Single(n => n.Name == "Box piece");
        Assert.Equal(sphereVolume / 2, VolumeOf(vm, inside.Id), sphereVolume * 0.03);
        Assert.Equal(boxVolume - VolumeOf(vm, inside.Id), VolumeOf(vm, outside.Id), boxVolume * 0.001);
        Assert.Equal(boxVolume, TotalVolume(vm), boxVolume * 0.001); // nothing lost, the knife is gone
        Assert.DoesNotContain(vm.Document.Root.Children, n => n.Id == sphere.Id || n.Id == box.Id);
        Assert.Equal(nodes.Select(n => n.Id).Order().ToArray(), vm.SelectedIds.Order().ToArray());
    }

    [UiFact]
    public async Task The_parts_are_separate_objects_that_can_be_moved()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);
        await Cut(vm, box, sphere);
        var inside = vm.Document.Root.Children.Single(n => n.Name == "Box piece");
        var before = SolidsOf(vm, inside.Id).Select(i => i.Mesh.ComputeBounds()).First().Min.X;

        inside.Transform = Transform.FromPosition(inside.Transform.Position + new Vector3(50, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        var after = SolidsOf(vm, inside.Id).Select(i => i.Mesh.ComputeBounds()).First().Min.X;
        Assert.Equal(before + 50, after, 2);
    }

    [UiFact]
    public async Task Undo_brings_the_box_and_the_sphere_back_in_one_step_and_redo_cuts_again()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);
        var totalBefore = TotalVolume(vm);
        var steps = vm.HistoryEntries.Count;

        await Cut(vm, box, sphere);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.StartsWith("Cut", vm.HistoryEntries[^1].Name);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal([box.Id, sphere.Id], vm.Document.Root.Children.Select(n => n.Id).ToArray());
        Assert.IsType<PrimitiveNode>(vm.Document.Root.Children[0]);
        Assert.Equal(totalBefore, TotalVolume(vm), totalBefore * 0.001);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.All(vm.Document.Root.Children, n => Assert.IsType<MeshNode>(n));
    }

    // ---- which object is the knife ---------------------------------------------------------------

    [UiFact]
    public async Task The_last_object_in_the_list_is_the_knife()
    {
        var vm = NewViewModel();
        var sphere = await Add(vm, "sphere");
        var box = await Add(vm, "box", new Vector3(0, 0, -(HeightOf(vm, sphere.Id) / 2)));

        await Cut(vm, sphere, box);

        // the box was the knife, so the sphere is what was cut (its names are kept)
        Assert.Equal(["Sphere", "Sphere piece"], vm.Document.Root.Children.Select(n => n.Name).Order().ToArray());
        Assert.DoesNotContain(vm.Document.Root.Children, n => n.Name == "Box");
    }

    [UiFact]
    public async Task Every_other_selected_object_is_cut_by_the_same_knife()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box", new Vector3(-8, 0, 0));
        var b = await Add(vm, "box", new Vector3(8, 0, 0));
        var knife = await SphereOnTop(vm);

        await Cut(vm, a, b, knife);

        var names = vm.Document.Root.Children.Select(n => n.Name).ToList();
        Assert.Equal(4, names.Count);
        Assert.Equal(2, names.Count(n => n.EndsWith(" piece")));
        Assert.Equal(2, names.Count(n => n.StartsWith("Box") && !n.EndsWith(" piece")));
        Assert.DoesNotContain("Sphere", names);
    }

    // ---- nothing to cut --------------------------------------------------------------------------

    [UiFact]
    public async Task An_object_the_knife_misses_stays_as_it_is()
    {
        var vm = NewViewModel();
        var hit = await Add(vm, "box");
        var far = await Add(vm, "box", new Vector3(200, 0, 0));
        var knife = await SphereOnTop(vm);

        await Cut(vm, hit, far, knife);

        Assert.Contains(vm.Document.Root.Children, n => ReferenceEquals(n, far));
        Assert.IsType<PrimitiveNode>(far);
        Assert.Equal(3, vm.Document.Root.Children.Count); // far, the dented box and the segment
    }

    [UiFact]
    public async Task When_the_knife_touches_nothing_nothing_changes_and_a_notice_says_so()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await Add(vm, "sphere", new Vector3(200, 0, 0));
        var steps = vm.HistoryEntries.Count;

        await Cut(vm, box, sphere);

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal([box.Id, sphere.Id], vm.Document.Root.Children.Select(n => n.Id).ToArray());
        Assert.Contains("Nothing was cut", vm.StatusText);
    }

    [UiFact]
    public async Task A_knife_that_swallows_the_object_leaves_it_unchanged()
    {
        var vm = NewViewModel();
        var small = await Add(vm, "box", new Vector3(0, 0, 0));
        small.Transform = Transform.FromPosition(Vector3.Zero);
        var big = await Add(vm, "box");
        ((PrimitiveNode)big).Parameters["width"] = "100";
        ((PrimitiveNode)big).Parameters["depth"] = "100";
        ((PrimitiveNode)big).Parameters["height"] = "100";
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var steps = vm.HistoryEntries.Count;

        await Cut(vm, small, big);

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal([small.Id, big.Id], vm.Document.Root.Children.Select(n => n.Id).ToArray());
    }

    // ---- locked, enabled -------------------------------------------------------------------------

    [UiFact]
    public async Task A_locked_object_is_not_cut_and_a_locked_knife_is_refused()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);

        box.Locked = true;
        await Cut(vm, box, sphere);
        Assert.Equal([box.Id, sphere.Id], vm.Document.Root.Children.Select(n => n.Id).ToArray());

        box.Locked = false;
        sphere.Locked = true;
        await Cut(vm, box, sphere);
        Assert.Equal([box.Id, sphere.Id], vm.Document.Root.Children.Select(n => n.Id).ToArray());
    }

    [UiFact]
    public async Task The_command_needs_two_selected_objects()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);

        vm.SelectOnly([]);
        Assert.False(vm.CutCommand.CanExecute(null));
        vm.SelectOnly([box.Id]);
        Assert.False(vm.CutCommand.CanExecute(null));
        vm.SelectOnly([box.Id, sphere.Id]);
        Assert.True(vm.CutCommand.CanExecute(null));
    }

    // ---- inside a group, saving ------------------------------------------------------------------

    [UiFact]
    public async Task An_object_inside_a_moved_group_is_cut_where_it_is_and_its_parts_stay_in_the_group()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.SelectOnly([box.Id]);
        vm.GroupCommand.Execute(null);
        var group = vm.Document.Root.Children[0];
        group.Transform = Transform.FromPosition(new Vector3(30, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var sphere = await SphereOnTop(vm, x: 30);
        var boxVolume = VolumeOf(vm, box.Id);

        await Cut(vm, box, sphere);

        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(2, group.Children.Count);
        var bounds = group.Children.SelectMany(c => SolidsOf(vm, c.Id)).Select(i => i.Mesh.ComputeBounds()).ToList();
        Assert.Equal(40, bounds.Max(b => b.Max.X), 1); // the box was 20 wide at x = 30
        Assert.Equal(20, bounds.Min(b => b.Min.X), 1);
        Assert.Equal(boxVolume, TotalVolume(vm), boxVolume * 0.001);
    }

    [UiFact]
    public async Task The_parts_survive_saving_and_loading_the_project()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var sphere = await SphereOnTop(vm);
        await Cut(vm, box, sphere);
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        Assert.Equal(2, back.Root.Children.Count);
        Assert.All(back.Root.Children, n => Assert.IsType<MeshNode>(n));
    }
}
