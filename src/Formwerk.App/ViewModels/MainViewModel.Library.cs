using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
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

    /// <summary>One check mark per own folder of the user (the "Folders" menu of the tile): whether the element is in that folder.</summary>
    public ObservableCollection<FolderMembershipViewModel> FolderMemberships { get; } = [];

    /// <summary>There is at least one own folder, so the "Folders" menu has something to offer.</summary>
    public bool HasFolders => FolderMemberships.Count > 0;

    internal void NotifyFoldersChanged() => OnPropertyChanged(nameof(HasFolders));

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

    /// <summary>Edge length in pixels of the pictures in the library (the slider of the panel; remembered).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryTileWidth))]
    public partial double LibraryThumbnailSize { get; set; } = 44;

    /// <summary>Width of a tile of the library: the picture and a little room for the star.</summary>
    public double LibraryTileWidth => LibraryThumbnailSize + 20;

    partial void OnLibraryThumbnailSizeChanged(double value)
    {
        var clamped = Math.Clamp(double.IsFinite(value) ? value : 44, UserSettings.MinThumbnailSize, UserSettings.MaxThumbnailSize);
        if (clamped != value)
        {
            LibraryThumbnailSize = clamped; // comes back here once with the allowed value
            return;
        }

        PersistUserSettings();
    }

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

    partial void OnLibrarySearchTextChanged(string value) => RefreshLibrary();

    private void InitializeLibrary()
    {
        _libraryAll.AddRange(LibraryCatalog.Items.Select(item => new LibraryEntryViewModel(item, OnFavoriteChanged)));
        RebuildFolderMemberships();
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
            RebuildFolderMemberships(entry);
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

        var index = LibraryCategoryIndex;
        LibraryCategory? category = null;
        string? subFolder = null;
        IReadOnlySet<string>? only = null;
        if (index == 1) only = new HashSet<string>(_favoriteIds, StringComparer.OrdinalIgnoreCase);
        else if (index is >= 2 and < SubFolderBase) category = (LibraryCategory)(index - 2);
        else if (index >= SubFolderBase && index < UserFolderBase && index - SubFolderBase < SubFolderList.Count)
            (category, subFolder) = SubFolderList[index - SubFolderBase];
        else if (index >= UserFolderBase) only = new HashSet<string>(FolderOfChoice(index)?.Ids ?? [], StringComparer.OrdinalIgnoreCase);

        var found = LibrarySearch.Filter(LibraryFolders.Order(entries.Keys), LibrarySearchText, category, only);
        var wanted = found.Where(item => subFolder is null || LibraryFolders.SubFolderOf(item) == subFolder).Select(item => entries[item]).ToList();

        if (!wanted.SequenceEqual(LibraryEntries))
        {
            LibraryEntries.Clear();
            foreach (var entry in wanted) LibraryEntries.Add(entry);
        }

        IsLibraryEmpty = wanted.Count == 0;
        var noTemplatesYet = LibraryCategoryIndex == 2 + (int)LibraryCategory.MyTemplates && LibrarySearchText.Trim().Length == 0 && _templateEntries.Count == 0;
        var emptyFolder = index >= UserFolderBase && LibrarySearchText.Trim().Length == 0;
        LibraryEmptyText = Texts.Get(noTemplatesYet ? "Panel.Templates.Empty" : emptyFolder ? "Folder.Empty" : "Library.Empty");
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

        ElementValues? values = null;
        if (item.IsParametric && !AskElementValues(item, out values)) return;

        var node = MakeElementNode(item, values);
        node.Name = UniqueName(item.Name);
        node.Transform = Transform.FromPosition(FreeSpot(values is null || item.FootprintFor is null ? item.Footprint : item.FootprintFor(values)));
        _history.Execute(new AddNodeCommand(_document, _document.Root, node));
        SelectOnly([node.Id]);
    }

    /// <summary>A new loose node of the element; one with parameters remembers the values it was made with (F-PAR-05).</summary>
    private static Node MakeElementNode(LibraryItem item, ElementValues? values)
    {
        if (values is null) return item.CreateNode();
        var node = item.CreateNode(values);
        node.Element = new ElementLink(item.Id, new Dictionary<string, string>(values.Expressions, StringComparer.OrdinalIgnoreCase));
        return node;
    }

    /// <summary>
    /// A value of the element section of the properties was typed (F-PAR-05): the parts of the object are rebuilt for the new values, as
    /// one undo step. Returns the error to show in the field, or null.
    /// </summary>
    private string? ApplyElementValue(Node node, ElementParameter parameter, string text)
    {
        if (node.Element is not { } link || LibraryCatalog.Find(link.ElementId) is not { IsParametric: true } item) return Texts.Get("Err.ElementUnknown");

        var variables = _document.ResolveVariables(out _);
        var definition = new ParameterDefinition(parameter.Name, parameter.Label, parameter.Default, parameter.Min, parameter.Max);
        var current = link.Values.TryGetValue(parameter.Name, out var saved) ? saved : parameter.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        var error = TemplateParametersViewModel.Check(text, definition, parameter.Label, current, variables, out var expression);
        if (error is not null) return error;
        if (expression == current) return null;

        var all = new Dictionary<string, string>(link.Values, StringComparer.OrdinalIgnoreCase) { [parameter.Name] = expression };
        var values = ElementValues.Resolve(item.Parameters!, all, variables, out var problem);
        if (values is null) return problem;

        var fresh = item.CreateNode(values);
        if (fresh.GetType() != node.GetType()) return Texts.Get("Err.ElementUnknown");
        _history.Execute(new RebuildElementCommand(_document, node, fresh, new ElementLink(item.Id, new Dictionary<string, string>(values.Expressions, StringComparer.OrdinalIgnoreCase))));
        return null;
    }

    /// <summary>
    /// The form of an element with parameters (F-PAR-05): one field per parameter, preset with the default; a number, a formula or a
    /// project variable is accepted (it stays linked). False when the form was cancelled or a value does not work: nothing is inserted then.
    /// </summary>
    private bool AskElementValues(LibraryItem item, out ElementValues? values)
    {
        values = null;
        var variables = _document.ResolveVariables(out _);
        var fields = new List<FieldViewModel>();
        foreach (var parameter in item.Parameters!)
        {
            var definition = new ParameterDefinition(parameter.Name, parameter.Label, parameter.Default, parameter.Min, parameter.Max);
            var saved = parameter.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var field = new FieldViewModel(parameter.Label, parameter.Unit,
                text => TemplateParametersViewModel.Check(text, definition, parameter.Label, saved, variables, out _));
            field.SetFromModel(saved);
            fields.Add(field);
        }

        var form = new TemplateParametersViewModel(item.Name, fields);
        if (!_dialogs.ShowTemplateParameters(form)) return false;
        if (!form.IsValid)
        {
            SetNotice(Texts.Get("Msg.ElementParametersInvalid"));
            return false;
        }

        var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < fields.Count; i++)
        {
            var parameter = item.Parameters[i];
            var definition = new ParameterDefinition(parameter.Name, parameter.Label, parameter.Default, parameter.Min, parameter.Max);
            var saved = parameter.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (TemplateParametersViewModel.Check(fields[i].Text, definition, parameter.Label, saved, variables, out var expression) is not null
                || ExpressionParser.Evaluate(expression, variables) is not { IsSuccess: true } result)
            {
                SetNotice(Texts.Get("Msg.ElementParametersInvalid"));
                return false;
            }

            expressions[parameter.Name] = expression;
            numbers[parameter.Name] = result.Value;
        }

        values = new ElementValues(expressions, numbers);
        return true;
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

        ElementValues? values = null;
        if (item!.IsParametric && !AskElementValues(item, out values)) return;

        var node = MakeElementNode(item, values);
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
