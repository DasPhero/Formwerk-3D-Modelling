using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Opening a group, boolean or merge by double click (F-BOO-04, F-BOO-02 in the 3D view).</summary>
public class OpenGroupTests
{
    private static MainViewModel NewViewModel(FakeDialogs? dialogs = null) => new(new ManifoldKernel(), dialogs ?? new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task Idle(MainViewModel vm) => await vm.WaitForEvaluationAsync();

    /// <summary>Box and sphere grouped (as a plain group), plus a loose cylinder.</summary>
    private static async Task<(MainViewModel Vm, Node Group, Node Box, Node Sphere, Node Cylinder)> GroupAndCylinder(FakeDialogs? dialogs = null)
    {
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        var (box, sphere, cylinder) = (vm.Document.Root.Children[0], vm.Document.Root.Children[1], vm.Document.Root.Children[2]);
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await Idle(vm);
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([]);
        return (vm, group, box, sphere, cylinder);
    }

    private static HashSet<Guid> Dimmed(MainViewModel vm) => [.. vm.ViewportItems.Where(i => i.Dimmed).Select(i => i.NodeId)];

    [UiFact]
    public async Task A_double_click_on_a_part_opens_its_group_and_dims_the_rest()
    {
        var (vm, group, box, sphere, cylinder) = await GroupAndCylinder();

        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        Assert.Equal(group.Id, vm.OpenContainerId);
        Assert.True(vm.IsGroupOpen);
        Assert.Equal(new HashSet<Guid> { cylinder.Id }, Dimmed(vm));
        Assert.DoesNotContain(vm.ViewportItems, i => (i.NodeId == box.Id || i.NodeId == sphere.Id) && i.Dimmed);
    }

    [UiFact]
    public async Task The_part_that_was_double_clicked_is_selected()
    {
        var (vm, _, box, _, _) = await GroupAndCylinder();

        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        Assert.Equal([box.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task A_double_click_on_a_single_top_level_object_does_nothing()
    {
        var (vm, _, _, _, cylinder) = await GroupAndCylinder();

        vm.OpenGroupCommand.Execute((Guid?)cylinder.Id);

        Assert.False(vm.IsGroupOpen);
        Assert.Empty(Dimmed(vm));
    }

    [UiFact]
    public async Task The_operands_of_a_boolean_can_be_picked_one_by_one_inside()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        var (box, cylinder) = (vm.Document.Root.Children[0], vm.Document.Root.Children[1]);
        vm.SelectOnly([box.Id, cylinder.Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Union);
        await Idle(vm);
        var boolean = vm.Document.Root.Children.Single();

        vm.PickCommand.Execute(new PickRequest(box.Id, false));
        Assert.Equal([boolean.Id], vm.SelectedIds); // outside: the boolean as a whole

        vm.OpenGroupCommand.Execute((Guid?)box.Id);
        Assert.Equal(boolean.Id, vm.OpenContainerId);
        vm.PickCommand.Execute(new PickRequest(cylinder.Id, false));
        Assert.Equal([cylinder.Id], vm.SelectedIds); // inside: the operand alone
    }

    [UiFact]
    public async Task Inside_a_click_on_empty_space_only_clears_the_selection()
    {
        var (vm, group, box, _, _) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.PickCommand.Execute(new PickRequest(null, false));

        Assert.Empty(vm.SelectedIds);
        Assert.Equal(group.Id, vm.OpenContainerId);
    }

    [UiFact]
    public async Task A_click_on_a_dimmed_object_leaves_the_group_and_selects_that_object()
    {
        var (vm, _, box, _, cylinder) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.PickCommand.Execute(new PickRequest(cylinder.Id, false));

        Assert.False(vm.IsGroupOpen);
        Assert.Equal([cylinder.Id], vm.SelectedIds);
        Assert.Empty(Dimmed(vm));
    }

    [UiFact]
    public async Task A_double_click_on_empty_space_leaves_the_group()
    {
        var (vm, _, box, _, _) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.OpenGroupCommand.Execute((Guid?)null);

        Assert.False(vm.IsGroupOpen);
        Assert.Empty(Dimmed(vm));
    }

    [UiFact]
    public async Task Esc_leaves_the_group_and_selects_it()
    {
        var (vm, group, box, _, _) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.ExitGroupCommand.Execute(null);

        Assert.False(vm.IsGroupOpen);
        Assert.Equal([group.Id], vm.SelectedIds);
        Assert.Empty(Dimmed(vm));
    }

    [UiFact]
    public async Task Esc_in_nested_groups_goes_up_one_level_at_a_time()
    {
        var (vm, inner, box, _, cylinder) = await GroupAndCylinder();
        vm.SelectOnly([inner.Id, cylinder.Id]);
        vm.GroupCommand.Execute(null);
        await Idle(vm);
        var outer = vm.Document.Root.Children.Single();
        vm.SelectOnly([]);

        vm.OpenGroupCommand.Execute((Guid?)box.Id);
        Assert.Equal(inner.Id, vm.OpenContainerId);

        vm.ExitGroupCommand.Execute(null);
        Assert.Equal(outer.Id, vm.OpenContainerId);
        Assert.Equal([inner.Id], vm.SelectedIds);

        vm.ExitGroupCommand.Execute(null);
        Assert.False(vm.IsGroupOpen);
        Assert.Equal([outer.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task Esc_without_an_open_group_does_nothing()
    {
        var (vm, _, _, _, cylinder) = await GroupAndCylinder();
        vm.SelectOnly([cylinder.Id]);

        vm.ExitGroupCommand.Execute(null);

        Assert.Equal([cylinder.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task Undoing_the_creation_of_the_group_leaves_the_mode()
    {
        var (vm, _, box, _, _) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.UndoCommand.Execute(null); // the group is gone
        await Idle(vm);

        Assert.False(vm.IsGroupOpen);
        Assert.Empty(Dimmed(vm));
    }

    [UiFact]
    public async Task A_rectangle_inside_takes_only_parts_of_the_open_group()
    {
        var (vm, _, box, sphere, cylinder) = await GroupAndCylinder();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.MarqueeCommand.Execute(new MarqueeRequest([box.Id, sphere.Id, cylinder.Id], false));

        Assert.Equal(new HashSet<Guid> { box.Id, sphere.Id }, vm.SelectedIds.ToHashSet());
        Assert.True(vm.IsGroupOpen);
    }

    [UiFact]
    public async Task The_hint_names_the_open_group_and_how_to_leave()
    {
        var (vm, group, box, _, _) = await GroupAndCylinder();

        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        Assert.Contains(group.Name, vm.HintText);
        Assert.Contains("Esc", vm.HintText);

        vm.ExitGroupCommand.Execute(null);
        Assert.DoesNotContain(Texts.Get("Hint.GroupOpen").Split('{')[0], vm.HintText);
    }

    [UiFact]
    public async Task A_new_document_starts_without_an_open_group()
    {
        var (vm, _, box, _, _) = await GroupAndCylinder(new FakeDialogs { UnsavedAnswer = UnsavedChoice.Discard }); // unsaved changes: throw them away
        vm.OpenGroupCommand.Execute((Guid?)box.Id);

        vm.NewCommand.Execute(null);
        await Idle(vm);

        Assert.False(vm.IsGroupOpen);
    }
}
