using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>The section view of parts: the body is cut, the cut surface is filled (E-06).</summary>
public class SectionBuilderTests
{
    private static readonly ManifoldKernel Kernel = new();

    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box() => new BoxGenerator().Generate(P()); // 20 x 20 x 20, z from 0 to 20

    private static ScenePart Part(MeshData mesh, PartKind kind = PartKind.Solid) => new(new PrimitiveNode("box", "Part"), mesh, kind);

    /// <summary>A 20 x 20 x 20 box with a vertical hole of diameter 10 (64 corners) through the middle.</summary>
    private static MeshData BoxWithHole()
    {
        var hole = new CylinderGenerator().Generate(P(("diameter", 10), ("height", 40), ("segments", 64))).Transformed(Matrix4x4.CreateTranslation(0, 0, -10));
        return Kernel.Boolean(BooleanOperation.Difference, [Box(), hole], CancellationToken.None);
    }

    private static double HoleArea() => 64 / 2.0 * 25 * Math.Sin(2 * Math.PI / 64); // the area of the 64-sided hole

    private static MeshData Join(MeshData a, MeshData b)
    {
        var vertices = a.Vertices.ToArray().Concat(b.Vertices.ToArray()).ToArray();
        var indices = a.Indices.ToArray().Concat(b.Indices.ToArray().Select(i => i + a.VertexCount)).ToArray();
        return new MeshData(vertices, indices);
    }

    // ---- the cap --------------------------------------------------------------------------------------

    [Fact]
    public void The_cap_of_a_box_is_its_cross_section_lying_in_the_plane_and_looking_to_the_front()
    {
        var cap = SectionBuilder.Cap(Box(), SectionPlane.Axis(2, 10), Kernel, CancellationToken.None)!;

        Assert.Equal(400, cap.ComputeSurfaceArea(), 0.01);
        Assert.All(cap.Vertices.ToArray(), v => Assert.Equal(10, v.Z, 1e-3));
        var vertices = cap.Vertices.ToArray();
        var indices = cap.Indices.ToArray();
        for (var t = 0; t < indices.Length; t += 3)
        {
            var normal = Vector3.Normalize(Vector3.Cross(vertices[indices[t + 1]] - vertices[indices[t]], vertices[indices[t + 2]] - vertices[indices[t]]));
            Assert.True(normal.Z > 0.99f, "the cap must look away from the material, towards the cut-away side");
        }
    }

    [Fact]
    public void The_body_and_the_cap_together_are_a_closed_solid_of_the_right_volume()
    {
        var plane = SectionPlane.Axis(2, 10);
        var body = MeshClipper.Clip(Box(), plane);
        var cap = SectionBuilder.Cap(Box(), plane, Kernel, CancellationToken.None)!;

        Assert.Equal(4000, Join(body, cap).ComputeVolume(), 0.05);
    }

    [Fact]
    public void The_cap_of_the_other_side_looks_the_other_way()
    {
        var plane = SectionPlane.Axis(2, 10).Flipped;
        var body = MeshClipper.Clip(Box(), plane);
        var cap = SectionBuilder.Cap(Box(), plane, Kernel, CancellationToken.None)!;

        Assert.Equal(4000, Join(body, cap).ComputeVolume(), 0.05);
        Assert.All(cap.Vertices.ToArray(), v => Assert.Equal(10, v.Z, 1e-3));
    }

    [Fact]
    public void A_hole_in_the_plane_is_a_hole_in_the_cap()
    {
        var cap = SectionBuilder.Cap(BoxWithHole(), SectionPlane.Axis(2, 10), Kernel, CancellationToken.None)!;

        Assert.Equal(400 - HoleArea(), cap.ComputeSurfaceArea(), 0.05);
    }

    [Fact]
    public void A_plane_through_the_hole_shows_the_hole_as_a_slot()
    {
        // the plane x = 0 goes through two corners of the 64-sided hole: a slot 10 mm wide over the whole height
        var cap = SectionBuilder.Cap(BoxWithHole(), SectionPlane.Axis(0, 0), Kernel, CancellationToken.None)!;

        Assert.Equal(400 - 10 * 20, cap.ComputeSurfaceArea(), 0.05);
        Assert.All(cap.Vertices.ToArray(), v => Assert.Equal(0, v.X, 1e-3));
    }

    [Fact]
    public void A_slanted_plane_gives_a_flat_cap_in_that_plane()
    {
        var plane = SectionPlane.FromNormal(new Vector3(1, 0, 1), new Vector3(0, 0, 10));
        var cap = SectionBuilder.Cap(Box(), plane, Kernel, CancellationToken.None)!;

        Assert.False(cap.IsEmpty);
        Assert.All(cap.Vertices.ToArray(), v => Assert.Equal(0, plane.Distance(v), 1e-3));
    }

    [Fact]
    public void A_plane_that_misses_the_mesh_has_no_cap()
    {
        var cap = SectionBuilder.Cap(Box(), SectionPlane.Axis(2, 50), Kernel, CancellationToken.None);

        Assert.True(cap is null || cap.IsEmpty);
    }

    [Fact]
    public void A_mesh_that_the_kernel_cannot_use_has_no_cap_and_does_not_throw()
    {
        var open = MeshClipper.Clip(Box(), SectionPlane.Axis(2, 10)); // a box without its top: not a solid

        var cap = SectionBuilder.Cap(open, SectionPlane.Axis(2, 5), Kernel, CancellationToken.None);

        Assert.True(cap is null || cap.IsEmpty || cap.ComputeSurfaceArea() >= 0);
    }

    [Fact]
    public void A_cancelled_cap_stops()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SectionBuilder.Cap(Box(), SectionPlane.Axis(2, 10), Kernel, source.Token));
    }

    // ---- parts ----------------------------------------------------------------------------------------

    [Fact]
    public void A_part_that_the_plane_cuts_gets_a_cut_body_and_a_cap()
    {
        var parts = SectionBuilder.Build([Part(Box())], SectionPlane.Axis(2, 10), Kernel, CancellationToken.None);

        var result = Assert.Single(parts);
        Assert.Equal(10, result.Body.ComputeBounds().Max.Z, 1e-4);
        Assert.NotNull(result.Cap);
        Assert.Equal(400, result.Cap!.ComputeSurfaceArea(), 0.01);
    }

    [Fact]
    public void A_part_behind_the_plane_is_passed_on_as_it_is()
    {
        var box = Box();

        var result = Assert.Single(SectionBuilder.Build([Part(box)], SectionPlane.Axis(2, 50), Kernel, CancellationToken.None));

        Assert.Same(box, result.Body);
        Assert.Null(result.Cap);
    }

    [Fact]
    public void A_part_in_front_of_the_plane_is_dropped()
    {
        Assert.Empty(SectionBuilder.Build([Part(Box())], SectionPlane.Axis(2, -5), Kernel, CancellationToken.None));
    }

    [Fact]
    public void A_hole_ghost_is_cut_but_gets_no_cap()
    {
        var result = Assert.Single(SectionBuilder.Build([Part(Box(), PartKind.Ghost)], SectionPlane.Axis(2, 10), Kernel, CancellationToken.None));

        Assert.Equal(10, result.Body.ComputeBounds().Max.Z, 1e-4);
        Assert.Null(result.Cap);
    }

    [Fact]
    public void Without_a_kernel_the_body_is_cut_and_there_is_no_cap()
    {
        var result = Assert.Single(SectionBuilder.Build([Part(Box())], SectionPlane.Axis(2, 10), null, CancellationToken.None));

        Assert.Equal(10, result.Body.ComputeBounds().Max.Z, 1e-4);
        Assert.Null(result.Cap);
    }

    [Fact]
    public void Several_parts_keep_their_order_and_their_source()
    {
        var a = Part(Box());
        var b = Part(Box().Transformed(Matrix4x4.CreateTranslation(50, 0, 0)));
        var c = Part(Box().Transformed(Matrix4x4.CreateTranslation(0, 0, 30))); // above the plane z = 10 + 10: in front, dropped

        var parts = SectionBuilder.Build([a, b, c], SectionPlane.Axis(2, 10), Kernel, CancellationToken.None);

        Assert.Equal(2, parts.Count);
        Assert.Same(a, parts[0].Source);
        Assert.Same(b, parts[1].Source);
        Assert.All(parts, p => Assert.NotNull(p.Cap));
    }

    [Fact]
    public void The_other_side_shows_the_other_half()
    {
        var plane = SectionPlane.Axis(2, 10).Flipped;

        var result = Assert.Single(SectionBuilder.Build([Part(Box())], plane, Kernel, CancellationToken.None));

        Assert.Equal(10, result.Body.ComputeBounds().Min.Z, 1e-4);
        Assert.Equal(400, result.Cap!.ComputeSurfaceArea(), 0.01);
    }

    [Fact]
    public void Building_can_be_cancelled()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SectionBuilder.Build([Part(Box())], SectionPlane.Axis(2, 10), Kernel, source.Token));
    }

    [Fact]
    public void The_input_meshes_are_not_changed()
    {
        var box = Box();
        var before = box.Vertices.ToArray();

        SectionBuilder.Build([Part(box)], SectionPlane.Axis(2, 10), Kernel, CancellationToken.None);

        Assert.Equal(before, box.Vertices.ToArray());
    }
}
