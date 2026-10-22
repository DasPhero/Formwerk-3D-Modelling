using System.Collections.ObjectModel;
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
public sealed partial class LibraryEntryViewModel(LibraryItem item, Action<LibraryEntryViewModel> favoriteChanged) : ObservableObject
{
    private bool _silent;

    public LibraryItem Item { get; } = item;

    public string Name => Item.Name;

    /// <summary>The tooltip: what the element is.</summary>
    public string Tip => Item.Description + "\n" + Texts.Get("Library.Item.Tip");

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

/// <summary>
/// The library (F-LIB-02): the left panel lists the basic shapes and the ready-made elements of <see cref="LibraryCatalog"/>. A search
/// and a category choice (all, favorites, one category) narrow the list; a star marks favorites, which are remembered between sessions.
/// A click inserts the element at the first free place; a ready-made element comes as a normal tree of shapes, so it stays editable.
/// </summary>
public sealed partial class MainViewModel
{
    private readonly List<LibraryEntryViewModel> _libraryAll = [];
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
        foreach (var entry in _libraryAll)
            entry.SetFavoriteSilently(_favoriteIds.Contains(entry.Item.Id, StringComparer.OrdinalIgnoreCase));
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

        LibraryCategory? category = LibraryCategoryIndex >= 2 ? (LibraryCategory)(LibraryCategoryIndex - 2) : null;
        IReadOnlySet<string>? only = LibraryCategoryIndex == 1 ? new HashSet<string>(_favoriteIds, StringComparer.OrdinalIgnoreCase) : null;
        var found = LibrarySearch.Filter(LibraryCatalog.Items, LibrarySearchText, category, only);
        var wanted = found.Select(item => _libraryAll.First(e => e.Item == item)).ToList();

        if (!wanted.SequenceEqual(LibraryEntries))
        {
            LibraryEntries.Clear();
            foreach (var entry in wanted) LibraryEntries.Add(entry);
        }

        IsLibraryEmpty = wanted.Count == 0;
    }

    /// <summary>The click on an element of the library: a basic shape like the old shape button, a ready-made element as a new tree.</summary>
    [RelayCommand]
    private void AddLibraryItem(LibraryEntryViewModel? entry)
    {
        if (entry is null) return;
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
    /// when grid snap is on (Alt or Shift: exactly where the mouse is). One undo step; the new element is selected.
    /// </summary>
    [RelayCommand]
    private void DropLibraryItem(LibraryDropRequest? request)
    {
        if (request is null || LibraryCatalog.Find(request.ItemId) is not { } item) return;

        var pick = request.Pick;
        var point = pick.Point;
        if (pick.NodeId == Guid.Empty && SnapToGrid && GridStep > 0 && !pick.NoSnap)
        {
            var step = (float)GridStep;
            point = new Vector3(MathF.Round(point.X / step) * step, MathF.Round(point.Y / step) * step, point.Z);
        }

        var node = item.CreateNode();
        node.Name = UniqueName(item.Name);
        node.Transform = FacePlacement.OnFace(point, pick.Normal);
        _history.Execute(new AddNodeCommand(_document, _document.Root, node));
        SelectOnly([node.Id]);
    }
}
