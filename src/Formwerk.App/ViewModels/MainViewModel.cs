using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private readonly FormwerkDocument _document = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator;
    private readonly object _evaluatorLock = new();
    private readonly IDialogService _dialogs;
    private readonly AutosaveStore? _autosave;
    private readonly Dictionary<Guid, NodeItemViewModel> _items = [];
    private readonly HashSet<Guid> _selected = [];
    private readonly Dictionary<Guid, string> _errors = [];

    private CancellationTokenSource? _cts;
    private int _version;
    private Task _latestEvaluation = Task.CompletedTask;
    private EvaluationResult? _lastResult;
    private bool _syncingSelection;
    private bool _rebuildingList;
    private int _historyIndex;
    private int _savedIndex;
    private IDocumentCommand? _savedCommand;
    private bool _forceDirty;
    private bool _replacing;
    private string? _notice;
    private bool _isDirty;
    private string? _filePath;

    /// <param name="autosave">Where autosaves go; null disables autosave and recovery (used by tests).</param>
    /// <param name="meshes">Imported meshes; a new empty store when null.</param>
    /// <param name="userSettings">Where the program settings are remembered; null keeps them for this session only (used by tests).</param>
    /// <param name="slicerLauncher">Starts the slicer program; null means starting programs is not possible (used by tests).</param>
    public MainViewModel(IGeometryKernel kernel, IDialogService dialogs, AutosaveStore? autosave = null, MeshStore? meshes = null,
        UserSettingsStore? userSettings = null, ISlicerLauncher? slicerLauncher = null, TemplateStore? templates = null,
        ITemplatePreview? templatePreview = null)
    {
        _templateStore = templates;
        _templatePreview = templatePreview;
        _slicer = slicerLauncher ?? new NoSlicerLauncher();
        Meshes = meshes ?? new MeshStore();
        _evaluator = new SceneEvaluator(kernel, Meshes);
        _dialogs = dialogs;
        _autosave = autosave;
        Shapes = [.. ShapeLibrary.All.Select(g => new ShapeEntryViewModel(g.Id, g.DisplayName))];
        _hintText = Texts.Get("Hint.Default");
        _statusText = Texts.Get("Status.Ready");
        _settingsStore = userSettings;
        LoadUserSettings();
        ApplyDefaultsTo(_document);

        _document.Changed += (_, _) => OnDocumentChanged();
        _history.Changed += (_, _) =>
        {
            if (!_replacing) _notice = null; // the next edit ends the message
            RebuildHistory();
            UpdateDirty();
        };
        _history.Executed += (_, command) => ExpectFloorCheck(command);
        RebuildHistory();
        LoadTemplateList();
        _ = RequestEvaluation();
    }

    public FormwerkDocument Document => _document;

    private VariablesViewModel? _variables;

    /// <summary>The table of project variables (F-PAR-03).</summary>
    public VariablesViewModel Variables => _variables ??= new VariablesViewModel(_document, _history);

    /// <summary>The meshes of imported objects (saved with the project).</summary>
    public MeshStore Meshes { get; }

    /// <summary>Raised when a menu command wants the viewport to change the camera.</summary>
    public event EventHandler<ViewPreset>? ViewPresetRequested;

    public event EventHandler? FrameSelectionRequested;

    [RelayCommand]
    private void SetView(ViewPreset preset) => ViewPresetRequested?.Invoke(this, preset);

    [RelayCommand]
    private void FrameSelection() => FrameSelectionRequested?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<ShapeEntryViewModel> Shapes { get; }

    public ObservableCollection<NodeItemViewModel> Nodes { get; } = [];

    public ObservableCollection<HistoryEntryViewModel> HistoryEntries { get; } = [];

    /// <summary>Build volume of the project; the viewport draws the plate from it (F-SET-01).</summary>
    public Vector3 BuildVolume => _document.Settings.BuildVolume;

    [ObservableProperty]
    public partial IReadOnlyList<ViewportItem> ViewportItems { get; set; } = [];

    [ObservableProperty]
    public partial IReadOnlyCollection<Guid> SelectedIds { get; set; } = new HashSet<Guid>();

    [ObservableProperty]
    public partial PropertiesViewModel? Properties { get; set; }

    [ObservableProperty]
    public partial string PropertiesMessage { get; set; } = Texts.Get("Props.NothingSelected");

    private string _hintText;

    public string HintText
    {
        get => _hintText;
        private set => SetProperty(ref _hintText, value);
    }

    private string _statusText;

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>Selected index of the history list; setting it jumps to that state (E-01).</summary>
    public int HistorySelectedIndex
    {
        get => _historyIndex;
        set
        {
            if (value < 0 || value == _historyIndex || value > _history.Entries.Count) return;
            _history.JumpTo(value);
        }
    }

    // ---- files: new, open, save, autosave ------------------------------------------------------

    /// <summary>Path of the project file, or null for a project that was never saved.</summary>
    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (SetProperty(ref _filePath, value)) OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>True when there are changes that are not in the file on disk.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value)) OnPropertyChanged(nameof(WindowTitle));
        }
    }

    public string DocumentName => FilePath is null ? Texts.Get("Title.Untitled") : Path.GetFileName(FilePath);

    public string WindowTitle => Texts.Format("Title.Format", DocumentName, IsDirty ? "*" : "");

    /// <summary>
    /// The project is clean when the history is at the position that was saved and the command there is the
    /// same one (a new command after an undo replaces the saved branch, which makes it dirty for good).
    /// </summary>
    private void UpdateDirty()
    {
        var atSaved = _history.CurrentIndex == _savedIndex
                      && (_savedIndex == 0 || (_savedIndex <= _history.Entries.Count && _history.Entries[_savedIndex - 1] == _savedCommand));
        IsDirty = _forceDirty || !atSaved;
        if (!IsDirty && !_replacing) _autosave?.Clear(); // the file on disk equals the current state; an autosave would only be stale
    }

    private void MarkSaved()
    {
        _forceDirty = false;
        _savedIndex = _history.CurrentIndex;
        _savedCommand = _savedIndex > 0 ? _history.Entries[_savedIndex - 1] : null;
        UpdateDirty();
    }

    /// <summary>Replaces the whole project. The history starts empty, so loading cannot be undone.</summary>
    private void ReplaceDocument(FormwerkDocument document, string? path, bool unsavedWork = false)
    {
        _replacing = true; // the autosave must survive while we swap the content
        try
        {
            _selected.Clear();
            _history.Clear();
            OpenContainerId = null;
            _document.ReplaceWith(document);
            FilePath = path;
            MarkSaved();
            if (unsavedWork)
            {
                _forceDirty = true; // recovered work is not in any file yet
                UpdateDirty();
            }
        }
        finally
        {
            _replacing = false;
        }

        AfterSelectionChanged();
    }

    /// <summary>Asks what to do with unsaved changes. False means the user cancelled (or saving failed).</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!IsDirty) return true;
        return _dialogs.ConfirmUnsavedChanges(DocumentName) switch
        {
            UnsavedChoice.Save => Save(),
            UnsavedChoice.Discard => true,
            _ => false,
        };
    }

    /// <summary>Called when the window closes. False cancels closing.</summary>
    public bool TryClose()
    {
        if (!ConfirmDiscardChanges()) return false;
        _autosave?.Clear(); // a deliberate exit: nothing to recover
        return true;
    }

    [RelayCommand]
    private void New()
    {
        if (!ConfirmDiscardChanges()) return;
        _autosave?.Clear();
        Meshes.Clear();
        ReplaceDocument(NewDocument(), null);
        ClearNotice();
    }

    [RelayCommand]
    private void Open()
    {
        if (!ConfirmDiscardChanges()) return;
        var path = _dialogs.PickOpenFile(Texts.Get("Dlg.OpenTitle"), Texts.Get("Dlg.ProjectFilter"));
        if (path is not null) OpenPath(path);
    }

    /// <summary>Opens a project file without asking about unsaved changes. Shows a readable error and keeps the current project on failure.</summary>
    public bool OpenPath(string path)
    {
        FormwerkDocument loaded;
        var loadedMeshes = new MeshStore();
        try
        {
            loaded = ProjectFile.Load(path, loadedMeshes);
        }
        catch (ProjectFormatException ex)
        {
            _dialogs.ShowError(Texts.Format("Dlg.OpenFailed", ex.Message));
            return false;
        }

        _autosave?.Clear();
        Meshes.ReplaceWith(loadedMeshes);
        ReplaceDocument(loaded, path);
        SetNotice(Texts.Format("Msg.Opened", path));
        return true;
    }

    [RelayCommand]
    private void SaveProject() => Save();

    [RelayCommand]
    private void SaveProjectAs() => SaveAs();

    /// <summary>Saves to the current file, or asks for a file name if there is none. False when cancelled or failed.</summary>
    public bool Save() => FilePath is null ? SaveAs() : SaveTo(FilePath);

    public bool SaveAs()
    {
        var suggestion = FilePath is null ? Texts.Get("Title.Untitled") + ProjectFile.Extension : Path.GetFileName(FilePath);
        var path = _dialogs.PickSaveFile(Texts.Get("Dlg.SaveTitle"), Texts.Get("Dlg.ProjectFilter"), suggestion);
        if (path is null) return false;
        if (!path.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) path += ProjectFile.Extension;
        return SaveTo(path);
    }

    private bool SaveTo(string path)
    {
        try
        {
            ProjectFile.SaveAtomic(path, _document, Meshes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException)
        {
            _dialogs.ShowError(Texts.Format("Dlg.SaveFailed", ex.Message));
            return false;
        }

        FilePath = path;
        MarkSaved();
        _autosave?.Clear();
        SetNotice(Texts.Format("Msg.Saved", path));
        return true;
    }

    /// <summary>
    /// Writes an autosave copy if there is unsaved work (E-05). The copy is taken on the calling (UI) thread and
    /// written on a background thread. Returns true when a file was written.
    /// </summary>
    public async Task<bool> AutosaveAsync()
    {
        if (_autosave is null || !IsDirty) return false;
        var snapshot = _document.Snapshot();
        var original = FilePath;
        try
        {
            await Task.Run(() => _autosave.Write(snapshot, original, Meshes)).ConfigureAwait(true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // autosave is best effort; the user's own save still works
        }
    }

    /// <summary>
    /// At startup: if an autosave from an unclean exit exists, offers to recover it. Returns true when work was recovered.
    /// </summary>
    public bool OfferRecovery()
    {
        if (_autosave is null || !_autosave.TryGetRecovery(out var info)) return false;

        var name = info.OriginalPath is null ? null : Path.GetFileName(info.OriginalPath);
        if (!_dialogs.ConfirmRecovery(info.SavedUtc.ToLocalTime(), name))
        {
            _autosave.Clear();
            return false;
        }

        FormwerkDocument recovered;
        var recoveredMeshes = new MeshStore();
        try
        {
            recovered = _autosave.Load(recoveredMeshes);
        }
        catch (ProjectFormatException ex)
        {
            _dialogs.ShowError(Texts.Format("Dlg.OpenFailed", ex.Message));
            _autosave.Clear();
            return false;
        }

        Meshes.ReplaceWith(recoveredMeshes);
        ReplaceDocument(recovered, info.OriginalPath, unsavedWork: true);
        SetNotice(Texts.Get("Msg.Recovered"));
        return true;
    }

    // ---- evaluation --------------------------------------------------------------------------

    /// <summary>Completes when the most recent evaluation has been applied (for tests and shutdown).</summary>
    public async Task WaitForEvaluationAsync()
    {
        Task current;
        do
        {
            current = _latestEvaluation;
            await current.ConfigureAwait(true);
        }
        while (current != _latestEvaluation);
    }

    private Task RequestEvaluation()
    {
        _latestEvaluation = EvaluateAsync();
        return _latestEvaluation;
    }

    private async Task EvaluateAsync()
    {
        var version = ++_version;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var snapshot = _document.Snapshot();
        IsBusy = true;
        UpdateStatus();

        EvaluationResult? result = null;
        try
        {
            // The evaluator keeps caches and is not thread-safe; an older, cancelled run finishes quickly and releases the lock.
            result = await Task.Run(() =>
            {
                lock (_evaluatorLock)
                {
                    token.ThrowIfCancellationRequested();
                    return _evaluator.Evaluate(snapshot, token);
                }
            }, CancellationToken.None).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // A newer change superseded this run; the previous result stays visible.
        }
        catch (Exception ex) when (version == _version)
        {
            // Never crash the UI because of a geometry problem; keep the last valid result (F-BOO-05).
            IsBusy = false;
            StatusText = Texts.Format("Status.Problems", 1, ex.Message);
            return;
        }

        if (version != _version) return;
        IsBusy = false;
        if (result is not null)
        {
            ApplyResult(result);
            KeepOnFloor();
        }

        UpdateStatus();
    }

    private void ApplyResult(EvaluationResult result)
    {
        // The result of a finished drag already contains the new position: the preview has done its job (not while a new drag runs).
        if (!_dragging && PreviewTransforms is not null) PreviewTransforms = null;
        _lastResult = result;
        _outside = FindOutside(result);
        BuildViewportItems(result);

        _errors.Clear();
        foreach (var error in result.Errors) _errors[error.Node.Id] = error.Message;
        foreach (var item in _items.Values)
        {
            item.ErrorText = _errors.GetValueOrDefault(item.Id);
            item.IsOutside = _outside.Contains(item.Id);
        }

        Properties?.Refresh();
        UpdatePivot();
    }

    /// <summary>A message (Saved, Opened, ...) that stays in the status bar until the next edit.</summary>
    private void SetNotice(string text)
    {
        _notice = text;
        UpdateStatus();
    }

    private void ClearNotice()
    {
        _notice = null;
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsBusy)
        {
            StatusText = Texts.Get("Status.Computing");
            return;
        }

        if (_errors.Count > 0)
        {
            StatusText = Texts.Format("Status.Problems", _errors.Count, _errors.Values.First());
            return;
        }

        if (_notice is not null)
        {
            StatusText = WithOutsideWarning(_notice);
            return;
        }

        var solids = _lastResult?.Parts.Where(p => p.Kind == PartKind.Solid).ToList() ?? [];
        StatusText = WithOutsideWarning(_lastResult is null
            ? Texts.Get("Status.Ready")
            : Texts.Format("Status.Scene", _document.Root.Descendants().Count(), solids.Sum(p => (long)p.Mesh.TriangleCount)));
    }

    // ---- document -> UI ----------------------------------------------------------------------

    private void OnDocumentChanged()
    {
        if (OpenContainerId is { } openId && _document.FindNode(openId) is null) SetOpenContainer(null); // the group is gone (undo, new, open)
        OnPropertyChanged(nameof(BuildVolume)); // a settings change, New or Open may have changed it
        // While a handle is dragged only transforms change: keep the list and the selection untouched (no flicker).
        if (!_dragging)
        {
            RebuildNodeList();
            PruneSelection();
        }

        Properties?.Refresh();
        if (!_dragging) QueueRepairCheck(); // a repair, its undo or opening a project changes the mesh of an object
        _ = RequestEvaluation();
    }

    private void RebuildNodeList()
    {
        _rebuildingList = true;
        try
        {
            var alive = new HashSet<Guid>();
            var flat = new List<NodeItemViewModel>();
            void Visit(Node node, int depth)
            {
                alive.Add(node.Id);
                if (!_items.TryGetValue(node.Id, out var item))
                {
                    item = new NodeItemViewModel(node, depth, SetVisible, OnItemSelectionChanged);
                    _items[node.Id] = item;
                }
                else
                {
                    item.Update(node, depth);
                }

                item.ErrorText = _errors.GetValueOrDefault(node.Id);
                item.IsOutside = _outside.Contains(node.Id);
                item.SetSelectedSilently(_selected.Contains(node.Id));
                flat.Add(item);
                foreach (var child in node.Children) Visit(child, depth + 1);
            }

            foreach (var child in _document.Root.Children) Visit(child, 0);
            foreach (var gone in _items.Keys.Where(id => !alive.Contains(id)).ToList()) _items.Remove(gone);

            Nodes.Clear();
            foreach (var item in flat) Nodes.Add(item);
        }
        finally
        {
            _rebuildingList = false;
        }
    }

    private void RebuildHistory()
    {
        _historyIndex = _history.CurrentIndex;
        HistoryEntries.Clear();
        HistoryEntries.Add(new HistoryEntryViewModel(0, Texts.Get("Hist.Initial"), isFuture: false));
        for (var i = 0; i < _history.Entries.Count; i++)
            HistoryEntries.Add(new HistoryEntryViewModel(i + 1, _history.Entries[i].Name, isFuture: i + 1 > _history.CurrentIndex));
        OnPropertyChanged(nameof(HistorySelectedIndex));
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    // ---- selection ---------------------------------------------------------------------------

    public IReadOnlyCollection<Guid> Selection => _selected;

    public void SelectOnly(IEnumerable<Guid> ids)
    {
        _selected.Clear();
        foreach (var id in ids) _selected.Add(id);
        AfterSelectionChanged();
    }

    private void OnItemSelectionChanged(NodeItemViewModel item, bool selected)
    {
        if (_syncingSelection || _rebuildingList) return;
        if (selected) _selected.Add(item.Id);
        else _selected.Remove(item.Id);
        AfterSelectionChanged(syncList: false);
    }

    private void PruneSelection()
    {
        var removed = _selected.RemoveWhere(id => !_items.ContainsKey(id));
        if (removed > 0) AfterSelectionChanged();
        else RaiseCommandStates();
    }

    private void AfterSelectionChanged(bool syncList = true)
    {
        if (syncList)
        {
            _syncingSelection = true;
            try
            {
                foreach (var item in _items.Values) item.SetSelectedSilently(_selected.Contains(item.Id));
            }
            finally
            {
                _syncingSelection = false;
            }
        }

        SelectedIds = new HashSet<Guid>(_selected);
        UpdatePivot();

        if (_selected.Count == 1 && _document.FindNode(_selected.First()) is { } node)
        {
            if (Properties?.NodeId != node.Id) Properties = new PropertiesViewModel(_document, _history, node, SizeOf, RememberOffset);
            else Properties.Refresh();
            PropertiesMessage = "";
            HintText = Texts.Get("Hint.Selected");
        }
        else
        {
            Properties = null;
            PropertiesMessage = _selected.Count == 0
                ? Texts.Get("Props.NothingSelected")
                : Texts.Format("Props.MultipleSelected", _selected.Count);
            HintText = _selected.Count == 0 ? Texts.Get("Hint.Default") : Texts.Get("Hint.Selected");
        }

        RefreshScaleHint();
        RaiseCommandStates();
        QueueRepairCheck();
    }

    /// <summary>Click in the viewport. Operands of a boolean or merge are selected as a whole (outermost container).</summary>
    [RelayCommand]
    private void Pick(PickRequest? request)
    {
        if (request is null) return;
        if (request.NodeId is not { } id)
        {
            if (!request.Additive) SelectOnly([]);
            return;
        }

        if (OpenContainer is { } open && _document.FindNode(id) is { } hit)
        {
            if (hit == open) return;
            if (!hit.IsDescendantOf(open)) SetOpenContainer(null); // a click on a dimmed object leaves the group and selects it
        }

        var node = SelectableFor(id);
        if (node is null) return;

        if (request.Additive)
        {
            if (!_selected.Add(node.Id)) _selected.Remove(node.Id);
            AfterSelectionChanged();
        }
        else
        {
            SelectOnly([node.Id]);
        }
    }

    /// <summary>
    /// The node a click or a rectangle on the displayed part <paramref name="id"/> selects: the part itself, or for an operand of a
    /// boolean or merge the outermost such container (they are selected as a whole). Holes (ghosts) and the parts of plain groups
    /// are selected individually. Null for an id that is not in the document.
    /// </summary>
    private Node? SelectableFor(Guid id)
    {
        var node = _document.FindNode(id);
        if (node is null) return null;
        if (OpenContainer is { } open)
        {
            // inside an open group the direct part of it is selected; what is outside cannot be selected here
            if (node == open || !node.IsDescendantOf(open)) return null;
            while (node.Parent is { } parent && parent != open) node = parent;
            return node;
        }

        var isGhost = ViewportItems.Any(i => i.NodeId == id && i.Kind == ViewportItemKind.Ghost);
        if (!isGhost)
        {
            while (node.Parent is BooleanNode or MergeNode or PatternNode) node = node.Parent;
        }

        return node;
    }

    /// <summary>
    /// A selection rectangle was dragged in the viewport (F-SEL-01): selects the objects it took, or adds them with Ctrl/Shift. An
    /// empty rectangle clears the selection unless it adds. Unlike a click it never toggles an object off.
    /// </summary>
    [RelayCommand]
    private void Marquee(MarqueeRequest? request)
    {
        if (request is null) return;
        var ids = request.NodeIds.Select(SelectableFor).OfType<Node>().Select(n => n.Id).Distinct().ToList();
        if (request.Additive)
        {
            if (ids.Count == 0) return;
            foreach (var id in ids) _selected.Add(id);
            AfterSelectionChanged();
        }
        else
        {
            SelectOnly(ids);
        }
    }

    private string SizeOf(Guid id)
    {
        var node = _document.FindNode(id);
        if (node is null || _lastResult is null) return "";
        var ids = new HashSet<Guid>(node.Descendants().Select(n => n.Id)) { id };
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var part in _lastResult.Parts.Where(p => p.Kind == PartKind.Solid && ids.Contains(p.Source.Id)))
        {
            if (part.Mesh.IsEmpty) continue;
            var bounds = part.Mesh.ComputeBounds();
            min = Vector3.Min(min, bounds.Min);
            max = Vector3.Max(max, bounds.Max);
            any = true;
        }

        if (!any) return "";
        var size = max - min;
        return string.Create(CultureInfo.InvariantCulture, $"{size.X:0.#} × {size.Y:0.#} × {size.Z:0.#} mm");
    }

    private void SetVisible(Guid id, bool visible)
    {
        if (_rebuildingList) return;
        if (_document.FindNode(id) is { } node && node.Visible != visible)
            _history.Execute(new SetVisibleCommand(_document, node, visible));
    }

    // ---- commands ----------------------------------------------------------------------------

    private IReadOnlyList<Node> SelectedNodes() =>
        [.. _selected.Select(_document.FindNode).OfType<Node>().Where(n => n != _document.Root)];

    /// <summary>Selected nodes, minus those whose ancestor is selected too.</summary>
    private List<Node> SelectedTopLevel()
    {
        var nodes = SelectedNodes();
        return [.. nodes.Where(n => !nodes.Any(other => other != n && n.IsDescendantOf(other)))];
    }

    private bool SameParent(IReadOnlyCollection<Node> nodes) =>
        nodes.Count > 0 && nodes.Select(n => n.Parent).Distinct().Count() == 1;

    private void RaiseCommandStates()
    {
        DeleteCommand.NotifyCanExecuteChanged();
        DuplicateCommand.NotifyCanExecuteChanged();
        CopyCommand.NotifyCanExecuteChanged();
        GroupCommand.NotifyCanExecuteChanged();
        UngroupCommand.NotifyCanExecuteChanged();
        MergeCommand.NotifyCanExecuteChanged();
        FlattenCommand.NotifyCanExecuteChanged();
        SaveTemplateCommand.NotifyCanExecuteChanged();
        Rotate90Command.NotifyCanExecuteChanged();
        FaceOnFloorCommand.NotifyCanExecuteChanged();
        DrillCommand.NotifyCanExecuteChanged();
        LinearPatternCommand.NotifyCanExecuteChanged();
        BooleanCommand.NotifyCanExecuteChanged();
        ToggleHoleCommand.NotifyCanExecuteChanged();
        DropToFloorCommand.NotifyCanExecuteChanged();
        ExportStlCommand.NotifyCanExecuteChanged();
        Export3mfCommand.NotifyCanExecuteChanged();
        ExportObjCommand.NotifyCanExecuteChanged();
        OpenInSlicerCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void AddShape(string? shapeId)
    {
        var generator = shapeId is null ? null : ShapeLibrary.Find(shapeId);
        if (generator is null) return;

        var node = new PrimitiveNode(generator.Id, UniqueName(generator.DisplayName));
        foreach (var definition in generator.Parameters)
            node.Parameters[definition.Name] = definition.Default.ToString("R", CultureInfo.InvariantCulture);

        node.Transform = Transform.FromPosition(FreeSpot(generator.Generate(new Dictionary<string, double>()).ComputeBounds().Size));
        _history.Execute(new AddNodeCommand(_document, _document.Root, node));
        SelectOnly([node.Id]);
    }

    /// <summary>
    /// First free position for a new shape: along the X axis (0, -30, +30, -60, ...), then, when the row is full up to the edge of the
    /// build plate, in the next rows (Y = +50, -50, +100, ...). The shape does not overlap what is already in the scene, so that new
    /// objects never hide each other, and stays inside the build volume if there is room. It rests on the plate (Z = 0).
    /// If nothing fits (a shape bigger than the plate) it goes to the first spot along the X axis that is free.
    /// </summary>
    private Vector3 FreeSpot(Vector3 size)
    {
        const float margin = 6;
        var volume = _document.Settings.BuildVolume;
        var occupied = (_lastResult?.Parts ?? [])
            .Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty)
            .Select(p => p.Mesh.ComputeBounds())
            .ToList();

        bool Free(float x, float y) =>
            !occupied.Any(b => b.Min.X < x + size.X / 2 + margin && b.Max.X > x - size.X / 2 - margin
                               && b.Min.Y < y + size.Y / 2 + margin && b.Max.Y > y - size.Y / 2 - margin);

        bool Inside(float x, float y) =>
            Math.Abs(x) + size.X / 2 <= volume.X / 2 && Math.Abs(y) + size.Y / 2 <= volume.Y / 2;

        for (var row = 0; row < 40; row++)
        {
            var y = (row + 1) / 2 * 50f * (row % 2 == 0 ? 1 : -1);
            for (var step = 0; step < 200; step++)
            {
                var x = (step + 1) / 2 * 30f * (step % 2 == 0 ? 1 : -1);
                if (Math.Abs(x) > volume.X / 2) break; // the row is full up to the edge of the plate
                if (Inside(x, y) && Free(x, y)) return new Vector3(x, y, 0);
            }
        }

        for (var step = 0; step < 200; step++)
        {
            var x = (step + 1) / 2 * 30f * (step % 2 == 0 ? 1 : -1);
            if (Free(x, 0)) return new Vector3(x, 0, 0);
        }

        return Vector3.Zero;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo() => _history.Undo();

    private bool CanUndo() => _history.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo() => _history.Redo();

    private bool CanRedo() => _history.CanRedo;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Delete()
    {
        foreach (var node in SelectedTopLevel()) _history.Execute(new RemoveNodeCommand(_document, node));
    }

    private bool HasSelection() => _selected.Count > 0;

    [RelayCommand(CanExecute = nameof(CanGroup))]
    private void Group() => Wrap(new GroupNode(UniqueName(Texts.Get("Name.Group"))));

    private bool CanGroup() => SameParent(SelectedTopLevel());

    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void Merge() => Wrap(new MergeNode(UniqueName(Texts.Get("Name.Merge"))));

    private bool CanMerge() => SelectedTopLevel() is { Count: >= 2 } nodes && SameParent(nodes);

    /// <summary>Parameter is a <see cref="BooleanOperation"/>.</summary>
    [RelayCommand(CanExecute = nameof(CanMerge))]
    private void Boolean(BooleanOperation operation) =>
        Wrap(new BooleanNode(UniqueName(operation.ToString()), operation));

    private void Wrap(Node container)
    {
        var nodes = SelectedTopLevel();
        if (!SameParent(nodes))
        {
            HintText = Texts.Get("Msg.SameParent");
            return;
        }

        _history.Execute(new WrapNodesCommand(_document, container, nodes));
        SelectOnly([container.Id]);
    }

    [RelayCommand(CanExecute = nameof(CanUngroup))]
    private void Ungroup()
    {
        var container = SelectedTopLevel().Single();
        var children = container.Children.Select(c => c.Id).ToList();
        _history.Execute(new UnwrapNodeCommand(_document, container));
        SelectOnly(children);
    }

    private bool CanUngroup() => SelectedTopLevel() is [{ CanHaveChildren: true }];

    [RelayCommand(CanExecute = nameof(HasSelection))]
    public void ToggleHole()
    {
        var nodes = SelectedTopLevel();
        if (nodes.Count == 0) return;
        var role = nodes.All(n => n.Role == NodeRole.Hole) ? NodeRole.Solid : NodeRole.Hole;
        foreach (var node in nodes.Where(n => n.Role != role)) _history.Execute(new SetRoleCommand(_document, node, role));
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportStl()
    {
        var meshes = _lastResult?.Parts.Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty).Select(p => p.Mesh).ToList() ?? [];
        if (meshes.Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToExport"));
            return;
        }

        var path = _dialogs.PickSaveFile(Texts.Get("Msg.ExportTitle"), Texts.Get("Msg.ExportFilter"), "model.stl");
        if (path is null) return;

        try
        {
            // Atomic write: a failed export never destroys an existing file (N-ROB-02).
            var temp = path + ".tmp";
            using (var stream = File.Create(temp)) StlWriter.WriteBinary(stream, meshes);
            File.Move(temp, path, overwrite: true);
            SetNotice(Texts.Format("Msg.Exported", meshes.Sum(m => m.TriangleCount), path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetNotice(Texts.Format("Msg.ExportFailed", ex.Message));
        }
    }

    private bool CanExport() => _lastResult?.Parts.Any(p => p.Kind == PartKind.Solid) == true;

    private string UniqueName(string baseName)
    {
        var names = new HashSet<string>(_document.Root.Descendants().Select(n => n.Name), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName} {i}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    partial void OnViewportItemsChanged(IReadOnlyList<ViewportItem> value)
    {
        ExportStlCommand.NotifyCanExecuteChanged();
        Export3mfCommand.NotifyCanExecuteChanged();
        ExportObjCommand.NotifyCanExecuteChanged();
        OpenInSlicerCommand.NotifyCanExecuteChanged();
    }
}
