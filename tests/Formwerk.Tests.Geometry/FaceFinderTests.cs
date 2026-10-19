using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

/// <summary>The flat face around a clicked point (snap of the hole tool to the face center, F-HOL-01).</summary>
public class FaceFinderTests
{
    /// <summary>A box from (x0, y0, 0) to (x1, y1, h) with outward triangles; <paramref name="welded"/> false gives every triangle its own vertices.</summary>
    private static MeshData Box(float x0, float y0, float x1, float y1, float h, bool welded = true)
    {
        Vector3[] v =
        [
            new(x0, y0, 0), new(x1, y0, 0), new(x1, y1, 0), new(x0, y1, 0),
            new(x0, y0, h), new(x1, y0, h), new(x1, y1, h), new(x0, y1, h),
        ];
        int[] i =
        [
            0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7,
        ];
        return welded ? new MeshData(v, i) : Unweld(new MeshData(v, i));
    }

    private static MeshData Unweld(MeshData mesh)
    {
        var vertices = new List<Vector3>();
        foreach (var index in mesh.Indices) vertices.Add(mesh.Vertices[index]);
        return new MeshData([.. vertices], [.. Enumerable.Range(0, vertices.Count)]);
    }

    private static MeshData Combine(params MeshData[] meshes)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        foreach (var mesh in meshes)
        {
            var offset = vertices.Count;
            vertices.AddRange(mesh.Vertices.ToArray());
            indices.AddRange(mesh.Indices.ToArray().Select(i => i + offset));
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    [Fact]
    public void The_top_of_a_box_is_found_with_its_center_and_size()
    {
        var face = FaceFinder.Find(Box(-10, -10, 10, 10, 10), new Vector3(3, 4, 10), Vector3.UnitZ);

        Assert.NotNull(face);
        Assert.True(Vector3.Distance(face!.Center, new Vector3(0, 0, 10)) < 1e-4f);
        Assert.Equal(20 * Math.Sqrt(2), face.Extent, 2);
        Assert.True(face.Contains(new Vector3(9, -9, 10)));
        Assert.False(face.Contains(new Vector3(11, 0, 10)));
    }

    [Fact]
    public void The_center_of_an_off_center_face_is_its_own_center()
    {
        var face = FaceFinder.Find(Box(10, 20, 50, 30, 5), new Vector3(12, 22, 5), Vector3.UnitZ);

        Assert.True(Vector3.Distance(face!.Center, new Vector3(30, 25, 5)) < 1e-4f);
    }

    [Fact]
    public void A_side_face_is_found_by_its_own_normal_and_does_not_take_the_top()
    {
        var mesh = Box(-10, -10, 10, 10, 10);

        var side = FaceFinder.Find(mesh, new Vector3(10, 2, 4), Vector3.UnitX);

        Assert.True(Vector3.Distance(side!.Center, new Vector3(10, 0, 5)) < 1e-4f);
        Assert.False(side.Contains(new Vector3(0, 0, 10)));
    }

    [Fact]
    public void Vertices_that_are_not_shared_are_still_one_face()
    {
        var face = FaceFinder.Find(Box(-10, -10, 10, 10, 10, welded: false), new Vector3(-8, 8, 10), Vector3.UnitZ);

        Assert.True(Vector3.Distance(face!.Center, new Vector3(0, 0, 10)) < 1e-4f);
        Assert.True(face.Contains(new Vector3(8, -8, 10)));
    }

    [Fact]
    public void A_flat_face_that_is_not_connected_is_not_part_of_it()
    {
        var mesh = Combine(Box(-30, -10, -10, 10, 10), Box(10, -10, 30, 10, 10)); // two tops at the same height with a gap

        var face = FaceFinder.Find(mesh, new Vector3(-20, 0, 10), Vector3.UnitZ);

        Assert.True(Vector3.Distance(face!.Center, new Vector3(-20, 0, 10)) < 1e-4f);
        Assert.False(face.Contains(new Vector3(20, 0, 10)));
    }

    [Fact]
    public void A_slanted_face_is_found()
    {
        // wedge: a ramp from (0, 0, 0) up to (0, 10, 10) over a 10 x 10 base
        Vector3[] v = [new(0, 0, 0), new(10, 0, 0), new(10, 10, 0), new(0, 10, 0), new(0, 10, 10), new(10, 10, 10)];
        int[] i = [0, 2, 1, 0, 3, 2, 0, 1, 5, 0, 5, 4, 0, 4, 3, 1, 2, 5, 3, 4, 5, 3, 5, 2];
        var mesh = new MeshData(v, i);
        var normal = Vector3.Normalize(new Vector3(0, -1, 1));

        var face = FaceFinder.Find(mesh, new Vector3(5, 5, 5), normal);

        Assert.NotNull(face);
        Assert.True(Vector3.Distance(face!.Center, new Vector3(5, 5, 5)) < 1e-4f);
        Assert.True(Vector3.Distance(face.Normal, normal) < 1e-4f);
    }

    [Fact]
    public void A_point_that_is_not_on_a_face_finds_nothing()
    {
        var mesh = Box(-10, -10, 10, 10, 10);

        Assert.Null(FaceFinder.Find(mesh, new Vector3(0, 0, 12), Vector3.UnitZ)); // above the box
        Assert.Null(FaceFinder.Find(mesh, new Vector3(30, 0, 10), Vector3.UnitZ)); // beside it
        Assert.Null(FaceFinder.Find(mesh, new Vector3(0, 0, 10), -Vector3.UnitZ)); // the wrong way round
    }

    [Fact]
    public void A_face_with_a_hole_has_its_center_in_the_middle_and_does_not_contain_the_hole()
    {
        // top face of a plate: outer square -10..10, square hole -3..3, eight triangles
        Vector3[] v =
        [
            new(-10, -10, 5), new(10, -10, 5), new(10, 10, 5), new(-10, 10, 5),
            new(-3, -3, 5), new(3, -3, 5), new(3, 3, 5), new(-3, 3, 5),
        ];
        int[] i = [0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5, 2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7];
        var face = FaceFinder.Find(new MeshData(v, i), new Vector3(-8, 0, 5), Vector3.UnitZ);

        Assert.NotNull(face);
        Assert.True(Vector3.Distance(face!.Center, new Vector3(0, 0, 5)) < 1e-4f);
        Assert.False(face.Contains(new Vector3(0, 0, 5)));
        Assert.True(face.Contains(new Vector3(-8, 0, 5)));
    }

    [Fact]
    public void An_empty_mesh_or_a_zero_normal_finds_nothing()
    {
        Assert.Null(FaceFinder.Find(MeshData.Empty, Vector3.Zero, Vector3.UnitZ));
        Assert.Null(FaceFinder.Find(Box(-1, -1, 1, 1, 1), new Vector3(0, 0, 1), Vector3.Zero));
    }
}
