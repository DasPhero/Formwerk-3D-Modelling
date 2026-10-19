using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Tests.Geometry;

public class MeshStoreTests
{
    private static MeshData Tetra(float size = 1) => new(
        [new(0, 0, 0), new(size, 0, 0), new(0, size, 0), new(0, 0, size)],
        [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);

    [Fact]
    public void Added_meshes_can_be_found_again()
    {
        var store = new MeshStore();
        var id = Guid.NewGuid();
        var mesh = Tetra();

        store.Add(id, mesh);

        Assert.True(store.TryGet(id, out var found));
        Assert.Same(mesh, found);
        Assert.Same(mesh, store.Get(id));
        Assert.True(store.Contains(id));
        Assert.Equal(1, store.Count);
        Assert.Equal([id], store.Ids);
    }

    [Fact]
    public void Unknown_ids_are_reported_not_thrown()
    {
        var store = new MeshStore();
        Assert.False(store.TryGet(Guid.NewGuid(), out var mesh));
        Assert.True(mesh.IsEmpty);
        Assert.Null(store.Get(Guid.NewGuid()));
    }

    [Fact]
    public void The_same_mesh_may_be_added_twice_but_a_different_one_may_not_take_its_id()
    {
        var store = new MeshStore();
        var id = Guid.NewGuid();
        var mesh = Tetra();
        store.Add(id, mesh);

        store.Add(id, mesh); // same instance: fine
        Assert.Throws<InvalidOperationException>(() => store.Add(id, Tetra(2)));
    }

    [Fact]
    public void ReplaceWith_copies_the_content_and_Clear_empties_the_store()
    {
        var a = new MeshStore();
        var b = new MeshStore();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        a.Add(idA, Tetra());
        b.Add(idB, Tetra(3));

        a.ReplaceWith(b);

        Assert.False(a.Contains(idA));
        Assert.True(a.Contains(idB));
        a.Clear();
        Assert.Equal(0, a.Count);
        Assert.Equal(1, b.Count); // the source is untouched
    }

    [Fact]
    public async Task The_store_can_be_read_while_another_thread_adds()
    {
        var store = new MeshStore();
        var ids = Enumerable.Range(0, 500).Select(_ => Guid.NewGuid()).ToList();
        var writer = Task.Run(() => { foreach (var id in ids) store.Add(id, Tetra()); });
        var reader = Task.Run(() =>
        {
            for (var i = 0; i < 20000; i++) store.TryGet(ids[i % ids.Count], out _);
        });

        await Task.WhenAll(writer, reader);

        Assert.Equal(500, store.Count);
    }

    // ---- placement ---------------------------------------------------------------------------------

    [Fact]
    public void Placing_on_the_plate_centers_x_and_y_and_puts_the_lowest_point_at_zero()
    {
        var mesh = Tetra(10).Transformed(Matrix4x4.CreateTranslation(100, -50, 7));

        var placed = MeshPlacement.PlaceOnPlate(mesh);

        var b = placed.ComputeBounds();
        Assert.Equal(0, b.Center.X, 4);
        Assert.Equal(0, b.Center.Y, 4);
        Assert.Equal(0, b.Min.Z, 4);
        Assert.Equal(mesh.ComputeVolume(), placed.ComputeVolume(), 3);
        Assert.True(placed.IsWatertight());
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25.4)]
    [InlineData(0.001)]
    public void Scaling_multiplies_the_size_and_the_volume_by_the_cube_of_the_factor(double factor)
    {
        var mesh = Tetra(2);

        var scaled = MeshPlacement.Scaled(mesh, factor);

        Assert.InRange(scaled.ComputeBounds().Size.X, mesh.ComputeBounds().Size.X * factor * (1 - 1e-5), mesh.ComputeBounds().Size.X * factor * (1 + 1e-5));
        var expected = mesh.ComputeVolume() * factor * factor * factor;
        Assert.InRange(scaled.ComputeVolume(), expected * (1 - 1e-5), expected * (1 + 1e-5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    [InlineData(double.NaN)]
    public void Invalid_scale_factors_are_rejected(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MeshPlacement.Scaled(Tetra(), factor));
    }

    [Fact]
    public void The_largest_extent_is_the_longest_side_of_the_bounding_box()
    {
        var mesh = new MeshData([new(0, 0, 0), new(5, 0, 0), new(0, 30, 0), new(0, 0, 12)], [0, 2, 1, 0, 1, 3, 0, 3, 2, 1, 2, 3]);
        Assert.Equal(30, MeshPlacement.LargestExtent(mesh), 4);
        Assert.Equal(0, MeshPlacement.LargestExtent(MeshData.Empty));
    }

    [Fact]
    public void Placing_an_empty_mesh_does_nothing()
    {
        Assert.True(MeshPlacement.PlaceOnPlate(MeshData.Empty).IsEmpty);
    }
}
