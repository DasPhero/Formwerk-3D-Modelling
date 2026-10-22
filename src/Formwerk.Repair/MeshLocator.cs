using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Repair;

/// <summary>The kinds of problems the analysis counts, as far as they have places in the mesh that can be shown (F-REP-01).</summary>
public enum ProblemKind
{
    /// <summary>Edges that only one triangle uses (the rim of a hole).</summary>
    OpenEdges,

    /// <summary>Edges that more than two triangles share.</summary>
    NonManifoldEdges,

    /// <summary>Edges whose two triangles disagree about the direction (a triangle is turned the wrong way).</summary>
    InconsistentEdges,

    /// <summary>Triangles without area, or that use a vertex twice.</summary>
    DegenerateTriangles,

    /// <summary>Extra copies of a triangle.</summary>
    DuplicateTriangles,

    /// <summary>Vertices that coincide with an earlier one.</summary>
    DuplicateVertices,

    /// <summary>Loose pieces that are mere specks.</summary>
    LooseSpecks,

    /// <summary>Closed pieces turned inside out.</summary>
    InvertedParts,

    /// <summary>Points where the surface touches itself without sharing an edge.</summary>
    NonManifoldVertices,

    /// <summary>Lines where the surface cuts through itself.</summary>
    SelfIntersections,
}

/// <summary>Where a problem is: bars along edges and points at triangles, vertices or pieces, in the coordinates of the mesh.</summary>
/// <param name="Truncated">There were more places than the limit; only the first ones are listed.</param>
public sealed record ProblemPlaces(IReadOnlyList<(Vector3 From, Vector3 To)> Segments, IReadOnlyList<Vector3> Points, bool Truncated)
{
    public static ProblemPlaces None { get; } = new([], [], false);
}

public static partial class MeshAnalyzer
{
    /// <summary>Most places that are listed for one kind of problem.</summary>
    public const int DefaultPlaceLimit = 5000;

    /// <summary>
    /// The places of one kind of problem, found with the same scan as <see cref="Analyze"/>, so the numbers agree with the report
    /// (open, non-manifold and inconsistent edges are bars, the rest are points: the center of a triangle, a vertex, the center of a piece).
    /// </summary>
    public static ProblemPlaces Locate(MeshData mesh, ProblemKind kind, int limit = DefaultPlaceLimit)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        if (mesh.TriangleCount == 0 || mesh.TriangleCount > MaxTriangles) return ProblemPlaces.None;

        var scan = Scan(mesh.Vertices.ToArray(), mesh.Indices.ToArray());
        var segments = new List<(Vector3, Vector3)>();
        var points = new List<Vector3>();
        var truncated = false;

        bool Full(int count)
        {
            if (count < limit) return false;
            truncated = true;
            return true;
        }

        var vertices = scan.Vertices;
        var indices = scan.Indices;
        switch (kind)
        {
            case ProblemKind.OpenEdges or ProblemKind.NonManifoldEdges or ProblemKind.InconsistentEdges:
                var edges = EdgeTable.Build(indices, scan.Repeated);
                for (var i = 0; i < edges.Count;)
                {
                    var length = edges.GroupLength(i);
                    var wanted = kind switch
                    {
                        ProblemKind.OpenEdges => length == 1,
                        ProblemKind.NonManifoldEdges => length > 2,
                        _ => length == 2 && EdgeTable.RunsUp(edges.Payload[i]) == EdgeTable.RunsUp(edges.Payload[i + 1]),
                    };
                    if (wanted)
                    {
                        if (Full(segments.Count)) break;
                        segments.Add((vertices[EdgeTable.Low(edges.Keys[i])], vertices[EdgeTable.High(edges.Keys[i])]));
                    }

                    i += length;
                }

                break;

            case ProblemKind.DegenerateTriangles:
                var minArea = 1e-10 * scan.Diagonal * scan.Diagonal;
                for (var t = 0; t < indices.Length / 3; t++)
                {
                    Vector3 a = vertices[indices[t * 3]], b = vertices[indices[t * 3 + 1]], c = vertices[indices[t * 3 + 2]];
                    if (!scan.Repeated[t] && Area(a, b, c) >= minArea) continue;
                    if (Full(points.Count)) break;
                    points.Add((a + b + c) / 3);
                }

                break;

            case ProblemKind.DuplicateTriangles:
                var seen = new HashSet<(int, int, int)>();
                for (var t = 0; t < indices.Length / 3; t++)
                {
                    if (scan.Repeated[t]) continue;
                    if (seen.Add(SortedTriple(indices[t * 3], indices[t * 3 + 1], indices[t * 3 + 2]))) continue;
                    if (Full(points.Count)) break;
                    points.Add((vertices[indices[t * 3]] + vertices[indices[t * 3 + 1]] + vertices[indices[t * 3 + 2]]) / 3);
                }

                break;

            case ProblemKind.DuplicateVertices:
                var welded = VertexWelder.Map(vertices, scan.Used, VertexWelder.Tolerance(scan.Min, scan.Max));
                for (var v = 0; v < vertices.Length; v++)
                {
                    if (!scan.Used[v] || welded[v] == v) continue;
                    if (Full(points.Count)) break;
                    points.Add(vertices[v]);
                }

                break;

            case ProblemKind.LooseSpecks:
                foreach (var root in TinyParts(scan))
                {
                    if (Full(points.Count)) break;
                    points.Add((scan.PieceMin[root] + scan.PieceMax[root]) * 0.5f);
                }

                break;

            case ProblemKind.SelfIntersections:
                var cuts = FindSelfIntersections(vertices, indices, scan.Repeated, limit, out var more);
                segments.AddRange(cuts.Select(c => (c.From, c.To)));
                truncated |= more;
                break;

            case ProblemKind.NonManifoldVertices:
                foreach (var v in FindNonManifoldVertices(vertices, indices, scan.Repeated))
                {
                    if (Full(points.Count)) break;
                    points.Add(vertices[v]);
                }

                break;

            case ProblemKind.InvertedParts:
                foreach (var root in InvertedPieces(scan))
                {
                    if (Full(points.Count)) break;
                    points.Add((scan.PieceMin[root] + scan.PieceMax[root]) * 0.5f);
                }

                break;
        }

        return new ProblemPlaces(segments, points, truncated);
    }
}
