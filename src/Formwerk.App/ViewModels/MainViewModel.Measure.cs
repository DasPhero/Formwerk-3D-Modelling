using System.Collections.ObjectModel;
using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>One finished measurement: what the list says and what the view draws.</summary>
public sealed record MeasurementItem(string Text, ViewportMeasurement Drawing);

/// <summary>
/// Measuring tool (E-07): distance between two points (corners, edge middles and face centers snap), distance or angle between two
/// faces, the angle of three points, the diameter of a round wall or edge. The clicks arrive as face picks; results are drawn in the
/// view and listed in the panel, to 0.01. Nothing in the document changes; the results go when the tool ends or the model changes.
/// </summary>
public sealed partial class MainViewModel
{
    private const int DistanceMode = 0, FacesMode = 1, AngleMode = 2, DiameterMode = 3;
    private const int CirclePoints = 48;

    private readonly List<(Vector3 Point, Vector3 Normal)> _measurePending = [];

    /// <summary>The measuring tool waits for clicks (it stays on until Esc or the button, so several measurements can follow).</summary>
    [ObservableProperty]
    public partial bool IsMeasuring { get; set; }

    /// <summary>0 distance of two points, 1 faces, 2 angle of three points, 3 diameter.</summary>
    [ObservableProperty]
    public partial int MeasureMode { get; set; }

    public IReadOnlyList<AxisChoice> MeasureModes { get; } =
        [new(0, Texts.Get("Measure.Mode.Distance")), new(1, Texts.Get("Measure.Mode.Faces")), new(2, Texts.Get("Measure.Mode.Angle")), new(3, Texts.Get("Measure.Mode.Diameter"))];

    /// <summary>The finished measurements, in the order they were made.</summary>
    public ObservableCollection<MeasurementItem> Measurements { get; } = [];

    /// <summary>What the view draws: the finished measurements and the points of the one that is being made.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<ViewportMeasurement> ViewportMeasurements { get; set; } = [];

    partial void OnIsMeasuringChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        if (!value) ClearMeasurements();
    }

    partial void OnMeasureModeChanged(int value)
    {
        _measurePending.Clear();
        RedrawMeasurements();
        RefreshScaleHint();
    }

    private bool CanMeasure() => _document.Root.Children.Count > 0;

    /// <summary>Starts or ends the measuring tool.</summary>
    [RelayCommand(CanExecute = nameof(CanMeasure))]
    private void Measure()
    {
        IsMeasuring = !IsMeasuring;
        if (IsMeasuring)
        {
            IsPickingFace = false;
            IsPlacingHole = false;
            IsPlacingEdgeCut = false;
            IsPickingSectionFace = false;
            IsPickingMirrorFace = false;
            IsPickingPivot = false;
            EndFaceToFaceMode();
        }

        _measurePending.Clear();
        RedrawMeasurements();
        RefreshScaleHint();
    }

    /// <summary>Removes all measurements and the points that were clicked for the next one.</summary>
    [RelayCommand]
    private void ClearMeasurements()
    {
        Measurements.Clear();
        _measurePending.Clear();
        RedrawMeasurements();
        if (IsMeasuring) RefreshScaleHint();
    }

    /// <summary>What the status bar asks for next in the current mode.</summary>
    private string MeasureHint() => MeasureMode switch
    {
        FacesMode => Texts.Get(_measurePending.Count == 0 ? "Hint.MeasureFaces1" : "Hint.MeasureFaces2"),
        AngleMode => Texts.Get(_measurePending.Count switch { 0 => "Hint.MeasureAngle1", 1 => "Hint.MeasureAngle2", _ => "Hint.MeasureAngle3" }),
        DiameterMode => Texts.Get("Hint.MeasureDiameter"),
        _ => Texts.Get(_measurePending.Count == 0 ? "Hint.MeasureDistance1" : "Hint.MeasureDistance2"),
    };

    /// <summary>
    /// A click in the tool. Points snap to the corners, the edge middles and the center of the clicked face (Alt or Shift: the clicked point
    /// as it is); faces are taken as they are; the diameter is looked for in the round wall that was clicked, or in the round edge of the
    /// flat face. A click on something that is not in the scene is ignored.
    /// </summary>
    private void MeasureClick(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is null || pick.Normal.LengthSquared() < 1e-12f) return;
        var normal = Vector3.Normalize(pick.Normal);

        switch (MeasureMode)
        {
            case DiameterMode:
                if (DiameterOf(pick, normal) is { } diameter) Measurements.Add(diameter);
                else SetNotice(Texts.Get("Msg.MeasureNoRound"));
                break;
            case FacesMode:
                _measurePending.Add((pick.Point, normal));
                if (_measurePending.Count == 2) FinishFaces();
                break;
            case AngleMode:
                _measurePending.Add((SnapMeasured(pick, normal), normal));
                if (_measurePending.Count == 3) FinishAngle();
                break;
            default:
                _measurePending.Add((SnapMeasured(pick, normal), normal));
                if (_measurePending.Count == 2) FinishDistance();
                break;
        }

        RedrawMeasurements();
        RefreshScaleHint();
    }

    private Vector3 SnapMeasured(FacePick pick, Vector3 normal)
    {
        if (pick.NoSnap) return pick.Point;
        foreach (var item in ViewportItems.Where(i => i.NodeId == pick.NodeId && i.Kind == ViewportItemKind.Solid))
        {
            var snapped = FaceFeatures.Snap(item.Mesh, pick.Point, normal);
            if (snapped.Kind != SnapKind.None) return snapped.Point;
        }

        return pick.Point;
    }

    private void FinishDistance()
    {
        var (a, b) = (_measurePending[0].Point, _measurePending[1].Point);
        _measurePending.Clear();
        var result = MeasureMath.Distance(a, b);
        var text = Texts.Format("Measure.Result.Distance", Measured(result.Length), Measured(MathF.Abs(result.Delta.X)), Measured(MathF.Abs(result.Delta.Y)), Measured(MathF.Abs(result.Delta.Z)));
        Measurements.Add(new MeasurementItem(text, new ViewportMeasurement([a, b], [(a, b)], Texts.Format("Measure.Label.Distance", Measured(result.Length)), (a + b) / 2)));
    }

    private void FinishFaces()
    {
        var ((p1, n1), (p2, n2)) = (_measurePending[0], _measurePending[1]);
        _measurePending.Clear();
        var relation = MeasureMath.Faces(p1, n1, p2, n2);
        if (relation.Parallel)
        {
            // across the faces: from the first click straight to the plane of the second face
            var end = p1 + n1 * Vector3.Dot(n1, p2 - p1);
            Measurements.Add(new MeasurementItem(
                Texts.Format("Measure.Result.FacesDistance", Measured(relation.Distance)),
                new ViewportMeasurement([p1, end], [(p1, end)], Texts.Format("Measure.Label.Distance", Measured(relation.Distance)), (p1 + end) / 2)));
            return;
        }

        Measurements.Add(new MeasurementItem(
            Texts.Format("Measure.Result.FacesAngle", Measured(relation.InsideAngle)),
            new ViewportMeasurement([p1, p2], [(p1, p2)], Texts.Format("Measure.Label.Angle", Measured(relation.InsideAngle)), (p1 + p2) / 2)));
    }

    private void FinishAngle()
    {
        Vector3 a = _measurePending[0].Point, vertex = _measurePending[1].Point, c = _measurePending[2].Point;
        _measurePending.Clear();
        var angle = MeasureMath.Angle(a, vertex, c);
        Measurements.Add(new MeasurementItem(
            Texts.Format("Measure.Result.Angle", Measured(angle)),
            new ViewportMeasurement([a, vertex, c], [(a, vertex), (vertex, c)], Texts.Format("Measure.Label.Angle", Measured(angle)), vertex)));
    }

    /// <summary>The diameter of the round wall at the click, or of the round edge of the flat face at the click.</summary>
    private MeasurementItem? DiameterOf(FacePick pick, Vector3 normal)
    {
        foreach (var item in ViewportItems.Where(i => i.NodeId == pick.NodeId && i.Kind == ViewportItemKind.Solid))
        {
            if (CylinderFinder.Find(item.Mesh, pick.Point, normal) is { } cylinder)
                return DiameterItem(cylinder.Center, cylinder.Axis, cylinder.Radius, cylinder.IsHole, pick.Point);
            if (EdgeFinder.Find(item.Mesh, pick.Point, normal).Rim is { } rim)
                return DiameterItem(rim.Center, rim.Axis, rim.Radius, rim.IsHole, pick.Point);
        }

        return null;
    }

    private static MeasurementItem DiameterItem(Vector3 center, Vector3 axis, float radius, bool isHole, Vector3 clicked)
    {
        axis = Vector3.Normalize(axis);
        var radial = clicked - center;
        radial -= axis * Vector3.Dot(radial, axis);
        if (radial.LengthSquared() < 1e-8f) radial = Vector3.Cross(axis, MathF.Abs(axis.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
        var u = Vector3.Normalize(radial);
        var v = Vector3.Cross(axis, u);

        // the circle that was measured, and the line across it through the center
        var segments = new List<(Vector3 From, Vector3 To)>();
        for (var i = 0; i < CirclePoints; i++)
        {
            double a1 = 2 * Math.PI * i / CirclePoints, a2 = 2 * Math.PI * (i + 1) / CirclePoints;
            segments.Add((center + radius * ((float)Math.Cos(a1) * u + (float)Math.Sin(a1) * v), center + radius * ((float)Math.Cos(a2) * u + (float)Math.Sin(a2) * v)));
        }

        Vector3 left = center - u * radius, right = center + u * radius;
        segments.Add((left, right));

        var diameter = Measured(radius * 2);
        var text = Texts.Format(isHole ? "Measure.Result.DiameterHole" : "Measure.Result.Diameter", diameter, Measured(radius));
        return new MeasurementItem(text, new ViewportMeasurement([left, right], segments, Texts.Format("Measure.Label.Diameter", diameter), center));
    }

    /// <summary>The finished measurements plus the points of the one that is being made.</summary>
    private void RedrawMeasurements()
    {
        var drawing = Measurements.Select(m => m.Drawing).ToList();
        if (_measurePending.Count > 0)
        {
            var points = _measurePending.Select(p => p.Point).ToList();
            var segments = new List<(Vector3, Vector3)>();
            if (MeasureMode == AngleMode)
            {
                for (var i = 0; i + 1 < points.Count; i++) segments.Add((points[i], points[i + 1]));
            }

            drawing.Add(new ViewportMeasurement(points, segments, null, points[^1]));
        }

        ViewportMeasurements = drawing;
    }

    /// <summary>A number with two decimals and a dot, and never "-0.00".</summary>
    private static string Measured(double value)
    {
        var rounded = Math.Round(value, 2);
        return (rounded == 0 ? 0 : rounded).ToString("0.00", CultureInfo.InvariantCulture);
    }
}
