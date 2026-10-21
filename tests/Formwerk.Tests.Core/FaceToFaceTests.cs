using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>Putting one face flush onto another (F-SNP-03): the transform of the object that moves.</summary>
public class FaceToFaceTests
{
    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    private static PrimitiveNode Node(Vector3 position, Quaternion? rotation = null) =>
        new("box", "Box") { Transform = new Transform(position, rotation ?? Quaternion.Identity, Vector3.One) };

    /// <summary>World position of a point that is given in the space of the node.</summary>
    private static Vector3 World(Node node, Vector3 local) => Vector3.Transform(local, TransformOps.LocalToWorld(node));

    private static Vector3 LocalPointOf(Node node, Vector3 world) =>
        Matrix4x4.Invert(TransformOps.LocalToWorld(node), out var inverse) ? Vector3.Transform(world, inverse) : world;

    private static Vector3 WorldNormal(Node node, Vector3 local) => Vector3.Normalize(Vector3.TransformNormal(local, TransformOps.LocalToWorld(node)));

    [Fact]
    public void A_face_that_looks_the_other_way_is_only_moved_onto_the_target_face()
    {
        // A: the right face at X = 10 looks along +X. B: the left face at X = 90 looks along -X.
        var node = Node(Vector3.Zero);

        var next = TransformOps.FaceToFace(node, node.Transform, new Vector3(10, 0, 10), Vector3.UnitX, new Vector3(90, 0, 10), -Vector3.UnitX, 0);

        Assert.True(Near(next.Position, new Vector3(80, 0, 0)), next.Position.ToString());
        Assert.Equal(Quaternion.Identity, next.Rotation); // no turn needed
    }

    [Fact]
    public void The_clicked_point_lands_on_the_target_point_also_across_the_face()
    {
        var node = Node(Vector3.Zero);

        var next = TransformOps.FaceToFace(node, node.Transform, new Vector3(10, 3, 12), Vector3.UnitX, new Vector3(90, -5, 8), -Vector3.UnitX, 0);

        Assert.True(Near(next.Position, new Vector3(80, -8, -4)), next.Position.ToString()); // the point moved from (10, 3, 12) to (90, -5, 8)
    }

    [Theory]
    [InlineData(0.2f)]
    [InlineData(1.5f)]
    [InlineData(-1f)] // an overlap
    [InlineData(0f)]
    public void The_gap_is_kept_exactly_along_the_normal_of_the_target(float gap)
    {
        var node = Node(Vector3.Zero);

        var next = TransformOps.FaceToFace(node, node.Transform, new Vector3(10, 0, 10), Vector3.UnitX, new Vector3(90, 0, 10), -Vector3.UnitX, gap);

        // the right face of A is at X = 10 + shift; the target face is at X = 90; the distance is the gap
        var face = 10 + next.Position.X;
        Assert.Equal(90 - gap, face, 4);
    }

    [Fact]
    public void A_face_that_looks_up_is_turned_to_look_against_a_side_face()
    {
        // A: the top face (Z = 20) looks along +Z. B: a face at X = 50 looks along -X: A is turned so that its top looks along +X.
        var node = Node(Vector3.Zero);
        var topPoint = new Vector3(0, 0, 20);

        var next = TransformOps.FaceToFace(node, node.Transform, topPoint, Vector3.UnitZ, new Vector3(50, 0, 10), -Vector3.UnitX, 0);
        node.Transform = next;

        Assert.True(Near(WorldNormal(node, Vector3.UnitZ), Vector3.UnitX), WorldNormal(node, Vector3.UnitZ).ToString()); // the top looks along +X
        Assert.True(Near(World(node, topPoint), new Vector3(50, 0, 10)), World(node, topPoint).ToString()); // and lies on the target point
    }

    [Fact]
    public void The_turned_face_lies_exactly_in_the_target_plane_apart_by_the_gap()
    {
        var node = Node(new Vector3(5, 5, 0));
        var point = new Vector3(5, 5, 20);
        var targetNormal = Vector3.Normalize(new Vector3(0, -1, 1)); // a slope

        var local = LocalPointOf(node, point); // the clicked material point, in the space of the node
        node.Transform = TransformOps.FaceToFace(node, node.Transform, point, Vector3.UnitZ, new Vector3(0, 40, 10), targetNormal, 0.2f);

        var placed = World(node, local); // the clicked point in the world after the move
        var distance = Vector3.Dot(placed - new Vector3(0, 40, 10), targetNormal);
        Assert.Equal(0.2f, distance, 3); // exactly 0.2 mm in front of the slope
        Assert.True(Near(WorldNormal(node, Vector3.UnitZ), -targetNormal), WorldNormal(node, Vector3.UnitZ).ToString()); // face to face
    }

    [Fact]
    public void A_face_that_looks_the_same_way_as_the_target_is_turned_over()
    {
        var node = Node(Vector3.Zero);
        var point = new Vector3(10, 0, 10);

        node.Transform = TransformOps.FaceToFace(node, node.Transform, point, Vector3.UnitX, new Vector3(90, 0, 10), Vector3.UnitX, 0);

        Assert.True(Near(WorldNormal(node, Vector3.UnitX), -Vector3.UnitX), WorldNormal(node, Vector3.UnitX).ToString());
        Assert.True(Near(World(node, point), new Vector3(90, 0, 10)));
    }

    [Fact]
    public void A_face_that_looks_straight_down_works_too()
    {
        var node = Node(new Vector3(0, 0, 30));
        var point = new Vector3(0, 0, 30);
        var local = LocalPointOf(node, point);

        node.Transform = TransformOps.FaceToFace(node, node.Transform, point, -Vector3.UnitZ, new Vector3(0, 0, 10), Vector3.UnitZ, 0);

        Assert.True(Near(World(node, local), new Vector3(0, 0, 10))); // down onto the top of the other object
        Assert.True(Near(WorldNormal(node, -Vector3.UnitZ), -Vector3.UnitZ));
    }

    [Fact]
    public void An_object_inside_a_moved_and_turned_group_ends_up_in_the_right_place_in_the_world()
    {
        var doc = new FormwerkDocument();
        var group = new GroupNode("G") { Transform = new Transform(new Vector3(100, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One) };
        new AddNodeCommand(doc, doc.Root, group).Do();
        var node = Node(new Vector3(10, 0, 0));
        new AddNodeCommand(doc, group, node).Do();
        // in the world the node stands at (100, 10, 0); a face of it at the world point (100, 20, 5) looks along +Y
        var point = new Vector3(100, 20, 5);

        node.Transform = TransformOps.FaceToFace(node, node.Transform, point, Vector3.UnitY, new Vector3(100, 60, 5), -Vector3.UnitY, 0.2f);

        // the node moved by 40 - 0.2 = 39.8 along +Y in the world
        Assert.True(Near(TransformOps.WorldPosition(node), new Vector3(100, 10 + 39.8f, 0)), TransformOps.WorldPosition(node).ToString());
    }

    [Fact]
    public void Without_a_direction_nothing_changes()
    {
        var node = Node(new Vector3(1, 2, 3));

        Assert.Equal(node.Transform, TransformOps.FaceToFace(node, node.Transform, Vector3.Zero, Vector3.Zero, Vector3.One, Vector3.UnitX, 0));
        Assert.Equal(node.Transform, TransformOps.FaceToFace(node, node.Transform, Vector3.Zero, Vector3.UnitX, Vector3.One, Vector3.Zero, 0));
    }
}
