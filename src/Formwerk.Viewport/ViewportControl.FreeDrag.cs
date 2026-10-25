using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>
/// Moving an object by dragging it with the left mouse button, as in 3D Builder (the free dragging on the work plane of F-TRF-01). The
/// drag is the same as the one of the plane handle of the move gizmo (so snapping, the readout, Esc and the one undo step are the
/// view model's business), but the plane goes through the point that was grabbed, so the object follows the mouse.
/// </summary>
public sealed partial class ViewportControl
{
    private bool _freeDragPending;
    private Vector3 _freeDragPoint;

    /// <summary>The item under the mouse and where the ray hit it. Ghosts win over solids so that holes inside a body stay selectable (F-VIS-04).</summary>
    private (ModelInfo Info, Vector3 Point)? ItemHitAt(Point position)
    {
        var hits = new List<(ModelInfo Info, double Distance, Point3D Point)>();
        VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult ray && _models.TryGetValue(ray.ModelHit, out var info))
                hits.Add((info, ray.DistanceToRayOrigin, ray.PointHit));
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(position));

        var best = hits.Where(h => h.Info.Item.Kind == ViewportItemKind.Ghost).OrderBy(h => h.Distance).Select(h => ((ModelInfo, Point3D)?)(h.Info, h.Point)).FirstOrDefault()
                   ?? hits.OrderBy(h => h.Distance).Select(h => ((ModelInfo, Point3D)?)(h.Info, h.Point)).FirstOrDefault();
        return best is { } b ? (b.Item1, new Vector3((float)b.Item2.X, (float)b.Item2.Y, (float)b.Item2.Z)) : null;
    }

    /// <summary>
    /// A left press on an object that could start a move: selects it when it is not selected yet, and remembers where it was grabbed.
    /// The move itself starts when the mouse has moved a few pixels.
    /// </summary>
    private void PrepareFreeDrag(Point position)
    {
        _freeDragPending = false;
        _pickedOnDown = false;
        if (GizmoTarget is null || ItemHitAt(position) is not { } hit) return;

        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!MouseRules.CanStartFreeDrag(ActiveTool, FacePickMode, additive, Keyboard.IsKeyDown(Key.Space), hit.Info.Item.Dimmed)) return;

        // What is selected and covers the part stays (the selection is what moves); otherwise the click rule of the view model selects.
        // _pickedOnDown tells the release that this press already selected, so that it does not click a second time.
        var request = new PickRequest(hit.Info.Item.NodeId, false, ForDrag: true);
        if (PickCommand?.CanExecute(request) != true) return;
        var before = SelectedIds?.ToHashSet() ?? [];
        PickCommand.Execute(request);
        _pickedOnDown = !before.SetEquals(SelectedIds ?? []);

        _freeDragPoint = hit.Point;
        _freeDragPending = true;
    }

    /// <summary>Starts the move as a drag of the plane handle of the XY plane, through the grabbed point. False when it cannot start (a locked object, a view along the plane).</summary>
    private bool TryBeginFreeDrag()
    {
        _freeDragPending = false;
        if (GizmoTarget is not { } target) return false;
        if (!GizmoMath.IntersectPlane(MouseRay(_downMouse), _freeDragPoint, Vector3.UnitZ, out _dragStartHit)) return false;
        if (!target.BeginDrag(GizmoHandleKind.MovePlaneXY)) return false;

        _gizmoDragging = true;
        _dragHandle = GizmoHandleKind.MovePlaneXY;
        _dragPivot = _freeDragPoint;
        _dragLastAngle = 0;
        _dragAngle = 0;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        ApplyHandleMaterials();
        return true;
    }
}
