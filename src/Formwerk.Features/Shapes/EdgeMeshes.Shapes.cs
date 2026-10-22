using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>Chamfer and fillet for the frustum, cone, hemisphere, tube, prism and wedge (F-EDG-01), see <c>RevolvedEdges</c> and <c>ConvexSolid</c>.</summary>
public static partial class EdgeMeshes
{
    /// <summary>
    /// A hemisphere with the rim of its base treated (there is no other edge). The wall is a curve, so the cut is worked out on the
    /// sphere itself: a chamfer goes from the distance s inside the rim on the base to the point s along the sphere (an arc of length s);
    /// a fillet is the arc of the ball of radius r that touches the base and the sphere (its center is r above the base and R - r from the
    /// middle of the sphere). The rest of the rings stays as it is.
    /// </summary>
    public static MeshData Hemisphere(float radius, int segments, int rings, float size, EdgeKind kind)
    {
        var outline = new List<Vector2> { new(0, 0) };
        var cutEnd = 0f; // the latitude where the cut meets the sphere
        var limited = MathF.Min(size, 0.45f * radius);
        if (kind == EdgeKind.Chamfer)
        {
            cutEnd = limited / radius;
            outline.Add(new Vector2(radius - limited, 0));
            outline.Add(new Vector2(radius * MathF.Cos(cutEnd), radius * MathF.Sin(cutEnd)));
        }
        else
        {
            var across = MathF.Sqrt((radius - limited) * (radius - limited) - limited * limited); // the center of the ball, from the axis
            cutEnd = MathF.Atan2(limited, across);
            var sweep = cutEnd + MathF.PI / 2;
            var steps = Math.Max(2, (int)MathF.Ceiling(8 * sweep / (MathF.PI / 2)));
            for (var k = 0; k <= steps; k++)
            {
                var angle = -MathF.PI / 2 + sweep * k / steps;
                outline.Add(new Vector2(across + limited * MathF.Cos(angle), limited + limited * MathF.Sin(angle)));
            }
        }

        for (var ring = 0; ring < rings; ring++)
        {
            var latitude = MathF.PI / 2 * ring / rings;
            if (latitude > cutEnd + 1e-4f) outline.Add(new Vector2(radius * MathF.Cos(latitude), radius * MathF.Sin(latitude)));
        }

        outline.Add(new Vector2(0, radius));
        return RevolvedEdges.Revolve(outline, false, segments);
    }
    /// <summary>A cone with the rim of its base treated (the tip is no edge).</summary>
    public static MeshData Cone(float radius, float height, int segments, float size, EdgeKind kind) =>
        RevolvedEdges.Build([new(0, 0), new(radius, 0), new(0, height)], [false, true, false], false, size, kind, segments);

    /// <summary>A frustum with the rims at the bottom and the top treated as chosen.</summary>
    public static MeshData Frustum(float bottomRadius, float topRadius, float height, int segments, float size, EdgeKind kind, EdgeSide side) =>
        RevolvedEdges.Build(
            [new(0, 0), new(bottomRadius, 0), new(topRadius, height), new(0, height)],
            [false, side != EdgeSide.Top, side != EdgeSide.Bottom, false], false, size, kind, segments);

    /// <summary>A tube with the outer and the inner rim at the bottom, at the top or at both ends treated.</summary>
    public static MeshData Tube(float outerRadius, float innerRadius, float height, int segments, float size, EdgeKind kind, EdgeSide side)
    {
        bool bottom = side != EdgeSide.Top, top = side != EdgeSide.Bottom;
        return RevolvedEdges.Build(
            [new(innerRadius, 0), new(outerRadius, 0), new(outerRadius, height), new(innerRadius, height)],
            [bottom, bottom, top, top], true, size, kind, segments);
    }

    /// <summary>A regular prism: all edges (also the vertical ones), only the edges of the top face, or only those of the bottom face.</summary>
    public static MeshData Prism(float radius, float height, int sides, float size, EdgeKind kind, EdgeSide side)
    {
        var bottom = new Vector3[sides];
        var top = new Vector3[sides];
        for (var k = 0; k < sides; k++)
        {
            var (sin, cos) = MathF.SinCos(2 * MathF.PI * k / sides);
            bottom[k] = new Vector3(radius * cos, radius * sin, 0);
            top[k] = new Vector3(radius * cos, radius * sin, height);
        }

        var faces = new List<Vector3[]> { bottom.Reverse().ToArray(), top };
        for (var k = 0; k < sides; k++)
        {
            var j = (k + 1) % sides;
            faces.Add([bottom[k], bottom[j], top[j], top[k]]);
        }

        var solid = new ConvexSolid(faces);
        solid.Treat(edge => Selected(edge, side, height), size, kind);
        return solid.ToMesh();
    }

    /// <summary>A wedge: its nine edges, only the one at the back top, or only the edges of the base.</summary>
    public static MeshData Wedge(float width, float depth, float height, float size, EdgeKind kind, EdgeSide side)
    {
        float hx = width / 2, hy = depth / 2;
        Vector3[] v =
        [
            new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, hy, 0), new(-hx, hy, 0), // 0..3 base
            new(hx, hy, height), new(-hx, hy, height),                          // 4, 5 top edge at the back
        ];
        var solid = new ConvexSolid(
        [
            [v[0], v[3], v[2], v[1]], // base
            [v[2], v[3], v[5], v[4]], // back
            [v[0], v[1], v[4], v[5]], // slope
            [v[1], v[2], v[4]],       // right side
            [v[3], v[0], v[5]],       // left side
        ]);
        solid.Treat(edge => Selected(edge, side, height), size, kind);
        return solid.ToMesh();
    }

    /// <summary>All edges, or the ones that lie in the top plane (z = height), or in the bottom plane (z = 0).</summary>
    private static bool Selected(ConvexEdge edge, EdgeSide side, float height) =>
        side == EdgeSide.All
        || (side == EdgeSide.Top && MathF.Abs(edge.A.Z - height) < 1e-4f && MathF.Abs(edge.B.Z - height) < 1e-4f)
        || (side == EdgeSide.Bottom && MathF.Abs(edge.A.Z) < 1e-4f && MathF.Abs(edge.B.Z) < 1e-4f);
}
