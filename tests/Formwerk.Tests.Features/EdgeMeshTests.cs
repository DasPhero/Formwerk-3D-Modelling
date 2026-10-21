using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>Chamfer and fillet as parameters of boxes and cylinders (F-EDG-01).</summary>
public class EdgeMeshTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box(double w, double d, double h, double size, EdgeKind kind, EdgeSide side) =>
        new BoxGenerator().Generate(P(("width", w), ("depth", d), ("height", h), ("edgeSize", size), ("edgeType", (int)kind), ("edgeWhere", (int)side)));

    private static MeshData Cylinder(double diameter, double h, double size, EdgeKind kind, EdgeSide side, int segments = 64) =>
        new CylinderGenerator().Generate(P(("diameter", diameter), ("height", h), ("segments", segments), ("edgeSize", size), ("edgeType", (int)kind), ("edgeWhere", (int)side)));

    private static bool Near(Vector3 a, Vector3 b, float tolerance = 1e-3f) => Vector3.Distance(a, b) < tolerance;

    private static bool HasVertex(MeshData mesh, Vector3 point) => mesh.Vertices.ToArray().Any(v => Near(v, point));

    /// <summary>The volume of a box (w x d x h) whose top is <c>z = min(fx(x), fy(y))</c>, by summing a fine grid (independent of the mesh code).</summary>
    private static double TopOnlyVolume(double w, double d, double h, double s, EdgeKind kind)
    {
        double Drop(double coordinate, double half)
        {
            var e = Math.Max(0, Math.Abs(coordinate) - (half - s));
            return kind == EdgeKind.Chamfer ? e : s - Math.Sqrt(Math.Max(0, s * s - e * e));
        }

        const int n = 1200;
        double sum = 0;
        for (var i = 0; i < n; i++)
        {
            var x = -w / 2 + (i + 0.5) * w / n;
            for (var j = 0; j < n; j++)
            {
                var y = -d / 2 + (j + 0.5) * d / n;
                sum += h - Math.Max(Drop(x, w / 2), Drop(y, d / 2));
            }
        }

        return sum * (w / n) * (d / n);
    }

    // ---- no treatment ---------------------------------------------------------------------------------

    [Fact]
    public void Size_zero_gives_exactly_the_plain_shape()
    {
        foreach (IShapeGenerator generator in new ShapeGenerator[] { new BoxGenerator(), new CylinderGenerator() })
        {
            var plain = generator.Generate(P());
            var zero = generator.Generate(P(("edgeSize", 0), ("edgeType", 0), ("edgeWhere", 2)));

            Assert.Equal(plain.Vertices.ToArray(), zero.Vertices.ToArray());
            Assert.Equal(plain.Indices.ToArray(), zero.Indices.ToArray());
        }
    }

    [Fact]
    public void The_edge_parameters_have_sensible_defaults_and_do_not_set_an_extent()
    {
        foreach (var generator in new ShapeGenerator[] { new BoxGenerator(), new CylinderGenerator() })
        {
            var size = generator.Parameters.Single(p => p.Name == "edgeSize");
            Assert.Equal(0, size.Default);
            Assert.True(size.IsDetail); // scaling keeps it (F-SCL-05)
            Assert.DoesNotContain(generator.ScaleBindings, b => b.Parameter == "edgeSize");
            Assert.True(generator.Parameters.Single(p => p.Name == "edgeType").IsInteger);
            Assert.True(generator.Parameters.Single(p => p.Name == "edgeWhere").IsInteger);
        }
    }

    // ---- box, all edges, fillet -----------------------------------------------------------------------

    [Fact]
    public void A_box_with_all_edges_rounded_has_the_volume_of_a_ball_swept_box()
    {
        const double w = 20, d = 30, h = 10, r = 2;
        var mesh = Box(w, d, h, r, EdgeKind.Fillet, EdgeSide.All);

        double a = w - 2 * r, b = d - 2 * r, c = h - 2 * r;
        var expected = a * b * c + 2 * r * (a * b + b * c + c * a) + Math.PI * r * r * (a + b + c) + 4.0 / 3 * Math.PI * r * r * r;

        Assert.True(mesh.IsWatertight());
        Assert.Equal(expected, mesh.ComputeVolume(), expected * 0.003);
    }

    [Fact]
    public void Rounding_all_edges_keeps_the_outer_size_and_the_floor()
    {
        var mesh = Box(20, 20, 10, 2, EdgeKind.Fillet, EdgeSide.All);

        var bounds = mesh.ComputeBounds();
        Assert.True(Near(new Vector3(-10, -10, 0), bounds.Min));
        Assert.True(Near(new Vector3(10, 10, 10), bounds.Max));
    }

    [Fact]
    public void Every_edge_of_a_rounded_box_has_the_radius_that_was_asked_for()
    {
        const float w = 20, d = 20, h = 10, r = 2;
        var vertices = Box(w, d, h, r, EdgeKind.Fillet, EdgeSide.All).Vertices.ToArray();
        const float hx = w / 2, hy = d / 2;
        const float eps = 1e-3f;

        foreach (var sx in new[] { -1f, 1f })
        {
            foreach (var sz in new[] { 0f, 1f }) // bottom and top
            {
                var cz = sz == 0 ? r : h - r;
                // edge along Y: points in the middle of the edge that are on the curved part
                var along = vertices.Where(v => MathF.Abs(v.Y) <= hy - r + eps && sx * v.X > hx - r + eps && (sz == 0 ? v.Z < r - eps : v.Z > h - r + eps)).ToList();
                Assert.NotEmpty(along);
                Assert.All(along, v => Assert.Equal(r, MathF.Sqrt((v.X - sx * (hx - r)) * (v.X - sx * (hx - r)) + (v.Z - cz) * (v.Z - cz)), 1e-3));
            }
        }

        // the vertical edges
        foreach (var sx in new[] { -1f, 1f })
        {
            foreach (var sy in new[] { -1f, 1f })
            {
                var vertical = vertices.Where(v => v.Z >= r - eps && v.Z <= h - r + eps && sx * v.X > hx - r + eps && sy * v.Y > hy - r + eps).ToList();
                Assert.NotEmpty(vertical);
                Assert.All(vertical, v => Assert.Equal(r, MathF.Sqrt((v.X - sx * (hx - r)) * (v.X - sx * (hx - r)) + (v.Y - sy * (hy - r)) * (v.Y - sy * (hy - r))), 1e-3));
            }
        }
    }

    [Fact]
    public void The_corners_of_a_rounded_box_are_spherical()
    {
        const float r = 2;
        var vertices = Box(20, 20, 10, r, EdgeKind.Fillet, EdgeSide.All).Vertices.ToArray();
        var corner = vertices.Where(v => v.X > 10 - r + 1e-3f && v.Y > 10 - r + 1e-3f && v.Z > 10 - r + 1e-3f).ToList();

        Assert.NotEmpty(corner);
        Assert.All(corner, v => Assert.Equal(r, Vector3.Distance(v, new Vector3(8, 8, 8)), 1e-3));
    }

    [Fact]
    public void The_flat_faces_of_a_rounded_box_stay_flat()
    {
        var mesh = Box(20, 20, 10, 2, EdgeKind.Fillet, EdgeSide.All);

        Assert.True(HasVertex(mesh, new Vector3(8, 8, 10)));   // corner of the flat top
        Assert.True(HasVertex(mesh, new Vector3(-8, -8, 0)));  // corner of the flat bottom
        Assert.True(HasVertex(mesh, new Vector3(10, 8, 8)));   // corner of the flat side
    }

    // ---- box, all edges, chamfer ----------------------------------------------------------------------

    [Fact]
    public void A_box_with_all_edges_chamfered_has_the_volume_of_an_octahedron_swept_box()
    {
        const double w = 20, d = 30, h = 10, c = 2;
        var mesh = Box(w, d, h, c, EdgeKind.Chamfer, EdgeSide.All);

        double a = w - 2 * c, b = d - 2 * c, e = h - 2 * c;
        var expected = a * b * e + 2 * c * (a * b + b * e + e * a) + 2 * c * c * (a + b + e) + 4.0 / 3 * c * c * c;

        Assert.True(mesh.IsWatertight());
        Assert.Equal(expected, mesh.ComputeVolume(), expected * 1e-4);
    }

    [Fact]
    public void A_chamfer_cuts_the_same_distance_from_both_faces()
    {
        var mesh = Box(20, 20, 10, 2, EdgeKind.Chamfer, EdgeSide.All);

        Assert.True(HasVertex(mesh, new Vector3(8, 8, 10)));  // on the top face, 2 mm from the edge
        Assert.True(HasVertex(mesh, new Vector3(10, 8, 8)));  // on the side face, 2 mm from the edge
        Assert.True(Near(new Vector3(20, 20, 10), mesh.ComputeBounds().Size));
    }

    // ---- box, top or bottom edges only ----------------------------------------------------------------

    [Theory]
    [InlineData(EdgeKind.Fillet)]
    [InlineData(EdgeKind.Chamfer)]
    public void Rounding_only_the_top_edges_matches_the_height_field_volume(EdgeKind kind)
    {
        const double w = 20, d = 30, h = 10, s = 3;
        var mesh = Box(w, d, h, s, kind, EdgeSide.Top);

        var expected = TopOnlyVolume(w, d, h, s, kind);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(expected, mesh.ComputeVolume(), expected * 0.003);
    }

    [Theory]
    [InlineData(EdgeKind.Fillet)]
    [InlineData(EdgeKind.Chamfer)]
    public void Only_the_top_edges_are_treated_and_the_vertical_ones_stay_sharp(EdgeKind kind)
    {
        var mesh = Box(20, 30, 10, 3, kind, EdgeSide.Top);

        var bounds = mesh.ComputeBounds();
        Assert.True(Near(new Vector3(-10, -15, 0), bounds.Min));
        Assert.True(Near(new Vector3(10, 15, 10), bounds.Max));
        Assert.True(HasVertex(mesh, new Vector3(10, 15, 0)));   // sharp bottom corner
        Assert.True(HasVertex(mesh, new Vector3(10, 15, 7)));   // the vertical edge ends where the top treatment starts
        Assert.False(HasVertex(mesh, new Vector3(10, 15, 10))); // the top corner is cut away
        Assert.True(HasVertex(mesh, new Vector3(7, 12, 10)));   // the flat top is smaller
    }

    [Fact]
    public void The_two_roundings_of_a_top_corner_meet_in_a_mitre()
    {
        // mitre: for every vertex of the corner patch the drop below the top is the larger of the drops in X and Y
        const float s = 3;
        var vertices = Box(20, 20, 10, s, EdgeKind.Fillet, EdgeSide.Top).Vertices.ToArray();
        var patch = vertices.Where(v => v.X > 10 - s + 1e-3f && v.Y > 10 - s + 1e-3f && v.Z > 10 - s + 1e-3f && v.X < 10 - 1e-3f && v.Y < 10 - 1e-3f).ToList();

        Assert.NotEmpty(patch);
        Assert.All(patch, v =>
        {
            var ex = v.X - (10 - s);
            var ey = v.Y - (10 - s);
            var expected = 10 - (s - MathF.Sqrt(MathF.Max(0, s * s - MathF.Max(ex, ey) * MathF.Max(ex, ey))));
            Assert.Equal(expected, v.Z, 1e-3);
        });
    }

    [Theory]
    [InlineData(EdgeKind.Fillet)]
    [InlineData(EdgeKind.Chamfer)]
    public void Bottom_edges_are_the_mirror_image_of_top_edges(EdgeKind kind)
    {
        var top = Box(20, 30, 10, 3, kind, EdgeSide.Top);
        var bottom = Box(20, 30, 10, 3, kind, EdgeSide.Bottom);

        Assert.True(bottom.IsWatertight());
        Assert.True(bottom.ComputeVolume() > 0);
        Assert.Equal(top.ComputeVolume(), bottom.ComputeVolume(), 1e-3);
        Assert.Equal(0, bottom.ComputeBounds().Min.Z, 4);
        Assert.True(HasVertex(bottom, new Vector3(10, 15, 10))); // sharp top corner
        Assert.False(HasVertex(bottom, new Vector3(10, 15, 0)));
        Assert.True(HasVertex(bottom, new Vector3(7, 12, 0)));
    }

    // ---- sizes that are too big -----------------------------------------------------------------------

    [Theory]
    [InlineData(EdgeKind.Fillet, EdgeSide.All)]
    [InlineData(EdgeKind.Chamfer, EdgeSide.All)]
    [InlineData(EdgeKind.Fillet, EdgeSide.Top)]
    [InlineData(EdgeKind.Chamfer, EdgeSide.Top)]
    [InlineData(EdgeKind.Fillet, EdgeSide.Bottom)]
    [InlineData(EdgeKind.Chamfer, EdgeSide.Bottom)]
    public void A_size_that_is_too_big_is_cut_down_and_the_shape_stays_valid(EdgeKind kind, EdgeSide side)
    {
        foreach (var mesh in new[] { Box(20, 30, 10, 500, kind, side), Cylinder(20, 10, 500, kind, side), Box(20, 30, 10, 5, kind, side), Cylinder(20, 10, 5, kind, side) })
        {
            Assert.True(mesh.IsWatertight());
            Assert.True(mesh.ComputeVolume() > 0);
            Assert.Equal(0, mesh.ComputeBounds().Min.Z, 4);
            Assert.Equal(10, mesh.ComputeBounds().Max.Z, 4);
        }
    }

    [Fact]
    public void Every_combination_is_watertight_and_never_larger_than_the_plain_shape()
    {
        foreach (var kind in new[] { EdgeKind.Chamfer, EdgeKind.Fillet })
        {
            foreach (var side in new[] { EdgeSide.All, EdgeSide.Top, EdgeSide.Bottom })
            {
                foreach (var size in new[] { 0.1, 1, 4 })
                {
                    var box = Box(24, 18, 8, size, kind, side);
                    Assert.True(box.IsWatertight(), $"box {kind} {side} {size}");
                    Assert.InRange(box.ComputeVolume(), 1, 24 * 18 * 8);

                    var cylinder = Cylinder(24, 8, size, kind, side, 32);
                    Assert.True(cylinder.IsWatertight(), $"cylinder {kind} {side} {size}");
                    Assert.InRange(cylinder.ComputeVolume(), 1, Math.PI * 144 * 8);
                }
            }
        }
    }

    // ---- cylinder -------------------------------------------------------------------------------------

    /// <summary>The factor between the volume of a regular n-gon prism and that of the circle with the same corner radius.</summary>
    private static double PolygonFactor(int n) => n / (2 * Math.PI) * Math.Sin(2 * Math.PI / n);

    [Fact]
    public void A_chamfered_cylinder_loses_a_ring_with_a_triangle_cross_section()
    {
        const double diameter = 30, h = 12, c = 3;
        const int n = 96;
        var mesh = Cylinder(diameter, h, c, EdgeKind.Chamfer, EdgeSide.All, n);

        var plain = Math.PI * 225 * h;
        var cut = 2 * (2 * Math.PI * (15 - c / 3) * c * c / 2);
        Assert.True(mesh.IsWatertight());
        Assert.Equal((plain - cut) * PolygonFactor(n), mesh.ComputeVolume(), plain * 1e-4);
    }

    [Fact]
    public void A_rounded_cylinder_loses_a_ring_with_a_spandrel_cross_section()
    {
        const double diameter = 30, h = 12, s = 3;
        const int n = 96;
        var mesh = Cylinder(diameter, h, s, EdgeKind.Fillet, EdgeSide.All, n);

        var plain = Math.PI * 225 * h;
        var centroid = s * (10 - 3 * Math.PI) / (3 * (4 - Math.PI)); // of the piece between the corner and the arc, from each straight side
        var cut = 2 * (2 * Math.PI * (15 - centroid) * (1 - Math.PI / 4) * s * s);
        Assert.True(mesh.IsWatertight());
        Assert.Equal((plain - cut) * PolygonFactor(n), mesh.ComputeVolume(), plain * 0.003);
    }

    [Fact]
    public void Only_the_chosen_rim_of_a_cylinder_is_treated()
    {
        const int n = 64;
        var top = Cylinder(20, 10, 2, EdgeKind.Fillet, EdgeSide.Top, n).Vertices.ToArray();
        var bottom = Cylinder(20, 10, 2, EdgeKind.Chamfer, EdgeSide.Bottom, n).Vertices.ToArray();

        Assert.Contains(top, v => Near(v, new Vector3(10, 0, 0)));      // sharp bottom rim
        Assert.DoesNotContain(top, v => Near(v, new Vector3(10, 0, 10)));
        Assert.Contains(top, v => Near(v, new Vector3(8, 0, 10)));      // the flat top is smaller
        Assert.Contains(bottom, v => Near(v, new Vector3(10, 0, 10)));  // sharp top rim
        Assert.DoesNotContain(bottom, v => Near(v, new Vector3(10, 0, 0)));
        Assert.Contains(bottom, v => Near(v, new Vector3(8, 0, 0)));
    }

    [Fact]
    public void The_rim_of_a_rounded_cylinder_has_the_radius_that_was_asked_for()
    {
        const float s = 2;
        var vertices = Cylinder(20, 10, s, EdgeKind.Fillet, EdgeSide.All, 64).Vertices.ToArray();
        var rim = vertices.Where(v => MathF.Sqrt(v.X * v.X + v.Y * v.Y) > 10 - s + 1e-3f && v.Z > 10 - s + 1e-3f).ToList();

        Assert.NotEmpty(rim);
        Assert.All(rim, v => Assert.Equal(s, Vector2.Distance(new Vector2(MathF.Sqrt(v.X * v.X + v.Y * v.Y), v.Z), new Vector2(10 - s, 10 - s)), 1e-3));
    }

    [Fact]
    public void Rounding_a_cylinder_keeps_the_outer_size()
    {
        var bounds = Cylinder(20, 10, 2, EdgeKind.Fillet, EdgeSide.All, 64).ComputeBounds();

        Assert.Equal(20, bounds.Size.X, 3);
        Assert.Equal(10, bounds.Size.Z, 4);
    }

    // ---- the parameter range --------------------------------------------------------------------------

    [Fact]
    public void A_negative_size_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new BoxGenerator().Generate(P(("edgeSize", -1))));
        Assert.Throws<ArgumentException>(() => new CylinderGenerator().Generate(P(("edgeSize", -1))));
    }
}
