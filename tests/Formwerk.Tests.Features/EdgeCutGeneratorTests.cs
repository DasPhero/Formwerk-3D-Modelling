using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>The cutters of the edge tool: a straight wedge and a ring (F-EDG-02).</summary>
public class EdgeCutGeneratorTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Straight(double length, double angle, double size, EdgeKind kind) =>
        ShapeLibrary.Find("edgecut")!.Generate(P(("length", length), ("angle", angle), ("edgeSize", size), ("edgeType", (int)kind)));

    private static MeshData Ring(double radius, double size, EdgeKind kind, bool inside, int segments = 64) =>
        ShapeLibrary.Find("edgering")!.Generate(P(("radius", radius), ("edgeSize", size), ("edgeType", (int)kind), ("inside", inside ? 1 : 0), ("segments", segments)));

    private static bool Has(MeshData mesh, Vector3 point, float tolerance = 1e-3f) => mesh.Vertices.ToArray().Any(v => Vector3.Distance(v, point) < tolerance);

    /// <summary>Area of a polygon (the absolute value of the shoelace formula).</summary>
    private static double Area(IReadOnlyList<Vector2> p)
    {
        double sum = 0;
        for (var i = 0; i < p.Count; i++) sum += (double)p[i].X * p[(i + 1) % p.Count].Y - (double)p[(i + 1) % p.Count].X * p[i].Y;
        return Math.Abs(sum) / 2;
    }

    /// <summary>The factor between the volume of a revolved polygon with n sides and the true solid of revolution.</summary>
    private static double PolygonFactor(int n) => n / (2 * Math.PI) * Math.Sin(2 * Math.PI / n);

    // ---- registration ---------------------------------------------------------------------------------

    [Fact]
    public void The_cutters_are_found_by_id_but_are_not_in_the_shape_menu()
    {
        Assert.NotNull(ShapeLibrary.Find("edgecut"));
        Assert.NotNull(ShapeLibrary.Find("EDGERING"));
        Assert.DoesNotContain(ShapeLibrary.All, g => g.Id is "edgecut" or "edgering");
    }

    [Fact]
    public void The_edge_parameters_have_the_names_that_the_properties_panel_knows()
    {
        foreach (var id in new[] { "edgecut", "edgering" })
        {
            var parameters = ShapeLibrary.Find(id)!.Parameters;
            Assert.True(parameters.Single(p => p.Name == "edgeSize").IsDetail);
            Assert.True(parameters.Single(p => p.Name == "edgeType").IsInteger);
        }
    }

    [Fact]
    public void Every_size_in_mm_of_a_cutter_is_bound_to_an_axis_or_is_a_detail()
    {
        foreach (var id in new[] { "edgecut", "edgering" })
        {
            var generator = ShapeLibrary.Find(id)!;
            foreach (var parameter in generator.Parameters.Where(p => !p.IsInteger && !p.IsDetail && p.Unit is null))
                Assert.Single(generator.ScaleBindings, b => b.Parameter == parameter.Name);
        }
    }

    // ---- straight cutter ------------------------------------------------------------------------------

    [Theory]
    [InlineData(EdgeKind.Chamfer, 90)]
    [InlineData(EdgeKind.Fillet, 90)]
    [InlineData(EdgeKind.Chamfer, 120)]
    [InlineData(EdgeKind.Fillet, 120)]
    [InlineData(EdgeKind.Chamfer, 45)]
    [InlineData(EdgeKind.Fillet, 45)]
    [InlineData(EdgeKind.Chamfer, 160)]
    [InlineData(EdgeKind.Fillet, 160)]
    public void A_straight_cutter_is_a_closed_solid(EdgeKind kind, double angle)
    {
        var mesh = Straight(30, angle, 2, kind);

        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0, "the normals must point outward");
    }

    [Fact]
    public void A_straight_cutter_is_a_little_longer_than_the_edge()
    {
        var bounds = Straight(30, 90, 2, EdgeKind.Chamfer).ComputeBounds();

        Assert.Equal(-EdgeCutGenerator.EndExtension, bounds.Min.X, 4);
        Assert.Equal(30 + EdgeCutGenerator.EndExtension, bounds.Max.X, 4);
    }

    [Fact]
    public void The_chamfer_runs_from_one_face_to_the_other_at_the_chosen_distance()
    {
        // right angle: the faces run 45 degrees down from the corner on both sides; 2 mm along each face
        var mesh = Straight(30, 90, 2, EdgeKind.Chamfer);
        var d = 2 * MathF.Sqrt(0.5f);

        foreach (var x in new[] { -EdgeCutGenerator.EndExtension, 30 + EdgeCutGenerator.EndExtension })
        {
            Assert.True(Has(mesh, new Vector3(x, d, -d)));
            Assert.True(Has(mesh, new Vector3(x, -d, -d)));
        }
    }

    [Fact]
    public void The_chamfer_distance_is_measured_along_the_faces_at_any_angle()
    {
        var mesh = Straight(10, 120, 2, EdgeKind.Chamfer);

        // 120 degrees between the faces: each face leaves the corner at 60 degrees from the bisector
        Assert.True(Has(mesh, new Vector3(-EdgeCutGenerator.EndExtension, 2 * MathF.Sin(MathF.PI / 3), -2 * MathF.Cos(MathF.PI / 3)), 1e-3f));
        Assert.True(Has(mesh, new Vector3(10 + EdgeCutGenerator.EndExtension, -2 * MathF.Sin(MathF.PI / 3), -2 * MathF.Cos(MathF.PI / 3)), 1e-3f));
    }

    [Fact]
    public void The_cross_section_of_the_chamfer_cutter_is_the_triangle_plus_the_margin_outside_the_faces()
    {
        const double length = 30, size = 2, m = EdgeCutGenerator.MarginBase + EdgeCutGenerator.MarginPerSize * size;
        var mesh = Straight(length, 90, size, EdgeKind.Chamfer);
        var n1 = new Vector2(MathF.Cos(MathF.PI / 4), MathF.Sin(MathF.PI / 4));
        var n2 = new Vector2(-n1.X, n1.Y);
        var d1 = new Vector2(n1.Y, -n1.X);
        var d2 = new Vector2(-n1.Y, -n1.X);
        var apexOut = (n1 + n2) * (float)m / (1 + Vector2.Dot(n1, n2));
        Vector2[] polygon = [(float)size * d1, (float)size * d1 + (float)m * n1, apexOut, (float)size * d2 + (float)m * n2, (float)size * d2];

        var expected = Area(polygon) * (length + 2 * EdgeCutGenerator.EndExtension);

        Assert.Equal(expected, mesh.ComputeVolume(), expected * 1e-3);
    }

    [Fact]
    public void The_fillet_arc_is_tangent_to_both_faces_and_has_the_chosen_radius()
    {
        const float r = 3;
        var mesh = Straight(20, 90, r, EdgeKind.Fillet);
        var center = new Vector2(0, -r * MathF.Sqrt(2)); // r / sin(45 degrees) below the corner

        var ends = mesh.Vertices.ToArray()
            .Where(v => MathF.Abs(v.X + EdgeCutGenerator.EndExtension) < 1e-4f && MathF.Abs(Vector2.Distance(new Vector2(v.Y, v.Z), center) - r) < 1e-3f)
            .ToList();

        Assert.True(ends.Count >= 6, "the arc has several points");
        // the tangent points are r / tan(45 degrees) = r from the corner along each face
        var t = r * MathF.Sqrt(0.5f);
        Assert.Contains(ends, v => MathF.Abs(v.Y - t) < 1e-3f && MathF.Abs(v.Z + t) < 1e-3f);
        Assert.Contains(ends, v => MathF.Abs(v.Y + t) < 1e-3f && MathF.Abs(v.Z + t) < 1e-3f);
    }

    [Fact]
    public void The_fillet_cutter_is_the_chamfer_cutter_minus_the_segment_between_the_chamfer_line_and_the_arc()
    {
        const double length = 30, r = 2;
        var fillet = Straight(length, 90, r, EdgeKind.Fillet);
        var chamfer = Straight(length, 90, r, EdgeKind.Chamfer);

        // at a right angle the tangent points are r along the faces, where the chamfer of the same size starts and ends
        var segment = r * r * (Math.PI / 4 - 0.5) * (length + 2 * EdgeCutGenerator.EndExtension);

        Assert.Equal(segment, chamfer.ComputeVolume() - fillet.ComputeVolume(), segment * 0.02);
    }
    [Fact]
    public void A_bigger_angle_has_longer_tangents_for_the_same_radius()
    {
        var sharp = Straight(10, 60, 2, EdgeKind.Fillet).ComputeBounds();
        var wide = Straight(10, 140, 2, EdgeKind.Fillet).ComputeBounds();

        // tangent length = r / tan(angle / 2): the wide one touches the faces closer to the corner
        Assert.True(sharp.Size.Y > wide.Size.Y);
    }

    [Fact]
    public void The_ranges_of_the_straight_cutter_are_checked()
    {
        var generator = ShapeLibrary.Find("edgecut")!;

        Assert.Throws<ArgumentException>(() => generator.Generate(P(("angle", 10))));
        Assert.Throws<ArgumentException>(() => generator.Generate(P(("angle", 175))));
        Assert.Throws<ArgumentException>(() => generator.Generate(P(("length", 0))));
        Assert.Throws<ArgumentException>(() => generator.Generate(P(("edgeSize", 0))));
    }

    // ---- ring cutter ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(EdgeKind.Chamfer, false)]
    [InlineData(EdgeKind.Fillet, false)]
    [InlineData(EdgeKind.Chamfer, true)]
    [InlineData(EdgeKind.Fillet, true)]
    public void A_ring_cutter_is_a_closed_solid(EdgeKind kind, bool inside)
    {
        var mesh = Ring(10, 2, kind, inside);

        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0, "the normals must point outward");
    }

    [Fact]
    public void The_ring_of_a_boss_cuts_from_the_top_inwards_and_down_the_wall()
    {
        var mesh = Ring(10, 2, EdgeKind.Chamfer, inside: false);

        Assert.True(Has(mesh, new Vector3(8, 0, 0)));   // on the face, 2 mm from the edge
        Assert.True(Has(mesh, new Vector3(10, 0, -2))); // on the wall, 2 mm below the edge
    }

    [Fact]
    public void The_ring_of_a_hole_cuts_from_the_top_outwards_and_down_the_wall()
    {
        var mesh = Ring(10, 2, EdgeKind.Chamfer, inside: true);

        Assert.True(Has(mesh, new Vector3(12, 0, 0)));
        Assert.True(Has(mesh, new Vector3(10, 0, -2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_volume_of_a_chamfer_ring_follows_from_its_cross_section(bool inside)
    {
        const double radius = 10, size = 2, m = EdgeCutGenerator.MarginBase + EdgeCutGenerator.MarginPerSize * size;
        const int n = 96;
        var mesh = Ring(radius, size, EdgeKind.Chamfer, inside, n);

        // boss: from the face inwards, down the wall; hole: from the face outwards, down the wall
        var s = inside ? 1.0 : -1.0;
        Vector2[] polygon =
        [
            new((float)(radius + s * size), 0), new((float)(radius + s * size), (float)m), new((float)(radius - s * m), (float)m),
            new((float)(radius - s * m), (float)-size), new((float)radius, (float)-size),
        ];
        double ring = 0;
        for (var i = 0; i < polygon.Length; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Length];
            ring += (a.X + b.X) * ((double)a.X * b.Y - (double)b.X * a.Y) / 6; // area times the distance of the centre of the area from the axis
        }

        var expected = Math.Abs(ring) * 2 * Math.PI * PolygonFactor(n);
        Assert.Equal(expected, mesh.ComputeVolume(), expected * 1e-3);
    }

    [Fact]
    public void The_ring_has_the_requested_number_of_segments_and_starts_on_the_x_axis()
    {
        var mesh = Ring(10, 2, EdgeKind.Chamfer, inside: false, segments: 24);

        // 24 positions around the axis at each of the 5 points of the cross section
        Assert.Equal(5 * 24, mesh.VertexCount);
        Assert.True(Has(mesh, new Vector3(8, 0, 0)));
        Assert.True(Has(mesh, new Vector3(8 * MathF.Cos(MathF.PI / 12), 8 * MathF.Sin(MathF.PI / 12), 0)));
    }

    [Fact]
    public void A_size_that_would_reach_the_axis_is_cut_down()
    {
        var boss = Ring(10, 50, EdgeKind.Fillet, inside: false);

        Assert.True(boss.IsWatertight());
        Assert.True(boss.ComputeBounds().Min.X > -11.5f); // not -40: the size stops just short of the axis
        Assert.True(boss.ComputeVolume() > 0);
        Assert.True(Ring(10, 50, EdgeKind.Chamfer, inside: false).IsWatertight());
    }
    [Fact]
    public void A_tiny_ring_keeps_its_margin_inside_the_radius()
    {
        var mesh = Ring(0.3, 0.1, EdgeKind.Chamfer, inside: true);

        Assert.True(mesh.IsWatertight());
        Assert.True(mesh.ComputeVolume() > 0);
    }

    // ---- fillet ring ----------------------------------------------------------------------------------

    [Fact]
    public void The_fillet_ring_is_the_chamfer_ring_minus_a_ring_shaped_circle_segment()
    {
        const double radius = 10, size = 2;
        const int n = 96;
        var fillet = Ring(radius, size, EdgeKind.Fillet, inside: false, n);
        var chamfer = Ring(radius, size, EdgeKind.Chamfer, inside: false, n);

        // the segment between the chamfer line and the arc has the area s^2 (pi/4 - 1/2); its centre is 0.416 s inside the rim
        var segment = size * size * (Math.PI / 4 - 0.5) * 2 * Math.PI * (radius - 0.416 * size) * PolygonFactor(n);

        Assert.Equal(segment, chamfer.ComputeVolume() - fillet.ComputeVolume(), segment * 0.03);
    }
}
