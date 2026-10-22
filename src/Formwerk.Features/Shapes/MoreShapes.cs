using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

// The nine basic shapes after box, cylinder and sphere (F-LIB-01). Like those, every shape has its origin in the center of its
// bottom, rests on the plate (Z = 0), points its triangles outward and is a closed mesh. Combinations of values that would make no
// sense (a tube wall thicker than the tube, a capsule shorter than it is wide) are corrected instead of reported, so that dragging
// a scale handle never shows an error.

/// <summary>Half a sphere standing on its flat side.</summary>
public sealed class HemisphereGenerator : ShapeGenerator
{
    public override string Id => "hemisphere";

    public override string DisplayName => "Hemisphere";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
        new("rings", "Rings", 16, 2, 256, IsInteger: true),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("diameter", ScaleAxes.X | ScaleAxes.Y | ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var r = (float)p["diameter"] / 2;
        var n = (int)p["segments"];
        var rings = (int)p["rings"];
        if (p["edgeSize"] > 0) return EdgeMeshes.Hemisphere(r, n, rings, (float)p["edgeSize"], (EdgeKind)(int)p["edgeType"]);

        // Layout: [center of the base][ring 0 = the rim of the base .. ring rings-1][pole]
        var v = new List<Vector3>(2 + rings * n) { Vector3.Zero };
        for (var ring = 0; ring < rings; ring++)
        {
            var latitude = MathF.PI / 2 * ring / rings;
            var (sinLat, cosLat) = MathF.SinCos(latitude);
            for (var k = 0; k < n; k++)
            {
                var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
                v.Add(new Vector3(r * cosLat * c, r * cosLat * s, r * sinLat));
            }
        }

        var pole = v.Count;
        v.Add(new Vector3(0, 0, r));

        int Ring(int ring, int k) => 1 + ring * n + k % n;

        var indices = new List<int>();
        for (var k = 0; k < n; k++)
        {
            indices.AddRange([0, Ring(0, k + 1), Ring(0, k)]); // flat base, facing down
            indices.AddRange([pole, Ring(rings - 1, k), Ring(rings - 1, k + 1)]);
            for (var ring = 0; ring < rings - 1; ring++)
            {
                indices.AddRange([Ring(ring, k), Ring(ring, k + 1), Ring(ring + 1, k + 1)]);
                indices.AddRange([Ring(ring, k), Ring(ring + 1, k + 1), Ring(ring + 1, k)]);
            }
        }

        return new MeshData([.. v], [.. indices]);
    }
}

/// <summary>A cone standing on its base, the tip straight above the center.</summary>
public sealed class ConeGenerator : ShapeGenerator
{
    public override string Id => "cone";

    public override string DisplayName => "Cone";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("diameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p) =>
        p["edgeSize"] > 0
            ? EdgeMeshes.Cone((float)p["diameter"] / 2, (float)p["height"], (int)p["segments"], (float)p["edgeSize"], (EdgeKind)(int)p["edgeType"])
            : ShapeMesh.Stump((float)p["diameter"] / 2, 0, (float)p["height"], (int)p["segments"]);
}

/// <summary>A cone with its tip cut off: different diameters at the bottom and at the top.</summary>
public sealed class FrustumGenerator : ShapeGenerator
{
    public override string Id => "frustum";

    public override string DisplayName => "Frustum";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("bottomDiameter", "Bottom diameter", 20, 0.01, 10000),
        new("topDiameter", "Top diameter", 10, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
        new("edgeWhere", "Edge position", 0, 0, 2, IsInteger: true), // 0 all, 1 top, 2 bottom
    ];

    // Both diameters belong to the plan (X and Y): the scale handles change them together.
    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("bottomDiameter", ScaleAxes.X | ScaleAxes.Y),
        new("topDiameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p) =>
        p["edgeSize"] > 0
            ? EdgeMeshes.Frustum(
                (float)p["bottomDiameter"] / 2, (float)p["topDiameter"] / 2, (float)p["height"], (int)p["segments"], (float)p["edgeSize"],
                (EdgeKind)(int)p["edgeType"], (EdgeSide)(int)p["edgeWhere"])
            : ShapeMesh.Stump((float)p["bottomDiameter"] / 2, (float)p["topDiameter"] / 2, (float)p["height"], (int)p["segments"]);
}

/// <summary>A pyramid with a rectangular base and the tip straight above its center.</summary>
public sealed class PyramidGenerator : ShapeGenerator
{
    public override string Id => "pyramid";

    public override string DisplayName => "Pyramid";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("width", "Width", 20, 0.01, 10000),
        new("depth", "Depth", 20, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("width", ScaleAxes.X),
        new("depth", ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        float hx = (float)p["width"] / 2, hy = (float)p["depth"] / 2, h = (float)p["height"];
        Vector3[] v = [new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, hy, 0), new(-hx, hy, 0), new(0, 0, h)];
        int[] i =
        [
            0, 2, 1, 0, 3, 2, // base
            0, 1, 4,          // front
            1, 2, 4,          // right
            2, 3, 4,          // back
            3, 0, 4,          // left
        ];
        return new MeshData(v, i);
    }
}

/// <summary>A prism with a regular polygon as its base: three corners (triangle) up to 64 (almost a cylinder).</summary>
public sealed class PrismGenerator : ShapeGenerator
{
    public override string Id => "prism";

    public override string DisplayName => "Prism";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
        new("sides", "Sides", 6, 3, 64, IsInteger: true),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
        new("edgeWhere", "Edge position", 0, 0, 2, IsInteger: true), // 0 all, 1 top, 2 bottom
    ];

    // The diameter is that of the circle through the corners; the extents in X and Y follow from it (a fixed ratio for a given number of sides).
    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("diameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var r = (float)p["diameter"] / 2;
        if (p["edgeSize"] > 0)
            return EdgeMeshes.Prism(r, (float)p["height"], (int)p["sides"], (float)p["edgeSize"], (EdgeKind)(int)p["edgeType"], (EdgeSide)(int)p["edgeWhere"]);
        return ShapeMesh.Stump(r, r, (float)p["height"], (int)p["sides"]);
    }
}

/// <summary>A ramp: half a box, rising towards the back (+Y) where it is as high as the box.</summary>
public sealed class WedgeGenerator : ShapeGenerator
{
    public override string Id => "wedge";

    public override string DisplayName => "Wedge";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("width", "Width", 20, 0.01, 10000),
        new("depth", "Depth", 20, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
        new("edgeWhere", "Edge position", 0, 0, 2, IsInteger: true), // 0 all, 1 top, 2 bottom
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("width", ScaleAxes.X),
        new("depth", ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        float hx = (float)p["width"] / 2, hy = (float)p["depth"] / 2, h = (float)p["height"];
        if (p["edgeSize"] > 0)
            return EdgeMeshes.Wedge((float)p["width"], (float)p["depth"], h, (float)p["edgeSize"], (EdgeKind)(int)p["edgeType"], (EdgeSide)(int)p["edgeWhere"]);
        Vector3[] v =
        [
            new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, hy, 0), new(-hx, hy, 0), // 0..3 base
            new(hx, hy, h), new(-hx, hy, h),                                    // 4, 5 top edge at the back
        ];
        int[] i =
        [
            0, 2, 1, 0, 3, 2, // base
            2, 3, 5, 2, 5, 4, // back, vertical
            0, 1, 4, 0, 4, 5, // slope
            1, 2, 4,          // right side
            3, 0, 5,          // left side
        ];
        return new MeshData(v, i);
    }
}

/// <summary>A doughnut lying flat: outer diameter of the whole ring and diameter of its tube.</summary>
public sealed class TorusGenerator : ShapeGenerator
{
    public override string Id => "torus";

    public override string DisplayName => "Torus";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("outerDiameter", "Outer diameter", 40, 0.01, 10000),
        new("tubeDiameter", "Tube diameter", 10, 0.01, 10000),
        new("segments", "Segments around", 64, 3, 512, IsInteger: true),
        new("sides", "Segments of the tube", 24, 3, 128, IsInteger: true),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("outerDiameter", ScaleAxes.X | ScaleAxes.Y),
        new("tubeDiameter", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var outer = (float)p["outerDiameter"];
        var tube = MathF.Min((float)p["tubeDiameter"], outer * 0.98f); // a tube wider than the ring would swallow the hole
        var major = (outer - tube) / 2; // distance of the tube center from the axis
        var minor = tube / 2;
        var segments = (int)p["segments"];
        var sides = (int)p["sides"];

        var v = new Vector3[segments * sides];
        for (var i = 0; i < segments; i++)
        {
            var (su, cu) = MathF.SinCos(2 * MathF.PI * i / segments);
            for (var j = 0; j < sides; j++)
            {
                var (sv, cv) = MathF.SinCos(2 * MathF.PI * j / sides);
                var radius = major + minor * cv;
                v[i * sides + j] = new Vector3(radius * cu, radius * su, minor + minor * sv);
            }
        }

        var indices = new List<int>(segments * sides * 6);
        for (var i = 0; i < segments; i++)
        {
            var next = (i + 1) % segments;
            for (var j = 0; j < sides; j++)
            {
                var nextSide = (j + 1) % sides;
                int a = i * sides + j, b = next * sides + j, c = next * sides + nextSide, d = i * sides + nextSide;
                indices.AddRange([a, b, c, a, c, d]);
            }
        }

        return new MeshData(v, [.. indices]);
    }
}

/// <summary>A pipe: a cylinder with a hole all the way through. The wall thickness is a detail that the scale handles leave alone.</summary>
public sealed class TubeGenerator : ShapeGenerator
{
    public override string Id => "tube";

    public override string DisplayName => "Tube";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("outerDiameter", "Outer diameter", 20, 0.01, 10000),
        new("wall", "Wall", 2, 0.01, 5000, IsDetail: true),
        new("height", "Height", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
        new("edgeSize", "Edge size", 0, 0, 10000, IsDetail: true),
        new("edgeType", "Edge type", 1, 0, 1, IsInteger: true),    // 0 chamfer, 1 fillet (F-EDG-01)
        new("edgeWhere", "Edge position", 0, 0, 2, IsInteger: true), // 0 all, 1 top, 2 bottom
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("outerDiameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var ro = (float)p["outerDiameter"] / 2;
        var ri = ro - MathF.Min((float)p["wall"], ro * 0.98f); // a wall of at least the whole radius would close the hole
        var h = (float)p["height"];
        var n = (int)p["segments"];
        if (p["edgeSize"] > 0)
            return EdgeMeshes.Tube(ro, ri, h, n, (float)p["edgeSize"], (EdgeKind)(int)p["edgeType"], (EdgeSide)(int)p["edgeWhere"]);

        // Layout: outer bottom [0,n), outer top [n,2n), inner bottom [2n,3n), inner top [3n,4n)
        var v = new Vector3[4 * n];
        for (var k = 0; k < n; k++)
        {
            var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
            v[k] = new Vector3(ro * c, ro * s, 0);
            v[n + k] = new Vector3(ro * c, ro * s, h);
            v[2 * n + k] = new Vector3(ri * c, ri * s, 0);
            v[3 * n + k] = new Vector3(ri * c, ri * s, h);
        }

        var indices = new List<int>(n * 24);
        for (var k = 0; k < n; k++)
        {
            var j = (k + 1) % n;
            indices.AddRange([k, j, n + j, k, n + j, n + k]);                                       // outside
            indices.AddRange([2 * n + k, 3 * n + j, 2 * n + j, 2 * n + k, 3 * n + k, 3 * n + j]);   // inside, facing the hole
            indices.AddRange([n + k, n + j, 3 * n + j, n + k, 3 * n + j, 3 * n + k]);               // top ring
            indices.AddRange([k, 2 * n + k, 2 * n + j, k, 2 * n + j, j]);                           // bottom ring
        }

        return new MeshData(v, [.. indices]);
    }
}

/// <summary>A cylinder with a half sphere on both ends. <c>height</c> is the whole length; it cannot be less than the diameter.</summary>
public sealed class CapsuleGenerator : ShapeGenerator
{
    public override string Id => "capsule";

    public override string DisplayName => "Capsule";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("height", "Height", 40, 0.01, 10000),
        new("segments", "Segments", 48, 3, 512, IsInteger: true),
        new("rings", "Rings per end", 12, 1, 128, IsInteger: true),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("diameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var r = (float)p["diameter"] / 2;
        var straight = MathF.Max((float)p["height"] - 2 * r, 0); // shorter than wide: just a sphere
        var n = (int)p["segments"];
        var rings = (int)p["rings"];

        // Rings from the bottom to the top; the poles are single vertices.
        var ringVertices = new List<Vector3[]>();
        Vector3[] MakeRing(float radius, float z)
        {
            var ring = new Vector3[n];
            for (var k = 0; k < n; k++)
            {
                var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
                ring[k] = new Vector3(radius * c, radius * s, z);
            }

            return ring;
        }

        for (var i = 1; i <= rings; i++)
        {
            var latitude = -MathF.PI / 2 + MathF.PI / 2 * i / rings;
            ringVertices.Add(MakeRing(r * MathF.Cos(latitude), r + r * MathF.Sin(latitude))); // the last of them is the lower equator
        }

        if (straight > 1e-6f) ringVertices.Add(MakeRing(r, r + straight)); // the upper equator
        var top = r + straight;
        for (var i = 1; i < rings; i++)
        {
            var latitude = MathF.PI / 2 * i / rings;
            ringVertices.Add(MakeRing(r * MathF.Cos(latitude), top + r * MathF.Sin(latitude)));
        }

        var v = new List<Vector3> { Vector3.Zero };
        foreach (var ring in ringVertices) v.AddRange(ring);
        var north = v.Count;
        v.Add(new Vector3(0, 0, top + r));

        int At(int ring, int k) => 1 + ring * n + k % n;

        var indices = new List<int>();
        var last = ringVertices.Count - 1;
        for (var k = 0; k < n; k++)
        {
            indices.AddRange([0, At(0, k + 1), At(0, k)]);
            indices.AddRange([north, At(last, k), At(last, k + 1)]);
            for (var ring = 0; ring < last; ring++)
            {
                indices.AddRange([At(ring, k), At(ring, k + 1), At(ring + 1, k + 1)]);
                indices.AddRange([At(ring, k), At(ring + 1, k + 1), At(ring + 1, k)]);
            }
        }

        return new MeshData([.. v], [.. indices]);
    }
}

/// <summary>Shared building blocks of the shapes above.</summary>
internal static class ShapeMesh
{
    /// <summary>
    /// A round (or regular polygon) solid between a bottom circle and a top circle of the given radii, with flat caps.
    /// A top radius of 0 gives a cone (one tip instead of a cap). Same vertex layout as the cylinder.
    /// </summary>
    public static MeshData Stump(float bottomRadius, float topRadius, float height, int sides)
    {
        var n = sides;
        if (topRadius <= 0)
        {
            var cone = new Vector3[n + 2];
            for (var k = 0; k < n; k++)
            {
                var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
                cone[k] = new Vector3(bottomRadius * c, bottomRadius * s, 0);
            }

            var apex = n;
            var center = n + 1;
            cone[apex] = new Vector3(0, 0, height);
            cone[center] = Vector3.Zero;
            var coneIndices = new List<int>(n * 6);
            for (var k = 0; k < n; k++)
            {
                var j = (k + 1) % n;
                coneIndices.AddRange([k, j, apex]);   // side
                coneIndices.AddRange([center, j, k]); // base
            }

            return new MeshData(cone, [.. coneIndices]);
        }

        var v = new Vector3[2 * n + 2];
        for (var k = 0; k < n; k++)
        {
            var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
            v[k] = new Vector3(bottomRadius * c, bottomRadius * s, 0);
            v[n + k] = new Vector3(topRadius * c, topRadius * s, height);
        }

        var bottomCenter = 2 * n;
        var topCenter = 2 * n + 1;
        v[bottomCenter] = Vector3.Zero;
        v[topCenter] = new Vector3(0, 0, height);

        var indices = new List<int>(n * 12);
        for (var k = 0; k < n; k++)
        {
            var j = (k + 1) % n;
            indices.AddRange([k, j, n + j, k, n + j, n + k]); // side
            indices.AddRange([topCenter, n + k, n + j]);      // top cap
            indices.AddRange([bottomCenter, j, k]);           // bottom cap
        }

        return new MeshData(v, [.. indices]);
    }
}
