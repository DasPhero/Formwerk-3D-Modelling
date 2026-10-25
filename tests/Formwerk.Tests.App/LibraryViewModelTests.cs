using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The library panel: search, categories, favorites and inserting elements (F-LIB-02).</summary>
public sealed class LibraryViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-library-{Guid.NewGuid():N}");

    public LibraryViewModelTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private UserSettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private static MainViewModel NewViewModel(UserSettingsStore? store = null) =>
        new(new ManifoldKernel(), new FakeDialogs(), userSettings: store);

    private static LibraryEntryViewModel Entry(MainViewModel vm, string id) =>
        vm.LibraryEntries.Single(e => e.Item.Id == id);

    private static async Task Insert(MainViewModel vm, string id)
    {
        vm.AddLibraryItemCommand.Execute(Entry(vm, id));
        await vm.WaitForEvaluationAsync();
    }

    private static string[] Ids(MainViewModel vm) => [.. vm.LibraryEntries.Select(e => e.Item.Id)];

    private static IEnumerable<ViewportItem> SolidsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid);

    // ---- list, search, categories ----------------------------------------------------------------

    [UiFact]
    public void The_library_starts_with_every_element_and_the_category_choices()
    {
        var vm = NewViewModel();

        Assert.Equal(LibraryCatalog.Items.Select(i => i.Id).ToArray(), Ids(vm));
        Assert.Equal(2 + Enum.GetValues<LibraryCategory>().Length, vm.LibraryCategoryChoices.Count);
        Assert.Equal(0, vm.LibraryCategoryIndex);
        Assert.Equal("", vm.LibrarySearchText);
        Assert.False(vm.IsLibraryEmpty);
        Assert.All(vm.LibraryEntries, e => Assert.False(e.IsFavorite));
    }

    [UiFact]
    public void Typing_a_search_filters_the_list_and_clearing_it_brings_everything_back()
    {
        var vm = NewViewModel();

        vm.LibrarySearchText = "Schraube";
        var screws = Ids(vm);
        Assert.NotEmpty(screws);
        Assert.All(vm.LibraryEntries, e => Assert.True(e.Item.Name.Contains("Bolt") || e.Item.Name.Contains("Screw"), e.Item.Name));

        vm.LibrarySearchText = "screw";
        Assert.Equal(screws, Ids(vm));

        vm.LibrarySearchText = "";
        Assert.Equal(LibraryCatalog.Items.Count, vm.LibraryEntries.Count);
    }

    [UiFact]
    public void Nothing_found_is_shown_as_an_empty_list()
    {
        var vm = NewViewModel();

        vm.LibrarySearchText = "zzzz";

        Assert.Empty(vm.LibraryEntries);
        Assert.True(vm.IsLibraryEmpty);
    }

    [UiFact]
    public void A_category_choice_narrows_the_list_and_works_together_with_the_search()
    {
        var vm = NewViewModel();

        vm.LibraryCategoryIndex = 2 + (int)LibraryCategory.Fasteners;
        Assert.NotEmpty(vm.LibraryEntries);
        Assert.All(vm.LibraryEntries, e => Assert.Equal(LibraryCategory.Fasteners, e.Item.Category));

        vm.LibrarySearchText = "nut";
        Assert.NotEmpty(vm.LibraryEntries);
        Assert.All(vm.LibraryEntries, e => Assert.True(e.Item.Name.StartsWith("Hex Nut") || e.Item.Name.StartsWith("Square Nut"), e.Item.Name));

        vm.LibraryCategoryIndex = 2 + (int)LibraryCategory.Enclosures;
        Assert.Empty(vm.LibraryEntries);
    }

    // ---- size of the pictures --------------------------------------------------------------------

    [UiFact]
    public void The_size_of_the_pictures_changes_the_tile_width_and_is_remembered()
    {
        var store = Store();
        var vm = NewViewModel(store);
        Assert.Equal(44, vm.LibraryThumbnailSize);
        Assert.Equal(64, vm.LibraryTileWidth);

        vm.LibraryThumbnailSize = 90;

        Assert.Equal(110, vm.LibraryTileWidth);
        Assert.Equal(90, store.Load().LibraryThumbnailSize);
        Assert.Equal(90, NewViewModel(store).LibraryThumbnailSize); // a new program start
    }

    [UiFact]
    public void A_picture_size_outside_the_range_is_brought_back_into_it()
    {
        var vm = NewViewModel(Store());

        vm.LibraryThumbnailSize = 3;
        Assert.Equal(UserSettings.MinThumbnailSize, vm.LibraryThumbnailSize);

        vm.LibraryThumbnailSize = 999;
        Assert.Equal(UserSettings.MaxThumbnailSize, vm.LibraryThumbnailSize);
    }

    // ---- favorites -------------------------------------------------------------------------------

    [UiFact]
    public void A_favorite_is_remembered_between_sessions_and_listed_under_favorites()
    {
        var store = Store();
        var vm = NewViewModel(store);
        Entry(vm, "hex-nut-m3").IsFavorite = true;
        Entry(vm, "box").IsFavorite = true;

        Assert.Equal(["box", "hex-nut-m3"], store.Load().FavoriteLibraryItems.Ids.Order().ToArray());

        var again = NewViewModel(store); // a new program start
        Assert.True(Entry(again, "hex-nut-m3").IsFavorite);
        Assert.False(Entry(again, "washer-m3").IsFavorite);
        again.LibraryCategoryIndex = 1;
        Assert.Equal(["box", "hex-nut-m3"], Ids(again).Order().ToArray());

        Entry(again, "box").IsFavorite = false;
        Assert.Equal(["hex-nut-m3"], Ids(again)); // leaves the favorites list at once
        Assert.Equal(["hex-nut-m3"], store.Load().FavoriteLibraryItems.Ids);
    }

    [UiFact]
    public void Without_favorites_the_favorites_list_is_empty()
    {
        var vm = NewViewModel();

        vm.LibraryCategoryIndex = 1;

        Assert.Empty(vm.LibraryEntries);
        Assert.True(vm.IsLibraryEmpty);
    }

    [UiFact]
    public void Starring_an_element_that_is_not_shown_by_the_filter_keeps_the_filter()
    {
        var vm = NewViewModel();
        vm.LibrarySearchText = "washer";
        var washer = vm.LibraryEntries[0];

        washer.IsFavorite = true;

        Assert.All(vm.LibraryEntries, e => Assert.Contains("Washer", e.Item.Name));
        Assert.Equal("washer", vm.LibrarySearchText);
    }

    // ---- inserting -------------------------------------------------------------------------------

    [UiFact]
    public async Task A_basic_shape_is_inserted_like_the_old_shape_button()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;

        await Insert(vm, "box");

        var node = Assert.IsType<PrimitiveNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("Box", node.Name);
        Assert.Equal("box", node.GeneratorId);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([node.Id], vm.SelectedIds.ToArray());
    }

    [UiFact]
    public async Task A_ready_made_element_is_inserted_as_one_editable_tree_in_one_step()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;

        await Insert(vm, "hex-nut-m3");

        var nut = Assert.IsType<BooleanNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("Hex Nut M3", nut.Name);
        Assert.Equal(BooleanOperation.Difference, nut.Operation);
        Assert.Equal(2, nut.Children.Count);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([nut.Id], vm.SelectedIds.ToArray());
        Assert.NotEmpty(SolidsOf(vm, nut.Children[0].Id).Concat(SolidsOf(vm, nut.Id)));

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task The_same_element_twice_gets_its_own_name_and_its_own_place()
    {
        var vm = NewViewModel();

        await Insert(vm, "hex-nut-m3");
        await Insert(vm, "hex-nut-m3");

        var nodes = vm.Document.Root.Children;
        Assert.Equal(["Hex Nut M3", "Hex Nut M3 2"], nodes.Select(n => n.Name).ToArray());
        Assert.NotEqual(nodes[0].Transform.Position, nodes[1].Transform.Position);
        var a = vm.ViewportItems.Where(i => nodes[0].Descendants().Append(nodes[0]).Any(n => n.Id == i.NodeId)).Select(i => i.Mesh.ComputeBounds()).ToList();
        var b = vm.ViewportItems.Where(i => nodes[1].Descendants().Append(nodes[1]).Any(n => n.Id == i.NodeId)).Select(i => i.Mesh.ComputeBounds()).ToList();
        Assert.True(a.Max(x => x.Max.X) <= b.Min(x => x.Min.X) || b.Max(x => x.Max.X) <= a.Min(x => x.Min.X)); // side by side, not on top of each other
    }

    [UiFact]
    public async Task Every_element_of_the_library_can_be_inserted()
    {
        var dialogs = new FakeDialogs { TemplateParametersScript = _ => true }; // elements with parameters ask first: take the defaults
        var vm = new MainViewModel(new ManifoldKernel(), dialogs);

        foreach (var item in LibraryCatalog.Items) await Insert(vm, item.Id);

        Assert.Equal(LibraryCatalog.Items.Count, vm.Document.Root.Children.Count);
        Assert.Equal(LibraryCatalog.Items.Select(i => i.Name).ToArray(), vm.Document.Root.Children.Select(n => n.Name).ToArray());
        var solids = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).ToList();
        Assert.True(solids.Count >= LibraryCatalog.Items.Count);
        Assert.All(solids, s => Assert.True(s.Mesh.ComputeBounds().Min.Z > -0.01f));
    }

    [UiFact]
    public async Task A_ready_made_element_survives_saving_and_loading_the_project()
    {
        var vm = NewViewModel();
        await Insert(vm, "l-bracket");
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        var bracket = Assert.IsType<BooleanNode>(Assert.Single(back.Root.Children));
        Assert.Equal(3, bracket.Children.Count);
        Assert.IsType<MergeNode>(bracket.Children[0]);
        Assert.Equal(2, bracket.Children[0].Children.Count);
    }

    [UiFact]
    public async Task A_part_of_an_element_can_be_changed_afterwards()
    {
        var vm = NewViewModel();
        await Insert(vm, "open-box");
        var outside = (PrimitiveNode)vm.Document.Root.Children[0].Children[0];

        outside.Parameters["height"] = "40";
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        var item = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Solid);
        Assert.Equal(40, item.Mesh.ComputeBounds().Size.Z, 2);
    }
}
