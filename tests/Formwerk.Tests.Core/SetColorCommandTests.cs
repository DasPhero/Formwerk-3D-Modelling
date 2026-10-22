using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class SetColorCommandTests
{
    [Fact]
    public void Setting_the_color_is_undoable_and_redoable()
    {
        var doc = new FormwerkDocument();
        var node = new GroupNode("G");
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));
        var history = new UndoHistory();
        var revision = doc.Revision;

        history.Execute(new SetColorCommand(doc, node, 0xFF112233));

        Assert.Equal(0xFF112233u, node.Color);
        Assert.Equal(revision + 1, doc.Revision);
        Assert.Equal("Color G", history.Entries[^1].Name);

        history.Undo();
        Assert.Equal(0xFFB0B8C4u, node.Color);

        history.Redo();
        Assert.Equal(0xFF112233u, node.Color);
    }

    [Fact]
    public void The_color_becomes_opaque()
    {
        var doc = new FormwerkDocument();
        var node = new GroupNode("G");
        var history = new UndoHistory();
        history.Execute(new AddNodeCommand(doc, doc.Root, node));

        history.Execute(new SetColorCommand(doc, node, 0x00FF8800));

        Assert.Equal(0xFFFF8800u, node.Color);
    }
}
