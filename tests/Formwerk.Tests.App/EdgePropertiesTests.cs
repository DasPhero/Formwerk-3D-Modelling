using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Chamfer and fillet in the properties panel of boxes and cylinders (F-EDG-01).</summary>
public class EdgePropertiesTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<PrimitiveNode> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = (PrimitiveNode)vm.Document.Root.Children[^1];
        vm.SelectOnly([node.Id]);
        return node;
    }

    private static int IndexOf(string shape, string parameter) => ShapeLibrary.Find(shape)!.Parameters.ToList().FindIndex(p => p.Name == parameter);

    private static double VolumeOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Sum(i => i.Mesh.ComputeVolume());

    // ---- what the panel offers ------------------------------------------------------------------------

    [UiFact]
    public async Task Boxes_and_cylinders_offer_the_edge_choices_other_shapes_do_not()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        Assert.True(vm.Properties!.HasEdgeChoices);

        await Add(vm, "cylinder");
        Assert.True(vm.Properties!.HasEdgeChoices);

        await Add(vm, "sphere");
        Assert.False(vm.Properties!.HasEdgeChoices);
    }

    [UiFact]
    public async Task The_size_is_a_normal_field_and_the_two_choices_have_their_own_controls()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        var parameters = vm.Properties!.Parameters;
        Assert.True(parameters[IndexOf("box", "edgeSize")].IsVisible);
        Assert.Equal("Edge size", parameters[IndexOf("box", "edgeSize")].Label);
        Assert.False(parameters[IndexOf("box", "edgeType")].IsVisible);
        Assert.False(parameters[IndexOf("box", "edgeWhere")].IsVisible);
        Assert.Equal("0", parameters[IndexOf("box", "edgeSize")].Text);
    }

    [UiFact]
    public async Task A_box_talks_about_edges_and_a_cylinder_about_rims()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = vm.Properties!.EdgeWheres.Select(c => c.Label).ToArray();
        await Add(vm, "cylinder");
        var cylinder = vm.Properties!.EdgeWheres.Select(c => c.Label).ToArray();

        Assert.Equal(["All edges", "Top edges", "Bottom edges"], box);
        Assert.Equal(["Both rims", "Top rim", "Bottom rim"], cylinder);
        Assert.Equal(["Chamfer", "Fillet"], vm.Properties!.EdgeTypes.Select(c => c.Label).ToArray());
    }

    [UiFact]
    public async Task The_defaults_are_a_fillet_on_all_edges()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        Assert.Equal(1, vm.Properties!.EdgeType);
        Assert.Equal(0, vm.Properties.EdgeWhere);
    }

    // ---- changing them --------------------------------------------------------------------------------

    [UiFact]
    public async Task Typing_a_size_treats_the_edges_in_one_undo_step()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var plain = VolumeOf(vm, box);
        var steps = vm.HistoryEntries.Count;

        vm.Properties!.Parameters[IndexOf("box", "edgeSize")].Text = "3";
        await vm.WaitForEvaluationAsync();

        Assert.True(VolumeOf(vm, box) < plain - 10);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(plain, VolumeOf(vm, box), 3);
    }

    [UiFact]
    public async Task A_formula_or_a_variable_works_as_the_size()
    {
        var vm = NewViewModel();
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[^1].Name.Text = "corner";
        vm.Variables.Rows[^1].Expression.Text = "2";
        var box = await Add(vm, "box");
        var plain = VolumeOf(vm, box);

        vm.Properties!.Parameters[IndexOf("box", "edgeSize")].Text = "corner * 1.5";
        await vm.WaitForEvaluationAsync();

        Assert.True(VolumeOf(vm, box) < plain - 10);
        Assert.False(vm.Properties.Parameters[IndexOf("box", "edgeSize")].HasError);
    }

    [UiFact]
    public async Task A_negative_size_is_refused_in_the_field()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var field = vm.Properties!.Parameters[IndexOf("box", "edgeSize")];

        field.Text = "-1";

        Assert.True(field.HasError);
    }

    [UiFact]
    public async Task The_type_and_the_position_change_the_shape_and_can_be_undone()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.Properties!.Parameters[IndexOf("box", "edgeSize")].Text = "3";
        await vm.WaitForEvaluationAsync();
        var fillet = VolumeOf(vm, box);

        vm.Properties!.EdgeType = 0; // chamfer cuts away more than a fillet of the same size
        await vm.WaitForEvaluationAsync();
        var chamfer = VolumeOf(vm, box);
        Assert.True(chamfer < fillet);

        vm.Properties!.EdgeWhere = 1; // only the top ones
        await vm.WaitForEvaluationAsync();
        Assert.True(VolumeOf(vm, box) > chamfer);

        vm.UndoCommand.Execute(null);
        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(fillet, VolumeOf(vm, box), 3);
    }

    [UiFact]
    public async Task The_choices_show_what_is_stored_when_the_object_is_selected_again()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder");
        vm.Properties!.Parameters[IndexOf("cylinder", "edgeSize")].Text = "2";
        vm.Properties!.EdgeType = 0;
        vm.Properties!.EdgeWhere = 2;
        await vm.WaitForEvaluationAsync();

        await Add(vm, "box");
        vm.SelectOnly([cylinder.Id]);

        Assert.Equal(0, vm.Properties!.EdgeType);
        Assert.Equal(2, vm.Properties.EdgeWhere);
        Assert.Equal("2", vm.Properties.Parameters[IndexOf("cylinder", "edgeSize")].Text);
    }

    [UiFact]
    public async Task Setting_the_choice_it_already_has_makes_no_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var steps = vm.HistoryEntries.Count;

        vm.Properties!.EdgeType = 1;
        vm.Properties!.EdgeWhere = 0;

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    // ---- scaling and templates ------------------------------------------------------------------------

    [UiFact]
    public async Task Scaling_a_box_keeps_the_edge_size()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.Properties!.Parameters[IndexOf("box", "edgeSize")].Text = "3";
        await vm.WaitForEvaluationAsync();

        vm.Properties!.Parameters[IndexOf("box", "width")].Text = "60"; // what the scale handles do to a box
        await vm.WaitForEvaluationAsync();

        Assert.Equal("3", box.Parameters["edgeSize"]);
        Assert.Equal(60, vm.ViewportItems.Where(i => i.NodeId == box.Id).Max(i => (double)i.Mesh.ComputeBounds().Size.X), 0.01);
    }

    [Fact]
    public void A_template_can_ask_for_the_edge_size_but_not_for_the_choices()
    {
        var names = SharedParameterChoice.For([new PrimitiveNode("box", "Box")]).Select(c => c.Parameter).ToList();

        Assert.Contains("edgeSize", names);
        Assert.DoesNotContain("edgeType", names);
        Assert.DoesNotContain("edgeWhere", names);
    }

    [UiFact]
    public async Task A_treated_box_survives_saving_the_project()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.Properties!.Parameters[IndexOf("box", "edgeSize")].Text = "3";
        vm.Properties!.EdgeType = 0;
        await vm.WaitForEvaluationAsync();

        var stream = new MemoryStream();
        Formwerk.IO.ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = Formwerk.IO.ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        var loaded = (PrimitiveNode)back.Root.Children[0];
        Assert.Equal(box.Parameters["edgeSize"], loaded.Parameters["edgeSize"]);
        Assert.Equal("0", loaded.Parameters["edgeType"]);
    }
}
