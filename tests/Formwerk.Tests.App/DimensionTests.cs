using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Dimensions in the 3D view (F-PAR-02): which ones are shown, and typing a value into one.</summary>
public class DimensionTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static Node Only(MainViewModel vm) => vm.Document.Root.Children.Single();

    private static ViewportDimension Dim(MainViewModel vm, string id) => vm.ViewportDimensions!.Single(d => d.Id == id);

    [UiFact]
    public async Task A_selected_box_shows_width_depth_and_height()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        var dims = vm.ViewportDimensions!;

        Assert.Equal(["depth", "height", "width"], dims.Select(d => d.Id).Order(StringComparer.Ordinal));
        Assert.All(dims, d => Assert.Equal("20 mm", d.Text));
    }

    [UiFact]
    public async Task The_dimension_lines_lie_along_the_edges_of_the_box()
    {
        var vm = NewViewModel();
        await Add(vm, "box"); // 20 mm cube, origin at the middle of the bottom: x -10..10, y -10..10, z 0..20

        var width = Dim(vm, "width");
        var height = Dim(vm, "height");

        Assert.Equal(20, Vector3.Distance(width.From, width.To), 3);
        Assert.Equal(0, width.From.Z, 3);
        Assert.Equal(width.From.Y, width.To.Y, 3);
        Assert.Equal(20, Vector3.Distance(height.From, height.To), 3);
        Assert.Equal(width.From.Z, Math.Min(height.From.Z, height.To.Z), 3);
        Assert.Equal(20, Math.Max(height.From.Z, height.To.Z), 3);
    }

    [UiFact]
    public async Task A_cylinder_shows_its_diameter_once_and_its_height()
    {
        var vm = NewViewModel();
        await Add(vm, "cylinder");

        Assert.Equal(["diameter", "height"], vm.ViewportDimensions!.Select(d => d.Id).Order(StringComparer.Ordinal));
    }

    [UiFact]
    public async Task The_text_follows_the_size_and_drops_needless_zeros()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Parameters[2].Text = "12.5";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("12.5 mm", Dim(vm, "height").Text);
        Assert.Equal("20 mm", Dim(vm, "width").Text);
    }

    [UiFact]
    public async Task Nothing_is_shown_without_a_selection_for_several_objects_or_for_other_kinds_of_nodes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");

        vm.SelectOnly([]);
        Assert.Null(vm.ViewportDimensions);

        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        Assert.Null(vm.ViewportDimensions);

        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Null(vm.ViewportDimensions); // the group is selected
    }

    [UiFact]
    public async Task A_locked_object_has_no_dimensions_to_type_into()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        Only(vm).Locked = true;
        vm.SelectOnly([]);
        vm.SelectOnly([Only(vm).Id]);

        Assert.Null(vm.ViewportDimensions);
    }

    [UiFact]
    public async Task Typing_a_value_changes_that_dimension_of_the_object()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.EditDimensionCommand.Execute(new DimensionEdit("height", "30"));
        await vm.WaitForEvaluationAsync();

        var box = Assert.IsType<PrimitiveNode>(Only(vm));
        Assert.Equal("30", box.Parameters["height"]);
        Assert.Equal("30 mm", Dim(vm, "height").Text);
        Assert.Equal("20 × 20 × 30 mm", vm.Properties!.SizeText);
    }

    [UiFact]
    public async Task A_typed_expression_or_relative_value_works_like_in_the_panel()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.EditDimensionCommand.Execute(new DimensionEdit("width", "20/2+3"));
        vm.EditDimensionCommand.Execute(new DimensionEdit("depth", "+10"));
        await vm.WaitForEvaluationAsync();

        Assert.Equal("13 mm", Dim(vm, "width").Text);
        Assert.Equal("30 mm", Dim(vm, "depth").Text);
    }

    [UiFact]
    public async Task A_typed_value_is_one_undo_step()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var before = vm.HistoryEntries.Count;

        vm.EditDimensionCommand.Execute(new DimensionEdit("height", "30"));
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal("20 mm", Dim(vm, "height").Text);
    }

    [UiFact]
    public async Task A_wrong_value_changes_nothing_and_says_why()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.EditDimensionCommand.Execute(new DimensionEdit("height", "abc"));
        await vm.WaitForEvaluationAsync();

        Assert.Equal("20 mm", Dim(vm, "height").Text);
        Assert.NotEqual("", vm.StatusText);
        Assert.Contains("abc", vm.StatusText + vm.Properties!.Parameters[2].Error);
    }

    [UiFact]
    public async Task An_unknown_dimension_or_no_selection_is_ignored()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var before = vm.HistoryEntries.Count;

        vm.EditDimensionCommand.Execute(new DimensionEdit("nonsense", "5"));
        vm.EditDimensionCommand.Execute(null);
        vm.SelectOnly([]);
        vm.EditDimensionCommand.Execute(new DimensionEdit("height", "5"));

        Assert.Equal(before, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task A_turned_object_shows_its_own_dimensions_along_its_own_axes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[2].Text = "40"; // 20 x 20 x 40
        vm.Properties.Rotation[0].Text = "90";
        await vm.WaitForEvaluationAsync();

        var height = Dim(vm, "height");

        Assert.Equal(40, Vector3.Distance(height.From, height.To), 2);
        Assert.Equal("40 mm", height.Text);
        Assert.True(Math.Abs(height.To.Y - height.From.Y) > 35, "the height of the turned box now runs along Y");
    }
}
