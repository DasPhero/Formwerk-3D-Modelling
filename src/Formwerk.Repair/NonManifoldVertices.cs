using System.Numerics;

namespace Formwerk.Repair;

public static partial class MeshAnalyzer
{
    /// <summary>
    /// Vertices where the surface touches itself without sharing an edge (a bow tie: two bodies that meet in a corner, a sheet pinched
    /// together). The triangles around such a vertex fall into more than one fan when only edges that exactly two triangles share
    /// connect them; an edge with more than two triangles does not count as a connection (it is a problem of its own).
    /// </summary>
    internal static List<int> FindNonManifoldVertices(Vector3[] vertices, int[] indices, bool[] repeated)
    {
        var triangleCount = indices.Length / 3;
        var start = new int[vertices.Length + 1];
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            for (var c = 0; c < 3; c++) start[indices[t * 3 + c] + 1]++;
        }

        for (var v = 0; v < vertices.Length; v++) start[v + 1] += start[v];
        var fill = (int[])start.Clone();
        var around = new int[start[^1]];
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            for (var c = 0; c < 3; c++) around[fill[indices[t * 3 + c]]++] = t;
        }

        var found = new List<int>();
        var byNeighbour = new Dictionary<int, List<int>>();
        for (var v = 0; v < vertices.Length; v++)
        {
            var count = start[v + 1] - start[v];
            if (count < 2) continue;

            byNeighbour.Clear();
            for (var i = start[v]; i < start[v + 1]; i++)
            {
                var t = around[i];
                for (var c = 0; c < 3; c++)
                {
                    var other = indices[t * 3 + c];
                    if (other == v) continue;
                    if (!byNeighbour.TryGetValue(other, out var list)) byNeighbour[other] = list = [];
                    list.Add(i - start[v]);
                }
            }

            var parent = new int[count];
            for (var i = 0; i < count; i++) parent[i] = i;
            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            foreach (var list in byNeighbour.Values)
            {
                if (list.Count != 2) continue; // an open edge or an edge with too many triangles does not connect
                var a = Find(list[0]);
                var b = Find(list[1]);
                if (a != b) parent[b] = a;
            }

            var fans = 0;
            for (var i = 0; i < count; i++)
            {
                if (Find(i) == i) fans++;
            }

            if (fans > 1) found.Add(v);
        }

        return found;
    }
}
