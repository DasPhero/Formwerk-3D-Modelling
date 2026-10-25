using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>The names written on the faces of the ViewCube. The texts come from the application's resources.</summary>
public sealed record ViewCubeLabels(
    string Front = "Front", string Back = "Back", string Left = "Left", string Right = "Right", string Top = "Top", string Bottom = "Bottom");

/// <summary>
/// The ViewCube (F-VIS-05, F-VIS-06): a small cube in the top right corner that turns with the camera. The field under the mouse
/// lights up; a click glides to that view. It has its own little <see cref="Viewport3D"/>; the math is in <see cref="ViewCubeMath"/>.
/// </summary>
public sealed partial class ViewportControl
{
    private const double CubeSize = 112;

    private static readonly Color FaceColor = Color.FromRgb(0x5B, 0x60, 0x6B);
    private static readonly Color EdgeColor = Color.FromRgb(0x44, 0x48, 0x52);
    private static readonly Color CornerColor = Color.FromRgb(0x38, 0x3B, 0x43);

    public static readonly DependencyProperty ViewCubeLabelsProperty = DependencyProperty.Register(
        nameof(ViewCubeLabels), typeof(ViewCubeLabels), typeof(ViewportControl),
        new PropertyMetadata(new ViewCubeLabels(), (d, _) => ((ViewportControl)d).RebuildViewCube()));

    private readonly Viewport3D _cubeViewport = new()
    {
        Width = CubeSize,
        Height = CubeSize,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, 12, 12, 0),
    };

    private readonly PerspectiveCamera _cubeCamera = new() { UpDirection = new Vector3D(0, 0, 1), FieldOfView = ViewCubeMath.FieldOfViewDegrees };
    private readonly ModelVisual3D _cubeVisual = new();
    private readonly Dictionary<Model3D, ViewCubeRegion> _cubeModels = [];
    private readonly Dictionary<ViewCubeRegion, Action<bool>> _cubeShading = [];
    private ViewCubeRegion? _cubeHover;

    /// <summary>The texts on the six faces of the ViewCube.</summary>
    public ViewCubeLabels ViewCubeLabels
    {
        get => (ViewCubeLabels)GetValue(ViewCubeLabelsProperty);
        set => SetValue(ViewCubeLabelsProperty, value);
    }

    /// <summary>False hides the ViewCube (the picture of a template is drawn without it).</summary>
    public bool ShowViewCube
    {
        get => _cubeViewport.Visibility == Visibility.Visible;
        set => _cubeViewport.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }

    private void InitializeViewCube()
    {
        _cubeViewport.Camera = _cubeCamera;
        _cubeViewport.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
        _cubeViewport.Children.Add(_cubeVisual);
        RebuildViewCube();
    }

    private string LabelOf(ViewCubeRegion face) => (face.X, face.Y, face.Z) switch
    {
        (0, -1, 0) => ViewCubeLabels.Front,
        (0, 1, 0) => ViewCubeLabels.Back,
        (-1, 0, 0) => ViewCubeLabels.Left,
        (1, 0, 0) => ViewCubeLabels.Right,
        (0, 0, 1) => ViewCubeLabels.Top,
        _ => ViewCubeLabels.Bottom,
    };

    private static Color ColorOf(ViewCubeRegion region) => region.Kind switch
    {
        ViewCubeKind.Face => FaceColor,
        ViewCubeKind.Edge => EdgeColor,
        _ => CornerColor,
    };

    private void RebuildViewCube()
    {
        _cubeModels.Clear();
        _cubeShading.Clear();
        var group = new Model3DGroup();

        // one brush (and one label) per region: the three fields of a corner light up together
        var materials = new Dictionary<ViewCubeRegion, Material>();
        foreach (var region in ViewCubeRegion.All)
        {
            if (region.Kind == ViewCubeKind.Face)
            {
                var label = new Border
                {
                    Width = 96,
                    Height = 96,
                    Background = new SolidColorBrush(ColorOf(region)),
                    Child = new TextBlock
                    {
                        Text = LabelOf(region),
                        Foreground = Brushes.White,
                        FontSize = 21,
                        FontWeight = FontWeights.SemiBold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    },
                };
                label.Measure(new Size(96, 96));
                label.Arrange(new Rect(0, 0, 96, 96));
                materials[region] = new DiffuseMaterial(new VisualBrush(label));
                _cubeShading[region] = hot => label.Background = new SolidColorBrush(hot ? Accent : ColorOf(region));
            }
            else
            {
                var brush = new SolidColorBrush(ColorOf(region));
                materials[region] = new DiffuseMaterial(brush);
                _cubeShading[region] = hot => brush.Color = hot ? Accent : ColorOf(region);
            }
        }

        foreach (var quad in ViewCubeGeometry.Quads)
        {
            var mesh = new MeshGeometry3D();
            foreach (var corner in quad.Corners) mesh.Positions.Add(new Point3D(corner.X, corner.Y, corner.Z));
            foreach (var index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(index);

            // the label covers the middle field of its face, so the face texture is cut down to that field
            var inner = (double)ViewCubeGeometry.Strip;
            foreach (var uv in quad.Uv)
            {
                var span = 1 - 2 * inner;
                mesh.TextureCoordinates.Add(new Point((uv.X - inner) / span, (uv.Y - inner) / span));
            }

            var model = new GeometryModel3D(mesh, materials[quad.Region]);
            _cubeModels[model] = quad.Region;
            group.Children.Add(model);
        }

        _cubeVisual.Content = group;
        _cubeHover = null;
        UpdateCubeCamera();
    }

    /// <summary>The small camera looks along the viewing direction of the main camera, so the cube shows how the model is seen.</summary>
    private void UpdateCubeCamera()
    {
        var forward = _orbit.Forward;
        var position = ViewCubeMath.CameraPosition(forward);
        _cubeCamera.Position = new Point3D(position.X, position.Y, position.Z);
        _cubeCamera.LookDirection = new Vector3D(forward.X, forward.Y, forward.Z);
    }

    /// <summary>The field of the cube under a point of this control, or null when the point is not over the cube.</summary>
    private ViewCubeRegion? CubeRegionAt(Point position)
    {
        var local = TranslatePoint(position, _cubeViewport);
        if (local.X < 0 || local.Y < 0 || local.X > CubeSize || local.Y > CubeSize) return null;

        ViewCubeRegion? best = null;
        var bestDistance = double.MaxValue;
        VisualTreeHelper.HitTest(_cubeViewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult ray && _cubeModels.TryGetValue(ray.ModelHit, out var region)
                && ray.DistanceToRayOrigin < bestDistance)
            {
                best = region;
                bestDistance = ray.DistanceToRayOrigin;
            }

            return HitTestResultBehavior.Continue;
        }, new PointHitTestParameters(local));
        return best;
    }

    private void UpdateCubeHover(Point position) => SetCubeHover(CubeRegionAt(position));

    private void SetCubeHover(ViewCubeRegion? region)
    {
        if (_cubeHover == region) return;
        if (_cubeHover is { } old) _cubeShading[old](false);
        if (region is { } now) _cubeShading[now](true);
        _cubeHover = region;
    }

    /// <summary>A click on a field glides to the view it stands for. Returns false when the click was not on the cube.</summary>
    private bool TryClickViewCube(Point position, int clickCount)
    {
        if (!ViewCubeMath.IsActingClick(clickCount))
        {
            // the second click of a double click: swallowed when it lands on the cube (which is turning by now), so it neither
            // picks another field nor clicks into the scene behind it
            var local = TranslatePoint(position, _cubeViewport);
            return local.X >= 0 && local.Y >= 0 && local.X <= CubeSize && local.Y <= CubeSize;
        }

        if (CubeRegionAt(position) is not { } region) return false;
        GlideTo(ViewCubeMath.StateFor(region, _orbit.State));
        return true;
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SetCubeHover(null);
        ClearFaceHover();
    }
}
