using System.Diagnostics;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>
/// An edge that the chamfer / fillet tool shows under the mouse (F-EDG-02): a line or a closed outline in world space. <paramref name="Valid"/>:
/// a click would cut it (drawn yellow); otherwise it is drawn grey.
/// </summary>
public sealed record ViewportEdgeHighlight(IReadOnlyList<Vector3> Points, bool Closed, bool Valid);

/// <summary>The edge under the mouse in face pick mode (F-EDG-02): reports the face under the mouse and draws the edge the app sends back.</summary>
public sealed partial class ViewportControl
{
    private static readonly Color EdgeValidColor = Color.FromRgb(0xFF, 0xD1, 0x4C);
    private static readonly Color EdgeValidLightColor = Color.FromRgb(0xE0, 0x8A, 0x00);
    private static readonly Color EdgeInvalidColor = Color.FromRgb(0x96, 0x9A, 0xA2);
    private static readonly Color EdgeInvalidLightColor = Color.FromRgb(0x70, 0x76, 0x80);

    public static readonly DependencyProperty EdgeHighlightProperty = DependencyProperty.Register(
        nameof(EdgeHighlight), typeof(ViewportEdgeHighlight), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    public static readonly DependencyProperty FaceHoverCommandProperty = DependencyProperty.Register(
        nameof(FaceHoverCommand), typeof(ICommand), typeof(ViewportControl));

    private readonly Stopwatch _faceHoverClock = Stopwatch.StartNew();
    private bool _faceHoverActive;

    /// <summary>The edge to draw over the scene (null: none).</summary>
    public ViewportEdgeHighlight? EdgeHighlight
    {
        get => (ViewportEdgeHighlight?)GetValue(EdgeHighlightProperty);
        set => SetValue(EdgeHighlightProperty, value);
    }

    /// <summary>Executed with the <see cref="FacePick"/> under the mouse while the face pick mode is on (about every 40 ms), and with null when there is none.</summary>
    public ICommand? FaceHoverCommand
    {
        get => (ICommand?)GetValue(FaceHoverCommandProperty);
        set => SetValue(FaceHoverCommandProperty, value);
    }

    /// <summary>Tells the app which face the mouse is over. Throttled: a hit test per mouse move would be too much for big scenes.</summary>
    private void UpdateFaceHover(Point position)
    {
        if (FaceHoverCommand is null) return;
        if (!HoverMarkersAllowed(SceneTriangleCount()))
        {
            ClearFaceHover();
            return;
        }

        if (_faceHoverClock.ElapsedMilliseconds < 40) return;
        _faceHoverClock.Restart();

        var pick = HitFace(position);
        if (pick is null)
        {
            ClearFaceHover();
            return;
        }

        _faceHoverActive = true;
        if (FaceHoverCommand.CanExecute(pick)) FaceHoverCommand.Execute(pick);
    }

    /// <summary>The mouse left the faces (or the view): the app takes its highlight back.</summary>
    private void ClearFaceHover()
    {
        if (!_faceHoverActive) return;
        _faceHoverActive = false;
        if (FaceHoverCommand?.CanExecute(null) == true) FaceHoverCommand.Execute(null);
    }

    /// <summary>Adds the highlighted edge to the marker overlay: thin bars along the outline.</summary>
    private void AddEdgeHighlight(Model3DGroup group)
    {
        if (EdgeHighlight is not { Points.Count: >= 2 } highlight) return;

        var mesh = new MeshGeometry3D();
        var half = _orbit.Distance * 0.0022;
        var points = highlight.Points;
        var count = highlight.Closed ? points.Count : points.Count - 1;
        for (var i = 0; i < count; i++) AddBar(mesh, points[i], points[(i + 1) % points.Count], half);

        var color = highlight.Valid
            ? (LightTheme ? EdgeValidLightColor : EdgeValidColor)
            : (LightTheme ? EdgeInvalidLightColor : EdgeInvalidColor);
        group.Children.Add(MarkerModel(mesh, color));
    }

    /// <summary>A bar with a square cross section of half-size <paramref name="half"/> from a to b, a little longer at both ends so that bars in a row have no gaps.</summary>
    private static void AddBar(MeshGeometry3D mesh, Vector3 a, Vector3 b, double half)
    {
        var d = b - a;
        if (d.LengthSquared() < 1e-12f)
        {
            AddMarker(mesh, a, half);
            return;
        }

        var direction = Vector3.Normalize(d);
        var helper = MathF.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(direction, helper)) * (float)half;
        var v = Vector3.Normalize(Vector3.Cross(direction, u)) * (float)half;
        a -= direction * (float)half;
        b += direction * (float)half;

        static Point3D P(Vector3 p) => new(p.X, p.Y, p.Z);
        var c = new[] { a - u - v, a + u - v, a + u + v, a - u + v, b - u - v, b + u - v, b + u + v, b - u + v };
        AddQuad(mesh, P(c[0]), P(c[1]), P(c[2]), P(c[3]));
        AddQuad(mesh, P(c[7]), P(c[6]), P(c[5]), P(c[4]));
        AddQuad(mesh, P(c[0]), P(c[4]), P(c[5]), P(c[1]));
        AddQuad(mesh, P(c[1]), P(c[5]), P(c[6]), P(c[2]));
        AddQuad(mesh, P(c[2]), P(c[6]), P(c[7]), P(c[3]));
        AddQuad(mesh, P(c[3]), P(c[7]), P(c[4]), P(c[0]));
    }
}
