using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class DocumentModelTests
{
    private static PrimitiveNode Cylinder(string name = "Cylinder", string diameter = "10") =>
        new("cylinder", name) { Parameters = { ["diameter"] = diameter, ["height"] = "20" } };

    [Fact]
    public void Add_and_remove_are_undoable()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Cylinder();

        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        Assert.Same(doc.Root, node.Parent);
        Assert.Single(doc.Root.Children);

        history.Execute(new RemoveNodeCommand(doc, node));
        Assert.Empty(doc.Root.Children);
        Assert.Null(node.Parent);

        history.Undo();
        Assert.Single(doc.Root.Children);
        history.Undo();
        Assert.Empty(doc.Root.Children);
    }

    [Fact]
    public void Root_cannot_be_deleted()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        Assert.Throws<InvalidOperationException>(() => history.Execute(new RemoveNodeCommand(doc, doc.Root)));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Primitive_cannot_have_children()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var cylinder = Cylinder();
        history.Execute(new AddNodeCommand(doc, doc.Root, cylinder));

        Assert.Throws<InvalidOperationException>(() => history.Execute(new AddNodeCommand(doc, cylinder, Cylinder("Inner"))));
    }

    [Fact]
    public void Document_revision_increases_with_every_change()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Cylinder();
        var before = doc.Revision;

        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        history.Execute(new SetTransformCommand(doc, node, Transform.FromPosition(new Vector3(1, 2, 3))));
        history.Undo();

        Assert.Equal(before + 3, doc.Revision);
    }

    // F-BOO-01: switching a node to a hole is a command with undo.
    [Fact]
    public void Role_switch_roundtrips()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Cylinder();
        history.Execute(new AddNodeCommand(doc, doc.Root, node));

        history.Execute(new SetRoleCommand(doc, node, NodeRole.Hole));
        Assert.Equal(NodeRole.Hole, node.Role);
        history.Undo();
        Assert.Equal(NodeRole.Solid, node.Role);
    }

    // F-BOO-02: operands stay children of the operation node and can be edited individually.
    [Fact]
    public void Boolean_node_keeps_operands_as_editable_children()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var box = new PrimitiveNode("box", "Box");
        var hole = Cylinder("Hole");
        history.Execute(new AddNodeCommand(doc, doc.Root, box));
        history.Execute(new AddNodeCommand(doc, doc.Root, hole));

        var diff = new BooleanNode("Difference", BooleanOperation.Difference);
        history.Execute(new WrapNodesCommand(doc, diff, [box, hole]));

        Assert.Equal([diff], doc.Root.Children);
        Assert.Equal([box, hole], diff.Children);

        history.Execute(new SetTransformCommand(doc, hole, Transform.FromPosition(new Vector3(5, 0, 0))));
        Assert.Equal(5, hole.Transform.Position.X);

        history.JumpTo(2);
        Assert.Equal([box, hole], doc.Root.Children);
        Assert.All(doc.Root.Children, n => Assert.Same(doc.Root, n.Parent));
    }

    [Fact]
    public void Wrap_keeps_the_position_of_the_first_node_and_undo_restores_order()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var a = Cylinder("A");
        var b = Cylinder("B");
        var c = Cylinder("C");
        foreach (var n in new[] { a, b, c }) history.Execute(new AddNodeCommand(doc, doc.Root, n));

        var group = new GroupNode("G");
        history.Execute(new WrapNodesCommand(doc, group, [c, b]));

        Assert.Equal([a, group], doc.Root.Children);
        Assert.Equal([b, c], group.Children);

        history.Undo();
        Assert.Equal([a, b, c], doc.Root.Children);
    }

    [Fact]
    public void Wrap_rejects_nodes_from_different_parents()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var g = new GroupNode("G");
        var a = Cylinder("A");
        var b = Cylinder("B");
        history.Execute(new AddNodeCommand(doc, doc.Root, g));
        history.Execute(new AddNodeCommand(doc, g, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));

        Assert.Throws<ArgumentException>(() => new WrapNodesCommand(doc, new GroupNode("X"), [a, b]));
    }

    // F-GRP-01: three levels deep, each level can be dissolved on its own.
    [Fact]
    public void Nested_groups_can_be_ungrouped_level_by_level()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var a = Cylinder("A");
        var b = Cylinder("B");
        history.Execute(new AddNodeCommand(doc, doc.Root, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));

        var g1 = new GroupNode("G1");
        history.Execute(new WrapNodesCommand(doc, g1, [a, b]));
        var g2 = new GroupNode("G2");
        history.Execute(new WrapNodesCommand(doc, g2, [g1]));
        var g3 = new GroupNode("G3");
        history.Execute(new WrapNodesCommand(doc, g3, [g2]));

        Assert.Equal(3, Depth(a) - Depth(doc.Root) - 1);

        history.Execute(new UnwrapNodeCommand(doc, g2));
        Assert.Equal([g1], g3.Children);
        history.Execute(new UnwrapNodeCommand(doc, g3));
        Assert.Equal([g1], doc.Root.Children);
        history.Execute(new UnwrapNodeCommand(doc, g1));
        Assert.Equal([a, b], doc.Root.Children);
    }

    [Fact]
    public void Ungroup_keeps_the_world_position_of_the_children()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var child = Cylinder();
        child.Transform = Transform.FromPosition(new Vector3(10, 0, 0));
        history.Execute(new AddNodeCommand(doc, doc.Root, child));
        var group = new GroupNode("G");
        history.Execute(new WrapNodesCommand(doc, group, [child]));
        history.Execute(new SetTransformCommand(doc, group, new Transform(
            new Vector3(0, 5, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)));

        var before = Vector3.Transform(Vector3.Zero, child.Transform.ToMatrix() * group.Transform.ToMatrix());
        history.Execute(new UnwrapNodeCommand(doc, group));
        var after = Vector3.Transform(Vector3.Zero, child.Transform.ToMatrix());

        Assert.Equal(before.X, after.X, 4);
        Assert.Equal(before.Y, after.Y, 4);
        Assert.Equal(before.Z, after.Z, 4);

        // Undo restores the group and the child's local transform.
        history.Undo();
        Assert.Equal(10, child.Transform.Position.X);
        Assert.Same(group, child.Parent);
    }

    [Fact]
    public void Hash_changes_with_geometry_but_not_with_name_or_color()
    {
        var node = Cylinder();
        var baseline = node.ComputeHash();

        node.Name = "Renamed";
        node.Color = 0xFF112233;
        node.Visible = false;
        Assert.Equal(baseline, node.ComputeHash());

        node.Parameters["diameter"] = "12";
        Assert.NotEqual(baseline, node.ComputeHash());
    }

    [Fact]
    public void Hash_depends_on_children_and_transform()
    {
        var group = new GroupNode("G");
        var empty = group.ComputeHash();
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(doc, group, Cylinder()));
        var withChild = group.ComputeHash();
        Assert.NotEqual(empty, withChild);

        group.Transform = Transform.FromPosition(new Vector3(1, 0, 0));
        Assert.NotEqual(withChild, group.ComputeHash());
    }

    // F-PAR-03: changing one variable changes the hash of every linked node in a single run.
    [Fact]
    public void Variable_change_updates_hash_of_linked_nodes_only()
    {
        var doc = new FormwerkDocument();
        doc.Variables["Wandstaerke"] = "2";
        doc.Variables["Doppelt"] = "Wandstaerke*2";

        var linked = Cylinder("Linked", "Doppelt+1");
        var fixedNode = Cylinder("Fixed", "10");

        var vars1 = doc.ResolveVariables(out var errors1);
        Assert.Empty(errors1);
        Assert.Equal(4, vars1["Doppelt"]);
        var linkedHash = linked.ComputeHash(vars1);
        var fixedHash = fixedNode.ComputeHash(vars1);

        doc.Variables["Wandstaerke"] = "3";
        var vars2 = doc.ResolveVariables(out _);
        Assert.Equal(6, vars2["Doppelt"]);

        Assert.NotEqual(linkedHash, linked.ComputeHash(vars2));
        Assert.Equal(fixedHash, fixedNode.ComputeHash(vars2));
    }

    [Fact]
    public void Unresolvable_variable_is_reported_not_thrown()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "B+1";
        doc.Variables["B"] = "2";

        var vars = doc.ResolveVariables(out var errors);

        Assert.True(errors.ContainsKey("A"));
        Assert.Equal(2, vars["B"]);
    }

    [Fact]
    public void Parameter_command_adds_changes_and_removes()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Cylinder();

        history.Execute(new SetParameterCommand(doc, node, "diameter", "30"));
        history.Execute(new SetParameterCommand(doc, node, "chamfer", "1"));
        Assert.Equal("30", node.Parameters["diameter"]);

        history.Undo();
        Assert.False(node.Parameters.ContainsKey("chamfer"));
        history.Undo();
        Assert.Equal("10", node.Parameters["diameter"]);
    }

    [Fact]
    public void Snapshot_is_an_independent_copy_with_the_same_ids_and_hashes()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        doc.Variables["W"] = "5";
        doc.Settings.Tolerance = 0.3;
        var cyl = Cylinder("Cyl", "W*2");
        cyl.Role = NodeRole.Hole;
        cyl.Transform = Transform.FromPosition(new Vector3(1, 2, 3));
        history.Execute(new AddNodeCommand(doc, doc.Root, cyl));
        var boolean = new BooleanNode("B", BooleanOperation.Intersection);
        history.Execute(new WrapNodesCommand(doc, boolean, [cyl]));

        var copy = doc.Snapshot();

        var copiedBoolean = Assert.IsType<BooleanNode>(Assert.Single(copy.Root.Children));
        var copiedCyl = Assert.IsType<PrimitiveNode>(Assert.Single(copiedBoolean.Children));
        Assert.Equal(BooleanOperation.Intersection, copiedBoolean.Operation);
        Assert.Equal(cyl.Id, copiedCyl.Id);
        Assert.Equal(NodeRole.Hole, copiedCyl.Role);
        Assert.Equal("W*2", copiedCyl.Parameters["diameter"]);
        Assert.Equal(0.3, copy.Settings.Tolerance);
        Assert.Equal(boolean.ComputeHash(doc.ResolveVariables(out _)), copiedBoolean.ComputeHash(copy.ResolveVariables(out _)));
        Assert.NotSame(cyl, copiedCyl);

        // Changing the original must not touch the snapshot.
        history.Execute(new SetParameterCommand(doc, cyl, "diameter", "99"));
        history.Execute(new SetTransformCommand(doc, cyl, Transform.Identity));
        Assert.Equal("W*2", copiedCyl.Parameters["diameter"]);
        Assert.Equal(1, copiedCyl.Transform.Position.X);
    }

    [Fact]
    public void ReplaceWith_takes_over_nodes_settings_and_variables_and_notifies_once()
    {
        var target = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(target, target.Root, Cylinder("Old")));
        target.Variables["Old"] = "1";

        var source = new FormwerkDocument();
        var sourceHistory = new UndoHistory();
        var group = new GroupNode("G");
        sourceHistory.Execute(new AddNodeCommand(source, source.Root, group));
        sourceHistory.Execute(new AddNodeCommand(source, group, Cylinder("New")));
        source.Variables["A"] = "2";
        source.Variables["B"] = "A*2";
        source.Settings.Tolerance = 0.35;
        source.Settings.BuildVolume = new Vector3(180, 180, 200);

        var changes = 0;
        var revision = target.Revision;
        target.Changed += (_, _) => changes++;
        target.ReplaceWith(source);

        Assert.Equal(1, changes);
        Assert.Equal(revision + 1, target.Revision);
        var loaded = Assert.IsType<GroupNode>(Assert.Single(target.Root.Children));
        Assert.Same(group, loaded);
        Assert.Same(target.Root, loaded.Parent);
        Assert.Equal("New", Assert.Single(loaded.Children).Name);
        Assert.Equal(["A", "B"], target.Variables.Keys);
        Assert.Equal(0.35, target.Settings.Tolerance);
        Assert.Equal(new Vector3(180, 180, 200), target.Settings.BuildVolume);
        Assert.Empty(source.Root.Children);
    }

    [Fact]
    public void Clone_with_new_ids_creates_a_distinct_duplicate()
    {
        var group = new GroupNode("G");
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(doc, doc.Root, group));
        history.Execute(new AddNodeCommand(doc, group, Cylinder()));

        var copy = group.Clone(newIds: true);

        Assert.NotEqual(group.Id, copy.Id);
        Assert.NotEqual(group.Children[0].Id, copy.Children[0].Id);
        Assert.Null(copy.Parent);
        Assert.Same(copy, copy.Children[0].Parent);
    }

    private static int Depth(Node node)
    {
        var depth = 0;
        for (var p = node.Parent; p is not null; p = p.Parent) depth++;
        return depth;
    }
}
