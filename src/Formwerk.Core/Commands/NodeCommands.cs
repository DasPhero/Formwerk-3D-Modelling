using Formwerk.Core.Model;

namespace Formwerk.Core.Commands;

public sealed class AddNodeCommand(FormwerkDocument document, Node parent, Node node, int? index = null) : IDocumentCommand
{
    public string Name => $"Add {node.Name}";

    public void Do()
    {
        parent.InsertChild(index ?? parent.Children.Count, node);
        document.NotifyChanged();
    }

    public void Undo()
    {
        parent.RemoveChild(node);
        document.NotifyChanged();
    }
}

public sealed class RemoveNodeCommand(FormwerkDocument document, Node node) : IDocumentCommand
{
    private Node? _parent;
    private int _index;

    public string Name => $"Delete {node.Name}";

    public void Do()
    {
        _parent = node.Parent ?? throw new InvalidOperationException("The root node cannot be deleted.");
        _index = _parent.RemoveChild(node);
        document.NotifyChanged();
    }

    public void Undo()
    {
        _parent!.InsertChild(_index, node);
        document.NotifyChanged();
    }
}

public sealed class SetTransformCommand(FormwerkDocument document, Node node, Transform transform, string? name = null) : IDocumentCommand
{
    private Transform _old;

    public string Name => name ?? $"Transform {node.Name}";

    public void Do()
    {
        _old = node.Transform;
        node.Transform = transform;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.Transform = _old;
        document.NotifyChanged();
    }
}

/// <summary>Changes the transform of several nodes as one undo step (drag of a multi-selection, drop to floor).</summary>
public sealed class SetTransformsCommand : IDocumentCommand
{
    private readonly FormwerkDocument _document;
    private readonly List<(Node Node, Transform Old, Transform New)> _changes;

    /// <param name="changes">Each node with its transform before and after.</param>
    public SetTransformsCommand(FormwerkDocument document, IEnumerable<(Node Node, Transform Old, Transform New)> changes, string name)
    {
        _document = document;
        _changes = [.. changes];
        Name = name;
    }

    public string Name { get; }

    /// <summary>False when nothing would change (the caller can skip the command).</summary>
    public bool HasEffect => _changes.Any(c => c.Old != c.New);

    public void Do()
    {
        foreach (var (node, _, @new) in _changes) node.Transform = @new;
        _document.NotifyChanged();
    }

    public void Undo()
    {
        foreach (var (node, old, _) in _changes) node.Transform = old;
        _document.NotifyChanged();
    }
}

/// <summary>Changes the project settings (build volume, nozzle, tolerance) as one undo step.</summary>
public sealed class SetSettingsCommand(FormwerkDocument document, DocumentSettings settings, string? name = null) : IDocumentCommand
{
    private readonly DocumentSettings _new = settings.Clone();
    private DocumentSettings _old = new();

    public string Name => name ?? "Change settings";

    /// <summary>False when the new settings equal the current ones (the caller can skip the command).</summary>
    public bool HasEffect => !_new.SameAs(document.Settings);

    public void Do()
    {
        _old = document.Settings.Clone();
        document.Settings.CopyFrom(_new);
        document.NotifyChanged();
    }

    public void Undo()
    {
        document.Settings.CopyFrom(_old);
        document.NotifyChanged();
    }
}

/// <summary>Points a mesh object at another mesh (a repaired version) as one undo step. The store keeps both meshes.</summary>
public sealed class SetMeshCommand(FormwerkDocument document, MeshNode node, Guid meshId, string? name = null) : IDocumentCommand
{
    private Guid _old = node.MeshId;

    public string Name => name ?? $"Replace mesh of {node.Name}";

    /// <summary>False when the object already uses this mesh.</summary>
    public bool HasEffect => node.MeshId != meshId;

    public void Do()
    {
        _old = node.MeshId;
        node.MeshId = meshId;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.MeshId = _old;
        document.NotifyChanged();
    }
}

public sealed class SetParameterCommand(FormwerkDocument document, ParametricNode node, string parameter, string expression) : IDocumentCommand
{
    private string? _old;

    public string Name => $"Set {parameter} of {node.Name}";

    public void Do()
    {
        _old = node.Parameters.GetValueOrDefault(parameter);
        node.Parameters[parameter] = expression;
        document.NotifyChanged();
    }

    public void Undo()
    {
        if (_old is null) node.Parameters.Remove(parameter);
        else node.Parameters[parameter] = _old;
        document.NotifyChanged();
    }
}

/// <summary>
/// Replaces the whole variable table (name to expression, in this order) as one undo step. It is the one command for adding,
/// changing, renaming and deleting a variable (F-PAR-03); undo brings the old table back in its old order.
/// </summary>
public sealed class SetVariablesCommand : IDocumentCommand
{
    private readonly FormwerkDocument _document;
    private readonly List<KeyValuePair<string, string>> _new;
    private List<KeyValuePair<string, string>> _old = [];

    public SetVariablesCommand(FormwerkDocument document, IEnumerable<KeyValuePair<string, string>> table, string name)
    {
        _document = document;
        _new = [.. table];
        Name = name;
    }

    public string Name { get; }

    /// <summary>False when the table would stay exactly as it is (the caller can skip the command).</summary>
    public bool HasEffect => !_document.Variables.SequenceEqual(_new);

    public void Do()
    {
        _old = [.. _document.Variables];
        Replace(_new);
    }

    public void Undo() => Replace(_old);

    private void Replace(List<KeyValuePair<string, string>> table)
    {
        _document.Variables.Clear(); // a cleared dictionary lists its items in the order they are added again
        foreach (var (name, expression) in table) _document.Variables[name] = expression;
        _document.NotifyChanged();
    }
}

/// <summary>Sets which instances of a pattern are left out as one undo step (F-PAT-03).</summary>
public sealed class SetSkippedCommand : IDocumentCommand
{
    private readonly FormwerkDocument _document;
    private readonly PatternNode _node;
    private readonly int[] _new;
    private int[] _old = [];

    public SetSkippedCommand(FormwerkDocument document, PatternNode node, IEnumerable<int> skipped, string? name = null)
    {
        _document = document;
        _node = node;
        _new = [.. skipped.Distinct().Order()];
        Name = name ?? $"Skip instances of {node.Name}";
    }

    public string Name { get; }

    /// <summary>False when the same instances are skipped already (the caller can skip the command).</summary>
    public bool HasEffect => !_node.Skipped.SequenceEqual(_new);

    public void Do()
    {
        _old = [.. _node.Skipped];
        Replace(_new);
    }

    public void Undo() => Replace(_old);

    private void Replace(int[] numbers)
    {
        _node.Skipped.Clear();
        foreach (var number in numbers) _node.Skipped.Add(number);
        _document.NotifyChanged();
    }
}

public sealed class SetRoleCommand(FormwerkDocument document, Node node, NodeRole role) : IDocumentCommand
{
    private NodeRole _old;

    public string Name => role == NodeRole.Hole ? $"Make {node.Name} a hole" : $"Make {node.Name} solid";

    public void Do()
    {
        _old = node.Role;
        node.Role = role;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.Role = _old;
        document.NotifyChanged();
    }
}

public sealed class SetVisibleCommand(FormwerkDocument document, Node node, bool visible) : IDocumentCommand
{
    private bool _old;

    public string Name => visible ? $"Show {node.Name}" : $"Hide {node.Name}";

    public void Do()
    {
        _old = node.Visible;
        node.Visible = visible;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.Visible = _old;
        document.NotifyChanged();
    }
}

public sealed class SetBooleanOperationCommand(FormwerkDocument document, BooleanNode node, BooleanOperation operation) : IDocumentCommand
{
    private BooleanOperation _old;

    public string Name => $"Set {node.Name} to {operation}";

    public void Do()
    {
        _old = node.Operation;
        node.Operation = operation;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.Operation = _old;
        document.NotifyChanged();
    }
}

public sealed class RenameNodeCommand(FormwerkDocument document, Node node, string newName) : IDocumentCommand
{
    private string _old = node.Name;

    public string Name => $"Rename {_old}";

    public void Do()
    {
        _old = node.Name;
        node.Name = newName;
        document.NotifyChanged();
    }

    public void Undo()
    {
        node.Name = _old;
        document.NotifyChanged();
    }
}

/// <summary>
/// Wraps nodes of the same parent into <paramref name="container"/> (Group, Merge, Boolean).
/// The container takes the place of the first selected node; the order of the nodes is kept.
/// </summary>
public sealed class WrapNodesCommand : IDocumentCommand
{
    private readonly FormwerkDocument _document;
    private readonly Node _container;
    private readonly Node _parent;
    private readonly List<(Node Node, int Index)> _members;
    private readonly int _containerIndex;

    public WrapNodesCommand(FormwerkDocument document, Node container, IReadOnlyCollection<Node> nodes)
    {
        if (nodes.Count == 0) throw new ArgumentException("Select at least one node.", nameof(nodes));
        if (!container.CanHaveChildren) throw new ArgumentException("The container cannot have children.", nameof(container));

        _document = document;
        _container = container;
        _parent = nodes.First().Parent ?? throw new ArgumentException("The root cannot be wrapped.", nameof(nodes));
        if (nodes.Any(n => n.Parent != _parent))
            throw new ArgumentException("All nodes must have the same parent.", nameof(nodes));

        _members = nodes.Select(n => (n, _parent.Children.ToList().IndexOf(n))).OrderBy(m => m.Item2).ToList();
        _containerIndex = _members[0].Index;
    }

    public string Name => $"{_container.GetType().Name.Replace("Node", "")} {_container.Name}";

    public void Do()
    {
        foreach (var (node, _) in _members) _parent.RemoveChild(node);
        _parent.InsertChild(_containerIndex, _container);
        for (var i = 0; i < _members.Count; i++) _container.InsertChild(i, _members[i].Node);
        _document.NotifyChanged();
    }

    public void Undo()
    {
        foreach (var (node, _) in _members) _container.RemoveChild(node);
        _parent.RemoveChild(_container);
        foreach (var (node, index) in _members) _parent.InsertChild(index, node);
        _document.NotifyChanged();
    }
}

/// <summary>Dissolves a container; its children take its place and keep their look (Ungroup, Separate).</summary>
public sealed class UnwrapNodeCommand(FormwerkDocument document, Node container) : IDocumentCommand
{
    private Node? _parent;
    private int _index;
    private List<(Node Node, Transform Original)> _children = [];

    public string Name => $"Ungroup {container.Name}";

    public void Do()
    {
        _parent = container.Parent ?? throw new InvalidOperationException("The root cannot be dissolved.");
        _children = container.Children.Select(c => (c, c.Transform)).ToList();
        _index = _parent.RemoveChild(container);

        foreach (var (child, _) in _children) container.RemoveChild(child);
        for (var i = 0; i < _children.Count; i++)
        {
            var child = _children[i].Node;
            child.Transform = Transform.Compose(container.Transform, _children[i].Original);
            _parent.InsertChild(_index + i, child);
        }

        document.NotifyChanged();
    }

    public void Undo()
    {
        foreach (var (child, _) in _children) _parent!.RemoveChild(child);
        _parent!.InsertChild(_index, container);
        for (var i = 0; i < _children.Count; i++)
        {
            _children[i].Node.Transform = _children[i].Original;
            container.InsertChild(i, _children[i].Node);
        }

        document.NotifyChanged();
    }
}
