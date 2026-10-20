using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Linear patterns in the program (F-PAT-02, F-PAT-03): make one from the selection, edit count, spacing, direction, skipped instances.</summary>
public class PatternViewModelTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<(MainViewModel Vm, Node Box)> BoxScene()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box"); // 20 x 20 x 20
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        return (vm, box);
    }

    private static async Task<PatternNode> MakeLinear(MainViewModel vm)
    {
        vm.LinearPatternCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Descendants().OfType<PatternNode>().Single();
    }

    private static int SolidCount(MainViewModel vm) => vm.ViewportItems.Count(i => i.Kind == ViewportItemKind.Solid);

    private static FieldViewModel Field(PropertiesViewModel props, string name)
    {
        var index = PatternParameters.For(PatternKind.Linear).ToList().FindIndex(d => d.Name == name);
        return props.Parameters[index];
    }

    [UiFact]
    public async Task The_selection_becomes_a_row_of_three_in_one_undo_step()
    {
        var (vm, box) = await BoxScene();
        var steps = vm.HistoryEntries.Count;

        var pattern = await MakeLinear(vm);

        Assert.Equal(PatternKind.Linear, pattern.Kind);
        Assert.Same(box, Assert.Single(pattern.Children));
        Assert.Same(pattern, Assert.Single(vm.Document.Root.Children));
        Assert.Equal(3, SolidCount(vm));
        Assert.Equal([pattern.Id], vm.SelectedIds);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(box, Assert.Single(vm.Document.Root.Children));
        Assert.Equal(1, SolidCount(vm));
    }

    [UiFact]
    public async Task The_first_spacing_leaves_a_gap_between_the_instances()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);

        var boxes = vm.ViewportItems.Select(i => i.Mesh.ComputeBounds()).OrderBy(b => b.Min.X).ToList();

        Assert.True(boxes[1].Min.X > boxes[0].Max.X + 1, "the instances touch or overlap");
        Assert.True(boxes[2].Min.X > boxes[1].Max.X + 1);
    }

    [UiFact]
    public void Without_a_selection_there_is_nothing_to_repeat()
    {
        var vm = NewViewModel();

        Assert.False(vm.LinearPatternCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Objects_with_different_parents_cannot_be_repeated_together()
    {
        var (vm, box) = await BoxScene();
        vm.AddShapeCommand.Execute("sphere");
        await vm.WaitForEvaluationAsync();
        var sphere = vm.Document.Root.Children[1];
        vm.SelectOnly([sphere.Id]);
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        var cylinder = vm.Document.Root.Children[2];
        vm.SelectOnly([box.Id, sphere.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([box.Id, cylinder.Id]); // one is in the group, the other is not

        Assert.False(vm.LinearPatternCommand.CanExecute(null));
    }

    [UiFact]
    public async Task A_hole_in_a_group_can_be_repeated_and_cuts_at_every_instance()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = (PrimitiveNode)vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.Properties!.Parameters[0].Text = "100"; // width
        vm.Properties.Parameters[2].Text = "10"; // height
        vm.PlaceHole(new FacePick(box.Id, new System.Numerics.Vector3(-30, 0, 10), System.Numerics.Vector3.UnitZ)); // makes the group
        await vm.WaitForEvaluationAsync();
        var hole = vm.Document.Root.Descendants().Single(n => n is PrimitiveNode { GeneratorId: "hole" });
        double Volume() => vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (double)i.Mesh.ComputeVolume());
        var oneHole = 100 * 20 * 10 - Volume();
        vm.SelectOnly([hole.Id]);

        vm.LinearPatternCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var pattern = vm.Document.Root.Descendants().OfType<PatternNode>().Single();
        Assert.IsType<GroupNode>(pattern.Parent); // the pattern sits in the group, next to the plate
        Assert.InRange((100 * 20 * 10 - Volume()) / oneHole, 2.9, 3.1); // three holes now
    }

    [UiFact]
    public async Task The_pattern_has_its_values_in_the_properties_and_changing_the_count_follows()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);

        var props = vm.Properties!;
        Assert.True(props.IsPattern);
        Assert.Equal("3", Field(props, "count").Text);
        Assert.True(Field(props, "spacing").IsVisible);
        Assert.True(Field(props, "countY").IsVisible);

        Field(props, "count").Text = "5";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("5", pattern.Parameters["count"]);
        Assert.Equal(5, SolidCount(vm));

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(3, SolidCount(vm));
    }

    [UiFact]
    public async Task The_choices_are_not_shown_as_numbers()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);

        var props = vm.Properties!;

        Assert.False(Field(props, "axis").IsVisible);
        Assert.False(Field(props, "axisY").IsVisible);
        Assert.False(Field(props, "totalLength").IsVisible);
        Assert.False(Field(props, "totalLengthY").IsVisible);
        Assert.Equal(3, props.PatternAxes.Count);
    }

    [UiFact]
    public async Task A_grid_is_made_by_a_second_count()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);

        Field(vm.Properties!, "count").Text = "4";
        Field(vm.Properties!, "countY").Text = "3";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(12, SolidCount(vm));
        Assert.Equal(3, vm.ViewportItems.Select(i => Math.Round(i.Mesh.ComputeBounds().Center.Y)).Distinct().Count()); // three rows
    }

    [UiFact]
    public async Task The_direction_is_chosen_with_the_axis_choice()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);

        vm.Properties!.PatternAxis = 1; // Y
        await vm.WaitForEvaluationAsync();

        Assert.Equal("1", pattern.Parameters["axis"]);
        var centers = vm.ViewportItems.Select(i => i.Mesh.ComputeBounds().Center).ToList();
        Assert.All(centers, c => Assert.Equal(centers[0].X, c.X, 3));
        Assert.Equal(3, centers.Select(c => Math.Round(c.Y)).Distinct().Count());
    }

    [UiFact]
    public async Task The_spacing_can_mean_the_total_length()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);
        Field(vm.Properties!, "count").Text = "5";
        Field(vm.Properties!, "spacing").Text = "100";

        vm.Properties!.PatternTotalLength = true;
        await vm.WaitForEvaluationAsync();

        Assert.Equal("1", pattern.Parameters["totalLength"]);
        var xs = vm.ViewportItems.Select(i => i.Mesh.ComputeBounds().Center.X).Order().ToList();
        Assert.Equal(100, xs[^1] - xs[0], 3); // from the first to the last instance
    }

    [UiFact]
    public async Task Skipping_leaves_instances_out_in_one_undo_step()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);
        Field(vm.Properties!, "count").Text = "5";
        await vm.WaitForEvaluationAsync();
        var steps = vm.HistoryEntries.Count;

        vm.Properties!.SkipField!.Text = "2, 4";
        await vm.WaitForEvaluationAsync();

        Assert.Equal([2, 4], pattern.Skipped.ToArray());
        Assert.Equal(3, SolidCount(vm));
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Null(vm.Properties!.SkipField!.Error);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(pattern.Skipped);
        Assert.Equal(5, SolidCount(vm));
        Assert.Equal("", vm.Properties!.SkipField!.Text);
    }

    [UiFact]
    public async Task A_wrong_skip_text_is_refused_and_nothing_changes()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);
        var steps = vm.HistoryEntries.Count;

        vm.Properties!.SkipField!.Text = "two";

        Assert.NotNull(vm.Properties.SkipField!.Error);
        Assert.Empty(pattern.Skipped);
        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task The_skip_text_is_shown_in_short_form()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);
        Field(vm.Properties!, "count").Text = "9";

        vm.Properties!.SkipField!.Text = "3 4 5, 7";

        Assert.Equal("3-5, 7", vm.Properties!.SkipField!.Text);
    }

    [UiFact]
    public async Task The_number_of_instances_is_told()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);
        Assert.Contains("3", vm.Properties!.InstanceText);

        Field(vm.Properties!, "count").Text = "4";
        Field(vm.Properties!, "countY").Text = "2";
        Assert.Contains("8", vm.Properties!.InstanceText);

        vm.Properties!.SkipField!.Text = "1, 2";
        Assert.Contains("6", vm.Properties!.InstanceText);
    }

    [UiFact]
    public async Task A_click_on_an_instance_selects_the_pattern()
    {
        var (vm, box) = await BoxScene();
        var pattern = await MakeLinear(vm);
        vm.SelectOnly([]);

        vm.PickCommand.Execute(new PickRequest(box.Id, false));

        Assert.Equal([pattern.Id], vm.SelectedIds);
    }

    [UiFact]
    public async Task The_pattern_shows_in_the_object_list_with_its_own_glyph()
    {
        var (vm, box) = await BoxScene();
        var pattern = await MakeLinear(vm);

        var item = vm.Nodes.Single(n => n.Id == pattern.Id);

        Assert.NotEqual(vm.Nodes.Single(n => n.Id == box.Id).Glyph, item.Glyph);
        Assert.Contains(vm.Nodes, n => n.Id == box.Id); // the source is listed below it
    }

    [UiFact]
    public async Task A_variable_can_drive_the_count_and_the_field_is_marked_as_linked()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[0].Name.Text = "Anzahl";
        vm.Variables.Rows[0].Expression.Text = "4";

        Field(vm.Properties!, "count").Text = "Anzahl";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(4, SolidCount(vm));
        Assert.True(Field(vm.Properties!, "count").IsLinked);

        vm.Variables.Rows[0].Expression.Text = "6";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(6, SolidCount(vm));
    }

    [UiFact]
    public async Task Renaming_a_variable_follows_into_the_pattern()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[0].Name.Text = "Anzahl";
        vm.Variables.Rows[0].Expression.Text = "4";
        Field(vm.Properties!, "count").Text = "Anzahl";

        vm.Variables.Rows[0].Name.Text = "Stueck";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Stueck", pattern.Parameters["count"]);
        Assert.Equal(4, SolidCount(vm));
    }

    [UiFact]
    public async Task A_duplicate_of_a_pattern_keeps_its_skipped_instances()
    {
        var (vm, _) = await BoxScene();
        await MakeLinear(vm);
        vm.Properties!.SkipField!.Text = "2";

        vm.DuplicateCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var patterns = vm.Document.Root.Children.OfType<PatternNode>().ToList();
        Assert.Equal(2, patterns.Count);
        Assert.All(patterns, p => Assert.Equal([2], p.Skipped.ToArray()));
    }

    [UiFact]
    public async Task The_pattern_survives_save_and_load()
    {
        var (vm, _) = await BoxScene();
        var pattern = await MakeLinear(vm);
        vm.Properties!.SkipField!.Text = "3";

        var path = Path.Combine(Path.GetTempPath(), $"pat-{Guid.NewGuid():N}.fwp");
        ProjectFile.SaveAtomic(path, vm.Document);
        var loaded = ProjectFile.Load(path);
        File.Delete(path);

        var copy = Assert.IsType<PatternNode>(Assert.Single(loaded.Root.Children));
        Assert.Equal([3], copy.Skipped.ToArray());
        Assert.Equal(pattern.Parameters["count"], copy.Parameters["count"]);
    }
}
