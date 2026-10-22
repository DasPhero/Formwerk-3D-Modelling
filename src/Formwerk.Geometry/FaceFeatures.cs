using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>What a snapped point of a face is: one of its corners, the middle of one of its edges, its center, or nothing (the click as it was).</summary>
public enum SnapKind
{
    None,
    Corner,
    EdgeMiddle,
    Center,
}

/// <summary>A point of a measurement and what it snapped to.</summary>
public readonly record struct SnappedPoint(Vector3 Point, SnapKind Kind);

/// <summary>
/// The points of a flat face that a measurement snaps to (E-07): the corners of its outline (also those around holes), the middles of
/// the edges of the outline and its center. The same idea as the snap points of the move tool.
/// </summary>
public static class FaceFeatures
{
    /// <summary>The reach of a snap: this part of the size of the face, but between the two limits (mm).</summary>
    private const float ReachPart = 0.12f, MinReach = 1f, MaxReach = 6f;

    /// <summary>
    /// The point of the face at <paramref name="point"/> (looking along <paramref name="normal"/>) that is nearest to the click, when it
    /// is within the reach; else the click itself. A click that is on no face stays as it is.
    /// </summary>
    public static SnappedPoint Snap(MeshData mesh, Vector3 point, Vector3 normal)
    {
        var face = FaceFinder.Find(mesh, point, normal);
        if (face is null) return new SnappedPoint(point, SnapKind.None);

        var best = Math.Clamp(ReachPart * face.Extent, MinReach, MaxReach);
        var result = new SnappedPoint(point, SnapKind.None);

        void Try(Vector3 candidate, SnapKind kind)
        {
            var distance = Vector3.Distance(candidate, point);
            if (distance > best) return;
            best = distance;
            result = new SnappedPoint(candidate, kind);
        }

        foreach (var (a, b) in face.Boundary())
        {
            Try(a, SnapKind.Corner);
            Try((a + b) / 2, SnapKind.EdgeMiddle);
        }

        Try(face.Center, SnapKind.Center);
        return result;
    }
}
