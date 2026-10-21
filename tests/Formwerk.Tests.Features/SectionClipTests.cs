using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>Cutting a mesh at a plane for the section view (E-06): the part in front of the plane goes away.</summary>
public class SectionClipTests
{
    private static Dictionary<string, double> P(params (string, double)[] values) =>
        values.ToDictionary(v => v.Item1, v => v.Item2, StringComparer.OrdinalIgnoreCase);

    private static MeshData Box() => new BoxGenerator().Generate(P()); // 20 x 20 x 20, z from 0 to 20

    // ---- the plane ------------------------------------------------------------------------------------

    [Fact]
    public void A_plane_along_an_axis_has_that_axis_as_its_normal()
    {
        Assert.Equal(Vector3.UnitX, SectionPlane.Axis(0, 5).Normal);
        Assert.Equal(Vector3.UnitY, SectionPlane.Axis(1, 5).Normal);
        Assert.Equal(Vector3.UnitZ, SectionPlane.Axis(2, 5).Normal);
        Assert.Equal(5, SectionPlane.Axis(2, 5).Offset);
    }

    [Fact]
    public void The_distance_is_positive_in_front_of_the_plane_and_negative_behind_it()
    {
        var plane = SectionPlane.Axis(2, 10);

        Assert.Equal(5, plane.Distance(new Vector3(3, 4, 15)), 1e-5);
        Assert.Equal(-4, plane.Distance(new Vector3(3, 4, 6)), 1e-5);
        Assert.Equal(0, plane.Distance(new Vector3(0, 0, 10)), 1e-5);
    }

    [Fact]
    public void The_other_side_is_the_same_plane_turned_round()
    {
        var plane = SectionPlane.Axis(2, 10);
        var other = plane.Flipped;

        Assert.Equal(-Vector3.UnitZ, other.Normal);
        Assert.Equal(-plane.Distance(new Vector3(0, 0, 15)), other.Distance(new Vector3(0, 0, 15)), 1e-5);
        Assert.Equal(plane, other.Flipped);
    }

    [Fact]
    public void A_slanted_plane_normalizes_its_normal()
    {
        var plane = SectionPlane.FromNormal(new Vector3(3, 0, 4), new Vector3(0, 0, 0));

        Assert.Equal(1, plane.Normal.Length(), 1e-5);
        Assert.Equal(0, plane.Offset, 1e-5);
        var through = SectionPlane.FromNormal(new Vector3(0, 0, 2), new Vector3(5, 5, 7));
        Assert.Equal(7, through.Offset, 1e-5);
    }

    // ---- clipping -------------------------------------------------------------------------------------

    [Fact]
    public void Clipping_a_box_in_the_middle_keeps_the_lower_half_without_a_top()
    {
        var clipped = MeshClipper.Clip(Box(), SectionPlane.Axis(2, 10));

        var bounds = clipped.ComputeBounds();
        Assert.Equal(0, bounds.Min.Z, 1e-4);
        Assert.Equal(10, bounds.Max.Z, 1e-4);
        Assert.Equal(400 + 4 * 20 * 10, clipped.ComputeSurfaceArea(), 1e-2); // the bottom and four half sides, the cut is open
    }

    [Fact]
    public void The_other_side_keeps_the_upper_half()
    {
        var clipped = MeshClipper.Clip(Box(), SectionPlane.Axis(2, 10).Flipped);

        var bounds = clipped.ComputeBounds();
        Assert.Equal(10, bounds.Min.Z, 1e-4);
        Assert.Equal(20, bounds.Max.Z, 1e-4);
        Assert.Equal(400 + 4 * 20 * 10, clipped.ComputeSurfaceArea(), 1e-2);
    }

    [Fact]
    public void A_plane_beyond_the_mesh_leaves_it_as_it_is()
    {
        var box = Box();

        Assert.Same(box, MeshClipper.Clip(box, SectionPlane.Axis(2, 50)));
        Assert.Same(box, MeshClipper.Clip(box, SectionPlane.Axis(2, 20))); // the top lies in the plane: nothing is in front of it
    }

    [Fact]
    public void A_plane_that_has_the_whole_mesh_in_front_removes_it()
    {
        var box = Box();

        Assert.True(MeshClipper.Clip(box, SectionPlane.Axis(2, -5)).IsEmpty);
        Assert.True(MeshClipper.Clip(box, SectionPlane.Axis(2, 0)).IsEmpty); // the bottom lies in the plane
    }

    [Fact]
    public void An_empty_mesh_stays_empty()
    {
        Assert.True(MeshClipper.Clip(MeshData.Empty, SectionPlane.Axis(2, 1)).IsEmpty);
    }

    [Fact]
    public void A_slanted_cut_leaves_nothing_in_front_of_the_plane()
    {
        var cylinder = new CylinderGenerator().Generate(P(("segments", 48)));
        var plane = SectionPlane.FromNormal(new Vector3(1, 1, 0.3f), new Vector3(1, 0, 8));

        var clipped = MeshClipper.Clip(cylinder, plane);

        Assert.False(clipped.IsEmpty);
        Assert.All(clipped.Vertices.ToArray(), v => Assert.True(plane.Distance(v) <= 1e-4f, $"{v} is in front of the plane"));
        Assert.True(clipped.TriangleCount > 10);
    }

    [Fact]
    public void Clipped_triangles_keep_their_outward_direction()
    {
        // the clipped box plus a lid at the plane is a closed solid with the volume of the lower half
        var clipped = MeshClipper.Clip(Box(), SectionPlane.Axis(2, 10));
        var vertices = clipped.Vertices.ToArray().Concat([new(-10, -10, 10), new(10, -10, 10), new(10, 10, 10), new(-10, 10, 10)]).ToArray();
        var n = clipped.VertexCount;
        var indices = clipped.Indices.ToArray().Concat([n, n + 1, n + 2, n, n + 2, n + 3]).ToArray();

        var closed = new MeshData(vertices, indices);

        Assert.Equal(4000, closed.ComputeVolume(), 1e-2);
    }

    [Fact]
    public void Welded_vertices_stay_shared_so_that_the_shading_stays_smooth()
    {
        var sphere = new SphereGenerator().Generate(P());

        var clipped = MeshClipper.Clip(sphere, SectionPlane.Axis(2, 3));

        Assert.True(clipped.VertexCount < clipped.TriangleCount * 3 * 0.8, $"{clipped.VertexCount} vertices for {clipped.TriangleCount} triangles");
    }

    [Fact]
    public void Every_index_of_the_result_points_to_a_vertex()
    {
        var clipped = MeshClipper.Clip(new SphereGenerator().Generate(P()), SectionPlane.FromNormal(new Vector3(1, 2, 3), new Vector3(0, 0, 4)));

        Assert.All(clipped.Indices.ToArray(), i => Assert.InRange(i, 0, clipped.VertexCount - 1));
        Assert.Equal(0, clipped.Indices.Length % 3);
    }

    [Fact]
    public void The_cut_runs_exactly_through_the_triangles_it_meets()
    {
        // a single triangle cut at its middle: a quadrilateral (two triangles) is left on one side, a triangle on the other
        var triangle = new MeshData([new(0, 0, 0), new(10, 0, 0), new(0, 10, 0)], [0, 1, 2]);

        var below = MeshClipper.Clip(triangle, SectionPlane.FromNormal(Vector3.UnitX, new Vector3(4, 0, 0)));
        var above = MeshClipper.Clip(triangle, SectionPlane.FromNormal(Vector3.UnitX, new Vector3(4, 0, 0)).Flipped);

        Assert.Equal(2, below.TriangleCount);
        Assert.Equal(1, above.TriangleCount);
        Assert.Equal(50, below.ComputeSurfaceArea() + above.ComputeSurfaceArea(), 1e-3);
        Assert.Equal(50 - 0.5 * 6 * 6, below.ComputeSurfaceArea(), 1e-3);
    }
}
