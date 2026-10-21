using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Draws the picture that is saved with a template (the real one renders the 3D view offscreen).</summary>
public interface ITemplatePreview
{
    /// <summary>A PNG of the items, or null when no picture could be made (the template is saved without one).</summary>
    byte[]? Render(IReadOnlyList<ViewportItem> items);
}

/// <summary>One template in the "My Templates" list, with the actions of its context menu.</summary>
public sealed class TemplateItemViewModel
{
    public TemplateItemViewModel(
        TemplateEntry entry,
        Action<TemplateItemViewModel>? edit = null,
        Action<TemplateItemViewModel>? duplicate = null,
        Action<TemplateItemViewModel>? export = null,
        Action<TemplateItemViewModel>? delete = null)
    {
        Path = entry.Path;
        Info = entry.Info;
        PreviewPng = entry.Preview;
        Tooltip = Texts.Format("Tpl.InsertTip", entry.Info.Name) + (entry.Info.Description.Length > 0 ? "\n" + entry.Info.Description : "");
        EditCommand = new RelayCommand(() => edit?.Invoke(this));
        DuplicateCommand = new RelayCommand(() => duplicate?.Invoke(this));
        ExportCommand = new RelayCommand(() => export?.Invoke(this));
        DeleteCommand = new RelayCommand(() => delete?.Invoke(this));
    }

    public string Path { get; }

    public TemplateInfo Info { get; }

    public string Name => Info.Name;

    public string Category => Info.Category;

    public string Description => Info.Description;

    public Vector3 Size => Info.Size;

    public byte[]? PreviewPng { get; }

    public string Tooltip { get; }

    /// <summary>Name, description, category and tags.</summary>
    public IRelayCommand EditCommand { get; }

    public IRelayCommand DuplicateCommand { get; }

    /// <summary>Writes the template as a file that can be copied to another computer.</summary>
    public IRelayCommand ExportCommand { get; }

    /// <summary>Asks first, then removes the template (objects inserted before stay).</summary>
    public IRelayCommand DeleteCommand { get; }
}

/// <summary>Templates (F-TPL-01 save, F-TPL-02 insert, F-TPL-03 structure stays editable).</summary>
public sealed partial class MainViewModel
{
    private readonly TemplateStore? _templateStore;
    private readonly ITemplatePreview? _templatePreview;

    /// <summary>The saved templates, as shown under "My Templates".</summary>
    public ObservableCollection<TemplateItemViewModel> Templates { get; } = [];

    private void LoadTemplateList()
    {
        if (_templateStore is null) return;
        var entries = _templateStore.List();

        // unchanged: leave the list alone (it is refreshed whenever the window is activated, that must not flicker)
        if (entries.Select(e => Fingerprint(e.Path, e.Info)).SequenceEqual(Templates.Select(t => Fingerprint(t.Path, t.Info)))) return;

        Templates.Clear();
        foreach (var entry in entries) Templates.Add(new TemplateItemViewModel(entry, EditTemplate, DuplicateTemplate, ExportTemplate, DeleteTemplate));
    }

    /// <summary>Reads the template folder again: files that came or went while the program ran (called when the window is activated).</summary>
    [RelayCommand]
    private void RefreshTemplates() => LoadTemplateList();

    private static string Fingerprint(string path, TemplateInfo info) =>
        string.Join("\u001f", path, info.Name, info.Description, info.Category, string.Join("\u001e", info.Tags), info.CreatedUtc.Ticks);

    private bool CanImportTemplate() => _templateStore is not null;

    /// <summary>The dialog for name, description, category and tags of a template; the objects and the picture stay as they are.</summary>
    private void EditTemplate(TemplateItemViewModel item)
    {
        if (_templateStore is null) return;
        var dialog = new SaveTemplateViewModel(_templateStore, item.Info, item.Path);
        if (!_dialogs.ShowSaveTemplate(dialog)) return;
        if (!dialog.IsValid)
        {
            SetNotice(dialog.Suggestion is { } next
                ? Texts.Format("Msg.TemplateTaken", TemplateNames.Clean(dialog.Name), next)
                : dialog.Error ?? Texts.Get("Tpl.Err.NameRequired"));
            return;
        }

        var info = dialog.Build(item.Info.Size) with { CreatedUtc = item.Info.CreatedUtc };
        if (!TryChangeLibrary(() => _templateStore.UpdateInfo(item.Path, info))) return;
        LoadTemplateList();
        SetNotice(Texts.Format("Msg.TemplateChanged", info.Name));
    }

    private void DuplicateTemplate(TemplateItemViewModel item)
    {
        if (_templateStore is null) return;
        var path = "";
        if (!TryChangeLibrary(() => path = _templateStore.Duplicate(item.Path))) return;
        LoadTemplateList();
        SetNotice(Texts.Format("Msg.TemplateDuplicated", Templates.FirstOrDefault(t => t.Path == path)?.Name ?? item.Name));
    }

    private void DeleteTemplate(TemplateItemViewModel item)
    {
        if (_templateStore is null) return;
        if (!_dialogs.Confirm(Texts.Format("Msg.TemplateDeleteAsk", item.Name))) return;
        if (!TryChangeLibrary(() => _templateStore.Delete(item.Path))) return;
        LoadTemplateList();
        SetNotice(Texts.Format("Msg.TemplateDeleted", item.Name));
    }

    /// <summary>Writes the template to a file of the user's choice (a USB stick, a mail attachment).</summary>
    private void ExportTemplate(TemplateItemViewModel item)
    {
        if (_templateStore is null) return;
        var invalid = Path.GetInvalidFileNameChars();
        var stem = new string([.. item.Name.Select(c => Array.IndexOf(invalid, c) >= 0 ? '_' : c)]).Trim();
        var destination = _dialogs.PickSaveFile(
            Texts.Get("Dlg.TemplateExportTitle"), Texts.Get("Dlg.TemplateFilter"), (stem.Length > 0 ? stem : "template") + TemplateFile.Extension);
        if (destination is null) return;
        if (!TryChangeLibrary(() => _templateStore.Export(item.Path, destination))) return;
        SetNotice(Texts.Format("Msg.TemplateExported", item.Name));
    }

    /// <summary>Takes a template file from somewhere else into the library; a name that is taken is renamed, a bad file is reported.</summary>
    [RelayCommand(CanExecute = nameof(CanImportTemplate))]
    private void ImportTemplate()
    {
        if (_templateStore is null) return;
        var source = _dialogs.PickOpenFile(Texts.Get("Dlg.TemplateImportTitle"), Texts.Get("Dlg.TemplateFilter"));
        if (source is null) return;

        TemplateImportResult result;
        try
        {
            result = _templateStore.Import(source);
        }
        catch (Exception ex) when (ex is TemplateFormatException or FileNotFoundException)
        {
            _dialogs.ShowError(Texts.Format("Msg.TemplateImportFailed", ex.Message));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogs.ShowError(Texts.Format("Msg.TemplateActionFailed", ex.Message));
            return;
        }

        LoadTemplateList();
        SetNotice(Texts.Format(result.Renamed ? "Msg.TemplateImportedRenamed" : "Msg.TemplateImported", result.Info.Name));
    }

    /// <summary>Runs a change of the template folder; problems with the files are shown in the status bar instead of crashing.</summary>
    private bool TryChangeLibrary(Action change)
    {
        try
        {
            change();
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or TemplateFormatException)
        {
            SetNotice(Texts.Format("Msg.TemplateActionFailed", ex.Message));
            LoadTemplateList();
            return false;
        }
    }

    private bool CanSaveTemplate() => _templateStore is not null && HasSelection();

    /// <summary>
    /// The project variables that the nodes use in their parameters, and those that these need in turn (a template takes along what it
    /// needs, not the whole table), in the order of the project table.
    /// </summary>
    private List<KeyValuePair<string, string>> UsedVariables(IEnumerable<Node> nodes)
    {
        var needed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>();

        void Use(string expression)
        {
            foreach (var name in ExpressionParser.UsedNames(expression))
            {
                if (_document.Variables.ContainsKey(name) && needed.Add(name)) queue.Enqueue(name);
            }
        }

        foreach (var node in nodes)
        {
            foreach (var primitive in node.Descendants().Prepend(node).OfType<ParametricNode>())
            {
                foreach (var expression in primitive.Parameters.Values) Use(expression);
            }
        }

        while (queue.Count > 0) Use(_document.Variables[queue.Dequeue()]);
        return [.. _document.Variables.Where(v => needed.Contains(v.Key))];
    }

    /// <summary>
    /// Saves the selected objects under a name (the dialog asks). They are saved with their whole structure, normalised to the center of
    /// the plate and the floor (X and Y of the middle of the selection at 0, lowest point at Z = 0), with the size and a picture.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSaveTemplate))]
    private void SaveTemplate()
    {
        if (_templateStore is null || _lastResult is null) return;
        var nodes = SelectedTopLevel();
        if (nodes.Count == 0) return;

        var ids = new HashSet<Guid>();
        foreach (var node in nodes) ids.UnionWith(node.Descendants().Select(d => d.Id).Prepend(node.Id));
        var bounds = BoundsOf(_lastResult, ids, includeGhosts: false) ?? BoundsOf(_lastResult, ids, includeGhosts: true);
        if (bounds is not { } box)
        {
            SetNotice(Texts.Get("Msg.TemplateNothing"));
            return;
        }

        var dialog = new SaveTemplateViewModel(_templateStore, nodes.Count == 1 ? nodes[0].Name : Texts.Get("Tpl.DefaultName"));
        if (!_dialogs.ShowSaveTemplate(dialog)) return;
        if (!dialog.IsValid)
        {
            // the window only confirms a valid description; this is a safety net for anything else that confirms
            SetNotice(dialog.Suggestion is { } next
                ? Texts.Format("Msg.TemplateTaken", TemplateNames.Clean(dialog.Name), next)
                : dialog.Error ?? Texts.Get("Tpl.Err.NameRequired"));
            return;
        }

        var size = box.Max - box.Min;
        var shift = Matrix4x4.CreateTranslation(-(box.Min.X + box.Max.X) / 2, -(box.Min.Y + box.Max.Y) / 2, -box.Min.Z);

        var template = new FormwerkDocument();
        foreach (var (name, expression) in UsedVariables(nodes)) template.Variables[name] = expression;
        var meshes = new MeshStore();
        foreach (var node in nodes)
        {
            foreach (var (id, mesh) in MeshesOf(node))
            {
                if (!meshes.Contains(id)) meshes.Add(id, mesh);
            }

            var copy = node.Clone(newIds: false);
            var world = TransformOps.LocalToWorld(node) * shift;
            copy.Transform = Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation)
                ? new Transform(translation, Quaternion.Normalize(rotation), scale)
                : Transform.FromPosition(translation);
            new AddNodeCommand(template, template.Root, copy).Do();
        }

        var preview = _templatePreview?.Render([.. ViewportItems.Where(i => ids.Contains(i.NodeId))]);
        var info = dialog.Build(size);
        try
        {
            _templateStore.Save(info, template, meshes, preview);
        }
        catch (InvalidOperationException) // somebody saved the same name in the meantime
        {
            SetNotice(Texts.Format("Msg.TemplateTaken", info.Name, _templateStore.SuggestName(info.Name, info.Category)));
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetNotice(Texts.Format("Msg.TemplateSaveFailed", ex.Message));
            return;
        }

        LoadTemplateList();
        SetNotice(Texts.Format("Msg.TemplateSaved", info.Name));
    }

    /// <summary>
    /// Puts a template into the scene at a free spot on the plate, on the floor. One object comes back with the template name; several
    /// objects come back as a group with that name. Groups, holes and boolean nodes inside stay as they were, so everything is still
    /// editable. One undo step.
    /// </summary>
    [RelayCommand]
    private void InsertTemplate(TemplateItemViewModel? item)
    {
        if (item is null) return;

        var meshes = new MeshStore();
        FormwerkDocument template;
        try
        {
            template = TemplateFile.Load(item.Path, meshes);
        }
        catch (Exception ex) when (ex is TemplateFormatException or IOException or UnauthorizedAccessException)
        {
            SetNotice(Texts.Format("Msg.TemplateLoadFailed", ex.Message));
            return;
        }

        var roots = template.Root.Children.ToList();
        if (roots.Count == 0) return;
        foreach (var mesh in template.Root.Descendants().OfType<MeshNode>())
        {
            if (!Meshes.Contains(mesh.MeshId) && meshes.Get(mesh.MeshId) is { } data) Meshes.Add(mesh.MeshId, data);
        }

        // the variables of the template that the project does not have yet come along (appended to the table); one that it has keeps
        // the value of the project, and the user is told when the template meant another one
        var added = new List<KeyValuePair<string, string>>();
        var differing = new List<string>();
        foreach (var (name, expression) in template.Variables)
        {
            if (!_document.Variables.TryGetValue(name, out var existing)) added.Add(new(name, expression));
            else if (existing.Trim() != expression.Trim()) differing.Add(name);
        }

        var spot = FreeSpot(item.Size);
        var commands = new List<IDocumentCommand>();
        if (added.Count > 0) commands.Add(new SetVariablesCommand(_document, [.. _document.Variables, .. added], "Add variables"));
        Guid inserted;
        if (roots.Count == 1)
        {
            var copy = roots[0].Clone(newIds: true);
            copy.Name = UniqueName(item.Name);
            var t = copy.Transform;
            copy.Transform = new Transform(t.Position + spot, t.Rotation, t.Scale);
            commands.Add(new AddNodeCommand(_document, _document.Root, copy));
            inserted = copy.Id;
        }
        else
        {
            var group = new GroupNode(UniqueName(item.Name)) { Transform = Transform.FromPosition(spot) };
            commands.Add(new AddNodeCommand(_document, _document.Root, group));
            foreach (var root in roots) commands.Add(new AddNodeCommand(_document, group, root.Clone(newIds: true)));
            inserted = group.Id;
        }

        _history.Execute(new CompositeCommand(Texts.Format("Name.InsertTemplate", item.Name), commands));
        SelectOnly([inserted]);
        if (differing.Count > 0) SetNotice(Texts.Format("Msg.TemplateVariableKept", string.Join(", ", differing)));
    }
}
