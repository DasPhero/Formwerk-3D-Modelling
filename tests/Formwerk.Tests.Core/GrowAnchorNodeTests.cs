using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>The choice where a shape grows from is a property of the shape, undoable, copied with it and not part of its geometry.</summary>
public class GrowAnchorNodeTests
{
    [Fact]
    public void The_default_is_the_center_in_X_and_Y_and_the_bottom_in_Z()
    {
        Assert.Equal(new Vector3(0, 0, -1), PrimitiveNode.DefaultGrowAnchor);
        Assert.Equal(PrimitiveNode.DefaultGrowAnchor, new PrimitiveNode("box", "Box").GrowAnchor);
    }

    [Fact]
    public void The_choice_is_copied_with_the_shape_and_does_not_change_its_hash()
    {
        var box = new PrimitiveNode("box", "Box") { GrowAnchor = new Vector3(1, -1, 0) };
        box.Parameters["width"] = "20";
        var plain = new PrimitiveNode("box", "Box");
        plain.Parameters["width"] = "20";

        Assert.Equal(new Vector3(1, -1, 0), ((PrimitiveNode)box.Clone(newIds: true)).GrowAnchor);
        Assert.Equal(plain.ComputeHash(), box.ComputeHash());
    }

    [Fact]
    public void Setting_the_choice_can_be_undone_and_redone()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var box = new PrimitiveNode("box", "Box");
        history.Execute(new AddNodeCommand(doc, doc.Root, box));

        history.Execute(new SetGrowAnchorCommand(doc, box, new Vector3(-1, 0, 0)));
        Assert.Equal(new Vector3(-1, 0, 0), box.GrowAnchor);

        history.Undo();
        Assert.Equal(PrimitiveNode.DefaultGrowAnchor, box.GrowAnchor);
        history.Redo();
        Assert.Equal(new Vector3(-1, 0, 0), box.GrowAnchor);
    }

    [Fact]
    public void Only_minus_one_zero_and_one_are_valid_per_axis()
    {
        var doc = new FormwerkDocument();
        var box = new PrimitiveNode("box", "Box");

        Assert.Throws<ArgumentOutOfRangeException>(() => new SetGrowAnchorCommand(doc, box, new Vector3(2, 0, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SetGrowAnchorCommand(doc, box, new Vector3(0, 0.5f, 0)));
    }
}
