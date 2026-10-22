using System.IO;
using System.Numerics;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>"My Templates" as part of the library: tiles, category, search, favorites, insert and drop (F-LIB-03).</summary>
public sealed class LibraryTemplateTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-libtpl-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private sealed class FakePreview : ITemplatePreview
    {
        public int Calls { get; private set; }

        public byte[]? Render(IReadOnlyList<ViewportItem> items)
        {
            Calls++;
            return [1, 2, 3];
        }
    }

    private static readonly int TemplatesChoice = 2 + (int)LibraryCategory.MyTemplates;

    private TemplateStore Templates() => new(Path.Combine(_folder, "templates"));

    private UserSettingsStore Settings() => new(Path.Combine(_folder, "settings.json"));

    private MainViewModel NewViewModel(FakePreview? preview = null) =>
        new(new ManifoldKernel(), _dialogs, userSettings: Settings(), templates: Templates(), templatePreview: preview ?? new FakePreview());

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Children[^1];
    }

    /// <summary>Saves a template of a new cylinder or box under the given name.</summary>
    private async Task<MainViewModel> WithTemplate(string name, string shape = "cylinder", string category = "", string description = "", string tags = "")
    {
        var vm = NewViewModel();
        await SaveTemplate(vm, name, shape, category, description, tags);
        return vm;
    }

    private async Task SaveTemplate(MainViewModel vm, string name, string shape = "cylinder", string category = "", string description = "", string tags = "")
    {
        var node = await Add(vm, shape);
        vm.SelectOnly([node.Id]);
        _dialogs.SaveTemplateScript = dialog =>
        {
            dialog.Name = name;
            dialog.Category = category;
            dialog.Description = description;
            dialog.TagsText = tags;
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        vm.DeleteCommand.Execute(null); // the scene is empty again, only the template is kept
        await vm.WaitForEvaluationAsync();
    }

    private static TemplateEntryViewModel TemplateTile(MainViewModel vm, string name) =>
        vm.LibraryEntries.OfType<TemplateEntryViewModel>().Single(e => e.Name == name);

    private static FacePick Plate(float x, float y) => new(Guid.Empty, new Vector3(x, y, 0), Vector3.UnitZ);

    private static Bounds BoundsOf(MainViewModel vm, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    // ---- the list --------------------------------------------------------------------------------

    [UiFact]
    public void Without_templates_the_my_templates_choice_is_empty_and_shows_the_old_hint()
    {
        var vm = NewViewModel();

        vm.LibraryCategoryIndex = TemplatesChoice;

        Assert.Empty(vm.LibraryEntries);
        Assert.True(vm.IsLibraryEmpty);
        Assert.Equal(Texts.Get("Panel.Templates.Empty"), vm.LibraryEmptyText);

        vm.LibrarySearchText = "zzz";
        Assert.Equal(Texts.Get("Library.Empty"), vm.LibraryEmptyText);
    }

    [UiFact]
    public async Task A_saved_template_is_a_tile_of_the_library_with_its_picture()
    {
        var vm = await WithTemplate("Round peg", category: "Mechanics", description: "A peg for holes");

        var tile = TemplateTile(vm, "Round peg");

        Assert.Equal(LibraryCatalog.Items.Count + 1, vm.LibraryEntries.Count); // "All" shows the elements and the templates
        Assert.Equal(LibraryCategory.MyTemplates, tile.Item.Category);
        Assert.Equal([1, 2, 3], tile.Thumbnail);
        Assert.Contains("A peg for holes", tile.Tip);
        Assert.False(tile.IsFavorite);
        Assert.Same(vm.Templates.Single(), tile.Template);
        Assert.StartsWith("template:", tile.Item.Id);
    }

    [UiFact]
    public async Task The_my_templates_choice_lists_only_templates()
    {
        var vm = await WithTemplate("Round peg");
        await SaveTemplate(vm, "Plain block", "box");

        vm.LibraryCategoryIndex = TemplatesChoice;

        Assert.Equal(["Plain block", "Round peg"], vm.LibraryEntries.Select(e => e.Name).Order().ToArray());
        Assert.All(vm.LibraryEntries, e => Assert.IsType<TemplateEntryViewModel>(e));

        vm.LibraryCategoryIndex = 2 + (int)LibraryCategory.Fasteners;
        Assert.DoesNotContain(vm.LibraryEntries, e => e is TemplateEntryViewModel);
    }

    [UiFact]
    public async Task The_search_finds_templates_by_name_category_tag_and_description()
    {
        var vm = await WithTemplate("Round peg", category: "Garden", description: "Holds the string", tags: "hook, outdoor");

        foreach (var word in new[] { "peg", "garden", "outdoor", "string", "templates" })
        {
            vm.LibrarySearchText = word;
            Assert.Contains(vm.LibraryEntries, e => e.Name == "Round peg");
        }

        vm.LibrarySearchText = "peg nut"; // all words have to match
        Assert.Empty(vm.LibraryEntries);
    }

    [UiFact]
    public async Task A_template_can_be_a_favorite_and_it_is_remembered()
    {
        var vm = await WithTemplate("Round peg");
        TemplateTile(vm, "Round peg").IsFavorite = true;

        var again = NewViewModel();

        Assert.True(TemplateTile(again, "Round peg").IsFavorite);
        again.LibraryCategoryIndex = 1;
        Assert.Equal(["Round peg"], again.LibraryEntries.Select(e => e.Name).ToArray());
    }

    [UiFact]
    public async Task A_new_start_lists_the_templates_that_are_in_the_folder()
    {
        await WithTemplate("Round peg");

        var again = NewViewModel();

        Assert.Equal([1, 2, 3], TemplateTile(again, "Round peg").Thumbnail);
    }

    // ---- the actions of the old menu -------------------------------------------------------------

    [UiFact]
    public async Task Editing_changes_the_tile_and_deleting_removes_it()
    {
        var vm = await WithTemplate("Round peg");
        var tile = TemplateTile(vm, "Round peg");
        _dialogs.SaveTemplateScript = dialog =>
        {
            dialog.Name = "Round pin";
            return true;
        };

        tile.Template.EditCommand.Execute(null);

        Assert.Equal("Round pin", TemplateTile(vm, "Round pin").Name);
        Assert.DoesNotContain(vm.LibraryEntries, e => e.Name == "Round peg");

        _dialogs.ConfirmAnswer = false;
        TemplateTile(vm, "Round pin").Template.DeleteCommand.Execute(null);
        Assert.Contains(vm.LibraryEntries, e => e.Name == "Round pin");

        _dialogs.ConfirmAnswer = true;
        TemplateTile(vm, "Round pin").Template.DeleteCommand.Execute(null);
        Assert.DoesNotContain(vm.LibraryEntries, e => e is TemplateEntryViewModel);
        Assert.Empty(vm.Templates);
    }

    [UiFact]
    public async Task Duplicating_adds_a_second_tile()
    {
        var vm = await WithTemplate("Round peg");

        TemplateTile(vm, "Round peg").Template.DuplicateCommand.Execute(null);

        Assert.Equal(2, vm.LibraryEntries.OfType<TemplateEntryViewModel>().Count());
    }

    // ---- inserting -------------------------------------------------------------------------------

    [UiFact]
    public async Task A_click_inserts_the_template_like_before()
    {
        var vm = await WithTemplate("Round peg");
        var steps = vm.HistoryEntries.Count;

        vm.AddLibraryItemCommand.Execute(TemplateTile(vm, "Round peg"));
        await vm.WaitForEvaluationAsync();

        var node = Assert.Single(vm.Document.Root.Children);
        Assert.Equal("Round peg", node.Name);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([node.Id], vm.SelectedIds.ToArray());
    }

    [UiFact]
    public async Task A_drop_on_the_plate_puts_the_template_at_the_snapped_mouse_position_on_the_floor()
    {
        var vm = await WithTemplate("Round peg");

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest(TemplateTile(vm, "Round peg").Item.Id, Plate(40.4f, -20.6f)));
        await vm.WaitForEvaluationAsync();

        var node = Assert.Single(vm.Document.Root.Children);
        var bounds = BoundsOf(vm, node);
        Assert.Equal(40, bounds.Center.X, 1);
        Assert.Equal(-21, bounds.Center.Y, 1);
        Assert.Equal(0, bounds.Min.Z, 2);
    }

    [UiFact]
    public async Task A_drop_on_a_slanted_face_makes_the_template_stand_on_it_along_its_normal()
    {
        var vm = await WithTemplate("Round peg");
        var wedge = await Add(vm, "wedge");
        var normal = Vector3.Normalize(new Vector3(0, -1, 1));
        var point = new Vector3(0, 0, 10);

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest(TemplateTile(vm, "Round peg").Item.Id, new FacePick(wedge.Id, point, normal)));
        await vm.WaitForEvaluationAsync();

        var node = vm.Document.Root.Children[^1];
        Assert.Equal("Round peg", node.Name);
        var vertices = vm.ViewportItems.Where(i => i.NodeId == node.Id && i.Kind == ViewportItemKind.Solid).SelectMany(i => i.Mesh.Vertices.ToArray()).ToList();
        var along = vertices.Select(v => Vector3.Dot(v - point, normal)).ToList();
        Assert.Equal(0, along.Min(), 1); // its base lies in the face
        Assert.Equal(20, along.Max(), 1); // and it rises along the normal
    }

    [UiFact]
    public async Task An_unknown_template_id_changes_nothing()
    {
        var vm = await WithTemplate("Round peg");
        var steps = vm.HistoryEntries.Count;

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest("template:missing.fwt", Plate(0, 0)));
        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.Document.Root.Children);
        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task The_pictures_of_templates_are_not_drawn_again_by_the_library()
    {
        var preview = new FakePreview();
        var vm = NewViewModel(preview);
        await SaveTemplate(vm, "Round peg");
        var calls = preview.Calls;

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.Equal(calls + LibraryCatalog.Items.Count, preview.Calls); // only the elements of the catalog
        Assert.Equal([1, 2, 3], TemplateTile(vm, "Round peg").Thumbnail);
    }
}
