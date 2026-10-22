using System.IO;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The pictures of the library elements (F-LIB-02): after the start every element that has no picture yet gets one, one element at a time
/// so that the window stays usable. A picture comes from the cache folder when it is there, else the element is evaluated in a scratch
/// document (nothing of the user's document is touched) and drawn by the same renderer as the pictures of templates; the result is
/// kept in the cache folder, so the next start shows all pictures at once.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly string? _thumbnailFolder;
    private Task? _libraryThumbnails;

    /// <summary>
    /// Makes the missing pictures (the program starts this after the window is up); the work is done once, later calls return the same task. Nothing is made without a renderer
    /// (<see cref="ITemplatePreview"/>); an element whose picture cannot be made stays without one.
    /// </summary>
    public Task GenerateLibraryThumbnailsAsync() => _libraryThumbnails ??= MakeLibraryThumbnailsAsync();

    /// <summary>Completes when all pictures are made (or could not be made); used by the tests.</summary>
    public Task WaitForLibraryThumbnailsAsync() => GenerateLibraryThumbnailsAsync();

    private async Task MakeLibraryThumbnailsAsync()
    {
        if (_templatePreview is null) return;
        CleanThumbnailFolder();
        var evaluator = new SceneEvaluator(_kernel); // its own caches; the kernel is shared, so it is used under the lock

        foreach (var entry in _libraryAll)
        {
            if (entry.Thumbnail is not null) continue;

            var png = ReadThumbnail(entry.Item);
            if (png is null)
            {
                var items = await Task.Run(() =>
                {
                    lock (_evaluatorLock)
                    {
                        return ThumbnailItems(entry.Item, evaluator);
                    }
                }).ConfigureAwait(true);
                if (items.Count == 0) continue;

                png = _templatePreview.Render(items); // drawing needs the UI thread
                if (png is null) continue;
                WriteThumbnail(entry.Item, png);
            }

            entry.Thumbnail = png;
            await Task.Yield(); // let the window handle its messages between two pictures
        }
    }

    /// <summary>Deletes the pictures of other versions of the catalog and of elements that are gone; other files in the folder stay.</summary>
    private void CleanThumbnailFolder()
    {
        if (_thumbnailFolder is null || !Directory.Exists(_thumbnailFolder)) return;
        var keep = new HashSet<string>(LibraryCatalog.Items.Select(i => $"{i.Id}-v{LibraryCatalog.ThumbnailVersion}.png"), StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var file in Directory.EnumerateFiles(_thumbnailFolder, "*-v*.png"))
            {
                if (!keep.Contains(Path.GetFileName(file))) File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // an old picture that cannot be deleted does no harm
        }
    }

    /// <summary>The solid parts of the element as it is inserted, evaluated in a scratch document.</summary>
    private static List<ViewportItem> ThumbnailItems(LibraryItem item, SceneEvaluator evaluator)
    {
        try
        {
            var document = new FormwerkDocument();
            new AddNodeCommand(document, document.Root, item.CreateNode()).Do();
            return [.. evaluator.Evaluate(document).Parts
                .Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty)
                .Select(p => new ViewportItem(p.Source.Id, p.Mesh, ViewportItemKind.Solid, p.Source.Color))];
        }
        catch (GeometryException)
        {
            return [];
        }
    }

    private string? ThumbnailPath(LibraryItem item) =>
        _thumbnailFolder is null ? null : Path.Combine(_thumbnailFolder, $"{item.Id}-v{LibraryCatalog.ThumbnailVersion}.png");

    private byte[]? ReadThumbnail(LibraryItem item)
    {
        if (ThumbnailPath(item) is not { } path) return null;
        try
        {
            if (!File.Exists(path)) return null;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length > 0 ? bytes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void WriteThumbnail(LibraryItem item, byte[] png)
    {
        if (ThumbnailPath(item) is not { } path) return;
        try
        {
            Directory.CreateDirectory(_thumbnailFolder!);
            File.WriteAllBytes(path, png);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // the picture is still shown; it is made again at the next start
        }
    }
}
