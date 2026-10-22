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

/// <summary>The old version of a repaired mesh laid over the repaired one (F-REP-04).</summary>
public sealed class RepairBeforeAfterTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-before-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new() { UnsavedAnswer = UnsavedChoice.Discard };

    public RepairBeforeAfterTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static MeshData BoxWithHole()
    {
        var box = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });
        return new MeshData(box.Vertices.ToArray(), box.Indices.ToArray()[3..]); // the first triangle is missing
    }

    private async Task<MainViewModel> ImportDamaged()
    {
        var path = Path.Combine(_folder, "damaged.stl");
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, [BoxWithHole()]);
        var vm = new MainViewModel(new ManifoldKernel(), _dialogs);
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        return vm;
    }

    private static async Task Repair(MainViewModel vm)
    {
        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
    }

    private static List<ViewportItem> Before(MainViewModel vm) =>
        [.. vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Ghost && vm.Document.FindNode(i.NodeId) is null)];

    private static Bounds BoundsOf(IEnumerable<ViewportItem> items)
    {
        var bounds = items.Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(bounds.Select(b => b.Min).Aggregate(Vector3.Min), bounds.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    [UiFact]
    public async Task Before_a_repair_there_is_nothing_to_compare()
    {
        var vm = await ImportDamaged();

        Assert.False(vm.HasRepairComparison);

        vm.ShowRepairBefore = true;

        Assert.False(vm.ShowRepairBefore); // nothing to show: the switch goes off by itself
        Assert.Empty(Before(vm));
    }

    [UiFact]
    public async Task After_a_repair_the_old_version_is_laid_over_the_new_one()
    {
        var vm = await ImportDamaged();
        var node = vm.Document.Root.Children[0];
        await Repair(vm);
        Assert.True(vm.HasRepairComparison);
        var repaired = vm.ViewportItems.Single(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid);

        vm.ShowRepairBefore = true;

        var ghosts = Before(vm);
        var ghost = Assert.Single(ghosts);
        Assert.Equal(BoxWithHole().TriangleCount, ghost.Mesh.TriangleCount); // the old mesh has the hole, so one triangle less
        Assert.True(ghost.Mesh.TriangleCount < repaired.Mesh.TriangleCount);
        var a = ghost.Mesh.ComputeBounds();
        var b = repaired.Mesh.ComputeBounds();
        Assert.True(Vector3.Distance(a.Center, b.Center) < 0.01f); // at the same place
        Assert.True(Vector3.Distance(a.Size, b.Size) < 0.01f);
        Assert.Contains(vm.ViewportItems, i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid); // the repaired mesh is still shown
    }

    [UiFact]
    public async Task Switching_off_takes_the_old_version_away()
    {
        var vm = await ImportDamaged();
        await Repair(vm);
        vm.ShowRepairBefore = true;

        vm.ShowRepairBefore = false;

        Assert.Empty(Before(vm));
    }

    [UiFact]
    public async Task Undo_of_the_repair_ends_the_comparison_and_redo_brings_it_back()
    {
        var vm = await ImportDamaged();
        await Repair(vm);
        vm.ShowRepairBefore = true;

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.False(vm.HasRepairComparison);
        Assert.False(vm.ShowRepairBefore);
        Assert.Empty(Before(vm));

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.True(vm.HasRepairComparison);
        Assert.False(vm.ShowRepairBefore); // it was switched off, it stays off
    }

    [UiFact]
    public async Task The_old_version_follows_a_moved_object()
    {
        var vm = await ImportDamaged();
        var node = vm.Document.Root.Children[0];
        await Repair(vm);
        vm.ShowRepairBefore = true;
        var before = BoundsOf(Before(vm)).Center;

        node.Transform = Transform.FromPosition(node.Transform.Position + new Vector3(50, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();

        Assert.Equal(before.X + 50, BoundsOf(Before(vm)).Center.X, 2);
    }

    [UiFact]
    public async Task The_overlay_changes_nothing_in_the_document_the_history_or_the_exports()
    {
        var vm = await ImportDamaged();
        await Repair(vm);
        var steps = vm.HistoryEntries.Count;
        var revision = vm.Document.Revision;

        vm.ShowRepairBefore = true;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal(revision, vm.Document.Revision);
        Assert.Single(vm.Document.Root.Children);
        Assert.True(vm.ExportStlCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Copies_of_the_repaired_mesh_each_show_their_old_version()
    {
        var vm = await ImportDamaged();
        var first = vm.Document.Root.Children[0];
        vm.SelectOnly([first.Id]);
        vm.DuplicateCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        await vm.WaitForRepairCheckAsync();
        await Repair(vm);

        vm.ShowRepairBefore = true;

        Assert.Equal(2, Before(vm).Count);
    }

    [UiFact]
    public async Task A_new_project_has_nothing_to_compare()
    {
        var vm = await ImportDamaged();
        await Repair(vm);
        vm.ShowRepairBefore = true;

        vm.NewCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.False(vm.HasRepairComparison);
        Assert.False(vm.ShowRepairBefore);
        Assert.Empty(Before(vm));
    }
}
