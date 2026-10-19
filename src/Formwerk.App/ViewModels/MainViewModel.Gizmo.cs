using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Tools, snapping, and what a drag of a gizmo handle does to the document (F-TRF-01, F-ROT-01/02, F-SNP-05, F-FLR-01).</summary>
public sealed partial class MainViewModel : IGizmoTarget
{
    private List<(Node Node, Transform Start)> _dragNodes = [];
    private Vector3 _dragPivotStart;
    private Vector3 _dragPrimaryStart;
    private GizmoHandleKind _dragHandle;
    private bool _dragging;
    private SnapBox? _snapOwn;
    private List<SnapBox> _snapTargets = [];

    // Drag preview: the meshes the view already shows are moved by a matrix instead of calculating the scene again for every mouse move.
    private bool _fastPreview;
    private Dictionary<Node, Matrix4x4> _dragStartWorld = [];
    private Dictionary<Node, Guid[]> _dragIds = [];
    private Vector3 _dragDelta;

    /// <summary>
    /// While a move or rotate handle is dragged: for every dragged object (and everything inside it) the matrix from where the view
    /// shows it to where it is now. Null when nothing is dragged, or when the drag recalculates the scene live.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyDictionary<Guid, Matrix4x4>? PreviewTransforms { get; set; }

    /// <summary>The view model receives the drags of the viewport handles itself.</summary>
    public IGizmoTarget GizmoTargetObject => this;

    [ObservableProperty]
    public partial ViewportTool ActiveTool { get; set; } = ViewportTool.Move;

    /// <summary>Center of the selected objects in world space; null when nothing is selected or not yet computed.</summary>
    [ObservableProperty]
    public partial Vector3? GizmoPivot { get; set; }

    [ObservableProperty]
    public partial bool SnapToGrid { get; set; } = true;

    /// <summary>Grid size in mm; moves snap the position of the object to multiples of it.</summary>
    [ObservableProperty]
    public partial double GridStep { get; set; } = 1;

    /// <summary>Moves snap to the edges and centers of other objects, the floor and the bed center lines (F-SNP-02).</summary>
    [ObservableProperty]
    public partial bool SnapToObjects { get; set; } = true;

    /// <summary>Alignment guides of the current drag; null when nothing is snapped.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SnapGuide>? SnapGuides { get; set; }

    /// <summary>The snap point (world position) a move drag has hit, shown as a marker in the view (F-SNP-01).</summary>
    [ObservableProperty]
    public partial IReadOnlyList<Vector3>? SnapMarkers { get; set; }

    [ObservableProperty]
    public partial bool SnapAngles { get; set; } = true;

    /// <summary>Angle step in degrees for rotations.</summary>
    [ObservableProperty]
    public partial double AngleStep { get; set; } = 15;

    /// <summary>Choices of the status bar; a value from the settings dialog that is not in the list is added.</summary>
    public ObservableCollection<double> GridSteps { get; } = [0.1, 0.5, 1, 5, 10];

    public ObservableCollection<double> AngleSteps { get; } = [1, 5, 15, 45, 90];

    public bool IsSelectTool
    {
        get => ActiveTool == ViewportTool.Select;
        set { if (value) ActiveTool = ViewportTool.Select; else OnPropertyChanged(); }
    }

    public bool IsMoveTool
    {
        get => ActiveTool == ViewportTool.Move;
        set { if (value) ActiveTool = ViewportTool.Move; else OnPropertyChanged(); }
    }

    public bool IsRotateTool
    {
        get => ActiveTool == ViewportTool.Rotate;
        set { if (value) ActiveTool = ViewportTool.Rotate; else OnPropertyChanged(); }
    }

    public bool IsScaleTool
    {
        get => ActiveTool == ViewportTool.Scale;
        set { if (value) ActiveTool = ViewportTool.Scale; else OnPropertyChanged(); }
    }

    partial void OnActiveToolChanged(ViewportTool value)
    {
        OnPropertyChanged(nameof(IsSelectTool));
        OnPropertyChanged(nameof(IsMoveTool));
        OnPropertyChanged(nameof(IsRotateTool));
        OnPropertyChanged(nameof(IsScaleTool));
        UpdatePivot();
        RefreshScaleHint();
    }

    /// <summary>Scaling works on one object at a time; with several selected objects the hint says what to do.</summary>
    private void RefreshScaleHint()
    {
        if (IsPlacingHole) HintText = Texts.Get("Hint.HolePlace");
        else if (IsPickingFace) HintText = Texts.Get("Hint.FacePick");
        else if (OpenContainer is { } open) HintText = Texts.Format("Hint.GroupOpen", open.Name);
        else if (ActiveTool == ViewportTool.Scale && _selected.Count > 1) HintText = Texts.Get("Hint.ScaleSingle");
        else if (ActiveTool == ViewportTool.Select) HintText = Texts.Get("Hint.SelectTool");
        else if (_selected.Count == 0) HintText = Texts.Get("Hint.Default");
        else HintText = Texts.Get("Hint.Selected");
    }

    [RelayCommand]
    private void SetTool(ViewportTool tool) => ActiveTool = tool;

    // ---- pivot ---------------------------------------------------------------------------------

    private void UpdatePivot()
    {
        GizmoPivot = ComputePivot();
        GizmoScaleBox = ActiveTool == ViewportTool.Scale ? ComputeScaleBox() : null;
        ViewportDimensions = ComputeDimensions();
    }

    /// <summary>Center of the bounding box of everything the selected objects show (solids and hole ghosts).</summary>
    private Vector3? ComputePivot()
    {
        if (_lastResult is null || _selected.Count == 0) return null;
        var bounds = BoundsOfSelection(_lastResult, includeGhosts: true);
        return bounds is null ? null : (bounds.Value.Min + bounds.Value.Max) * 0.5f;
    }

    private (Vector3 Min, Vector3 Max)? BoundsOfSelection(EvaluationResult result, bool includeGhosts)
    {
        var ids = new HashSet<Guid>();
        foreach (var node in SelectedTopLevel())
        {
            ids.Add(node.Id);
            foreach (var descendant in node.Descendants()) ids.Add(descendant.Id);
        }

        return BoundsOf(result, ids, includeGhosts);
    }

    private static (Vector3 Min, Vector3 Max)? BoundsOf(EvaluationResult result, HashSet<Guid> ids, bool includeGhosts)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var part in result.Parts)
        {
            if (!ids.Contains(part.Source.Id) || part.Mesh.IsEmpty) continue;
            if (part.Kind == PartKind.Ghost && !includeGhosts) continue;
            var b = part.Mesh.ComputeBounds();
            min = Vector3.Min(min, b.Min);
            max = Vector3.Max(max, b.Max);
            any = true;
        }

        return any ? (min, max) : null;
    }

    // ---- dragging a handle ---------------------------------------------------------------------

    public bool BeginDrag(GizmoHandleKind handle)
    {
        _fastPreview = false;
        if (GizmoHandles.IsScale(handle)) return BeginScale(handle);

        var nodes = SelectedTopLevel();
        if (nodes.Count == 0 || nodes.Any(n => n.Locked) || GizmoPivot is not { } pivot) return false;

        // Objects that sit directly in the scene cannot change each other's shape while they move, so the view can just move them.
        // Inside a group (holes, boolean operands) the result depends on the position and is calculated live. If the result of the
        // last drag has not arrived yet, the view does not show the objects where they are, so this drag calculates live as well.
        _fastPreview = PreviewTransforms is null && nodes.All(n => n.Parent == _document.Root);
        _dragDelta = Vector3.Zero;
        if (_fastPreview)
        {
            _dragStartWorld = nodes.ToDictionary(n => n, TransformOps.LocalToWorld);
            _dragIds = nodes.ToDictionary(n => n, n => n.Descendants().Select(d => d.Id).Prepend(n.Id).ToArray());
        }

        _dragNodes = [.. nodes.Select(n => (n, n.Transform))];
        PrepareObjectSnapping(nodes);
        _dragPivotStart = pivot;
        _dragPrimaryStart = TransformOps.WorldPosition(nodes[0]);
        _dragHandle = handle;
        _dragging = true;
        return true;
    }

    public string UpdateDrag(GizmoDrag drag)
    {
        if (!_dragging) return "";
        if (_scale is not null) return UpdateScale(drag);

        string text;
        if (GizmoHandles.IsRotation(drag.Handle))
        {
            var angle = drag.AngleDegrees;
            if (SnapAngles && !drag.SnapSuppressed && AngleStep > 0) angle = Math.Round(angle / AngleStep) * AngleStep;
            var axis = GizmoHandles.Axis(drag.Handle);
            foreach (var (node, start) in _dragNodes)
                node.Transform = TransformOps.RotatedAbout(node, start, _dragPivotStart, axis, angle);
            text = Texts.Format("Readout.Angle", Fmt(angle));
        }
        else
        {
            SnapGuides = null;
            SnapMarkers = null;
            var delta = SnapMove(drag);
            _dragDelta = delta;
            foreach (var (node, start) in _dragNodes) node.Transform = TransformOps.MovedBy(node, start, delta);
            var position = _dragPrimaryStart + delta;
            text = Texts.Format("Readout.Position", Fmt(position.X), Fmt(position.Y), Fmt(position.Z));
        }

        if (_fastPreview) ShowDragPreview();
        else _document.NotifyChanged(); // live preview through the normal background evaluation; no history entry yet
        return text;
    }

    /// <summary>Tells the view where the dragged objects are now, without calculating anything.</summary>
    private void ShowDragPreview()
    {
        var preview = new Dictionary<Guid, Matrix4x4>();
        foreach (var (node, _) in _dragNodes)
        {
            if (!Matrix4x4.Invert(_dragStartWorld[node], out var startInverse)) continue;
            var delta = startInverse * TransformOps.LocalToWorld(node); // a point of the mesh as shown (world, at the start) to where it is now
            foreach (var id in _dragIds[node]) preview[id] = delta;
        }

        PreviewTransforms = preview;
        if (!GizmoHandles.IsRotation(_dragHandle)) GizmoPivot = _dragPivotStart + _dragDelta; // the handles follow the object
        Properties?.Refresh(); // the position fields follow the mouse
    }

    /// <summary>
    /// Snaps the world position of the first selected object to the grid (F-SNP-05), only along the axes the handle
    /// moves in; all other selected objects follow by the same delta.
    /// </summary>
    private Vector3 SnapMove(GizmoDrag drag)
    {
        var free = GizmoHandles.IsPlane(drag.Handle)
            ? Vector3.One - Vector3.Abs(GizmoHandles.PlaneNormal(drag.Handle))
            : Vector3.Abs(GizmoHandles.Axis(drag.Handle));

        // A handle only moves along its own axes, whatever the viewport reports.
        var delta = drag.TranslationDelta * free;
        if (drag.SnapSuppressed) return delta;

        // Other objects first (F-SNP-02): an axis that snapped to an object is not snapped to the grid any more.
        if (SnapToObjects && drag.WorldPerPixel > 0 && _snapOwn is { } own)
        {
            var snap = ObjectSnapper.Snap(own, delta, free, _snapTargets, (float)(SnapTolerancePixels * drag.WorldPerPixel));
            delta = snap.Delta;
            free *= Vector3.One - snap.SnappedAxes;
            SnapGuides = snap.Guides.Count > 0 ? snap.Guides : null;
            SnapMarkers = snap.Marker is { } marker ? [marker] : null;
        }

        if (!SnapToGrid || GridStep <= 0) return delta;

        var step = (float)GridStep;
        var target = _dragPrimaryStart + delta;
        var snapped = new Vector3(
            MathF.Round(target.X / step) * step,
            MathF.Round(target.Y / step) * step,
            MathF.Round(target.Z / step) * step);
        // Only the axes that did not snap to an object take the grid value; the others keep what they have.
        var gridDelta = snapped - _dragPrimaryStart;
        return delta + (gridDelta - delta) * free;
    }

    public void CommitDrag()
    {
        SnapGuides = null;
        SnapMarkers = null;
        if (!_dragging) return;
        if (_scale is not null)
        {
            CommitScale();
            return;
        }

        _dragging = false; // from here on document changes rebuild the lists again
        if (!GizmoHandles.IsRotation(_dragHandle) && _dragNodes.Count > 0)
            RememberOffset(TransformOps.WorldPosition(_dragNodes[0].Node) - _dragPrimaryStart); // Ctrl+D repeats this move
        _fastPreview = false; // the preview stays on screen until the result of this change has been applied (see ApplyResult)

        var command = new SetTransformsCommand(_document,
            _dragNodes.Select(d => (d.Node, d.Start, d.Node.Transform)), DragName());
        _dragNodes = [];
        if (command.HasEffect) _history.Execute(command);
        else _document.NotifyChanged();
    }

    public void CancelDrag()
    {
        SnapGuides = null;
        SnapMarkers = null;
        if (!_dragging) return;
        if (_scale is not null)
        {
            CancelScale();
            return;
        }

        _dragging = false;
        foreach (var (node, start) in _dragNodes) node.Transform = start;
        _dragNodes = [];
        if (_fastPreview)
        {
            // the view never left the start state: take the preview away and show the start position of the handles again
            _fastPreview = false;
            PreviewTransforms = null;
            UpdatePivot();
            Properties?.Refresh();
        }
        else
        {
            _document.NotifyChanged();
        }
    }

    /// <summary>Collects what a move can snap to: the bounds of the selection and of every other visible solid.</summary>
    private void PrepareObjectSnapping(IReadOnlyList<Node> dragged)
    {
        _snapOwn = null;
        _snapTargets = [];
        if (_lastResult is null) return;

        var ids = new HashSet<Guid>();
        foreach (var node in dragged)
        {
            ids.Add(node.Id);
            foreach (var d in node.Descendants()) ids.Add(d.Id);
        }

        if (BoundsOf(_lastResult, ids, includeGhosts: true) is { } own) _snapOwn = new SnapBox(own.Min, own.Max);
        foreach (var part in _lastResult.Parts)
        {
            if (part.Kind != PartKind.Solid || part.Mesh.IsEmpty || ids.Contains(part.Source.Id)) continue;
            var b = part.Mesh.ComputeBounds();
            _snapTargets.Add(new SnapBox(b.Min, b.Max));
        }
    }

    private string DragName()
    {
        var what = _dragNodes.Count == 1 ? _dragNodes[0].Node.Name : $"{_dragNodes.Count} objects";
        return GizmoHandles.IsRotation(_dragHandle) ? $"Rotate {what}" : $"Move {what}";
    }

    private static string Fmt(double value)
    {
        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.##", CultureInfo.InvariantCulture);
    }

    // ---- drop to floor -------------------------------------------------------------------------

    /// <summary>Puts the lowest point of every selected object on the build plate, Z = 0 (F-FLR-01).</summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DropToFloor()
    {
        var changes = FloorChanges(SelectedTopLevel(), holesToo: true);
        if (changes.Count == 0) return;
        _history.Execute(new SetTransformsCommand(_document, changes, Texts.Get("Name.DropToFloor")));
    }

    /// <summary>
    /// The moves that put the lowest solid point of each unlocked node on Z = 0 (nodes that already rest there are left out). With
    /// <paramref name="holesToo"/> a node that is only a hole is moved by its ghost; otherwise it is left alone.
    /// </summary>
    private List<(Node Node, Transform Old, Transform New)> FloorChanges(IEnumerable<Node> nodes, bool holesToo)
    {
        var changes = new List<(Node Node, Transform Old, Transform New)>();
        if (_lastResult is null) return changes;
        foreach (var node in nodes.Where(n => !n.Locked))
        {
            var ids = new HashSet<Guid>(node.Descendants().Select(d => d.Id)) { node.Id };
            var bounds = BoundsOf(_lastResult, ids, includeGhosts: false) ?? (holesToo ? BoundsOf(_lastResult, ids, includeGhosts: true) : null);
            if (bounds is null || Math.Abs(bounds.Value.Min.Z) < 1e-4f) continue;
            changes.Add((node, node.Transform, TransformOps.MovedBy(node, node.Transform, new Vector3(0, 0, -bounds.Value.Min.Z))));
        }

        return changes;
    }
}
