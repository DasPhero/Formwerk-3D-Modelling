using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Flatten (F-GRP-04): an object becomes one mesh, and undo brings the whole tree back.</summary>
public sealed class FlattenTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-flatten-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static IEnumerable<ViewportItem> Solids(MainViewModel vm) => vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid);

    private static Bounds WorldBounds(MainViewModel vm, params Guid[] ids)
    {
        var parts = Solids(vm).Where(i => ids.Contains(i.NodeId)).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static double SolidVolume(MainViewModel vm) => Solids(vm).Sum(i => i.Mesh.ComputeVolume());

    private static void AssertSame(Bounds expected, Bounds actual, int digits = 2)
    {
        Assert.Equal(expected.Min.X, actual.Min.X, digits);
        Assert.Equal(expected.Min.Y, actual.Min.Y, digits);
        Assert.Equal(expected.Min.Z, actual.Min.Z, digits);
        Assert.Equal(expected.Max.X, actual.Max.X, digits);
        Assert.Equal(expected.Max.Y, actual.Max.Y, digits);
        Assert.Equal(expected.Max.Z, actual.Max.Z, digits);
    }

    private async Task Flatten(MainViewModel vm)
    {
        await vm.FlattenCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
    }

    // ---- a single shape --------------------------------------------------------------------------

    [UiFact]
    public async Task A_shape_becomes_a_mesh_with_the_same_name_place_and_look()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        box.Color = 0xFF112233;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var before = WorldBounds(vm, box.Id);
        var entries = vm.HistoryEntries.Count;

        await Flatten(vm);

        var flat = Assert.IsType<MeshNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("Box", flat.Name);
        Assert.Equal(0xFF112233u, flat.Color);
        Assert.Equal(12, vm.Meshes.Get(flat.MeshId)!.TriangleCount);
        Assert.True(vm.Meshes.Get(flat.MeshId)!.IsWatertight());
        AssertSame(before, WorldBounds(vm, flat.Id));
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal("Flatten Box", vm.HistoryEntries[^1].Name);
        Assert.Equal([flat.Id], vm.Selection.ToArray());
        Assert.True(vm.IsDirty);
        Assert.Contains("Flattened Box", vm.StatusText);
    }

    [UiFact]
    public async Task The_mesh_origin_is_the_center_of_the_bottom_like_for_imports()
    {
        var vm = NewViewModel();
        await Add(vm, "sphere");
        var sphere = Child(vm, 0);
        vm.Properties!.Position[0].Text = "40";
        vm.Properties.Position[1].Text = "-15";
        vm.Properties.Position[2].Text = "5";
        await vm.WaitForEvaluationAsync();

        await Flatten(vm);

        var flat = (MeshNode)Child(vm, 0);
        var bounds = vm.Meshes.Get(flat.MeshId)!.ComputeBounds();
        Assert.Equal(0, bounds.Center.X, 3);
        Assert.Equal(0, bounds.Center.Y, 3);
        Assert.Equal(0, bounds.Min.Z, 3);
        Assert.Equal(40, flat.Transform.Position.X, 2);
        Assert.Equal(-15, flat.Transform.Position.Y, 2);
        Assert.Equal(5, flat.Transform.Position.Z, 2);
    }

    [UiFact]
    public async Task Undo_brings_the_shape_back_with_its_parameters_and_redo_flattens_again()
    {
        var vm = NewViewModel();
        await Add(vm, "cylinder");
        var cylinder = (PrimitiveNode)Child(vm, 0);
        vm.Properties!.Parameters[0].Text = "33";
        await vm.WaitForEvaluationAsync();

        await Flatten(vm);
        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Same(cylinder, Child(vm, 0));
        Assert.Equal("33", cylinder.Parameters["diameter"]);
        Assert.Equal(33, WorldBounds(vm, cylinder.Id).Size.X, 2);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.IsType<MeshNode>(Child(vm, 0));
    }

    // ---- groups, holes, booleans -----------------------------------------------------------------

    [UiFact]
    public async Task A_group_becomes_one_mesh_and_undo_gives_the_group_with_its_children_back()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var box = Child(vm, 0);
        var sphere = Child(vm, 1);
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        var before = WorldBounds(vm, box.Id, sphere.Id);
        var volume = SolidVolume(vm);
        vm.SelectOnly([group.Id]);

        await Flatten(vm);

        var flat = Assert.IsType<MeshNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(group.Name, flat.Name);
        Assert.Single(Solids(vm));
        AssertSame(before, WorldBounds(vm, flat.Id));
        Assert.Equal(volume, SolidVolume(vm), 2);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Same(group, Child(vm, 0));
        Assert.Equal(["Box", "Sphere"], group.Children.Select(c => c.Name).ToArray());
        Assert.Equal(2, Solids(vm).Count());
        AssertSame(before, WorldBounds(vm, box.Id, sphere.Id));
    }

    [UiFact]
    public async Task A_group_with_a_hole_is_flattened_with_the_cut_applied_and_without_the_ghost()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Child(vm, 0);
        vm.Properties!.Parameters[0].Text = "40";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Parameters[2].Text = "40";
        await Add(vm, "cylinder");
        var hole = Child(vm, 1);
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "-5";
        vm.Properties.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "50";
        vm.Properties.IsHole = true;
        vm.SelectOnly([box.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var cutVolume = SolidVolume(vm);
        Assert.Contains(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);

        await Flatten(vm);

        var flat = Assert.IsType<MeshNode>(Assert.Single(vm.Document.Root.Children));
        Assert.True(vm.Meshes.Get(flat.MeshId)!.IsWatertight());
        Assert.Equal(cutVolume, SolidVolume(vm), 2);
        Assert.True(cutVolume < 64000 - 500);
        Assert.DoesNotContain(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost); // the hole did its work
    }

    [UiFact]
    public async Task A_boolean_node_becomes_a_mesh_with_the_result_of_the_operation()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.Properties!.Position[0].Text = "5";
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Difference);
        await vm.WaitForEvaluationAsync();
        var volume = SolidVolume(vm);
        var boolean = Child(vm, 0);
        vm.SelectOnly([boolean.Id]);

        await Flatten(vm);

        Assert.IsType<MeshNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(volume, SolidVolume(vm), 2);
        Assert.Single(Solids(vm));
    }

    [UiFact]
    public async Task Hidden_parts_are_not_part_of_the_mesh()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Child(vm, 0);
        group.Children[1].Visible = false;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([group.Id]);

        await Flatten(vm);

        var flat = (MeshNode)Child(vm, 0);
        Assert.Equal(12, vm.Meshes.Get(flat.MeshId)!.TriangleCount); // one box, not two
    }

    // ---- places in the tree ----------------------------------------------------------------------

    [UiFact]
    public async Task An_object_inside_a_moved_and_turned_group_stays_exactly_where_it_is()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        await Add(vm, "sphere");
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var inner = Child(vm, 0);
        var sphere = Child(vm, 1);
        vm.SelectOnly([inner.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var outer = Child(vm, 0);
        outer.Transform = new Transform(new Vector3(50, 20, 3), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f), Vector3.One);
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var innerIds = inner.Descendants().Select(d => d.Id).Append(inner.Id).ToArray();
        var before = WorldBounds(vm, innerIds);
        var volume = Solids(vm).Where(i => innerIds.Contains(i.NodeId)).Sum(i => i.Mesh.ComputeVolume());
        vm.SelectOnly([inner.Id]);

        await Flatten(vm);

        var flat = outer.Children.OfType<MeshNode>().Single();
        Assert.Equal(2, outer.Children.Count);
        AssertSame(before, WorldBounds(vm, flat.Id));
        Assert.Equal(volume, Solids(vm).Where(i => i.NodeId == flat.Id).Sum(i => i.Mesh.ComputeVolume()), 2);
        Assert.Equal(inner.Name, flat.Name);
    }

    [UiFact]
    public async Task Several_selected_objects_are_flattened_one_by_one_in_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        var originals = vm.Document.Root.Children.ToList();
        vm.SelectOnly(originals.Select(o => o.Id));
        var entries = vm.HistoryEntries.Count;
        var volume = SolidVolume(vm);

        await Flatten(vm);

        Assert.Equal(["Box", "Sphere", "Cylinder"], vm.Document.Root.Children.Select(c => c.Name).ToArray()); // same order
        Assert.All(vm.Document.Root.Children, c => Assert.IsType<MeshNode>(c));
        Assert.Equal(entries + 1, vm.HistoryEntries.Count);
        Assert.Equal(volume, SolidVolume(vm), 2);
        Assert.Equal(3, vm.Selection.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(originals, vm.Document.Root.Children.ToList());
    }

    // ---- what is skipped -------------------------------------------------------------------------

    [UiFact]
    public async Task A_mesh_cannot_be_flattened_any_more()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Flatten(vm);
        var entries = vm.HistoryEntries.Count;

        Assert.False(vm.FlattenCommand.CanExecute(null));
        await Flatten(vm);

        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Contains("nothing to flatten", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task Meshes_in_a_mixed_selection_are_skipped_and_the_rest_is_flattened()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly([Child(vm, 0).Id]);
        await Flatten(vm);
        var mesh = Child(vm, 0);
        vm.SelectOnly(vm.Document.Root.Children.Select(c => c.Id));

        await Flatten(vm);

        Assert.Same(mesh, Child(vm, 0));
        Assert.IsType<MeshNode>(Child(vm, 1));
    }

    [UiFact]
    public async Task A_lone_hole_has_nothing_solid_and_is_left_alone()
    {
        var vm = NewViewModel();
        await Add(vm, "cylinder");
        var hole = Child(vm, 0);
        vm.ToggleHole();
        await vm.WaitForEvaluationAsync();
        var entries = vm.HistoryEntries.Count;

        await Flatten(vm);

        Assert.Same(hole, Child(vm, 0));
        Assert.Equal(entries, vm.HistoryEntries.Count);
        Assert.Contains("nothing to flatten", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task A_locked_object_is_not_flattened()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Child(vm, 0).Locked = true;
        var entries = vm.HistoryEntries.Count;

        await Flatten(vm);

        Assert.IsType<PrimitiveNode>(Child(vm, 0));
        Assert.Equal(entries, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task Without_a_selection_the_command_is_off()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.SelectOnly([]);

        Assert.False(vm.FlattenCommand.CanExecute(null));
    }

    // ---- with the rest of the program ------------------------------------------------------------

    [UiFact]
    public async Task A_flattened_object_survives_saving_and_opening()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        vm.SelectOnly([Child(vm, 0).Id, Child(vm, 1).Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        await Flatten(vm);
        var triangles = Solids(vm).Sum(i => (long)i.Mesh.TriangleCount);
        Directory.CreateDirectory(_folder);
        _dialogs.SavePath = Path.Combine(_folder, "flat.fwp");
        Assert.True(vm.Save());

        var other = NewViewModel();
        Assert.True(other.OpenPath(_dialogs.SavePath));
        await other.WaitForEvaluationAsync();

        Assert.IsType<MeshNode>(Assert.Single(other.Document.Root.Children));
        Assert.Equal(triangles, Solids(other).Sum(i => (long)i.Mesh.TriangleCount));
    }

    [UiFact]
    public async Task The_flattened_object_can_be_moved_like_any_other()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Flatten(vm);
        var flat = Child(vm, 0);

        Assert.True(vm.BeginDrag(GizmoHandleKind.MoveX));
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.MoveX, new Vector3(25, 0, 0), 0, false));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Equal(25, flat.Transform.Position.X, 3);
        Assert.Equal(25, WorldBounds(vm, flat.Id).Center.X, 2);
    }
}
