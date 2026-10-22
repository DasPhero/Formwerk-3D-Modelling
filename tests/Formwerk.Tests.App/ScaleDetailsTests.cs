using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Scaling keeps (or scales) holes and chamfers, depending on the option (F-SCL-05).</summary>
public sealed class ScaleDetailsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-scaledetails-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static MainViewModel NewViewModel(UserSettingsStore? store = null) => new(new ManifoldKernel(), new FakeDialogs(), userSettings: store);

    private static async Task Set(MainViewModel vm, PrimitiveNode node, string name, string value)
    {
        node.Parameters[name] = value;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
    }

    private static async Task<(MainViewModel Vm, PrimitiveNode Shape)> ScaleToolWith(string shape)
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        vm.ActiveTool = ViewportTool.Scale;
        return (vm, (PrimitiveNode)vm.Document.Root.Children[0]);
    }

    private static async Task Drag(MainViewModel vm, GizmoHandleKind handle, double factor, bool commit = true)
    {
        Assert.True(vm.BeginDrag(handle));
        vm.UpdateDrag(new GizmoDrag(handle, Vector3.Zero, 0, true, factor));
        await vm.WaitForEvaluationAsync();
        if (commit) vm.CommitDrag();
        await vm.WaitForEvaluationAsync();
    }

    private static double Number(PrimitiveNode node, string name) =>
        double.Parse(node.Parameters[name], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A group of a cylinder (diameter 20, height 20) and an M3 hole (3.4 mm) at (5, 0, 10); selected, Scale tool.</summary>
    private static async Task<(MainViewModel Vm, GroupNode Group, PrimitiveNode Cylinder, PrimitiveNode Hole)> CylinderWithHole()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        var cylinder = (PrimitiveNode)vm.Document.Root.Children[0];
        vm.DrillCommand.NotifyCanExecuteChanged();
        var hole = new PrimitiveNode("hole", "Hole") { Role = NodeRole.Hole, Transform = Transform.FromPosition(new Vector3(5, 0, 10)) };
        hole.Parameters["diameter"] = "3.4";
        hole.Parameters["through"] = "1";
        vm.SelectOnly([cylinder.Id]);
        vm.GroupCommand.Execute(null);
        var group = (GroupNode)vm.Document.Root.Children[0];
        new Formwerk.Core.Commands.AddNodeCommand(vm.Document, group, hole).Do();
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([group.Id]);
        vm.ActiveTool = ViewportTool.Scale;
        return (vm, group, cylinder, hole);
    }

    private static float SmallestExtentOfGhost(MainViewModel vm, Node node)
    {
        var size = vm.ViewportItems.Where(i => i.NodeId == node.Id).Select(i => i.Mesh.ComputeBounds().Size).First();
        return Math.Min(size.X, Math.Min(size.Y, size.Z));
    }

    // ---- the option ------------------------------------------------------------------------------

    [UiFact]
    public void The_option_is_on_by_default_and_remembered()
    {
        Directory.CreateDirectory(_folder);
        var store = new UserSettingsStore(Path.Combine(_folder, "settings.json"));
        var vm = NewViewModel(store);
        Assert.True(vm.KeepDetailsWhenScaling);

        vm.KeepDetailsWhenScaling = false;

        Assert.False(store.Load().KeepDetailsWhenScaling);
        Assert.False(NewViewModel(store).KeepDetailsWhenScaling);
    }

    // ---- shapes ----------------------------------------------------------------------------------

    [UiFact]
    public async Task A_chamfer_keeps_its_size_when_the_option_is_on_and_scales_when_it_is_off()
    {
        var (vm, box) = await ScaleToolWith("box");
        await Set(vm, box, "edgeSize", "2");

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        Assert.Equal(40, Number(box, "width"), 3);
        Assert.Equal("2", box.Parameters["edgeSize"]);

        vm.KeepDetailsWhenScaling = false;
        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        Assert.Equal(80, Number(box, "width"), 3);
        Assert.Equal(4, Number(box, "edgeSize"), 3);
    }

    [UiFact]
    public async Task The_wall_of_a_tube_follows_the_option()
    {
        var (vm, tube) = await ScaleToolWith("tube");
        var wall = Number(tube, "wall");

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        Assert.Equal(wall, tube.Parameters.TryGetValue("wall", out var kept) ? double.Parse(kept, System.Globalization.CultureInfo.InvariantCulture) : wall, 3);

        vm.KeepDetailsWhenScaling = false;
        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        Assert.Equal(wall * 2, Number(tube, "wall"), 3);
    }

    [UiFact]
    public async Task Scaling_a_shape_with_details_is_one_undo_step_and_undo_restores_everything()
    {
        var (vm, box) = await ScaleToolWith("box");
        await Set(vm, box, "edgeSize", "2");
        vm.KeepDetailsWhenScaling = false;
        var steps = vm.HistoryEntries.Count;

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal("20", box.Parameters["width"]);
        Assert.Equal("2", box.Parameters["edgeSize"]);
    }

    // ---- containers with holes -------------------------------------------------------------------

    [UiFact]
    public async Task A_hole_keeps_its_diameter_when_a_cylinder_with_a_hole_is_scaled_to_200_percent()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();
        var steps = vm.HistoryEntries.Count;
        Assert.Equal(3.4f, SmallestExtentOfGhost(vm, hole), 2);

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);

        Assert.Equal(40, Number(cylinder, "diameter"), 3);
        Assert.Equal(40, Number(cylinder, "height"), 3);
        Assert.Equal("3.4", hole.Parameters["diameter"]); // the hole stays an M3 hole
        Assert.Equal(Vector3.One, group.Transform.Scale); // the group itself was not stretched
        Assert.Equal(new Vector3(10, 0, 20), hole.Transform.Position); // but it sits where it belongs on the bigger body
        Assert.Equal(3.4f, SmallestExtentOfGhost(vm, hole), 2);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task One_undo_brings_back_the_shape_the_hole_and_the_position()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();
        var groupStart = group.Transform;

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);
        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("20", cylinder.Parameters["diameter"]);
        Assert.Equal("20", cylinder.Parameters["height"]);
        Assert.Equal(new Vector3(5, 0, 10), hole.Transform.Position);
        Assert.Equal(groupStart, group.Transform);
        Assert.Equal(3.4f, SmallestExtentOfGhost(vm, hole), 2);
    }

    [UiFact]
    public async Task With_the_option_off_the_whole_group_is_scaled_and_the_hole_with_it()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();
        vm.KeepDetailsWhenScaling = false;

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);

        Assert.Equal(new Vector3(2, 2, 2), group.Transform.Scale);
        Assert.Equal("20", cylinder.Parameters["diameter"]);
        Assert.Equal(6.8f, SmallestExtentOfGhost(vm, hole), 2);
    }

    [UiFact]
    public async Task A_scale_along_one_axis_cannot_keep_the_hole_round_and_scales_the_group()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2);

        Assert.Equal(2, group.Transform.Scale.X, 3);
        Assert.Equal(1, group.Transform.Scale.Y, 3);
        Assert.Equal("20", cylinder.Parameters["diameter"]);
        Assert.Equal("3.4", hole.Parameters["diameter"]);
    }

    [UiFact]
    public async Task Proportional_mode_makes_a_single_handle_uniform_so_the_hole_is_kept()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();
        vm.ScaleLinked = true;

        await Drag(vm, GizmoHandleKind.ScaleXMax, 2);

        Assert.Equal(Vector3.One, group.Transform.Scale);
        Assert.Equal(40, Number(cylinder, "diameter"), 3);
        Assert.Equal("3.4", hole.Parameters["diameter"]);
    }

    [UiFact]
    public async Task Cancelling_the_drag_puts_everything_back()
    {
        var (vm, group, cylinder, hole) = await CylinderWithHole();
        var groupStart = group.Transform;

        Assert.True(vm.BeginDrag(GizmoHandleKind.ScaleCorner7));
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.ScaleCorner7, Vector3.Zero, 0, true, 2));
        await vm.WaitForEvaluationAsync();
        vm.CancelDrag();
        await vm.WaitForEvaluationAsync();

        Assert.Equal("20", cylinder.Parameters["diameter"]);
        Assert.Equal(new Vector3(5, 0, 10), hole.Transform.Position);
        Assert.Equal(groupStart, group.Transform);
    }

    [UiFact]
    public async Task A_group_with_a_mesh_scales_the_mesh_object_with_it()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        await vm.FlattenCommand.ExecuteAsync(null); // a mesh object
        await vm.WaitForEvaluationAsync();
        var mesh = vm.Document.Root.Children[0];
        vm.SelectOnly([mesh.Id]);
        vm.GroupCommand.Execute(null);
        var group = (GroupNode)vm.Document.Root.Children[0];
        vm.SelectOnly([group.Id]);
        vm.ActiveTool = ViewportTool.Scale;

        await Drag(vm, GizmoHandleKind.ScaleCorner7, 2);

        Assert.Equal(Vector3.One, group.Transform.Scale);
        Assert.Equal(new Vector3(2, 2, 2), group.Children[0].Transform.Scale);
        var size = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds().Size;
        Assert.Equal(40, size.X, 2);
    }
}
