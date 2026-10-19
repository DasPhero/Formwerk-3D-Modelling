using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

public class MainViewModelTests
{
    private static MainViewModel NewViewModel(string? exportPath = null) =>
        new(new ManifoldKernel(), new FakeDialogs { SavePath = exportPath });

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task Idle(MainViewModel vm) => await vm.WaitForEvaluationAsync();

    private static Node Only(MainViewModel vm) => vm.Document.Root.Children.Single();

    private static Bounds BoundsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();

    [UiFact]
    public async Task Adding_a_shape_selects_it_and_shows_it_resting_on_the_plate()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        var node = Only(vm);
        Assert.Equal([node.Id], vm.SelectedIds);
        Assert.Single(vm.Nodes);
        Assert.NotNull(vm.Properties);
        var item = Assert.Single(vm.ViewportItems);
        Assert.Equal(0, item.Mesh.ComputeBounds().Min.Z, 3);
        Assert.Equal("20 × 20 × 20 mm", vm.Properties!.SizeText);
        Assert.True(vm.ExportStlCommand.CanExecute(null));
    }

    [UiFact]
    public async Task New_shapes_do_not_overlap_existing_ones()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "cylinder");
        await Add(vm, "sphere");

        var bounds = vm.Document.Root.Children.Select(n => BoundsOf(vm, n.Id)).ToList();
        for (var i = 0; i < bounds.Count; i++)
        for (var j = i + 1; j < bounds.Count; j++)
        {
            var overlapX = bounds[i].Min.X < bounds[j].Max.X && bounds[i].Max.X > bounds[j].Min.X;
            Assert.False(overlapX, $"shape {i} overlaps shape {j}");
        }
    }

    // F-TRF-02: typing "+10" moves by exactly 10 mm; everything is undoable.
    [UiFact]
    public async Task Typed_relative_position_moves_exactly_and_can_be_undone()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var node = Only(vm);
        var startX = node.Transform.Position.X;

        vm.Properties!.Position[0].Text = "+10";
        await Idle(vm);

        Assert.Equal(startX + 10, node.Transform.Position.X, 3);
        Assert.Equal(startX + 10, BoundsOf(vm, node.Id).Center.X, 2);
        Assert.Equal((startX + 10).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), vm.Properties.Position[0].Text);

        vm.UndoCommand.Execute(null);
        Assert.Equal(startX, node.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Expressions_are_accepted_in_position_fields()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Position[1].Text = "20/2+3";
        await Idle(vm);

        Assert.Equal(13, Only(vm).Transform.Position.Y, 3);
    }

    [UiFact]
    public async Task Invalid_position_input_shows_an_error_and_changes_nothing()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var before = Only(vm).Transform;
        var historyBefore = vm.HistoryEntries.Count;

        vm.Properties!.Position[0].Text = "banana";

        Assert.True(vm.Properties.Position[0].HasError);
        Assert.Equal(before, Only(vm).Transform);
        Assert.Equal(historyBefore, vm.HistoryEntries.Count);
    }

    // F-PAR-01 / F-LIB-01: editing a dimension updates the shape.
    [UiFact]
    public async Task Changing_a_dimension_updates_the_shape()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Parameters[0].Text = "50"; // width
        await Idle(vm);

        Assert.Equal(50, BoundsOf(vm, Only(vm).Id).Size.X, 3);
        Assert.StartsWith("50 ×", vm.Properties.SizeText);
    }

    [UiFact]
    public async Task Percent_input_scales_a_dimension()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Parameters[2].Text = "150%"; // height 20 -> 30
        await Idle(vm);

        Assert.Equal(30, BoundsOf(vm, Only(vm).Id).Size.Z, 3);
    }

    // F-PAR-03: a dimension can be linked to a project variable.
    [UiFact]
    public async Task Dimension_linked_to_a_variable_follows_the_variable()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Document.Variables["Wandstaerke"] = "2";

        vm.Properties!.Parameters[0].Text = "Wandstaerke*10";
        await Idle(vm);
        Assert.Equal(20, BoundsOf(vm, Only(vm).Id).Size.X, 3);

        vm.Document.Variables["Wandstaerke"] = "3";
        vm.Properties.Parameters[1].Text = "10"; // any change triggers a new evaluation
        await Idle(vm);
        Assert.Equal(30, BoundsOf(vm, Only(vm).Id).Size.X, 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("99999")]
    [InlineData("abc")]
    public async Task Out_of_range_dimensions_are_rejected(string text)
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.Parameters[0].Text = text;

        Assert.True(vm.Properties.Parameters[0].HasError);
        Assert.Equal("20", ((PrimitiveNode)Only(vm)).Parameters["width"]);
    }

    [UiFact]
    public async Task Rotation_fields_rotate_the_shape()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "40"; // width 40, depth 20
        await Idle(vm);

        vm.Properties.Rotation[2].Text = "90";
        await Idle(vm);

        var size = BoundsOf(vm, Only(vm).Id).Size;
        Assert.Equal(20, size.X, 2);
        Assert.Equal(40, size.Y, 2);
    }

    [UiFact]
    public async Task Renaming_validates_and_is_undoable()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Properties!.NameField.Text = "   ";
        Assert.True(vm.Properties.NameField.HasError);
        Assert.Equal("Box", Only(vm).Name);

        vm.Properties.NameField.Text = "Base plate";
        Assert.Equal("Base plate", Only(vm).Name);
        Assert.Equal("Base plate", vm.Nodes.Single().Name);
        vm.UndoCommand.Execute(null);
        Assert.Equal("Box", Only(vm).Name);
    }

    // F-BOO-01: toggling "Hole" and grouping cuts the neighbour; the hole stays as a ghost.
    [UiFact]
    public async Task Hole_in_a_group_cuts_the_box_and_shows_a_ghost()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Only(vm);
        await Add(vm, "cylinder");
        var cylinder = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = box.Transform.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Parameters[0].Text = "8";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Position[2].Text = "-10";
        vm.Properties.IsHole = true;
        await Idle(vm);

        vm.SelectOnly([box.Id, cylinder.Id]);
        Assert.True(vm.GroupCommand.CanExecute(null));
        vm.GroupCommand.Execute(null);
        await Idle(vm);

        var solid = Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
        Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
        Assert.True(solid.Mesh.ComputeVolume() < 8000 - 400, $"volume {solid.Mesh.ComputeVolume()}");
        Assert.True(solid.Mesh.IsWatertight());
    }

    [UiFact]
    public async Task Group_ungroup_roundtrip_and_selection()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));

        vm.GroupCommand.Execute(null);
        await Idle(vm);
        var group = Assert.IsType<GroupNode>(Only(vm));
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Equal([1, 1], vm.Nodes.Skip(1).Select(n => n.Depth));
        Assert.Equal([group.Id], vm.SelectedIds);

        vm.UngroupCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Equal(2, vm.SelectedIds.Count);
    }

    [UiFact]
    public async Task Group_is_disabled_for_nodes_of_different_parents_and_merge_needs_two()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var inner = Only(vm);
        vm.SelectOnly([inner.Id]);
        vm.GroupCommand.Execute(null);
        await Idle(vm);
        var group = Assert.IsType<GroupNode>(Only(vm));
        await Add(vm, "cylinder");
        var outside = vm.Document.Root.Children.Last();

        vm.SelectOnly([inner.Id, outside.Id]);
        Assert.False(vm.GroupCommand.CanExecute(null));

        vm.SelectOnly([outside.Id]);
        Assert.False(vm.MergeCommand.CanExecute(null));
        Assert.NotNull(group);
    }

    [UiFact]
    public async Task Boolean_difference_shows_one_body_and_clicking_it_selects_the_node()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var box = Only(vm);
        await Add(vm, "cylinder");
        var cylinder = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = box.Transform.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Position[2].Text = "-10";
        await Idle(vm);

        vm.SelectOnly([box.Id, cylinder.Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Difference);
        await Idle(vm);

        var boolean = Assert.IsType<BooleanNode>(Only(vm));
        Assert.Single(vm.ViewportItems);
        Assert.True(vm.Properties!.IsBoolean);

        // Clicking the result selects the boolean node, not one of its operands.
        vm.PickCommand.Execute(new PickRequest(box.Id, false));
        Assert.Equal([boolean.Id], vm.SelectedIds);

        vm.Properties!.Operation = BooleanOperation.Union;
        await Idle(vm);
        Assert.Equal(BooleanOperation.Union, boolean.Operation);
    }

    [UiFact]
    public async Task Picking_empty_space_clears_the_selection_and_additive_picking_toggles()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var ids = vm.Document.Root.Children.Select(n => n.Id).ToList();

        vm.PickCommand.Execute(new PickRequest(ids[0], false));
        vm.PickCommand.Execute(new PickRequest(ids[1], true));
        Assert.Equal(2, vm.SelectedIds.Count);
        Assert.Null(vm.Properties);

        vm.PickCommand.Execute(new PickRequest(ids[1], true));
        Assert.Equal([ids[0]], vm.SelectedIds);

        vm.PickCommand.Execute(new PickRequest(null, false));
        Assert.Empty(vm.SelectedIds);
    }

    [UiFact]
    public async Task Selecting_in_the_list_updates_the_viewport_selection_and_properties()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        var first = vm.Nodes[0];

        first.IsSelected = true;

        // The sphere was selected by adding it; the box joins it.
        Assert.Contains(first.Id, vm.SelectedIds);
        Assert.Equal(2, vm.SelectedIds.Count);
        Assert.Null(vm.Properties);

        vm.Nodes[1].IsSelected = false;
        Assert.Equal([first.Id], vm.SelectedIds);
        Assert.Equal(first.Id, vm.Properties!.NodeId);
    }

    [UiFact]
    public async Task Duplicate_and_delete_work_with_undo()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var original = Only(vm);

        vm.DuplicateCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        var copy = vm.Document.Root.Children[1];
        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal(original.Transform.Position.X + 15, copy.Transform.Position.X, 3);
        Assert.Equal([copy.Id], vm.SelectedIds);

        vm.DeleteCommand.Execute(null);
        await Idle(vm);
        Assert.Single(vm.Document.Root.Children);
        Assert.Empty(vm.SelectedIds);

        vm.UndoCommand.Execute(null);
        await Idle(vm);
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task Hiding_an_object_removes_it_from_the_view()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.Nodes[0].Visible = false;
        await Idle(vm);

        Assert.Empty(vm.ViewportItems);
        Assert.False(vm.ExportStlCommand.CanExecute(null));
    }

    // E-01: clicking an entry of the history list jumps to that state.
    [UiFact]
    public async Task History_list_jumps_to_any_state()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        await Add(vm, "cylinder");
        Assert.Equal(4, vm.HistoryEntries.Count);
        Assert.Equal(3, vm.HistorySelectedIndex);

        vm.HistorySelectedIndex = 1;
        await Idle(vm);

        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(1, vm.HistorySelectedIndex);
        Assert.True(vm.HistoryEntries[2].IsFuture);
        Assert.False(vm.HistoryEntries[1].IsFuture);

        vm.HistorySelectedIndex = 3;
        await Idle(vm);
        Assert.Equal(3, vm.Document.Root.Children.Count);
        Assert.Single(vm.HistoryEntries.Where(e => e.Index == 0), e => e.Name == "Initial state");
    }

    [UiFact]
    public async Task Undo_redo_commands_report_their_availability()
    {
        var vm = NewViewModel();
        Assert.False(vm.UndoCommand.CanExecute(null));
        await Add(vm, "box");
        Assert.True(vm.UndoCommand.CanExecute(null));
        vm.UndoCommand.Execute(null);
        Assert.True(vm.RedoCommand.CanExecute(null));
        Assert.Empty(vm.Nodes);
    }

    [UiFact]
    public async Task Export_writes_a_binary_stl_of_the_visible_solids()
    {
        var path = Path.Combine(Path.GetTempPath(), $"formwerk-test-{Guid.NewGuid():N}.stl");
        try
        {
            var vm = NewViewModel(path);
            await Add(vm, "box");

            vm.ExportStlCommand.Execute(null);

            Assert.True(File.Exists(path));
            Assert.Equal(Formwerk.IO.StlWriter.SizeFor(12), new FileInfo(path).Length);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Contains("Exported 12 triangles", vm.StatusText);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [UiFact]
    public async Task Cancelled_export_dialog_writes_nothing()
    {
        var vm = NewViewModel(exportPath: null);
        await Add(vm, "box");
        var status = vm.StatusText;

        vm.ExportStlCommand.Execute(null);

        Assert.Equal(status, vm.StatusText);
    }

    [UiFact]
    public async Task Failing_node_is_marked_and_status_reports_the_problem()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "10";
        ((PrimitiveNode)Only(vm)).Parameters["width"] = "1/0"; // bypass the field validation on purpose
        vm.Document.NotifyChanged();
        await Idle(vm);

        Assert.True(vm.Nodes[0].HasError);
        Assert.Contains("problem", vm.StatusText);
    }

    [UiFact]
    public async Task Evaluation_runs_in_the_background_and_old_runs_are_superseded()
    {
        var vm = NewViewModel();
        await Add(vm, "sphere");
        // Rapid edits: only the last state may end up in the view.
        for (var i = 1; i <= 10; i++) vm.Properties!.Parameters[0].Text = (10 + i).ToString();
        await Idle(vm);

        Assert.Equal(20, BoundsOf(vm, Only(vm).Id).Size.X, 2);
        Assert.False(vm.IsBusy);
    }
}
