using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Repair;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Split at the plane (F-SPL-01): the plane of the section view (an axis, a face or three points) cuts the objects into two closed mesh
/// objects each. The section view is the preview; "Split Here" makes the cut.
/// </summary>
public sealed partial class MainViewModel
{
    private bool CanSplitAtPlane() => IsSectionActive && _document.Root.Children.Count > 0;

    /// <summary>
    /// Cuts every selected object (all top-level objects when nothing is selected) that is not locked and that the plane goes through into
    /// "name A" (behind the plane, the side the section view keeps) and "name B" (in front of it), with color and place kept. Objects the
    /// plane misses stay as they are. One undo step; the section view stays on.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSplitAtPlane))]
    private async Task SplitAtPlane()
    {
        await WaitForEvaluationAsync().ConfigureAwait(true); // the meshes come from the evaluated scene
        if (_lastResult is not { } result) return;

        var plane = CurrentSectionPlane();
        IEnumerable<Node> chosen = _selected.Count > 0 ? SelectedTopLevel() : _document.Root.Children.ToList();
        var targets = chosen.Where(n => !n.Locked).Select(n => (Node: n, Parts: SolidParts(n, result))).Where(t => t.Parts.Count > 0).ToList();

        List<(Node Node, PlaneSplit Split)> found;
        try
        {
            // the boolean kernel is shared with the evaluator, which is not thread-safe: one at a time
            found = await Task.Run(() =>
            {
                lock (_evaluatorLock)
                {
                    var list = new List<(Node, PlaneSplit)>();
                    foreach (var (node, parts) in targets)
                    {
                        if (PlaneSplitter.Split(Merged(parts), plane, _kernel, CancellationToken.None) is { } split)
                            list.Add((node, new PlaneSplit(Tidy(split.Behind), Tidy(split.Front))));
                    }

                    return list;
                }
            }).ConfigureAwait(true);
        }
        catch (GeometryException ex)
        {
            SetNotice(Texts.Format("Msg.CutFailed", ex.Message));
            return;
        }

        if (found.Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingSplitPlane"));
            return;
        }

        var lists = new Dictionary<Node, List<Node>>();
        var commands = new List<IDocumentCommand>();
        var created = new List<Guid>();
        foreach (var (node, split) in found)
        {
            var colors = SolidParts(node, result).Select(p => p.Source.Color).Distinct().ToList();
            ReplaceByMeshes(node, [(split.Behind, $"{node.Name} A"), (split.Front, $"{node.Name} B")], colors.Count == 1 ? colors[0] : node.Color, lists, commands, created);
        }

        var label = found.Count == 1 ? found[0].Node.Name : $"{found.Count} objects";
        _history.Execute(new CompositeCommand($"Split {label} at plane", commands));
        SelectOnly(created);
        SetNotice(Texts.Format("Msg.SplitPlane", label, created.Count.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Adds the commands that take <paramref name="node"/> out of its parent and put a mesh object for every piece (world coordinates) in its
    /// place. <paramref name="lists"/> holds copies of the child lists that follow the commands, so that the indices stay right when several
    /// objects of one parent are replaced.
    /// </summary>
    private void ReplaceByMeshes(Node node, IEnumerable<(MeshData Mesh, string Name)> pieces, uint color,
        Dictionary<Node, List<Node>> lists, List<IDocumentCommand> commands, List<Guid> created)
    {
        var parent = node.Parent!;
        if (!lists.TryGetValue(parent, out var list)) lists[parent] = list = [.. parent.Children];
        var index = list.IndexOf(node);
        list.RemoveAt(index);
        commands.Add(new RemoveNodeCommand(_document, node));
        foreach (var (mesh, name) in pieces)
        {
            var piece = CreateMeshNodeInParentSpace(node, name, mesh, color);
            if (piece is null) continue;
            list.Insert(index, piece);
            commands.Add(new AddNodeCommand(_document, parent, piece, index++));
            created.Add(piece.Id);
        }
    }

    /// <summary>
    /// A plane that goes exactly through corners of a round body leaves doubled corners and a sliver triangle in the kernel's result; the
    /// repair welds and removes them, so the parts do not start their life with a warning. The result is only used when it is better.
    /// </summary>
    private static MeshData Tidy(MeshData mesh)
    {
        var repaired = MeshRepair.Repair(mesh);
        return repaired.Changed && repaired.After.ProblemCount < repaired.Before.ProblemCount ? repaired.Mesh : mesh;
    }
}
