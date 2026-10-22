using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Keeping holes and chamfers when scaling (F-SCL-05). With the option on, a shape keeps its detail sizes (edge size, wall) and a group,
/// boolean or merge that is scaled evenly is scaled child by child: positions and the sizes of solid shapes follow the factor, holes and
/// edge cuts keep their size. With the option off the details scale with the shape and a container is scaled as a whole.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Scaling does not change the sizes of holes, chamfers and other details (on by default, remembered).</summary>
    [ObservableProperty]
    public partial bool KeepDetailsWhenScaling { get; set; } = true;

    partial void OnKeepDetailsWhenScalingChanged(bool value) => PersistUserSettings();

    /// <summary>The small panel with the option is shown: the Scale tool is active and something is selected.</summary>
    public bool IsScaleOptionsVisible => ActiveTool == ViewportTool.Scale && _selected.Count > 0;

    /// <summary>Tool generators: their sizes are those of a hole or of an edge cut, not of a shape that grows with the body.</summary>
    private static bool IsToolNode(Node node) => node is PrimitiveNode { GeneratorId: "hole" or "edgecut" or "edgering" };

    /// <summary>
    /// What every descendant looks like now, or null when the tree cannot be scaled child by child (a pattern or mirror, a shape that is
    /// not known, a parameter that does not evaluate): then the container is scaled as a whole.
    /// </summary>
    private Dictionary<Node, NodeSnapshot>? SnapshotForKeepingDetails(Node container)
    {
        if (!container.CanHaveChildren || container is PatternNode or MirrorNode) return null;

        var variables = _document.ResolveVariables(out _);
        var snapshot = new Dictionary<Node, NodeSnapshot>();
        foreach (var node in container.Descendants())
        {
            switch (node)
            {
                case PatternNode or MirrorNode:
                    return null;
                case PrimitiveNode primitive:
                    if (ShapeLibrary.Find(primitive.GeneratorId) is not { } generator) return null;
                    var numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    if (!IsToolNode(primitive))
                    {
                        var values = primitive.EvaluateParameters(variables, out var errors);
                        if (errors.Count > 0) return null;
                        foreach (var binding in generator.ScaleBindings)
                            numbers[binding.Parameter] = values.TryGetValue(binding.Parameter, out var v) ? v : generator.Parameters.First(d => d.Name == binding.Parameter).Default;
                    }

                    snapshot[node] = new NodeSnapshot(node.Transform, new Dictionary<string, string>(primitive.Parameters, StringComparer.OrdinalIgnoreCase), numbers);
                    break;
                case GroupNode or MergeNode or BooleanNode or MeshNode:
                    snapshot[node] = new NodeSnapshot(node.Transform, new Dictionary<string, string>(), new Dictionary<string, double>());
                    break;
                default:
                    return null;
            }
        }

        return snapshot;
    }

    /// <summary>Puts every descendant to the state at the start and then scales it evenly by <paramref name="factor"/> (positions, solid shapes, meshes; holes keep their sizes).</summary>
    private static void BakeUniformScale(ScaleSession s, double factor)
    {
        var f = (float)factor;
        foreach (var (node, snap) in s.Snapshot!)
        {
            var scale = node is MeshNode ? snap.Transform.Scale * f : snap.Transform.Scale;
            node.Transform = snap.Transform with { Position = snap.Transform.Position * f, Scale = scale };
            if (node is not PrimitiveNode primitive) continue;

            primitive.Parameters.Clear();
            foreach (var (name, expression) in snap.Parameters) primitive.Parameters[name] = expression;
            if (IsToolNode(primitive) || ShapeLibrary.Find(primitive.GeneratorId) is not { } generator) continue;

            foreach (var binding in generator.ScaleBindings)
            {
                var definition = generator.Parameters.First(p => string.Equals(p.Name, binding.Parameter, StringComparison.OrdinalIgnoreCase));
                var value = Math.Clamp(Math.Round(snap.Numbers[binding.Parameter] * factor, 3), definition.Min, definition.Max);
                primitive.Parameters[binding.Parameter] = value.ToString("R", CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Gives every descendant back its state at the start.</summary>
    private static void RestoreSnapshot(ScaleSession s)
    {
        foreach (var (node, snap) in s.Snapshot!)
        {
            node.Transform = snap.Transform;
            if (node is not PrimitiveNode primitive) continue;
            primitive.Parameters.Clear();
            foreach (var (name, expression) in snap.Parameters) primitive.Parameters[name] = expression;
        }
    }

    /// <summary>Did the drag change a child (it did when the factor was not 1 and the container was scaled child by child)?</summary>
    private static bool ChangedByBake(ScaleSession s) =>
        s.Snapshot!.Any(pair => pair.Key.Transform != pair.Value.Transform
            || (pair.Key is PrimitiveNode p && !p.Parameters.OrderBy(x => x.Key).SequenceEqual(pair.Value.Parameters.OrderBy(x => x.Key))));

    /// <summary>The commands of a drag that scaled the children: parameters and transforms in one undo step. The nodes go back to the start first, the commands do the change.</summary>
    private void CommitBakedScale(ScaleSession s, Transform finalTransform, List<IDocumentCommand> commands)
    {
        var transforms = new List<(Node Node, Transform Old, Transform New)>();
        var parameters = new List<(PrimitiveNode Node, string Name, string Expression)>();
        foreach (var (node, snap) in s.Snapshot!)
        {
            if (node.Transform != snap.Transform) transforms.Add((node, snap.Transform, node.Transform));
            if (node is not PrimitiveNode primitive) continue;
            foreach (var (name, expression) in primitive.Parameters)
            {
                if (!snap.Parameters.TryGetValue(name, out var before) || before != expression) parameters.Add((primitive, name, expression));
            }
        }

        if (finalTransform != s.StartTransform) transforms.Add((s.Node, s.StartTransform, finalTransform));

        RestoreScaleStart(s);
        foreach (var (node, name, expression) in parameters) commands.Add(new SetParameterCommand(_document, node, name, expression));
        if (transforms.Count > 0) commands.Add(new SetTransformsCommand(_document, transforms, "Scale"));
    }
}
