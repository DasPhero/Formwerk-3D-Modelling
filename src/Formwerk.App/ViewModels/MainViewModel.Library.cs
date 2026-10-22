using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.IO;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>One element in the library list: the catalog item and whether it is a favorite.</summary>
public partial class LibraryEntryViewModel(LibraryItem item, Action<LibraryEntryViewModel> favoriteChanged) : ObservableObject
{
    private bool _silent;

    public LibraryItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>The tooltip: what the element is.</summary>
    public virtual string Tip => Item.Description + "\n" + Texts.Get("Library.Item.Tip");

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    /// <summary>The picture (PNG) of the element; null while it is not made or when it could not be made.</summary>
    [ObservableProperty]
    public partial byte[]? Thumbnail { get; set; }

    partial void OnIsFavoriteChanged(bool value)
    {
        if (!_silent) favoriteChanged(this);
    }

    /// <summary>Sets the star without telling the view model (when the settings are loaded).</summary>
    internal void SetFavoriteSilently(bool value)
    {
        _silent = true;
        try
        {
            IsFavorite = value;
        }
        finally
        {
            _silent = false;
        }
    }
}

/// <summary>A saved template as a tile of the library (F-LIB-03): it has the actions of the old "My Templates" list in its context menu.</summary>
public sealed class TemplateEntryViewModel : LibraryEntryViewModel
{
    public TemplateEntryViewModel(TemplateItemViewModel template, Action<LibraryEntryViewModel> favoriteChanged)
        : base(ItemOf(template), favoriteChanged)
    {
        Template = template;
        Thumbnail = template.PreviewPng;
    }

    public TemplateItemViewModel Template { get; }

    public override string Tip => Template.Tooltip + "\n" + Texts.Get("Library.Item.Tip");

    /// <summary>The library item that stands for the template: found by name, category, tags and description.</summary>
    private static LibraryItem ItemOf(TemplateItemViewModel template)
    {
        var keywords = new List<string>(template.Info.Tags);
        if (template.Category.Length > 0) keywords.Add(template.Category);
        if (template.Description.Length > 0) keywords.Add(template.Description);
        return new LibraryItem(template.Id, template.Name, LibraryCategory.MyTemplates, keywords, template.Description, template.Size);
    }
}

/// <summary>
/// The library (F-LIB-02, F-LIB-03): the left panel lists the basic shapes and the ready-made elements of <see cref="LibraryCatalog"/> and
/// the saved templates. A search and a category choice (all, favorites, one category, "My Templates") narrow the list; a star marks
/// favorites, which are remembered between sessions. A click inserts the element at the first free place; a ready-made element comes as a
/// normal tree of shapes, so it stays editable.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly List<LibraryEntryViewModel> _libraryAll = []; // the elements of the catalog
    private readonly List<TemplateEntryViewModel> _templateEntries = [];
    private readonly List<string> _favoriteIds = [];

    /// <summary>The elements that match the search and the category choice.</summary>
    public ObservableCollection<LibraryEntryViewModel> LibraryEntries { get; } = [];

    /// <summary>0 = all, 1 = favorites, then one entry per <see cref="LibraryCategory"/> (index = 2 + the category).</summary>
    public IReadOnlyList<AxisChoice> LibraryCategoryChoices { get; } =
    [
        new(0, Texts.Get("Library.All")),
        new(1, Texts.Get("Library.Favorites")),
        .. Enum.GetValues<LibraryCategory>().Select(c => new AxisChoice(2 + (int)c, c.DisplayName())),
    ];

    [ObservableProperty]
    public partial int LibraryCategoryIndex { get; set; }

    [ObservableProperty]
    public partial string LibrarySearchText { get; set; } = "";

    /// <summary>Nothing matches the search and the category choice.</summary>
    [ObservableProperty]
    public partial bool IsLibraryEmpty { get; set; }

    /// <summary>What the empty list says: that there are no templates yet (the hint of the old section), or that nothing was found.</summary>
    [ObservableProperty]
    public partial string LibraryEmptyText { get; set; } = Texts.Get("Library.Empty");

    partial void OnLibraryCategoryIndexChanged(int value) => RefreshLibrary();

    partial void OnLibrarySearchTextChanged(string value) => RefreshLibrary();

    private void InitializeLibrary()
    {
        _libraryAll.AddRange(LibraryCatalog.Items.Select(item => new LibraryEntryViewModel(item, OnFavoriteChanged)));
        RefreshLibrary();
    }

    /// <summary>Takes the favorites from the settings (at the start and when the settings dialog was closed).</summary>
    private void ApplyFavorites(IEnumerable<string> ids)
    {
        _favoriteIds.Clear();
        _favoriteIds.AddRange(ids);
        foreach (var entry in _libraryAll.Concat(_templateEntries))
            entry.SetFavoriteSilently(IsFavoriteId(entry.Item.Id));
        RefreshLibrary();
    }

    private bool IsFavoriteId(string id) => _favoriteIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    /// <summary>The templates changed (saved, renamed, deleted, imported): their tiles are made again.</summary>
    private void RebuildTemplateEntries()
    {
        _templateEntries.Clear();
        foreach (var template in Templates)
        {
            var entry = new TemplateEntryViewModel(template, OnFavoriteChanged);
            entry.SetFavoriteSilently(IsFavoriteId(entry.Item.Id));
            _templateEntries.Add(entry);
        }

        RefreshLibrary();
    }

    private void OnFavoriteChanged(LibraryEntryViewModel entry)
    {
        _favoriteIds.RemoveAll(id => string.Equals(id, entry.Item.Id, StringComparison.OrdinalIgnoreCase));
        if (entry.IsFavorite) _favoriteIds.Add(entry.Item.Id);
        PersistUserSettings();
        if (LibraryCategoryIndex == 1) RefreshLibrary(); // an element that is no favorite any more leaves the favorites at once
    }

    private void RefreshLibrary()
    {
        if (_libraryAll.Count == 0) return;

        var entries = new Dictionary<LibraryItem, LibraryEntryViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var entry in _libraryAll.Concat(_templateEntries)) entries[entry.Item] = entry;

        LibraryCategory? category = LibraryCategoryIndex >= 2 ? (LibraryCategory)(LibraryCategoryIndex - 2) : null;
        IReadOnlySet<string>? only = LibraryCategoryIndex == 1 ? new HashSet<string>(_favoriteIds, StringComparer.OrdinalIgnoreCase) : null;
        var found = LibrarySearch.Filter(entries.Keys.OrderBy(i => i.Category == LibraryCategory.MyTemplates ? 1 : 0), LibrarySearchText, category, only);
        var wanted = found.Select(item => entries[item]).ToList();

        if (!wanted.SequenceEqual(LibraryEntries))
        {
            LibraryEntries.Clear();
            foreach (var entry in wanted) LibraryEntries.Add(entry);
        }

        IsLibraryEmpty = wanted.Count == 0;
        var noTemplatesYet = LibraryCategoryIndex == 2 + (int)LibraryCategory.MyTemplates && LibrarySearchText.Trim().Length == 0 && _templateEntries.Count == 0;
        LibraryEmptyText = Texts.Get(noTemplatesYet ? "Panel.Templates.Empty" : "Library.Empty");
    }

    /// <summary>The click on an element of the library: a basic shape like the old shape button, a ready-made element as a new tree, a template as before.</summary>
    [RelayCommand]
    private void AddLibraryItem(LibraryEntryViewModel? entry)
    {
        if (entry is null) return;
        if (entry is TemplateEntryViewModel template)
        {
            InsertTemplateAt(template.Template, null);
            return;
        }

        var item = entry.Item;
        if (item.IsShape)
        {
            AddShape(item.ShapeId);
            return;
        }

        var node = item.CreateNode();
        node.Name = UniqueName(item.Name);
        node.Transform = Transform.FromPosition(FreeSpot(item.Footprint));
        _history.Execute(new AddNodeCommand(_document, _document.Root, node));
        SelectOnly([node.Id]);
    }

    /// <summary>Where a drop puts the element: on the point of a face along its normal; on the plate on the grid when grid snap is on.</summary>
    private Transform DropPlacement(FacePick pick)
    {
        var point = pick.Point;
        if (pick.NodeId == Guid.Empty && SnapToGrid && GridStep > 0 && !pick.NoSnap)
        {
            var step = (float)GridStep;
            point = new Vector3(MathF.Round(point.X / step) * step, MathF.Round(point.Y / step) * step, point.Z);
        }

        return FacePlacement.OnFace(point, pick.Normal);
    }

    /// <summary>The group that is open (a drop goes into it), or the root.</summary>
    private Node DropParent() => OpenContainer is { } open && open != _document.Root ? open : _document.Root;

    /// <summary>A placement in world space as a transform in the space of <paramref name="parent"/> (the element keeps standing where the mouse was).</summary>
    private static Transform InSpaceOf(Transform world, Node parent)
    {
        if (parent.Parent is null) return world;
        if (!Matrix4x4.Invert(TransformOps.LocalToWorld(parent), out var inverse)) return world;
        return Matrix4x4.Decompose(world.ToMatrix() * inverse, out var scale, out var rotation, out var translation)
            ? new Transform(translation, Quaternion.Normalize(rotation), scale)
            : world;
    }

    /// <summary>
    /// An element dropped into the view (F-LIB-04): on a face it stands on the point with its up axis along the normal of the face (a
    /// cylinder dropped on a slanted face stands perpendicular on it); on the plate it stands upright at the mouse position, on the grid
    /// when grid snap is on (Alt or Shift: exactly where the mouse is). One undo step; the new element is selected. A template is
    /// placed the same way (F-LIB-03). While a group is open the element goes into that group.
    /// </summary>
    [RelayCommand]
    private void DropLibraryItem(LibraryDropRequest? request)
    {
        RemoveLibraryGhost();
        if (request is null) return;
        var template = _templateEntries.FirstOrDefault(e => string.Equals(e.Item.Id, request.ItemId, StringComparison.OrdinalIgnoreCase));
        var item = template is null ? LibraryCatalog.Find(request.ItemId) : null;
        if (template is null && item is null) return;

        var parent = DropParent();
        var placement = InSpaceOf(DropPlacement(request.Pick), parent);
        if (template is not null)
        {
            InsertTemplateAt(template.Template, placement, parent);
            return;
        }

        var node = item!.CreateNode();
        node.Name = UniqueName(item.Name);
        node.Transform = placement;
        _history.Execute(new AddNodeCommand(_document, parent, node));
        SelectOnly([node.Id]);
    }

    // ---- the ghost while an element is dragged over the view ---------------------------------------

    private SceneEvaluator? _ghostEvaluator;
    private readonly Dictionary<string, List<ViewportItem>> _ghostCache = [];
    private Guid _ghostId = Guid.NewGuid();
    private string? _ghostFor;
    private List<ViewportItem> _ghostItems = [];

    /// <summary>
    /// An element is dragged over the view: a ghost of it (the real shape of a catalog element, a box of its size for a template) is added
    /// once and then only moved by <see cref="PreviewTransforms"/> to where a drop would put it, so the scene is not rebuilt for every
    /// mouse move. Nothing in the document or the history changes.
    /// </summary>
    [RelayCommand]
    private void LibraryDragOver(LibraryDropRequest? request)
    {
        if (request is null || !EnsureLibraryGhost(request.ItemId)) return;
        PreviewTransforms = new Dictionary<Guid, Matrix4x4> { [_ghostId] = DropPlacement(request.Pick).ToMatrix() };
    }

    /// <summary>The dragged element left the view or was dropped: the ghost goes.</summary>
    [RelayCommand]
    private void LibraryDragEnd() => RemoveLibraryGhost();

    private bool GhostShown => ViewportItems.Any(i => i.NodeId == _ghostId);

    private void RemoveLibraryGhost()
    {
        if (!GhostShown) return;
        ViewportItems = [.. ViewportItems.Where(i => i.NodeId != _ghostId)];
        PreviewTransforms = null;
        _ghostFor = null;
    }

    private bool EnsureLibraryGhost(string itemId)
    {
        if (GhostShown && string.Equals(_ghostFor, itemId, StringComparison.OrdinalIgnoreCase)) return true;

        var meshes = GhostMeshes(itemId);
        if (meshes is null) return false;

        _ghostId = Guid.NewGuid();
        _ghostFor = itemId;
        _ghostItems = [.. meshes.Select(i => i with { NodeId = _ghostId, Kind = ViewportItemKind.Ghost })];
        ViewportItems = [.. ViewportItems, .. _ghostItems];
        return true;
    }

    /// <summary>A new scene was built while the ghost is up: the ghost is put back.</summary>
    private void KeepLibraryGhost()
    {
        if (_ghostFor is not null && !GhostShown) ViewportItems = [.. ViewportItems, .. _ghostItems];
    }

    private List<ViewportItem>? GhostMeshes(string itemId)
    {
        if (_ghostCache.TryGetValue(itemId, out var cached)) return cached;

        List<ViewportItem>? meshes = null;
        if (_templateEntries.FirstOrDefault(e => string.Equals(e.Item.Id, itemId, StringComparison.OrdinalIgnoreCase)) is { } template)
        {
            var size = template.Template.Size;
            var box = Formwerk.Features.Shapes.ShapeLibrary.Find("box")!.Generate(new Dictionary<string, double>
            {
                ["width"] = Math.Max(size.X, 0.1f), ["depth"] = Math.Max(size.Y, 0.1f), ["height"] = Math.Max(size.Z, 0.1f),
            });
            meshes = [new ViewportItem(Guid.Empty, box, ViewportItemKind.Solid, 0xFFB0B8C4)];
            return meshes; // a template can change: its box is made again every time
        }

        if (LibraryCatalog.Find(itemId) is { } item)
        {
            _ghostEvaluator ??= new SceneEvaluator(_kernel);
            lock (_evaluatorLock)
            {
                meshes = ThumbnailItems(item, _ghostEvaluator);
            }

            if (meshes.Count > 0) _ghostCache[itemId] = meshes;
        }

        return meshes is { Count: > 0 } ? meshes : null;
    }
}
