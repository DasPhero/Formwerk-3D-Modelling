using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Repair;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The banner lists the kinds of problems; a click shows their places in the view (F-REP-01).</summary>
public sealed class RepairHighlightTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-highlight-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public RepairHighlightTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static MeshData Box() =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 10, ["depth"] = 20, ["height"] = 30 });

    private static MeshData BoxWithHole()
    {
        var box = Box();
        return new MeshData(box.Vertices.ToArray(), box.Indices.ToArray()[3..]); // the first triangle is missing
    }

    private static MeshData BoxWithDoubledTriangle()
    {
        var box = Box();
        return new MeshData(box.Vertices.ToArray(), [.. box.Indices.ToArray(), .. box.Indices.ToArray()[..3]]);
    }

    private async Task<MainViewModel> Import(MeshData mesh, string name = "damaged.stl")
    {
        var path = Path.Combine(_folder, name);
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, [mesh]);
        var vm = new MainViewModel(new ManifoldKernel(), _dialogs);
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        return vm;
    }

    private static async Task Highlight(MainViewModel vm, ProblemKind kind)
    {
        vm.HighlightProblemCommand.Execute(vm.RepairProblems.Single(p => p.Kind == kind));
        await vm.WaitForRepairCheckAsync();
    }

    private static Bounds WorldBounds(MainViewModel vm, Node node)
    {
        var parts = vm.ViewportItems.Where(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    // ---- the list --------------------------------------------------------------------------------

    [UiFact]
    public async Task The_banner_lists_the_kinds_of_problems_with_their_counts()
    {
        var vm = await Import(BoxWithHole());

        var item = Assert.Single(vm.RepairProblems);

        Assert.Equal(ProblemKind.OpenEdges, item.Kind);
        Assert.Equal(3, item.Count);
        Assert.False(string.IsNullOrWhiteSpace(item.Label));
        Assert.False(item.IsShown);
        Assert.Null(vm.ProblemMarks);
    }

    [UiFact]
    public async Task A_healthy_mesh_has_no_list()
    {
        var vm = await Import(Box(), "fine.stl");

        Assert.Empty(vm.RepairProblems);
        Assert.False(vm.IsRepairBannerVisible);
    }

    [UiFact]
    public async Task Several_kinds_are_listed_each_with_its_own_count()
    {
        var vm = await Import(BoxWithDoubledTriangle());

        Assert.Contains(vm.RepairProblems, p => p.Kind == ProblemKind.DuplicateTriangles && p.Count == 1);
    }

    // ---- showing the places ----------------------------------------------------------------------

    [UiFact]
    public async Task A_click_shows_the_open_edges_in_world_coordinates()
    {
        var vm = await Import(BoxWithHole());
        var node = vm.Document.Root.Children[0];

        await Highlight(vm, ProblemKind.OpenEdges);

        Assert.True(vm.RepairProblems[0].IsShown);
        var marks = vm.ProblemMarks!;
        Assert.Equal(3, marks.Segments.Count);
        var bounds = WorldBounds(vm, node);
        Assert.All(marks.Segments.SelectMany(s => new[] { s.From, s.To }), p =>
        {
            Assert.InRange(p.X, bounds.Min.X - 0.01f, bounds.Max.X + 0.01f);
            Assert.InRange(p.Y, bounds.Min.Y - 0.01f, bounds.Max.Y + 0.01f);
            Assert.InRange(p.Z, bounds.Min.Z - 0.01f, bounds.Max.Z + 0.01f);
        });
    }

    [UiFact]
    public async Task A_second_click_takes_the_marks_away()
    {
        var vm = await Import(BoxWithHole());
        await Highlight(vm, ProblemKind.OpenEdges);

        await Highlight(vm, ProblemKind.OpenEdges);

        Assert.Null(vm.ProblemMarks);
        Assert.False(vm.RepairProblems[0].IsShown);
    }

    [UiFact]
    public async Task Another_kind_replaces_the_marks()
    {
        var mesh = BoxWithDoubledTriangle();
        var indices = mesh.Indices.ToArray();
        var vm = await Import(new MeshData(mesh.Vertices.ToArray(), [.. indices[..3], .. indices[6..]])); // the second triangle is missing, the first is there twice
        Assert.True(vm.RepairProblems.Count >= 2);

        await Highlight(vm, ProblemKind.OpenEdges);
        Assert.NotEmpty(vm.ProblemMarks!.Segments);

        await Highlight(vm, ProblemKind.DuplicateTriangles);

        Assert.Single(vm.RepairProblems, p => p.IsShown);
        Assert.Equal(ProblemKind.DuplicateTriangles, vm.RepairProblems.Single(p => p.IsShown).Kind);
        Assert.Empty(vm.ProblemMarks!.Segments);
        Assert.Single(vm.ProblemMarks.Points);
    }

    [UiFact]
    public async Task A_new_selection_a_repair_or_a_move_keep_the_marks_where_the_object_is()
    {
        var vm = await Import(BoxWithHole());
        var node = vm.Document.Root.Children[0];
        await Highlight(vm, ProblemKind.OpenEdges);
        var before = vm.ProblemMarks!.Segments[0].From;

        node.Transform = Transform.FromPosition(node.Transform.Position + new Vector3(50, 0, 0));
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();

        Assert.Equal(before.X + 50, vm.ProblemMarks!.Segments[0].From.X, 2);
    }

    [UiFact]
    public async Task Selecting_something_else_removes_the_marks_and_the_list()
    {
        var vm = await Import(BoxWithHole());
        await Highlight(vm, ProblemKind.OpenEdges);

        vm.SelectOnly([]);
        await vm.WaitForRepairCheckAsync();

        Assert.Null(vm.ProblemMarks);
        Assert.Empty(vm.RepairProblems);
    }

    [UiFact]
    public async Task Repairing_removes_the_marks_and_the_list()
    {
        var vm = await Import(BoxWithHole());
        await Highlight(vm, ProblemKind.OpenEdges);

        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();

        Assert.Null(vm.ProblemMarks);
        Assert.Empty(vm.RepairProblems);
    }

    [UiFact]
    public async Task Copies_of_a_damaged_mesh_are_marked_each_where_it_stands()
    {
        var vm = await Import(BoxWithHole());
        var first = vm.Document.Root.Children[0];
        vm.SelectOnly([first.Id]);
        vm.DuplicateCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        await vm.WaitForRepairCheckAsync();

        await Highlight(vm, ProblemKind.OpenEdges);

        Assert.Equal(6, vm.ProblemMarks!.Segments.Count);
        Assert.Equal(6, vm.RepairProblems.Single().Count); // the list counts the damaged mesh objects together, like the banner
    }
}
