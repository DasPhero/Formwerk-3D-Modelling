using System.Numerics;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The hole tool (F-HOL-01, F-HOL-05): a click on a face puts a hole perpendicular to it that cuts the body.</summary>
public class HolePlacementTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<(MainViewModel Vm, Node Box)> BoxScene()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box"); // 20 x 20 x 20, standing on the plate
        await vm.WaitForEvaluationAsync();
        return (vm, vm.Document.Root.Children[0]);
    }

    private static double SolidVolume(MainViewModel vm) =>
        vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (double)i.Mesh.ComputeVolume());

    private static FacePick TopOf(Node box, float dx = 0, float dy = 0) =>
        new(box.Id, TransformOps.WorldPosition(box) + new Vector3(dx, dy, 20), Vector3.UnitZ);

    /// <summary>The properties panel of the node, as the user would use it.</summary>
    private static PropertiesViewModel Props(MainViewModel vm, Node node)
    {
        vm.SelectOnly([node.Id]);
        return vm.Properties!;
    }

    private static async Task<Node> Drill(MainViewModel vm, FacePick pick)
    {
        vm.PlaceHole(pick);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Descendants().Last(n => n is PrimitiveNode { GeneratorId: "hole" });
    }

    [UiFact]
    public async Task A_click_on_the_top_face_puts_a_hole_into_a_new_group_with_the_body()
    {
        var (vm, box) = await BoxScene();

        var hole = await Drill(vm, TopOf(box, 3, 2) with { NoSnap = true }); // the click is near the middle: without NoSnap it would snap there

        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal([box, hole], group.Children);
        Assert.Equal(NodeRole.Hole, hole.Role);
        Assert.Equal(new Vector3(3, 2, 20), TransformOps.WorldPosition(hole));
        Assert.Equal([hole.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_hole_cuts_the_body_all_the_way_through_by_default()
    {
        var (vm, box) = await BoxScene();
        var before = SolidVolume(vm);

        await Drill(vm, TopOf(box));

        var removed = before - SolidVolume(vm);
        Assert.InRange(removed, Math.PI * 1.7 * 1.7 * 20 * 0.99, Math.PI * 1.7 * 1.7 * 20 * 1.01); // M3 clearance hole, 20 mm
    }

    [UiFact]
    public async Task A_hole_on_a_slanted_face_stands_perpendicular_to_that_face()
    {
        var (vm, box) = await BoxScene();
        var normal = Vector3.Normalize(new Vector3(0.6f, -0.3f, 0.8f));

        var hole = await Drill(vm, new FacePick(box.Id, new Vector3(1, 2, 20), normal));

        var axis = Vector3.TransformNormal(Vector3.UnitZ, TransformOps.LocalToWorld(hole));
        Assert.True(Vector3.Distance(Vector3.Normalize(axis), normal) < 1e-4f, axis.ToString());
        Assert.Equal(new Vector3(1, 2, 20), TransformOps.WorldPosition(hole));
    }

    [UiFact]
    public async Task A_face_that_looks_straight_down_gets_a_hole_pointing_down()
    {
        var (vm, box) = await BoxScene();

        var hole = await Drill(vm, new FacePick(box.Id, new Vector3(0, 0, 0), -Vector3.UnitZ));

        var axis = Vector3.TransformNormal(Vector3.UnitZ, TransformOps.LocalToWorld(hole));
        Assert.True(Vector3.Distance(Vector3.Normalize(axis), -Vector3.UnitZ) < 1e-4f);
        Assert.True(SolidVolume(vm) < 8000);
    }

    [UiFact]
    public async Task The_whole_placement_is_one_undo_step()
    {
        var (vm, box) = await BoxScene();
        var steps = vm.HistoryEntries.Count;

        await Drill(vm, TopOf(box));
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(box, Assert.Single(vm.Document.Root.Children)); // the box is back at the top, no group
        Assert.Equal(8000, SolidVolume(vm), 0);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.True(SolidVolume(vm) < 8000);
    }

    [UiFact]
    public async Task A_second_hole_goes_into_the_same_group()
    {
        var (vm, box) = await BoxScene();
        await Drill(vm, TopOf(box, -5));

        var second = await Drill(vm, TopOf(box, 5));

        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(3, group.Children.Count);
        Assert.Same(group, second.Parent);
        Assert.Equal(2, group.Children.Count(c => c.Role == NodeRole.Hole));
        Assert.InRange(8000 - SolidVolume(vm), 2 * Math.PI * 1.7 * 1.7 * 20 * 0.98, 2 * Math.PI * 1.7 * 1.7 * 20 * 1.02);
    }

    [UiFact]
    public async Task A_hole_on_a_part_of_a_group_joins_that_group_and_keeps_its_place_when_the_group_moved()
    {
        var (vm, box) = await BoxScene();
        vm.AddShapeCommand.Execute("sphere");
        await vm.WaitForEvaluationAsync();
        var sphere = vm.Document.Root.Children[1];
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        Props(vm, group).Position[0].Text = "100";

        var topOfBox = new FacePick(box.Id, TransformOps.WorldPosition(box) + new Vector3(0, 0, 20), Vector3.UnitZ);
        var hole = await Drill(vm, topOfBox);

        Assert.Same(group, hole.Parent);
        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(topOfBox.Point, TransformOps.WorldPosition(hole));
    }

    [UiFact]
    public async Task A_locked_body_gets_no_hole()
    {
        var (vm, box) = await BoxScene();
        box.Locked = true;
        var steps = vm.HistoryEntries.Count;

        vm.PlaceHole(TopOf(box));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Single(vm.Document.Root.Children);
        Assert.Contains(box.Name, vm.StatusText);
    }

    [UiFact]
    public async Task A_pick_of_something_that_is_not_in_the_scene_is_ignored()
    {
        var (vm, _) = await BoxScene();
        var steps = vm.HistoryEntries.Count;

        vm.PlaceHole(new FacePick(Guid.NewGuid(), Vector3.Zero, Vector3.UnitZ));

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task A_blind_hole_has_the_depth_of_the_tool()
    {
        var (vm, box) = await BoxScene();
        vm.HoleTool.Through = false;
        vm.HoleTool.Depth.Text = "4";
        var before = SolidVolume(vm);

        var hole = await Drill(vm, TopOf(box));

        Assert.Equal("4", ((PrimitiveNode)hole).Parameters["depth"]);
        Assert.InRange(before - SolidVolume(vm), Math.PI * 1.7 * 1.7 * 4 * 0.99, Math.PI * 1.7 * 1.7 * 4 * 1.01);
    }

    [UiFact]
    public async Task A_through_hole_in_a_slanted_direction_is_long_enough_to_leave_the_body()
    {
        var (vm, box) = await BoxScene();
        var normal = Vector3.Normalize(new Vector3(1, 0, 1)); // 45 degrees

        var hole = (PrimitiveNode)await Drill(vm, new FacePick(box.Id, new Vector3(0, 0, 20), normal));

        var depth = double.Parse(hole.Parameters["depth"], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(depth >= 20 * Math.Sqrt(2) / 2 + 20 * Math.Sqrt(2) / 2, depth.ToString()); // box extent along the axis
    }

    [UiFact]
    public async Task The_hole_can_be_moved_afterwards_and_the_body_follows_live()
    {
        var (vm, box) = await BoxScene();
        var hole = await Drill(vm, TopOf(box));
        var cut = SolidVolume(vm);

        Props(vm, hole).Position[0].Text = "100"; // far outside the body
        await vm.WaitForEvaluationAsync();

        Assert.True(SolidVolume(vm) > cut);
        Assert.Equal(8000, SolidVolume(vm), 0);
    }

    [UiFact]
    public async Task The_hole_can_be_deleted_and_the_body_is_whole_again()
    {
        var (vm, box) = await BoxScene();
        var hole = await Drill(vm, TopOf(box));

        vm.SelectOnly([hole.Id]);
        vm.DeleteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(8000, SolidVolume(vm), 0);
    }

    [UiFact]
    public async Task A_parameter_of_the_hole_can_be_changed_afterwards()
    {
        var (vm, box) = await BoxScene();
        var hole = (PrimitiveNode)await Drill(vm, TopOf(box));
        var cut = 8000 - SolidVolume(vm);

        var index = ShapeLibrary.Hole.Parameters.ToList().FindIndex(p => p.Name == "diameter");
        Props(vm, hole).Parameters[index].Text = "6.8";
        await vm.WaitForEvaluationAsync();

        Assert.InRange((8000 - SolidVolume(vm)) / cut, 3.9, 4.1); // twice the diameter, four times the volume
    }

    [UiFact]
    public async Task The_tool_stays_on_after_a_hole_so_that_several_can_be_placed()
    {
        var (vm, box) = await BoxScene();
        vm.DrillCommand.Execute(null);
        Assert.True(vm.IsPlacingHole);
        Assert.True(vm.IsFacePickActive);

        await Drill(vm, TopOf(box));

        Assert.True(vm.IsPlacingHole);
    }

    [UiFact]
    public async Task Esc_and_the_button_end_the_tool()
    {
        var (vm, _) = await BoxScene();

        vm.DrillCommand.Execute(null);
        vm.FacePickedCommand.Execute(null); // the viewport reports Esc as a pick of null
        Assert.False(vm.IsPlacingHole);
        Assert.False(vm.IsFacePickActive);

        vm.DrillCommand.Execute(null);
        vm.DrillCommand.Execute(null);
        Assert.False(vm.IsPlacingHole);
    }

    [UiFact]
    public async Task The_tool_and_face_on_floor_do_not_run_at_the_same_time()
    {
        var (vm, _) = await BoxScene();

        vm.DrillCommand.Execute(null);
        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsPlacingHole);
        Assert.True(vm.IsPickingFace);

        vm.DrillCommand.Execute(null);
        Assert.True(vm.IsPlacingHole);
        Assert.False(vm.IsPickingFace);
    }

    [UiFact]
    public async Task The_hint_explains_the_tool()
    {
        var (vm, _) = await BoxScene();

        vm.DrillCommand.Execute(null);

        Assert.Equal(Texts.Get("Hint.HolePlace"), vm.HintText);
    }

    [UiFact]
    public async Task The_hole_survives_save_and_load()
    {
        var (vm, box) = await BoxScene();
        var hole = (PrimitiveNode)await Drill(vm, TopOf(box, 2, 3));

        var path = Path.Combine(Path.GetTempPath(), $"hole-{Guid.NewGuid():N}.fwp");
        Formwerk.IO.ProjectFile.SaveAtomic(path, vm.Document);
        var loaded = Formwerk.IO.ProjectFile.Load(path);
        File.Delete(path);

        var copy = Assert.IsType<PrimitiveNode>(loaded.Root.Descendants().Single(n => n is PrimitiveNode { GeneratorId: "hole" }));
        Assert.Equal(NodeRole.Hole, copy.Role);
        Assert.Equal(hole.Parameters["diameter"], copy.Parameters["diameter"]);
        Assert.Equal(hole.Transform, copy.Transform);
    }
}
