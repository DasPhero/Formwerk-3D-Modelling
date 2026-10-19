using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Repair;

/// <summary>What the repair did, in numbers the user can read.</summary>
public sealed record RepairActions(
    int WeldedVertices,
    int RemovedDegenerateTriangles,
    int RemovedDuplicateTriangles,
    int FlippedTriangles,
    int FilledHoles,
    int AddedTriangles,
    int UnfilledHoles,
    int FlippedParts,
    int RemovedLooseParts)
{
    public static RepairActions None { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public bool AnythingDone =>
        WeldedVertices + RemovedDegenerateTriangles + RemovedDuplicateTriangles + FlippedTriangles + FilledHoles + FlippedParts + RemovedLooseParts > 0;
}

/// <param name="Mesh">The repaired mesh; the very same instance as the input when there was nothing to do.</param>
/// <param name="Before">The analysis of the input.</param>
/// <param name="After">The analysis of the result: what is still wrong, if anything.</param>
/// <param name="Actions">What was done.</param>
/// <param name="Changed">False when the input was returned as it is.</param>
public sealed record RepairResult(MeshData Mesh, MeshReport Before, MeshReport After, RepairActions Actions, bool Changed);

/// <summary>
/// One-click repair (F-REP-02): welds vertices, removes degenerate and duplicate triangles, makes the orientation consistent,
/// closes holes, turns inside-out pieces around and removes specks. Self-intersections and edges shared by more than two
/// triangles are not repaired; the report of the result says what is left.
/// </summary>
public static class MeshRepair
{
    /// <summary>Holes with more corners than this are closed with a fan around their center instead of by ear clipping.</summary>
    public const int MaxEarClippingCorners = 2000;

    public static RepairResult Repair(MeshData mesh, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var before = MeshAnalyzer.Analyze(mesh);
        if (mesh.IsEmpty || before.Skipped || before.IsHealthy) return new RepairResult(mesh, before, before, RepairActions.None, Changed: false);

        var vertices = new List<Vector3>(mesh.Vertices.ToArray());
        var triangles = mesh.Indices.ToArray();

        var welded = Weld(vertices, triangles);
        cancellationToken.ThrowIfCancellationRequested();

        var cleaned = Clean(vertices, triangles, out var degenerate, out var duplicates);
        cancellationToken.ThrowIfCancellationRequested();

        var flipped = Orient(cleaned, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var all = new List<int>(cleaned);
        var holes = FillHoles(vertices, all, cancellationToken, out var added, out var unfilled);
        cancellationToken.ThrowIfCancellationRequested();

        var flippedParts = 0;
        var removedParts = 0;
        if (all.Count > 0)
        {
            var scan = MeshAnalyzer.Scan(vertices.ToArray(), all.ToArray());
            var inverted = MeshAnalyzer.InvertedPieces(scan).ToHashSet();
            var specks = MeshAnalyzer.TinyParts(scan).ToHashSet();
            flippedParts = inverted.Count;
            removedParts = specks.Count;
            if (inverted.Count + specks.Count > 0)
            {
                var kept = new List<int>(all.Count);
                for (var t = 0; t < all.Count / 3; t++)
                {
                    var root = scan.Pieces.Find(all[t * 3]);
                    if (specks.Contains(root)) continue;
                    if (inverted.Contains(root)) kept.AddRange([all[t * 3], all[t * 3 + 2], all[t * 3 + 1]]);
                    else kept.AddRange([all[t * 3], all[t * 3 + 1], all[t * 3 + 2]]);
                }

                all = kept;
            }
        }

        var result = Compact(vertices, all);
        var actions = new RepairActions(welded, degenerate, duplicates, flipped, holes, added, unfilled, flippedParts, removedParts);
        return new RepairResult(result, before, MeshAnalyzer.Analyze(result), actions, Changed: true);
    }

    // ---- 1. weld ---------------------------------------------------------------------------------

    /// <summary>Merges vertices that lie within the tolerance of an earlier one. Returns how many vertices were merged.</summary>
    private static int Weld(List<Vector3> vertices, int[] triangles)
    {
        var array = vertices.ToArray();
        var used = new bool[array.Length];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var index in triangles)
        {
            used[index] = true;
            min = Vector3.Min(min, array[index]);
            max = Vector3.Max(max, array[index]);
        }

        var map = VertexWelder.Map(array, used, VertexWelder.Tolerance(min, max));
        var merged = 0;
        for (var v = 0; v < map.Length; v++)
        {
            if (used[v] && map[v] != v) merged++;
        }

        for (var i = 0; i < triangles.Length; i++) triangles[i] = map[triangles[i]];
        return merged;
    }

    // ---- 2. degenerate and duplicate triangles ---------------------------------------------------

    private static int[] Clean(List<Vector3> vertices, int[] triangles, out int degenerate, out int duplicates)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var index in triangles)
        {
            min = Vector3.Min(min, vertices[index]);
            max = Vector3.Max(max, vertices[index]);
        }

        var diagonal = (double)(max - min).Length();
        var minArea = 1e-10 * diagonal * diagonal;

        degenerate = 0;
        var kept = new List<int>(triangles.Length);
        for (var t = 0; t < triangles.Length / 3; t++)
        {
            int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
            if (a == b || b == c || a == c || MeshAnalyzer.Area(vertices[a], vertices[b], vertices[c]) < minArea)
            {
                degenerate++;
                continue;
            }

            kept.AddRange([a, b, c]);
        }

        // The same three vertices more than once: keep one copy, or none when it is there in both directions (a wall without thickness).
        duplicates = 0;
        var count = kept.Count / 3;
        var first = new Dictionary<(int, int, int), int>();
        List<int>? groupsToCheck = null;
        var seenTwice = new HashSet<(int, int, int)>();
        for (var t = 0; t < count; t++)
        {
            var key = MeshAnalyzer.SortedTriple(kept[t * 3], kept[t * 3 + 1], kept[t * 3 + 2]);
            if (!first.TryAdd(key, t)) seenTwice.Add(key);
        }

        if (seenTwice.Count == 0) return [.. kept];

        groupsToCheck = [];
        var groups = new Dictionary<(int, int, int), List<int>>();
        for (var t = 0; t < count; t++)
        {
            var key = MeshAnalyzer.SortedTriple(kept[t * 3], kept[t * 3 + 1], kept[t * 3 + 2]);
            if (!seenTwice.Contains(key)) continue;
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = [];
            list.Add(t);
        }

        var removed = new bool[count];
        foreach (var members in groups.Values)
        {
            var reference = members[0];
            var same = members.Where(m => SameWinding(kept, m, reference)).ToList();
            var opposite = members.Where(m => !SameWinding(kept, m, reference)).ToList();
            List<int> keep;
            if (same.Count == opposite.Count) keep = [];
            else keep = [same.Count > opposite.Count ? same[0] : opposite[0]];

            foreach (var m in members)
            {
                if (keep.Contains(m)) continue;
                removed[m] = true;
                duplicates++;
            }
        }

        var result = new List<int>(kept.Count);
        for (var t = 0; t < count; t++)
        {
            if (!removed[t]) result.AddRange([kept[t * 3], kept[t * 3 + 1], kept[t * 3 + 2]]);
        }

        return [.. result];
    }

    private static bool SameWinding(List<int> triangles, int t, int reference)
    {
        int a = triangles[t * 3], b = triangles[t * 3 + 1];
        int ra = triangles[reference * 3], rb = triangles[reference * 3 + 1], rc = triangles[reference * 3 + 2];
        return (a == ra && b == rb) || (a == rb && b == rc) || (a == rc && b == ra);
    }

    // ---- 3. orientation --------------------------------------------------------------------------

    /// <summary>
    /// Turns triangles around so that neighbours agree about the direction of the edge they share. Works piece by piece from a
    /// seed triangle and flips the smaller half, so a single wrong triangle is the only one that moves. Returns how many triangles were turned.
    /// </summary>
    private static int Orient(int[] triangles, CancellationToken cancellationToken)
    {
        var count = triangles.Length / 3;
        var edges = EdgeTable.Build(triangles);

        // neighbours over edges that exactly two triangles share
        var degree = new int[count + 1];
        for (var i = 0; i < edges.Count;)
        {
            var length = edges.GroupLength(i);
            if (length == 2)
            {
                degree[EdgeTable.TriangleOf(edges.Payload[i])]++;
                degree[EdgeTable.TriangleOf(edges.Payload[i + 1])]++;
            }

            i += length;
        }

        var start = new int[count + 1];
        for (var t = 0; t < count; t++) start[t + 1] = start[t] + degree[t];
        var neighbour = new int[start[count]];
        var disagree = new bool[start[count]];
        var fill = new int[count];
        for (var i = 0; i < edges.Count;)
        {
            var length = edges.GroupLength(i);
            if (length == 2)
            {
                int p = edges.Payload[i], q = edges.Payload[i + 1];
                int tp = EdgeTable.TriangleOf(p), tq = EdgeTable.TriangleOf(q);
                var same = EdgeTable.RunsUp(p) == EdgeTable.RunsUp(q);
                neighbour[start[tp] + fill[tp]] = tq;
                disagree[start[tp] + fill[tp]++] = same;
                neighbour[start[tq] + fill[tq]] = tp;
                disagree[start[tq] + fill[tq]++] = same;
            }

            i += length;
        }

        var visited = new bool[count];
        var flip = new bool[count];
        var queue = new int[count];
        var flippedTotal = 0;
        for (var seed = 0; seed < count; seed++)
        {
            if (visited[seed]) continue;
            if ((seed & 0xFFFF) == 0) cancellationToken.ThrowIfCancellationRequested();

            var head = 0;
            var tail = 0;
            queue[tail++] = seed;
            visited[seed] = true;
            var flips = 0;
            while (head < tail)
            {
                var u = queue[head++];
                for (var k = start[u]; k < start[u + 1]; k++)
                {
                    var v = neighbour[k];
                    if (visited[v]) continue;
                    visited[v] = true;
                    flip[v] = disagree[k] ? !flip[u] : flip[u];
                    if (flip[v]) flips++;
                    queue[tail++] = v;
                }
            }

            // flip the smaller half: either all flagged ones or all the others
            var size = tail;
            if (flips * 2 > size)
            {
                for (var q = 0; q < size; q++) flip[queue[q]] = !flip[queue[q]];
                flips = size - flips;
            }

            flippedTotal += flips;
        }

        for (var t = 0; t < count; t++)
        {
            if (flip[t]) (triangles[t * 3 + 1], triangles[t * 3 + 2]) = (triangles[t * 3 + 2], triangles[t * 3 + 1]);
        }

        return flippedTotal;
    }

    // ---- 4. holes --------------------------------------------------------------------------------

    private static int FillHoles(List<Vector3> vertices, List<int> triangles, CancellationToken cancellationToken, out int added, out int unfilled)
    {
        added = 0;
        unfilled = 0;
        var edges = EdgeTable.Build([.. triangles]);

        // A triangle has the edge u -> v and no neighbour has v -> u: the new triangle that closes the hole needs v -> u.
        var outgoing = new Dictionary<int, List<int>>();
        for (var i = 0; i < edges.Count;)
        {
            var length = edges.GroupLength(i);
            if (length == 1)
            {
                var low = EdgeTable.Low(edges.Keys[i]);
                var high = EdgeTable.High(edges.Keys[i]);
                var runsUp = EdgeTable.RunsUp(edges.Payload[i]);
                int from = runsUp ? low : high, to = runsUp ? high : low;
                if (!outgoing.TryGetValue(to, out var list)) outgoing[to] = list = [];
                list.Add(from);
            }

            i += length;
        }

        var filled = 0;
        var loops = TraceLoops(outgoing, out var deadEnds);
        foreach (var loop in loops)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = triangles.Count;
            if (loop.Length >= 3 && Triangulate(loop, vertices, triangles))
            {
                filled++;
                added += (triangles.Count - before) / 3;
            }
            else
            {
                unfilled++;
            }
        }

        unfilled += deadEnds;
        return filled;
    }

    /// <summary>
    /// Follows the reversed rim edges into closed loops. Where rims touch in a corner a loop is cut out as soon as a vertex
    /// repeats, so every loop is a simple polygon. Rim pieces that do not close (next to non-manifold edges) are counted as dead ends.
    /// </summary>
    private static List<int[]> TraceLoops(Dictionary<int, List<int>> outgoing, out int deadEnds)
    {
        deadEnds = 0;
        var loops = new List<int[]>();
        foreach (var startVertex in outgoing.Keys.ToList())
        {
            var path = new List<int> { startVertex };
            var position = new Dictionary<int, int> { [startVertex] = 0 };
            while (path.Count > 0)
            {
                var current = path[^1];
                if (!outgoing.TryGetValue(current, out var next) || next.Count == 0)
                {
                    if (path.Count > 1) deadEnds++;
                    break; // nothing leaves this vertex: abandon the rest of the path
                }

                var target = next[^1];
                next.RemoveAt(next.Count - 1);
                if (position.TryGetValue(target, out var at))
                {
                    var loop = path.GetRange(at, path.Count - at).ToArray();
                    loops.Add(loop);
                    for (var i = at + 1; i < path.Count; i++) position.Remove(path[i]);
                    path.RemoveRange(at + 1, path.Count - at - 1);
                }
                else
                {
                    position[target] = path.Count;
                    path.Add(target);
                }
            }
        }

        return loops;
    }

    /// <summary>
    /// Closes a loop with triangles that run along it in the same direction (so they agree with the surroundings): ear clipping in the
    /// best-fit plane, or a fan around the center for very large loops and loops without a plane.
    /// </summary>
    private static bool Triangulate(int[] loop, List<Vector3> vertices, List<int> output)
    {
        if (loop.Length == 3)
        {
            output.AddRange(loop);
            return true;
        }

        var points = loop.Select(i => vertices[i]).ToArray();
        var normal = NewellNormal(points);
        if (loop.Length > MaxEarClippingCorners || normal.LengthSquared() < 1e-24)
        {
            Fan(loop, points, vertices, output);
            return true;
        }

        normal = Vector3.Normalize(normal);
        var axis = Math.Abs(normal.X) <= Math.Abs(normal.Y) && Math.Abs(normal.X) <= Math.Abs(normal.Z) ? Vector3.UnitX
            : Math.Abs(normal.Y) <= Math.Abs(normal.Z) ? Vector3.UnitY : Vector3.UnitZ;
        var e1 = Vector3.Normalize(Vector3.Cross(normal, axis));
        var e2 = Vector3.Cross(normal, e1);
        var m = loop.Length;
        var x = new double[m];
        var y = new double[m];
        for (var i = 0; i < m; i++)
        {
            x[i] = Vector3.Dot(points[i], e1);
            y[i] = Vector3.Dot(points[i], e2);
        }

        var scale = 0.0;
        for (var i = 0; i < m; i++) scale = Math.Max(scale, Math.Max(Math.Abs(x[i]), Math.Abs(y[i])));
        var tiny = 1e-12 * Math.Max(scale * scale, 1e-12);

        var next = new int[m];
        var prev = new int[m];
        for (var i = 0; i < m; i++)
        {
            next[i] = (i + 1) % m;
            prev[i] = (i + m - 1) % m;
        }

        bool Convex(int v) => Cross(x, y, prev[v], v, next[v]) > tiny;

        bool IsEar(int v, int remaining)
        {
            if (!Convex(v)) return false;
            int a = prev[v], c = next[v];
            for (var other = next[c]; other != a; other = next[other])
            {
                if (InsideOrOn(x, y, a, v, c, other, tiny)) return false;
            }

            return true;
        }

        var left = m;
        var current = 0;
        while (left > 3)
        {
            var ear = -1;
            var candidate = current;
            for (var k = 0; k < left; k++, candidate = next[candidate])
            {
                if (IsEar(candidate, left))
                {
                    ear = candidate;
                    break;
                }
            }

            if (ear < 0)
            {
                // no clean ear (self-touching or almost flat loop): take the most convex corner so that the loop still closes
                var best = double.MinValue;
                candidate = current;
                for (var k = 0; k < left; k++, candidate = next[candidate])
                {
                    var c = Cross(x, y, prev[candidate], candidate, next[candidate]);
                    if (c > best)
                    {
                        best = c;
                        ear = candidate;
                    }
                }
            }

            output.AddRange([loop[prev[ear]], loop[ear], loop[next[ear]]]);
            next[prev[ear]] = next[ear];
            prev[next[ear]] = prev[ear];
            current = next[ear];
            left--;
        }

        output.AddRange([loop[prev[current]], loop[current], loop[next[current]]]);
        return true;
    }

    private static void Fan(int[] loop, Vector3[] points, List<Vector3> vertices, List<int> output)
    {
        var center = Vector3.Zero;
        foreach (var p in points) center += p;
        center /= points.Length;
        var centerIndex = vertices.Count;
        vertices.Add(center);
        for (var i = 0; i < loop.Length; i++) output.AddRange([loop[i], loop[(i + 1) % loop.Length], centerIndex]);
    }

    private static Vector3 NewellNormal(Vector3[] points)
    {
        double nx = 0, ny = 0, nz = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var p = points[i];
            var q = points[(i + 1) % points.Length];
            nx += ((double)p.Y - q.Y) * ((double)p.Z + q.Z);
            ny += ((double)p.Z - q.Z) * ((double)p.X + q.X);
            nz += ((double)p.X - q.X) * ((double)p.Y + q.Y);
        }

        return new Vector3((float)nx, (float)ny, (float)nz);
    }

    private static double Cross(double[] x, double[] y, int a, int b, int c) =>
        (x[b] - x[a]) * (y[c] - y[b]) - (y[b] - y[a]) * (x[c] - x[b]);

    /// <summary>Is point p inside the triangle abc (counter-clockwise) or on its border? Points at the same place as a corner do not count.</summary>
    private static bool InsideOrOn(double[] x, double[] y, int a, int b, int c, int p, double tiny)
    {
        if (Same(x, y, p, a) || Same(x, y, p, b) || Same(x, y, p, c)) return false;
        var d1 = (x[b] - x[a]) * (y[p] - y[a]) - (y[b] - y[a]) * (x[p] - x[a]);
        var d2 = (x[c] - x[b]) * (y[p] - y[b]) - (y[c] - y[b]) * (x[p] - x[b]);
        var d3 = (x[a] - x[c]) * (y[p] - y[c]) - (y[a] - y[c]) * (x[p] - x[c]);
        return d1 >= -tiny && d2 >= -tiny && d3 >= -tiny;
    }

    private static bool Same(double[] x, double[] y, int i, int j) => x[i] == x[j] && y[i] == y[j];

    // ---- 7. compact ------------------------------------------------------------------------------

    private static MeshData Compact(List<Vector3> vertices, List<int> triangles)
    {
        if (triangles.Count == 0) return MeshData.Empty;

        var map = new int[vertices.Count];
        Array.Fill(map, -1);
        var compact = new List<Vector3>();
        var indices = new int[triangles.Count];
        for (var i = 0; i < triangles.Count; i++)
        {
            var old = triangles[i];
            if (map[old] < 0)
            {
                map[old] = compact.Count;
                compact.Add(vertices[old]);
            }

            indices[i] = map[old];
        }

        return new MeshData([.. compact], indices);
    }
}
