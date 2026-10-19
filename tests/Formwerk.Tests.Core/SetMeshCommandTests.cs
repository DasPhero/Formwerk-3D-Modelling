using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class SetMeshCommandTests
{
    private static (FormwerkDocument Doc, MeshNode Node, UndoHistory History) Setup(Guid meshId)
    {
        var doc = new FormwerkDocument();
        var node = new MeshNode("Part", meshId);
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        return (doc, node, history);
    }

    [Fact]
    public void Replacing_the_mesh_is_one_undoable_step()
    {
        var original = Guid.NewGuid();
        var repaired = Guid.NewGuid();
        var (doc, node, history) = Setup(original);
        var revision = doc.Revision;

        history.Execute(new SetMeshCommand(doc, node, repaired, "Repair Part"));

        Assert.Equal(repaired, node.MeshId);
        Assert.Equal(revision + 1, doc.Revision);
        Assert.Equal("Repair Part", history.Entries[^1].Name);

        history.Undo();
        Assert.Equal(original, node.MeshId);

        history.Redo();
        Assert.Equal(repaired, node.MeshId);
    }

    [Fact]
    public void The_hash_follows_the_mesh_so_that_cached_geometry_is_not_reused()
    {
        var (doc, node, history) = Setup(Guid.NewGuid());
        var before = node.ComputeHash(null);

        history.Execute(new SetMeshCommand(doc, node, Guid.NewGuid()));

        Assert.NotEqual(before, node.ComputeHash(null));
    }

    [Fact]
    public void A_copy_of_the_node_keeps_the_replaced_mesh()
    {
        var (doc, node, history) = Setup(Guid.NewGuid());
        var repaired = Guid.NewGuid();
        history.Execute(new SetMeshCommand(doc, node, repaired));

        var copy = (MeshNode)node.Clone();

        Assert.Equal(repaired, copy.MeshId);
    }

    [Fact]
    public void Replacing_by_the_same_mesh_reports_no_effect()
    {
        var id = Guid.NewGuid();
        var (doc, node, _) = Setup(id);

        Assert.False(new SetMeshCommand(doc, node, id).HasEffect);
        Assert.True(new SetMeshCommand(doc, node, Guid.NewGuid()).HasEffect);
    }

    [Fact]
    public void Several_replacements_can_be_one_history_entry()
    {
        var a = new MeshNode("A", Guid.NewGuid());
        var b = new MeshNode("B", Guid.NewGuid());
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(doc, doc.Root, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));
        var oldA = a.MeshId;
        var oldB = b.MeshId;
        var entries = history.Entries.Count;

        history.Execute(new CompositeCommand("Repair 2 objects",
            [new SetMeshCommand(doc, a, Guid.NewGuid()), new SetMeshCommand(doc, b, Guid.NewGuid())]));

        Assert.Equal(entries + 1, history.Entries.Count);
        history.Undo();
        Assert.Equal(oldA, a.MeshId);
        Assert.Equal(oldB, b.MeshId);
    }
}
