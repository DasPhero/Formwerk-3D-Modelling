using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>Edge lines and display modes of the view (F-VIS-02, F-VIS-03).</summary>
public sealed partial class ViewportControl
{
    /// <summary>Above this many triangles in the scene no edges are drawn (extracting and building them would make the view sluggish).</summary>
    public const int EdgeTriangleLimit = 200_000;

    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(ViewportDisplayMode), typeof(ViewportControl),
        new PropertyMetadata(ViewportDisplayMode.ShadedWithEdges, (d, _) => ((ViewportControl)d).OnDisplayModeChanged()));

    public static readonly DependencyProperty EdgeAngleProperty = DependencyProperty.Register(
        nameof(EdgeAngle), typeof(double), typeof(ViewportControl),
        new PropertyMetadata(30.0, (d, _) => ((ViewportControl)d).RebuildEdges()));

    private readonly ModelVisual3D _edgesVisual = new();
    private readonly Dictionary<(byte, byte, byte), Material> _edgeMaterials = [];

    // Edge sets per mesh instance: the evaluator hands out the same mesh again as long as an object does not change.
    private readonly ConditionalWeakTable<MeshData, Dictionary<double, EdgeSet>> _sharpEdges = [];
    private readonly ConditionalWeakTable<MeshData, EdgeSet> _allEdges = [];

    private double _edgeHalfWidth;

    public ViewportDisplayMode DisplayMode
    {
        get => (ViewportDisplayMode)GetValue(DisplayModeProperty);
        set => SetValue(DisplayModeProperty, value);
    }

    /// <summary>Fold angle in degrees from which the edge between two faces is drawn.</summary>
    public double EdgeAngle
    {
        get => (double)GetValue(EdgeAngleProperty);
        set => SetValue(EdgeAngleProperty, value);
    }

    private void OnDisplayModeChanged()
    {
        ApplyMaterials();
        RebuildEdges();
    }

    private double WantedEdgeHalfWidth() =>
        EdgeGeometry.HalfWidth(_orbit.WorldPerPixelAt(_orbit.Target, Math.Max(ActualWidth, 800)));

    /// <summary>Called when the camera moved: the lines are rebuilt only if the zoom changed enough for them to look too thick or thin.</summary>
    private void UpdateEdgeWidth()
    {
        if (_edgesVisual.Content is not null && EdgeGeometry.NeedsRebuild(_edgeHalfWidth, WantedEdgeHalfWidth())) RebuildEdges();
    }

    private void RebuildEdges()
    {
        _edgeHalfWidth = 0;
        _edgesVisual.Content = null;
        _edgeModels.Clear();
        if (DisplayMode == ViewportDisplayMode.Shaded) return;

        var items = (Items ?? []).Where(i => i.Kind == ViewportItemKind.Solid && !i.Mesh.IsEmpty && !i.Dimmed).ToList();
        if (items.Sum(i => (long)i.Mesh.TriangleCount) > EdgeTriangleLimit) return;

        var halfWidth = WantedEdgeHalfWidth();
        if (halfWidth <= 0) return;

        // One model per object, so that a dragged object takes its lines along (see ApplyPreview) without rebuilding them.
        var selected = SelectedIds;
        var content = new Model3DGroup();
        foreach (var item in items)
        {
            var color = EdgeColors.For(item.ColorArgb, selected?.Contains(item.NodeId) == true, seeThrough: DisplayMode == ViewportDisplayMode.Wireframe);
            var material = EdgeMaterial(color);
            var model = new GeometryModel3D(EdgeGeometry.Build([EdgesOf(item.Mesh)], halfWidth), material) { BackMaterial = material };
            content.Children.Add(model);
            if (!_edgeModels.TryGetValue(item.NodeId, out var models)) _edgeModels[item.NodeId] = models = [];
            models.Add(model);
        }

        _edgesVisual.Content = content;
        _edgeHalfWidth = halfWidth;
        ApplyPreview();
    }

    private EdgeSet EdgesOf(MeshData mesh)
    {
        if (DisplayMode == ViewportDisplayMode.Wireframe)
        {
            if (!_allEdges.TryGetValue(mesh, out var all)) _allEdges.Add(mesh, all = FeatureEdges.AllEdges(mesh));
            return all;
        }

        var perAngle = _sharpEdges.GetOrCreateValue(mesh);
        if (!perAngle.TryGetValue(EdgeAngle, out var sharp)) perAngle[EdgeAngle] = sharp = FeatureEdges.Extract(mesh, EdgeAngle);
        return sharp;
    }

    /// <summary>A color that does not react to light, so that lines look the same from every side.</summary>
    private Material EdgeMaterial((byte R, byte G, byte B) color)
    {
        if (_edgeMaterials.TryGetValue(color, out var cached)) return cached;
        var group = new MaterialGroup();
        group.Children.Add(new DiffuseMaterial(Brushes.Black));
        group.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B))));
        group.Freeze();
        return _edgeMaterials[color] = group;
    }

    /// <summary>How opaque the surface of a solid is in the current mode (0 to 255).</summary>
    private static byte SurfaceOpacity(ViewportDisplayMode mode, bool selected) => mode switch
    {
        ViewportDisplayMode.Wireframe => selected ? (byte)40 : (byte)14, // almost invisible, but still hit by clicks
        ViewportDisplayMode.XRay => selected ? (byte)120 : (byte)80,
        _ => 255,
    };
}
