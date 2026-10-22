using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>Move and rotate handles on top of the scene (F-TRF-01, F-ROT-01).</summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty ActiveToolProperty = DependencyProperty.Register(
        nameof(ActiveTool), typeof(ViewportTool), typeof(ViewportControl),
        new PropertyMetadata(ViewportTool.Move, (d, _) => ((ViewportControl)d).RebuildGizmo()));

    /// <summary>Center of the selection in world space; null hides the gizmo.</summary>
    public static readonly DependencyProperty GizmoPivotProperty = DependencyProperty.Register(
        nameof(GizmoPivot), typeof(Vector3?), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    public static readonly DependencyProperty GizmoTargetProperty = DependencyProperty.Register(
        nameof(GizmoTarget), typeof(IGizmoTarget), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    /// <summary>Box of the object being scaled (oriented like the object); null hides the scale handles.</summary>
    public static readonly DependencyProperty ScaleBoxProperty = DependencyProperty.Register(
        nameof(ScaleBox), typeof(ScaleGizmoBox?), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    /// <summary>Alignment guides shown while a move snaps to other objects (F-SNP-02).</summary>
    public static readonly DependencyProperty SnapGuidesProperty = DependencyProperty.Register(
        nameof(SnapGuides), typeof(IReadOnlyList<Formwerk.Core.Model.SnapGuide>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).RebuildGuides()));

    private static readonly Color GuideColor = Color.FromRgb(0xFF, 0x4F, 0xD8);

    private const double GizmoScaleFactor = 0.11; // gizmo radius as a fraction of the camera distance: constant size on screen

    private static readonly Color AxisX = Color.FromRgb(0xE5, 0x53, 0x4B);
    private static readonly Color AxisY = Color.FromRgb(0x5F, 0xC2, 0x5F);
    private static readonly Color AxisZ = Color.FromRgb(0x4C, 0x8D, 0xFF);

    private readonly Viewport3D _gizmoViewport = new() { ClipToBounds = true, Visibility = Visibility.Collapsed };
    private readonly ModelVisual3D _gizmoVisual = new();
    private readonly ModelVisual3D _guideVisual = new();
    private readonly Dictionary<Model3D, GizmoHandleKind> _handleOfModel = [];
    private readonly Dictionary<GizmoHandleKind, List<(GeometryModel3D Model, Color Color, bool IsPlane)>> _visibleParts = [];
    private readonly Border _readout = new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x1E, 0x1F, 0x22)),
        BorderBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x9A, 0xFF)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(3),
        Padding = new Thickness(7, 3, 7, 3),
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
        Child = new TextBlock { Foreground = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE5)), FontSize = 12 },
    };

    private GizmoHandleKind? _hoverHandle;
    private bool _gizmoDragging;
    private GizmoHandleKind _dragHandle;
    private Vector3 _dragPivot;
    private float _dragStartAxisParameter;
    private Vector3 _dragStartHit;
    private double _dragLastAngle;
    private double _dragAngle;

    // scale drags
    private ScaleGizmoBox _scaleStartBox;
    private Vector3 _scaleHandleWorld;
    private Vector3 _scaleAxisWorld;
    private float _scaleStartOpposite;
    private float _scaleStartCenter;

    public ViewportTool ActiveTool
    {
        get => (ViewportTool)GetValue(ActiveToolProperty);
        set => SetValue(ActiveToolProperty, value);
    }

    public Vector3? GizmoPivot
    {
        get => (Vector3?)GetValue(GizmoPivotProperty);
        set => SetValue(GizmoPivotProperty, value);
    }

    /// <summary>Receives the drags of the handles. Without a target no gizmo is shown.</summary>
    public IGizmoTarget? GizmoTarget
    {
        get => (IGizmoTarget?)GetValue(GizmoTargetProperty);
        set => SetValue(GizmoTargetProperty, value);
    }

    public IReadOnlyList<Formwerk.Core.Model.SnapGuide>? SnapGuides
    {
        get => (IReadOnlyList<Formwerk.Core.Model.SnapGuide>?)GetValue(SnapGuidesProperty);
        set => SetValue(SnapGuidesProperty, value);
    }

    public ScaleGizmoBox? ScaleBox
    {
        get => (ScaleGizmoBox?)GetValue(ScaleBoxProperty);
        set => SetValue(ScaleBoxProperty, value);
    }

    private void InitializeGizmo()
    {
        // A second view with the same camera is drawn on top of the scene, so the gizmo is never hidden inside a body.
        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Colors.White));
        _gizmoViewport.Children.Add(new ModelVisual3D { Content = lights });
        _gizmoViewport.Children.Add(_gizmoVisual);
        _gizmoViewport.Children.Add(_guideVisual);
        InitializeMarkers();
        InitializeMeasurements();
        InitializeDimensions();
        _gizmoViewport.Camera = _camera;
        RebuildGizmo();
    }

    // ---- building ------------------------------------------------------------------------------

    private void RebuildGizmo()
    {
        _handleOfModel.Clear();
        _visibleParts.Clear();
        var group = new Model3DGroup();

        if (ActiveTool == ViewportTool.Move) BuildMoveHandles(group);
        else if (ActiveTool == ViewportTool.Rotate) BuildRotateHandles(group);

        // The scale handles depend on the camera distance and are built in UpdateGizmoTransform.
        _gizmoVisual.Content = group;
        ApplyHandleMaterials();
        UpdateGizmoTransform();
    }

    /// <summary>
    /// The scale box with a handle on every face and corner, in world coordinates. The handles are rebuilt whenever the camera
    /// or the box changes so that they keep the same size on screen.
    /// </summary>
    private void RebuildScaleHandles()
    {
        _handleOfModel.Clear();
        _visibleParts.Clear();
        var group = new Model3DGroup();

        if (ScaleBox is { } box)
        {
            var distance = (float)_orbit.Distance;
            // Constant size on screen, but never so big that the handles of a small object swallow each other:
            // the hit areas of neighbouring handles must not overlap.
            var smallestSide = MathF.Min(box.Size.X, MathF.Min(box.Size.Y, box.Size.Z));
            var half = MathF.Max(MathF.Min(distance * 0.010f, smallestSide * 0.15f), distance * 0.004f);
            var hitHalf = MathF.Max(MathF.Min(distance * 0.018f, smallestSide * 0.22f), distance * 0.006f);
            var tube = distance * 0.0022f;
            Vector3 Corner(int bits) => box.ToWorld(box.HandleLocal(GizmoHandleKind.ScaleCorner0 + bits));

            var outline = new MeshGeometry3D();
            (int, int)[] edges = [(0, 1), (2, 3), (4, 5), (6, 7), (0, 2), (1, 3), (4, 6), (5, 7), (0, 4), (1, 5), (2, 6), (3, 7)];
            foreach (var (a, b) in edges)
            {
                if (Vector3.DistanceSquared(Corner(a), Corner(b)) > 1e-8f) GizmoGeometry.AddTube(outline, Corner(a), Corner(b), tube, 6);
            }

            outline.Freeze();
            var outlineMaterial = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xC7)));
            outlineMaterial.Freeze();
            group.Children.Add(new GeometryModel3D(outline, outlineMaterial) { BackMaterial = outlineMaterial });

            Color[] axisColors = [AxisX, AxisY, AxisZ];
            foreach (var handle in Enum.GetValues<GizmoHandleKind>().Where(GizmoHandles.IsScale))
            {
                var position = box.ToWorld(box.HandleLocal(handle));
                var color = GizmoHandles.IsScaleCorner(handle) ? Color.FromRgb(0xD0, 0xD4, 0xDC) : axisColors[GizmoHandles.ScaleAxisIndex(handle)];

                var visible = new MeshGeometry3D();
                GizmoGeometry.AddBox(visible, position, box.Rotation, half);
                AddHandle(group, handle, visible, color, isPlane: false);

                var hit = new MeshGeometry3D();
                GizmoGeometry.AddBox(hit, position, box.Rotation, hitHalf);
                AddHitTarget(group, handle, hit);
            }
        }

        _gizmoVisual.Content = group;
        _gizmoVisual.Transform = Transform3D.Identity;
        ApplyHandleMaterials();
    }

    private void BuildMoveHandles(Model3DGroup group)
    {
        (GizmoHandleKind Handle, Vector3 Axis, Color Color)[] axes =
        [
            (GizmoHandleKind.MoveX, Vector3.UnitX, AxisX),
            (GizmoHandleKind.MoveY, Vector3.UnitY, AxisY),
            (GizmoHandleKind.MoveZ, Vector3.UnitZ, AxisZ),
        ];

        foreach (var (handle, axis, color) in axes)
        {
            var visible = new MeshGeometry3D();
            GizmoGeometry.AddTube(visible, axis * 0.12f, axis * 0.88f, 0.028f);
            GizmoGeometry.AddCone(visible, axis * 0.88f, axis * 1.22f, 0.085f);
            AddHandle(group, handle, visible, color, isPlane: false);

            var hit = new MeshGeometry3D();
            GizmoGeometry.AddTube(hit, axis * 0.1f, axis * 1.22f, 0.13f);
            AddHitTarget(group, handle, hit);
        }

        (GizmoHandleKind Handle, Vector3 U, Vector3 V, Color Color)[] planes =
        [
            (GizmoHandleKind.MovePlaneXY, Vector3.UnitX, Vector3.UnitY, AxisZ),
            (GizmoHandleKind.MovePlaneXZ, Vector3.UnitX, Vector3.UnitZ, AxisY),
            (GizmoHandleKind.MovePlaneYZ, Vector3.UnitY, Vector3.UnitZ, AxisX),
        ];

        foreach (var (handle, u, v, color) in planes)
        {
            var square = new MeshGeometry3D();
            GizmoGeometry.AddSquare(square, u, v, 0.28f, 0.58f);
            AddHandle(group, handle, square, color, isPlane: true);
        }
    }

    private void BuildRotateHandles(Model3DGroup group)
    {
        (GizmoHandleKind Handle, Vector3 Axis, Color Color)[] rings =
        [
            (GizmoHandleKind.RotateX, Vector3.UnitX, AxisX),
            (GizmoHandleKind.RotateY, Vector3.UnitY, AxisY),
            (GizmoHandleKind.RotateZ, Vector3.UnitZ, AxisZ),
        ];

        foreach (var (handle, axis, color) in rings)
        {
            var visible = new MeshGeometry3D();
            GizmoGeometry.AddRing(visible, axis, 1.0f, 0.022f);
            AddHandle(group, handle, visible, color, isPlane: false);

            var hit = new MeshGeometry3D();
            GizmoGeometry.AddRing(hit, axis, 1.0f, 0.11f, segments: 48, tubeSegments: 6);
            AddHitTarget(group, handle, hit);
        }
    }

    private void AddHandle(Model3DGroup group, GizmoHandleKind handle, MeshGeometry3D geometry, Color color, bool isPlane)
    {
        geometry.Freeze();
        var model = new GeometryModel3D { Geometry = geometry };
        group.Children.Add(model);
        _handleOfModel[model] = handle;
        if (!_visibleParts.TryGetValue(handle, out var list)) _visibleParts[handle] = list = [];
        list.Add((model, color, isPlane));
    }

    /// <summary>Invisible, fatter copy of a handle so that thin arrows and rings are easy to hit.</summary>
    private void AddHitTarget(Model3DGroup group, GizmoHandleKind handle, MeshGeometry3D geometry)
    {
        geometry.Freeze();
        var material = new DiffuseMaterial(Brushes.Transparent);
        material.Freeze();
        var model = new GeometryModel3D { Geometry = geometry, Material = material, BackMaterial = material };
        group.Children.Add(model);
        _handleOfModel[model] = handle;
    }

    private void ApplyHandleMaterials()
    {
        foreach (var (handle, parts) in _visibleParts)
        {
            var active = _gizmoDragging && handle == _dragHandle;
            var hover = !_gizmoDragging && handle == _hoverHandle;
            foreach (var (model, color, isPlane) in parts)
            {
                var shown = active ? Blend(color, Colors.White, 0.7) : hover ? Blend(color, Colors.White, 0.45) : color;
                var alpha = isPlane ? (byte)(active || hover ? 215 : 140) : (byte)255;
                var material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(alpha, shown.R, shown.G, shown.B)));
                material.Freeze();
                model.Material = material;
                model.BackMaterial = material;
            }
        }
    }

    /// <summary>One rectangle per active alignment, in the plane perpendicular to the snapped axis. Drawn on top of the scene.</summary>
    private void RebuildGuides()
    {
        var guides = SnapGuides;
        if (guides is null || guides.Count == 0)
        {
            _guideVisual.Content = null;
            return;
        }

        var mesh = new MeshGeometry3D();
        var radius = (float)(_orbit.Distance * 0.0016);
        foreach (var guide in guides)
        {
            var a = (guide.Axis + 1) % 3;
            var b = (guide.Axis + 2) % 3;
            Vector3 Corner(float u, float v)
            {
                var p = guide.Min;
                p = SetComponent(p, guide.Axis, guide.Coordinate);
                p = SetComponent(p, a, u);
                p = SetComponent(p, b, v);
                return p;
            }

            // a little margin so that a rectangle that exactly covers an object stays visible
            var margin = radius * 6;
            var u0 = Formwerk.Core.Model.SnapBox.Component(guide.Min, a) - margin;
            var u1 = Formwerk.Core.Model.SnapBox.Component(guide.Max, a) + margin;
            var v0 = Formwerk.Core.Model.SnapBox.Component(guide.Min, b) - margin;
            var v1 = Formwerk.Core.Model.SnapBox.Component(guide.Max, b) + margin;
            (Vector3, Vector3)[] edges = [(Corner(u0, v0), Corner(u1, v0)), (Corner(u1, v0), Corner(u1, v1)), (Corner(u1, v1), Corner(u0, v1)), (Corner(u0, v1), Corner(u0, v0))];
            foreach (var (from, to) in edges)
            {
                if (Vector3.DistanceSquared(from, to) > 1e-8f) GizmoGeometry.AddTube(mesh, from, to, radius, 6);
            }
        }

        mesh.Freeze();
        var material = new DiffuseMaterial(new SolidColorBrush(GuideColor));
        material.Freeze();
        _guideVisual.Content = new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private static Vector3 SetComponent(Vector3 v, int axis, float value) => axis switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

    private void UpdateGizmoTransform()
    {
        RebuildGuides(); // the guides keep their thickness on screen when the camera moves
        RebuildMarkers();
        RebuildDimensions();
        var visible = GizmoTarget is not null && ActiveTool switch
        {
            ViewportTool.Move or ViewportTool.Rotate => GizmoPivot is not null,
            ViewportTool.Scale => ScaleBox is not null,
            _ => false,
        };
        _gizmoViewport.Visibility = visible || _markerVisual.Content is not null || _dimensionVisual.Content is not null ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        if (ActiveTool == ViewportTool.Scale)
        {
            RebuildScaleHandles();
            return;
        }

        var pivot = GizmoPivot!.Value;
        var scale = Math.Max(_orbit.Distance * GizmoScaleFactor, 0.001);
        var group = new Transform3DGroup();
        group.Children.Add(new ScaleTransform3D(scale, scale, scale));
        group.Children.Add(new TranslateTransform3D(pivot.X, pivot.Y, pivot.Z));
        _gizmoVisual.Transform = group;
    }

    // ---- interaction ---------------------------------------------------------------------------

    private GizmoHandleKind? HitTestGizmo(Point position)
    {
        if (_gizmoViewport.Visibility != Visibility.Visible) return null;
        var hits = new List<(GizmoHandleKind Handle, double Distance)>();
        VisualTreeHelper.HitTest(_gizmoViewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult ray && _handleOfModel.TryGetValue(ray.ModelHit, out var handle))
                hits.Add((handle, ray.DistanceToRayOrigin));
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(position));

        return hits.Count == 0 ? null : hits.OrderBy(h => h.Distance).First().Handle;
    }

    private Ray MouseRay(Point position) => _orbit.CreateRay(position.X, position.Y, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1));

    private bool TryBeginGizmoDrag(Point position)
    {
        var target = GizmoTarget;
        if (target is null) return false;
        if (ActiveTool == ViewportTool.Scale) return TryBeginScaleDrag(position, target);
        if (GizmoPivot is not { } pivot) return false;
        if (HitTestGizmo(position) is not { } handle) return false;

        // The handle was hit: this click belongs to the gizmo, whatever happens next.
        var ray = MouseRay(position);
        var axis = GizmoHandles.Axis(handle);
        var normal = GizmoHandles.PlaneNormal(handle);

        if (GizmoHandles.IsRotation(handle))
        {
            if (!GizmoMath.IntersectPlane(ray, pivot, axis, out _dragStartHit)) return true;
        }
        else if (GizmoHandles.IsPlane(handle))
        {
            if (!GizmoMath.IntersectPlane(ray, pivot, normal, out _dragStartHit)) return true;
        }
        else
        {
            if (GizmoMath.ClosestParameterOnLine(ray, pivot, axis) is not { } t) return true;
            _dragStartAxisParameter = t;
        }

        if (!target.BeginDrag(handle)) return true;

        _gizmoDragging = true;
        _dragHandle = handle;
        _dragPivot = pivot;
        _dragLastAngle = 0;
        _dragAngle = 0;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        ApplyHandleMaterials();
        return true;
    }

    private void UpdateGizmoDrag(Point position)
    {
        var target = GizmoTarget;
        if (target is null) return;

        if (GizmoHandles.IsScale(_dragHandle))
        {
            UpdateScaleDrag(position, target);
            return;
        }

        var ray = MouseRay(position);
        var axis = GizmoHandles.Axis(_dragHandle);
        var translation = Vector3.Zero;
        var snapSuppressed = Keyboard.Modifiers.HasFlag(GizmoHandles.IsRotation(_dragHandle) ? ModifierKeys.Shift : ModifierKeys.Alt);

        if (GizmoHandles.IsRotation(_dragHandle))
        {
            if (!GizmoMath.IntersectPlane(ray, _dragPivot, axis, out var hit)) return;
            var start = _dragStartHit - _dragPivot;
            var now = hit - _dragPivot;
            if (now.LengthSquared() < 1e-8f || start.LengthSquared() < 1e-8f) return;
            var raw = GizmoMath.SignedAngleDegrees(start, now, axis);
            _dragAngle = GizmoMath.Unwrap(_dragLastAngle, raw);
            _dragLastAngle = _dragAngle;
        }
        else if (GizmoHandles.IsPlane(_dragHandle))
        {
            if (!GizmoMath.IntersectPlane(ray, _dragPivot, GizmoHandles.PlaneNormal(_dragHandle), out var hit)) return;
            translation = hit - _dragStartHit;
        }
        else
        {
            if (GizmoMath.ClosestParameterOnLine(ray, _dragPivot, axis) is not { } t) return;
            translation = axis * (t - _dragStartAxisParameter);
        }

        var worldPerPixel = _orbit.WorldPerPixelAt(_dragPivot, Math.Max(ActualWidth, 1));
        var text = target.UpdateDrag(new GizmoDrag(_dragHandle, translation, _dragAngle, snapSuppressed, WorldPerPixel: worldPerPixel));
        ShowReadout(text, position);
    }

    private bool TryBeginScaleDrag(Point position, IGizmoTarget target)
    {
        if (ScaleBox is not { } box) return false;
        if (HitTestGizmo(position) is not { } handle || !GizmoHandles.IsScale(handle)) return false;

        var ray = MouseRay(position);
        var handleWorld = box.ToWorld(box.HandleLocal(handle));
        if (GizmoHandles.IsScaleFace(handle))
        {
            // Face handle: how far along its axis the mouse is at the start.
            var axis = box.AxisWorld(GizmoHandles.ScaleAxisIndex(handle));
            if (GizmoMath.ClosestParameterOnLine(ray, handleWorld, axis) is not { } t) return true;
            _scaleAxisWorld = axis;
            _scaleStartOpposite = t;
        }
        else
        {
            // Corner handle: where the mouse is on the diagonal from the fixed corner (and from the center, for Ctrl).
            var opposite = box.ToWorld(box.OppositeLocal(handle));
            var center = box.ToWorld(box.CenterLocal);
            var fromOpposite = GizmoMath.ClosestParameterOnLine(ray, opposite, handleWorld - opposite);
            var fromCenter = GizmoMath.ClosestParameterOnLine(ray, center, handleWorld - center);
            if (fromOpposite is null || fromCenter is null) return true;
            _scaleStartOpposite = fromOpposite.Value;
            _scaleStartCenter = fromCenter.Value;
        }

        if (!target.BeginDrag(handle)) return true;

        _gizmoDragging = true;
        _dragHandle = handle;
        _scaleStartBox = box;
        _scaleHandleWorld = handleWorld;
        CaptureMouse();
        Cursor = Cursors.SizeAll;
        ApplyHandleMaterials();
        return true;
    }

    private void UpdateScaleDrag(Point position, IGizmoTarget target)
    {
        var box = _scaleStartBox;
        var handle = _dragHandle;
        var ray = MouseRay(position);
        var fromCenter = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var suppressSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);

        double factor;
        if (GizmoHandles.IsScaleFace(handle))
        {
            if (GizmoMath.ClosestParameterOnLine(ray, _scaleHandleWorld, _scaleAxisWorld) is not { } t) return;
            var axis = GizmoHandles.ScaleAxisIndex(handle);
            var extent = axis switch { 0 => box.Size.X, 1 => box.Size.Y, _ => box.Size.Z };
            factor = ScaleMath.FaceFactor(extent, GizmoHandles.ScaleSide(handle), t - _scaleStartOpposite, fromCenter);
        }
        else
        {
            var reference = fromCenter ? box.ToWorld(box.CenterLocal) : box.ToWorld(box.OppositeLocal(handle));
            if (GizmoMath.ClosestParameterOnLine(ray, reference, _scaleHandleWorld - reference) is not { } t) return;
            factor = ScaleMath.CornerFactor(t, fromCenter ? _scaleStartCenter : _scaleStartOpposite);
        }

        var text = target.UpdateDrag(new GizmoDrag(handle, Vector3.Zero, 0, suppressSnap, factor, fromCenter));
        ShowReadout(text, position);
    }

    private void EndGizmoDrag(bool commit)
    {
        if (!_gizmoDragging) return;
        _gizmoDragging = false; // first: releasing the capture raises LostMouseCapture, which must not cancel again
        ReleaseMouseCapture();
        Cursor = null;
        _readout.Visibility = Visibility.Collapsed;
        if (commit) GizmoTarget?.CommitDrag();
        else GizmoTarget?.CancelDrag();
        ApplyHandleMaterials();
    }

    private void UpdateGizmoHover(Point position)
    {
        var handle = HitTestGizmo(position);
        if (handle == _hoverHandle) return;
        _hoverHandle = handle;
        Cursor = handle is null ? null : Cursors.Hand;
        ApplyHandleMaterials();
    }

    private void ShowReadout(string text, Point position)
    {
        if (string.IsNullOrEmpty(text))
        {
            _readout.Visibility = Visibility.Collapsed;
            return;
        }

        ((TextBlock)_readout.Child).Text = text;
        _readout.Margin = new Thickness(position.X + 18, position.Y + 18, 0, 0);
        _readout.Visibility = Visibility.Visible;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_gizmoDragging) EndGizmoDrag(commit: false);
        if (_marqueeActive)
        {
            CancelMarquee();
            _leftDown = false;
        }
    }
}
