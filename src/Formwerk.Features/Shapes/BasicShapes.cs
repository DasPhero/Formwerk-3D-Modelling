using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

public sealed class BoxGenerator : ShapeGenerator
{
    public override string Id => "box";

    public override string DisplayName => "Box";

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
        var hx = (float)p["width"] / 2;
        var hy = (float)p["depth"] / 2;
        var h = (float)p["height"];

        Vector3[] v =
        [
            new(-hx, -hy, 0), new(hx, -hy, 0), new(hx, hy, 0), new(-hx, hy, 0),
            new(-hx, -hy, h), new(hx, -hy, h), new(hx, hy, h), new(-hx, hy, h),
        ];
        int[] i =
        [
            0, 2, 1, 0, 3, 2, // bottom
            4, 5, 6, 4, 6, 7, // top
            0, 1, 5, 0, 5, 4, // front  (-Y)
            1, 2, 6, 1, 6, 5, // right  (+X)
            2, 3, 7, 2, 7, 6, // back   (+Y)
            3, 0, 4, 3, 4, 7, // left   (-X)
        ];
        return new MeshData(v, i);
    }
}

public sealed class CylinderGenerator : ShapeGenerator
{
    public override string Id => "cylinder";

    public override string DisplayName => "Cylinder";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("height", "Height", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
    ];

    public override IReadOnlyList<ScaleBinding> ScaleBindings { get; } =
    [
        new("diameter", ScaleAxes.X | ScaleAxes.Y),
        new("height", ScaleAxes.Z),
    ];

    protected override MeshData Build(IReadOnlyDictionary<string, double> p)
    {
        var r = (float)p["diameter"] / 2;
        var h = (float)p["height"];
        var n = (int)p["segments"];

        var v = new Vector3[2 * n + 2];
        for (var k = 0; k < n; k++)
        {
            var angle = 2 * MathF.PI * k / n;
            var (s, c) = MathF.SinCos(angle);
            v[k] = new Vector3(r * c, r * s, 0);
            v[n + k] = new Vector3(r * c, r * s, h);
        }

        var bottomCenter = 2 * n;
        var topCenter = 2 * n + 1;
        v[bottomCenter] = Vector3.Zero;
        v[topCenter] = new Vector3(0, 0, h);

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

public sealed class SphereGenerator : ShapeGenerator
{
    public override string Id => "sphere";

    public override string DisplayName => "Sphere";

    public override IReadOnlyList<ParameterDefinition> Parameters { get; } =
    [
        new("diameter", "Diameter", 20, 0.01, 10000),
        new("segments", "Segments", 64, 3, 512, IsInteger: true),
        new("rings", "Rings", 32, 2, 256, IsInteger: true),
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

        // Layout: [south pole][ring 1 .. ring rings-1][north pole]
        var v = new List<Vector3>(2 + (rings - 1) * n) { new(0, 0, 0) };
        for (var ring = 1; ring < rings; ring++)
        {
            var latitude = -MathF.PI / 2 + MathF.PI * ring / rings;
            var (sinLat, cosLat) = MathF.SinCos(latitude);
            for (var k = 0; k < n; k++)
            {
                var (s, c) = MathF.SinCos(2 * MathF.PI * k / n);
                v.Add(new Vector3(r * cosLat * c, r * cosLat * s, r + r * sinLat));
            }
        }

        var north = v.Count;
        v.Add(new Vector3(0, 0, 2 * r));

        int Ring(int ring, int k) => 1 + (ring - 1) * n + k % n;

        var indices = new List<int>();
        for (var k = 0; k < n; k++)
        {
            indices.AddRange([0, Ring(1, k + 1), Ring(1, k)]);
            indices.AddRange([north, Ring(rings - 1, k), Ring(rings - 1, k + 1)]);
            for (var ring = 1; ring < rings - 1; ring++)
            {
                indices.AddRange([Ring(ring, k), Ring(ring, k + 1), Ring(ring + 1, k + 1)]);
                indices.AddRange([Ring(ring, k), Ring(ring + 1, k + 1), Ring(ring + 1, k)]);
            }
        }

        return new MeshData([.. v], [.. indices]);
    }
}
