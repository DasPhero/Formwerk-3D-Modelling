using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

public class MeshDataTests
{
    // Unit tetrahedron with outward-facing triangles; volume = 1/6.
    private static MeshData Tetrahedron() => new(
        [new(0, 0, 0), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1)],
        [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);

    [Fact]
    public void Tetrahedron_has_expected_volume_and_is_watertight()
    {
        var mesh = Tetrahedron();
        Assert.Equal(1.0 / 6.0, mesh.ComputeVolume(), 6);
        Assert.True(mesh.IsWatertight());
        Assert.Equal(4, mesh.TriangleCount);
    }

    [Fact]
    public void Open_mesh_is_not_watertight()
    {
        var mesh = new MeshData(Tetrahedron().Vertices.ToArray(), [0, 2, 1, 0, 1, 3, 0, 3, 2]);
        Assert.False(mesh.IsWatertight());
    }

    [Fact]
    public void Inconsistent_winding_is_not_watertight()
    {
        var mesh = new MeshData(Tetrahedron().Vertices.ToArray(), [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 3, 2]);
        Assert.False(mesh.IsWatertight());
    }

    [Fact]
    public void Empty_mesh_is_not_watertight_and_has_no_volume()
    {
        Assert.False(MeshData.Empty.IsWatertight());
        Assert.Equal(0, MeshData.Empty.ComputeVolume());
    }

    [Fact]
    public void Constructor_rejects_invalid_input()
    {
        Assert.Throws<ArgumentException>(() => new MeshData([Vector3.Zero], [0, 0]));
        Assert.Throws<ArgumentException>(() => new MeshData([Vector3.Zero], [0, 0, 5]));
    }

    [Fact]
    public void Bounds_cover_all_vertices()
    {
        var b = Tetrahedron().ComputeBounds();
        Assert.Equal(Vector3.Zero, b.Min);
        Assert.Equal(Vector3.One, b.Max);
    }

    [Fact]
    public void Translation_and_rotation_keep_the_volume()
    {
        var mesh = Tetrahedron();
        var m = Matrix4x4.CreateFromYawPitchRoll(0.3f, 1.1f, -0.7f) * Matrix4x4.CreateTranslation(10, -5, 3);
        var moved = mesh.Transformed(m);
        Assert.Equal(mesh.ComputeVolume(), moved.ComputeVolume(), 5);
        Assert.True(moved.IsWatertight());
    }

    [Fact]
    public void Mirroring_flips_winding_so_the_volume_stays_positive()
    {
        var mirrored = Tetrahedron().Transformed(Matrix4x4.CreateScale(-1, 1, 1));
        Assert.Equal(1.0 / 6.0, mirrored.ComputeVolume(), 6);
        Assert.True(mirrored.IsWatertight());
    }
}
