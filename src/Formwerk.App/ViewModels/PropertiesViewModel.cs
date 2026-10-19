using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
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

    /// <param name="positionMoved">Told by how much (in world space) a typed position moved the object, for "Ctrl+D repeats the last offset".</param>
    public PropertiesViewModel(FormwerkDocument document, UndoHistory history, Node node, Func<Guid, string> sizeProvider,
        Action<Vector3>? positionMoved = null)
    {
        _positionMoved = positionMoved;
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
                new FieldViewModel(definition.Label, definition.IsInteger ? "" : "mm", text => ApplyParameter(primitive, definition, text)))];
        }

        Refresh();
    }

    public Guid NodeId => _node.Id;

    public FieldViewModel NameField => _name;

    public IReadOnlyList<FieldViewModel> Position { get; }

    public IReadOnlyList<FieldViewModel> Rotation { get; }

    public IReadOnlyList<FieldViewModel> Parameters { get; } = [];

    public bool HasParameters => Parameters.Count > 0;

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
                Parameters[i].SetFromModel(primitive.Parameters.TryGetValue(definition.Name, out var text)
                    ? text
                    : Format(definition.Default));
            }
        }

        OnPropertyChanged(string.Empty);
    }

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
    private string? ApplyParameter(PrimitiveNode node, ParameterDefinition definition, string text)
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
