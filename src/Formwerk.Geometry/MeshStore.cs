using System.Collections.Concurrent;
using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>
/// Imported and flattened meshes, by id (<c>MeshNode.MeshId</c>). Meshes are never changed after they were added,
/// so the store only grows; undo and redo of adding or deleting an object need no special handling.
/// Safe to read from the evaluation thread while the user thread adds meshes.
/// </summary>
public sealed class MeshStore
{
    private readonly ConcurrentDictionary<Guid, MeshData> _meshes = new();

    public int Count => _meshes.Count;

    public IReadOnlyCollection<Guid> Ids => [.. _meshes.Keys];

    public bool TryGet(Guid id, out MeshData mesh)
    {
        if (_meshes.TryGetValue(id, out var found))
        {
            mesh = found;
            return true;
        }

        mesh = MeshData.Empty;
        return false;
    }

    public MeshData? Get(Guid id) => _meshes.GetValueOrDefault(id);

    public bool Contains(Guid id) => _meshes.ContainsKey(id);

    /// <exception cref="InvalidOperationException">The id is already used for a different mesh.</exception>
    public void Add(Guid id, MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (!_meshes.TryAdd(id, mesh) && !ReferenceEquals(_meshes[id], mesh))
            throw new InvalidOperationException("A different mesh is already stored under this id.");
    }

    public void Clear() => _meshes.Clear();

    /// <summary>Replaces the content by that of <paramref name="other"/> (used when a project is opened).</summary>
    public void ReplaceWith(MeshStore other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (ReferenceEquals(other, this)) return;
        _meshes.Clear();
        foreach (var (id, mesh) in other._meshes) _meshes[id] = mesh;
    }
}

/// <summary>Preparing an imported mesh: unit conversion and placement on the build plate (F-IMP-02, F-FLR-01).</summary>
public static class MeshPlacement
{
    /// <summary>Multiplies all coordinates, for example by 10 for centimetres or 25.4 for inches.</summary>
    public static MeshData Scaled(MeshData mesh, double factor)
    {
        if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
        return mesh.Transformed(Matrix4x4.CreateScale((float)factor));
    }

    /// <summary>
    /// Moves the mesh so that its origin is the center of its bottom face: the middle of the bounding box in X and Y,
    /// the lowest point at Z = 0. This is where the generated shapes have their origin, too, so every object behaves alike.
    /// </summary>
    public static MeshData PlaceOnPlate(MeshData mesh)
    {
        if (mesh.IsEmpty) return mesh;
        var bounds = mesh.ComputeBounds();
        var center = bounds.Center;
        return mesh.Transformed(Matrix4x4.CreateTranslation(-center.X, -center.Y, -bounds.Min.Z));
    }

    /// <summary>The largest side of the bounding box in mm; the unit question compares it with 1 mm and 1000 mm.</summary>
    public static double LargestExtent(MeshData mesh)
    {
        if (mesh.IsEmpty) return 0;
        var size = mesh.ComputeBounds().Size;
        return Math.Max(size.X, Math.Max(size.Y, size.Z));
    }
}
