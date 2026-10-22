using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The polish of the library: old pictures are cleaned up, drops go into the open group, a ghost follows the drag (F-LIB-02/03/04).</summary>
public sealed class LibraryPolishTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-libpolish-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private sealed class FakePreview : ITemplatePreview
    {
        public byte[]? Render(IReadOnlyList<ViewportItem> items) => [1, 2, 3];
    }

    private MainViewModel NewViewModel(string? thumbs = null, bool templates = false) =>
        new(new ManifoldKernel(), _dialogs, templates: templates ? new TemplateStore(Path.Combine(_folder, "templates")) : null,
            templatePreview: new FakePreview(), libraryThumbnailFolder: thumbs);

    private static FacePick Plate(float x, float y) => new(Guid.Empty, new Vector3(x, y, 0), Vector3.UnitZ);

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Children[^1];
    }

    private static Bounds BoundsOf(MainViewModel vm, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    /// <summary>A moved and turned group with one box in it, opened; returns the group.</summary>
    private static async Task<GroupNode> OpenTurnedGroup(MainViewModel vm)
    {
        var box = await Add(vm, "box");
        vm.SelectOnly([box.Id]);
        vm.GroupCommand.Execute(null);
        var group = (GroupNode)vm.Document.Root.Children[0];
        group.Transform = new Transform(new Vector3(30, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One);
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        vm.OpenGroupCommand.Execute((Guid?)box.Id);
        Assert.True(vm.IsGroupOpen);
        return group;
    }

    // ---- old pictures ----------------------------------------------------------------------------

    [UiFact]
    public async Task Old_pictures_are_deleted_when_the_pictures_are_made_and_other_files_stay()
    {
        Directory.CreateDirectory(_folder);
        var current = Path.Combine(_folder, $"box-v{LibraryCatalog.ThumbnailVersion}.png");
        File.WriteAllBytes(current, [9, 9, 9]);
        var oldVersion = Path.Combine(_folder, $"box-v{LibraryCatalog.ThumbnailVersion - 1}.png");
        var unknownId = Path.Combine(_folder, $"no-longer-there-v{LibraryCatalog.ThumbnailVersion}.png");
        var notes = Path.Combine(_folder, "notes.txt");
        File.WriteAllBytes(oldVersion, [1]);
        File.WriteAllBytes(unknownId, [1]);
        File.WriteAllText(notes, "keep me");
        var vm = NewViewModel(_folder);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.False(File.Exists(oldVersion));
        Assert.False(File.Exists(unknownId));
        Assert.True(File.Exists(current));
        Assert.True(File.Exists(notes));
        Assert.Equal([9, 9, 9], vm.LibraryEntries.Single(e => e.Item.Id == "box").Thumbnail); // the good picture was used
    }

    // ---- drop into the open group ----------------------------------------------------------------

    [UiFact]
    public async Task A_drop_while_a_group_is_open_goes_into_the_group_where_the_mouse_is()
    {
        var vm = NewViewModel();
        var group = await OpenTurnedGroup(vm);

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest("cylinder", Plate(50, 10)));
        await vm.WaitForEvaluationAsync();

        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(2, group.Children.Count);
        var cylinder = group.Children[^1];
        Assert.Equal("Cylinder", cylinder.Name);
        var bounds = BoundsOf(vm, cylinder);
        Assert.Equal(50, bounds.Center.X, 1); // the group is moved and turned: the cylinder still stands where the mouse was
        Assert.Equal(10, bounds.Center.Y, 1);
        Assert.Equal(0, bounds.Min.Z, 2);
    }

    [UiFact]
    public async Task Without_an_open_group_a_drop_still_goes_to_the_root()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest("cylinder", Plate(50, 10)));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    // ---- the ghost -------------------------------------------------------------------------------

    private static List<ViewportItem> Ghosts(MainViewModel vm) =>
        [.. vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Ghost && vm.Document.FindNode(i.NodeId) is null)];

    private static Matrix4x4 GhostMatrix(MainViewModel vm, Guid id) => vm.PreviewTransforms![id];

    [UiFact]
    public void Dragging_an_element_over_the_plate_shows_a_ghost_that_follows_the_snapped_point()
    {
        var vm = NewViewModel();

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", Plate(23.4f, -7.6f)));

        var ghosts = Ghosts(vm);
        Assert.NotEmpty(ghosts);
        var id = ghosts[0].NodeId;
        Assert.All(ghosts, g => Assert.Equal(id, g.NodeId));
        Assert.Equal(new Vector3(23, -8, 0), GhostMatrix(vm, id).Translation);

        var items = vm.ViewportItems;
        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", Plate(-30, 12)));

        Assert.Same(items, vm.ViewportItems); // moving the ghost does not rebuild the scene
        Assert.Equal(new Vector3(-30, 12, 0), GhostMatrix(vm, id).Translation);
    }

    [UiFact]
    public async Task The_ghost_on_a_slanted_face_stands_along_the_normal()
    {
        var vm = NewViewModel();
        var wedge = await Add(vm, "wedge");
        var normal = Vector3.Normalize(new Vector3(0, -1, 1));

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", new FacePick(wedge.Id, new Vector3(0, 0, 10), normal)));

        var id = Ghosts(vm)[0].NodeId;
        var up = Vector3.TransformNormal(Vector3.UnitZ, GhostMatrix(vm, id));
        Assert.True(Vector3.Distance(normal, up) < 1e-4f);
        Assert.Equal(new Vector3(0, 0, 10), GhostMatrix(vm, id).Translation);
    }

    [UiFact]
    public async Task Ending_the_drag_or_dropping_takes_the_ghost_away()
    {
        var vm = NewViewModel();
        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", Plate(0, 0)));
        Assert.NotEmpty(Ghosts(vm));

        vm.LibraryDragEndCommand.Execute(null);

        Assert.Empty(Ghosts(vm));
        Assert.Null(vm.PreviewTransforms);

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", Plate(0, 0)));
        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest("cylinder", Plate(0, 0)));
        await vm.WaitForEvaluationAsync();
        Assert.Empty(Ghosts(vm));
        Assert.Null(vm.PreviewTransforms);
        Assert.Single(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task The_ghost_changes_nothing_in_the_document_or_the_exports_and_an_unknown_element_has_none()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("cylinder", Plate(0, 0)));
        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("no-such-element", Plate(5, 5)));
        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.Document.Root.Children);
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.False(vm.ExportStlCommand.CanExecute(null));
        Assert.DoesNotContain("*", vm.WindowTitle);

        vm.LibraryDragEndCommand.Execute(null);
        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("no-such-element", Plate(5, 5)));
        Assert.Empty(Ghosts(vm));
    }

    [UiFact]
    public async Task A_ready_made_element_has_a_ghost_of_its_real_shape()
    {
        var vm = NewViewModel();

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest("hex-nut-m3", Plate(0, 0)));
        await vm.WaitForEvaluationAsync();

        var ghosts = Ghosts(vm);
        Assert.NotEmpty(ghosts);
        var volume = ghosts.Sum(g => g.Mesh.ComputeVolume());
        var solid = 3 * Math.Sqrt(3) / 2 * 3.175 * 3.175 * 2.4;
        Assert.True(volume < solid * 0.9, "the ghost has the hole of the nut");
    }

    [UiFact]
    public async Task A_template_has_a_ghost_box_of_its_size()
    {
        var vm = NewViewModel(templates: true);
        var cylinder = await Add(vm, "cylinder");
        vm.SelectOnly([cylinder.Id]);
        _dialogs.SaveTemplateScript = dialog =>
        {
            dialog.Name = "Round peg";
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        var tile = vm.LibraryEntries.OfType<TemplateEntryViewModel>().Single();

        vm.LibraryDragOverCommand.Execute(new LibraryDropRequest(tile.Item.Id, Plate(0, 0)));

        var ghosts = Ghosts(vm);
        var ghost = Assert.Single(ghosts);
        var size = ghost.Mesh.ComputeBounds().Size;
        Assert.Equal(tile.Template.Size.X, size.X, 1);
        Assert.Equal(tile.Template.Size.Z, size.Z, 1);
        Assert.Equal(0, ghost.Mesh.ComputeBounds().Min.Z, 2);
    }

    [UiFact]
    public async Task A_template_dropped_into_an_open_group_goes_into_the_group()
    {
        var vm = NewViewModel(templates: true);
        var cylinder = await Add(vm, "cylinder");
        vm.SelectOnly([cylinder.Id]);
        _dialogs.SaveTemplateScript = dialog =>
        {
            dialog.Name = "Round peg";
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        vm.DeleteCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = await OpenTurnedGroup(vm);
        var tile = vm.LibraryEntries.OfType<TemplateEntryViewModel>().Single();

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest(tile.Item.Id, Plate(50, 10)));
        await vm.WaitForEvaluationAsync();

        var inserted = group.Children[^1];
        Assert.Equal("Round peg", inserted.Name);
        var bounds = BoundsOf(vm, inserted);
        Assert.Equal(50, bounds.Center.X, 1);
        Assert.Equal(10, bounds.Center.Y, 1);
    }
}
