using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>What the view model does with a selection rectangle from the viewport (F-SEL-01).</summary>
public class MarqueeTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task<List<Node>> ThreeShapes(MainViewModel vm)
    {
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        return [.. vm.Document.Root.Children];
    }

    private static void Drag(MainViewModel vm, bool additive, params Guid[] ids) =>
        vm.MarqueeCommand.Execute(new MarqueeRequest(ids, additive));

    [UiFact]
    public async Task A_rectangle_replaces_the_selection()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[2].Id]);

        Drag(vm, false, nodes[0].Id, nodes[1].Id);

        Assert.Equal(new HashSet<Guid> { nodes[0].Id, nodes[1].Id }, vm.SelectedIds.ToHashSet());
        Assert.Null(vm.Properties); // several objects: no single property panel
    }

    [UiFact]
    public async Task A_rectangle_around_one_object_selects_it_and_shows_its_properties()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);

        Drag(vm, false, nodes[1].Id);

        Assert.Equal([nodes[1].Id], vm.SelectedIds);
        Assert.Equal(nodes[1].Id, vm.Properties!.NodeId);
    }

    [UiFact]
    public async Task With_Ctrl_or_Shift_the_rectangle_adds_to_the_selection()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id]);

        Drag(vm, true, nodes[1].Id, nodes[2].Id);

        Assert.Equal(3, vm.SelectedIds.Count);
    }

    [UiFact]
    public async Task Adding_an_object_that_is_already_selected_keeps_it_selected()
    {
        // unlike a click, which toggles: a rectangle only ever adds
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id, nodes[1].Id]);

        Drag(vm, true, nodes[1].Id);

        Assert.Equal(new HashSet<Guid> { nodes[0].Id, nodes[1].Id }, vm.SelectedIds.ToHashSet());
    }

    [UiFact]
    public async Task An_empty_rectangle_clears_the_selection_but_not_with_Ctrl_or_Shift()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id, nodes[1].Id]);

        Drag(vm, true);
        Assert.Equal(2, vm.SelectedIds.Count);

        Drag(vm, false);
        Assert.Empty(vm.SelectedIds);
        Assert.Null(vm.Properties);
    }

    [UiFact]
    public async Task The_object_list_follows_the_rectangle()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);

        Drag(vm, false, nodes[0].Id, nodes[2].Id);

        Assert.Equal(new HashSet<Guid> { nodes[0].Id, nodes[2].Id }, vm.Nodes.Where(n => n.IsSelected).Select(n => n.Id).ToHashSet());
        Assert.False(vm.Nodes.Single(n => n.Id == nodes[1].Id).IsSelected);
    }

    [UiFact]
    public async Task Ids_that_are_not_in_the_document_are_ignored()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);

        Drag(vm, false, Guid.NewGuid(), nodes[0].Id);

        Assert.Equal([nodes[0].Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task A_null_request_does_nothing()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id]);

        vm.MarqueeCommand.Execute(null);

        Assert.Equal([nodes[0].Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_operands_of_a_boolean_select_the_whole_boolean_once()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id, nodes[1].Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Union);
        await vm.WaitForEvaluationAsync();
        var boolean = Assert.IsType<BooleanNode>(vm.Document.Root.Children.First(n => n is BooleanNode));
        vm.SelectOnly([]);

        Drag(vm, false, nodes[0].Id, nodes[1].Id);

        Assert.Equal([boolean.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_parts_of_a_plain_group_are_selected_one_by_one()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id, nodes[1].Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([]);

        Drag(vm, false, nodes[0].Id);

        Assert.Equal([nodes[0].Id], vm.SelectedIds); // the box, not its group
    }

    [UiFact]
    public async Task A_hole_is_selected_itself_and_not_its_group()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = vm.Document.Root.Children.Single();
        await Add(vm, "cylinder");
        var cylinder = vm.Document.Root.Children[1];
        vm.Properties!.IsHole = true;
        vm.SelectOnly([box.Id, cylinder.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Contains(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost && i.NodeId == cylinder.Id);
        vm.SelectOnly([]);

        Drag(vm, false, cylinder.Id);

        Assert.Equal([cylinder.Id], vm.SelectedIds);
    }

    // ---- hint --------------------------------------------------------------------------------------

    [UiFact]
    public async Task The_select_tool_explains_the_rectangle_in_the_hint_line()
    {
        var vm = NewViewModel();
        var nodes = await ThreeShapes(vm);
        vm.SelectOnly([nodes[0].Id]);

        vm.ActiveTool = ViewportTool.Select;
        Assert.Equal(Texts.Get("Hint.SelectTool"), vm.HintText);
        Assert.Contains("rectangle", vm.HintText, StringComparison.OrdinalIgnoreCase);

        vm.SelectOnly([]);
        Assert.Equal(Texts.Get("Hint.SelectTool"), vm.HintText);

        vm.ActiveTool = ViewportTool.Move;
        Assert.Equal(Texts.Get("Hint.Default"), vm.HintText);
    }

    [UiFact]
    public async Task Picking_a_face_takes_over_the_hint_from_the_select_tool()
    {
        var vm = NewViewModel();
        await ThreeShapes(vm);
        vm.ActiveTool = ViewportTool.Select;

        vm.FaceOnFloorCommand.Execute(null);

        Assert.Equal(Texts.Get("Hint.FacePick"), vm.HintText);
    }

    [UiFact]
    public void The_tool_tip_of_the_select_tool_mentions_the_rectangle()
    {
        Assert.Contains("rectangle", Texts.Get("Tool.Select.Tip"), StringComparison.OrdinalIgnoreCase);
    }
}
