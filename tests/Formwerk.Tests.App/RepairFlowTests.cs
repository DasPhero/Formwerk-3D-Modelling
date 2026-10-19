using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Repair;

namespace Formwerk.Tests.App;

/// <summary>The mesh check banner and the one-click repair through the view model (F-IMP-03, F-REP-01, F-REP-02, F-REP-04).</summary>
public sealed class RepairFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-repair-app-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public RepairFlowTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static MeshData Box() =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });

    /// <summary>The box without its first triangle: one hole.</summary>
    private static MeshData BoxWithHole()
    {
        var box = Box();
        return new MeshData(box.Vertices.ToArray(), box.Indices.ToArray()[3..]);
    }

    private string WriteStl(string name, MeshData mesh)
    {
        var path = Path.Combine(_folder, name);
        using var stream = File.Create(path);
        StlWriter.WriteBinary(stream, [mesh]);
        return path;
    }

    private async Task<MainViewModel> ImportDamaged(string name = "broken.stl")
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteStl(name, BoxWithHole())]);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        return vm;
    }

    private static double Volume(MainViewModel vm) =>
        vm.ViewportItems.Where(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

    // ---- the banner ------------------------------------------------------------------------------

    [UiFact]
    public async Task A_healthy_import_shows_no_banner()
    {
        var vm = NewViewModel();

        await vm.ImportFilesAsync([WriteStl("fine.stl", Box())]);
        await vm.WaitForRepairCheckAsync();

        Assert.False(vm.IsRepairBannerVisible);
        Assert.False(vm.RepairMeshCommand.CanExecute(null));
    }

    [UiFact]
    public async Task A_damaged_import_shows_what_is_wrong_and_offers_the_repair()
    {
        var vm = await ImportDamaged();

        Assert.True(vm.IsRepairBannerVisible);
        Assert.Contains("broken", vm.RepairBannerText);
        Assert.Contains("1 hole(s)", vm.RepairBannerText);
        Assert.True(vm.RepairMeshCommand.CanExecute(null));
        Assert.Contains("errors", vm.StatusText); // the import notice points to the banner
    }

    [UiFact]
    public async Task The_banner_follows_the_selection()
    {
        var vm = await ImportDamaged();
        var node = vm.Document.Root.Children.Single();

        vm.SelectOnly([]);
        await vm.WaitForRepairCheckAsync();
        Assert.False(vm.IsRepairBannerVisible);

        vm.SelectOnly([node.Id]);
        await vm.WaitForRepairCheckAsync();
        Assert.True(vm.IsRepairBannerVisible);
    }

    [UiFact]
    public async Task Generated_shapes_never_show_a_banner()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("sphere");
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();

        Assert.False(vm.IsRepairBannerVisible);
    }

    // ---- the repair ------------------------------------------------------------------------------

    [UiFact]
    public async Task One_click_repairs_the_mesh_in_one_undo_step()
    {
        var vm = await ImportDamaged();
        var history = vm.HistoryEntries.Count;
        var node = (MeshNode)vm.Document.Root.Children.Single();
        var oldMesh = node.MeshId;

        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();

        Assert.NotEqual(oldMesh, node.MeshId);
        Assert.Equal(history + 1, vm.HistoryEntries.Count);
        Assert.True(vm.Meshes.Get(node.MeshId)!.IsWatertight());
        Assert.False(vm.IsRepairBannerVisible);
        Assert.Equal(6000, Volume(vm), 2);
        Assert.Contains("closed 1 hole(s)", vm.StatusText);
        Assert.Contains("ready", vm.StatusText);
    }

    [UiFact]
    public async Task Undo_brings_back_the_damaged_mesh_and_the_banner_and_redo_repairs_again()
    {
        var vm = await ImportDamaged();
        var node = (MeshNode)vm.Document.Root.Children.Single();
        var oldMesh = node.MeshId;
        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForRepairCheckAsync();
        var repaired = node.MeshId;

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        Assert.Equal(oldMesh, node.MeshId);
        Assert.True(vm.IsRepairBannerVisible);
        Assert.False(vm.Meshes.Get(node.MeshId)!.IsWatertight());

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        Assert.Equal(repaired, node.MeshId);
        Assert.False(vm.IsRepairBannerVisible);
    }

    [UiFact]
    public async Task A_repaired_mesh_can_be_cut_by_a_boolean_and_exported()
    {
        var vm = await ImportDamaged();
        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        var cutter = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "-5";
        vm.Properties.Parameters[0].Text = "4";
        vm.Properties.Parameters[1].Text = "50";
        vm.Properties.IsHole = true;
        vm.SelectOnly([vm.Document.Root.Children[0].Id, cutter.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var solid = vm.ViewportItems.Single(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid);
        Assert.True(solid.Mesh.ComputeVolume() < 6000 - 100); // the bore was cut out
        Assert.False(vm.StatusText.Contains("problem", StringComparison.OrdinalIgnoreCase), vm.StatusText);
    }

    [UiFact]
    public async Task Objects_that_share_a_mesh_are_repaired_together_and_the_mesh_is_repaired_once()
    {
        var vm = await ImportDamaged();
        vm.DuplicateCommand.Execute(null); // the copy points at the same mesh
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly(vm.Document.Root.Children.Select(c => c.Id));
        await vm.WaitForRepairCheckAsync();
        var meshes = vm.Meshes.Count;
        Assert.Contains("2 objects", vm.RepairBannerText);

        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForRepairCheckAsync();

        var nodes = vm.Document.Root.Children.Cast<MeshNode>().ToList();
        Assert.Equal(nodes[0].MeshId, nodes[1].MeshId);
        Assert.Equal(meshes + 1, vm.Meshes.Count);
        Assert.False(vm.IsRepairBannerVisible);
        Assert.Equal(1, vm.HistoryEntries.Count(h => h.Name.StartsWith("Repair", StringComparison.Ordinal)));
    }

    [UiFact]
    public async Task Inside_a_group_only_the_damaged_part_is_repaired()
    {
        var path = Path.Combine(_folder, "pair.3mf");
        ThreeMfWriter.SaveAtomic(path, [
            new ThreeMfObject("Good", Box(), 0xFF112233),
            new ThreeMfObject("Bad", BoxWithHole().Transformed(Matrix4x4.CreateTranslation(50, 0, 0)), 0xFF445566)]);
        var vm = NewViewModel();
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        var group = (GroupNode)vm.Document.Root.Children.Single();
        var good = (MeshNode)group.Children[0];
        var bad = (MeshNode)group.Children[1];
        var goodMesh = good.MeshId;

        Assert.Contains("Bad", vm.RepairBannerText);
        Assert.DoesNotContain("Good", vm.RepairBannerText);

        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForRepairCheckAsync();

        Assert.Equal(goodMesh, good.MeshId);
        Assert.True(vm.Meshes.Get(bad.MeshId)!.IsWatertight());
        Assert.False(vm.IsRepairBannerVisible);
    }

    [UiFact]
    public async Task Problems_that_cannot_be_repaired_are_shown_but_the_button_stays_off()
    {
        // two tetrahedra that share only an edge: that edge belongs to four triangles
        MeshData Tetra(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var mesh = new MeshData([a, b, c, d], [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);
            return mesh.ComputeVolume() >= 0 ? mesh : new MeshData([a, b, c, d], [0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0]);
        }

        var first = Tetra(new(0, 0, 0), new(10, 0, 0), new(0, 10, 0), new(0, 0, 10));
        var second = Tetra(new(0, 0, 0), new(10, 0, 0), new(5, -10, 0), new(5, -3, 10));
        var vertices = first.Vertices.ToArray().Concat(second.Vertices.ToArray()).ToArray();
        var indices = first.Indices.ToArray().Concat(second.Indices.ToArray().Select(i => i + 4)).ToArray();
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteStl("bowtie.stl", new MeshData(vertices, indices))]);
        await vm.WaitForRepairCheckAsync();

        Assert.True(vm.IsRepairBannerVisible);
        Assert.Contains("shared by more than two triangles", vm.RepairBannerText);
        Assert.False(vm.RepairMeshCommand.CanExecute(null));
    }

    [UiFact]
    public async Task A_loaded_project_with_a_damaged_mesh_shows_the_banner_when_it_is_selected()
    {
        var first = await ImportDamaged();
        var project = Path.Combine(_folder, "p.fwp");
        _dialogs.SavePath = project;
        Assert.True(first.Save());

        var second = NewViewModel();
        Assert.True(second.OpenPath(project));
        second.SelectOnly([second.Document.Root.Children.Single().Id]);
        await second.WaitForRepairCheckAsync();

        Assert.True(second.IsRepairBannerVisible);
        Assert.Contains("1 hole(s)", second.RepairBannerText);
    }

    [UiFact]
    public async Task The_repair_result_survives_save_and_open()
    {
        var first = await ImportDamaged();
        await first.RepairMeshCommand.ExecuteAsync(null);
        var project = Path.Combine(_folder, "repaired.fwp");
        _dialogs.SavePath = project;
        Assert.True(first.Save());

        var second = NewViewModel();
        Assert.True(second.OpenPath(project));
        await second.WaitForEvaluationAsync();
        second.SelectOnly([second.Document.Root.Children.Single().Id]);
        await second.WaitForRepairCheckAsync();

        Assert.False(second.IsRepairBannerVisible);
        Assert.Equal(6000, Volume(second), 2);
    }

    // ---- words -----------------------------------------------------------------------------------

    [UiFact]
    public void Problems_are_listed_in_words_and_nothing_is_said_when_all_is_well()
    {
        var report = MeshAnalyzer.Analyze(BoxWithHole());

        Assert.Equal("1 hole(s)", RepairText.Problems(report));
        Assert.Equal("", RepairText.Problems(MeshAnalyzer.Analyze(Box())));
        Assert.Equal("2 hole(s)", RepairText.Problems(report, report)); // several objects add up
    }

    [UiFact]
    public void Actions_are_listed_in_words()
    {
        var result = MeshRepair.Repair(BoxWithHole());

        Assert.Equal("closed 1 hole(s)", RepairText.Actions(result.Actions));
        Assert.Equal("", RepairText.Actions(RepairActions.None));
    }
}
