using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The click of the hole tool snaps to the center of the face and to the grid (F-HOL-01).</summary>
public class HoleSnapTests
{
    private static async Task<(MainViewModel Vm, Node Box)> BoxScene(bool objects, bool grid, double step = 1)
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        vm.AddShapeCommand.Execute("box"); // 20 x 20 x 20, X and Y from -10 to 10
        await vm.WaitForEvaluationAsync();
        vm.SnapToObjects = objects;
        vm.SnapToGrid = grid;
        vm.GridStep = step;
        return (vm, vm.Document.Root.Children[0]);
    }

    private static async Task<Vector3> HoleAt(MainViewModel vm, FacePick pick)
    {
        vm.PlaceHole(pick);
        await vm.WaitForEvaluationAsync();
        var hole = vm.Document.Root.Descendants().Last(n => n is PrimitiveNode { GeneratorId: "hole" });
        return TransformOps.WorldPosition(hole);
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    [UiFact]
    public async Task A_click_near_the_middle_of_a_face_snaps_to_the_middle()
    {
        var (vm, box) = await BoxScene(objects: true, grid: false);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(2.7f, -1.9f, 20), Vector3.UnitZ));

        Assert.True(Near(at, new Vector3(0, 0, 20)), at.ToString());
    }

    [UiFact]
    public async Task A_click_far_from_the_middle_keeps_its_place_without_the_grid()
    {
        var (vm, box) = await BoxScene(objects: true, grid: false);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(7.3f, -6.4f, 20), Vector3.UnitZ));

        Assert.True(Near(at, new Vector3(7.3f, -6.4f, 20)), at.ToString());
    }

    [UiFact]
    public async Task The_grid_rounds_the_two_directions_in_the_face()
    {
        var (vm, box) = await BoxScene(objects: false, grid: true, step: 5);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(7.3f, -6.4f, 20), Vector3.UnitZ));

        Assert.True(Near(at, new Vector3(5, -5, 20)), at.ToString());
    }

    [UiFact]
    public async Task The_grid_also_works_on_a_side_face()
    {
        var (vm, box) = await BoxScene(objects: false, grid: true);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(10, 3.3f, 8.6f), Vector3.UnitX));

        Assert.True(Near(at, new Vector3(10, 3, 9)), at.ToString()); // X is the direction of the hole and stays
    }

    [UiFact]
    public async Task A_grid_point_that_would_lie_beside_the_face_is_not_used()
    {
        var (vm, box) = await BoxScene(objects: false, grid: true, step: 12);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(9.8f, 0.2f, 20), Vector3.UnitZ)); // 12 would be outside of -10..10

        Assert.True(Near(at, new Vector3(9.8f, 0.2f, 20)), at.ToString());
    }

    [UiFact]
    public async Task The_middle_wins_over_the_grid_when_both_are_on()
    {
        var (vm, box) = await BoxScene(objects: true, grid: true, step: 5);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(2.4f, 1.1f, 20), Vector3.UnitZ));

        Assert.True(Near(at, new Vector3(0, 0, 20)), at.ToString());
    }

    [UiFact]
    public async Task Holding_Alt_or_Shift_switches_the_snap_off()
    {
        var (vm, box) = await BoxScene(objects: true, grid: true);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(2.7f, -1.9f, 20), Vector3.UnitZ, NoSnap: true));

        Assert.True(Near(at, new Vector3(2.7f, -1.9f, 20)), at.ToString());
    }

    [UiFact]
    public async Task Both_snaps_off_leave_the_click_alone()
    {
        var (vm, box) = await BoxScene(objects: false, grid: false);

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(2.7f, -1.9f, 20), Vector3.UnitZ));

        Assert.True(Near(at, new Vector3(2.7f, -1.9f, 20)), at.ToString());
    }

    [UiFact]
    public async Task A_slanted_click_that_is_not_on_a_face_is_not_snapped()
    {
        var (vm, box) = await BoxScene(objects: true, grid: true);
        var normal = Vector3.Normalize(new Vector3(0.6f, -0.3f, 0.8f));

        var at = await HoleAt(vm, new FacePick(box.Id, new Vector3(2.7f, -1.9f, 20), normal));

        Assert.True(Near(at, new Vector3(2.7f, -1.9f, 20)), at.ToString());
    }

    [UiFact]
    public async Task The_hint_mentions_how_to_switch_the_snap_off()
    {
        var (vm, _) = await BoxScene(objects: true, grid: true);

        vm.DrillCommand.Execute(null);

        Assert.Contains("Alt", vm.HintText);
    }
}
