using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Cut apart (F-GRP-03): the last of the selected objects in the object list is the knife; every other selected object becomes two
/// mesh objects, what stays outside the knife (a box with a dent) and what was inside it (the sphere segment). The knife goes away.
/// </summary>
public sealed partial class MainViewModel
{
    private bool CanCut() => SelectedTopLevel().Count >= 2;

    /// <summary>One object that is cut: the node and the two meshes (world coordinates) that come out of it.</summary>
    private sealed record CutJob(Node Node, MeshData Outside, MeshData Inside);

    [RelayCommand(CanExecute = nameof(CanCut))]
    private async Task Cut()
    {
        await WaitForEvaluationAsync().ConfigureAwait(true); // the meshes come from the evaluated scene
        if (_lastResult is not { } result) return;

        var order = _document.Root.Descendants().ToList();
        var selected = SelectedTopLevel().OrderBy(order.IndexOf).ToList();
        if (selected.Count < 2) return;
        var knife = selected[^1];
        if (knife.Locked)
        {
            SetNotice(Texts.Format("Msg.CutKnifeLocked", knife.Name));
            return;
        }

        var knifeParts = SolidParts(knife, result);
        var targets = selected.Take(selected.Count - 1).Where(n => !n.Locked).Select(n => (Node: n, Parts: SolidParts(n, result)))
            .Where(t => t.Parts.Count > 0).ToList();
        if (knifeParts.Count == 0 || targets.Count == 0)
        {
            SetNotice(Texts.Format("Msg.NothingCut", knife.Name));
            return;
        }

        List<CutJob> jobs;
        try
        {
            // the boolean kernel is shared with the evaluator, which is not thread-safe: one at a time
            jobs = await Task.Run(() =>
            {
                lock (_evaluatorLock)
                {
                    var knifeMesh = Merged(knifeParts);
                    var found = new List<CutJob>();
                    foreach (var (node, parts) in targets)
                    {
                        var body = Merged(parts);
                        var inside = _kernel.Boolean(BooleanOperation.Intersection, [body, knifeMesh], CancellationToken.None);
                        if (inside.IsEmpty) continue; // the knife misses it
                        var outside = _kernel.Boolean(BooleanOperation.Difference, [body, knifeMesh], CancellationToken.None);
                        if (outside.IsEmpty) continue; // the knife swallows it: nothing to cut apart
                        found.Add(new CutJob(node, outside, inside));
                    }

                    return found;
                }
            }).ConfigureAwait(true);
        }
        catch (GeometryException ex)
        {
            SetNotice(Texts.Format("Msg.CutFailed", ex.Message));
            return;
        }

        if (jobs.Count == 0)
        {
            SetNotice(Texts.Format("Msg.NothingCut", knife.Name));
            return;
        }

        // The commands run in this order, so the indices are worked out on copies of the child lists.
        var lists = new Dictionary<Node, List<Node>>();
        List<Node> ListOf(Node parent) => lists.TryGetValue(parent, out var l) ? l : lists[parent] = [.. parent.Children];

        var commands = new List<IDocumentCommand>();
        var created = new List<Guid>();
        foreach (var job in jobs)
        {
            var parent = job.Node.Parent!;
            var list = ListOf(parent);
            var index = list.IndexOf(job.Node);
            list.RemoveAt(index);
            commands.Add(new RemoveNodeCommand(_document, job.Node));
            foreach (var (mesh, name) in new[] { (job.Outside, job.Node.Name), (job.Inside, Texts.Format("Name.CutPiece", job.Node.Name)) })
            {
                var piece = CreateMeshNodeInParentSpace(job.Node, name, mesh, job.Node.Color);
                if (piece is null) continue;
                list.Insert(index, piece);
                commands.Add(new AddNodeCommand(_document, parent, piece, index++));
                created.Add(piece.Id);
            }
        }

        commands.Add(new RemoveNodeCommand(_document, knife));
        _history.Execute(new CompositeCommand($"Cut {(jobs.Count == 1 ? jobs[0].Node.Name : $"{jobs.Count} objects")}", commands));
        SelectOnly(created);
        SetNotice(Texts.Format("Msg.Cut", jobs.Count == 1 ? jobs[0].Node.Name : $"{jobs.Count} objects", knife.Name,
            created.Count.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>The visible solid parts the scene shows for a node and everything inside it (world coordinates).</summary>
    private static List<ScenePart> SolidParts(Node node, EvaluationResult result)
    {
        var ids = new HashSet<Guid>(node.Descendants().Select(d => d.Id)) { node.Id };
        return [.. result.Parts.Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty && ids.Contains(p.Source.Id))];
    }

    /// <summary>One mesh of the parts: several parts are united, so overlaps do not count twice.</summary>
    private MeshData Merged(List<ScenePart> parts) =>
        parts.Count == 1 ? parts[0].Mesh : _kernel.Boolean(BooleanOperation.Union, [.. parts.Select(p => p.Mesh)], CancellationToken.None);
}
