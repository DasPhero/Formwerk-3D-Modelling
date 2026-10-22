using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>Splitting a mesh into its separate shells (F-SPL-03).</summary>
public static class MeshShells
{
    /// <summary>
    /// One mesh per shell: triangles that share a vertex belong together, and vertices at exactly the same place count as one, so
    /// files whose triangles have vertices of their own are not torn apart. A shell that is turned inside out (negative volume, the cavity
    /// of a hollow body) is not a body of its own: it goes with the smallest other shell whose bounds contain it, and only stays a piece of
    /// its own when there is none. The vertices keep their positions and are compacted per piece; the pieces are ordered by the smallest
    /// X, then Y, then Z of their bounds.
    /// </summary>
    public static IReadOnlyList<MeshData> Split(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.IsEmpty) return [];

        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        var triangleCount = indices.Length / 3;

        // Vertices at the same place are the same vertex.
        var place = new Dictionary<Vector3, int>(vertices.Length);
        var canonical = new int[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            if (!place.TryGetValue(vertices[i], out var first)) place[vertices[i]] = first = i;
            canonical[i] = first;
        }

        var parent = new int[vertices.Length];
        for (var i = 0; i < parent.Length; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        for (var t = 0; t < triangleCount; t++)
        {
            var a = Find(canonical[indices[t * 3]]);
            for (var c = 1; c < 3; c++)
            {
                var b = Find(canonical[indices[t * 3 + c]]);
                if (a != b) parent[b] = a;
            }
        }

        var byRoot = new Dictionary<int, List<int>>();
        for (var t = 0; t < triangleCount; t++)
        {
            var root = Find(canonical[indices[t * 3]]);
            if (!byRoot.TryGetValue(root, out var list)) byRoot[root] = list = [];
            list.Add(t);
        }

        var shells = byRoot.Values.Select(triangles => new Shell(triangles, vertices, indices)).ToList();

        // Cavities go to the smallest shell around them.
        var bodies = shells.Where(s => s.Volume >= 0).ToList();
        foreach (var cavity in shells.Where(s => s.Volume < 0).ToList())
        {
            var around = bodies.Where(b => b.Contains(cavity)).OrderBy(b => b.BoundsVolume).FirstOrDefault();
            if (around is null) continue;
            around.Add(cavity);
            shells.Remove(cavity);
        }

        return [.. shells
            .OrderBy(s => s.Min.X).ThenBy(s => s.Min.Y).ThenBy(s => s.Min.Z)
            .Select(s => s.ToMesh(vertices, indices))];
    }

    private sealed class Shell
    {
        private const float Margin = 1e-4f;

        public Shell(List<int> triangles, Vector3[] vertices, int[] indices)
        {
            Triangles = triangles;
            Min = new Vector3(float.MaxValue);
            Max = new Vector3(float.MinValue);
            Measure(triangles, vertices, indices);
        }

        public List<int> Triangles { get; }

        public Vector3 Min { get; private set; }

        public Vector3 Max { get; private set; }

        public double Volume { get; private set; }

        public double BoundsVolume
        {
            get
            {
                var size = Max - Min;
                return (double)size.X * size.Y * size.Z;
            }
        }

        public bool Contains(Shell other) =>
            other.Min.X >= Min.X - Margin && other.Min.Y >= Min.Y - Margin && other.Min.Z >= Min.Z - Margin
            && other.Max.X <= Max.X + Margin && other.Max.Y <= Max.Y + Margin && other.Max.Z <= Max.Z + Margin;

        public void Add(Shell other)
        {
            Triangles.AddRange(other.Triangles);
            Volume += other.Volume;
        }

        public MeshData ToMesh(Vector3[] vertices, int[] indices)
        {
            var map = new Dictionary<int, int>();
            var outVertices = new List<Vector3>();
            var outIndices = new int[Triangles.Count * 3];
            var next = 0;
            foreach (var t in Triangles.Order())
            {
                for (var c = 0; c < 3; c++)
                {
                    var original = indices[t * 3 + c];
                    if (!map.TryGetValue(original, out var mapped))
                    {
                        map[original] = mapped = outVertices.Count;
                        outVertices.Add(vertices[original]);
                    }

                    outIndices[next++] = mapped;
                }
            }

            return new MeshData([.. outVertices], outIndices);
        }

        private void Measure(List<int> triangles, Vector3[] vertices, int[] indices)
        {
            double volume = 0;
            foreach (var t in triangles)
            {
                Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
                Min = Vector3.Min(Min, Vector3.Min(a, Vector3.Min(b, c)));
                Max = Vector3.Max(Max, Vector3.Max(a, Vector3.Max(b, c)));
                // signed volume of the tetrahedron (origin, a, b, c), in double so that bodies far from the origin keep their sign
                double ax = a.X, ay = a.Y, az = a.Z, bx = b.X, by = b.Y, bz = b.Z, cx = c.X, cy = c.Y, cz = c.Z;
                volume += (ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx)) / 6.0;
            }

            Volume = volume;
        }
    }
}
