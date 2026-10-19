using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>"Face on floor" (F-ROT-04): the clicked face looks down and lies exactly on Z = 0.</summary>
public class LaidOnFloorTests
{
    private static void AssertNear(Vector3 expected, Vector3 actual, int digits = 3)
    {
        Assert.Equal(expected.X, actual.X, digits);
        Assert.Equal(expected.Y, actual.Y, digits);
        Assert.Equal(expected.Z, actual.Z, digits);
    }

    private static (FormwerkDocument Doc, Node Node) NodeAt(Transform transform, Transform? parentTransform = null)
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var node = new PrimitiveNode("box", "Box") { Transform = transform };
        if (parentTransform is { } parentValue)
        {
            var parent = new GroupNode("Parent") { Transform = parentValue };
            history.Execute(new AddNodeCommand(doc, doc.Root, parent));
            history.Execute(new AddNodeCommand(doc, parent, node));
        }
        else
        {
            history.Execute(new AddNodeCommand(doc, doc.Root, node));
        }

        return (doc, node);
    }

    /// <summary>Where a point of the node (given in world space for the start transform) is in world space for the new transform.</summary>
    private static Vector3 Moved(Node node, Transform start, Transform next, Vector3 worldPoint)
    {
        var parentToWorld = TransformOps.ParentToWorld(node);
        Matrix4x4.Invert(start.ToMatrix() * parentToWorld, out var worldToLocal);
        var local = Vector3.Transform(worldPoint, worldToLocal);
        return Vector3.Transform(local, next.ToMatrix() * parentToWorld);
    }

    private static Vector3 NormalAfter(Node node, Transform start, Transform next, Vector3 worldNormal)
    {
        var parentToWorld = TransformOps.ParentToWorld(node);
        Matrix4x4.Invert(start.ToMatrix() * parentToWorld, out var worldToLocal);
        var local = Vector3.TransformNormal(worldNormal, worldToLocal);
        return Vector3.Normalize(Vector3.TransformNormal(local, next.ToMatrix() * parentToWorld));
    }

    public static IEnumerable<object[]> Normals() =>
    [
        [new Vector3(1, 0, 0)],
        [new Vector3(-1, 0, 0)],
        [new Vector3(0, 1, 0)],
        [new Vector3(0, -1, 0)],
        [new Vector3(0, 0, 1)],     // looks up: turned over
        [new Vector3(0, 0, -1)],    // already looks down
        [new Vector3(1, 1, 1)],
        [new Vector3(-0.3f, 0.8f, -0.2f)],
        [new Vector3(0.001f, 0, 1)], // almost up
    ];

    [Theory]
    [MemberData(nameof(Normals))]
    public void The_face_looks_down_and_lies_on_the_floor_with_the_clicked_point_where_it_was(Vector3 normal)
    {
        var (_, node) = NodeAt(new Transform(new Vector3(10, 20, 5), new EulerAngles(10, 20, 30).ToQuaternion(), Vector3.One));
        var start = node.Transform;
        var point = new Vector3(12, 18, 9);

        var next = TransformOps.LaidOnFloor(node, start, point, normal);

        AssertNear(new Vector3(0, 0, -1), NormalAfter(node, start, next, normal), 4);
        var pointNow = Moved(node, start, next, point);
        Assert.Equal(12, pointNow.X, 3);
        Assert.Equal(18, pointNow.Y, 3);
        Assert.Equal(0, pointNow.Z, 4); // exactly on the plate
    }

    [Fact]
    public void A_face_that_already_looks_down_is_only_moved_to_the_floor()
    {
        var (_, node) = NodeAt(Transform.FromPosition(new Vector3(4, 5, 7)));
        var start = node.Transform;

        var next = TransformOps.LaidOnFloor(node, start, new Vector3(4, 5, 7), new Vector3(0, 0, -1));

        Assert.Equal(start.Rotation, next.Rotation);
        AssertNear(new Vector3(4, 5, 0), next.Position);
    }

    [Fact]
    public void A_face_that_already_lies_on_the_floor_changes_nothing()
    {
        var (_, node) = NodeAt(Transform.FromPosition(new Vector3(4, 5, 0)));
        var start = node.Transform;

        var next = TransformOps.LaidOnFloor(node, start, new Vector3(4, 5, 0), new Vector3(0, 0, -1));

        Assert.Equal(start, next);
    }

    [Fact]
    public void A_face_that_looks_up_is_turned_over()
    {
        var (_, node) = NodeAt(Transform.FromPosition(new Vector3(0, 0, 0)));
        var start = node.Transform;

        var next = TransformOps.LaidOnFloor(node, start, new Vector3(0, 0, 30), new Vector3(0, 0, 1)); // the top face of a 30 mm box

        AssertNear(new Vector3(0, 0, -1), NormalAfter(node, start, next, new Vector3(0, 0, 1)), 4);
        Assert.Equal(0, Moved(node, start, next, new Vector3(0, 0, 30)).Z, 4);
    }

    [Fact]
    public void Inside_a_moved_turned_and_scaled_parent_the_result_is_still_exact()
    {
        var parent = new Transform(new Vector3(30, -10, 4), new EulerAngles(15, 40, 75).ToQuaternion(), new Vector3(2, 2, 2));
        var (_, node) = NodeAt(new Transform(new Vector3(1, 2, 3), new EulerAngles(0, 20, 0).ToQuaternion(), Vector3.One), parent);
        var start = node.Transform;
        var point = new Vector3(33, -8, 9);
        var normal = new Vector3(0.4f, -0.5f, 0.7f);

        var next = TransformOps.LaidOnFloor(node, start, point, normal);

        AssertNear(new Vector3(0, 0, -1), NormalAfter(node, start, next, normal), 3);
        var pointNow = Moved(node, start, next, point);
        Assert.Equal(33, pointNow.X, 2);
        Assert.Equal(-8, pointNow.Y, 2);
        Assert.Equal(0, pointNow.Z, 3);
    }

    [Fact]
    public void A_scaled_node_keeps_its_scale()
    {
        var (_, node) = NodeAt(new Transform(new Vector3(0, 0, 0), Quaternion.Identity, new Vector3(2, 3, 4)));
        var start = node.Transform;

        var next = TransformOps.LaidOnFloor(node, start, new Vector3(5, 0, 10), new Vector3(1, 0, 0));

        Assert.Equal(new Vector3(2, 3, 4), next.Scale);
    }

    [Fact]
    public void A_normal_without_length_changes_nothing()
    {
        var (_, node) = NodeAt(Transform.FromPosition(new Vector3(1, 2, 3)));

        Assert.Equal(node.Transform, TransformOps.LaidOnFloor(node, node.Transform, new Vector3(1, 2, 3), Vector3.Zero));
    }

    [Fact]
    public void The_normal_does_not_need_to_be_a_unit_vector()
    {
        var (_, node) = NodeAt(Transform.Identity);
        var a = TransformOps.LaidOnFloor(node, node.Transform, new Vector3(1, 2, 3), new Vector3(1, 0, 0));
        var b = TransformOps.LaidOnFloor(node, node.Transform, new Vector3(1, 2, 3), new Vector3(25, 0, 0));

        AssertNear(a.Position, b.Position, 4);
        Assert.True(Math.Abs(Quaternion.Dot(a.Rotation, b.Rotation)) > 0.99999f);
    }
}
