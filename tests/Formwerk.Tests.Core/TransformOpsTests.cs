using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class TransformOpsTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    /// <summary>Root > outer group (moved, turned 90° about Z, scaled 2) > inner group (moved) > box (moved, turned).</summary>
    private static (FormwerkDocument Doc, Node Box) NestedBox()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var outer = new GroupNode("Outer")
        {
            Transform = new Transform(new Vector3(10, 20, 5), new EulerAngles(0, 0, 90).ToQuaternion(), new Vector3(2, 2, 2)),
        };
        var inner = new GroupNode("Inner") { Transform = Transform.FromPosition(new Vector3(3, -1, 2)) };
        var box = new PrimitiveNode("box", "Box")
        {
            Transform = new Transform(new Vector3(1, 2, 3), new EulerAngles(30, 10, 20).ToQuaternion(), Vector3.One),
        };
        history.Execute(new AddNodeCommand(doc, doc.Root, outer));
        history.Execute(new AddNodeCommand(doc, outer, inner));
        history.Execute(new AddNodeCommand(doc, inner, box));
        return (doc, box);
    }

    [Fact]
    public void Top_level_node_moves_exactly_by_the_world_delta()
    {
        var doc = new FormwerkDocument();
        var node = new PrimitiveNode("box", "B") { Transform = Transform.FromPosition(new Vector3(1, 2, 3)) };
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));

        var moved = TransformOps.MovedBy(node, node.Transform, new Vector3(10, 0, -4));

        AssertNear(new Vector3(11, 2, -1), moved.Position);
    }

    [Fact]
    public void Node_inside_a_rotated_scaled_group_moves_by_the_world_delta()
    {
        var (_, box) = NestedBox();
        var before = TransformOps.WorldPosition(box);

        var moved = TransformOps.MovedBy(box, box.Transform, new Vector3(7, -3, 1));
        box.Transform = moved;

        AssertNear(before + new Vector3(7, -3, 1), TransformOps.WorldPosition(box));
    }

    [Fact]
    public void Moving_does_not_change_rotation_or_scale()
    {
        var (_, box) = NestedBox();
        var moved = TransformOps.MovedBy(box, box.Transform, new Vector3(5, 5, 5));
        Assert.Equal(box.Transform.Rotation, moved.Rotation);
        Assert.Equal(box.Transform.Scale, moved.Scale);
    }

    [Fact]
    public void Rotation_about_the_world_pivot_moves_the_origin_along_a_circle()
    {
        var doc = new FormwerkDocument();
        var node = new PrimitiveNode("box", "B") { Transform = Transform.FromPosition(new Vector3(10, 0, 0)) };
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));

        node.Transform = TransformOps.RotatedAbout(node, node.Transform, Vector3.Zero, Vector3.UnitZ, 90);

        AssertNear(new Vector3(0, 10, 0), node.Transform.Position);
        AssertNear(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, node.Transform.Rotation));
    }

    // The pivot itself must not move; every point of the object turns as a rigid body around it.
    [Theory]
    [InlineData(90, 0, 0, 1)]
    [InlineData(45, 1, 1, 0)]
    [InlineData(-30, 0, 1, 0)]
    [InlineData(170, 1, 2, 3)]
    public void Rotation_inside_nested_groups_turns_the_whole_object_rigidly_about_the_pivot(double degrees, float ax, float ay, float az)
    {
        var (_, box) = NestedBox();
        var pivot = new Vector3(4, 1, 6);
        var axis = Vector3.Normalize(new Vector3(ax == 0 && ay == 0 && az == 0 ? 1 : ax, ay, az));
        var probe = new Vector3(0.5f, -2, 1.5f); // some point in the box's local space

        var worldBefore = Vector3.Transform(probe, TransformOps.LocalToWorld(box));
        var expected = pivot + Vector3.Transform(worldBefore - pivot, Quaternion.CreateFromAxisAngle(axis, (float)(degrees * Math.PI / 180)));

        box.Transform = TransformOps.RotatedAbout(box, box.Transform, pivot, axis, degrees);

        AssertNear(expected, Vector3.Transform(probe, TransformOps.LocalToWorld(box)), digits: 2);
    }

    [Fact]
    public void Rotating_a_node_around_its_own_world_position_keeps_the_position()
    {
        var (_, box) = NestedBox();
        var position = TransformOps.WorldPosition(box);

        box.Transform = TransformOps.RotatedAbout(box, box.Transform, position, Vector3.UnitZ, 33);

        AssertNear(position, TransformOps.WorldPosition(box), digits: 2);
    }

    [Fact]
    public void Rotation_does_not_change_the_scale()
    {
        var (_, box) = NestedBox();
        var rotated = TransformOps.RotatedAbout(box, box.Transform, Vector3.Zero, Vector3.UnitX, 20);
        Assert.Equal(box.Transform.Scale, rotated.Scale);
    }

    [Fact]
    public void Zero_axis_or_zero_angle_changes_nothing_harmful()
    {
        var (_, box) = NestedBox();
        Assert.Equal(box.Transform, TransformOps.RotatedAbout(box, box.Transform, Vector3.Zero, Vector3.Zero, 45));
        var same = TransformOps.RotatedAbout(box, box.Transform, Vector3.Zero, Vector3.UnitZ, 0);
        AssertNear(box.Transform.Position, same.Position, digits: 3);
    }

    [Fact]
    public void Start_transform_is_used_not_the_current_one()
    {
        var doc = new FormwerkDocument();
        var node = new PrimitiveNode("box", "B") { Transform = Transform.FromPosition(new Vector3(1, 0, 0)) };
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));
        var start = node.Transform;

        // Simulate several drag updates: the result only depends on the start and the total delta.
        node.Transform = TransformOps.MovedBy(node, start, new Vector3(2, 0, 0));
        node.Transform = TransformOps.MovedBy(node, start, new Vector3(5, 0, 0));

        AssertNear(new Vector3(6, 0, 0), node.Transform.Position);
    }

    [Fact]
    public void Several_nodes_change_in_one_undo_step()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var a = new PrimitiveNode("box", "A");
        var b = new PrimitiveNode("box", "B") { Transform = Transform.FromPosition(new Vector3(5, 0, 0)) };
        history.Execute(new AddNodeCommand(doc, doc.Root, a));
        history.Execute(new AddNodeCommand(doc, doc.Root, b));
        var revision = doc.Revision;

        var command = new SetTransformsCommand(doc,
            [(a, a.Transform, Transform.FromPosition(new Vector3(1, 1, 1))), (b, b.Transform, Transform.FromPosition(new Vector3(9, 9, 9)))],
            "Move 2 objects");
        Assert.True(command.HasEffect);
        history.Execute(command);

        Assert.Equal(new Vector3(1, 1, 1), a.Transform.Position);
        Assert.Equal(new Vector3(9, 9, 9), b.Transform.Position);
        Assert.Equal("Move 2 objects", history.Entries[^1].Name);
        Assert.Equal(revision + 1, doc.Revision);

        history.Undo();
        Assert.Equal(Vector3.Zero, a.Transform.Position);
        Assert.Equal(new Vector3(5, 0, 0), b.Transform.Position);
        Assert.Equal(3, history.Entries.Count); // two adds + the undone move (can be redone)
        Assert.Equal(2, history.CurrentIndex);
    }

    [Fact]
    public void Frame_of_a_top_level_node_is_its_own_position_rotation_and_scale()
    {
        var doc = new FormwerkDocument();
        var node = new PrimitiveNode("box", "B")
        {
            Transform = new Transform(new Vector3(5, 6, 7), new EulerAngles(0, 0, 90).ToQuaternion(), new Vector3(2, 3, 4)),
        };
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));

        var frame = TransformOps.FrameOf(node);

        AssertNear(new Vector3(5, 6, 7), frame.Origin);
        AssertNear(new Vector3(2, 3, 4), frame.Unit);
        AssertNear(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, frame.Rotation));
    }

    [Fact]
    public void Frame_of_a_nested_node_includes_the_parents()
    {
        var (_, box) = NestedBox();
        var frame = TransformOps.FrameOf(box);

        AssertNear(TransformOps.WorldPosition(box), frame.Origin);
        // outer group scales everything by 2
        Assert.Equal(2f, frame.Unit.X, 3);
        Assert.Equal(2f, frame.Unit.Y, 3);
        Assert.Equal(2f, frame.Unit.Z, 3);
    }

    [Fact]
    public void Frame_converts_between_world_and_frame_coordinates_both_ways()
    {
        var (_, box) = NestedBox();
        var frame = TransformOps.FrameOf(box);
        var world = new Vector3(13, -4, 9);

        AssertNear(world, frame.ToWorld(frame.ToFrame(world)));
        AssertNear(Vector3.Zero, frame.ToFrame(frame.Origin));
        // one step along the frame's X axis in world space is one unit along X in frame coordinates
        var step = frame.ToFrame(frame.Origin + Vector3.Transform(Vector3.UnitX, frame.Rotation));
        AssertNear(Vector3.UnitX, step);
    }

    [Fact]
    public void Composite_command_is_one_history_entry_and_undoes_in_reverse_order()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = new PrimitiveNode("box", "B") { Parameters = { ["width"] = "10" } };
        history.Execute(new AddNodeCommand(doc, doc.Root, node));
        var entries = history.Entries.Count;

        history.Execute(new CompositeCommand("Scale B",
        [
            new SetParameterCommand(doc, node, "width", "20"),
            new SetParameterCommand(doc, node, "width", "30"),
            new SetTransformsCommand(doc, [(node, node.Transform, Transform.FromPosition(new Vector3(4, 0, 0)))], "x"),
        ]));

        Assert.Equal(entries + 1, history.Entries.Count);
        Assert.Equal("Scale B", history.Entries[^1].Name);
        Assert.Equal("30", node.Parameters["width"]);
        Assert.Equal(4, node.Transform.Position.X);

        history.Undo();
        Assert.Equal("10", node.Parameters["width"]); // 30 -> 20 -> 10: reverse order
        Assert.Equal(0, node.Transform.Position.X);

        history.Redo();
        Assert.Equal("30", node.Parameters["width"]);
    }

    [Fact]
    public void Command_without_any_change_reports_no_effect()
    {
        var doc = new FormwerkDocument();
        var node = new PrimitiveNode("box", "A");
        var command = new SetTransformsCommand(doc, [(node, node.Transform, node.Transform)], "x");
        Assert.False(command.HasEffect);
    }
}
