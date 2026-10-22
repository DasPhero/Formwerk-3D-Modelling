using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.Features.Evaluation;

/// <summary>The two closed parts of a split: the one behind the plane (what the section view keeps) and the one in front of it.</summary>
public sealed record PlaneSplit(MeshData Behind, MeshData Front);

/// <summary>Splitting a closed mesh at a plane into two closed meshes (F-SPL-01).</summary>
public static class PlaneSplitter
{
    /// <summary>
    /// Both parts are the mesh intersected with a big slab on one side of the plane, so the cut surfaces are closed by the boolean kernel
    /// and the parts are exact and watertight. Null when the plane does not go through the mesh (it misses it or only touches it) or the
    /// kernel cannot work with the mesh (not a closed solid).
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static PlaneSplit? Split(MeshData mesh, SectionPlane plane, IGeometryKernel kernel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mesh.IsEmpty) return null;

        var bounds = mesh.ComputeBounds();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
        foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
        foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
        {
            var d = plane.Distance(new System.Numerics.Vector3(x, y, z));
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }

        if (max <= 1e-4f || min >= -1e-4f) return null;

        var reach = bounds.Size.Length() + 1;
        try
        {
            var behind = kernel.Boolean(BooleanOperation.Intersection, [mesh, SectionBuilder.Slab(plane, bounds, reach, 0)], cancellationToken);
            var front = kernel.Boolean(BooleanOperation.Intersection, [mesh, SectionBuilder.Slab(plane, bounds, 0, reach)], cancellationToken);
            return behind.IsEmpty || front.IsEmpty ? null : new PlaneSplit(behind, front);
        }
        catch (GeometryException)
        {
            return null;
        }
    }
}
