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

/// <summary>Parametric primitive created by a generator (e.g. <c>cylinder</c>). Parameter values are expressions.</summary>
public sealed class PrimitiveNode : Node
{
    public PrimitiveNode(string generatorId, string name) : base(name)
    {
        GeneratorId = generatorId;
    }

    public string GeneratorId { get; }

    /// <summary>Parameter name to expression text, e.g. <c>"Wandstaerke*2"</c>.</summary>
    public Dictionary<string, string> Parameters { get; } = new(StringComparer.OrdinalIgnoreCase);

    protected override Node CreateCopy(Guid id)
    {
        var copy = new PrimitiveNode(GeneratorId, Name) { Id = id };
        foreach (var (key, value) in Parameters) copy.Parameters[key] = value;
        return copy;
    }

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

    protected override void AppendHashInput(StringBuilder sb, IReadOnlyDictionary<string, double>? variables)
    {
        base.AppendHashInput(sb, variables);
        sb.Append('#').Append(GeneratorId);
        foreach (var (key, expression) in Parameters.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            var result = ExpressionParser.Evaluate(expression, variables);
            sb.Append('|').Append(key.ToLowerInvariant()).Append('=').Append(result.IsSuccess ? F(result.Value) : "!" + expression);
        }
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
