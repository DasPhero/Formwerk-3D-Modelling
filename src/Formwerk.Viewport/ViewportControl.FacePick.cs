using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>A face the user clicked: the object it belongs to, the point on it and its outward direction, all in world space.</summary>
public sealed record FacePick(Guid NodeId, Vector3 Point, Vector3 Normal);

public static class FaceMath
{
    /// <summary>
    /// Outward normal of a triangle (counter-clockwise seen from outside, like all meshes of the program); zero for a triangle
    /// without area.
    /// </summary>
    public static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var length = cross.Length();
        return length < 1e-12f ? Vector3.Zero : cross / length;
    }
}

/// <summary>"Face on floor" (F-ROT-04): while the mode is on, a click reports the clicked face instead of selecting.</summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty FacePickModeProperty = DependencyProperty.Register(
        nameof(FacePickMode), typeof(bool), typeof(ViewportControl),
        new PropertyMetadata(false, (d, e) => ((ViewportControl)d).Cursor = (bool)e.NewValue ? Cursors.Cross : null));

    public static readonly DependencyProperty FacePickCommandProperty = DependencyProperty.Register(
        nameof(FacePickCommand), typeof(ICommand), typeof(ViewportControl));

    /// <summary>When true a click on an object does not select it but executes <see cref="FacePickCommand"/> with the face under the mouse.</summary>
    public bool FacePickMode
    {
        get => (bool)GetValue(FacePickModeProperty);
        set => SetValue(FacePickModeProperty, value);
    }

    /// <summary>Executed with a <see cref="FacePick"/>, or with null when the user pressed Esc.</summary>
    public ICommand? FacePickCommand
    {
        get => (ICommand?)GetValue(FacePickCommandProperty);
        set => SetValue(FacePickCommandProperty, value);
    }

    /// <summary>Reports the face of the nearest solid under the mouse; a click on nothing does nothing (the mode stays on).</summary>
    private void PickFace(Point position)
    {
        RayMeshGeometry3DHitTestResult? best = null;
        ModelInfo? bestInfo = null;
        System.Windows.Media.VisualTreeHelper.HitTest(_viewport, null, result =>
        {
            if (result is RayMeshGeometry3DHitTestResult ray && _models.TryGetValue(ray.ModelHit, out var info) && info.Item.Kind == ViewportItemKind.Solid
                && (best is null || ray.DistanceToRayOrigin < best.DistanceToRayOrigin))
            {
                best = ray;
                bestInfo = info;
            }

            return System.Windows.Media.HitTestResultBehavior.Continue;
        }, new System.Windows.Media.PointHitTestParameters(position));

        if (best is null || bestInfo is null) return;
        var positions = best.MeshHit.Positions;
        Vector3 At(int index) => new((float)positions[index].X, (float)positions[index].Y, (float)positions[index].Z);
        var normal = FaceMath.TriangleNormal(At(best.VertexIndex1), At(best.VertexIndex2), At(best.VertexIndex3));
        var point = new Vector3((float)best.PointHit.X, (float)best.PointHit.Y, (float)best.PointHit.Z);

        var pick = new FacePick(bestInfo.Item.NodeId, point, normal);
        if (FacePickCommand?.CanExecute(pick) == true) FacePickCommand.Execute(pick);
    }
}
