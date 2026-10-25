using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Library;

namespace Formwerk.Tests.App;

/// <summary>Sub folders of the built-in categories and own folders of the user in the library panel (F-LIB-02).</summary>
public sealed class LibraryFolderTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-libfolders-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public LibraryFolderTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private UserSettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private MainViewModel NewViewModel(UserSettingsStore? store = null) => new(new ManifoldKernel(), _dialogs, userSettings: store);

    private static void Show(MainViewModel vm, string label) =>
        vm.LibraryCategoryIndex = vm.LibraryCategoryChoices.Single(c => c.Label.Trim() == label).Index;

    private static string[] Ids(MainViewModel vm) => [.. vm.LibraryEntries.Select(e => e.Item.Id)];

    private static LibraryEntryViewModel Entry(MainViewModel vm, string id) => vm.LibraryEntries.First(e => e.Item.Id == id);

    private static void NewFolder(MainViewModel vm, FakeDialogs dialogs, string name)
    {
        dialogs.TextAnswer = name;
        vm.NewLibraryFolderCommand.Execute(null);
    }

    // ---- built-in sub folders --------------------------------------------------------------------

    [UiFact]
    public void The_folder_choice_lists_the_categories_with_their_sub_folders_indented()
    {
        var vm = NewViewModel();

        var labels = vm.LibraryCategoryChoices.Select(c => c.Label).ToList();

        Assert.Equal("All", labels[0]);
        Assert.Equal("Favorites", labels[1]);
        var fasteners = labels.IndexOf("Fasteners");
        Assert.True(fasteners > 1);
        Assert.Equal(["    Nuts", "    Bolts and Screws", "    Washers", "    Spacers and Posts", "    Pins and Rods"], labels.Skip(fasteners + 1).Take(5).ToArray());
        Assert.Contains("My Templates", labels);
        Assert.Equal(vm.LibraryCategoryChoices.Count, vm.LibraryCategoryChoices.Select(c => c.Index).Distinct().Count());
    }

    [UiFact]
    public void A_sub_folder_shows_only_its_elements_in_order_and_a_category_shows_all_of_its_sub_folders()
    {
        var vm = NewViewModel();

        Show(vm, "Nuts");
        Assert.Equal(["hex-nut-m3", "hex-nut-m4", "hex-nut-m5", "hex-nut-m6", "square-nut-m3", "square-nut-m4"], Ids(vm));

        Show(vm, "Fasteners");
        Assert.All(vm.LibraryEntries, e => Assert.Equal(LibraryCategory.Fasteners, e.Item.Category));
        Assert.True(Ids(vm).ToList().IndexOf("hex-nut-m3") < Ids(vm).ToList().IndexOf("washer-m3")); // nuts before washers

        vm.LibrarySearchText = "m4"; // the search works inside a sub folder, too
        Show(vm, "Washers");
        Assert.Equal(["washer-m4"], Ids(vm));
    }

    [UiFact]
    public void The_whole_library_is_sorted_by_category_sub_folder_and_name()
    {
        var vm = NewViewModel();

        var ids = Ids(vm);

        Assert.Equal(LibraryFolders.Order(LibraryCatalog.Items).Select(i => i.Id).ToArray(), ids);
    }

    // ---- own folders -----------------------------------------------------------------------------

    [UiFact]
    public void A_new_folder_appears_in_the_choice_is_shown_and_remembered()
    {
        var store = Store();
        var vm = NewViewModel(store);

        NewFolder(vm, _dialogs, "Printer parts");

        Assert.Equal(["Printer parts"], vm.UserLibraryFolderNames);
        Assert.Equal("Printer parts", vm.LibraryCategoryChoices.Single(c => c.Index == vm.LibraryCategoryIndex).Label);
        Assert.Empty(vm.LibraryEntries);
        Assert.Equal(["Printer parts"], store.Load().LibraryFolders.Folders.Select(f => f.Name).ToArray());
        Assert.Equal(["Printer parts"], NewViewModel(store).UserLibraryFolderNames); // a new program start
    }

    [UiFact]
    public void An_element_is_put_into_a_folder_from_its_menu_and_taken_out_again()
    {
        var store = Store();
        var vm = NewViewModel(store);
        NewFolder(vm, _dialogs, "Mine");
        vm.LibraryCategoryIndex = 0;

        var membership = Entry(vm, "hex-nut-m3").FolderMemberships.Single();
        Assert.Equal("Mine", membership.Name);
        Assert.False(membership.IsMember);
        membership.IsMember = true;
        Entry(vm, "cable-clip").FolderMemberships.Single().IsMember = true;

        Show(vm, "Mine");
        Assert.Equal(["cable-clip", "hex-nut-m3"], Ids(vm).Order().ToArray());
        Assert.Equal(["hex-nut-m3", "cable-clip"], store.Load().LibraryFolders.Folders.Single().Items.Ids);
        Assert.True(Entry(NewViewModel(store), "hex-nut-m3").FolderMemberships.Single().IsMember);

        Entry(vm, "hex-nut-m3").FolderMemberships.Single().IsMember = false; // leaves the folder at once
        Assert.Equal(["cable-clip"], Ids(vm));
        Show(vm, "Nuts");
        Assert.Contains("hex-nut-m3", Ids(vm)); // the original stays where it is
    }

    [UiFact]
    public void An_element_can_be_in_several_folders_and_every_entry_offers_every_folder()
    {
        var vm = NewViewModel(Store());
        NewFolder(vm, _dialogs, "A");
        NewFolder(vm, _dialogs, "B");
        vm.LibraryCategoryIndex = 0;

        var memberships = Entry(vm, "box").FolderMemberships;
        Assert.Equal(["A", "B"], memberships.Select(m => m.Name).ToArray());
        memberships[0].IsMember = true;
        memberships[1].IsMember = true;

        Show(vm, "A");
        Assert.Equal(["box"], Ids(vm));
        Show(vm, "B");
        Assert.Equal(["box"], Ids(vm));
    }

    [UiFact]
    public void A_folder_can_be_renamed_and_keeps_its_elements()
    {
        var store = Store();
        var vm = NewViewModel(store);
        NewFolder(vm, _dialogs, "Old");
        vm.LibraryCategoryIndex = 0;
        Entry(vm, "box").FolderMemberships.Single().IsMember = true;
        Show(vm, "Old");

        _dialogs.TextAnswer = "New name";
        vm.RenameLibraryFolderCommand.Execute(null);

        Assert.Equal(["New name"], vm.UserLibraryFolderNames);
        Assert.Equal("New name", vm.LibraryCategoryChoices.Single(c => c.Index == vm.LibraryCategoryIndex).Label);
        Assert.Equal(["box"], Ids(vm));
        Assert.Equal("Old", _dialogs.TextPrompts.Last().Initial);
        Assert.Equal(["box"], store.Load().LibraryFolders.Folders.Single(f => f.Name == "New name").Items.Ids);
    }

    [UiFact]
    public void Deleting_a_folder_asks_first_and_keeps_the_elements_themselves()
    {
        var store = Store();
        var vm = NewViewModel(store);
        NewFolder(vm, _dialogs, "Gone");
        vm.LibraryCategoryIndex = 0;
        Entry(vm, "box").FolderMemberships.Single().IsMember = true;
        Show(vm, "Gone");

        _dialogs.ConfirmAnswer = false;
        vm.DeleteLibraryFolderCommand.Execute(null);
        Assert.Equal(["Gone"], vm.UserLibraryFolderNames);
        Assert.Single(_dialogs.ConfirmPrompts);

        _dialogs.ConfirmAnswer = true;
        vm.DeleteLibraryFolderCommand.Execute(null);

        Assert.Empty(vm.UserLibraryFolderNames);
        Assert.Equal(0, vm.LibraryCategoryIndex); // back to "All"
        Assert.Contains("box", Ids(vm));
        Assert.Empty(store.Load().LibraryFolders.Folders);
        Assert.Empty(Entry(vm, "box").FolderMemberships);
    }

    [UiFact]
    public void Rename_and_delete_are_only_possible_while_an_own_folder_is_shown()
    {
        var vm = NewViewModel(Store());
        Assert.False(vm.RenameLibraryFolderCommand.CanExecute(null));
        Assert.False(vm.DeleteLibraryFolderCommand.CanExecute(null));

        NewFolder(vm, _dialogs, "X");
        Assert.True(vm.RenameLibraryFolderCommand.CanExecute(null));
        Assert.True(vm.DeleteLibraryFolderCommand.CanExecute(null));

        Show(vm, "Nuts");
        Assert.False(vm.DeleteLibraryFolderCommand.CanExecute(null));
    }

    [UiFact]
    public void A_cancelled_or_unusable_name_makes_no_folder()
    {
        var vm = NewViewModel(Store());
        NewFolder(vm, _dialogs, "Taken");

        _dialogs.TextAnswer = null; // cancelled
        vm.NewLibraryFolderCommand.Execute(null);
        NewFolder(vm, _dialogs, "   "); // blank
        NewFolder(vm, _dialogs, "taken"); // the same name, any case
        NewFolder(vm, _dialogs, "Fasteners"); // a name of the library itself would be confusing

        Assert.Equal(["Taken"], vm.UserLibraryFolderNames);
        Assert.Equal(3, _dialogs.Errors.Count);
    }

    [UiFact]
    public async Task A_template_can_be_put_into_a_folder_too()
    {
        var templates = new TemplateStore(Path.Combine(_folder, "templates"));
        var vm = new MainViewModel(new ManifoldKernel(), _dialogs, userSettings: Store(), templates: templates, templatePreview: new NoPreview());
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([vm.Document.Root.Children[0].Id]);
        _dialogs.SaveTemplateScript = dialog =>
        {
            dialog.Name = "Round peg";
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        NewFolder(vm, _dialogs, "Mine");
        vm.LibraryCategoryIndex = 0;

        var entry = vm.LibraryEntries.OfType<TemplateEntryViewModel>().Single();
        entry.FolderMemberships.Single().IsMember = true;

        Show(vm, "Mine");
        Assert.Equal([entry.Item.Id], Ids(vm));
        Show(vm, "My Templates");
        Assert.Equal([entry.Item.Id], Ids(vm)); // it stays in its own place
    }

    private sealed class NoPreview : ITemplatePreview
    {
        public byte[]? Render(IReadOnlyList<Formwerk.Viewport.ViewportItem> items) => null;
    }
}
