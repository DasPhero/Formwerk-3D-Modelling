using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>All twelve basic shapes through the view model: adding, the properties panel, typing, scale handles, saving (F-LIB-01, F-PAR-01, F-SCL-05).</summary>
public sealed class ShapePanelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-shapes-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static IReadOnlyList<IShapeGenerator> Generators => [.. ShapeLibrary.All];

    private static async Task Add(MainViewModel vm, string id)
    {
        vm.AddShapeCommand.Execute(id);
        await vm.WaitForEvaluationAsync();
    }

    private static Bounds BoundsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();

    [UiFact]
    public void The_shape_panel_offers_all_twelve_shapes()
    {
        var vm = NewViewModel();

        Assert.Equal(12, vm.Shapes.Count);
        Assert.Equal(
            ["box", "capsule", "cone", "cylinder", "frustum", "hemisphere", "prism", "pyramid", "sphere", "torus", "tube", "wedge"],
            vm.Shapes.Select(s => s.Id).Order().ToArray());
        Assert.All(vm.Shapes, s => Assert.False(string.IsNullOrWhiteSpace(s.DisplayName)));
    }

    [UiFact]
    public async Task Every_shape_can_be_added_and_gives_a_closed_solid_on_the_plate()
    {
        foreach (var generator in Generators)
        {
            var vm = NewViewModel();

            await Add(vm, generator.Id);

            var node = Assert.IsType<PrimitiveNode>(Assert.Single(vm.Document.Root.Children));
            Assert.Equal(generator.Id, node.GeneratorId);
            Assert.Equal(generator.DisplayName, node.Name);
            var item = vm.ViewportItems.Single(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid);
            Assert.True(item.Mesh.IsWatertight(), generator.Id);
            Assert.Equal(0, item.Mesh.ComputeBounds().Min.Z, 3);
            Assert.False(vm.HasProblems(), generator.Id);
        }
    }

    [UiFact]
    public async Task The_properties_panel_shows_exactly_the_parameters_of_the_shape()
    {
        foreach (var generator in Generators)
        {
            var vm = NewViewModel();

            await Add(vm, generator.Id);

            var panel = vm.Properties!;
            Assert.Equal(generator.Parameters.Select(p => p.Label), panel.Parameters.Select(f => f.Label));
            Assert.Equal(generator.Parameters.Count, panel.Parameters.Count);
        }
    }

    [UiFact]
    public async Task Typing_a_new_value_updates_the_shape()
    {
        foreach (var generator in Generators)
        {
            var vm = NewViewModel();
            await Add(vm, generator.Id);
            var node = vm.Document.Root.Children[0];
            var before = BoundsOf(vm, node.Id).Size;
            var index = generator.Parameters.ToList().FindIndex(p => generator.ScaleBindings.Any(b => b.Parameter == p.Name));
            var parameter = generator.Parameters[index];

            vm.Properties!.Parameters[index].Text = (parameter.Default * 1.5).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            await vm.WaitForEvaluationAsync();

            Assert.False(vm.Properties.Parameters[index].HasError, $"{generator.Id}: {vm.Properties.Parameters[index].Error}");
            var after = BoundsOf(vm, node.Id).Size;
            Assert.NotEqual(before, after);
        }
    }

    [UiFact]
    public async Task A_wrong_value_is_explained_and_the_shape_stays()
    {
        var vm = NewViewModel();
        await Add(vm, "tube");
        var before = BoundsOf(vm, vm.Document.Root.Children[0].Id).Size;

        vm.Properties!.Parameters[1].Text = "0"; // the wall must be above zero

        Assert.True(vm.Properties.Parameters[1].HasError);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before, BoundsOf(vm, vm.Document.Root.Children[0].Id).Size);
    }

    [UiFact]
    public async Task The_scale_handles_resize_every_shape_by_its_dimensions()
    {
        foreach (var generator in Generators)
        {
            foreach (var (handle, axis) in new[] { (GizmoHandleKind.ScaleXMax, 'X'), (GizmoHandleKind.ScaleZMax, 'Z') })
            {
                var vm = NewViewModel();
                await Add(vm, generator.Id);
                vm.SnapToGrid = false;
                vm.ActiveTool = ViewportTool.Scale;
                await vm.WaitForEvaluationAsync();
                var node = vm.Document.Root.Children[0];
                var before = BoundsOf(vm, node.Id).Size;

                Assert.True(vm.BeginDrag(handle), generator.Id);
                vm.UpdateDrag(new GizmoDrag(handle, Vector3.Zero, 0, false, ScaleFactor: 1.5));
                await vm.WaitForEvaluationAsync();
                vm.CommitDrag();
                await vm.WaitForEvaluationAsync();

                var after = BoundsOf(vm, node.Id).Size;
                var ratio = axis == 'X' ? after.X / before.X : after.Z / before.Z;
                Assert.True(Math.Abs(ratio - 1.5) < 0.02, $"{generator.Id} {axis}: {before} -> {after}");
            }
        }
    }

    [UiFact]
    public async Task All_shapes_survive_saving_and_opening()
    {
        var vm = NewViewModel();
        foreach (var generator in Generators) await Add(vm, generator.Id);
        var triangles = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (long)i.Mesh.TriangleCount);
        Directory.CreateDirectory(_folder);
        _dialogs.SavePath = Path.Combine(_folder, "shapes.fwp");
        Assert.True(vm.Save());

        var other = NewViewModel();
        Assert.True(other.OpenPath(_dialogs.SavePath));
        await other.WaitForEvaluationAsync();

        Assert.Equal(12, other.Document.Root.Children.Count);
        Assert.Equal(
            Generators.Select(g => g.Id).Order().ToArray(),
            other.Document.Root.Children.Cast<PrimitiveNode>().Select(n => n.GeneratorId).Order().ToArray());
        Assert.Equal(triangles, other.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (long)i.Mesh.TriangleCount));
    }

    [UiFact]
    public async Task Twelve_shapes_added_one_after_the_other_all_fit_on_the_plate_without_overlapping()
    {
        var vm = NewViewModel();
        foreach (var generator in Generators) await Add(vm, generator.Id);

        Assert.Equal(0, vm.OutsideCount); // none sticks out of the 256 mm plate
        var bounds = vm.Document.Root.Children.Select(n => BoundsOf(vm, n.Id)).ToList();
        for (var i = 0; i < bounds.Count; i++)
        for (var j = i + 1; j < bounds.Count; j++)
        {
            var apart = bounds[i].Min.X >= bounds[j].Max.X || bounds[i].Max.X <= bounds[j].Min.X
                        || bounds[i].Min.Y >= bounds[j].Max.Y || bounds[i].Max.Y <= bounds[j].Min.Y;
            Assert.True(apart, $"shape {i} overlaps shape {j}");
        }

        Assert.True(bounds.Any(b => Math.Abs(b.Center.Y) > 10), "the shapes should continue in a second row");
    }

    [UiFact]
    public async Task A_shape_bigger_than_the_plate_still_gets_a_place()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "400";
        await vm.WaitForEvaluationAsync();

        await Add(vm, "sphere"); // does not fit anywhere inside the plate next to the 400 mm box: it takes the first free spot

        Assert.Equal(2, vm.Document.Root.Children.Count);
        var a = BoundsOf(vm, vm.Document.Root.Children[0].Id);
        var b = BoundsOf(vm, vm.Document.Root.Children[1].Id);
        Assert.True(a.Min.X >= b.Max.X || a.Max.X <= b.Min.X || a.Min.Y >= b.Max.Y || a.Max.Y <= b.Min.Y);
    }

    [UiFact]
    public async Task A_new_shape_can_be_used_as_a_hole()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = vm.Document.Root.Children[0];
        vm.Properties!.Parameters[0].Text = "40";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Parameters[2].Text = "40";
        await vm.WaitForEvaluationAsync();
        var full = BoundsOf(vm, box.Id).Size;
        await Add(vm, "cone");
        var cone = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "10";
        vm.Properties.IsHole = true;
        vm.SelectOnly([box.Id, cone.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var solid = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Solid);

        Assert.True(solid.Mesh.IsWatertight());
        Assert.True(solid.Mesh.ComputeVolume() < 40 * 40 * 40 - 500, $"volume {solid.Mesh.ComputeVolume()}");
        Assert.Equal(full.X, solid.Mesh.ComputeBounds().Size.X, 2);
    }
}

internal static class ViewModelProblems
{
    /// <summary>True when the status bar reports a problem with the scene.</summary>
    public static bool HasProblems(this MainViewModel vm) => vm.StatusText.Contains("problem", StringComparison.OrdinalIgnoreCase);
}
