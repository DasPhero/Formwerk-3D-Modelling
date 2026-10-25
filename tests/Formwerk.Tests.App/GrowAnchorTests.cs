using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>A shape grows from its center or from one side when its size changes: typed values and project variables (user wish).</summary>
public class GrowAnchorTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<PrimitiveNode> NewBox(MainViewModel vm)
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = (PrimitiveNode)vm.Document.Root.Children[^1];
        vm.SelectOnly([box.Id]);
        return box;
    }

    private static FieldViewModel Field(MainViewModel vm, string name)
    {
        var index = ShapeLibrary.Find("box")!.Parameters.ToList().FindIndex(p => p.Name == name);
        return vm.Properties!.Parameters[index];
    }

    [UiFact]
    public async Task By_default_a_box_grows_from_the_center_of_its_base_as_before()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm);
        var position = box.Transform.Position;

        Field(vm, "width").Text = "80";
        Field(vm, "height").Text = "35";

        Assert.Equal(position, box.Transform.Position);
        Assert.Equal(PrimitiveNode.DefaultGrowAnchor, box.GrowAnchor);
    }

    [UiFact]
    public async Task With_the_left_side_chosen_a_wider_box_keeps_its_left_edge_and_it_is_one_undo_step()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm); // 20 wide, centered at x = 0 (or where it was put)
        var left = box.Transform.Position.X - 10;
        vm.Properties!.GrowX = -1;
        var steps = vm.HistoryEntries.Count;

        Field(vm, "width").Text = "60";

        Assert.Equal(left, box.Transform.Position.X - 30, 3); // the left edge is where it was
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(left + 10, box.Transform.Position.X, 3);
        Assert.Equal("20", box.Parameters["width"]);
    }

    [UiFact]
    public async Task With_the_top_chosen_a_taller_box_keeps_its_top_and_a_relative_value_works_too()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm); // 20 high
        var top = box.Transform.Position.Z + 20;
        vm.Properties!.GrowZ = 1;

        Field(vm, "height").Text = "+10"; // 30 high

        Assert.Equal(top, box.Transform.Position.Z + 30, 3);
    }

    [UiFact]
    public async Task With_the_center_chosen_in_Z_a_taller_box_grows_up_and_down_and_may_leave_the_plate()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm);
        vm.Properties!.GrowZ = 0;

        Field(vm, "height").Text = "26"; // 6 more: 3 below the plate

        Assert.Equal(-3, box.Transform.Position.Z, 3);
        await vm.WaitForEvaluationAsync();
        Assert.True(vm.OutsideCount > 0); // this is what the "outside the build area" in the screenshot was
    }

    [UiFact]
    public async Task A_rotated_and_moved_box_keeps_its_side_in_the_world()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm);
        box.Transform = new Transform(new Vector3(50, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One);
        vm.Properties!.GrowX = -1; // the local left side: after the quarter turn it faces world -Y... its edge at local x = -10 maps to world y = -10

        Field(vm, "width").Text = "40";

        // local shift +10 along the local X, turned to the world Y
        Assert.Equal(50, box.Transform.Position.X, 3);
        Assert.Equal(10, box.Transform.Position.Y, 3);
    }

    [UiFact]
    public async Task A_project_variable_that_changes_moves_the_anchored_shapes_and_leaves_the_others()
    {
        var vm = NewViewModel();
        vm.Variables.AddCommand.Execute(null);
        var row = vm.Variables.Rows[^1];
        row.Name.Text = "Breite";
        row.Expression.Text = "20";

        var anchored = await NewBox(vm);
        Field(vm, "width").Text = "Breite";
        vm.Properties!.GrowX = -1;
        var centered = await NewBox(vm);
        Field(vm, "width").Text = "Breite";
        var anchoredX = anchored.Transform.Position.X;
        var centeredX = centered.Transform.Position.X;
        var steps = vm.HistoryEntries.Count;

        row.Expression.Text = "50"; // 30 wider

        Assert.Equal(anchoredX + 15, anchored.Transform.Position.X, 3); // the left edge stays
        Assert.Equal(centeredX, centered.Transform.Position.X, 3); // grows from its center as before
        Assert.Equal(steps + 1, vm.HistoryEntries.Count); // one undo step

        vm.UndoCommand.Execute(null);
        Assert.Equal(anchoredX, anchored.Transform.Position.X, 3);
        Assert.Equal("20", vm.Document.Variables["Breite"]);
    }

    [UiFact]
    public async Task The_choice_is_saved_with_the_project_and_copied_with_the_shape()
    {
        var vm = NewViewModel();
        var box = await NewBox(vm);
        vm.Properties!.GrowX = 1;
        vm.Properties.GrowZ = 0;

        Assert.Equal(new Vector3(1, 0, 0), box.GrowAnchor);
        Assert.Equal(new Vector3(1, 0, 0), ((PrimitiveNode)box.Clone(newIds: true)).GrowAnchor);

        using var stream = new MemoryStream();
        ProjectFile.Write(stream, vm.Document);
        stream.Position = 0;
        var loaded = (PrimitiveNode)ProjectFile.Read(stream).Root.Children.Single();
        Assert.Equal(new Vector3(1, 0, 0), loaded.GrowAnchor);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        Assert.Equal(PrimitiveNode.DefaultGrowAnchor, box.GrowAnchor);
    }

    [UiFact]
    public async Task Other_objects_have_no_choice_to_make()
    {
        var vm = NewViewModel();
        var a = await NewBox(vm);
        var b = await NewBox(vm);
        vm.SelectOnly([a.Id, b.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.SelectOnly([vm.Document.Root.Children.OfType<GroupNode>().Single().Id]);

        Assert.False(vm.Properties!.HasGrowAnchor);
    }
}
