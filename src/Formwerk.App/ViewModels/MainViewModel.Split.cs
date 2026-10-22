using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.App.ViewModels;

/// <summary>Split into parts (F-SPL-03): an object made of several separate shells becomes one mesh object per shell.</summary>
public sealed partial class MainViewModel
{
    private bool CanSplitParts() => SelectedTopLevel().Count > 0;

    /// <summary>
    /// Replaces every selected object (not locked) that consists of several shells by one mesh object per shell, named "name 1", "name 2", ...
    /// from left to right, with the same color and in the same place. Objects with a single shell stay as they are. One undo step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSplitParts))]
    private async Task SplitParts()
    {
        await WaitForEvaluationAsync().ConfigureAwait(true); // the meshes come from the evaluated scene
        if (_lastResult is not { } result) return;

        var targets = SelectedTopLevel().Where(n => !n.Locked)
            .Select(n => (Node: n, Parts: SolidParts(n, result))).Where(t => t.Parts.Count > 0).ToList();

        var found = await Task.Run(() => targets
            .Select(t => (t.Node, Shells: MeshShells.Split(MeshCombine.Concat(t.Parts.Select(p => p.Mesh)))))
            .Where(t => t.Shells.Count >= 2)
            .ToList()).ConfigureAwait(true);

        if (found.Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToSplit"));
            return;
        }

        // The commands run in this order, so the indices are worked out on copies of the child lists.
        var lists = new Dictionary<Node, List<Node>>();
        List<Node> ListOf(Node parent) => lists.TryGetValue(parent, out var l) ? l : lists[parent] = [.. parent.Children];

        var commands = new List<IDocumentCommand>();
        var created = new List<Guid>();
        foreach (var (node, shells) in found)
        {
            var parent = node.Parent!;
            var list = ListOf(parent);
            var index = list.IndexOf(node);
            list.RemoveAt(index);
            commands.Add(new RemoveNodeCommand(_document, node));

            var colors = SolidParts(node, result).Select(p => p.Source.Color).Distinct().ToList();
            var color = colors.Count == 1 ? colors[0] : node.Color;
            var number = 1;
            foreach (var shell in shells)
            {
                var piece = CreateMeshNodeInParentSpace(node, $"{node.Name} {number++}", shell, color);
                if (piece is null) continue;
                list.Insert(index, piece);
                commands.Add(new AddNodeCommand(_document, parent, piece, index++));
                created.Add(piece.Id);
            }
        }

        var label = found.Count == 1 ? found[0].Node.Name : $"{found.Count} objects";
        _history.Execute(new CompositeCommand($"Split {label}", commands));
        SelectOnly(created);
        SetNotice(Texts.Format("Msg.Split", label, created.Count.ToString(CultureInfo.InvariantCulture)));
    }
}
