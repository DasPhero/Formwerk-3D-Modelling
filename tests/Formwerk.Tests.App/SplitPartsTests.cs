using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Splitting an object into its loose parts (F-SPL-03): an import with 5 single parts gives 5 objects.</summary>
public sealed class SplitPartsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-split-{Guid.NewGuid():N}");

    public SplitPartsTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static MeshData Box() =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });

    /// <summary>One mesh with several boxes side by side, 30 mm apart along X.</summary>
    private static MeshData Boxes(int count) =>
        MeshCombine.Concat(Enumerable.Range(0, count).Select(i => Box().Transformed(Matrix4x4.CreateTranslation(i * 30, 0, 0))));

    private async Task<MainViewModel> ImportBoxes(int count, string name = "parts.stl")
    {
        var path = Path.Combine(_folder, name);
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, [Boxes(count)]);
        var vm = NewViewModel();
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        return vm;
    }

    private static async Task Add(MainViewModel vm, string shape, Vector3? position = null)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        if (position is not { } p) return;
        vm.Document.Root.Children[^1].Transform = Transform.FromPosition(p);
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
    }

    private static IEnumerable<ViewportItem> SolidsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid);

    private static double VolumeOf(MainViewModel vm, Guid id) => SolidsOf(vm, id).Sum(i => i.Mesh.ComputeVolume());

    private static Bounds BoundsOf(MainViewModel vm, params Guid[] ids)
    {
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static async Task Split(MainViewModel vm, params Node[] nodes)
    {
        vm.SelectOnly(nodes.Select(n => n.Id));
        await vm.SplitPartsCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
    }

    // ---- the acceptance case ---------------------------------------------------------------------

    [UiFact]
    public async Task An_import_with_five_single_parts_gives_five_objects()
    {
        var vm = await ImportBoxes(5);
        var imported = Assert.Single(vm.Document.Root.Children);
        var bounds = BoundsOf(vm, imported.Id);

        await Split(vm, imported);

        var nodes = vm.Document.Root.Children;
        Assert.Equal(5, nodes.Count);
        Assert.All(nodes, n => Assert.IsType<MeshNode>(n));
        Assert.Equal(5, nodes.Select(n => n.Name).Distinct().Count());
        Assert.All(nodes, n => Assert.StartsWith(imported.Name, n.Name));
        Assert.All(nodes, n => Assert.Equal(Box().ComputeVolume(), VolumeOf(vm, n.Id), 3));
        // nothing moved: all together they fill the same space as before
        var after = BoundsOf(vm, [.. nodes.Select(n => n.Id)]);
        Assert.Equal(bounds.Min.X, after.Min.X, 2);
        Assert.Equal(bounds.Max.X, after.Max.X, 2);
        Assert.Equal(bounds.Min.Z, after.Min.Z, 2);
        Assert.Equal(bounds.Max.Z, after.Max.Z, 2);
        Assert.Equal(nodes.Select(n => n.Id).Order().ToArray(), vm.SelectedIds.Order().ToArray());
    }

    [UiFact]
    public async Task The_parts_are_numbered_from_left_to_right_and_are_separate_movable_objects()
    {
        var vm = await ImportBoxes(3);
        await Split(vm, vm.Document.Root.Children[0]);
        var nodes = vm.Document.Root.Children;

        var lefts = nodes.Select(n => BoundsOf(vm, n.Id).Min.X).ToArray();
        Assert.Equal(lefts.Order().ToArray(), lefts);
        Assert.EndsWith(" 1", nodes[0].Name);
        Assert.EndsWith(" 3", nodes[2].Name);

        var before = BoundsOf(vm, nodes[1].Id).Min.X;
        nodes[1].Transform = Transform.FromPosition(nodes[1].Transform.Position + new Vector3(0, 0, 40));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before, BoundsOf(vm, nodes[1].Id).Min.X, 2);
        Assert.Equal(40, BoundsOf(vm, nodes[1].Id).Min.Z, 2);
    }

    [UiFact]
    public async Task The_color_is_kept()
    {
        var vm = await ImportBoxes(2);
        var imported = vm.Document.Root.Children[0];
        imported.Color = 0xFF336699;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        await Split(vm, imported);

        Assert.All(vm.Document.Root.Children, n => Assert.Equal(0xFF336699u, n.Color));
    }

    [UiFact]
    public async Task Undo_brings_the_one_object_back_in_one_step_and_redo_splits_again()
    {
        var vm = await ImportBoxes(4);
        var imported = vm.Document.Root.Children[0];
        var steps = vm.HistoryEntries.Count;

        await Split(vm, imported);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.StartsWith("Split", vm.HistoryEntries[^1].Name);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(imported, Assert.Single(vm.Document.Root.Children));

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(4, vm.Document.Root.Children.Count);
    }

    // ---- other objects ---------------------------------------------------------------------------

    [UiFact]
    public async Task A_group_of_separate_bodies_becomes_one_object_per_body()
    {
        var vm = NewViewModel();
        await Add(vm, "box", new Vector3(-30, 0, 0));
        await Add(vm, "box", new Vector3(0, 0, 0));
        await Add(vm, "box", new Vector3(30, 0, 0));
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Assert.Single(vm.Document.Root.Children);

        await Split(vm, group);

        Assert.Equal(3, vm.Document.Root.Children.Count);
        Assert.All(vm.Document.Root.Children, n => Assert.IsType<MeshNode>(n));
    }

    [UiFact]
    public async Task Parts_inside_a_moved_group_stay_where_they_were_and_in_the_group()
    {
        var vm = await ImportBoxes(3);
        var imported = vm.Document.Root.Children[0];
        vm.SelectOnly([imported.Id]);
        vm.GroupCommand.Execute(null);
        var group = vm.Document.Root.Children[0];
        group.Transform = Transform.FromPosition(new Vector3(0, 50, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        var bounds = BoundsOf(vm, imported.Id);

        await Split(vm, imported);

        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(3, group.Children.Count);
        var after = BoundsOf(vm, [.. group.Children.Select(n => n.Id)]);
        Assert.Equal(bounds.Min.Y, after.Min.Y, 2);
        Assert.Equal(bounds.Max.X, after.Max.X, 2);
    }

    [UiFact]
    public async Task A_hollow_body_is_one_part_and_nothing_changes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var outer = (PrimitiveNode)vm.Document.Root.Children[^1];
        outer.Parameters["width"] = "40";
        outer.Parameters["depth"] = "40";
        outer.Parameters["height"] = "40";
        await Add(vm, "box", new Vector3(0, 0, 10));
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        vm.BooleanCommand.Execute(BooleanOperation.Difference);
        await vm.WaitForEvaluationAsync();
        var hollow = Assert.Single(vm.Document.Root.Children);
        var steps = vm.HistoryEntries.Count;

        await Split(vm, hollow);

        Assert.Same(hollow, Assert.Single(vm.Document.Root.Children));
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Contains("already", vm.StatusText);
    }

    [UiFact]
    public async Task One_shell_gives_a_notice_and_no_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = vm.Document.Root.Children[0];
        var steps = vm.HistoryEntries.Count;

        await Split(vm, box);

        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Contains("already", vm.StatusText);
    }

    [UiFact]
    public async Task A_locked_object_is_not_split()
    {
        var vm = await ImportBoxes(3);
        var imported = vm.Document.Root.Children[0];
        imported.Locked = true;

        await Split(vm, imported);

        Assert.Same(imported, Assert.Single(vm.Document.Root.Children));
    }

    [UiFact]
    public async Task Several_selected_objects_are_split_in_one_step()
    {
        var vm = await ImportBoxes(2, "one.stl");
        var first = vm.Document.Root.Children[0];
        var path = Path.Combine(_folder, "two.stl");
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, [Boxes(3)]);
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        var second = vm.Document.Root.Children[^1];
        var steps = vm.HistoryEntries.Count;

        await Split(vm, first, second);

        Assert.Equal(5, vm.Document.Root.Children.Count);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task The_command_needs_a_selection()
    {
        var vm = await ImportBoxes(2);

        vm.SelectOnly([]);
        Assert.False(vm.SplitPartsCommand.CanExecute(null));
        vm.SelectOnly([vm.Document.Root.Children[0].Id]);
        Assert.True(vm.SplitPartsCommand.CanExecute(null));
    }

    [UiFact]
    public async Task The_parts_survive_saving_and_loading_the_project()
    {
        var vm = await ImportBoxes(3);
        await Split(vm, vm.Document.Root.Children[0]);
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new MeshStore());

        Assert.Equal(3, back.Root.Children.Count);
        Assert.All(back.Root.Children, n => Assert.IsType<MeshNode>(n));
    }
}
