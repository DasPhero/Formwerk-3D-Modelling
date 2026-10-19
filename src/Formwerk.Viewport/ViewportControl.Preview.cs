using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>Conversions for the drag preview.</summary>
public static class PreviewMath
{
    /// <summary>
    /// The same transformation as a WPF matrix. Both use the row-vector convention (a point is multiplied from the left,
    /// the translation sits in the last row), so the numbers are copied one to one.
    /// </summary>
    public static Matrix3D ToMatrix3D(Matrix4x4 m) => new(
        m.M11, m.M12, m.M13, m.M14,
        m.M21, m.M22, m.M23, m.M24,
        m.M31, m.M32, m.M33, m.M34,
        m.M41, m.M42, m.M43, m.M44);
}

/// <summary>
/// Moving objects while a handle is dragged (spec D-03, ADR 0005): the meshes that are already in the view are moved on screen by
/// a matrix instead of rebuilding the whole scene for every mouse move, so dragging stays smooth for big meshes.
/// </summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty PreviewTransformsProperty = DependencyProperty.Register(
        nameof(PreviewTransforms), typeof(IReadOnlyDictionary<Guid, Matrix4x4>), typeof(ViewportControl),
        new PropertyMetadata(null, (d, _) => ((ViewportControl)d).ApplyPreview()));

    // The edge lines of every object are a model of their own, so that they move with the object without being rebuilt.
    private readonly Dictionary<Guid, List<GeometryModel3D>> _edgeModels = [];

    /// <summary>
    /// For objects that are being dragged: the node id and the matrix that moves the object from where the items show it (world
    /// coordinates) to where it is now. Null or missing ids mean "stays where it is".
    /// </summary>
    public IReadOnlyDictionary<Guid, Matrix4x4>? PreviewTransforms
    {
        get => (IReadOnlyDictionary<Guid, Matrix4x4>?)GetValue(PreviewTransformsProperty);
        set => SetValue(PreviewTransformsProperty, value);
    }

    private void ApplyPreview()
    {
        var preview = PreviewTransforms;
        foreach (var info in _models.Values) info.Model.Transform = PreviewTransformFor(preview, info.Item.NodeId);
        foreach (var (id, models) in _edgeModels)
        {
            var transform = PreviewTransformFor(preview, id);
            foreach (var model in models) model.Transform = transform;
        }
    }

    private static Transform3D PreviewTransformFor(IReadOnlyDictionary<Guid, Matrix4x4>? preview, Guid nodeId) =>
        preview is not null && preview.TryGetValue(nodeId, out var matrix)
            ? new MatrixTransform3D(PreviewMath.ToMatrix3D(matrix))
            : Transform3D.Identity;
}
