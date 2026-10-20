using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>The mirror node (F-PAT-04): a container whose own transform is the mirror plane.</summary>
public class MirrorNodeTests
{
    private static MirrorNode Mirror() => new("Mirror") { Parameters = { ["axis"] = "0", ["copyOnly"] = "0" } };

    [Fact]
    public void A_mirror_can_hold_children_and_has_parameters()
    {
        var mirror = Mirror();

        Assert.True(mirror.CanHaveChildren);
        Assert.IsAssignableFrom<ParametricNode>(mirror);
    }

    [Fact]
    public void A_copy_keeps_parameters_and_children_and_is_independent()
    {
        var doc = new FormwerkDocument();
        var mirror = Mirror();
        new AddNodeCommand(doc, doc.Root, mirror).Do();
        new AddNodeCommand(doc, mirror, new PrimitiveNode("box", "Box")).Do();

        var copy = (MirrorNode)mirror.Clone(newIds: true);
        copy.Parameters["axis"] = "2";

        Assert.NotEqual(mirror.Id, copy.Id);
        Assert.Equal("0", mirror.Parameters["axis"]);
        Assert.Equal("0", mirror.Parameters["copyOnly"]);
        Assert.Single(copy.Children);
        Assert.NotEqual(mirror.Children[0].Id, copy.Children[0].Id);
    }

    [Fact]
    public void The_hash_follows_the_plane_and_the_mode_but_not_the_name()
    {
        var mirror = Mirror();
        var hash = mirror.ComputeHash();

        mirror.Name = "Symmetry";
        Assert.Equal(hash, mirror.ComputeHash());

        mirror.Parameters["axis"] = "1";
        Assert.NotEqual(hash, mirror.ComputeHash());
        mirror.Parameters["axis"] = "0";

        mirror.Parameters["copyOnly"] = "1";
        Assert.NotEqual(hash, mirror.ComputeHash());
        mirror.Parameters["copyOnly"] = "0";

        mirror.Transform = Transform.FromPosition(new System.Numerics.Vector3(5, 0, 0)); // the plane moves
        Assert.NotEqual(hash, mirror.ComputeHash());
    }

    [Fact]
    public void The_hash_differs_from_that_of_a_pattern_with_the_same_parameters()
    {
        var pattern = new PatternNode("P", PatternKind.Linear) { Parameters = { ["axis"] = "0", ["copyOnly"] = "0" } };

        Assert.NotEqual(pattern.ComputeHash(), Mirror().ComputeHash());
    }

    [Fact]
    public void The_hash_follows_the_children()
    {
        var doc = new FormwerkDocument();
        var mirror = Mirror();
        new AddNodeCommand(doc, doc.Root, mirror).Do();
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = "10" } };
        new AddNodeCommand(doc, mirror, box).Do();
        var hash = mirror.ComputeHash();

        box.Parameters["width"] = "12";

        Assert.NotEqual(hash, mirror.ComputeHash());
    }

    [Fact]
    public void The_plane_axis_is_changed_by_the_normal_parameter_command()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var mirror = Mirror();

        history.Execute(new SetParameterCommand(doc, mirror, "axis", "2"));
        Assert.Equal("2", mirror.Parameters["axis"]);

        history.Undo();
        Assert.Equal("0", mirror.Parameters["axis"]);
    }
}
