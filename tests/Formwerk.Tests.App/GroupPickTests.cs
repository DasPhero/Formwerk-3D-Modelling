using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>
/// A click on a part of a group selects the group; only when the group (or something in it) is already selected, the single part is
/// (user wish). Operands of a boolean or merge and holes behave as before.
/// </summary>
public class GroupPickTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    /// <summary>A box and a sphere grouped, a loose cylinder.</summary>
    private static async Task<(MainViewModel Vm, Node Group, Node Box, Node Sphere, Node Cylinder)> Scene()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        var (box, sphere, cylinder) = (vm.Document.Root.Children[0], vm.Document.Root.Children[1], vm.Document.Root.Children[2]);
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([]);
        return (vm, group, box, sphere, cylinder);
    }

    private static void Click(MainViewModel vm, Node node, bool additive = false) => vm.PickCommand.Execute(new PickRequest(node.Id, additive));

    [UiFact]
    public async Task The_first_click_on_a_part_of_a_group_selects_the_group()
    {
        var (vm, group, box, _, _) = await Scene();

        Click(vm, box);

        Assert.Equal([group.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task A_second_click_on_a_part_selects_only_that_part()
    {
        var (vm, group, box, sphere, _) = await Scene();
        Click(vm, box);

        Click(vm, box);
        Assert.Equal([box.Id], vm.SelectedIds);

        Click(vm, sphere); // another part of the group while a part is selected: that part
        Assert.Equal([sphere.Id], vm.SelectedIds);
        Assert.Contains(group, vm.Document.Root.Children);
    }

    [UiFact]
    public async Task A_click_on_a_loose_object_or_empty_space_works_as_before()
    {
        var (vm, group, box, _, cylinder) = await Scene();
        Click(vm, box);

        Click(vm, cylinder);
        Assert.Equal([cylinder.Id], vm.SelectedIds);

        Click(vm, box); // the group again, because a loose object is selected
        Assert.Equal([group.Id], vm.SelectedIds);

        vm.PickCommand.Execute(new PickRequest(null, false));
        Assert.Empty(vm.SelectedIds);
    }

    [UiFact]
    public async Task Clicking_a_part_of_a_group_while_something_else_is_selected_selects_the_group()
    {
        var (vm, group, box, _, cylinder) = await Scene();
        vm.SelectOnly([cylinder.Id]);

        Click(vm, box);

        Assert.Equal([group.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task With_Ctrl_the_group_is_added_and_the_next_click_with_Ctrl_takes_the_part_in_its_place()
    {
        var (vm, group, box, _, cylinder) = await Scene();
        Click(vm, cylinder);

        Click(vm, box, additive: true);

        Assert.Equal(new HashSet<Guid> { cylinder.Id, group.Id }, [.. vm.SelectedIds]);
    }

    [UiFact]
    public async Task Nested_groups_are_entered_from_the_outside_in()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        var (box, sphere, cylinder) = (vm.Document.Root.Children[0], vm.Document.Root.Children[1], vm.Document.Root.Children[2]);
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var inner = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([inner.Id, cylinder.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var outer = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([]);

        Click(vm, box);
        Assert.Equal([outer.Id], vm.SelectedIds);
        Click(vm, box);
        Assert.Equal([inner.Id], vm.SelectedIds);
        Click(vm, box);
        Assert.Equal([box.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_operands_of_a_boolean_are_still_selected_as_a_whole()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        var (box, cylinder) = (vm.Document.Root.Children[0], vm.Document.Root.Children[1]);
        vm.SelectOnly([box.Id, cylinder.Id]);
        vm.MergeCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var boolean = vm.Document.Root.Children.Single();
        vm.SelectOnly([]);

        Click(vm, box);
        Assert.Equal([boolean.Id], vm.SelectedIds);
        Click(vm, box);
        Assert.Equal([boolean.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task A_press_to_drag_keeps_a_selection_that_already_covers_the_part_and_selects_the_group_otherwise()
    {
        var (vm, group, box, _, cylinder) = await Scene();

        vm.PickCommand.Execute(new PickRequest(box.Id, false, ForDrag: true)); // nothing selected: the group is taken along
        Assert.Equal([group.Id], vm.SelectedIds);

        vm.PickCommand.Execute(new PickRequest(box.Id, false, ForDrag: true)); // the group is selected: stays, so the whole group is dragged
        Assert.Equal([group.Id], vm.SelectedIds);

        vm.SelectOnly([box.Id]);
        vm.PickCommand.Execute(new PickRequest(box.Id, false, ForDrag: true)); // the part itself is selected: stays
        Assert.Equal([box.Id], vm.SelectedIds);

        vm.SelectOnly([cylinder.Id, group.Id]);
        vm.PickCommand.Execute(new PickRequest(box.Id, false, ForDrag: true)); // part of a multi selection: the selection is dragged
        Assert.Equal(new HashSet<Guid> { cylinder.Id, group.Id }, [.. vm.SelectedIds]);
    }
}
