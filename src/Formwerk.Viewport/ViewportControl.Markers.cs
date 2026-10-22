using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Formwerk.Core.Model;

namespace Formwerk.Viewport;

/// <summary>
/// Snap point markers (F-SNP-01). Hovering an object shows its 27 snap points (corners, edge middles, face centers, center; the
/// one near the mouse is highlighted); a move drag that hits a point of another object shows that point (<see cref="SnapMarkers"/>).
/// Drawn on top of the scene like the handles.
/// </summary>
public sealed partial class ViewportControl
{
    private const double HoverPixelTolerance = 12;

    /// <summary>
    /// Above this many triangles in the scene the hover markers are off: every mouse move would hit test all of them (WPF takes about 40 ms
    /// for a million triangles, measured by the speed tests), which would make the mouse feel sticky. Clicks and drags are not affected.
    /// </summary>
    public const int HoverMarkerTriangleLimit = 300_000;

    public static bool HoverMarkersAllowed(int sceneTriangles) => sceneTriangles <= HoverMarkerTriangleLimit;
    private static readonly Color HoverMarkerColor = Color.FromRgb(0xD8, 0xDE, 0xE9);
    private static readonly Color SnapMarkerColor = Color.FromRgb(0xFF, 0xD1, 0x4C);

    public static readonly DependencyProperty SnapMarkersProperty = DependencyProperty.Register(
        nameof(SnapMarkers), typeof(IReadOnlyList<Vector3>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    private readonly ModelVisual3D _markerVisual = new();
    private readonly Stopwatch _hoverClock = Stopwatch.StartNew();
    private Guid? _hoverNode;
    private IReadOnlyList<Vector3> _hoverPoints = [];
    private int _hoverNearest = -1;

    /// <summary>World positions of snap points to show as markers (the point a drag has snapped to).</summary>
    public IReadOnlyList<Vector3>? SnapMarkers
    {
        get => (IReadOnlyList<Vector3>?)GetValue(SnapMarkersProperty);
        set => SetValue(SnapMarkersProperty, value);
    }

    private void InitializeMarkers() => _gizmoViewport.Children.Add(_markerVisual);

    /// <summary>The snap points of the object under the mouse. Throttled: a hit test per mouse move would be too much for big meshes.</summary>
    private void UpdateSnapHover(Point position)
    {
        if (ActiveTool != ViewportTool.Move || FacePickMode || _gizmoDragging || !HoverMarkersAllowed(SceneTriangleCount()))
        {
            ClearSnapHover();
            return;
        }

        if (_hoverClock.ElapsedMilliseconds < 40) return;
        _hoverClock.Restart();

        var hit = ItemAt(position);
        if (hit is null || hit.Item.Dimmed)
        {
            ClearSnapHover();
            return;
        }

        if (_hoverNode != hit.Item.NodeId)
        {
            var box = BoundsOfNode(hit.Item.NodeId);
            _hoverNode = hit.Item.NodeId;
            _hoverPoints = box is { } b ? ObjectSnapper.Lattice(b) : [];
        }

        var nearest = -1;
        var best = HoverPixelTolerance;
        for (var i = 0; i < _hoverPoints.Count; i++)
        {
            if (_orbit.Project(_hoverPoints[i], Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1)) is not { } p) continue;
            var distance = Math.Sqrt((p.X - position.X) * (p.X - position.X) + (p.Y - position.Y) * (p.Y - position.Y));
            if (distance < best)
            {
                best = distance;
                nearest = i;
            }
        }

        if (nearest == _hoverNearest && _markerVisual.Content is not null) return;
        _hoverNearest = nearest;
        UpdateGizmoTransform();
    }

    private int _sceneTrianglesFor = -1;
    private int _sceneTriangles;

    /// <summary>The number of triangles shown, counted again only when the items changed.</summary>
    private int SceneTriangleCount()
    {
        var items = Items;
        if (items is null) return 0;
        if (_sceneTrianglesFor != System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(items))
        {
            _sceneTriangles = items.Sum(i => i.Mesh.TriangleCount);
            _sceneTrianglesFor = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(items);
        }

        return _sceneTriangles;
    }

    private void ClearSnapHover()
    {
        if (_hoverNode is null && _hoverPoints.Count == 0) return;
        _hoverNode = null;
        _hoverPoints = [];
        _hoverNearest = -1;
        UpdateGizmoTransform();
    }

    private SnapBox? BoundsOfNode(Guid id)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var any = false;
        foreach (var item in Items ?? [])
        {
            if (item.NodeId != id || item.Mesh.IsEmpty) continue;
            var bounds = item.Mesh.ComputeBounds();
            min = Vector3.Min(min, bounds.Min);
            max = Vector3.Max(max, bounds.Max);
            any = true;
        }

        return any ? new SnapBox(min, max) : null;
    }

    /// <summary>Little cubes at the points; their size follows the zoom so that they stay about the same on screen.</summary>
    private void RebuildMarkers()
    {
        var plain = new MeshGeometry3D();
        var hot = new MeshGeometry3D();
        var size = _orbit.Distance * 0.0045;

        for (var i = 0; i < _hoverPoints.Count; i++)
            AddMarker(i == _hoverNearest ? hot : plain, _hoverPoints[i], i == _hoverNearest ? size * 1.9 : size);
        foreach (var point in SnapMarkers ?? []) AddMarker(hot, point, size * 1.9);

        var group = new Model3DGroup();
        if (plain.Positions.Count > 0) group.Children.Add(MarkerModel(plain, LightTheme ? Color.FromRgb(0x4A, 0x52, 0x60) : HoverMarkerColor));
        if (hot.Positions.Count > 0) group.Children.Add(MarkerModel(hot, LightTheme ? Color.FromRgb(0xE0, 0x8A, 0x00) : SnapMarkerColor));
        AddEdgeHighlight(group);
        AddMeasurements(group);
        AddProblemMarks(group);
        _markerVisual.Content = group.Children.Count > 0 ? group : null;
    }

    private static void AddMarker(MeshGeometry3D mesh, Vector3 point, double size) =>
        AddBox(mesh, new Point3D(point.X, point.Y, point.Z), new Point3D(point.X, point.Y, point.Z), size); // a cube with half-size `size`

    private static GeometryModel3D MarkerModel(MeshGeometry3D mesh, Color color)
    {
        mesh.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(Brushes.Black));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(color)));
        material.Freeze();
        return new GeometryModel3D(mesh, material) { BackMaterial = material };
    }
}
