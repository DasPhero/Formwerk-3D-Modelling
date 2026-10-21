using System.IO;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Managing templates in the program (F-TPL-04, F-TPL-07): properties, duplicate, delete, export, import.</summary>
public sealed class TemplateManagementViewModelTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"formwerk-tplman-vm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Library(string name = "library") => Path.Combine(_root, name);

    private (MainViewModel Vm, FakeDialogs Dialogs) NewViewModel(string library = "library")
    {
        var dialogs = new FakeDialogs();
        var vm = new MainViewModel(new ManifoldKernel(), dialogs, templates: new TemplateStore(Library(library)));
        return (vm, dialogs);
    }

    /// <summary>Saves a box as a template with the given name and category and returns its item in the list.</summary>
    private static async Task<TemplateItemViewModel> SaveBox(MainViewModel vm, FakeDialogs dialogs, string name, string category = "")
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([vm.Document.Root.Children[^1].Id]);
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = name;
            d.Category = category;
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        return vm.Templates.Single(t => t.Name == name);
    }

    // ---- properties ---------------------------------------------------------------------------------

    [UiFact]
    public async Task The_properties_dialog_shows_what_the_template_has_now_and_is_valid_for_the_own_name()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge", "Doors");
        SaveTemplateViewModel? seen = null;
        dialogs.SaveTemplateScript = d =>
        {
            seen = d;
            return false;
        };

        item.EditCommand.Execute(null);

        Assert.NotNull(seen);
        Assert.Equal("Hinge", seen!.Name);
        Assert.Equal("Doors", seen.Category);
        Assert.True(seen.IsValid); // its own name is no conflict
        Assert.Equal(Texts.Get("Dlg.TemplatePropertiesTitle"), seen.Title);
    }

    [UiFact]
    public async Task Renaming_and_describing_changes_the_list_and_keeps_the_objects()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge", "Doors");
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = "Door hinge";
            d.Description = "Opens and closes";
            d.Category = "Mechanics";
            d.TagsText = "door, metal";
            return true;
        };

        item.EditCommand.Execute(null);

        var changed = Assert.Single(vm.Templates);
        Assert.Equal("Door hinge", changed.Name);
        Assert.Equal("Mechanics", changed.Category);
        Assert.Equal("Opens and closes", changed.Description);
        Assert.Equal(new System.Numerics.Vector3(20, 20, 20), changed.Size);
        Assert.Contains("Door hinge", vm.StatusText);
    }

    [UiFact]
    public async Task Renaming_does_not_change_objects_that_were_inserted_before()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();
        var inserted = vm.Document.Root.Children[^1];
        Assert.Equal("Hinge", inserted.Name);
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = "Door hinge";
            return true;
        };

        item.EditCommand.Execute(null);

        Assert.Equal("Hinge", inserted.Name);
        Assert.Equal("Door hinge", Assert.Single(vm.Templates).Name);
    }

    [UiFact]
    public async Task A_name_that_another_template_has_cannot_be_confirmed_and_a_suggestion_is_offered()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Hinge");
        var latch = await SaveBox(vm, dialogs, "Latch");
        SaveTemplateViewModel? seen = null;
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = "Hinge";
            seen = d;
            return false;
        };

        latch.EditCommand.Execute(null);

        Assert.False(seen!.IsValid);
        Assert.Equal("Hinge (2)", seen.Suggestion);
        Assert.Equal(["Hinge", "Latch"], vm.Templates.Select(t => t.Name).ToArray());
    }

    [UiFact]
    public async Task Cancelling_the_properties_changes_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = "Something else";
            return false;
        };

        item.EditCommand.Execute(null);

        Assert.Equal("Hinge", Assert.Single(vm.Templates).Name);
    }

    [UiFact]
    public async Task Another_category_is_possible_and_the_list_follows()
    {
        var (vm, dialogs) = NewViewModel();
        await SaveBox(vm, dialogs, "Alpha", "Zeta");
        var beta = await SaveBox(vm, dialogs, "Beta", "Alpha");
        dialogs.SaveTemplateScript = d =>
        {
            d.Category = "Zzz";
            return true;
        };

        beta.EditCommand.Execute(null);

        Assert.Equal(["Alpha", "Beta"], vm.Templates.Select(t => t.Name).ToArray()); // Zeta before Zzz
        Assert.Equal(["Zeta", "Zzz"], vm.Templates.Select(t => t.Category).ToArray());
    }

    // ---- duplicate ----------------------------------------------------------------------------------

    [UiFact]
    public async Task A_duplicate_appears_with_the_next_free_name()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge", "Doors");

        item.DuplicateCommand.Execute(null);

        Assert.Equal(["Hinge", "Hinge (2)"], vm.Templates.Select(t => t.Name).ToArray());
        Assert.All(vm.Templates, t => Assert.Equal("Doors", t.Category));
        Assert.Contains("Hinge (2)", vm.StatusText);
    }

    // ---- delete -------------------------------------------------------------------------------------

    [UiFact]
    public async Task Deleting_asks_first_and_does_nothing_when_the_answer_is_no()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        dialogs.ConfirmAnswer = false;

        item.DeleteCommand.Execute(null);

        Assert.Single(dialogs.ConfirmPrompts);
        Assert.Contains("Hinge", dialogs.ConfirmPrompts[0]);
        Assert.Single(vm.Templates);
        Assert.True(File.Exists(item.Path));
    }

    [UiFact]
    public async Task Deleting_after_yes_removes_the_template_from_the_list_and_the_folder()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        var keep = await SaveBox(vm, dialogs, "Latch");
        dialogs.ConfirmAnswer = true;

        item.DeleteCommand.Execute(null);

        Assert.Equal(["Latch"], vm.Templates.Select(t => t.Name).ToArray());
        Assert.False(File.Exists(item.Path));
        Assert.True(File.Exists(keep.Path));
    }

    [UiFact]
    public async Task Objects_that_were_inserted_stay_when_their_template_is_deleted()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();
        var count = vm.Document.Root.Children.Count;
        dialogs.ConfirmAnswer = true;

        item.DeleteCommand.Execute(null);

        Assert.Equal(count, vm.Document.Root.Children.Count);
        Assert.Empty(vm.Templates);
    }

    // ---- export and import --------------------------------------------------------------------------

    [UiFact]
    public async Task Exporting_writes_a_file_that_another_library_can_import_and_insert()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge", "Doors");
        Directory.CreateDirectory(Library("usb"));
        dialogs.SavePath = Path.Combine(Library("usb"), "Hinge.fwt");

        item.ExportCommand.Execute(null);

        Assert.True(File.Exists(dialogs.SavePath));
        var (other, otherDialogs) = NewViewModel("other machine");
        otherDialogs.OpenPath = dialogs.SavePath;
        other.ImportTemplateCommand.Execute(null);

        var imported = Assert.Single(other.Templates);
        Assert.Equal("Hinge", imported.Name);
        Assert.Equal("Doors", imported.Category);

        other.InsertTemplateCommand.Execute(imported);
        await other.WaitForEvaluationAsync();
        Assert.Single(other.Document.Root.Children);
        Assert.Equal("Hinge", other.Document.Root.Children[0].Name);
    }

    [UiFact]
    public async Task Cancelling_the_export_dialog_writes_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        dialogs.SavePath = null;

        item.ExportCommand.Execute(null);

        Assert.False(Directory.Exists(Library("usb")));
    }

    [UiFact]
    public async Task Importing_a_template_with_a_name_that_is_taken_renames_it_and_says_so()
    {
        var (vm, dialogs) = NewViewModel();
        var item = await SaveBox(vm, dialogs, "Hinge");
        Directory.CreateDirectory(Library("usb"));
        dialogs.SavePath = Path.Combine(Library("usb"), "Hinge.fwt");
        item.ExportCommand.Execute(null);
        dialogs.OpenPath = dialogs.SavePath;

        vm.ImportTemplateCommand.Execute(null);

        Assert.Equal(["Hinge", "Hinge (2)"], vm.Templates.Select(t => t.Name).ToArray());
        Assert.Contains("Hinge (2)", vm.StatusText);
    }

    [UiFact]
    public void A_file_that_is_no_template_is_reported_and_nothing_is_added()
    {
        var (vm, dialogs) = NewViewModel();
        Directory.CreateDirectory(_root);
        var junk = Path.Combine(_root, "junk.fwt");
        File.WriteAllText(junk, "this is not a template");
        dialogs.OpenPath = junk;

        vm.ImportTemplateCommand.Execute(null);

        Assert.Empty(vm.Templates);
        Assert.Single(dialogs.Errors);
    }

    [UiFact]
    public void Cancelling_the_import_dialog_does_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        dialogs.OpenPath = null;

        vm.ImportTemplateCommand.Execute(null);

        Assert.Empty(vm.Templates);
        Assert.Empty(dialogs.Errors);
    }

    [UiFact]
    public void Without_a_template_folder_importing_is_not_possible()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());

        Assert.False(vm.ImportTemplateCommand.CanExecute(null));
    }
}
