using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>
/// Dimensions in the 3D view (F-PAR-02): a line with end marks along an edge of the selected object and its value next to it. A click
/// on the value opens a small input box right there; Enter reports the typed text through <see cref="DimensionCommand"/>, Esc or
/// leaving the box cancels.
/// </summary>
public sealed partial class ViewportControl
{
    private static readonly Color DimensionColor = Color.FromRgb(0xC9, 0xD4, 0xE6);

    public static readonly DependencyProperty DimensionsProperty = DependencyProperty.Register(
        nameof(Dimensions), typeof(IReadOnlyList<ViewportDimension>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).OnDimensionsChanged()));

    public static readonly DependencyProperty DimensionCommandProperty = DependencyProperty.Register(
        nameof(DimensionCommand), typeof(ICommand), typeof(ViewportControl));

    private readonly ModelVisual3D _dimensionVisual = new();
    private readonly Canvas _dimensionLabels = new();
    private readonly Dictionary<string, Border> _labelCache = [];
    private readonly TextBox _dimensionEditor = new()
    {
        Width = 78,
        Padding = new Thickness(4, 1, 4, 1),
        Visibility = Visibility.Collapsed,
        FontSize = 13,
    };

    private string? _editingDimension;

    /// <summary>The dimensions of the selected object to show; null or empty for none.</summary>
    public IReadOnlyList<ViewportDimension>? Dimensions
    {
        get => (IReadOnlyList<ViewportDimension>?)GetValue(DimensionsProperty);
        set => SetValue(DimensionsProperty, value);
    }

    /// <summary>Executed with a <see cref="DimensionEdit"/> when the user typed a value into a dimension.</summary>
    public ICommand? DimensionCommand
    {
        get => (ICommand?)GetValue(DimensionCommandProperty);
        set => SetValue(DimensionCommandProperty, value);
    }

    private bool HasDimensions => Dimensions is { Count: > 0 } && !_gizmoDragging;

    private void InitializeDimensions()
    {
        _gizmoViewport.Children.Add(_dimensionVisual);
        Children.Add(_dimensionLabels);
        _dimensionLabels.Children.Add(_dimensionEditor);
        _dimensionEditor.PreviewKeyDown += OnDimensionEditorKeyDown;
        _dimensionEditor.LostKeyboardFocus += (_, _) => EndDimensionEdit(commit: false);
    }

    private void OnDimensionsChanged()
    {
        if (_editingDimension is not null && Dimensions?.Any(d => d.Id == _editingDimension) != true) EndDimensionEdit(commit: false);
        UpdateGizmoTransform();
    }

    /// <summary>Lines (with small end marks) and value labels; rebuilt when the camera moves so that they keep their size on screen.</summary>
    private void RebuildDimensions()
    {
        IReadOnlyList<ViewportDimension> dimensions = HasDimensions ? Dimensions! : [];
        var mesh = new MeshGeometry3D();
        var radius = (float)(_orbit.Distance * 0.0014);
        var offset = (float)(_orbit.Distance * 0.02);
        var width = Math.Max(ActualWidth, 1);
        var height = Math.Max(ActualHeight, 1);

        var keep = new List<UIElement> { _dimensionEditor };
        foreach (var dimension in dimensions)
        {
            var from = dimension.From + dimension.Outward * offset;
            var to = dimension.To + dimension.Outward * offset;
            if (Vector3.DistanceSquared(from, to) < 1e-8f) continue;

            GizmoGeometry.AddTube(mesh, from, to, radius, 6);
            foreach (var end in new[] { from, to }) // a short mark from the dimension line to the object at both ends
                GizmoGeometry.AddTube(mesh, end, end - dimension.Outward * offset, radius * 0.8f, 6);

            var middle = (from + to) * 0.5f + dimension.Outward * (offset * 0.55f);
            if (_orbit.Project(middle, width, height) is not { } p) continue;
            var label = LabelFor(dimension);
            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(label, p.X - label.DesiredSize.Width / 2);
            Canvas.SetTop(label, p.Y - label.DesiredSize.Height / 2);
            keep.Add(label);

            if (_editingDimension == dimension.Id)
            {
                Canvas.SetLeft(_dimensionEditor, p.X - _dimensionEditor.Width / 2);
                Canvas.SetTop(_dimensionEditor, p.Y - 14);
                label.Visibility = Visibility.Hidden; // the input box takes its place
            }
        }

        for (var i = _dimensionLabels.Children.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(_dimensionLabels.Children[i])) _dimensionLabels.Children.RemoveAt(i);
        }

        foreach (var element in keep.Where(e => !_dimensionLabels.Children.Contains(e))) _dimensionLabels.Children.Add(element);

        if (mesh.Positions.Count == 0)
        {
            _dimensionVisual.Content = null;
            return;
        }

        mesh.Freeze();
        var material = new MaterialGroup();
        material.Children.Add(new DiffuseMaterial(Brushes.Black));
        material.Children.Add(new EmissiveMaterial(new SolidColorBrush(LightTheme ? Color.FromRgb(0x3C, 0x44, 0x52) : DimensionColor)));
        material.Freeze();
        _dimensionVisual.Content = new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    private Border LabelFor(ViewportDimension dimension)
    {
        if (_labelCache.TryGetValue(dimension.Id, out var cached))
        {
            ((TextBlock)cached.Child).Text = dimension.Text;
            cached.Visibility = Visibility.Visible;
            cached.Tag = dimension;
            return cached;
        }

        var label = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x23, 0x26, 0x2B)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x7B, 0x84, 0x93)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            Cursor = Cursors.IBeam,
            Tag = dimension,
            Child = new TextBlock { Text = dimension.Text, Foreground = new SolidColorBrush(DimensionColor), FontSize = 12 },
        };
        label.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true; // no selecting or orbiting behind the label
            if (label.Tag is ViewportDimension d) BeginDimensionEdit(d);
        };
        _labelCache[dimension.Id] = label;
        return label;
    }

    private void BeginDimensionEdit(ViewportDimension dimension)
    {
        _editingDimension = dimension.Id;
        _dimensionEditor.Text = dimension.Text.Replace(" mm", "");
        _dimensionEditor.Visibility = Visibility.Visible;
        RebuildDimensions();
        _dimensionEditor.Focus();
        Keyboard.Focus(_dimensionEditor);
        _dimensionEditor.SelectAll();
    }

    private void EndDimensionEdit(bool commit)
    {
        if (_editingDimension is not { } id) return;
        var text = _dimensionEditor.Text;
        _editingDimension = null;
        _dimensionEditor.Visibility = Visibility.Collapsed;
        if (commit && !string.IsNullOrWhiteSpace(text) && DimensionCommand is { } command)
        {
            var edit = new DimensionEdit(id, text);
            if (command.CanExecute(edit)) command.Execute(edit);
        }

        Focus();
        RebuildDimensions();
    }

    private void OnDimensionEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            EndDimensionEdit(commit: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            EndDimensionEdit(commit: false);
            e.Handled = true;
        }
    }
}
