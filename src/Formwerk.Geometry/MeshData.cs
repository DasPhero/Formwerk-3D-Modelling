using System.Numerics;

namespace Formwerk.Geometry;

public readonly record struct Bounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Size => Max - Min;

    public Vector3 Center => (Min + Max) * 0.5f;
}

/// <summary>
/// Indexed triangle mesh in mm. Triangles are counter-clockwise when seen from outside.
/// Immutable after construction.
/// </summary>
public sealed class MeshData
{
    private readonly Vector3[] _vertices;
    private readonly int[] _indices;

    public MeshData(Vector3[] vertices, int[] indices)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        if (indices.Length % 3 != 0) throw new ArgumentException("The index count must be a multiple of 3.", nameof(indices));
        foreach (var i in indices)
        {
            if ((uint)i >= (uint)vertices.Length) throw new ArgumentException($"Index {i} is outside the vertex array.", nameof(indices));
        }

        _vertices = vertices;
        _indices = indices;
    }

    public static MeshData Empty { get; } = new([], []);

    public ReadOnlySpan<Vector3> Vertices => _vertices;

    public ReadOnlySpan<int> Indices => _indices;

    public int VertexCount => _vertices.Length;

    public int TriangleCount => _indices.Length / 3;

    public bool IsEmpty => _indices.Length == 0;

    public Bounds ComputeBounds()
    {
        if (_vertices.Length == 0) return default;
        var min = _vertices[0];
        var max = _vertices[0];
        foreach (var v in _vertices)
        {
            min = Vector3.Min(min, v);
            max = Vector3.Max(max, v);
        }

        return new Bounds(min, max);
    }

    /// <summary>Enclosed volume in mm³ (positive for outward-facing triangles).</summary>
    public double ComputeVolume()
    {
        double volume = 0;
        for (var t = 0; t < _indices.Length; t += 3)
        {
            var a = ToDouble(_vertices[_indices[t]]);
            var b = ToDouble(_vertices[_indices[t + 1]]);
            var c = ToDouble(_vertices[_indices[t + 2]]);
            // Signed volume of the tetrahedron (origin, a, b, c) = a · (b × c) / 6.
            volume += a.X * (b.Y * c.Z - b.Z * c.Y) - a.Y * (b.X * c.Z - b.Z * c.X) + a.Z * (b.X * c.Y - b.Y * c.X);
        }

        return volume / 6.0;
    }

    public double ComputeSurfaceArea()
    {
        double area = 0;
        for (var t = 0; t < _indices.Length; t += 3)
        {
            var ab = _vertices[_indices[t + 1]] - _vertices[_indices[t]];
            var ac = _vertices[_indices[t + 2]] - _vertices[_indices[t]];
            area += Vector3.Cross(ab, ac).Length() * 0.5;
        }

        return area;
    }

    /// <summary>
    /// True when the mesh is closed, manifold and consistently oriented:
    /// every directed edge occurs exactly once and its reverse exactly once.
    /// </summary>
    public bool IsWatertight()
    {
        if (_indices.Length == 0) return false;

        var directed = new Dictionary<(int, int), int>();
        for (var t = 0; t < _indices.Length; t += 3)
        {
            for (var e = 0; e < 3; e++)
            {
                var from = _indices[t + e];
                var to = _indices[t + (e + 1) % 3];
                if (from == to) return false;
                directed[(from, to)] = directed.GetValueOrDefault((from, to)) + 1;
            }
        }

        foreach (var ((from, to), count) in directed)
        {
            if (count != 1) return false;
            if (!directed.TryGetValue((to, from), out var reverse) || reverse != 1) return false;
        }

        return true;
    }

    public MeshData Transformed(Matrix4x4 matrix)
    {
        var vertices = new Vector3[_vertices.Length];
        for (var i = 0; i < vertices.Length; i++) vertices[i] = Vector3.Transform(_vertices[i], matrix);

        var indices = (int[])_indices.Clone();
        // A mirroring matrix flips the triangle winding.
        if (matrix.GetDeterminant() < 0)
        {
            for (var t = 0; t < indices.Length; t += 3) (indices[t + 1], indices[t + 2]) = (indices[t + 2], indices[t + 1]);
        }

        return new MeshData(vertices, indices);
    }

    private static (double X, double Y, double Z) ToDouble(Vector3 v) => (v.X, v.Y, v.Z);
}
