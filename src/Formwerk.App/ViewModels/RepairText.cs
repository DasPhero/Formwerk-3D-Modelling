using Formwerk.App.Resources;
using Formwerk.Repair;

namespace Formwerk.App.ViewModels;

/// <summary>Words for the mesh check banner and the repair message.</summary>
public static class RepairText
{
    /// <summary>The problems of one or several reports, for example "2 hole(s), 3 wrongly turned edge(s)". Empty when there are none.</summary>
    public static string Problems(params MeshReport[] reports)
    {
        var parts = new List<string>();
        Add(parts, "Repair.Holes", reports.Sum(r => r.OpenLoops));
        Add(parts, "Repair.Flipped", reports.Sum(r => r.InconsistentEdges));
        Add(parts, "Repair.Inside", reports.Sum(r => r.InvertedParts));
        Add(parts, "Repair.Junk", reports.Sum(r => r.DegenerateTriangles + r.DuplicateTriangles));
        Add(parts, "Repair.Unwelded", reports.Sum(r => r.DuplicateVertices));
        Add(parts, "Repair.Specks", reports.Sum(r => r.TinyLooseParts));
        Add(parts, "Repair.NonManifold", reports.Sum(r => r.NonManifoldEdges));
        return string.Join(", ", parts);
    }

    /// <summary>What the repair did, for example "closed 2 hole(s), welded 28 vertices". Empty when it did nothing.</summary>
    public static string Actions(RepairActions actions)
    {
        var parts = new List<string>();
        Add(parts, "Act.Welded", actions.WeldedVertices);
        Add(parts, "Act.Holes", actions.FilledHoles);
        Add(parts, "Act.Flipped", actions.FlippedTriangles);
        Add(parts, "Act.Inside", actions.FlippedParts);
        Add(parts, "Act.Junk", actions.RemovedDegenerateTriangles + actions.RemovedDuplicateTriangles);
        Add(parts, "Act.Specks", actions.RemovedLooseParts);
        return string.Join(", ", parts);
    }

    // The keys are written out here (not built from strings) so that the text test can check that they exist.
    private static void Add(List<string> parts, string key, int count)
    {
        if (count > 0) parts.Add(Texts.Format(key, count));
    }
}
