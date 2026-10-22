using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.Core.Model;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Before and after of a repair (F-REP-04): a switch lays the old version of every repaired mesh over the repaired one as a ghost. The old
/// mesh stays in the (append-only) mesh store, so a comparison is valid while the object still uses the repaired mesh: undo ends it, redo
/// brings it back. Only what is drawn changes; the document, the history and the exports are untouched.
/// </summary>
public sealed partial class MainViewModel
{
    private sealed record RepairComparison(Guid NodeId, Guid OldMeshId, Guid NewMeshId);

    private readonly List<RepairComparison> _comparisons = [];
    private readonly Guid _beforeId = Guid.NewGuid();

    /// <summary>At least one object shows a repaired mesh whose old version is still known.</summary>
    [ObservableProperty]
    public partial bool HasRepairComparison { get; set; }

    /// <summary>The old versions of the repaired meshes are shown over the new ones (View menu).</summary>
    [ObservableProperty]
    public partial bool ShowRepairBefore { get; set; }

    partial void OnShowRepairBeforeChanged(bool value)
    {
        RefreshRepairCompare();
        UpdateRepairOverlay();
    }

    /// <summary>A repair replaced the mesh of an object: remembers the old one (the latest repair of an object counts).</summary>
    private void NoteRepair(Guid nodeId, Guid oldMeshId, Guid newMeshId)
    {
        _comparisons.RemoveAll(c => c.NodeId == nodeId);
        _comparisons.Add(new RepairComparison(nodeId, oldMeshId, newMeshId));
    }

    private IEnumerable<(MeshNode Node, RepairComparison Comparison)> ValidComparisons() =>
        _comparisons.Select(c => (Node: _document.FindNode(c.NodeId) as MeshNode, Comparison: c))
            .Where(x => x.Node is not null && x.Node.MeshId == x.Comparison.NewMeshId && Meshes.Contains(x.Comparison.OldMeshId))
            .Select(x => (x.Node!, x.Comparison));

    /// <summary>Called when the document changed (an undo can end a comparison, a redo bring it back).</summary>
    private void RefreshRepairCompare()
    {
        HasRepairComparison = ValidComparisons().Any();
        if (!HasRepairComparison && ShowRepairBefore) ShowRepairBefore = false;
    }

    /// <summary>Puts the old versions into the items of the view, or takes them out.</summary>
    private void UpdateRepairOverlay()
    {
        var shown = ViewportItems.Any(i => i.NodeId == _beforeId);
        if (!ShowRepairBefore && !shown) return;

        var items = ViewportItems.Where(i => i.NodeId != _beforeId).ToList();
        if (ShowRepairBefore)
        {
            foreach (var (node, comparison) in ValidComparisons())
            {
                var old = Meshes.Get(comparison.OldMeshId)!.Transformed(TransformOps.LocalToWorld(node));
                items.Add(new ViewportItem(_beforeId, old, ViewportItemKind.Ghost, node.Color));
            }
        }

        ViewportItems = items;
    }

    /// <summary>A new scene was built while the old versions are shown: they are put back where the objects are now.</summary>
    private void KeepRepairOverlay()
    {
        if (ShowRepairBefore) UpdateRepairOverlay();
    }
}
