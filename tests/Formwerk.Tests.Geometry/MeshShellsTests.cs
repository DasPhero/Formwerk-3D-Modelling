using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

/// <summary>Splitting a mesh into its separate shells (F-SPL-03).</summary>
public class MeshShellsTests
{
    /// <summary>A tetrahedron with a corner at (x, y, z) and the given edge length; <paramref name="inverted"/> turns it inside out.</summary>
    private static MeshData Tetra(float x, float size = 1, bool inverted = false, float y = 0, float z = 0)
    {
        Vector3[] v = [new(x, y, z), new(x + size, y, z), new(x, y + size, z), new(x, y, z + size)];
        int[] outward = [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3];
        int[] inside = [0, 1, 2, 0, 3, 1, 1, 3, 2, 2, 3, 0];
        return new MeshData(v, inverted ? inside : outward);
    }

    /// <summary>The same tetrahedron with its own three vertices for every triangle, as unwelded files have it.</summary>
    private static MeshData Unwelded(MeshData mesh)
    {
        var vertices = new List<Vector3>();
        var indices = new List<int>();
        for (var i = 0; i < mesh.Indices.Length; i++)
        {
            indices.Add(vertices.Count);
            vertices.Add(mesh.Vertices[mesh.Indices[i]]);
        }

        return new MeshData([.. vertices], [.. indices]);
    }

    [Fact]
    public void Separate_bodies_become_one_mesh_each()
    {
        var shells = MeshShells.Split(MeshCombine.Concat([Tetra(0), Tetra(10), Tetra(20)]));

        Assert.Equal(3, shells.Count);
        Assert.All(shells, s =>
        {
            Assert.Equal(4, s.VertexCount);
            Assert.Equal(4, s.TriangleCount);
            Assert.True(s.IsWatertight());
            Assert.Equal(Tetra(0).ComputeVolume(), s.ComputeVolume(), 6);
        });
    }

    [Fact]
    public void One_connected_body_stays_one_mesh()
    {
        var shells = MeshShells.Split(Tetra(0, 5));

        var only = Assert.Single(shells);
        Assert.Equal(4, only.TriangleCount);
    }

    [Fact]
    public void Vertices_at_the_same_place_count_as_shared_so_unwelded_files_stay_whole()
    {
        var shells = MeshShells.Split(Unwelded(Tetra(0, 5)));

        var only = Assert.Single(shells);
        Assert.Equal(4, only.TriangleCount);
        Assert.Equal(Tetra(0, 5).ComputeVolume(), only.ComputeVolume(), 6);
    }

    [Fact]
    public void Unwelded_separate_bodies_are_still_separated()
    {
        var shells = MeshShells.Split(MeshCombine.Concat([Unwelded(Tetra(0)), Unwelded(Tetra(10))]));

        Assert.Equal(2, shells.Count);
    }

    [Fact]
    public void The_pieces_are_ordered_from_left_to_right()
    {
        var shells = MeshShells.Split(MeshCombine.Concat([Tetra(20), Tetra(0), Tetra(10)]));

        Assert.Equal([0f, 10f, 20f], shells.Select(s => s.ComputeBounds().Min.X).ToArray());
    }

    [Fact]
    public void A_cavity_stays_with_the_body_around_it()
    {
        // a big body with a small one turned inside out inside it: a hollow body, one piece
        var hollow = MeshCombine.Concat([Tetra(0, 20), Tetra(2, 3, inverted: true, y: 2, z: 2)]);

        var shells = MeshShells.Split(hollow);

        var only = Assert.Single(shells);
        Assert.Equal(8, only.TriangleCount);
        Assert.Equal(Tetra(0, 20).ComputeVolume() - Tetra(2, 3, y: 2, z: 2).ComputeVolume(), only.ComputeVolume(), 6);
    }

    [Fact]
    public void A_cavity_goes_to_the_body_that_surrounds_it_and_not_to_another_one()
    {
        var mesh = MeshCombine.Concat([Tetra(100, 20), Tetra(0, 20), Tetra(2, 3, inverted: true, y: 2, z: 2)]);

        var shells = MeshShells.Split(mesh);

        Assert.Equal(2, shells.Count);
        Assert.Equal(8, shells[0].TriangleCount); // x from 0: the hollow one
        Assert.Equal(4, shells[1].TriangleCount);
    }

    [Fact]
    public void A_turned_inside_out_body_without_a_body_around_it_is_a_piece_of_its_own()
    {
        var shells = MeshShells.Split(MeshCombine.Concat([Tetra(0), Tetra(10, inverted: true)]));

        Assert.Equal(2, shells.Count);
    }

    [Fact]
    public void Nothing_gives_no_pieces()
    {
        Assert.Empty(MeshShells.Split(MeshData.Empty));
    }

    [Fact]
    public void The_triangles_keep_their_winding_and_position()
    {
        var source = Tetra(7, 3);

        var only = Assert.Single(MeshShells.Split(source));

        Assert.Equal(source.ComputeVolume(), only.ComputeVolume(), 9);
        Assert.Equal(source.ComputeBounds(), only.ComputeBounds());
    }
}
