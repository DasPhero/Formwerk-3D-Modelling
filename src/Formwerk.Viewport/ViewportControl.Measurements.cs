using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>
/// A measurement shown in the view (E-07): little cubes at its <see cref="Marks"/>, bars along its <see cref="Segments"/>, and the
/// <see cref="Text"/> (when there is one) next to <see cref="LabelPoint"/>. A measurement that is still being made has marks only.
/// </summary>
public sealed record ViewportMeasurement(IReadOnlyList<Vector3> Marks, IReadOnlyList<(Vector3 From, Vector3 To)> Segments, string? Text, Vector3 LabelPoint);

/// <summary>Draws the measurements of the measuring tool over the scene: marks and bars in the marker overlay, the values as labels that keep their size on screen.</summary>
public sealed partial class ViewportControl
{
    private static readonly Color MeasureColor = Color.FromRgb(0x4C, 0xD2, 0xFF);
    private static readonly Color MeasureLightColor = Color.FromRgb(0x00, 0x7A, 0xB8);

    public static readonly DependencyProperty MeasurementsProperty = DependencyProperty.Register(
        nameof(Measurements), typeof(IReadOnlyList<ViewportMeasurement>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).UpdateGizmoTransform()));

    private readonly Canvas _measureLabels = new() { IsHitTestVisible = false };

    /// <summary>The measurements to draw (null or empty: none).</summary>
    public IReadOnlyList<ViewportMeasurement>? Measurements
    {
        get => (IReadOnlyList<ViewportMeasurement>?)GetValue(MeasurementsProperty);
        set => SetValue(MeasurementsProperty, value);
    }

    private void InitializeMeasurements() => Children.Add(_measureLabels);

    /// <summary>Adds the marks and the bars to the marker overlay, and puts the labels where the measurements are on the screen now.</summary>
    private void AddMeasurements(Model3DGroup group)
    {
        var measurements = Measurements ?? [];
        var mesh = new MeshGeometry3D();
        var bar = _orbit.Distance * 0.0016;
        var mark = _orbit.Distance * 0.004;
        foreach (var measurement in measurements)
        {
            foreach (var point in measurement.Marks) AddMarker(mesh, point, mark);
            foreach (var (from, to) in measurement.Segments) AddBar(mesh, from, to, bar);
        }

        if (mesh.Positions.Count > 0) group.Children.Add(MarkerModel(mesh, LightTheme ? MeasureLightColor : MeasureColor));
        RebuildMeasureLabels(measurements);
    }

    private void RebuildMeasureLabels(IReadOnlyList<ViewportMeasurement> measurements)
    {
        _measureLabels.Children.Clear();
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);
        foreach (var measurement in measurements)
        {
            if (string.IsNullOrEmpty(measurement.Text) || _orbit.Project(measurement.LabelPoint, width, height) is not { } p) continue;

            var foreground = new SolidColorBrush(LightTheme ? MeasureLightColor : MeasureColor);
            var label = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x23, 0x26, 0x2B)),
                BorderBrush = foreground,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(5, 1, 5, 1),
                Child = new TextBlock { Text = measurement.Text, Foreground = new SolidColorBrush(Color.FromRgb(0xE8, 0xF4, 0xFF)), FontSize = 12 },
            };
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, p.Y - label.DesiredSize.Height - 6);
            _measureLabels.Children.Add(label);
        }
    }
}
