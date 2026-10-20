using System.Globalization;
using System.Text;
using Formwerk.Core.Expressions;

namespace Formwerk.Core.Model;

public enum NodeRole
{
    Solid,
    Hole,
}

public enum BooleanOperation
{
    Union,
    Difference,
    Intersection,
}

/// <summary>
/// Base of all document nodes (spec section 6). Structural changes go through
/// <c>Formwerk.Core.Commands</c> so that they can be undone.
/// </summary>
public abstract class Node
{
    private readonly List<Node> _children = [];

    protected Node(string name)
    {
        Name = name;
    }

    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; }

    public Transform Transform { get; set; } = Transform.Identity;

    public NodeRole Role { get; set; } = NodeRole.Solid;

    /// <summary>ARGB color.</summary>
    public uint Color { get; set; } = 0xFFB0B8C4;

    public bool Visible { get; set; } = true;

    public bool Locked { get; set; }

    public Node? Parent { get; private set; }

    public IReadOnlyList<Node> Children => _children;

    /// <summary>Nodes of this type may have children.</summary>
    public virtual bool CanHaveChildren => false;

    /// <summary>All nodes below this one, depth first.</summary>
    public IEnumerable<Node> Descendants()
    {
        foreach (var child in _children)
        {
            yield return child;
            foreach (var nested in child.Descendants()) yield return nested;
        }
    }

    public bool IsDescendantOf(Node other)
    {
        for (var node = Parent; node is not null; node = node.Parent)
        {
            if (node == other) return true;
        }

        return false;
    }

    /// <summary>
    /// Deep copy of this subtree. With <paramref name="newIds"/> false the copy keeps all ids (snapshot for background
    /// evaluation); with true every node gets a fresh id (duplicate).
    /// </summary>
    public Node Clone(bool newIds = false)
    {
        var copy = CreateCopy(newIds ? Guid.NewGuid() : Id);
        copy.Transform = Transform;
        copy.Role = Role;
        copy.Color = Color;
        copy.Visible = Visible;
        copy.Locked = Locked;
        foreach (var child in _children) copy.InsertChild(copy._children.Count, child.Clone(newIds));
        return copy;
    }

    /// <summary>Creates an empty node of the same type and with the same type-specific data.</summary>
    protected abstract Node CreateCopy(Guid id);

    internal void InsertChild(int index, Node child)
    {
        if (!CanHaveChildren) throw new InvalidOperationException($"{GetType().Name} cannot have children.");
        if (child.Parent is not null) throw new InvalidOperationException("The node already has a parent.");
        if (child == this || IsDescendantOf(child)) throw new InvalidOperationException("A node cannot contain itself.");
        _children.Insert(index, child);
        child.Parent = this;
    }

    internal int RemoveChild(Node child)
    {
        var index = _children.IndexOf(child);
        if (index < 0) throw new InvalidOperationException("The node is not a child of this node.");
        _children.RemoveAt(index);
        child.Parent = null;
        return index;
    }

    /// <summary>
    /// Hash over everything that influences the geometry of this subtree (Auswertung, section 6).
    /// Name, color, visibility and lock state are deliberately excluded.
    /// </summary>
    public string ComputeHash(IReadOnlyDictionary<string, double>? variables = null)
    {
        var sb = new StringBuilder();
        AppendHashInput(sb, variables);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    protected virtual void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        var t = Transform;
        sb.Append(GetType().Name).Append('|').Append(Role).Append('|')
            .Append(F(t.Position.X)).Append(',').Append(F(t.Position.Y)).Append(',').Append(F(t.Position.Z)).Append('|')
            .Append(F(t.Rotation.X)).Append(',').Append(F(t.Rotation.Y)).Append(',').Append(F(t.Rotation.Z)).Append(',').Append(F(t.Rotation.W)).Append('|')
            .Append(F(t.Scale.X)).Append(',').Append(F(t.Scale.Y)).Append(',').Append(F(t.Scale.Z));

        sb.Append('[');
        foreach (var child in _children)
        {
            child.AppendHashInput(sb, variables);
            sb.Append(';');
        }

        sb.Append(']');
    }

    protected static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}

/// <summary>
/// A node whose values are parameters, each an expression (<c>"20"</c>, <c>"Wandstaerke*2"</c>): a primitive made by a generator, or a
/// pattern. Edited through <c>SetParameterCommand</c>.
/// </summary>
public abstract class ParametricNode(string name) : Node(name)
{
    /// <summary>Parameter name to expression text, e.g. <c>"Wandstaerke*2"</c>.</summary>
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Evaluates all parameters. Failed expressions are reported in <paramref name="errors"/>.</summary>
    public Dictionary<string, double> EvaluateParameters(
        IReadOnlyDictionary<string, double>? variables, out Dictionary<string, string> errors)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, expression) in Parameters)
        {
            var result = ExpressionParser.Evaluate(expression, variables);
            if (result.IsSuccess) values[key] = result.Value;
            else errors[key] = result.Error!;
        }

        return values;
    }

    protected void CopyParametersTo(ParametricNode copy)
    {
        foreach (var (key, value) in Parameters) copy.Parameters[key] = value;
    }

    protected void AppendParameters(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        foreach (var (key, expression) in Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var result = ExpressionParser.Evaluate(expression, variables);
            sb.Append('|').Append(key.ToLowerInvariant()).Append('=').Append(result.IsSuccess ? F(result.Value) : "!" + expression);
        }
    }
}

/// <summary>Parametric primitive created by a generator (e.g. <c>cylinder</c>). Parameter values are expressions.</summary>
public sealed class PrimitiveNode : ParametricNode
{
    public PrimitiveNode(string generatorId, string name) : base(name)
    {
        GeneratorId = generatorId;
    }

    public string GeneratorId { get; }

    protected override Node CreateCopy(Guid id)
    {
        var copy = new PrimitiveNode(GeneratorId, Name) { Id = id };
        CopyParametersTo(copy);
        return copy;
    }

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append('#').Append(GeneratorId);
        AppendParameters(sb, variables);
    }
}

/// <summary>How a pattern repeats its source (F-PAT-01, F-PAT-02).</summary>
public enum PatternKind
{
    /// <summary>In a row, or in a grid with two directions.</summary>
    Linear,

    /// <summary>Turned about an axis through the origin of the pattern.</summary>
    Circular,
}

/// <summary>
/// Repeats its children (the source, usually one object) as instances (F-PAT-02, F-PAT-03). A pattern stays a feature: its parameters
/// (count, spacing, angle, ...) are expressions that can be changed at any time, and single instances can be skipped. The origin and
/// the orientation of the pattern node are the origin and orientation of the pattern space (the center of a circular pattern); the
/// first instance is the source where it is. A hole in a pattern cuts at every instance.
/// </summary>
public sealed class PatternNode(string name, PatternKind kind) : ParametricNode(name)
{
    /// <summary>The largest instance number that can be skipped (and the largest number of instances).</summary>
    public const int MaxInstances = 1000;

    public PatternKind Kind { get; } = kind;

    /// <summary>The instances that are left out, as 1-based numbers (row by row in a grid).</summary>
    public SortedSet<int> Skipped { get; } = [];

    public override bool CanHaveChildren => true;

    protected override Node CreateCopy(Guid id)
    {
        var copy = new PatternNode(Name, Kind) { Id = id };
        CopyParametersTo(copy);
        foreach (var number in Skipped) copy.Skipped.Add(number);
        return copy;
    }

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append("#pattern:").Append(Kind);
        AppendParameters(sb, variables);
        sb.Append("|skip=").AppendJoin(',', Skipped);
    }

    /// <summary>
    /// Reads a list of instance numbers like <c>2, 5, 7-9</c> (numbers and ranges, separated by commas or spaces). An empty text is an
    /// empty list. <paramref name="error"/> says what is wrong; the result is then empty.
    /// </summary>
    public static SortedSet<int> ParseSkipped(string? text, out string? error)
    {
        error = null;
        var result = new SortedSet<int>();
        var separators = new[] { ',', ';', ' ' };
        foreach (var token in (text ?? "").Split(separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = token.Split('-');
            var first = 0;
            var ok = parts.Length <= 2 && TryNumber(parts[0], out first);
            var last = first;
            if (ok && parts.Length == 2) ok = TryNumber(parts[1], out last);
            if (!ok)
            {
                error = $"'{token}' is not an instance number or a range like 3-5.";
                return [];
            }

            if (last < first)
            {
                error = $"The range '{token}' runs backwards.";
                return [];
            }

            for (var number = first; number <= last; number++) result.Add(number);
        }

        return result;
    }

    private static bool TryNumber(string part, out int number) =>
        int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out number) && number is >= 1 and <= MaxInstances;

    /// <summary>The list as short text: <c>2-4, 7</c>. The reverse of <see cref="ParseSkipped"/>.</summary>
    public static string FormatSkipped(IEnumerable<int> numbers)
    {
        var sorted = numbers.Distinct().Order().ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j > i ? $"{sorted[i]}-{sorted[j]}" : sorted[i].ToString(CultureInfo.InvariantCulture));
            i = j + 1;
        }

        return string.Join(", ", parts);
    }
}

/// <summary>
/// Mirrors its children at a plane (F-PAT-04). The plane goes through the origin of the mirror node and its normal is the local axis
/// X, Y or Z (parameter <c>axis</c>: 0, 1, 2), so moving and turning the node moves and turns the plane, also onto a face. With
/// <c>copyOnly</c> 0 the mirror is linked: the source stays and its mirror image is added, and changing the source changes the image.
/// With <c>copyOnly</c> 1 only the mirror image is shown; the node then holds a private copy of the source, an independent mirrored
/// copy of the original.
/// </summary>
public sealed class MirrorNode(string name) : ParametricNode(name)
{
    public override bool CanHaveChildren => true;

    protected override Node CreateCopy(Guid id)
    {
        var copy = new MirrorNode(Name) { Id = id };
        CopyParametersTo(copy);
        return copy;
    }

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append("#mirror");
        AppendParameters(sb, variables);
    }
}

/// <summary>Imported or flattened mesh, stored separately under <see cref="MeshId"/>.</summary>
public sealed class MeshNode(string name, Guid meshId) : Node(name)
{
    /// <summary>The mesh in the <c>MeshStore</c>. Changes only through <c>SetMeshCommand</c> (repair); the old mesh stays in the store.</summary>
    public Guid MeshId { get; set; } = meshId;

    protected override Node CreateCopy(Guid id) => new MeshNode(Name, MeshId) { Id = id };

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append("#mesh:").Append(MeshId);
    }
}

/// <summary>Moves children together; no geometry operation.</summary>
public sealed class GroupNode(string name) : Node(name)
{
    public override bool CanHaveChildren => true;

    protected override Node CreateCopy(Guid id) => new GroupNode(Name) { Id = id };
}

/// <summary>Non-destructive boolean: operands are the children, in order (first child is the minuend).</summary>
public sealed class BooleanNode(string name, BooleanOperation operation) : Node(name)
{
    public BooleanOperation Operation { get; set; } = operation;

    public override bool CanHaveChildren => true;

    protected override Node CreateCopy(Guid id) => new BooleanNode(Name, Operation) { Id = id };

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append("#op:").Append(Operation);
    }
}

/// <summary>Union of solid children minus all hole children (Merge, Ctrl+M).</summary>
public sealed class MergeNode(string name) : Node(name)
{
    public override bool CanHaveChildren => true;

    protected override Node CreateCopy(Guid id) => new MergeNode(Name) { Id = id };
}
