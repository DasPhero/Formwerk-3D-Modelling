using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The pictures of the library elements: made once in the background, kept on disk (F-LIB-02).</summary>
public sealed class LibraryThumbnailTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-thumbs-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>A renderer that records what it was asked to draw and returns a small fake picture (or null).</summary>
    private sealed class FakePreview(bool fail = false) : ITemplatePreview
    {
        public List<IReadOnlyList<ViewportItem>> Calls { get; } = [];

        public byte[]? Render(IReadOnlyList<ViewportItem> items)
        {
            Calls.Add(items);
            return fail ? null : [0x89, 0x50, 0x4E, 0x47, (byte)Calls.Count];
        }
    }

    private MainViewModel NewViewModel(ITemplatePreview? preview, string? folder = null) =>
        new(new ManifoldKernel(), new FakeDialogs(), templatePreview: preview, libraryThumbnailFolder: folder);

    private string CacheFile(string id) => Path.Combine(_folder, $"{id}-v{LibraryCatalog.ThumbnailVersion}.png");

    // ---- making them -----------------------------------------------------------------------------

    [UiFact]
    public async Task Every_element_gets_a_picture_from_the_renderer()
    {
        var preview = new FakePreview();
        var vm = NewViewModel(preview);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.All(vm.LibraryEntries, e => Assert.NotNull(e.Thumbnail));
        Assert.Equal(LibraryCatalog.Items.Count, preview.Calls.Count);
    }

    [UiFact]
    public async Task The_renderer_gets_the_solid_parts_of_that_element_on_the_plate()
    {
        var preview = new FakePreview();
        var vm = NewViewModel(preview);
        await vm.WaitForLibraryThumbnailsAsync();
        var index = LibraryCatalog.Items.ToList().FindIndex(i => i.Id == "hex-nut-m3");

        var items = preview.Calls[index];

        Assert.NotEmpty(items);
        Assert.All(items, i => Assert.Equal(ViewportItemKind.Solid, i.Kind));
        var bounds = items.Select(i => i.Mesh.ComputeBounds()).ToList();
        var size = new Vector3(bounds.Max(b => b.Max.X) - bounds.Min(b => b.Min.X), bounds.Max(b => b.Max.Y) - bounds.Min(b => b.Min.Y),
            bounds.Max(b => b.Max.Z) - bounds.Min(b => b.Min.Z));
        var footprint = LibraryCatalog.Items[index].Footprint;
        Assert.Equal(footprint.X, size.X, 1);
        Assert.Equal(footprint.Z, size.Z, 1); // the hole is cut: the picture shows the finished nut
        Assert.Equal(0, bounds.Min(b => b.Min.Z), 2);
    }

    [UiFact]
    public async Task Making_the_pictures_does_not_touch_the_document_the_history_or_the_view()
    {
        var preview = new FakePreview();
        var vm = NewViewModel(preview);
        var steps = vm.HistoryEntries.Count;

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.Empty(vm.Document.Root.Children);
        Assert.Empty(vm.ViewportItems);
        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.DoesNotContain("*", vm.WindowTitle);
    }

    [UiFact]
    public async Task The_pictures_are_made_only_once()
    {
        var preview = new FakePreview();
        var vm = NewViewModel(preview);
        await vm.WaitForLibraryThumbnailsAsync();
        var calls = preview.Calls.Count;

        await vm.WaitForLibraryThumbnailsAsync();
        await vm.GenerateLibraryThumbnailsAsync();

        Assert.Equal(calls, preview.Calls.Count);
    }

    [UiFact]
    public async Task Working_while_the_pictures_are_made_gives_the_right_scene()
    {
        var vm = NewViewModel(new FakePreview());

        _ = vm.GenerateLibraryThumbnailsAsync();
        vm.AddShapeCommand.Execute("box"); // at once, the pictures are still being made
        await vm.WaitForEvaluationAsync();
        await vm.WaitForLibraryThumbnailsAsync();
        await vm.WaitForEvaluationAsync();

        Assert.Single(vm.Document.Root.Children);
        Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
        Assert.All(vm.LibraryEntries, e => Assert.NotNull(e.Thumbnail));
    }

    // ---- the cache -------------------------------------------------------------------------------

    [UiFact]
    public async Task The_pictures_are_kept_on_disk_and_the_next_start_does_not_draw_them_again()
    {
        var first = NewViewModel(new FakePreview(), _folder);
        await first.WaitForLibraryThumbnailsAsync();
        Assert.All(LibraryCatalog.Items, i => Assert.True(File.Exists(CacheFile(i.Id)), i.Id));

        var preview = new FakePreview();
        var second = NewViewModel(preview, _folder);
        await second.WaitForLibraryThumbnailsAsync();

        Assert.Empty(preview.Calls);
        Assert.All(second.LibraryEntries, e => Assert.NotNull(e.Thumbnail));
        Assert.Equal(first.LibraryEntries[0].Thumbnail, second.LibraryEntries[0].Thumbnail);
    }

    [UiFact]
    public async Task An_empty_cache_file_is_ignored_and_made_again()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(CacheFile("box"), []);
        var preview = new FakePreview();
        var vm = NewViewModel(preview, _folder);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.NotNull(vm.LibraryEntries.Single(e => e.Item.Id == "box").Thumbnail);
        Assert.Equal(LibraryCatalog.Items.Count, preview.Calls.Count); // the empty file did not count
        Assert.True(new FileInfo(CacheFile("box")).Length > 0);
    }

    [UiFact]
    public async Task A_picture_of_another_version_of_the_catalog_is_not_used()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllBytes(Path.Combine(_folder, $"box-v{LibraryCatalog.ThumbnailVersion - 1}.png"), [1, 2, 3, 4]);
        var preview = new FakePreview();
        var vm = NewViewModel(preview, _folder);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.Equal(LibraryCatalog.Items.Count, preview.Calls.Count);
        Assert.NotEqual(new byte[] { 1, 2, 3, 4 }, vm.LibraryEntries.Single(e => e.Item.Id == "box").Thumbnail);
    }

    // ---- when it does not work -------------------------------------------------------------------

    [UiFact]
    public async Task A_renderer_that_gives_nothing_leaves_the_tiles_without_a_picture()
    {
        var vm = NewViewModel(new FakePreview(fail: true), _folder);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.Equal(LibraryCatalog.Items.Count, vm.LibraryEntries.Count);
        Assert.All(vm.LibraryEntries, e => Assert.Null(e.Thumbnail));
        Assert.False(Directory.Exists(_folder) && Directory.GetFiles(_folder).Length > 0);
    }

    [UiFact]
    public async Task Without_a_renderer_nothing_is_made()
    {
        var vm = NewViewModel(null, _folder);

        await vm.WaitForLibraryThumbnailsAsync();

        Assert.All(vm.LibraryEntries, e => Assert.Null(e.Thumbnail));
    }

    [UiFact]
    public async Task A_folder_that_cannot_be_written_does_not_stop_the_pictures()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_folder)!);
        File.WriteAllText(_folder, "this is a file, not a folder"); // the cache folder cannot be created
        try
        {
            var vm = NewViewModel(new FakePreview(), _folder);

            await vm.WaitForLibraryThumbnailsAsync();

            Assert.All(vm.LibraryEntries, e => Assert.NotNull(e.Thumbnail));
        }
        finally
        {
            File.Delete(_folder);
        }
    }
}
