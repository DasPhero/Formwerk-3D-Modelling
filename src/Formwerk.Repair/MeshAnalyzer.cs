using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Repair;

/// <summary>Checks a mesh for the defects that keep it from being printed or used in boolean operations (F-REP-01).</summary>
public static class MeshAnalyzer
{
    /// <summary>Larger meshes are not analysed (the edge table would need too much memory); the report says so.</summary>
    public const int MaxTriangles = 3_000_000;

    /// <summary>Loose pieces up to this many triangles can count as specks.</summary>
    internal const int TinyPartTriangles = 100;

    /// <summary>Loose pieces whose size is below this fraction of the whole model can count as specks.</summary>
    internal const double TinyPartSizeFraction = 0.01;

    public static MeshReport Analyze(MeshData mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        var triangleCount = mesh.TriangleCount;
        if (triangleCount == 0) return new MeshReport(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        if (triangleCount > MaxTriangles) return new MeshReport(0, triangleCount, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Skipped: true);

        var scan = Scan(mesh.Vertices.ToArray(), mesh.Indices.ToArray());
        return new MeshReport(scan.UsedCount, triangleCount, scan.OpenEdges, scan.OpenLoops, scan.NonManifoldEdges, scan.InconsistentEdges,
            scan.Degenerate, scan.Duplicates, scan.DuplicateVertices, scan.LooseParts, TinyParts(scan).Count, InvertedPieces(scan).Count);
    }

    /// <summary>Everything the analysis and the repair need to know about a triangle list.</summary>
    internal sealed class MeshScan
    {
        public required Vector3[] Vertices { get; init; }

        public required int[] Indices { get; init; }

        public required bool[] Used { get; init; }

        public required int UsedCount { get; init; }

        public required Vector3 Min { get; init; }

        public required Vector3 Max { get; init; }

        public double Diagonal => (Max - Min).Length();

        /// <summary>Triangles that use a vertex twice.</summary>
        public required bool[] Repeated { get; init; }

        /// <summary>Repeated vertices or no area.</summary>
        public required int Degenerate { get; init; }

        public required int Duplicates { get; init; }

        public required int OpenEdges { get; init; }

        public required int OpenLoops { get; init; }

        public required int NonManifoldEdges { get; init; }

        public required int InconsistentEdges { get; init; }

        public required int DuplicateVertices { get; init; }

        /// <summary>Vertices that triangles share belong to one piece; this finds the piece (its smallest vertex index).</summary>
        public required UnionFind Pieces { get; init; }

        /// <summary>Per piece root: has open, non-manifold or disagreeing edges.</summary>
        public required bool[] Damaged { get; init; }

        public required int[] PieceTriangles { get; init; }

        public required double[] PieceVolume { get; init; }

        public required Vector3[] PieceMin { get; init; }

        public required Vector3[] PieceMax { get; init; }

        public required List<int> Roots { get; init; }

        public int LooseParts => Math.Max(Roots.Count - 1, 0);
    }

    internal static MeshScan Scan(Vector3[] vertices, int[] indices)
    {
        var triangleCount = indices.Length / 3;
        var vertexCount = vertices.Length;

        // Which vertices are used, and how big is the model.
        var used = new bool[vertexCount];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var usedCount = 0;
        foreach (var index in indices)
        {
            if (used[index]) continue;
            used[index] = true;
            usedCount++;
            min = Vector3.Min(min, vertices[index]);
            max = Vector3.Max(max, vertices[index]);
        }

        var diagonal = (double)(max - min).Length();
        var minArea = 1e-10 * diagonal * diagonal;

        // Degenerate triangles: a repeated vertex (also left out of the edge table) or no area.
        var repeated = new bool[triangleCount];
        var degenerate = 0;
        for (var t = 0; t < triangleCount; t++)
        {
            int a = indices[t * 3], b = indices[t * 3 + 1], c = indices[t * 3 + 2];
            repeated[t] = a == b || b == c || a == c;
            if (repeated[t] || Area(vertices[a], vertices[b], vertices[c]) < minArea) degenerate++;
        }

        // Pieces: vertices that share a triangle belong together.
        var pieces = new UnionFind(vertexCount);
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            pieces.Union(indices[t * 3], indices[t * 3 + 1]);
            pieces.Union(indices[t * 3 + 1], indices[t * 3 + 2]);
        }

        // Edges: open rims, edges shared by too many triangles, neighbours that disagree about the direction.
        var edges = EdgeTable.Build(indices, repeated);
        var damaged = new bool[vertexCount];
        var rims = new UnionFind(vertexCount);
        var rimVertices = new List<int>();
        int openEdges = 0, nonManifold = 0, inconsistent = 0;
        for (var i = 0; i < edges.Count;)
        {
            var length = edges.GroupLength(i);
            var low = EdgeTable.Low(edges.Keys[i]);
            var high = EdgeTable.High(edges.Keys[i]);
            if (length == 1)
            {
                openEdges++;
                rims.Union(low, high);
                rimVertices.Add(low);
                damaged[pieces.Find(low)] = true;
            }
            else if (length == 2)
            {
                if (EdgeTable.RunsUp(edges.Payload[i]) == EdgeTable.RunsUp(edges.Payload[i + 1]))
                {
                    inconsistent++;
                    damaged[pieces.Find(low)] = true;
                }
            }
            else
            {
                nonManifold++;
                damaged[pieces.Find(low)] = true;
            }

            i += length;
        }

        var openLoops = rimVertices.Select(rims.Find).Distinct().Count();

        // Duplicate triangles: the same three vertices again, in any order.
        var seen = new HashSet<(int, int, int)>();
        var duplicates = 0;
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            if (!seen.Add(SortedTriple(indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]))) duplicates++;
        }

        // Pieces: size, bounds and volume.
        var pieceTriangles = new int[vertexCount];
        var pieceVolume = new double[vertexCount];
        var pieceMin = new Vector3[vertexCount];
        var pieceMax = new Vector3[vertexCount];
        for (var t = 0; t < triangleCount; t++)
        {
            if (repeated[t]) continue;
            var a = vertices[indices[t * 3]];
            var b = vertices[indices[t * 3 + 1]];
            var c = vertices[indices[t * 3 + 2]];
            var root = pieces.Find(indices[t * 3]);
            var lo = Vector3.Min(a, Vector3.Min(b, c));
            var hi = Vector3.Max(a, Vector3.Max(b, c));
            if (pieceTriangles[root]++ == 0)
            {
                pieceMin[root] = lo;
                pieceMax[root] = hi;
            }
            else
            {
                pieceMin[root] = Vector3.Min(pieceMin[root], lo);
                pieceMax[root] = Vector3.Max(pieceMax[root], hi);
            }

            pieceVolume[root] += SignedVolume(a, b, c);
        }

        var welded = VertexWelder.Map(vertices, used, VertexWelder.Tolerance(min, max));
        var duplicateVertices = 0;
        for (var v = 0; v < vertexCount; v++)
        {
            if (used[v] && welded[v] != v) duplicateVertices++;
        }

        return new MeshScan
        {
            Vertices = vertices,
            Indices = indices,
            Used = used,
            UsedCount = usedCount,
            Min = min,
            Max = max,
            Repeated = repeated,
            Degenerate = degenerate,
            Duplicates = duplicates,
            OpenEdges = openEdges,
            OpenLoops = openLoops,
            NonManifoldEdges = nonManifold,
            InconsistentEdges = inconsistent,
            DuplicateVertices = duplicateVertices,
            Pieces = pieces,
            Damaged = damaged,
            PieceTriangles = pieceTriangles,
            PieceVolume = pieceVolume,
            PieceMin = pieceMin,
            PieceMax = pieceMax,
            Roots = [.. Enumerable.Range(0, vertexCount).Where(v => pieceTriangles[v] > 0)],
        };
    }

    /// <summary>Loose pieces that are mere specks: few triangles, tiny next to the whole model, and not the biggest piece.</summary>
    internal static List<int> TinyParts(MeshScan scan)
    {
        if (scan.Roots.Count < 2) return [];
        var biggest = scan.Roots.MaxBy(r => scan.PieceTriangles[r]);
        var diagonal = scan.Diagonal;
        return [.. scan.Roots.Where(r => r != biggest
                                         && scan.PieceTriangles[r] <= TinyPartTriangles
                                         && (scan.PieceMax[r] - scan.PieceMin[r]).Length() < TinyPartSizeFraction * diagonal)];
    }

    /// <summary>
    /// Closed pieces whose triangles face inwards. A piece that sits inside another closed piece is a cavity (a hollow body) and
    /// is meant to face inwards.
    /// </summary>
    internal static List<int> InvertedPieces(MeshScan scan)
    {
        var diagonal = scan.Diagonal;
        var epsilon = Math.Max(1e-9 * diagonal * diagonal * diagonal, 1e-12);
        var negatives = scan.Roots.Where(r => !scan.Damaged[r] && scan.PieceVolume[r] < -epsilon).ToList();
        if (negatives.Count == 0) return [];

        var positives = scan.Roots.Where(r => !scan.Damaged[r] && scan.PieceVolume[r] > epsilon).ToList();
        var checkCavities = (long)negatives.Count * positives.Count <= 10_000_000;
        return [.. negatives.Where(n => !checkCavities || !positives.Any(p => Contains(scan.PieceMin[p], scan.PieceMax[p], scan.PieceMin[n], scan.PieceMax[n])))];
    }

    internal static bool Contains(Vector3 outerMin, Vector3 outerMax, Vector3 innerMin, Vector3 innerMax) =>
        innerMin.X >= outerMin.X && innerMin.Y >= outerMin.Y && innerMin.Z >= outerMin.Z
        && innerMax.X <= outerMax.X && innerMax.Y <= outerMax.Y && innerMax.Z <= outerMax.Z;

    internal static double Area(Vector3 a, Vector3 b, Vector3 c)
    {
        var u = b - a;
        var v = c - a;
        var x = (double)u.Y * v.Z - (double)u.Z * v.Y;
        var y = (double)u.Z * v.X - (double)u.X * v.Z;
        var z = (double)u.X * v.Y - (double)u.Y * v.X;
        return 0.5 * Math.Sqrt(x * x + y * y + z * z);
    }

    internal static double SignedVolume(Vector3 a, Vector3 b, Vector3 c) =>
        ((double)a.X * ((double)b.Y * c.Z - (double)b.Z * c.Y)
         - (double)a.Y * ((double)b.X * c.Z - (double)b.Z * c.X)
         + (double)a.Z * ((double)b.X * c.Y - (double)b.Y * c.X)) / 6.0;

    internal static (int, int, int) SortedTriple(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }
}
