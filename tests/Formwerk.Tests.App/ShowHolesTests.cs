using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Showing and hiding the ghosts of holes (F-VIS-04).</summary>
public class ShowHolesTests
{
    private static async Task<MainViewModel> BoxWithHoleInGroup()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        vm.Properties!.IsHole = true;
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        return vm;
    }

    [UiFact]
    public async Task Ghosts_are_shown_by_default()
    {
        var vm = await BoxWithHoleInGroup();

        Assert.True(vm.ShowHoles);
        Assert.Contains(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
    }

    [UiFact]
    public async Task Switching_them_off_removes_the_ghosts_but_not_the_solids_and_back_on_restores_them()
    {
        var vm = await BoxWithHoleInGroup();
        var solids = vm.ViewportItems.Count(i => i.Kind == ViewportItemKind.Solid);

        vm.ShowHoles = false;
        Assert.DoesNotContain(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
        Assert.Equal(solids, vm.ViewportItems.Count(i => i.Kind == ViewportItemKind.Solid));

        vm.ShowHoles = true;
        Assert.Contains(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
    }

    [UiFact]
    public async Task A_new_result_keeps_the_choice()
    {
        var vm = await BoxWithHoleInGroup();
        vm.ShowHoles = false;

        vm.AddShapeCommand.Execute("sphere");
        await vm.WaitForEvaluationAsync();

        Assert.DoesNotContain(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
    }

    [UiFact]
    public async Task The_geometry_of_the_solids_does_not_change_when_ghosts_are_hidden()
    {
        var vm = await BoxWithHoleInGroup();
        var before = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

        vm.ShowHoles = false;

        Assert.Equal(before, vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume()), 6);
    }
}
