using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>A node's coordinate frame in world space (see <see cref="TransformOps.FrameOf"/>).</summary>
public readonly record struct NodeFrame(Vector3 Origin, Quaternion Rotation, Vector3 Unit)
{
    /// <summary>World point to frame coordinates (in world millimetres along the frame axes).</summary>
    public Vector3 ToFrame(Vector3 world) => Vector3.Transform(world - Origin, Quaternion.Inverse(Rotation));

    /// <summary>Frame coordinates (world millimetres) to world point.</summary>
    public Vector3 ToWorld(Vector3 frame) => Origin + Vector3.Transform(frame, Rotation);
}

/// <summary>
/// Moves and rotates nodes in world space. A node's own <see cref="Transform"/> is relative to its parent, so a node
/// inside a rotated or moved group needs its parents' transforms taken into account. The functions take the node's
/// start transform explicitly so that they can be called repeatedly during a drag.
/// Exact for uniform parent scale; a non-uniform parent scale with rotation cannot be stored as position/rotation/scale.
/// </summary>
public static class TransformOps
{
    /// <summary>Matrix that maps the node's parent space to world space (identity for children of the root).</summary>
    public static Matrix4x4 ParentToWorld(Node node)
    {
        var m = Matrix4x4.Identity;
        for (var parent = node.Parent; parent is not null; parent = parent.Parent) m *= parent.Transform.ToMatrix();
        return m;
    }

    public static Matrix4x4 LocalToWorld(Node node) => node.Transform.ToMatrix() * ParentToWorld(node);

    /// <summary>World position of the node's origin.</summary>
    public static Vector3 WorldPosition(Node node) => Vector3.Transform(Vector3.Zero, LocalToWorld(node));

    /// <summary>
    /// The node's own coordinate frame in world space: its origin, its orientation and how many world millimetres one
    /// unit along each frame axis is (the scale of the node and of its parents). Used for oriented bounding boxes.
    /// </summary>
    public static NodeFrame FrameOf(Node node)
    {
        var matrix = LocalToWorld(node);
        var origin = Vector3.Transform(Vector3.Zero, matrix);
        if (!Matrix4x4.Decompose(matrix, out var scale, out var rotation, out _))
            return new NodeFrame(origin, Quaternion.Identity, Vector3.One);
        return new NodeFrame(origin, Quaternion.Normalize(rotation), Vector3.Abs(scale));
    }

    /// <summary>
    /// "Face on floor" (F-ROT-04): the node's transform after turning it so that the face at <paramref name="worldPoint"/>, whose
    /// outward direction is <paramref name="worldNormal"/>, looks straight down, and moving it straight up or down so that the point
    /// (and with it the whole flat face) is at Z = 0. The clicked point stays where it was on the plate. A face that already looks
    /// down only needs the move; a face that looks up is turned over. Without a direction nothing changes.
    /// </summary>
    public static Transform LaidOnFloor(Node node, Transform start, Vector3 worldPoint, Vector3 worldNormal)
    {
        if (worldNormal.LengthSquared() < 1e-12f) return start;

        var normal = Vector3.Normalize(worldNormal);
        var down = -Vector3.UnitZ;
        var cosine = Math.Clamp(Vector3.Dot(normal, down), -1f, 1f);
        var turned = start;
        if (cosine < 0.9999999f)
        {
            var axis = Vector3.Cross(normal, down);
            if (axis.LengthSquared() < 1e-12f) axis = Vector3.UnitX; // exactly up: any horizontal axis turns it over
            turned = RotatedAbout(node, start, worldPoint, axis, Math.Acos(cosine) * 180 / Math.PI);
        }

        // the turn leaves the point where it was, so only its height is left to fix
        return Math.Abs(worldPoint.Z) < 1e-6f ? turned : MovedBy(node, turned, new Vector3(0, 0, -worldPoint.Z));
    }

    /// <summary>
    /// "Face to face" (F-SNP-03): the node's transform after putting its face at <paramref name="worldPoint"/> (outward direction
    /// <paramref name="worldNormal"/>) onto the face of another object at <paramref name="targetPoint"/> (outward direction
    /// <paramref name="targetNormal"/>). The node is turned about the clicked point until its face looks against the target face (a
    /// face that looks the other way already needs no turn) and moved so that the clicked point lies on the target point, in front
    /// of the target face by <paramref name="gap"/> mm along its normal: 0 is flush, a positive gap leaves a space (0.2 for a print
    /// fit), a negative gap overlaps. Without a direction nothing changes.
    /// </summary>
    public static Transform FaceToFace(Node node, Transform start, Vector3 worldPoint, Vector3 worldNormal, Vector3 targetPoint, Vector3 targetNormal, float gap)
    {
        if (worldNormal.LengthSquared() < 1e-12f || targetNormal.LengthSquared() < 1e-12f) return start;

        var normal = Vector3.Normalize(worldNormal);
        var target = Vector3.Normalize(targetNormal);
        var wanted = -target; // the face has to look against the target face
        var cosine = Math.Clamp(Vector3.Dot(normal, wanted), -1f, 1f);
        var turned = start;
        if (cosine < 0.9999999f)
        {
            var axis = Vector3.Cross(normal, wanted);
            if (axis.LengthSquared() < 1e-12f) // exactly the other way round: any axis across the normal turns it over
                axis = Vector3.Cross(normal, MathF.Abs(normal.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX);
            turned = RotatedAbout(node, start, worldPoint, axis, Math.Acos(cosine) * 180 / Math.PI);
        }

        // the turn leaves the clicked point where it is: what is left is to bring it to the target point, plus the gap
        return MovedBy(node, turned, targetPoint + target * gap - worldPoint);
    }

    /// <summary>The node's transform after moving it by <paramref name="worldDelta"/>.</summary>
    public static Transform MovedBy(Node node, Transform start, Vector3 worldDelta)
    {
        if (!Matrix4x4.Invert(ParentToWorld(node), out var worldToParent)) return start;
        return start with { Position = start.Position + Vector3.TransformNormal(worldDelta, worldToParent) };
    }

    /// <summary>
    /// The node's transform after rotating it by <paramref name="degrees"/> around the world axis
    /// <paramref name="worldAxis"/> through <paramref name="worldPivot"/>. The node turns in place around the pivot.
    /// </summary>
    public static Transform RotatedAbout(Node node, Transform start, Vector3 worldPivot, Vector3 worldAxis, double degrees)
    {
        var parentToWorld = ParentToWorld(node);
        if (!Matrix4x4.Invert(parentToWorld, out var worldToParent)) return start;
        if (!Matrix4x4.Decompose(parentToWorld, out _, out var parentRotation, out _)) return start;
        if (worldAxis.LengthSquared() < 1e-12f) return start;

        var delta = Quaternion.CreateFromAxisAngle(Vector3.Normalize(worldAxis), (float)(degrees * Math.PI / 180));

        var worldPosition = Vector3.Transform(start.Position, parentToWorld);
        var newWorldPosition = worldPivot + Vector3.Transform(worldPosition - worldPivot, delta);

        var worldRotation = parentRotation * start.Rotation;
        var newLocalRotation = Quaternion.Normalize(Quaternion.Inverse(parentRotation) * delta * worldRotation);

        return start with { Position = Vector3.Transform(newWorldPosition, worldToParent), Rotation = newLocalRotation };
    }
}
