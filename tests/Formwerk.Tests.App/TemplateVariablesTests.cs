using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Templates carry the project variables that they use, and bring them along when they are inserted (F-PAR-03, F-TPL-01, F-TPL-02).</summary>
public sealed class TemplateVariablesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-tpl-vars-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private MainViewModel NewViewModel(string? templateName = null) => new(
        new ManifoldKernel(),
        new FakeDialogs { SaveTemplateScript = templateName is null ? null : (dialog => { dialog.Name = templateName; return true; }) },
        templates: new TemplateStore(_folder));

    private static void Variable(MainViewModel vm, string name, string expression)
    {
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[^1].Name.Text = name;
        vm.Variables.Rows[^1].Expression.Text = expression;
    }

    /// <summary>A box whose width is the given expression, selected.</summary>
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

    private static async Task<FormwerkDocument> SaveAndRead(MainViewModel vm)
    {
        vm.SaveTemplateCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var path = Assert.Single(vm.Templates).Path;
        return TemplateFile.Load(path, new MeshStore());
    }

    private static double WidthOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Max(i => (double)i.Mesh.ComputeBounds().Size.X);

    [UiFact]
    public async Task A_template_takes_the_variables_it_uses_and_the_ones_those_need_but_no_others()
    {
        var vm = NewViewModel("Plate");
        Variable(vm, "Wand", "2");
        Variable(vm, "Dick", "Wand*2");
        Variable(vm, "Frei", "99");
        await BoxWithWidth(vm, "Dick*5");

        var template = await SaveAndRead(vm);

        Assert.Equal(["Wand", "Dick"], template.Variables.Keys.ToArray());
        Assert.Equal("Wand*2", template.Variables["Dick"]);
    }

    [UiFact]
    public async Task A_template_that_uses_no_variable_stores_none()
    {
        var vm = NewViewModel("Plain");
        Variable(vm, "Wand", "2");
        await BoxWithWidth(vm, "30");

        var template = await SaveAndRead(vm);

        Assert.Empty(template.Variables);
    }

    [UiFact]
    public async Task A_name_that_is_not_a_variable_of_the_project_is_not_stored()
    {
        var vm = NewViewModel("Odd");
        await BoxWithWidth(vm, "30");
        ((PrimitiveNode)vm.Document.Root.Children[0]).Parameters["depth"] = "Fehlt*2"; // a field that cannot be resolved

        var template = await SaveAndRead(vm);

        Assert.Empty(template.Variables);
    }

    [UiFact]
    public async Task Inserting_into_a_project_without_the_variables_brings_them_along_in_one_undo_step()
    {
        var source = NewViewModel("Plate");
        Variable(source, "Wand", "2");
        Variable(source, "Dick", "Wand*2");
        await BoxWithWidth(source, "Dick*5");
        await SaveAndRead(source);

        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["Wand", "Dick"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(["Wand", "Dick"], vm.Variables.Rows.Select(r => r.Name.Text).ToArray());
        var inserted = vm.Document.Root.Children.Single();
        Assert.Equal(20, WidthOf(vm, inserted), 3); // Dick = 4, width = 20
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.False(vm.Nodes.Single().HasError);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);
        Assert.Empty(vm.Document.Variables);
    }

    [UiFact]
    public async Task A_variable_that_the_project_has_already_keeps_the_value_of_the_project_and_says_so()
    {
        var source = NewViewModel("Plate");
        Variable(source, "Wand", "2");
        await BoxWithWidth(source, "Wand*10");
        await SaveAndRead(source);

        var vm = NewViewModel();
        Variable(vm, "wand", "3"); // other case, other value
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["wand"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal("3", vm.Document.Variables["wand"]);
        Assert.Equal(30, WidthOf(vm, vm.Document.Root.Children.Single()), 3);
        Assert.Contains("Wand", vm.StatusText);
    }

    [UiFact]
    public async Task A_variable_with_the_same_formula_causes_no_message()
    {
        var source = NewViewModel("Plate");
        Variable(source, "Wand", "2");
        await BoxWithWidth(source, "Wand*10");
        await SaveAndRead(source);

        var vm = NewViewModel();
        Variable(vm, "Wand", "2");
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        Assert.DoesNotContain("another value", vm.StatusText);
        Assert.Equal(["Wand"], vm.Document.Variables.Keys.ToArray());
    }

    [UiFact]
    public async Task New_variables_come_after_the_ones_of_the_project()
    {
        var source = NewViewModel("Plate");
        Variable(source, "Wand", "2");
        await BoxWithWidth(source, "Wand*10");
        await SaveAndRead(source);

        var vm = NewViewModel();
        Variable(vm, "Zoll", "25.4");
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["Zoll", "Wand"], vm.Document.Variables.Keys.ToArray());
    }

    [UiFact]
    public async Task A_template_without_variables_does_not_touch_the_table()
    {
        var source = NewViewModel("Plain");
        await BoxWithWidth(source, "30");
        await SaveAndRead(source);

        var vm = NewViewModel();
        Variable(vm, "Zoll", "25.4");
        var before = vm.HistoryEntries.Count;
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["Zoll"], vm.Document.Variables.Keys.ToArray());
        Assert.Equal(before + 1, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task The_inserted_fields_are_marked_as_linked()
    {
        var source = NewViewModel("Plate");
        Variable(source, "Wand", "2");
        await BoxWithWidth(source, "Wand*10");
        await SaveAndRead(source);

        var vm = NewViewModel();
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        var index = ShapeLibrary.Find("box")!.Parameters.ToList().FindIndex(p => p.Name == "width");
        Assert.True(vm.Properties!.Parameters[index].IsLinked);
    }
}
