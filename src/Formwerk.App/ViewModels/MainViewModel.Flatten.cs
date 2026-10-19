using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;

namespace Formwerk.App.ViewModels;

/// <summary>Flatten (F-GRP-04): an object, a group or the result of a boolean becomes one mesh object.</summary>
public sealed partial class MainViewModel
{
    private bool CanFlatten() => SelectedTopLevel().Any(n => n is not MeshNode);

    /// <summary>
    /// Replaces every selected object (that is not a mesh yet and not locked) by a mesh object with the same name, place and color.
    /// The mesh is what the scene shows of the object now: all visible solid parts, with the cuts of holes and booleans applied and
    /// without hole ghosts. One undo step: the old objects are only taken out of the tree, so undo brings them back with every
    /// parameter and child.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanFlatten))]
    private async Task Flatten()
    {
        await WaitForEvaluationAsync().ConfigureAwait(true); // the mesh comes from the evaluated scene
        var result = _lastResult;
        var targets = SelectedTopLevel().Where(n => n is not MeshNode && !n.Locked).ToList();

        var commands = new List<IDocumentCommand>();
        var created = new List<Guid>();
        var triangles = 0;
        string? lastName = null;
        foreach (var node in targets)
        {
            var flat = result is null ? null : BuildFlatNode(node, result);
            if (flat is null) continue;

            var parent = node.Parent!;
            var index = parent.Children.ToList().IndexOf(node);
            commands.Add(new RemoveNodeCommand(_document, node));
            commands.Add(new AddNodeCommand(_document, parent, flat, index));
            created.Add(flat.Id);
            triangles += Meshes.Get(flat.MeshId)!.TriangleCount;
            lastName = flat.Name;
        }

        if (created.Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToFlatten"));
            return;
        }

        var label = created.Count == 1 ? lastName! : $"{created.Count} objects";
        _history.Execute(new CompositeCommand($"Flatten {label}", commands));
        SelectOnly(created);
        SetNotice(Texts.Format("Msg.Flattened", label, triangles.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)));
    }

    /// <summary>The mesh object for <paramref name="node"/>, or null when it has nothing solid to show.</summary>
    private MeshNode? BuildFlatNode(Node node, EvaluationResult result)
    {
        var ids = new HashSet<Guid>(node.Descendants().Select(d => d.Id)) { node.Id };
        var parts = result.Parts.Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty && ids.Contains(p.Source.Id)).ToList();
        if (parts.Count == 0) return null;
        if (!Matrix4x4.Invert(TransformOps.ParentToWorld(node), out var worldToParent)) return null;

        // The parts are in world coordinates; the new object lives where the old one did, in the space of its parent, and
        // gets its origin in the center of its bottom like every imported or generated object.
        var inParent = MeshCombine.Concat(parts.Select(p => p.Mesh)).Transformed(worldToParent);
        var bounds = inParent.ComputeBounds();
        var origin = new Vector3(bounds.Center.X, bounds.Center.Y, bounds.Min.Z);
        var mesh = inParent.Transformed(Matrix4x4.CreateTranslation(-origin));

        var meshId = Guid.NewGuid();
        Meshes.Add(meshId, mesh);
        var colors = parts.Select(p => p.Source.Color).Distinct().ToList();
        return new MeshNode(node.Name, meshId)
        {
            Transform = Transform.FromPosition(origin),
            Color = colors.Count == 1 ? colors[0] : node.Color,
        };
    }
}
