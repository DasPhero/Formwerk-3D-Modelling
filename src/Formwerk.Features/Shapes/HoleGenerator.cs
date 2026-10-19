using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>The four kinds of hole (F-HOL-02). The numbers are what the parameter <c>type</c> holds in the document.</summary>
public enum HoleType
{
    /// <summary>Straight, one diameter.</summary>
    Cylindrical = 0,

    /// <summary>Narrows from the diameter at the surface to the end diameter at the bottom.</summary>
    Conical = 1,

    /// <summary>A cone at the surface (for a countersunk screw head) above a straight hole.</summary>
    Countersink = 2,

    /// <summary>A wider cylinder at the surface (for a cylinder head) above a straight hole.</summary>
    Counterbore = 3,
}

/// <summary>
/// The cutter of a hole: a solid of revolution whose axis is +Z, whose origin lies on the surface that is drilled (Z = 0) and which
/// reaches <see cref="Overshoot"/> above the surface so that the cut is clean. It goes down to -<c>depth</c>. A hole is a
/// <c>PrimitiveNode</c> with this generator and the role "hole", so it is moved, saved and edited like any other shape.
/// </summary>
public sealed class HoleGenerator : ShapeGenerator
{
    /// <summary>How far the cutter reaches out of the surface in mm.</summary>
    public const float Overshoot = 1f;

    public override string Id => "hole";

    public override string DisplayName => "Hole";

    // The sizes of a hole do not set an extent in the sense of the scale handles (the origin is on the surface, not in the middle of
    // the bottom), so they are all details: they are changed in the properties and in the tool, not by dragging.
    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("type", "Type", 0, 0, 3, IsInteger: true),
        new("diameter", "Diameter", 3.4, 0.01, 1000, IsDetail: true),
        new("endDiameter", "End diameter", 1.7, 0, 1000, IsDetail: true),
        new("depth", "Depth", 10, 0.01, 10000, IsDetail: true),
        new("sinkDiameter", "Head diameter", 6.4, 0.01, 1000, IsDetail: true),
        new("sinkAngle", "Head angle", 90, 10, 170, IsDetail: true),
        new("sinkDepth", "Head depth", 3, 0.01, 1000, IsDetail: true),
        new("segments", "Segments", 48, 3, 256, IsInteger: true),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } = [];

    /// <summary>
    /// The outline of the cutter in the plane through the axis: (distance from the axis, Z) from the top on the axis down and around
    /// the outside to the bottom on the axis. The same outline draws the cross-section picture of the tool.
    /// </summary>
    public IReadOnlyList<(float R, float Z)> Profile(IReadOnlyDictionary<string, double> values) => ProfileOf(Resolve(values));

    protected override MeshData Build(IReadOnlyDictionary<string, double> p) => Revolve(ProfileOf(p), (int)p["segments"]);

    private static List<(float R, float Z)> ProfileOf(IReadOnlyDictionary<string, double> p)
    {
        var type = (HoleType)(int)p["type"];
        var radius = (float)p["diameter"] / 2;
        var depth = (float)p["depth"];
        var sinkRadius = (float)p["sinkDiameter"] / 2;
        const float e = Overshoot;

        List<(float R, float Z)> points;
        switch (type)
        {
            case HoleType.Conical:
            {
                var end = (float)p["endDiameter"] / 2;
                points = [(0, e), (radius, e), (radius, 0), (end, -depth), (0, -depth)];
                break;
            }

            case HoleType.Countersink when sinkRadius > radius:
            {
                var slope = MathF.Tan((float)(p["sinkAngle"] * Math.PI / 360)); // radius lost per mm of depth
                var coneDepth = (sinkRadius - radius) / slope;
                points = coneDepth >= depth
                    ? [(0, e), (sinkRadius, e), (sinkRadius, 0), (sinkRadius - depth * slope, -depth), (0, -depth)]
                    : [(0, e), (sinkRadius, e), (sinkRadius, 0), (radius, -coneDepth), (radius, -depth), (0, -depth)];
                break;
            }

            case HoleType.Counterbore when sinkRadius > radius:
            {
                var boreDepth = Math.Min((float)p["sinkDepth"], depth);
                points = boreDepth >= depth
                    ? [(0, e), (sinkRadius, e), (sinkRadius, -depth), (0, -depth)]
                    : [(0, e), (sinkRadius, e), (sinkRadius, -boreDepth), (radius, -boreDepth), (radius, -depth), (0, -depth)];
                break;
            }

            default:
                points = [(0, e), (radius, e), (radius, -depth), (0, -depth)];
                break;
        }

        // points that coincide (a conical hole that ends in a point, a cone exactly as deep as the hole) would make empty triangles
        var clean = new List<(float R, float Z)>(points.Count);
        foreach (var point in points)
        {
            if (clean.Count == 0 || Math.Abs(clean[^1].R - point.R) > 1e-6f || Math.Abs(clean[^1].Z - point.Z) > 1e-6f) clean.Add(point);
        }

        return clean;
    }

    /// <summary>
    /// Turns an outline that runs from the axis down the outside back to the axis around the Z axis. A point on the axis becomes one
    /// vertex, every other point a ring of <paramref name="sides"/> vertices.
    /// </summary>
    private static MeshData Revolve(List<(float R, float Z)> profile, int sides)
    {
        var vertices = new List<Vector3>();
        var first = new int[profile.Count]; // index of the first vertex of each outline point
        for (var i = 0; i < profile.Count; i++)
        {
            first[i] = vertices.Count;
            var (r, z) = profile[i];
            if (r <= 0)
            {
                vertices.Add(new Vector3(0, 0, z));
                continue;
            }

            for (var k = 0; k < sides; k++)
            {
                var (s, c) = MathF.SinCos(2 * MathF.PI * k / sides);
                vertices.Add(new Vector3(r * c, r * s, z));
            }
        }

        var indices = new List<int>();
        for (var i = 0; i + 1 < profile.Count; i++)
        {
            var upperOnAxis = profile[i].R <= 0;
            var lowerOnAxis = profile[i + 1].R <= 0;
            for (var k = 0; k < sides; k++)
            {
                var next = (k + 1) % sides;
                var a = first[i] + (upperOnAxis ? 0 : k);
                var b = first[i] + (upperOnAxis ? 0 : next);
                var c = first[i + 1] + (lowerOnAxis ? 0 : k);
                var d = first[i + 1] + (lowerOnAxis ? 0 : next);
                if (!upperOnAxis && !lowerOnAxis) indices.AddRange([a, c, b, b, c, d]);
                else if (upperOnAxis) indices.AddRange([a, c, d]);
                else indices.AddRange([a, c, b]);
            }
        }

        return new MeshData([.. vertices], [.. indices]);
    }
}
