using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Resizing by dragging the box handles (F-SCL-01, 02, 03, 05, 06).</summary>
public sealed partial class MainViewModel
{
    /// <summary>Smallest size an object can be dragged to, in mm.</summary>
    private const double MinimumSize = 0.1;

    private sealed class ScaleSession
    {
        public required Node Node { get; init; }

        public required Transform StartTransform { get; init; }

        public required ScaleGizmoBox Box { get; init; }

        public required NodeFrame Frame { get; init; }

        /// <summary>Set for shapes that are resized through their parameters (F-SCL-05).</summary>
        public PrimitiveNode? Primitive { get; init; }

        public IShapeGenerator? Generator { get; init; }

        /// <summary>The expressions of the shape's parameters at the start, to restore them for the undo entry.</summary>
        public Dictionary<string, string> StartExpressions { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, double> StartValues { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Bounds OldLocalBounds { get; set; }
    }

    private ScaleSession? _scale;

    /// <summary>Oriented box of the single selected object while the Scale tool is active; null otherwise.</summary>
    [ObservableProperty]
    public partial ScaleGizmoBox? GizmoScaleBox { get; set; }

    /// <summary>Proportional mode (F-SCL-02): every handle scales all three axes together.</summary>
    [ObservableProperty]
    public partial bool ScaleLinked { get; set; }

    private ScaleGizmoBox? ComputeScaleBox()
    {
        if (_lastResult is null || _selected.Count == 0) return null;
        var nodes = SelectedTopLevel();
        if (nodes.Count != 1) return null;

        var node = nodes[0];
        var ids = new HashSet<Guid>(node.Descendants().Select(d => d.Id)) { node.Id };
        var parts = _lastResult.Parts.Where(p => ids.Contains(p.Source.Id) && !p.Mesh.IsEmpty).ToList();
        var shown = parts.Where(p => p.Kind != PartKind.Ghost).ToList();
        if (shown.Count == 0) shown = parts; // a hole is only shown as a ghost
        if (shown.Count == 0) return null;

        var frame = TransformOps.FrameOf(node);
        var inverse = Quaternion.Inverse(frame.Rotation);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var part in shown)
        {
            foreach (var vertex in part.Mesh.Vertices)
            {
                var local = Vector3.Transform(vertex - frame.Origin, inverse);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }

        return new ScaleGizmoBox(frame.Origin, frame.Rotation, min, max);
    }

    private bool BeginScale(GizmoHandleKind handle)
    {
        var nodes = SelectedTopLevel();
        if (nodes.Count != 1 || nodes[0].Locked || GizmoScaleBox is not { } box) return false;

        var node = nodes[0];
        ScaleSession session;
        if (node is PrimitiveNode primitive && ShapeLibrary.Find(primitive.GeneratorId) is { } generator)
        {
            var values = primitive.EvaluateParameters(_document.ResolveVariables(out _), out var errors);
            if (errors.Count > 0) return false; // a broken expression cannot be scaled

            session = new ScaleSession
            {
                Node = node, StartTransform = node.Transform, Box = box, Frame = TransformOps.FrameOf(node),
                Primitive = primitive, Generator = generator,
            };
            foreach (var definition in generator.Parameters)
                session.StartValues[definition.Name] = values.TryGetValue(definition.Name, out var v) ? v : definition.Default;
            foreach (var (name, expression) in primitive.Parameters) session.StartExpressions[name] = expression;
            session.OldLocalBounds = generator.Generate(session.StartValues).ComputeBounds();
        }
        else
        {
            session = new ScaleSession { Node = node, StartTransform = node.Transform, Box = box, Frame = TransformOps.FrameOf(node) };
        }

        _scale = session;
        _dragHandle = handle;
        _dragging = true;
        return true;
    }

    private string UpdateScale(GizmoDrag drag)
    {
        var s = _scale!;
        var handle = drag.Handle;
        var size = new[] { s.Box.Size.X, s.Box.Size.Y, s.Box.Size.Z };
        var corner = GizmoHandles.IsScaleCorner(handle);
        var axis = corner ? -1 : GizmoHandles.ScaleAxisIndex(handle);

        // Which axes the drag asks to scale: all of them for corners and in proportional mode, else the handle's axis.
        bool Requested(int i) => corner || ScaleLinked || i == axis;

        var factor = drag.ScaleFactor;

        // Snap the new size of the dragged extent to the grid (the largest extent for proportional scaling).
        if (SnapToGrid && !drag.SnapSuppressed && GridStep > 0)
        {
            var reference = Enumerable.Range(0, 3).Where(Requested).Max(i => size[i]);
            if (reference > 1e-6)
            {
                var snapped = Math.Round(reference * factor / GridStep) * GridStep;
                factor = Math.Max(snapped, GridStep) / reference;
            }
        }

        // Never smaller than the minimum size on any scaled axis.
        foreach (var i in Enumerable.Range(0, 3).Where(Requested))
        {
            if (size[i] > 1e-6) factor = Math.Max(factor, MinimumSize / size[i]);
        }

        // Fixed point per axis (in the box's coordinates): the opposite side of the dragged handle, or the center.
        var center = s.Box.CenterLocal;
        var signs = GizmoHandles.ScaleSigns(handle);
        float Fixed(int i)
        {
            var c = i switch { 0 => center.X, 1 => center.Y, _ => center.Z };
            if (drag.FromCenter || !Requested(i) || (!corner && i != axis)) return c;
            var sign = i switch { 0 => signs.X, 1 => signs.Y, _ => signs.Z };
            var min = i switch { 0 => s.Box.Min.X, 1 => s.Box.Min.Y, _ => s.Box.Min.Z };
            var max = i switch { 0 => s.Box.Max.X, 1 => s.Box.Max.Y, _ => s.Box.Max.Z };
            return sign > 0 ? min : max;
        }

        var fixedPoint = new Vector3(Fixed(0), Fixed(1), Fixed(2));
        var requested = new Vector3(Requested(0) ? (float)factor : 1, Requested(1) ? (float)factor : 1, Requested(2) ? (float)factor : 1);

        Vector3 newSize;
        if (s.Primitive is not null && s.Generator is not null) newSize = ScalePrimitive(s, requested, fixedPoint);
        else newSize = ScaleContainer(s, requested, fixedPoint);

        _document.NotifyChanged(); // live preview through the normal background evaluation
        return Texts.Format("Readout.Size", F1(newSize.X), F1(newSize.Y), F1(newSize.Z));
    }

    /// <summary>
    /// Resizes a shape through its parameters (F-SCL-05) and moves it so that the fixed point stays where it was.
    /// Returns the new size in mm.
    /// </summary>
    private Vector3 ScalePrimitive(ScaleSession s, Vector3 requested, Vector3 fixedPoint)
    {
        var generator = s.Generator!;
        var primitive = s.Primitive!;
        var values = new Dictionary<string, double>(s.StartValues, StringComparer.OrdinalIgnoreCase);

        foreach (var binding in generator.ScaleBindings)
        {
            var definition = generator.Parameters.First(p => string.Equals(p.Name, binding.Parameter, StringComparison.OrdinalIgnoreCase));
            var parameterFactor = 1f;
            if (binding.Axes.HasFlag(ScaleAxes.X) && requested.X != 1) parameterFactor = requested.X;
            else if (binding.Axes.HasFlag(ScaleAxes.Y) && requested.Y != 1) parameterFactor = requested.Y;
            else if (binding.Axes.HasFlag(ScaleAxes.Z) && requested.Z != 1) parameterFactor = requested.Z;
            else if (requested is { X: 1, Y: 1, Z: 1 }) parameterFactor = 1;

            var value = Math.Clamp(Math.Round(s.StartValues[binding.Parameter] * parameterFactor, 3), definition.Min, definition.Max);
            values[binding.Parameter] = value;
            primitive.Parameters[binding.Parameter] = parameterFactor == 1 && s.StartExpressions.TryGetValue(binding.Parameter, out var original)
                ? original
                : value.ToString("R", CultureInfo.InvariantCulture);
        }

        var newBounds = generator.Generate(values).ComputeBounds();
        var oldBounds = s.OldLocalBounds;
        var unit = s.Frame.Unit;

        var shift = new float[3];
        for (var i = 0; i < 3; i++)
        {
            var oldSize = Component(oldBounds.Size, i);
            var actual = oldSize > 1e-9f ? Component(newBounds.Size, i) / oldSize : 1f;
            var u = Component(unit, i);
            var reference = Component(fixedPoint, i);
            var targetMin = reference + (Component(oldBounds.Min, i) * u - reference) * actual;
            shift[i] = targetMin - Component(newBounds.Min, i) * u;
        }

        var worldShift = Vector3.Transform(new Vector3(shift[0], shift[1], shift[2]), s.Frame.Rotation);
        s.Node.Transform = TransformOps.MovedBy(s.Node, s.StartTransform, worldShift);
        return newBounds.Size * unit;
    }

    /// <summary>Resizes a group, boolean, merge or mesh through its scale and moves it so that the fixed point stays where it was.</summary>
    private static Vector3 ScaleContainer(ScaleSession s, Vector3 requested, Vector3 fixedPoint)
    {
        var start = s.StartTransform;
        var scaled = start with { Scale = start.Scale * requested };
        var shift = fixedPoint * (Vector3.One - requested);
        s.Node.Transform = TransformOps.MovedBy(s.Node, scaled, Vector3.Transform(shift, s.Frame.Rotation));
        return s.Box.Size * requested;
    }

    private void CommitScale()
    {
        var s = _scale!;
        _scale = null;
        _dragging = false;

        var finalTransform = s.Node.Transform;
        var commands = new List<IDocumentCommand>();

        if (s.Primitive is not null && s.Generator is not null)
        {
            var changed = new List<(string Name, string Expression)>();
            foreach (var binding in s.Generator.ScaleBindings)
            {
                var now = s.Primitive.Parameters.GetValueOrDefault(binding.Parameter);
                var before = s.StartExpressions.GetValueOrDefault(binding.Parameter);
                if (now is not null && now != before) changed.Add((binding.Parameter, now));
            }

            RestoreScaleStart(s);
            foreach (var (name, expression) in changed) commands.Add(new SetParameterCommand(_document, s.Primitive, name, expression));
        }
        else
        {
            s.Node.Transform = s.StartTransform;
        }

        if (finalTransform != s.StartTransform)
            commands.Add(new SetTransformsCommand(_document, [(s.Node, s.StartTransform, finalTransform)], "Move"));

        if (commands.Count == 0)
        {
            _document.NotifyChanged();
            return;
        }

        _history.Execute(new CompositeCommand($"Scale {s.Node.Name}", commands));
    }

    private void CancelScale()
    {
        var s = _scale!;
        _scale = null;
        _dragging = false;
        RestoreScaleStart(s);
        _document.NotifyChanged();
    }

    private static void RestoreScaleStart(ScaleSession s)
    {
        s.Node.Transform = s.StartTransform;
        if (s.Primitive is null || s.Generator is null) return;
        foreach (var binding in s.Generator.ScaleBindings)
        {
            if (s.StartExpressions.TryGetValue(binding.Parameter, out var expression)) s.Primitive.Parameters[binding.Parameter] = expression;
            else s.Primitive.Parameters.Remove(binding.Parameter);
        }
    }

    private static float Component(Vector3 v, int i) => i switch { 0 => v.X, 1 => v.Y, _ => v.Z };

    private static string F1(double value) => Math.Round(value, 1).ToString("0.0", CultureInfo.InvariantCulture);
}
