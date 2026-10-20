using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The variable table (F-PAR-03): create, change, rename and delete project variables; linked fields.</summary>
public class VariablesViewModelTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    /// <summary>Adds a variable through the table and gives it a name and an expression, as the user would.</summary>
    private static VariableRowViewModel Add(MainViewModel vm, string name, string expression)
    {
        vm.Variables.AddCommand.Execute(null);
        var row = vm.Variables.Rows[^1];
        row.Name.Text = name;
        row.Expression.Text = expression;
        return row;
    }

    private static async Task<PrimitiveNode> BoxWithWidth(MainViewModel vm, string width)
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = (PrimitiveNode)vm.Document.Root.Children[^1];
        vm.SelectOnly([box.Id]);
        var index = ShapeLibrary.Find("box")!.Parameters.ToList().FindIndex(p => p.Name == "width");
        vm.Properties!.Parameters[index].Text = width;
        await vm.WaitForEvaluationAsync();
        return box;
    }

    private static double WidthOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Max(i => (double)i.Mesh.ComputeBounds().Size.X);

    [UiFact]
    public void The_table_starts_empty_and_a_row_is_added_with_a_free_name()
    {
        var vm = NewViewModel();
        Assert.Empty(vm.Variables.Rows);

        vm.Variables.AddCommand.Execute(null);
        vm.Variables.AddCommand.Execute(null);

        Assert.Equal(["Variable", "Variable2"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());
        Assert.Equal(["Variable", "Variable2"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(2, vm.HistoryEntries.Count(e => e.Name.StartsWith("Add variable")));
    }

    [UiFact]
    public void A_new_variable_has_a_value_the_user_can_see()
    {
        var vm = NewViewModel();
        vm.Variables.AddCommand.Execute(null);

        var row = vm.Variables.Rows[0];

        Assert.Equal("10", row.Expression.Text);
        Assert.Equal("10", row.ValueText);
        Assert.False(row.HasValueError);
    }

    [UiFact]
    public void Changing_an_expression_changes_the_document_and_shows_the_value()
    {
        var vm = NewViewModel();
        var row = Add(vm, "Wand", "2");

        row.Expression.Text = "1.5*2";

        Assert.Equal("1.5*2", vm.Document.Variables["Wand"]);
        Assert.Equal("3", vm.Variables.Rows[0].ValueText);
    }

    [UiFact]
    public async Task Changing_a_variable_updates_all_linked_objects_in_one_run()
    {
        var vm = NewViewModel();
        Add(vm, "Wand", "2");
        var first = await BoxWithWidth(vm, "Wand*10");
        var second = await BoxWithWidth(vm, "Wand*15");
        Assert.Equal(20, WidthOf(vm, first), 3);
        Assert.Equal(30, WidthOf(vm, second), 3);

        vm.Variables.Rows[0].Expression.Text = "3";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(30, WidthOf(vm, first), 3);
        Assert.Equal(45, WidthOf(vm, second), 3);
    }

    [UiFact]
    public void A_broken_expression_is_refused_and_the_old_one_stays()
    {
        var vm = NewViewModel();
        var row = Add(vm, "Wand", "2");

        row.Expression.Text = "2 +* 3";

        Assert.NotNull(row.Expression.Error);
        Assert.Equal("2", vm.Document.Variables["Wand"]);
    }

    [UiFact]
    public void An_expression_with_an_unknown_name_is_refused()
    {
        var vm = NewViewModel();
        var row = Add(vm, "Wand", "2");

        row.Expression.Text = "Fehlt*2";

        Assert.Contains("Fehlt", row.Expression.Error);
        Assert.Equal("2", vm.Document.Variables["Wand"]);
    }

    [UiFact]
    public void An_empty_expression_is_refused()
    {
        var vm = NewViewModel();
        var row = Add(vm, "Wand", "2");

        row.Expression.Text = "  ";

        Assert.NotNull(row.Expression.Error);
        Assert.Equal("2", vm.Document.Variables["Wand"]);
    }

    [UiFact]
    public void A_variable_may_use_one_further_down_the_table()
    {
        var vm = NewViewModel();
        Add(vm, "Aussen", "10");
        Add(vm, "Innen", "6");

        vm.Variables.Rows[0].Expression.Text = "Innen + 4";

        Assert.Null(vm.Variables.Rows[0].Expression.Error);
        Assert.Equal("10", vm.Variables.Rows[0].ValueText);
    }

    [UiFact]
    public void A_circle_is_refused()
    {
        var vm = NewViewModel();
        Add(vm, "A", "1");
        Add(vm, "B", "A + 1");

        vm.Variables.Rows[0].Expression.Text = "B + 1";

        Assert.Contains("ircular", vm.Variables.Rows[0].Expression.Error);
        Assert.Equal("1", vm.Document.Variables["A"]);
    }

    [UiFact]
    public void A_rename_changes_the_name_and_keeps_the_place_in_the_table()
    {
        var vm = NewViewModel();
        Add(vm, "A", "1");
        Add(vm, "B", "2");
        Add(vm, "C", "3");

        vm.Variables.Rows[1].Name.Text = "Breite";

        Assert.Equal(["A", "Breite", "C"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(["A", "Breite", "C"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());
    }

    [UiFact]
    public async Task A_rename_follows_the_references_in_variables_and_parameters_in_one_undo_step()
    {
        var vm = NewViewModel();
        Add(vm, "Wand", "2");
        Add(vm, "Dick", "Wand*2");
        var box = await BoxWithWidth(vm, "Wand*10 + Dick");
        var steps = vm.HistoryEntries.Count;

        vm.Variables.Rows[0].Name.Text = "Seite";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Seite*2", vm.Document.Variables["Dick"]);
        Assert.Equal("Seite*10 + Dick", box.Parameters["width"]);
        Assert.Equal(24, WidthOf(vm, box), 3); // nothing changed in the result
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Wand*2", vm.Document.Variables["Dick"]);
        Assert.Equal("Wand*10 + Dick", box.Parameters["width"]);
        Assert.Equal(["Wand", "Dick"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());
    }

    [UiFact]
    public void A_name_that_is_not_allowed_is_refused_and_the_old_one_stays()
    {
        foreach (var name in new[] { "", "1abc", "a b", "min", "Andere", "andere" })
        {
            var vm = NewViewModel();
            var row = Add(vm, "Wand", "2");
            Add(vm, "Andere", "5");

            row.Name.Text = name;

            Assert.NotNull(row.Name.Error);
            Assert.Equal(["Wand", "Andere"], vm.Document.Variables.Keys.ToArray());
        }
    }

    [UiFact]
    public void A_name_may_change_only_in_its_case()
    {
        var vm = NewViewModel();
        var row = Add(vm, "wand", "2");

        row.Name.Text = "Wand";

        Assert.Null(row.Name.Error);
        Assert.Equal(["Wand"], vm.Document.Variables.Keys.ToArray());
    }

    [UiFact]
    public void Delete_removes_the_variable_and_undo_puts_it_back_at_its_place()
    {
        var vm = NewViewModel();
        Add(vm, "A", "1");
        Add(vm, "B", "2");
        Add(vm, "C", "3");

        vm.Variables.Rows[1].DeleteCommand.Execute(null);
        Assert.Equal(["A", "C"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(["A", "C"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());

        vm.UndoCommand.Execute(null);
        Assert.Equal(["A", "B", "C"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(["A", "B", "C"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());
    }

    [UiFact]
    public async Task Deleting_a_variable_that_is_used_shows_an_error_on_the_object_and_undo_repairs_it()
    {
        var vm = NewViewModel();
        Add(vm, "Wand", "2");
        var box = await BoxWithWidth(vm, "Wand*10");

        vm.Variables.Rows[0].DeleteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.True(vm.Nodes.Single(n => n.Id == box.Id).HasError);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.False(vm.Nodes.Single(n => n.Id == box.Id).HasError);
    }

    [UiFact]
    public void A_variable_that_cannot_be_resolved_shows_its_error_as_the_value()
    {
        var vm = NewViewModel();
        vm.Document.Variables["A"] = "Fehlt + 1"; // as it could come from a hand-edited file
        vm.Document.NotifyChanged();

        var row = vm.Variables.Rows.Single();

        Assert.True(row.HasValueError);
        Assert.Contains("Fehlt", row.ValueText);
    }

    [UiFact]
    public void Undo_and_redo_of_an_edit_update_the_rows()
    {
        var vm = NewViewModel();
        var row = Add(vm, "Wand", "2");
        row.Expression.Text = "5";

        vm.UndoCommand.Execute(null);
        Assert.Equal("2", vm.Variables.Rows[0].Expression.Text);
        Assert.Equal("2", vm.Variables.Rows[0].ValueText);

        vm.RedoCommand.Execute(null);
        Assert.Equal("5", vm.Variables.Rows[0].Expression.Text);
    }

    [UiFact]
    public void A_new_project_empties_the_table()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs { UnsavedAnswer = UnsavedChoice.Discard });
        Add(vm, "Wand", "2");

        vm.NewCommand.Execute(null);

        Assert.Empty(vm.Variables.Rows);
        Assert.Empty(vm.Document.Variables);
    }

    [UiFact]
    public async Task A_field_that_uses_a_variable_is_marked_as_linked()
    {
        var vm = NewViewModel();
        Add(vm, "Wand", "2");
        await BoxWithWidth(vm, "Wand*10");
        var parameters = ShapeLibrary.Find("box")!.Parameters.ToList();
        var width = vm.Properties!.Parameters[parameters.FindIndex(p => p.Name == "width")];
        var depth = vm.Properties!.Parameters[parameters.FindIndex(p => p.Name == "depth")];

        Assert.True(width.IsLinked);
        Assert.Contains("Wand", width.LinkTip);
        Assert.False(depth.IsLinked);

        width.Text = "25";
        Assert.False(vm.Properties!.Parameters[parameters.FindIndex(p => p.Name == "width")].IsLinked);
    }

    [UiFact]
    public async Task The_variables_are_saved_with_the_project()
    {
        var vm = NewViewModel();
        Add(vm, "Wand", "2");
        Add(vm, "Dick", "Wand*2");
        await BoxWithWidth(vm, "Dick*5");

        var path = Path.Combine(Path.GetTempPath(), $"vars-{Guid.NewGuid():N}.fwp");
        Formwerk.IO.ProjectFile.SaveAtomic(path, vm.Document);
        var loaded = Formwerk.IO.ProjectFile.Load(path);
        File.Delete(path);

        Assert.Equal(["Wand", "Dick"], loaded.Variables.Keys.ToArray());
        Assert.Equal("Wand*2", loaded.Variables["Dick"]);
    }
}
