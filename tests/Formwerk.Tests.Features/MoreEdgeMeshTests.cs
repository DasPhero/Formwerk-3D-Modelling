using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>Chamfer and fillet on the frustum, cone, hemisphere, tube, prism and wedge (F-EDG-01).</summary>
public class MoreEdgeMeshTests
{
    private static readonly string[] AllShapes = ["frustum", "cone", "hemisphere", "tube", "prism", "wedge"];
    private static readonly string[] WithPosition = ["frustum", "tube", "prism", "wedge"];

    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Make(string id, double size, EdgeKind kind, EdgeSide side, params (string, double)[] more)
    {
        var values = more.ToList();
        values.Add(("edgeSize", size));
        values.Add(("edgeType", (int)kind));
        values.Add(("edgeWhere", (int)side));
        return ShapeLibrary.Find(id)!.Generate(P([.. values]));
    }

    /// <summary>The factor between the volume of a revolved n-gon ring and the true solid of revolution.</summary>
    private static double PolygonFactor(int n) => n / (2 * Math.PI) * Math.Sin(2 * Math.PI / n);

    /// <summary>Volume of a polygon (radius, height) turned around the axis, with n corners around: Pappus, for the n-gon rings the meshes use.</summary>
    private static double Revolved(IReadOnlyList<Vector2> polygon, int n)
    {
        double sum = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            sum += (a.X + b.X) * ((double)a.X * b.Y - (double)b.X * a.Y) / 6;
        }

        return Math.Abs(sum) * 2 * Math.PI * PolygonFactor(n);
    }

    /// <summary>The material that a chamfer of distance s takes away at a corner: the triangle between the corner and the points s along both edges.</summary>
    private static double ChamferRing(Vector2 corner, Vector2 towardsA, Vector2 towardsB, double s, int n)
    {
        var u1 = Vector2.Normalize(towardsA - corner);
        var u2 = Vector2.Normalize(towardsB - corner);
        return Revolved([corner, corner + (float)s * u1, corner + (float)s * u2], n);
    }

    // ---- all six -----------------------------------------------------------------------------------

    [Fact]
    public void Size_zero_gives_exactly_the_plain_shape()
    {
        foreach (var id in AllShapes)
        {
            var generator = ShapeLibrary.Find(id)!;
            var plain = generator.Generate(P());
            var zero = generator.Generate(P(("edgeSize", 0), ("edgeType", 0), ("edgeWhere", 2)));

            Assert.Equal(plain.Vertices.ToArray(), zero.Vertices.ToArray());
            Assert.Equal(plain.Indices.ToArray(), zero.Indices.ToArray());
        }
    }

    [Fact]
    public void The_edge_parameters_are_there_and_a_position_only_where_it_makes_sense()
    {
        foreach (var id in AllShapes)
        {
            var parameters = ShapeLibrary.Find(id)!.Parameters;
            var size = parameters.Single(p => p.Name == "edgeSize");
            Assert.Equal(0, size.Default);
            Assert.True(size.IsDetail);
            Assert.True(parameters.Single(p => p.Name == "edgeType").IsInteger);
            Assert.Equal(WithPosition.Contains(id), parameters.Any(p => p.Name == "edgeWhere"));
            Assert.DoesNotContain(ShapeLibrary.Find(id)!.ScaleBindings, b => b.Parameter == "edgeSize");
        }
    }

    [Fact]
    public void Every_shape_every_kind_and_every_position_is_watertight_and_never_bigger_than_the_plain_shape()
    {
        foreach (var id in AllShapes)
        {
            var plain = ShapeLibrary.Find(id)!.Generate(P());
            var plainBounds = plain.ComputeBounds();
            foreach (var kind in new[] { EdgeKind.Chamfer, EdgeKind.Fillet })
            {
                foreach (var side in new[] { EdgeSide.All, EdgeSide.Top, EdgeSide.Bottom })
                {
                    foreach (var size in new[] { 0.5, 2, 3.5 })
                    {
                        var mesh = Make(id, size, kind, side);
                        var label = $"{id} {kind} {side} {size}";
                        Assert.True(mesh.IsWatertight(), label);
                        Assert.InRange(mesh.ComputeVolume(), 1, plain.ComputeVolume() + 1e-3);
                        var bounds = mesh.ComputeBounds();
                        Assert.True(bounds.Min.X >= plainBounds.Min.X - 1e-3f && bounds.Max.X <= plainBounds.Max.X + 1e-3f, label);
                        Assert.True(bounds.Min.Y >= plainBounds.Min.Y - 1e-3f && bounds.Max.Y <= plainBounds.Max.Y + 1e-3f, label);
                        Assert.True(bounds.Min.Z >= plainBounds.Min.Z - 1e-3f && bounds.Max.Z <= plainBounds.Max.Z + 1e-3f, label);
                    }
                }
            }
        }
    }

    [Fact]
    public void A_size_that_is_far_too_big_is_cut_down_and_the_shape_stays_valid()
    {
        foreach (var id in AllShapes)
        {
            foreach (var kind in new[] { EdgeKind.Chamfer, EdgeKind.Fillet })
            {
                foreach (var side in new[] { EdgeSide.All, EdgeSide.Top, EdgeSide.Bottom })
                {
                    var mesh = Make(id, 5000, kind, side);

                    Assert.True(mesh.IsWatertight(), $"{id} {kind} {side}");
                    Assert.True(mesh.ComputeVolume() > 0, $"{id} {kind} {side}");
                }
            }
        }
    }

    [Fact]
    public void The_treatment_changes_the_shape()
    {
        foreach (var id in AllShapes)
        {
            var plain = ShapeLibrary.Find(id)!.Generate(P()).ComputeVolume();

            Assert.True(Make(id, 2, EdgeKind.Chamfer, EdgeSide.All).ComputeVolume() < plain - 1, id);
            Assert.True(Make(id, 2, EdgeKind.Fillet, EdgeSide.All).ComputeVolume() < plain - 1, id);
        }
    }

    // ---- tube ---------------------------------------------------------------------------------------

    [Fact]
    public void A_tube_with_chamfered_rims_loses_a_triangle_ring_at_each_corner()
    {
        const int n = 96;
        const double s = 0.8;
        var mesh = Make("tube", s, EdgeKind.Chamfer, EdgeSide.All, ("outerDiameter", 20), ("wall", 2), ("height", 20), ("segments", n));

        // the corners of the cross section (radius 8 to 10, height 0 to 20): all right angles
        var plain = Revolved([new(8, 0), new(10, 0), new(10, 20), new(8, 20)], n);
        var removed = ChamferRing(new(8, 0), new(10, 0), new(8, 20), s, n) + ChamferRing(new(10, 0), new(8, 0), new(10, 20), s, n)
            + ChamferRing(new(10, 20), new(10, 0), new(8, 20), s, n) + ChamferRing(new(8, 20), new(10, 20), new(8, 0), s, n);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(plain - removed, mesh.ComputeVolume(), plain * 1e-3);
    }

    [Theory]
    [InlineData(EdgeSide.Top)]
    [InlineData(EdgeSide.Bottom)]
    public void Only_the_chosen_end_of_a_tube_is_treated(EdgeSide side)
    {
        const int n = 96;
        const double s = 0.8;
        var mesh = Make("tube", s, EdgeKind.Chamfer, side, ("outerDiameter", 20), ("wall", 2), ("height", 20), ("segments", n));

        var plain = Revolved([new(8, 0), new(10, 0), new(10, 20), new(8, 20)], n);
        var removed = side == EdgeSide.Top
            ? ChamferRing(new(10, 20), new(10, 0), new(8, 20), s, n) + ChamferRing(new(8, 20), new(10, 20), new(8, 0), s, n)
            : ChamferRing(new(8, 0), new(10, 0), new(8, 20), s, n) + ChamferRing(new(10, 0), new(8, 0), new(10, 20), s, n);
        Assert.Equal(plain - removed, mesh.ComputeVolume(), plain * 1e-3);

        var vertices = mesh.Vertices.ToArray();
        var untreatedZ = side == EdgeSide.Top ? 0f : 20f;
        Assert.Contains(vertices, v => MathF.Abs(v.Z - untreatedZ) < 1e-3f && MathF.Abs(new Vector2(v.X, v.Y).Length() - 10) < 1e-3f); // the sharp outer rim
        Assert.Contains(vertices, v => MathF.Abs(v.Z - untreatedZ) < 1e-3f && MathF.Abs(new Vector2(v.X, v.Y).Length() - 8) < 1e-3f);  // the sharp inner rim
    }

    [Fact]
    public void A_tube_with_rounded_rims_loses_the_spandrel_at_each_corner()
    {
        const int n = 96;
        const double s = 0.8, d = 0.2234 * s; // the centroid of the piece between the corner and the arc is d from both straight sides
        var mesh = Make("tube", s, EdgeKind.Fillet, EdgeSide.All, ("outerDiameter", 20), ("wall", 2), ("height", 20), ("segments", n));

        var plain = Revolved([new(8, 0), new(10, 0), new(10, 20), new(8, 20)], n);
        var spandrel = (1 - Math.PI / 4) * s * s;
        var removed = spandrel * 2 * Math.PI * (2 * (10 - d) + 2 * (8 + d)) * PolygonFactor(n);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(plain - removed, mesh.ComputeVolume(), removed * 0.04);
    }

    [Fact]
    public void The_rims_of_a_tube_keep_the_outer_and_the_inner_size()
    {
        var bounds = Make("tube", 0.8, EdgeKind.Fillet, EdgeSide.All, ("outerDiameter", 20), ("wall", 2), ("height", 20)).ComputeBounds();

        Assert.Equal(20, bounds.Size.X, 1e-3);
        Assert.Equal(20, bounds.Size.Z, 1e-3);
    }

    // ---- frustum and cone -----------------------------------------------------------------------------

    [Fact]
    public void A_frustum_with_chamfered_rims_loses_a_triangle_ring_at_each_slanted_corner()
    {
        const int n = 96;
        const double s = 2;
        var mesh = Make("frustum", s, EdgeKind.Chamfer, EdgeSide.All, ("bottomDiameter", 20), ("topDiameter", 10), ("height", 20), ("segments", n));

        Vector2 bottom = new(10, 0), top = new(5, 20), axisBottom = new(0, 0), axisTop = new(0, 20);
        var plain = Revolved([axisBottom, bottom, top, axisTop], n);
        var removed = ChamferRing(bottom, axisBottom, top, s, n) + ChamferRing(top, bottom, axisTop, s, n);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(plain - removed, mesh.ComputeVolume(), plain * 1e-3);
    }

    [Fact]
    public void Only_the_chosen_rim_of_a_frustum_is_treated()
    {
        const int n = 96;
        const double s = 2;
        Vector2 bottom = new(10, 0), top = new(5, 20), axisBottom = new(0, 0), axisTop = new(0, 20);
        var plain = Revolved([axisBottom, bottom, top, axisTop], n);

        var upper = Make("frustum", s, EdgeKind.Chamfer, EdgeSide.Top, ("bottomDiameter", 20), ("topDiameter", 10), ("height", 20), ("segments", n));
        var lower = Make("frustum", s, EdgeKind.Chamfer, EdgeSide.Bottom, ("bottomDiameter", 20), ("topDiameter", 10), ("height", 20), ("segments", n));

        Assert.Equal(plain - ChamferRing(top, bottom, axisTop, s, n), upper.ComputeVolume(), plain * 1e-3);
        Assert.Equal(plain - ChamferRing(bottom, axisBottom, top, s, n), lower.ComputeVolume(), plain * 1e-3);
        Assert.True(lower.ComputeBounds().Max.X < 9.6f, "the widest part, the bottom rim, is cut away");
        Assert.Equal(10, upper.ComputeBounds().Max.X, 1e-3); // the bottom rim is untouched
    }

    [Fact]
    public void A_rounded_frustum_rim_is_an_arc_that_touches_both_faces()
    {
        const double r = 2;
        var mesh = Make("frustum", r, EdgeKind.Fillet, EdgeSide.Bottom, ("bottomDiameter", 20), ("topDiameter", 10), ("height", 20), ("segments", 96));

        // the corner of the base and the slanted wall: the angle between them inside the material
        var u2 = Vector2.Normalize(new Vector2(5 - 10, 20));
        var alpha = Math.Acos(Vector2.Dot(new Vector2(-1, 0), u2));
        var tangent = r / Math.Tan(alpha / 2);
        var vertices = mesh.Vertices.ToArray();

        // the arc starts on the base at the tangent distance from the corner (10, 0) and ends on the wall
        Assert.Contains(vertices, v => MathF.Abs(v.Z) < 1e-3f && MathF.Abs(new Vector2(v.X, v.Y).Length() - (10 - (float)tangent)) < 1e-2f);
        var onWall = new Vector2(10, 0) + (float)tangent * u2;
        Assert.Contains(vertices, v => MathF.Abs(v.Z - onWall.Y) < 1e-2f && MathF.Abs(new Vector2(v.X, v.Y).Length() - onWall.X) < 1e-2f);
    }

    [Fact]
    public void A_cone_has_only_its_base_rim_treated()
    {
        const int n = 96;
        const double s = 2;
        var mesh = Make("cone", s, EdgeKind.Chamfer, EdgeSide.All, ("diameter", 20), ("height", 20), ("segments", n));

        Vector2 rim = new(10, 0), axisBottom = new(0, 0), apex = new(0, 20);
        var plain = Revolved([axisBottom, rim, apex], n);
        var removed = ChamferRing(rim, axisBottom, apex, s, n);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(plain - removed, mesh.ComputeVolume(), plain * 1e-3);
        Assert.Equal(20, mesh.ComputeBounds().Max.Z, 1e-3); // the tip is not touched
    }

    // ---- hemisphere -----------------------------------------------------------------------------------

    [Fact]
    public void A_chamfered_hemisphere_loses_the_piece_between_the_base_the_sphere_and_the_chamfer()
    {
        const int n = 96, rings = 24;
        const double s = 1.5;
        var mesh = Make("hemisphere", s, EdgeKind.Chamfer, EdgeSide.All, ("diameter", 20), ("segments", n), ("rings", rings));

        var plain = ShapeLibrary.Find("hemisphere")!.Generate(P(("diameter", 20), ("rings", rings), ("segments", n))).ComputeVolume();
        var latitude = (float)(s / 10); // the chamfer ends s along the sphere
        Vector2 baseEnd = new(10 - (float)s, 0), rim = new(10, 0), sphereEnd = new(10 * MathF.Cos(latitude), 10 * MathF.Sin(latitude));
        var removed = Revolved([baseEnd, rim, sphereEnd], n);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(plain - removed, mesh.ComputeVolume(), plain * 1e-3);
        Assert.Equal(10, mesh.ComputeBounds().Max.Z, 1e-3); // the pole is not touched
    }

    [Fact]
    public void A_rounded_hemisphere_has_an_arc_that_touches_the_base_and_the_sphere()
    {
        const double r = 2;
        var mesh = Make("hemisphere", r, EdgeKind.Fillet, EdgeSide.All, ("diameter", 20), ("segments", 96), ("rings", 24));
        var vertices = mesh.Vertices.ToArray();

        // the ball of radius 2 that touches the base and the sphere of radius 10 has its center 2 above the base and 8 from the middle
        var across = (float)Math.Sqrt((10 - r) * (10 - r) - r * r);
        var center = new Vector2(across, (float)r);
        var onArc = vertices.Where(v => MathF.Abs(Vector2.Distance(new Vector2(new Vector2(v.X, v.Y).Length(), v.Z), center) - (float)r) < 1e-3f).ToList();

        Assert.True(onArc.Count >= 6 * 96, "the arc is turned all around");
        Assert.Contains(vertices, v => MathF.Abs(v.Z) < 1e-3f && MathF.Abs(new Vector2(v.X, v.Y).Length() - across) < 1e-3f); // touches the base
        Assert.Contains(vertices, v => v.Z > 0.1f && MathF.Abs(v.Length() - 10) < 1e-3f);                                    // and the sphere
        Assert.True(mesh.IsWatertight());
        Assert.Equal(10, mesh.ComputeBounds().Max.Z, 1e-3);
    }
    // ---- prism ----------------------------------------------------------------------------------------

    /// <summary>The corners of the regular n-gon with the given circumradius, like the meshes have them.</summary>
    private static Vector2[] Polygon(int sides, double radius) =>
        [.. Enumerable.Range(0, sides).Select(k => new Vector2((float)(radius * Math.Cos(2 * Math.PI * k / sides)), (float)(radius * Math.Sin(2 * Math.PI * k / sides))))];

    /// <summary>The volume of a prism whose top is <c>z = H - max drop(e_i)</c>, e_i = how far into the treated strip along side i (grid sum, independent of the mesh code).</summary>
    private static double TopTreatedPrism(int sides, double radius, double height, double s, EdgeKind kind)
    {
        var polygon = Polygon(sides, radius);
        var normals = new Vector2[sides];
        var offsets = new double[sides];
        for (var i = 0; i < sides; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % sides];
            var edge = b - a;
            normals[i] = Vector2.Normalize(new Vector2(edge.Y, -edge.X)); // outward for a counter-clockwise polygon
            offsets[i] = Vector2.Dot(normals[i], a);
        }

        double Drop(double e) => kind == EdgeKind.Chamfer ? e : s - Math.Sqrt(Math.Max(0, s * s - e * e));
        const int n = 1500;
        double sum = 0;
        var step = 2 * radius / n;
        for (var i = 0; i < n; i++)
        {
            var x = -radius + (i + 0.5) * step;
            for (var j = 0; j < n; j++)
            {
                var y = -radius + (j + 0.5) * step;
                double worst = 0;
                var inside = true;
                for (var k = 0; k < sides && inside; k++)
                {
                    var distance = offsets[k] - (normals[k].X * x + normals[k].Y * y); // inward distance from side k
                    if (distance < 0) inside = false;
                    else worst = Math.Max(worst, Drop(Math.Max(0, s - distance)));
                }

                if (inside) sum += height - worst;
            }
        }

        return sum * step * step;
    }

    [Theory]
    [InlineData(EdgeKind.Chamfer, 0.002)]
    [InlineData(EdgeKind.Fillet, 0.006)]
    public void The_top_edges_of_a_prism_make_a_height_field_with_mitred_corners(EdgeKind kind, double tolerance)
    {
        const double radius = 10, height = 20, s = 2;
        var mesh = Make("prism", s, kind, EdgeSide.Top, ("diameter", 20), ("height", height), ("sides", 6));

        var expected = TopTreatedPrism(6, radius, height, s, kind);

        Assert.True(mesh.IsWatertight());
        Assert.Equal(expected, mesh.ComputeVolume(), expected * tolerance);
    }

    [Theory]
    [InlineData(EdgeKind.Chamfer)]
    [InlineData(EdgeKind.Fillet)]
    public void The_bottom_edges_of_a_prism_are_the_mirror_image_of_the_top_ones(EdgeKind kind)
    {
        var top = Make("prism", 2, kind, EdgeSide.Top, ("height", 20));
        var bottom = Make("prism", 2, kind, EdgeSide.Bottom, ("height", 20));

        Assert.Equal(top.ComputeVolume(), bottom.ComputeVolume(), 1e-3);
        Assert.Equal(0, bottom.ComputeBounds().Min.Z, 1e-4);
    }

    [Fact]
    public void All_edges_of_a_prism_include_the_vertical_ones()
    {
        const double s = 2;
        var plain = ShapeLibrary.Find("prism")!.Generate(P());
        var top = Make("prism", s, EdgeKind.Chamfer, EdgeSide.Top);
        var all = Make("prism", s, EdgeKind.Chamfer, EdgeSide.All);

        Assert.True(all.ComputeVolume() < top.ComputeVolume() - 1, "the vertical edges lose material too");
        // a hexagon corner (120 degrees inside) loses cos(60) * s along its bisector: the corner on the X axis moves in
        Assert.Equal(plain.ComputeBounds().Max.X, top.ComputeBounds().Max.X, 1e-3);
        Assert.Equal(10 - 0.5 * s, all.ComputeBounds().Max.X, 1e-3);
    }

    [Fact]
    public void A_rounded_prism_has_round_vertical_edges()
    {
        var all = Make("prism", 2, EdgeKind.Fillet, EdgeSide.All, ("sides", 4), ("diameter", 20), ("height", 20));

        // a square (with its corners on the axes): the corner at (10, 0) is rounded with radius 2, so the furthest point
        // of the mid height cross section is at 10 - 2 (sqrt(2) - 1)
        var expected = 10 - 2 * (Math.Sqrt(2) - 1);
        Assert.Equal(expected, all.ComputeBounds().Max.X, 0.03);
        Assert.True(all.IsWatertight());
    }

    [Fact]
    public void A_prism_with_three_or_many_sides_works()
    {
        foreach (var sides in new[] { 3, 5, 12, 40 })
        {
            foreach (var kind in new[] { EdgeKind.Chamfer, EdgeKind.Fillet })
            {
                var mesh = Make("prism", 1, kind, EdgeSide.All, ("sides", sides), ("height", 15));

                Assert.True(mesh.IsWatertight(), $"{sides} {kind}");
                Assert.True(mesh.ComputeVolume() > 0, $"{sides} {kind}");
            }
        }
    }

    // ---- wedge ----------------------------------------------------------------------------------------

    [Fact]
    public void A_chamfered_back_edge_of_a_wedge_removes_a_triangle_along_the_whole_edge()
    {
        const double s = 2, width = 20;
        var mesh = Make("wedge", s, EdgeKind.Chamfer, EdgeSide.Top);

        // the ramp and the back wall meet at 45 degrees (20 deep, 20 high); the chamfer is measured along both faces
        var removed = 0.5 * s * s * Math.Sin(Math.PI / 4) * width;

        Assert.True(mesh.IsWatertight());
        Assert.Equal(4000 - removed, mesh.ComputeVolume(), 0.05);
    }

    [Fact]
    public void A_rounded_back_edge_of_a_wedge_removes_the_area_between_the_arc_and_the_corner()
    {
        const double r = 2, width = 20, alpha = Math.PI / 4;
        var mesh = Make("wedge", r, EdgeKind.Fillet, EdgeSide.Top);

        var removed = r * r * (1 / Math.Tan(alpha / 2) - (Math.PI - alpha) / 2) * width;

        Assert.True(mesh.IsWatertight());
        Assert.Equal(4000 - removed, mesh.ComputeVolume(), removed * 0.02);
    }

    [Fact]
    public void The_edges_of_the_base_of_a_wedge_are_the_bottom_ones()
    {
        var plain = 4000.0;
        var bottom = Make("wedge", 2, EdgeKind.Chamfer, EdgeSide.Bottom);
        var top = Make("wedge", 2, EdgeKind.Chamfer, EdgeSide.Top);
        var all = Make("wedge", 2, EdgeKind.Chamfer, EdgeSide.All);

        Assert.True(bottom.ComputeVolume() < plain - 1);
        Assert.True(all.ComputeVolume() < Math.Min(bottom.ComputeVolume(), top.ComputeVolume()) - 1);
        Assert.Equal(0, bottom.ComputeBounds().Min.Z, 1e-3); // the base is still at the floor
    }

    [Fact]
    public void A_wedge_with_a_very_flat_ramp_skips_edges_that_are_too_sharp_instead_of_failing()
    {
        // 100 deep, 5 high: the ramp meets the base at under 3 degrees, nothing sensible can be cut there
        foreach (var kind in new[] { EdgeKind.Chamfer, EdgeKind.Fillet })
        {
            var mesh = Make("wedge", 1, kind, EdgeSide.All, ("depth", 100), ("height", 5));

            Assert.True(mesh.IsWatertight());
            Assert.True(mesh.ComputeVolume() > 0);
        }
    }

    // ---- sizes ----------------------------------------------------------------------------------------

    [Fact]
    public void A_negative_size_is_refused_by_every_shape()
    {
        foreach (var id in AllShapes)
            Assert.Throws<ArgumentException>(() => ShapeLibrary.Find(id)!.Generate(P(("edgeSize", -1))));
    }
}
