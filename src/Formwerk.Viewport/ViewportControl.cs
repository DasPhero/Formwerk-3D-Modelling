using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>
/// 3D view with build plate, orbit camera, selection highlight and click picking.
/// Interim renderer on WPF's built-in 3D (see ADR 0004); the public surface (items in, pick requests out)
/// stays the same when it is replaced by the DirectX renderer (spec D-03).
/// </summary>
public sealed partial class ViewportControl : Grid
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IReadOnlyList<ViewportItem>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).RebuildScene()));

    public static readonly DependencyProperty SelectedIdsProperty = DependencyProperty.Register(
        nameof(SelectedIds), typeof(IReadOnlyCollection<Guid>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) =>
        {
            ((ViewportControl)d).ApplyMaterials();
            ((ViewportControl)d).RebuildEdges(); // selected objects get their own edge color
        }));

    public static readonly DependencyProperty BuildVolumeProperty = DependencyProperty.Register(
        nameof(BuildVolume), typeof(Vector3), typeof(ViewportControl),
        new PropertyMetadata(new Vector3(256, 256, 256), (d, _) => ((ViewportControl)d).RebuildBed()));

    public static readonly DependencyProperty PickCommandProperty = DependencyProperty.Register(
        nameof(PickCommand), typeof(ICommand), typeof(ViewportControl));

    private static readonly Color Accent = Color.FromRgb(0x4C, 0x9A, 0xFF);
    private static readonly Color GhostColor = Color.FromRgb(0xFF, 0x8A, 0x5C);
    private static readonly Color OutsideColor = Color.FromRgb(0xE5, 0x39, 0x35);

    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { UpDirection = new Vector3D(0, 0, 1) };
    private readonly OrbitCamera _orbit = new();
    private readonly DirectionalLight _keyLight = new(Colors.White, new Vector3D(0, 0, -1));
    private readonly ModelVisual3D _bedVisual = new();
    private bool _showAxes = true;
    private readonly ModelVisual3D _partsVisual = new();
    private readonly Dictionary<Model3D, ModelInfo> _models = [];
    private readonly Dictionary<(uint, bool, ViewportItemKind, bool, ViewportDisplayMode, bool, int), Material> _materials = [];

    private Point _lastMouse;
    private Point _downMouse;
    private bool _leftDown;
    private bool _dragged;

    private sealed record ModelInfo(GeometryModel3D Model, ViewportItem Item, int Hatch = 0);

    public ViewportControl()
    {
        Focusable = true;
        AllowDrop = true; // elements of the library can be dropped into the view (F-LIB-04)
        FocusVisualStyle = null;
        ClipToBounds = true;
        Background = BackgroundFor(false);

        var lights = new Model3DGroup();
        lights.Children.Add(new AmbientLight(Color.FromRgb(0x58, 0x58, 0x5C)));
        lights.Children.Add(_keyLight);
        lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x58), new Vector3D(-0.3, 0.4, -1)));
        _viewport.Children.Add(new ModelVisual3D { Content = lights });
        _viewport.Children.Add(_bedVisual);
        _viewport.Children.Add(_partsVisual);
        _viewport.Children.Add(_edgesVisual);
        _viewport.Camera = _camera;
        Children.Add(_viewport);
        InitializeGizmo();
        Children.Add(_gizmoViewport);
        Children.Add(_readout);
        Children.Add(_marqueeBox);
        InitializeViewCube();
        Children.Add(_cubeViewport);

        RebuildBed();
        UpdateCamera();
        SizeChanged += (_, _) => UpdateCamera();
        Unloaded += (_, _) => StopGlide();
    }

    public IReadOnlyList<ViewportItem>? Items
    {
        get => (IReadOnlyList<ViewportItem>?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public IReadOnlyCollection<Guid>? SelectedIds
    {
        get => (IReadOnlyCollection<Guid>?)GetValue(SelectedIdsProperty);
        set => SetValue(SelectedIdsProperty, value);
    }

    public Vector3 BuildVolume
    {
        get => (Vector3)GetValue(BuildVolumeProperty);
        set => SetValue(BuildVolumeProperty, value);
    }

    /// <summary>Executed with a <see cref="PickRequest"/> when the user clicks in the view.</summary>
    public ICommand? PickCommand
    {
        get => (ICommand?)GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    public void SetView(ViewPreset preset) => GlideTo(_orbit.PresetState(preset));

    /// <summary>Frames the selection, or everything when nothing is selected, or the build plate when the scene is empty.</summary>
    public void FrameSelection()
    {
        var ids = SelectedIds;
        var items = (Items ?? []).Where(i => ids is { Count: > 0 } ? ids.Contains(i.NodeId) : i.Kind == ViewportItemKind.Solid).ToList();
        if (items.Count == 0)
        {
            var v = BuildVolume;
            GlideTo(_orbit.FramedState(new Bounds(new Vector3(-v.X / 2, -v.Y / 2, 0), new Vector3(v.X / 2, v.Y / 2, Math.Min(v.Z, 60)))));
        }
        else
        {
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var item in items)
            {
                if (item.Mesh.IsEmpty) continue;
                var b = item.Mesh.ComputeBounds();
                min = Vector3.Min(min, b.Min);
                max = Vector3.Max(max, b.Max);
            }

            if (min.X <= max.X) GlideTo(_orbit.FramedState(new Bounds(min, max)));
        }
    }

    private void UpdateCamera()
    {
        _orbit.IsOrthographic = Orthographic;
        var position = _orbit.Position;
        var forward = _orbit.Forward;
        ProjectionCamera active = Orthographic ? _orthographicCamera : _camera;
        active.Position = new Point3D(position.X, position.Y, position.Z);
        active.LookDirection = new Vector3D(forward.X, forward.Y, forward.Z);
        active.NearPlaneDistance = Math.Max(_orbit.Distance * 0.01, 0.1);
        active.FarPlaneDistance = _orbit.Distance * 20 + 5000;
        _camera.FieldOfView = OrbitCamera.FieldOfViewDegrees;
        _orthographicCamera.Width = 2 * _orbit.Distance * Math.Tan(OrbitCamera.FieldOfViewDegrees * Math.PI / 360); // as wide at the target as the perspective view
        UpdateCubeCamera();
        if (!ReferenceEquals(_viewport.Camera, active))
        {
            _viewport.Camera = active;
            _gizmoViewport.Camera = active; // the handles are drawn with the same camera
        }

        // Key light comes from the upper left of the viewer.
        var light = forward + _orbit.Right * 0.6f - _orbit.Up * 0.8f;
        _keyLight.Direction = new Vector3D(light.X, light.Y, light.Z);
        UpdateGizmoTransform();
        UpdateEdgeWidth();
    }

    // ---- scene -------------------------------------------------------------------------------

    private void RebuildScene()
    {
        _models.Clear();
        var group = new Model3DGroup();
        var items = Items ?? [];

        // Opaque first, transparent ghosts last so that they blend over the solids.
        foreach (var item in items.Where(i => i.Kind == ViewportItemKind.Solid).Concat(items.Where(i => i.Kind == ViewportItemKind.Ghost)))
        {
            if (item.Mesh.IsEmpty) continue;
            var geometry = MeshConverter.ToGeometry(item.Mesh);
            var periods = 0;
            if (item.Kind == ViewportItemKind.Ghost && !item.Dimmed)
            {
                // hatching (F-VIS-04): the stripe coordinate comes from the world position, mapped onto 0..1 (WPF clamps outside it)
                geometry = geometry.Clone(); // the converter's geometry is frozen; a copy takes the texture coordinates
                var coordinates = geometry.Positions.Select(p => GhostHatch.Coordinate(new Vector3((float)p.X, (float)p.Y, (float)p.Z))).ToList();
                var layout = GhostHatch.LayoutFor(coordinates);
                foreach (var c in coordinates) geometry.TextureCoordinates.Add(new Point(layout.Map(c), 0.5));
                periods = layout.Periods;
                geometry.Freeze();
            }

            var model = new GeometryModel3D { Geometry = geometry };
            _models[model] = new ModelInfo(model, item, periods);
            group.Children.Add(model);
        }

        _partsVisual.Content = group;
        ApplyMaterials();
        RebuildEdges();
        ApplyPreview(); // a scene that arrives while an object is being dragged keeps showing it where the mouse has it
    }

    private void ApplyMaterials()
    {
        var selected = SelectedIds;
        foreach (var info in _models.Values)
        {
            var isSelected = selected?.Contains(info.Item.NodeId) == true;
            var material = GetMaterial(info.Item.ColorArgb, isSelected, info.Item.Kind, info.Item.Outside, DisplayMode, info.Item.Dimmed, info.Hatch);
            info.Model.Material = material;
            info.Model.BackMaterial = material;
        }
    }

    private Material GetMaterial(uint argb, bool selected, ViewportItemKind kind, bool outside, ViewportDisplayMode mode, bool dimmed, int hatch)
    {
        if (_materials.TryGetValue((argb, selected, kind, outside, mode, dimmed, hatch), out var cached)) return cached;

        Material material;
        if (dimmed)
        {
            // outside the group that is open: a faint, see-through shape
            var faint = kind == ViewportItemKind.Ghost ? GhostColor : Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(40, faint.R, faint.G, faint.B)));
        }
        else if (kind == ViewportItemKind.Ghost)
        {
            var color = selected ? Accent : GhostColor;
            material = new DiffuseMaterial(HatchBrush(color, selected, Math.Max(hatch, 1)));
        }
        else
        {
            var baseColor = Color.FromRgb((byte)(argb >> 16), (byte)(argb >> 8), (byte)argb);
            if (outside) baseColor = Blend(baseColor, OutsideColor, 0.6);
            var surface = selected ? Blend(baseColor, Accent, 0.45) : baseColor;
            var opacity = SurfaceOpacity(mode, selected);
            if (opacity < 255)
            {
                // wireframe and X-ray: a see-through surface
                material = new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(opacity, surface.R, surface.G, surface.B)));
            }
            else
            {
                var group = new MaterialGroup();
                group.Children.Add(new DiffuseMaterial(new SolidColorBrush(surface)));
                if (selected) group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0x10, 0x28, 0x48))));
                material = group;
            }
        }

        material.Freeze();
        _materials[(argb, selected, kind, outside, mode, dimmed, hatch)] = material;
        return material;
    }

    /// <summary>
    /// Stripes of a see-through color: half of each period stronger, half faint (the ghost stays visible and selectable between
    /// them). One gradient with hard edges across the 0..1 texture range; tiled brushes are not drawn in 3D here.
    /// </summary>
    private static Brush HatchBrush(Color color, bool selected, int periods)
    {
        var strong = Color.FromArgb(selected ? (byte)190 : (byte)150, color.R, color.G, color.B);
        var faint = Color.FromArgb(selected ? (byte)90 : (byte)45, color.R, color.G, color.B);
        var stops = new GradientStopCollection(periods * 4);
        for (var i = 0; i < periods; i++)
        {
            double start = (double)i / periods, middle = (i + 0.5) / periods, end = (double)(i + 1) / periods;
            stops.Add(new GradientStop(strong, start));
            stops.Add(new GradientStop(strong, middle));
            stops.Add(new GradientStop(faint, middle));
            stops.Add(new GradientStop(faint, end));
        }

        var brush = new LinearGradientBrush(stops, new Point(0, 0.5), new Point(1, 0.5));
        brush.Freeze();
        return brush;
    }

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    // ---- build plate, grid and axes ------------------------------------------------------------

    /// <summary>False leaves out the axes at the origin (the pictures of templates and library elements are drawn without them).</summary>
    public bool ShowAxes
    {
        get => _showAxes;
        set
        {
            if (_showAxes == value) return;
            _showAxes = value;
            RebuildBed();
        }
    }

    private void RebuildBed()
    {
        var v = BuildVolume;
        double hx = v.X / 2, hy = v.Y / 2;
        var group = new Model3DGroup();

        var colors = LightTheme ? BedColors.Light : BedColors.Dark;
        var plate = new MeshGeometry3D();
        AddQuad(plate, new Point3D(-hx, -hy, -0.05), new Point3D(hx, -hy, -0.05), new Point3D(hx, hy, -0.05), new Point3D(-hx, hy, -0.05));
        group.Children.Add(Flat(plate, colors.Plate));

        var minor = new MeshGeometry3D();
        var major = new MeshGeometry3D();
        for (var x = 10 * Math.Ceiling(-hx / 10); x <= hx; x += 10)
            AddLine(x % 50 == 0 ? major : minor, new Point3D(x, -hy, -0.02), new Point3D(x, hy, -0.02), x % 50 == 0 ? 0.22 : 0.12);
        for (var y = 10 * Math.Ceiling(-hy / 10); y <= hy; y += 10)
            AddLine(y % 50 == 0 ? major : minor, new Point3D(-hx, y, -0.02), new Point3D(hx, y, -0.02), y % 50 == 0 ? 0.22 : 0.12);
        group.Children.Add(Flat(minor, colors.Minor));
        group.Children.Add(Flat(major, colors.Major));

        var border = new MeshGeometry3D();
        AddLine(border, new Point3D(-hx, -hy, -0.01), new Point3D(hx, -hy, -0.01), 0.6);
        AddLine(border, new Point3D(hx, -hy, -0.01), new Point3D(hx, hy, -0.01), 0.6);
        AddLine(border, new Point3D(hx, hy, -0.01), new Point3D(-hx, hy, -0.01), 0.6);
        AddLine(border, new Point3D(-hx, hy, -0.01), new Point3D(-hx, -hy, -0.01), 0.6);
        group.Children.Add(Flat(border, colors.Border));

        // Faint outline of the build volume's height, so the usable space is visible (F-SET-01).
        if (v.Z > 0)
        {
            var frame = new MeshGeometry3D();
            double z = v.Z;
            AddLine(frame, new Point3D(-hx, -hy, z), new Point3D(hx, -hy, z), 0.3);
            AddLine(frame, new Point3D(hx, -hy, z), new Point3D(hx, hy, z), 0.3);
            AddLine(frame, new Point3D(hx, hy, z), new Point3D(-hx, hy, z), 0.3);
            AddLine(frame, new Point3D(-hx, hy, z), new Point3D(-hx, -hy, z), 0.3);
            foreach (var (cx, cy) in new[] { (-hx, -hy), (hx, -hy), (hx, hy), (-hx, hy) })
                AddBox(frame, new Point3D(cx, cy, 0), new Point3D(cx, cy, z), 0.15);
            group.Children.Add(Flat(frame, colors.Frame));
        }

        // Axes: X red, Y green, Z blue (spec section 7).
        if (_showAxes)
        {
            group.Children.Add(Flat(Axis(new Vector3D(1, 0, 0)), Color.FromRgb(0xE5, 0x53, 0x4B)));
            group.Children.Add(Flat(Axis(new Vector3D(0, 1, 0)), Color.FromRgb(0x5F, 0xC2, 0x5F)));
            group.Children.Add(Flat(Axis(new Vector3D(0, 0, 1)), Color.FromRgb(0x4C, 0x8D, 0xFF)));
        }

        _bedVisual.Content = group;
    }

    private static GeometryModel3D Flat(MeshGeometry3D geometry, Color color)
    {
        geometry.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(Brushes.Black));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        material.Freeze();
        return new GeometryModel3D(geometry, material) { BackMaterial = material };
    }

    private static MeshGeometry3D Axis(Vector3D direction)
    {
        var mesh = new MeshGeometry3D();
        const double length = 40, half = 0.5;
        var start = new Point3D(0, 0, direction.Z > 0 ? 0 : half);
        var end = start + direction * length;
        AddBox(mesh, start, end, half);
        return mesh;
    }

    private static void AddBox(MeshGeometry3D mesh, Point3D a, Point3D b, double half)
    {
        var min = new Point3D(Math.Min(a.X, b.X) - half, Math.Min(a.Y, b.Y) - half, Math.Min(a.Z, b.Z) - (a.Z == b.Z ? half : 0));
        var max = new Point3D(Math.Max(a.X, b.X) + half, Math.Max(a.Y, b.Y) + half, Math.Max(a.Z, b.Z) + (a.Z == b.Z ? half : 0));
        Point3D P(double x, double y, double z) => new(x, y, z);
        AddQuad(mesh, P(min.X, min.Y, max.Z), P(max.X, min.Y, max.Z), P(max.X, max.Y, max.Z), P(min.X, max.Y, max.Z));
        AddQuad(mesh, P(min.X, min.Y, min.Z), P(min.X, max.Y, min.Z), P(max.X, max.Y, min.Z), P(max.X, min.Y, min.Z));
        AddQuad(mesh, P(min.X, min.Y, min.Z), P(max.X, min.Y, min.Z), P(max.X, min.Y, max.Z), P(min.X, min.Y, max.Z));
        AddQuad(mesh, P(max.X, min.Y, min.Z), P(max.X, max.Y, min.Z), P(max.X, max.Y, max.Z), P(max.X, min.Y, max.Z));
        AddQuad(mesh, P(max.X, max.Y, min.Z), P(min.X, max.Y, min.Z), P(min.X, max.Y, max.Z), P(max.X, max.Y, max.Z));
        AddQuad(mesh, P(min.X, max.Y, min.Z), P(min.X, min.Y, min.Z), P(min.X, min.Y, max.Z), P(min.X, max.Y, max.Z));
    }

    private static void AddLine(MeshGeometry3D mesh, Point3D from, Point3D to, double width)
    {
        var dir = to - from;
        var side = Vector3D.CrossProduct(dir, new Vector3D(0, 0, 1));
        side.Normalize();
        side *= width / 2;
        AddQuad(mesh, from - side, from + side, to + side, to - side);
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        var i = mesh.Positions.Count;
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.Positions.Add(d);
        foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(i + index);
    }

    // ---- input ---------------------------------------------------------------------------------

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        ClearSnapHover();
        // before StopGlide: a click on the cube starts its own glide, and the second click of a double click must leave it running
        if (e.ChangedButton == MouseButton.Left && TryClickViewCube(e.GetPosition(this), e.ClickCount))
        {
            e.Handled = true;
            return;
        }

        StopGlide();
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && !FacePickMode && OpenGroupCommand is not null)
        {
            // double click (before the handles, the second click often lands on one of them): open the group of the part under the mouse, or leave the open group on empty space (F-BOO-04)
            Guid? id = ItemAt(e.GetPosition(_viewport))?.Item.NodeId;
            if (OpenGroupCommand.CanExecute(id)) OpenGroupCommand.Execute(id);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && !FacePickMode && TryBeginGizmoDrag(e.GetPosition(this)))
        {
            e.Handled = true;
            return;
        }

        _lastMouse = _downMouse = e.GetPosition(this);
        if (e.ChangedButton == MouseButton.Left)
        {
            _leftDown = true;
            _dragged = false;
        }

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_gizmoDragging)
        {
            UpdateGizmoDrag(e.GetPosition(this));
            return;
        }

        if (!IsMouseCaptured)
        {
            UpdateCubeHover(e.GetPosition(this));
            UpdateSnapHover(e.GetPosition(this));
            if (FacePickMode)
            {
                UpdateFaceHover(e.GetPosition(this));
                return; // the cross cursor stays, no handle is highlighted
            }

            ClearFaceHover();
            UpdateGizmoHover(e.GetPosition(this));
            return;
        }

        var p = e.GetPosition(this);
        var dx = p.X - _lastMouse.X;
        var dy = p.Y - _lastMouse.Y;
        _lastMouse = p;

        if (e.MiddleButton == MouseButtonState.Pressed || (e.RightButton == MouseButtonState.Pressed && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            _orbit.Pan(dx, dy, ActualWidth);
        }
        else if (e.RightButton == MouseButtonState.Pressed)
        {
            _orbit.Orbit(dx, dy);
        }
        else if (_leftDown)
        {
            if (!_dragged && (Math.Abs(p.X - _downMouse.X) > 4 || Math.Abs(p.Y - _downMouse.Y) > 4)) _dragged = true;
            if (_dragged && IsMarqueeTool)
            {
                UpdateMarquee(p);
                return;
            }

            if (_dragged) _orbit.Orbit(dx, dy);
            else return;
        }
        else
        {
            return;
        }

        UpdateCamera();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_gizmoDragging && e.ChangedButton == MouseButton.Left)
        {
            EndGizmoDrag(commit: true);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left && _leftDown)
        {
            _leftDown = false;
            if (_marqueeActive) EndMarquee(e.GetPosition(this));
            else if (!_dragged) Pick(e.GetPosition(_viewport));
        }

        if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released && e.MiddleButton == MouseButtonState.Released)
            ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        StopGlide();
        var at = e.GetPosition(this);
        _orbit.ZoomAt(e.Delta / 120.0, at.X, at.Y, ActualWidth, ActualHeight); // towards the mouse
        UpdateCamera();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.OriginalSource is System.Windows.Controls.Primitives.TextBoxBase) return; // typing into the dimension box is not a shortcut
        if (e.Key == Key.Escape && FacePickMode)
        {
            FacePickCommand?.Execute(null); // Esc leaves the mode
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _marqueeActive)
        {
            CancelMarquee();
            _leftDown = false; // the release of the button must not click
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && _gizmoDragging)
        {
            EndGizmoDrag(commit: false);
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None) return;

        switch (e.Key)
        {
            case Key.F: FrameSelection(); break;
            case Key.D1: SetView(ViewPreset.Front); break;
            case Key.D2: SetView(ViewPreset.Back); break;
            case Key.D3: SetView(ViewPreset.Left); break;
            case Key.D4: SetView(ViewPreset.Right); break;
            case Key.D5: SetView(ViewPreset.Top); break;
            case Key.D6: SetView(ViewPreset.Bottom); break;
            case Key.D7: SetView(ViewPreset.Isometric); break;
            default: return;
        }

        e.Handled = true;
    }

    /// <summary>The item under the mouse: ghosts win over solids so that holes inside a body stay selectable (F-VIS-04).</summary>
    private ModelInfo? ItemAt(Point position)
    {
        var hits = new List<(ModelInfo Info, double Distance)>();
        VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult ray && _models.TryGetValue(ray.ModelHit, out var info))
                hits.Add((info, ray.DistanceToRayOrigin));
            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(position));

        return hits.Where(h => h.Info.Item.Kind == ViewportItemKind.Ghost).OrderBy(h => h.Distance).Select(h => h.Info).FirstOrDefault()
               ?? hits.OrderBy(h => h.Distance).Select(h => h.Info).FirstOrDefault();
    }

    private void Pick(Point position)
    {
        if (FacePickMode)
        {
            PickFace(position);
            return;
        }

        var best = ItemAt(position);
        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var request = new PickRequest(best?.Item.NodeId, additive);
        if (PickCommand?.CanExecute(request) == true) PickCommand.Execute(request);
    }
}
