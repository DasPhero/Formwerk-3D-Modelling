using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.Features.Evaluation;

/// <summary>One part as the section view shows it: the body cut at the plane, and the filled cut surface (null: none, or the kernel could not make it).</summary>
public sealed record SectionPart(ScenePart Source, MeshData Body, MeshData? Cap);

/// <summary>
/// The section view (E-06): cuts the parts of the scene at a plane and fills the cut surface. The model itself is not touched; the
/// result is only for drawing.
/// </summary>
public static class SectionBuilder
{
    /// <summary>The thickness in mm of the plate that is intersected with a mesh to get its cut surface.</summary>
    public const float CapThickness = 0.05f;

    /// <summary>
    /// The parts behind the plane. A part that is completely behind it is passed on as it is (same mesh), one that is completely in front
    /// of it is left out. A solid that the plane cuts gets a <see cref="SectionPart.Cap"/> when <paramref name="kernel"/> can make it;
    /// hole ghosts are cut but never closed.
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static IReadOnlyList<SectionPart> Build(IReadOnlyList<ScenePart> parts, SectionPlane plane, IGeometryKernel? kernel, CancellationToken cancellationToken)
    {
        var result = new List<SectionPart>(parts.Count);
        foreach (var part in parts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var body = MeshClipper.Clip(part.Mesh, plane);
            if (body.IsEmpty) continue;

            MeshData? cap = null;
            if (kernel is not null && part.Kind == PartKind.Solid && !ReferenceEquals(body, part.Mesh))
                cap = Cap(part.Mesh, plane, kernel, cancellationToken);
            result.Add(new SectionPart(part, body, cap is { IsEmpty: false } ? cap : null));
        }

        return result;
    }

    /// <summary>
    /// The surface where the plane cuts a closed mesh, as triangles in the plane that look to the front (away from the material). The
    /// mesh is intersected with a thin plate just behind the plane by the boolean kernel; the faces of the plate that lie in the plane are
    /// the cut surface, triangulated with its holes and islands. Null when the plane misses the mesh or the kernel cannot work with it
    /// (a mesh that is not a closed solid).
    /// </summary>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public static MeshData? Cap(MeshData mesh, SectionPlane plane, IGeometryKernel kernel, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (mesh.IsEmpty) return null;

        var bounds = mesh.ComputeBounds();
        float min = float.MaxValue, max = float.MinValue;
        foreach (var x in new[] { bounds.Min.X, bounds.Max.X })
        foreach (var y in new[] { bounds.Min.Y, bounds.Max.Y })
        foreach (var z in new[] { bounds.Min.Z, bounds.Max.Z })
        {
            var d = plane.Distance(new Vector3(x, y, z));
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }

        if (max <= 0 || min >= 0) return null; // the plane misses the mesh

        MeshData cut;
        try
        {
            cut = kernel.Boolean(BooleanOperation.Intersection, [mesh, Plate(plane, bounds)], cancellationToken);
        }
        catch (GeometryException)
        {
            return null;
        }

        var vertices = cut.Vertices.ToArray();
        var indices = cut.Indices.ToArray();
        var capVertices = new List<Vector3>();
        var capIndices = new List<int>();
        var map = new Dictionary<int, int>();
        for (var t = 0; t < indices.Length; t += 3)
        {
            Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
            if (MathF.Abs(plane.Distance(a)) > 1e-3f || MathF.Abs(plane.Distance(b)) > 1e-3f || MathF.Abs(plane.Distance(c)) > 1e-3f) continue;
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.LengthSquared() < 1e-18f || Vector3.Dot(Vector3.Normalize(normal), plane.Normal) < 0.9f) continue;

            for (var k = 0; k < 3; k++)
            {
                var old = indices[t + k];
                if (!map.TryGetValue(old, out var id))
                {
                    id = capVertices.Count;
                    capVertices.Add(vertices[old]);
                    map[old] = id;
                }

                capIndices.Add(id);
            }
        }

        return capIndices.Count == 0 ? null : new MeshData([.. capVertices], [.. capIndices]);
    }

    /// <summary>A flat box that is much bigger than the mesh across the plane and <see cref="CapThickness"/> thick, just behind the plane.</summary>
    private static MeshData Plate(SectionPlane plane, Bounds bounds)
    {
        var n = plane.Normal;
        var helper = MathF.Abs(n.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(n, helper));
        var v = Vector3.Cross(n, u); // (u, v, n) is right-handed, so the faces keep pointing outward
        var center = bounds.Center - n * plane.Distance(bounds.Center); // the point of the plane nearest to the middle of the mesh
        var half = bounds.Size.Length() + 1;

        Vector3 P(float sx, float sy, float t) => center + u * (sx * half) + v * (sy * half) + n * t;
        Vector3[] corners =
        [
            P(-1, -1, -CapThickness), P(1, -1, -CapThickness), P(1, 1, -CapThickness), P(-1, 1, -CapThickness),
            P(-1, -1, 0), P(1, -1, 0), P(1, 1, 0), P(-1, 1, 0),
        ];
        int[] triangles =
        [
            0, 2, 1, 0, 3, 2, // behind
            4, 5, 6, 4, 6, 7, // in the plane
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7,
        ];
        return new MeshData(corners, triangles);
    }
}
