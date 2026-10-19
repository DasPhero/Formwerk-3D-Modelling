namespace Formwerk.Geometry;

/// <summary>Putting meshes together (flatten, F-GRP-04).</summary>
public static class MeshCombine
{
    /// <summary>
    /// One mesh with the vertices and triangles of all given meshes. The pieces stay separate shells: nothing is united or cut.
    /// Empty meshes are skipped; a single mesh is returned as it is; no mesh at all gives <see cref="MeshData.Empty"/>.
    /// </summary>
    public static MeshData Concat(IEnumerable<MeshData> meshes)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        var parts = meshes.Where(m => !m.IsEmpty).ToList();
        if (parts.Count == 0) return MeshData.Empty;
        if (parts.Count == 1) return parts[0];

        var vertexTotal = parts.Sum(m => (long)m.VertexCount);
        var indexTotal = parts.Sum(m => (long)m.Indices.Length);
        if (vertexTotal > int.MaxValue || indexTotal > int.MaxValue) throw new InvalidOperationException("The meshes are too large to be put together.");

        var vertices = new System.Numerics.Vector3[vertexTotal];
        var indices = new int[indexTotal];
        var vertexOffset = 0;
        var indexOffset = 0;
        foreach (var mesh in parts)
        {
            mesh.Vertices.CopyTo(vertices.AsSpan(vertexOffset));
            var source = mesh.Indices;
            for (var i = 0; i < source.Length; i++) indices[indexOffset + i] = source[i] + vertexOffset;
            vertexOffset += mesh.VertexCount;
            indexOffset += source.Length;
        }

        return new MeshData(vertices, indices);
    }
}
