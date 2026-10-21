using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;

namespace Formwerk.App.ViewModels;

/// <summary>Properties of the single selected node. Every edit is a document command (undoable).</summary>
public sealed partial class PropertiesViewModel : ObservableObject
{
    private readonly FormwerkDocument _document;
    private readonly UndoHistory _history;
    private readonly Node _node;
    private readonly Func<Guid, string> _sizeProvider;
    private readonly FieldViewModel _name;
    private readonly Action<Vector3>? _positionMoved;
    private readonly Func<PatternNode, PatternReference?>? _patternReference;
    private bool _edgeToHoleEdge;

    /// <param name="positionMoved">Told by how much (in world space) a typed position moved the object, for "Ctrl+D repeats the last offset".</param>
    public PropertiesViewModel(FormwerkDocument document, UndoHistory history, Node node, Func<Guid, string> sizeProvider,
        Action<Vector3>? positionMoved = null, Func<PatternNode, PatternReference?>? patternReference = null)
    {
        _positionMoved = positionMoved;
        _patternReference = patternReference;
        _document = document;
        _history = history;
        _node = node;
        _sizeProvider = sizeProvider;

        _name = new FieldViewModel(Texts.Get("Props.Name"), "", ApplyName);
        string[] axes = ["X", "Y", "Z"];
        Position = [.. axes.Select((axis, i) => new FieldViewModel(axis, "mm", text => ApplyPosition(i, text)))];
        Rotation = [.. axes.Select((axis, i) => new FieldViewModel(axis, "°", text => ApplyRotation(i, text)))];

        if (node is PrimitiveNode primitive && ShapeLibrary.Find(primitive.GeneratorId) is { } generator)
        {
            Parameters = [.. generator.Parameters.Select(definition =>
                new FieldViewModel(definition.Label, definition.Unit ?? (definition.IsInteger ? "" : "mm"), text => ApplyParameter(primitive, definition, text)))];
        }
        else if (node is PatternNode pattern)
        {
            Parameters = [.. PatternParameters.For(pattern.Kind).Select(definition =>
                new FieldViewModel(definition.Label, definition.Unit ?? (definition.IsInteger ? "" : "mm"), text => ApplyParameter(pattern, definition, text)))];
            SkipField = new FieldViewModel(Texts.Get("Pattern.Skip"), "", text => ApplySkipped(pattern, text));
            if (pattern.Kind == PatternKind.Circular)
            {
                RadiusField = new FieldViewModel(Texts.Get("Pattern.Radius"), "mm", text => ApplyRadius(pattern, text));
                EdgeDistanceField = new FieldViewModel(Texts.Get("Pattern.EdgeDistance"), "mm", text => ApplyEdgeDistance(pattern, text));
            }
        }

        Refresh();
    }

    // ---- patterns (F-PAT-01..03) ---------------------------------------------------------------

    /// <summary>The node is a pattern: the choices (direction, total length) and the skipped instances show.</summary>
    public bool IsPattern => _node is PatternNode;

    public bool IsLinearPattern => _node is PatternNode { Kind: PatternKind.Linear };

    public bool IsCircularPattern => _node is PatternNode { Kind: PatternKind.Circular };

    /// <summary>"Direction" for a row, "Axis" for a circle.</summary>
    public string PatternAxisLabel => Texts.Get(IsCircularPattern ? "Pattern.Axis" : "Pattern.Direction");

    // ---- the radius of a circular pattern (F-PAT-01) -------------------------------------------------

    /// <summary>
    /// The distance of the source from the axis of a circular pattern. It is not a parameter of the pattern: the source stands where
    /// it stands, and typing a radius moves the source (and everything else in the pattern) away from or towards the axis.
    /// </summary>
    public FieldViewModel? RadiusField { get; }

    /// <summary>The distance from the outer edge of the body around the center, to the middle of the source (or to its edge).</summary>
    public FieldViewModel? EdgeDistanceField { get; }

    /// <summary>There is a body around the center to measure from; without one the distance from the edge cannot be used.</summary>
    public bool HasEdgeReference => _node is PatternNode { Kind: PatternKind.Circular } pattern && _patternReference?.Invoke(pattern) is not null;

    /// <summary>The distance from the edge is measured to the edge of the source (a hole), not to its middle.</summary>
    public bool EdgeToHoleEdge
    {
        get => _edgeToHoleEdge;
        set
        {
            if (_edgeToHoleEdge == value) return;
            _edgeToHoleEdge = value;
            Refresh();
        }
    }

    /// <summary>The distance of the first child from the axis, in the plane of the circle.</summary>
    private (double Radius, Vector3 Direction) CurrentRadius(PatternNode pattern)
    {
        var position = pattern.Children.Count > 0 ? pattern.Children[0].Transform.Position : Vector3.Zero;
        var axis = IntParameter("axis");
        var across = axis switch { 0 => new Vector3(0, position.Y, position.Z), 1 => new Vector3(position.X, 0, position.Z), _ => new Vector3(position.X, position.Y, 0) };
        var radius = across.Length();
        var direction = radius > 1e-6 ? across / (float)radius : axis switch { 0 => Vector3.UnitY, 1 => Vector3.UnitZ, _ => Vector3.UnitX };
        return (radius, direction);
    }

    private string? ApplyRadius(PatternNode pattern, string text)
    {
        var current = CurrentRadius(pattern).Radius;
        var result = NumericInput.Resolve(text, current, _document.ResolveVariables(out _));
        if (!result.IsSuccess) return result.Error;
        if (result.Value < 0) return Texts.Get("Err.RadiusNegative");
        MoveToRadius(pattern, result.Value);
        return null;
    }

    private string? ApplyEdgeDistance(PatternNode pattern, string text)
    {
        if (_patternReference?.Invoke(pattern) is not { } reference) return Texts.Get("Err.NoEdge");
        var shown = reference.OuterRadius - CurrentRadius(pattern).Radius - (_edgeToHoleEdge ? reference.SourceHalfWidth : 0);
        var result = NumericInput.Resolve(text, shown, _document.ResolveVariables(out _));
        if (!result.IsSuccess) return result.Error;

        var radius = reference.OuterRadius - result.Value - (_edgeToHoleEdge ? reference.SourceHalfWidth : 0);
        if (radius < 0) return Texts.Get("Err.EdgeTooFar");
        MoveToRadius(pattern, radius);
        return null;
    }

    /// <summary>Moves all children of the pattern radially so that the first one has the radius; one undo step.</summary>
    private void MoveToRadius(PatternNode pattern, double radius)
    {
        var (current, direction) = CurrentRadius(pattern);
        var shift = direction * (float)(radius - current);
        if (shift.LengthSquared() < 1e-12f) return;
        var changes = pattern.Children.Select(c => (c, c.Transform, c.Transform with { Position = c.Transform.Position + shift }));
        _history.Execute(new SetTransformsCommand(_document, changes, Texts.Format("Name.SetRadius", pattern.Name)));
    }

    /// <summary>The instances to leave out, as typed (<c>2, 5</c> or <c>3-5</c>); null for other nodes.</summary>
    public FieldViewModel? SkipField { get; }

    public IReadOnlyList<AxisChoice> PatternAxes { get; } =
        [new(0, Texts.Get("Pattern.Axis.X")), new(1, Texts.Get("Pattern.Axis.Y")), new(2, Texts.Get("Pattern.Axis.Z"))];

    /// <summary>The direction of a linear pattern (0 X, 1 Y, 2 Z) or the axis of a circular one.</summary>
    public int PatternAxis
    {
        get => IntParameter("axis");
        set => SetChoice("axis", value);
    }

    /// <summary>The second direction of a grid.</summary>
    public int PatternAxisY
    {
        get => IntParameter("axisY");
        set => SetChoice("axisY", value);
    }

    /// <summary>The spacing is the whole length from the first to the last instance, not the distance between two.</summary>
    public bool PatternTotalLength
    {
        get => IntParameter("totalLength") == 1;
        set => SetChoice("totalLength", value ? 1 : 0);
    }

    public bool PatternTotalLengthY
    {
        get => IntParameter("totalLengthY") == 1;
        set => SetChoice("totalLengthY", value ? 1 : 0);
    }

    /// <summary>How many instances the pattern has (after skipping), or nothing when its values cannot be used.</summary>
    public string InstanceText
    {
        get
        {
            if (_node is not PatternNode pattern) return "";
            var values = pattern.EvaluateParameters(_document.ResolveVariables(out _), out var errors);
            if (errors.Count > 0) return "";
            var instances = PatternMath.Instances(pattern, values, out var problem);
            return problem is null ? Texts.Format("Pattern.Instances", instances.Count) : "";
        }
    }

    /// <summary>A whole-number parameter of a pattern or a mirror (a choice such as the axis), with its default when it is missing or broken.</summary>
    private int IntParameter(string name)
    {
        var definition = ChoiceDefinitions()?.FirstOrDefault(d => d.Name == name);
        if (_node is not ParametricNode node || definition is null) return 0;
        if (!node.Parameters.TryGetValue(name, out var text)) return (int)definition.Default;
        return ExpressionParser.Evaluate(text, _document.ResolveVariables(out _)) is { IsSuccess: true } result ? (int)Math.Round(result.Value) : (int)definition.Default;
    }

    private IReadOnlyList<ParameterDefinition>? ChoiceDefinitions() => _node switch
    {
        PrimitiveNode primitive => ShapeLibrary.Find(primitive.GeneratorId)?.Parameters,
        PatternNode pattern => PatternParameters.For(pattern.Kind),
        MirrorNode => MirrorParameters.Definitions,
        _ => null,
    };

    private void SetChoice(string name, int value)
    {
        if (_node is ParametricNode node && ChoiceDefinitions() is not null && IntParameter(name) != value)
            _history.Execute(new SetParameterCommand(_document, node, name, value.ToString(CultureInfo.InvariantCulture)));
    }

    // ---- chamfer and fillet (F-EDG-01) ---------------------------------------------------------

    /// <summary>The shape has an edge treatment (a box or a cylinder): the choices are offered below the size.</summary>
    public bool HasEdgeChoices => _node is PrimitiveNode primitive && ShapeLibrary.Find(primitive.GeneratorId)?.Parameters.Any(p => p.Name == "edgeSize") == true;

    /// <summary>The shape has the choice of which edges are treated (a cutter sits on one edge and has none).</summary>
    public bool HasEdgeWhere => _node is PrimitiveNode primitive && ShapeLibrary.Find(primitive.GeneratorId)?.Parameters.Any(p => p.Name == "edgeWhere") == true;

    public IReadOnlyList<EdgeChoice> EdgeTypes { get; } = [new(0, Texts.Get("Edge.Chamfer")), new(1, Texts.Get("Edge.Fillet"))];

    /// <summary>Which edges: a box has edges, a cylinder has rims.</summary>
    public IReadOnlyList<EdgeChoice> EdgeWheres => _node is PrimitiveNode { GeneratorId: "cylinder" }
        ? [new(0, Texts.Get("Edge.Where.Rims")), new(1, Texts.Get("Edge.Where.TopRim")), new(2, Texts.Get("Edge.Where.BottomRim"))]
        : [new(0, Texts.Get("Edge.Where.All")), new(1, Texts.Get("Edge.Where.Top")), new(2, Texts.Get("Edge.Where.Bottom"))];

    /// <summary>0 chamfer, 1 fillet.</summary>
    public int EdgeType
    {
        get => IntParameter("edgeType");
        set => SetChoice("edgeType", value);
    }

    /// <summary>0 all, 1 top, 2 bottom.</summary>
    public int EdgeWhere
    {
        get => IntParameter("edgeWhere");
        set => SetChoice("edgeWhere", value);
    }

    // ---- mirror (F-PAT-04) ---------------------------------------------------------------------

    /// <summary>The node is a mirror: the plane and "show the original" are offered.</summary>
    public bool IsMirror => _node is MirrorNode;

    /// <summary>The normal of the mirror plane (0 X, 1 Y, 2 Z), in the space of the mirror node.</summary>
    public int MirrorAxis
    {
        get => IntParameter("axis");
        set => SetChoice("axis", value);
    }

    /// <summary>Off: only the mirror image is shown (an independent mirrored copy).</summary>
    public bool MirrorShowOriginal
    {
        get => IntParameter("copyOnly") == 0;
        set => SetChoice("copyOnly", value ? 0 : 1);
    }
    private string? ApplySkipped(PatternNode pattern, string text)
    {
        var numbers = PatternNode.ParseSkipped(text, out var error);
        if (error is not null) return error;

        var command = new SetSkippedCommand(_document, pattern, numbers);
        if (command.HasEffect) _history.Execute(command);
        SkipField!.SetFromModel(PatternNode.FormatSkipped(pattern.Skipped)); // shown in the short form
        return null;
    }

    public Guid NodeId => _node.Id;

    public FieldViewModel NameField => _name;

    public IReadOnlyList<FieldViewModel> Position { get; }

    public IReadOnlyList<FieldViewModel> Rotation { get; }

    public IReadOnlyList<FieldViewModel> Parameters { get; } = [];

    public bool HasParameters => Parameters.Count > 0 || IsMirror; // a mirror has no number fields but its own controls

    /// <summary>The node is a hole (F-HOL-02): the type is a choice, only the fields of the type show, and a picture shows the section.</summary>
    public bool IsHoleShape => _node is PrimitiveNode { GeneratorId: "hole" };

    public IReadOnlyList<HoleTypeItem> HoleTypes { get; } = HoleTypeItem.All();

    public HoleType HoleType
    {
        get => _node is PrimitiveNode primitive && primitive.Parameters.TryGetValue("type", out var text)
            && ExpressionParser.Evaluate(text, _document.ResolveVariables(out _)) is { IsSuccess: true } result
            ? (HoleType)(int)Math.Clamp(Math.Round(result.Value), 0, 4)
            : HoleType.Cylindrical;
        set
        {
            if (_node is PrimitiveNode primitive && IsHoleShape && HoleType != value)
                _history.Execute(new SetParameterCommand(_document, primitive, "type", ((int)value).ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>"Through all": the hole is as deep as the body needs (the evaluator works it out). Switching it off gives the hole its stored depth back.</summary>
    public bool HoleThrough
    {
        get => _node is PrimitiveNode primitive && primitive.Parameters.TryGetValue("through", out var text)
            && ExpressionParser.Evaluate(text, _document.ResolveVariables(out _)) is { IsSuccess: true } result && result.Value >= 0.5;
        set
        {
            if (_node is PrimitiveNode primitive && IsHoleShape && HoleThrough != value)
                _history.Execute(new SetParameterCommand(_document, primitive, "through", value ? "1" : "0"));
        }
    }

    /// <summary>The cross-section picture of the hole, null for other shapes.</summary>
    public HoleSection? Section => IsHoleShape && _node is PrimitiveNode primitive ? HoleSection.From(HoleProfile(primitive)) : null;

    private IReadOnlyList<(float R, float Z)> HoleProfile(PrimitiveNode primitive)
    {
        var values = primitive.EvaluateParameters(_document.ResolveVariables(out _), out _);
        try
        {
            return ShapeLibrary.Hole.Profile(values);
        }
        catch (ArgumentException) // a value that is out of range: show the default hole until it is corrected
        {
            return ShapeLibrary.Hole.Profile(new Dictionary<string, double> { ["type"] = (int)HoleType });
        }
    }

    /// <summary>Which fields of a hole the type needs (the type itself is the choice, so its number never shows).</summary>
    private static bool IsFieldOfType(string parameter, HoleType type, bool through) => parameter switch
    {
        "type" or "through" => false, // both are a choice or a checkbox
        "depth" => !through,
        "endDiameter" => type == HoleType.Conical,
        "sinkDiameter" => type is HoleType.Countersink or HoleType.Counterbore or HoleType.NutTrap,
        "sinkAngle" => type == HoleType.Countersink,
        "sinkDepth" => type is HoleType.Counterbore or HoleType.NutTrap,
        _ => true,
    };

    public bool IsBoolean => _node is BooleanNode;

    public IReadOnlyList<BooleanOperation> Operations { get; } = Enum.GetValues<BooleanOperation>();

    public BooleanOperation Operation
    {
        get => (_node as BooleanNode)?.Operation ?? BooleanOperation.Union;
        set
        {
            if (_node is BooleanNode boolean && boolean.Operation != value)
                _history.Execute(new SetBooleanOperationCommand(_document, boolean, value));
        }
    }

    public bool IsHole
    {
        get => _node.Role == NodeRole.Hole;
        set
        {
            if (IsHole != value) _history.Execute(new SetRoleCommand(_document, _node, value ? NodeRole.Hole : NodeRole.Solid));
        }
    }

    public string SizeText => _sizeProvider(_node.Id);

    /// <summary>Re-reads all values from the node (after any document change).</summary>
    public void Refresh()
    {
        _name.SetFromModel(_node.Name);
        var position = _node.Transform.Position;
        float[] p = [position.X, position.Y, position.Z];
        for (var i = 0; i < 3; i++) Position[i].SetFromModel(Format(p[i]));

        var euler = EulerAngles.FromQuaternion(_node.Transform.Rotation);
        double[] r = [euler.X, euler.Y, euler.Z];
        for (var i = 0; i < 3; i++) Rotation[i].SetFromModel(Format(r[i]));

        if (_node is PrimitiveNode primitive)
        {
            var generator = ShapeLibrary.Find(primitive.GeneratorId);
            for (var i = 0; i < Parameters.Count && generator is not null; i++)
            {
                var definition = generator.Parameters[i];
                var stored = primitive.Parameters.TryGetValue(definition.Name, out var text) ? text : Format(definition.Default);
                Parameters[i].SetFromModel(stored);
                Parameters[i].ShowLinks(ExpressionParser.UsedNames(stored)); // marked when it uses project variables (F-PAR-03)
            }

            if (!IsHoleShape)
            {
                for (var i = 0; i < Parameters.Count && generator is not null; i++)
                    Parameters[i].IsVisible = generator.Parameters[i].Name is not ("edgeType" or "edgeWhere" or "inside"); // the choices have their own controls (or are facts of the edge)
            }

            if (IsHoleShape)
            {
                var type = HoleType;
                var through = HoleThrough;
                for (var i = 0; i < Parameters.Count && generator is not null; i++)
                {
                    Parameters[i].IsVisible = IsFieldOfType(generator.Parameters[i].Name, type, through);
                    Parameters[i].Label = HoleGenerator.LabelFor(generator.Parameters[i].Name, type);
                }
            }
        }

        if (_node is PatternNode pattern)
        {
            var definitions = PatternParameters.For(pattern.Kind);
            for (var i = 0; i < Parameters.Count; i++)
            {
                var definition = definitions[i];
                var stored = pattern.Parameters.TryGetValue(definition.Name, out var text) ? text : Format(definition.Default);
                Parameters[i].SetFromModel(stored);
                Parameters[i].ShowLinks(ExpressionParser.UsedNames(stored));
                Parameters[i].IsVisible = !IsPatternChoice(definition.Name); // the choices have their own controls
            }

            SkipField?.SetFromModel(PatternNode.FormatSkipped(pattern.Skipped));

            if (RadiusField is not null && EdgeDistanceField is not null)
            {
                var radius = CurrentRadius(pattern).Radius;
                RadiusField.SetFromModel(Format(radius));
                var reference = _patternReference?.Invoke(pattern);
                EdgeDistanceField.SetFromModel(reference is null ? "" : Format(reference.OuterRadius - radius - (_edgeToHoleEdge ? reference.SourceHalfWidth : 0)));
            }
        }

        OnPropertyChanged(string.Empty);
    }

    private static bool IsPatternChoice(string parameter) => parameter is "axis" or "axisY" or "totalLength" or "totalLengthY";

    private static string Format(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

    private string? ApplyName(string text)
    {
        var name = text.Trim();
        if (name.Length == 0) return Texts.Get("Err.NameEmpty");
        if (name != _node.Name) _history.Execute(new RenameNodeCommand(_document, _node, name));
        return null;
    }

    private string? ApplyPosition(int axis, string text)
    {
        var position = _node.Transform.Position;
        var current = axis switch { 0 => position.X, 1 => position.Y, _ => position.Z };
        var result = NumericInput.Resolve(text, current, _document.ResolveVariables(out _));
        if (!result.IsSuccess) return result.Error;

        var value = (float)result.Value;
        var moved = axis switch
        {
            0 => new Vector3(value, position.Y, position.Z),
            1 => new Vector3(position.X, value, position.Z),
            _ => new Vector3(position.X, position.Y, value),
        };
        if (moved != position)
        {
            var before = TransformOps.WorldPosition(_node);
            _history.Execute(new SetTransformCommand(_document, _node, _node.Transform with { Position = moved }, $"Move {_node.Name}"));
            _positionMoved?.Invoke(TransformOps.WorldPosition(_node) - before);
        }

        return null;
    }

    private string? ApplyRotation(int axis, string text)
    {
        var euler = EulerAngles.FromQuaternion(_node.Transform.Rotation);
        var current = axis switch { 0 => euler.X, 1 => euler.Y, _ => euler.Z };
        var result = NumericInput.Resolve(text, current, _document.ResolveVariables(out _));
        if (!result.IsSuccess) return result.Error;

        var rotated = axis switch
        {
            0 => euler with { X = result.Value },
            1 => euler with { Y = result.Value },
            _ => euler with { Z = result.Value },
        };
        if (rotated != euler)
            _history.Execute(new SetTransformCommand(_document, _node, _node.Transform with { Rotation = rotated.ToQuaternion() }, $"Rotate {_node.Name}"));
        return null;
    }

    /// <summary>
    /// Plain numbers and expressions ("Wandstaerke*2") are stored as typed so that links to variables survive (F-PAR-03);
    /// relative ("+10") and percent ("150%") input is resolved to a number first.
    /// </summary>
    private string? ApplyParameter(ParametricNode node, ParameterDefinition definition, string text)
    {
        var trimmed = text.Trim();
        var variables = _document.ResolveVariables(out _);
        var stored = trimmed;

        if (trimmed.StartsWith('+') || trimmed.EndsWith('%'))
        {
            var currentText = node.Parameters.TryGetValue(definition.Name, out var existing) ? existing : Format(definition.Default);
            var current = ExpressionParser.Evaluate(currentText, variables);
            var resolved = NumericInput.Resolve(trimmed, current.IsSuccess ? current.Value : definition.Default, variables);
            if (!resolved.IsSuccess) return resolved.Error;
            stored = resolved.Value.ToString("R", CultureInfo.InvariantCulture);
        }

        var value = ExpressionParser.Evaluate(stored, variables);
        if (!value.IsSuccess) return value.Error;
        var number = definition.IsInteger ? Math.Round(value.Value) : value.Value;
        if (number < definition.Min || number > definition.Max)
            return Texts.Format("Err.Range", definition.Label, Format(definition.Min), Format(definition.Max));

        if (!node.Parameters.TryGetValue(definition.Name, out var old) || old != stored)
            _history.Execute(new SetParameterCommand(_document, node, definition.Name, stored));
        return null;
    }
}
