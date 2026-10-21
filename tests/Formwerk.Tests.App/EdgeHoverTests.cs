using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The edge under the mouse in the chamfer / fillet tool (F-EDG-02, hover).</summary>
public class EdgeHoverTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<PrimitiveNode> Add(MainViewModel vm, string shape, params (string Name, string Value)[] parameters)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = (PrimitiveNode)vm.Document.Root.Children[^1];
        foreach (var (name, value) in parameters) node.Parameters[name] = value;
        vm.SelectOnly([node.Id]);
        if (parameters.Length > 0) vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        return node;
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    // a 20 x 20 x 20 box: the +Y edge of its top is at y = 10, z = 20
    private static FacePick TopNearPlusY(Guid id) => new(id, new Vector3(0, 8, 20), Vector3.UnitZ);

    private static FacePick SlantedRim(Guid id) => new(id, new Vector3(4.6f, 0, 10), Vector3.UnitZ);

    [UiFact]
    public async Task Hovering_a_face_in_the_tool_highlights_the_edge_that_a_click_would_take()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);

        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        var highlight = Assert.IsType<ViewportEdgeHighlight>(vm.EdgeHighlight);
        Assert.True(highlight.Valid);
        Assert.False(highlight.Closed);
        Assert.Contains(highlight.Points, p => Near(p, new Vector3(-10, 10, 20)));
        Assert.Contains(highlight.Points, p => Near(p, new Vector3(10, 10, 20)));
    }

    [UiFact]
    public async Task The_edge_follows_the_mouse()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);

        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));
        var first = vm.EdgeHighlight!;
        vm.FaceHoveredCommand.Execute(new FacePick(box.Id, new Vector3(8, 0, 20), Vector3.UnitZ));

        Assert.NotSame(first, vm.EdgeHighlight);
        Assert.Contains(vm.EdgeHighlight!.Points, p => Near(p, new Vector3(10, -10, 20)));
    }

    [UiFact]
    public async Task The_rim_of_a_cylinder_is_highlighted_as_a_circle()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("height", "10"));
        vm.EdgeCutCommand.Execute(null);

        vm.FaceHoveredCommand.Execute(new FacePick(cylinder.Id, new Vector3(9, 1, 10), Vector3.UnitZ));

        var highlight = vm.EdgeHighlight!;
        Assert.True(highlight.Valid);
        Assert.True(highlight.Closed);
        Assert.Equal(64, highlight.Points.Count);
    }

    [UiFact]
    public async Task An_edge_that_cannot_be_cut_is_shown_grey_and_the_hint_says_why()
    {
        var vm = NewViewModel();
        var frustum = await Add(vm, "frustum", ("bottomDiameter", "20"), ("topDiameter", "10"), ("height", "10"));
        vm.EdgeCutCommand.Execute(null);

        vm.FaceHoveredCommand.Execute(SlantedRim(frustum.Id));

        Assert.False(vm.EdgeHighlight!.Valid);
        Assert.Contains("right angle", vm.HintText);
    }

    [UiFact]
    public async Task Leaving_the_object_clears_the_highlight_and_brings_the_normal_hint_back()
    {
        var vm = NewViewModel();
        var frustum = await Add(vm, "frustum", ("bottomDiameter", "20"), ("topDiameter", "10"), ("height", "10"));
        vm.EdgeCutCommand.Execute(null);
        var normal = vm.HintText;
        vm.FaceHoveredCommand.Execute(SlantedRim(frustum.Id));
        Assert.NotEqual(normal, vm.HintText);

        vm.FaceHoveredCommand.Execute(null);

        Assert.Null(vm.EdgeHighlight);
        Assert.Equal(normal, vm.HintText);
    }

    [UiFact]
    public async Task A_good_edge_after_a_bad_one_gives_the_normal_hint_again()
    {
        var vm = NewViewModel();
        var frustum = await Add(vm, "frustum", ("bottomDiameter", "20"), ("topDiameter", "10"), ("height", "10"));
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        var normal = vm.HintText;
        vm.FaceHoveredCommand.Execute(SlantedRim(frustum.Id));
        Assert.NotEqual(normal, vm.HintText);

        // the second shape was put beside the first one: look where its top is
        var bounds = vm.ViewportItems.First(i => i.NodeId == box.Id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();
        vm.FaceHoveredCommand.Execute(new FacePick(box.Id, new Vector3(bounds.Center.X, bounds.Max.Y - 2, bounds.Max.Z), Vector3.UnitZ));

        Assert.True(vm.EdgeHighlight!.Valid);
        Assert.Equal(normal, vm.HintText);
    }

    [UiFact]
    public async Task A_point_that_is_on_no_face_has_no_highlight()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        vm.FaceHoveredCommand.Execute(new FacePick(box.Id, new Vector3(50, 50, 50), Vector3.UnitZ));

        Assert.Null(vm.EdgeHighlight);
    }

    [UiFact]
    public async Task Hovering_does_nothing_in_other_modes()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.DrillCommand.Execute(null);

        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        Assert.Null(vm.EdgeHighlight);
    }

    [UiFact]
    public async Task Ending_the_tool_clears_the_highlight()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));
        Assert.NotNull(vm.EdgeHighlight);

        vm.EdgeCutCommand.Execute(null);

        Assert.Null(vm.EdgeHighlight);
    }

    [UiFact]
    public async Task Another_mode_taking_over_clears_the_highlight()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        vm.DrillCommand.Execute(null);

        Assert.Null(vm.EdgeHighlight);
    }

    [UiFact]
    public async Task Esc_clears_the_highlight_too()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        vm.FacePickedCommand.Execute(null);

        Assert.Null(vm.EdgeHighlight);
    }

    [UiFact]
    public async Task A_click_clears_the_highlight_because_the_edge_is_cut_now()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.EdgeTool.Size.Text = "2";
        vm.FaceHoveredCommand.Execute(TopNearPlusY(box.Id));

        vm.FacePickedCommand.Execute(TopNearPlusY(box.Id));
        await vm.WaitForEvaluationAsync();

        Assert.Null(vm.EdgeHighlight);
        Assert.Contains(vm.Document.Root.Descendants().OfType<PrimitiveNode>(), n => n.GeneratorId == "edgecut");
    }

    [UiFact]
    public async Task Parts_that_are_too_big_to_search_at_every_mouse_move_get_no_highlight()
    {
        var vm = NewViewModel();
        var sphere = await Add(vm, "sphere", ("segments", "512"), ("rings", "256"));
        vm.EdgeCutCommand.Execute(null);
        var item = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid);
        Assert.True(item.Mesh.TriangleCount > MainViewModel.EdgeHoverTriangleLimit, item.Mesh.TriangleCount.ToString());
        var vertices = item.Mesh.Vertices.ToArray();
        var indices = item.Mesh.Indices.ToArray();
        var centre = (vertices[indices[0]] + vertices[indices[1]] + vertices[indices[2]]) / 3;

        vm.FaceHoveredCommand.Execute(new FacePick(sphere.Id, centre, Vector3.Normalize(centre)));

        Assert.Null(vm.EdgeHighlight);
    }
}
