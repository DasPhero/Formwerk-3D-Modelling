using Formwerk.App.ViewModels;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Perspective or orthographic view (F-VIS-06): the switch in the view model.</summary>
public class ProjectionTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    [UiFact]
    public void The_view_starts_in_perspective()
    {
        Assert.False(NewViewModel().IsOrthographic);
    }

    [UiFact]
    public void The_command_switches_between_perspective_and_orthographic()
    {
        var vm = NewViewModel();

        vm.ToggleProjectionCommand.Execute(null);
        Assert.True(vm.IsOrthographic);

        vm.ToggleProjectionCommand.Execute(null);
        Assert.False(vm.IsOrthographic);
    }

    [UiFact]
    public void The_menu_item_can_set_the_projection_directly_and_the_view_is_told()
    {
        var vm = NewViewModel();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.IsOrthographic = true;

        Assert.Contains(nameof(MainViewModel.IsOrthographic), changed);
    }

    [UiFact]
    public void Switching_the_projection_is_not_a_document_change()
    {
        var vm = NewViewModel();

        vm.ToggleProjectionCommand.Execute(null);

        Assert.False(vm.IsDirty);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }
}
