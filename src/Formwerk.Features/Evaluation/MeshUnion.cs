using Formwerk.Geometry;

namespace Formwerk.Features.Evaluation;

/// <summary>
/// Uniting many meshes quickly (F-BOO-03, Q-03). Meshes whose bounding boxes do not touch cannot meet, so they are simply put
/// together; only groups of meshes that overlap or touch (through a chain of such pairs) go to the boolean engine, one call per group.
/// A plate with 50 holes needs no union at all, while the engine would take most of a second for it.
/// </summary>
public static class MeshUnion
{
    /// <summary>Boxes closer than this (mm) count as touching, so that a shared face or a tiny overlap is always left to the engine.</summary>
    private const float Touch = 0.001f;

    /// <summary>
    /// The union of <paramref name="meshes"/>: empty meshes are ignored, a single mesh comes back as it is. <paramref name="uniteOverlapping"/>
    /// is called once for every group of two or more meshes that belong together, and may throw (the caller handles it).
    /// </summary>
    public static MeshData Compute(IReadOnlyList<MeshData> meshes, Func<IReadOnlyList<MeshData>, MeshData> uniteOverlapping)
    {
        ArgumentNullException.ThrowIfNull(meshes);
        ArgumentNullException.ThrowIfNull(uniteOverlapping);

        var pieces = meshes.Where(m => !m.IsEmpty).ToList();
        if (pieces.Count == 0) return MeshData.Empty;
        if (pieces.Count == 1) return pieces[0];

        var results = new List<MeshData>();
        foreach (var group in Partition(pieces))
            results.Add(group.Count == 1 ? pieces[group[0]] : uniteOverlapping([.. group.Select(i => pieces[i])]));
        return MeshCombine.Concat(results);
    }

    /// <summary>
    /// The indices of <paramref name="meshes"/> grouped by whether their boxes overlap or touch, also through other meshes. Groups are
    /// ordered by their first member, members by index.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<int>> Partition(IReadOnlyList<MeshData> meshes)
    {
        var count = meshes.Count;
        var boxes = meshes.Select(m => m.ComputeBounds()).ToArray();
        var parent = Enumerable.Range(0, count).ToArray();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        // sweep along X: only boxes whose X ranges meet are compared in Y and Z
        var order = Enumerable.Range(0, count).OrderBy(i => boxes[i].Min.X).ToArray();
        for (var a = 0; a < count; a++)
        {
            var i = order[a];
            for (var b = a + 1; b < count; b++)
            {
                var j = order[b];
                if (boxes[j].Min.X > boxes[i].Max.X + Touch) break;
                if (boxes[j].Min.Y > boxes[i].Max.Y + Touch || boxes[i].Min.Y > boxes[j].Max.Y + Touch) continue;
                if (boxes[j].Min.Z > boxes[i].Max.Z + Touch || boxes[i].Min.Z > boxes[j].Max.Z + Touch) continue;
                parent[Find(i)] = Find(j);
            }
        }

        var groups = new Dictionary<int, List<int>>();
        var firstSeen = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                groups[root] = list = [];
                firstSeen.Add(root);
            }

            list.Add(i);
        }

        return [.. firstSeen.Select(root => (IReadOnlyList<int>)groups[root])];
    }
}
