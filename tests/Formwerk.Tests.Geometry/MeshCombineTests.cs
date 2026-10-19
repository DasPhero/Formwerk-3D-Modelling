using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

public class MeshCombineTests
{
    private static MeshData Tetra(float x) =>
        new([new(x, 0, 0), new(x + 1, 0, 0), new(x, 1, 0), new(x, 0, 1)], [0, 2, 1, 0, 1, 3, 1, 2, 3, 2, 0, 3]);

    [Fact]
    public void Several_meshes_become_one_with_all_vertices_and_triangles()
    {
        var combined = MeshCombine.Concat([Tetra(0), Tetra(10), Tetra(20)]);

        Assert.Equal(12, combined.VertexCount);
        Assert.Equal(12, combined.TriangleCount);
        Assert.True(combined.IsWatertight());
        Assert.Equal(3 * Tetra(0).ComputeVolume(), combined.ComputeVolume(), 6);
    }

    [Fact]
    public void The_triangles_of_later_meshes_point_at_their_own_vertices()
    {
        var combined = MeshCombine.Concat([Tetra(0), Tetra(10)]);

        for (var t = 4; t < 8; t++)
        {
            for (var c = 0; c < 3; c++)
            {
                var index = combined.Indices[t * 3 + c];
                Assert.InRange(index, 4, 7);
                Assert.True(combined.Vertices[index].X >= 10);
            }
        }
    }

    [Fact]
    public void Empty_meshes_are_skipped_and_nothing_gives_the_empty_mesh()
    {
        Assert.Same(MeshData.Empty, MeshCombine.Concat([]));
        Assert.Same(MeshData.Empty, MeshCombine.Concat([MeshData.Empty, MeshData.Empty]));

        var only = MeshCombine.Concat([MeshData.Empty, Tetra(0), MeshData.Empty]);
        Assert.Equal(4, only.VertexCount);
    }

    [Fact]
    public void One_mesh_is_handed_back_as_it_is()
    {
        var mesh = Tetra(5);

        Assert.Same(mesh, MeshCombine.Concat([mesh]));
    }

    [Fact]
    public void The_input_meshes_are_not_changed()
    {
        var a = Tetra(0);
        var indices = a.Indices.ToArray();

        MeshCombine.Concat([a, Tetra(10)]);

        Assert.Equal(indices, a.Indices.ToArray());
        Assert.Equal(4, a.VertexCount);
    }

    [Fact]
    public void Bounds_cover_all_parts()
    {
        var bounds = MeshCombine.Concat([Tetra(-5), Tetra(30)]).ComputeBounds();

        Assert.Equal(-5, bounds.Min.X);
        Assert.Equal(31, bounds.Max.X);
    }
}
