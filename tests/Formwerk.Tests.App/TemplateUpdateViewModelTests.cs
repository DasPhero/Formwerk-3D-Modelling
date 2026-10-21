using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Edit > Update Template: the selection becomes a new version of an existing template (F-TPL-05).</summary>
public sealed class TemplateUpdateViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-tplupd-vm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private (MainViewModel Vm, FakeDialogs Dialogs) NewViewModel()
    {
        var dialogs = new FakeDialogs();
        return (new MainViewModel(new ManifoldKernel(), dialogs, templates: new TemplateStore(_folder)), dialogs);
    }

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = vm.Document.Root.Children[^1];
        vm.SelectOnly([node.Id]);
        return node;
    }

    /// <summary>Saves a box as template <paramref name="name"/> (with description, category and tags) and returns its item.</summary>
    private static async Task<TemplateItemViewModel> SaveBox(MainViewModel vm, FakeDialogs dialogs, string name)
    {
        await Add(vm, "box");
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = name;
            d.Description = "Opens and closes";
            d.Category = "Doors";
            d.TagsText = "door, metal";
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        return vm.Templates.Single(t => t.Name == name);
    }

    private static PrimitiveNode First(string path) => (PrimitiveNode)TemplateFile.Load(path).Root.Children[0];

    // ---- the command ---------------------------------------------------------------------------------

    [UiFact]
    public async Task Updating_needs_a_selection_and_a_template_folder()
    {
        var (vm, _) = NewViewModel();
        Assert.False(vm.UpdateTemplateCommand.CanExecute(null)); // nothing selected

        await Add(vm, "box");
        Assert.True(vm.UpdateTemplateCommand.CanExecute(null));

        var withoutFolder = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        await Add(withoutFolder, "box");
        Assert.False(withoutFolder.UpdateTemplateCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Without_any_template_the_user_is_told_and_no_dialog_opens()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var opened = false;
        dialogs.UpdateTemplateScript = _ =>
        {
            opened = true;
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.False(opened);
        Assert.Contains("Save as Template", vm.StatusText);
    }

    // ---- the dialog ----------------------------------------------------------------------------------

    [UiFact]
    public async Task The_dialog_lists_the_templates_and_preselects_the_one_named_like_the_selected_object()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Latch");
        var hinge = await SaveBox(vm, dialogs, "Hinge");
        vm.InsertTemplateCommand.Execute(hinge);
        await vm.WaitForEvaluationAsync(); // the inserted object is named "Hinge" and selected
        UpdateTemplateViewModel? seen = null;
        dialogs.UpdateTemplateScript = d =>
        {
            seen = d;
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.NotNull(seen);
        Assert.Equal(["Hinge", "Latch"], seen!.Templates.Select(t => t.Name).ToArray());
        Assert.Equal("Hinge", seen.Selected?.Name);
        Assert.True(seen.CanConfirm);
    }

    [UiFact]
    public async Task A_numbered_copy_of_the_object_name_still_finds_its_template()
    {
        var (vm, dialogs) = NewViewModel();
        var hinge = await SaveBox(vm, dialogs, "Hinge");
        vm.InsertTemplateCommand.Execute(hinge);
        vm.InsertTemplateCommand.Execute(hinge); // the second one is named "Hinge 2"
        await vm.WaitForEvaluationAsync();
        Assert.Equal("Hinge 2", vm.Document.Root.Children[^1].Name);
        UpdateTemplateViewModel? seen = null;
        dialogs.UpdateTemplateScript = d =>
        {
            seen = d;
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Equal("Hinge", seen!.Selected?.Name);
    }

    [UiFact]
    public async Task Another_name_preselects_nothing_and_confirming_is_not_possible()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder"); // named "Cylinder"
        UpdateTemplateViewModel? seen = null;
        dialogs.UpdateTemplateScript = d =>
        {
            seen = d;
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Null(seen!.Selected);
        Assert.False(seen.CanConfirm);
    }

    [UiFact]
    public async Task The_dialog_says_which_version_the_template_gets()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        UpdateTemplateViewModel? seen = null;
        dialogs.UpdateTemplateScript = d =>
        {
            seen = d;
            d.Selected = d.Templates[0];
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Contains("Hinge", seen!.Summary);
        Assert.Contains("1", seen.Summary);
        Assert.Contains("2", seen.Summary);
    }

    // ---- updating ------------------------------------------------------------------------------------

    [UiFact]
    public async Task Confirming_replaces_the_objects_of_the_template_and_raises_the_version()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        var updated = Assert.Single(vm.Templates);
        Assert.Equal(item.Path, updated.Path);
        Assert.Equal(2, updated.Info.Version);
        Assert.Equal("cylinder", First(updated.Path).GeneratorId);
        Assert.Contains("Hinge", vm.StatusText);
        Assert.Contains("2", vm.StatusText);
    }

    [UiFact]
    public async Task Name_description_category_and_tags_stay()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        var info = Assert.Single(vm.Templates).Info;
        Assert.Equal("Hinge", info.Name);
        Assert.Equal("Opens and closes", info.Description);
        Assert.Equal("Doors", info.Category);
        Assert.Equal(["door", "metal"], info.Tags);
    }

    [UiFact]
    public async Task The_size_in_the_list_follows_the_new_objects()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge"); // a box, 20 x 20 x 20
        var cylinder = await Add(vm, "cylinder");
        vm.SelectOnly([cylinder.Id]);
        vm.Properties!.Parameters[0].Text = "50"; // the diameter
        await vm.WaitForEvaluationAsync();
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        var size = Assert.Single(vm.Templates).Size;
        Assert.Equal(50, size.X, 0.5);
        Assert.Equal(50, size.Y, 0.5);
    }

    [UiFact]
    public async Task Objects_inserted_before_stay_as_they_were_and_new_insertions_use_the_new_version()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();
        var inserted = Assert.IsType<PrimitiveNode>(vm.Document.Root.Children[^1]);
        Assert.Equal("box", inserted.GeneratorId);
        await Add(vm, "cylinder");
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);
        Assert.Equal("box", inserted.GeneratorId);

        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();
        Assert.Equal("cylinder", Assert.IsType<PrimitiveNode>(vm.Document.Root.Children[^1]).GeneratorId);
    }

    [UiFact]
    public async Task Cancelling_changes_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return false;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Equal(1, Assert.Single(vm.Templates).Info.Version);
        Assert.Equal("box", First(item.Path).GeneratorId);
    }

    [UiFact]
    public async Task Confirming_without_a_choice_changes_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = null;
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Equal(1, Assert.Single(vm.Templates).Info.Version);
        Assert.Equal("box", First(item.Path).GeneratorId);
    }

    [UiFact]
    public async Task The_template_is_not_a_change_of_the_project()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        await Add(vm, "cylinder");
        var steps = vm.HistoryEntries.Count;
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task The_new_version_takes_the_variables_it_uses_along()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[^1].Name.Text = "wall";
        vm.Variables.Rows[^1].Expression.Text = "3";
        var box = await Add(vm, "box");
        vm.SelectOnly([box.Id]);
        vm.Properties!.Parameters[0].Text = "wall * 4";
        await vm.WaitForEvaluationAsync();
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        var template = TemplateFile.Load(Assert.Single(vm.Templates).Path);
        Assert.Equal("3", template.Variables["wall"]);
    }

    [UiFact]
    public async Task Several_objects_become_the_new_version_as_they_are()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        var a = await Add(vm, "cylinder");
        var b = await Add(vm, "sphere");
        vm.SelectOnly([a.Id, b.Id]);
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        var template = TemplateFile.Load(Assert.Single(vm.Templates).Path);
        Assert.Equal(2, template.Root.Children.Count);
    }
}
