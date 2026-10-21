using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The align panel (F-SNP-04): align per axis to the first object or the selection box, distribute evenly.</summary>
public class AlignViewModelTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    /// <summary>A box of the given width (X), depth 20, height 20, centered on (x, y) and standing at the height z.</summary>
    private static async Task<Node> AddBox(MainViewModel vm, double width, double x, double y = 0, double z = 0, double height = 20)
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[^1];
        vm.SelectOnly([box.Id]);
        vm.Properties!.Parameters[0].Text = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Parameters[2].Text = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Position[0].Text = x.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Position[1].Text = y.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Position[2].Text = z.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await vm.WaitForEvaluationAsync();
        return box;
    }

    /// <summary>Three boxes along X: 10 wide at 0 (-5..5), 30 wide at 40 (25..55), 10 wide at 100 (95..105). All selected in this order.</summary>
    private static async Task<(MainViewModel Vm, Node A, Node B, Node C)> Three()
    {
        var vm = NewViewModel();
        var a = await AddBox(vm, 10, 0);
        var b = await AddBox(vm, 30, 40, y: 10, z: 5);
        var c = await AddBox(vm, 10, 100, y: -10, z: 10, height: 40);
        vm.SelectOnly([a.Id, b.Id, c.Id]);
        return (vm, a, b, c);
    }

    private static Bounds BoxOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Select(i => i.Mesh.ComputeBounds())
            .Aggregate((x, y) => new Bounds(Vector3.Min(x.Min, y.Min), Vector3.Max(x.Max, y.Max)));

    [UiFact]
    public async Task The_panel_is_shown_for_two_or_more_objects_only()
    {
        var (vm, a, b, _) = await Three();
        Assert.True(vm.IsAlignVisible);

        vm.SelectOnly([a.Id, b.Id]);
        Assert.True(vm.IsAlignVisible);

        vm.SelectOnly([a.Id]);
        Assert.False(vm.IsAlignVisible);

        vm.SelectOnly([]);
        Assert.False(vm.IsAlignVisible);
    }

    [UiFact]
    public async Task Aligning_the_left_edges_to_the_selection_box_puts_them_on_the_leftmost_edge()
    {
        var (vm, a, b, c) = await Three();

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();

        Assert.All(new[] { a, b, c }, n => Assert.Equal(-5, BoxOf(vm, n).Min.X, 3));
    }

    [UiFact]
    public async Task Aligning_the_right_edges_and_the_centers_works_the_same_way()
    {
        var (vm, a, b, c) = await Three();

        vm.AlignCommand.Execute("X-Max");
        await vm.WaitForEvaluationAsync();
        Assert.All(new[] { a, b, c }, n => Assert.Equal(105, BoxOf(vm, n).Max.X, 3));

        vm.AlignCommand.Execute("X-Center");
        await vm.WaitForEvaluationAsync();
        var centers = new[] { a, b, c }.Select(n => BoxOf(vm, n).Center.X).Distinct().Select(x => MathF.Round(x, 2)).ToList();
        Assert.Single(centers);
    }

    [UiFact]
    public async Task Y_and_Z_are_aligned_along_their_own_axes_and_leave_the_others_alone()
    {
        var (vm, a, b, c) = await Three();
        var xs = new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.X).ToArray();

        vm.AlignCommand.Execute("Z-Min");
        await vm.WaitForEvaluationAsync();
        Assert.All(new[] { a, b, c }, n => Assert.Equal(0, BoxOf(vm, n).Min.Z, 3));

        vm.AlignCommand.Execute("Y-Center");
        await vm.WaitForEvaluationAsync();
        Assert.Single(new[] { a, b, c }.Select(n => MathF.Round(BoxOf(vm, n).Center.Y, 2)).Distinct());
        Assert.Equal(xs, new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.X).ToArray()); // X did not change
    }

    [UiFact]
    public async Task Aligning_to_the_first_selected_object_leaves_that_object_where_it_is()
    {
        var (vm, a, b, c) = await Three();
        vm.SelectOnly([b.Id, a.Id, c.Id]); // B was selected first
        vm.AlignToFirst = true;

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(25, BoxOf(vm, b).Min.X, 3);
        Assert.Equal(25, BoxOf(vm, a).Min.X, 3);
        Assert.Equal(25, BoxOf(vm, c).Min.X, 3);
    }

    [UiFact]
    public async Task The_first_selected_object_is_the_first_one_clicked_even_with_Ctrl()
    {
        var (vm, a, b, c) = await Three();
        vm.SelectOnly([]);
        vm.AlignToFirst = true;

        vm.PickCommand.Execute(new PickRequest(c.Id, false));
        vm.PickCommand.Execute(new PickRequest(a.Id, true));
        vm.PickCommand.Execute(new PickRequest(b.Id, true));
        vm.AlignCommand.Execute("X-Max");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(105, BoxOf(vm, c).Max.X, 3); // C was the first
        Assert.Equal(105, BoxOf(vm, a).Max.X, 3);
        Assert.Equal(105, BoxOf(vm, b).Max.X, 3);
    }

    [UiFact]
    public async Task Three_objects_are_spread_evenly_in_X_and_the_outer_ones_stay()
    {
        var (vm, a, b, c) = await Three();
        var before = (BoxOf(vm, a), BoxOf(vm, c));

        vm.DistributeCommand.Execute("X");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(before.Item1.Min.X, BoxOf(vm, a).Min.X, 3);
        Assert.Equal(before.Item2.Max.X, BoxOf(vm, c).Max.X, 3);
        var (boxA, boxB, boxC) = (BoxOf(vm, a), BoxOf(vm, b), BoxOf(vm, c));
        Assert.Equal(boxB.Min.X - boxA.Max.X, boxC.Min.X - boxB.Max.X, 3); // equal gaps: (110 - 50) / 2 = 30 each
        Assert.Equal(30, boxB.Min.X - boxA.Max.X, 3);
    }

    [UiFact]
    public async Task Distributing_changes_only_the_chosen_axis()
    {
        var (vm, a, b, c) = await Three();
        var ys = new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.Y).ToArray();
        var zs = new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.Z).ToArray();

        vm.DistributeCommand.Execute("X");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(ys, new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.Y).ToArray());
        Assert.Equal(zs, new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.Z).ToArray());
    }

    [UiFact]
    public async Task Distributing_needs_three_objects()
    {
        var (vm, a, b, _) = await Three();
        Assert.True(vm.DistributeCommand.CanExecute("X"));

        vm.SelectOnly([a.Id, b.Id]);

        Assert.False(vm.DistributeCommand.CanExecute("X"));
        Assert.True(vm.AlignCommand.CanExecute("X-Min")); // two are enough to align

        vm.SelectOnly([a.Id]);
        Assert.False(vm.AlignCommand.CanExecute("X-Min"));
    }

    [UiFact]
    public async Task Aligning_is_one_undo_step_and_undo_brings_everything_back()
    {
        var (vm, a, b, c) = await Three();
        var before = new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.X).ToArray();
        var steps = vm.HistoryEntries.Count;

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before, new[] { a, b, c }.Select(n => BoxOf(vm, n).Min.X).ToArray());
    }

    [UiFact]
    public async Task Nothing_to_do_makes_no_history_entry()
    {
        var (vm, _, _, _) = await Three();
        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();
        var steps = vm.HistoryEntries.Count;

        vm.AlignCommand.Execute("X-Min"); // already aligned

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task A_locked_object_stays_and_the_others_move()
    {
        var (vm, a, b, c) = await Three();
        b.Locked = true;

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(25, BoxOf(vm, b).Min.X, 3); // it keeps its place
        Assert.Equal(-5, BoxOf(vm, a).Min.X, 3); // a is the leftmost one, the box starts at -5
        Assert.Equal(-5, BoxOf(vm, c).Min.X, 3);
        Assert.Contains(b.Name, vm.StatusText);
    }

    [UiFact]
    public async Task A_group_counts_as_one_object_and_moves_as_a_whole()
    {
        var vm = NewViewModel();
        var a = await AddBox(vm, 10, 0);
        var b = await AddBox(vm, 10, 50);
        var c = await AddBox(vm, 10, 80);
        vm.SelectOnly([b.Id, c.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([a.Id, group.Id]);

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(-5, BoxOf(vm, b).Min.X, 3);
        Assert.Equal(25, BoxOf(vm, c).Min.X, 3); // the group keeps its inner distance: 30 mm between the two minimum edges
    }

    [UiFact]
    public async Task An_object_inside_a_moved_group_is_moved_by_the_right_amount_in_the_world()
    {
        var vm = NewViewModel();
        var a = await AddBox(vm, 10, 0);
        var b = await AddBox(vm, 10, 0);
        vm.SelectOnly([b.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = vm.Document.Root.Children.OfType<GroupNode>().Single();
        vm.SelectOnly([group.Id]);
        vm.Properties!.Position[0].Text = "100";
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([a.Id, b.Id]); // a at the top level, b inside the group at the world X = 100

        vm.AlignCommand.Execute("X-Min");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(BoxOf(vm, a).Min.X, BoxOf(vm, b).Min.X, 3);
    }

    [UiFact]
    public async Task A_wrong_parameter_does_nothing()
    {
        var (vm, _, _, _) = await Three();
        var steps = vm.HistoryEntries.Count;

        vm.AlignCommand.Execute("Q-Min");
        vm.AlignCommand.Execute("X-Middle");
        vm.AlignCommand.Execute(null);
        vm.DistributeCommand.Execute("W");

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }
}
