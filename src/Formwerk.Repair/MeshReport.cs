namespace Formwerk.Repair;

/// <summary>
/// What is wrong with a mesh (F-REP-01). Edge counts refer to the triangles as they are stored (before any welding);
/// <see cref="DuplicateVertices"/> says how many vertices lie within the weld tolerance of another one, which is the usual cause of open edges
/// in files that were exported without sharing vertices.
/// </summary>
/// <param name="Vertices">Number of vertices that triangles use.</param>
/// <param name="Triangles">Number of triangles.</param>
/// <param name="OpenEdges">Edges that only one triangle uses: the rim of a hole.</param>
/// <param name="OpenLoops">Separate holes (connected rims).</param>
/// <param name="NonManifoldEdges">Edges that more than two triangles share.</param>
/// <param name="InconsistentEdges">Edges whose two triangles disagree about the direction (a triangle is turned the wrong way).</param>
/// <param name="DegenerateTriangles">Triangles without area, or that use a vertex twice.</param>
/// <param name="DuplicateTriangles">Extra copies of a triangle, also a triangle and its mirror image (an internal wall without thickness).</param>
/// <param name="DuplicateVertices">Vertices that coincide with an earlier vertex within the tolerance.</param>
/// <param name="LooseParts">Separate pieces besides the biggest (several bodies in one file are normal).</param>
/// <param name="TinyLooseParts">Loose pieces that are mere specks: at most 100 triangles and much smaller than the whole model.</param>
/// <param name="InvertedParts">Closed pieces that are turned inside out (their triangles face inwards); cavities inside another piece do not count.</param>
/// <param name="Skipped">The mesh is too large to be analysed; all other numbers are zero.</param>
/// <param name="SelfIntersections">Pairs of triangles that cut through each other (bodies that overlap, a surface that folds through itself); meshes of more than 300000 triangles are not checked.</param>
/// <param name="NonManifoldVertices">Points where the surface touches itself without sharing an edge (two bodies that meet in a corner).</param>
public sealed record MeshReport(
    int Vertices,
    int Triangles,
    int OpenEdges,
    int OpenLoops,
    int NonManifoldEdges,
    int InconsistentEdges,
    int DegenerateTriangles,
    int DuplicateTriangles,
    int DuplicateVertices,
    int LooseParts,
    int TinyLooseParts,
    int InvertedParts,
    bool Skipped = false,
    int NonManifoldVertices = 0,
    int SelfIntersections = 0)
{
    /// <summary>Number of things the user would call an error. Several bodies in one mesh are not an error, specks are.</summary>
    public int ProblemCount =>
        OpenLoops + NonManifoldEdges + NonManifoldVertices + SelfIntersections + InconsistentEdges + DegenerateTriangles + DuplicateTriangles + DuplicateVertices + TinyLooseParts + InvertedParts;

    /// <summary>Closed, manifold, consistently oriented and without junk: ready for booleans and printing.</summary>
    public bool IsHealthy => !Skipped && Triangles > 0 && OpenEdges == 0 && ProblemCount == 0;

    /// <summary>What repair can fix (everything except non-manifold edges and points).</summary>
    public bool HasRepairableProblems => !Skipped && (ProblemCount - NonManifoldEdges - NonManifoldVertices > 0 || OpenEdges > 0);
}
