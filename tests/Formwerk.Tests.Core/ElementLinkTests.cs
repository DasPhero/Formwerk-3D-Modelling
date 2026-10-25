using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>An object made from a library element remembers which element and which values (F-PAR-05), and can be rebuilt in place.</summary>
public class ElementLinkTests
{
    private static ElementLink Link(string value = "8") => new("cable-clip", new Dictionary<string, string> { ["cable"] = value, ["wall"] = "2" });

    private static Node Tree(string childName, double width)
    {
        var top = new BooleanNode("Part", BooleanOperation.Difference);
        var body = new PrimitiveNode("box", childName);
        body.Parameters["width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        new UndoHistory().Execute(new AddNodeCommand(new FormwerkDocument(), top, body)); // a loose tree, as a recipe makes it
        return top;
    }

    [Fact]
    public void A_copy_of_a_node_keeps_the_element_link_and_the_geometry_hash_ignores_it()
    {
        var node = new GroupNode("Clip") { Element = Link() };
        var plain = new GroupNode("Clip");

        var copy = node.Clone(newIds: true);

        Assert.Equal("cable-clip", copy.Element!.ElementId);
        Assert.Equal("8", copy.Element.Values["cable"]);
        Assert.Equal(plain.ComputeHash(), node.ComputeHash());
        Assert.Null(plain.Clone().Element);
    }

    [Fact]
    public void Rebuilding_swaps_the_parts_in_place_keeps_the_node_and_resets_the_scale()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Tree("Old", 10);
        node.Name = "My clip";
        node.Color = 0xFF112233;
        node.Element = Link("8");
        node.Transform = new Transform(new Vector3(5, 6, 0), Quaternion.Identity, new Vector3(2, 2, 2));
        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        var id = node.Id;
        var oldChild = node.Children[0];

        history.Execute(new RebuildElementCommand(doc, node, Tree("New", 20), Link("12")));

        Assert.Same(node, doc.Root.Children[0]);
        Assert.Equal(id, node.Id);
        Assert.Equal("New", node.Children.Single().Name);
        Assert.Equal("My clip", node.Name);
        Assert.Equal(0xFF112233u, node.Color);
        Assert.Equal(new Vector3(5, 6, 0), node.Transform.Position);
        Assert.Equal(Vector3.One, node.Transform.Scale);
        Assert.Equal("12", node.Element!.Values["cable"]);
        Assert.Null(oldChild.Parent);
    }

    [Fact]
    public void Rebuilding_can_be_undone_and_redone()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = Tree("Old", 10);
        node.Element = Link("8");
        node.Transform = new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(2, 2, 2));
        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        history.Execute(new RebuildElementCommand(doc, node, Tree("New", 20), Link("12")));

        history.Undo();
        Assert.Equal("Old", node.Children.Single().Name);
        Assert.Equal("8", node.Element!.Values["cable"]);
        Assert.Equal(new Vector3(2, 2, 2), node.Transform.Scale);

        history.Redo();
        Assert.Equal("New", node.Children.Single().Name);
        Assert.Equal("12", node.Element!.Values["cable"]);
        Assert.Equal(Vector3.One, node.Transform.Scale);
    }

    [Fact]
    public void A_single_shape_element_gets_its_parameters_replaced()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var shape = new PrimitiveNode("tube", "Sleeve") { Element = Link("8") };
        shape.Parameters["height"] = "10";
        shape.Parameters["outerDiameter"] = "6";
        history.Execute(new AddNodeCommand(doc, doc.Root, shape));
        var fresh = new PrimitiveNode("tube", "Sleeve");
        fresh.Parameters["height"] = "25";
        fresh.Parameters["wall"] = "1";

        history.Execute(new RebuildElementCommand(doc, shape, fresh, Link("9")));

        Assert.Equal("25", shape.Parameters["height"]);
        Assert.Equal("1", shape.Parameters["wall"]);
        Assert.False(shape.Parameters.ContainsKey("outerDiameter"));

        history.Undo();
        Assert.Equal("10", shape.Parameters["height"]);
        Assert.Equal("6", shape.Parameters["outerDiameter"]);
        Assert.False(shape.Parameters.ContainsKey("wall"));
    }

    [Fact]
    public void A_different_kind_of_node_cannot_replace_the_parts()
    {
        var doc = new FormwerkDocument();
        var node = new GroupNode("Group");

        Assert.Throws<ArgumentException>(() => new RebuildElementCommand(doc, node, new MergeNode("Merge"), Link()));
    }
}
