using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>The places of one kind of problem of a mesh (F-REP-01): bars along edges and cubes at points, in world coordinates.</summary>
public sealed record ViewportProblemMarks(IReadOnlyList<(Vector3 From, Vector3 To)> Segments, IReadOnlyList<Vector3> Points);

/// <summary>Draws the places of a problem in red over the scene; the bars and cubes keep their size on screen.</summary>
public sealed partial class ViewportControl
{
    private static readonly Color ProblemColor = Color.FromRgb(0xFF, 0x4D, 0x42);
    private static readonly Color ProblemLightColor = Color.FromRgb(0xC6, 0x28, 0x28);

    public static readonly DependencyProperty ProblemMarksProperty = DependencyProperty.Register(
        nameof(ProblemMarks), typeof(ViewportProblemMarks), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    /// <summary>The problem places to draw (null: none).</summary>
    public ViewportProblemMarks? ProblemMarks
    {
        get => (ViewportProblemMarks?)GetValue(ProblemMarksProperty);
        set => SetValue(ProblemMarksProperty, value);
    }

    /// <summary>Adds the red bars and cubes to the marker overlay.</summary>
    private void AddProblemMarks(Model3DGroup group)
    {
        if (ProblemMarks is not { } marks || (marks.Segments.Count == 0 && marks.Points.Count == 0)) return;

        var mesh = new MeshGeometry3D();
        var bar = _orbit.Distance * 0.0022;
        var mark = _orbit.Distance * 0.0055;
        foreach (var (from, to) in marks.Segments) AddBar(mesh, from, to, bar);
        foreach (var point in marks.Points) AddMarker(mesh, point, mark);
        group.Children.Add(MarkerModel(mesh, LightTheme ? ProblemLightColor : ProblemColor));
    }
}
