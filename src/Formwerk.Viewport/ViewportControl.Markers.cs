using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Formwerk.Core.Model;

namespace Formwerk.Viewport;

/// <summary>
/// Snap point marker (F-SNP-01): a move drag that hits a point of another object shows that point (<see cref="SnapMarkers"/>). Hovering
/// an object shows nothing (the 27 white points it showed once looked like handles that do nothing). Drawn on top of the scene like
/// the handles; the same visual carries the edge highlight, the measurements and the marks of mesh problems.
/// </summary>
public sealed partial class ViewportControl
{
    /// <summary>
    /// Above this many triangles in the scene the hover markers are off: every mouse move would hit test all of them (WPF takes about 40 ms
    /// for a million triangles, measured by the speed tests), which would make the mouse feel sticky. Clicks and drags are not affected.
    /// </summary>
    public const int HoverMarkerTriangleLimit = 300_000;

    public static bool HoverMarkersAllowed(int sceneTriangles) => sceneTriangles <= HoverMarkerTriangleLimit;
    private static readonly Color SnapMarkerColor = Color.FromRgb(0xFF, 0xD1, 0x4C);

    public static readonly DependencyProperty SnapMarkersProperty = DependencyProperty.Register(
        nameof(SnapMarkers), typeof(IReadOnlyList<Vector3>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    private readonly ModelVisual3D _markerVisual = new();

    /// <summary>World positions of snap points to show as markers (the point a drag has snapped to).</summary>
    public IReadOnlyList<Vector3>? SnapMarkers
    {
        get => (IReadOnlyList<Vector3>?)GetValue(SnapMarkersProperty);
        set => SetValue(SnapMarkersProperty, value);
    }

    private void InitializeMarkers() => _gizmoViewport.Children.Add(_markerVisual);

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

    /// <summary>Little cubes at the points; their size follows the zoom so that they stay about the same on screen.</summary>
    private void RebuildMarkers()
    {
        var hot = new MeshGeometry3D();
        var size = _orbit.Distance * 0.0045;

        foreach (var point in SnapMarkers ?? []) AddMarker(hot, point, size * 1.9);

        var group = new Model3DGroup();
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
