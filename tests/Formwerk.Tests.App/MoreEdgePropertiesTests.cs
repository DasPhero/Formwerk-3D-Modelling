using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The edge parameters of the frustum, cone, hemisphere, tube, prism and wedge in the properties panel (F-EDG-01).</summary>
public class MoreEdgePropertiesTests
{
    private static readonly string[] Shapes = ["frustum", "cone", "hemisphere", "tube", "prism", "wedge"];

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
        vm.ViewportItems.Where(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

    [UiFact]
    public async Task These_shapes_offer_the_edge_size_and_type_like_the_box_does()
    {
        foreach (var shape in Shapes)
        {
            var vm = NewViewModel();
            await Add(vm, shape);

            Assert.True(vm.Properties!.HasEdgeChoices, shape);
            var parameters = vm.Properties.Parameters;
            Assert.True(parameters[IndexOf(shape, "edgeSize")].IsVisible, shape);
            Assert.Equal("Edge size", parameters[IndexOf(shape, "edgeSize")].Label);
            Assert.False(parameters[IndexOf(shape, "edgeType")].IsVisible, shape); // the type has its own combo box
            Assert.Equal(1, vm.Properties.EdgeType);
            Assert.Equal(["Chamfer", "Fillet"], vm.Properties.EdgeTypes.Select(c => c.Label).ToArray());
        }
    }

    [UiFact]
    public async Task The_choice_of_the_edges_is_only_there_where_there_is_more_than_one_rim()
    {
        var expected = new Dictionary<string, bool>
        {
            ["frustum"] = true, ["tube"] = true, ["prism"] = true, ["wedge"] = true, ["cone"] = false, ["hemisphere"] = false,
        };
        foreach (var (shape, has) in expected)
        {
            var vm = NewViewModel();
            await Add(vm, shape);

            Assert.Equal(has, vm.Properties!.HasEdgeWhere);
            if (has) Assert.False(vm.Properties.Parameters[IndexOf(shape, "edgeWhere")].IsVisible, shape);
        }
    }

    [UiFact]
    public async Task Round_shapes_talk_about_rims_and_flat_ones_about_edges()
    {
        string[] rims = ["Both rims", "Top rim", "Bottom rim"];
        string[] edges = ["All edges", "Top edges", "Bottom edges"];
        var expected = new Dictionary<string, string[]>
        {
            ["cylinder"] = rims, ["frustum"] = rims, ["tube"] = rims, ["box"] = edges, ["prism"] = edges, ["wedge"] = edges,
        };
        foreach (var (shape, labels) in expected)
        {
            var vm = NewViewModel();
            await Add(vm, shape);

            Assert.Equal(labels, vm.Properties!.EdgeWheres.Select(c => c.Label).ToArray());
        }
    }

    [UiFact]
    public async Task Typing_a_size_treats_the_edges_in_one_undo_step()
    {
        foreach (var shape in Shapes)
        {
            var vm = NewViewModel();
            var node = await Add(vm, shape);
            var plain = VolumeOf(vm, node);
            var steps = vm.HistoryEntries.Count;

            vm.Properties!.Parameters[IndexOf(shape, "edgeSize")].Text = "1.5";
            await vm.WaitForEvaluationAsync();

            Assert.True(VolumeOf(vm, node) < plain - 1, shape);
            Assert.Equal(steps + 1, vm.HistoryEntries.Count);

            vm.UndoCommand.Execute(null);
            await vm.WaitForEvaluationAsync();
            Assert.Equal(plain, VolumeOf(vm, node), 3);
        }
    }

    [UiFact]
    public async Task The_type_and_the_position_change_these_shapes_too()
    {
        foreach (var shape in new[] { "frustum", "tube", "prism", "wedge" })
        {
            var vm = NewViewModel();
            var node = await Add(vm, shape);
            vm.Properties!.Parameters[IndexOf(shape, "edgeSize")].Text = "1.5";
            vm.Properties!.EdgeType = 0;
            await vm.WaitForEvaluationAsync();
            var all = VolumeOf(vm, node);

            vm.Properties!.EdgeWhere = 1;
            await vm.WaitForEvaluationAsync();

            Assert.True(VolumeOf(vm, node) > all, shape); // fewer edges are treated, so more is left
            vm.UndoCommand.Execute(null);
            await vm.WaitForEvaluationAsync();
            Assert.Equal(all, VolumeOf(vm, node), 3);
        }
    }

    [UiFact]
    public async Task A_wrong_size_is_refused_in_the_field()
    {
        foreach (var shape in Shapes)
        {
            var vm = NewViewModel();
            await Add(vm, shape);
            var field = vm.Properties!.Parameters[IndexOf(shape, "edgeSize")];

            field.Text = "-2";

            Assert.True(field.HasError, shape);
        }
    }

    [UiFact]
    public async Task Scaling_keeps_the_edge_size()
    {
        foreach (var shape in Shapes)
        {
            var vm = NewViewModel();
            var node = await Add(vm, shape);
            vm.Properties!.Parameters[IndexOf(shape, "edgeSize")].Text = "1";
            await vm.WaitForEvaluationAsync();

            var scaled = ShapeLibrary.Find(shape)!.ScaleBindings[0].Parameter; // what the scale handles change
            vm.Properties!.Parameters[IndexOf(shape, scaled)].Text = "30";
            await vm.WaitForEvaluationAsync();

            Assert.Equal("1", node.Parameters["edgeSize"]);
        }
    }

    [Fact]
    public void A_template_can_ask_for_the_edge_size_of_these_shapes()
    {
        foreach (var shape in Shapes)
        {
            var names = SharedParameterChoice.For([new PrimitiveNode(shape, "Part")]).Select(c => c.Parameter).ToList();

            Assert.Contains("edgeSize", names);
            Assert.DoesNotContain("edgeType", names);
            Assert.DoesNotContain("edgeWhere", names);
        }
    }
}
