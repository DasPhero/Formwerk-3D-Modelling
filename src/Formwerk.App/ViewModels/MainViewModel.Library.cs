using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
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

    /// <summary>
    /// An element dropped into the view (F-LIB-04): on a face it stands on the point with its up axis along the normal of the face (a
    /// cylinder dropped on a slanted face stands perpendicular on it); on the plate it stands upright at the mouse position, on the grid
    /// when grid snap is on (Alt or Shift: exactly where the mouse is). One undo step; the new element is selected. A template is
    /// placed the same way (F-LIB-03).
    /// </summary>
    [RelayCommand]
    private void DropLibraryItem(LibraryDropRequest? request)
    {
        if (request is null) return;
        var template = _templateEntries.FirstOrDefault(e => string.Equals(e.Item.Id, request.ItemId, StringComparison.OrdinalIgnoreCase));
        var item = template is null ? LibraryCatalog.Find(request.ItemId) : null;
        if (template is null && item is null) return;

        var pick = request.Pick;
        var point = pick.Point;
        if (pick.NodeId == Guid.Empty && SnapToGrid && GridStep > 0 && !pick.NoSnap)
        {
            var step = (float)GridStep;
            point = new Vector3(MathF.Round(point.X / step) * step, MathF.Round(point.Y / step) * step, point.Z);
        }

        var placement = FacePlacement.OnFace(point, pick.Normal);
        if (template is not null)
        {
            InsertTemplateAt(template.Template, placement);
            return;
        }

        var node = item!.CreateNode();
        node.Name = UniqueName(item.Name);
        node.Transform = placement;
        _history.Execute(new AddNodeCommand(_document, _document.Root, node));
        SelectOnly([node.Id]);
    }
}
