using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>Position (mm), rotation (quaternion) and scale of a node, relative to its parent.</summary>
public readonly record struct Transform(Vector3 Position, Quaternion Rotation, Vector3 Scale)
{
    public static Transform Identity { get; } = new(Vector3.Zero, Quaternion.Identity, Vector3.One);

    public static Transform FromPosition(Vector3 position) => Identity with { Position = position };

    /// <summary>Row-vector convention: scale, then rotate, then translate.</summary>
    public Matrix4x4 ToMatrix() =>
        Matrix4x4.CreateScale(Scale) * Matrix4x4.CreateFromQuaternion(Rotation) * Matrix4x4.CreateTranslation(Position);

    /// <summary>
    /// Transform of a child that is moved out of <paramref name="parent"/> without changing its look.
    /// Exact for uniform parent scale; a non-uniform parent scale combined with a rotated child cannot
    /// be expressed as position/rotation/scale and is approximated.
    /// </summary>
    public static Transform Compose(Transform parent, Transform child) => new(
        parent.Position + Vector3.Transform(parent.Scale * child.Position, parent.Rotation),
        Quaternion.Normalize(parent.Rotation * child.Rotation),
        parent.Scale * child.Scale);
}
