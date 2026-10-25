using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.Features.Shapes;

/// <summary>
/// Where a shape grows from when a dimension changes (<see cref="PrimitiveNode.GrowAnchor"/>). The mesh of a shape is made around its own
/// origin, so a bigger shape grows around that origin; to keep the center or one side where it was, the position of the object has
/// to move by the difference of the anchor point of the bounds before and after, turned and scaled like the object.
/// </summary>
public static class GrowAnchoring
{
    /// <summary>A shift smaller than this (in mm) is no shift: float noise of shapes that are symmetric around their origin.</summary>
    private const float Noise = 1e-4f;

    /// <summary>Shapes that can choose where they grow from: not the tools (holes, edge cuts), whose size is that of a cutter.</summary>
    public static bool AppliesTo(PrimitiveNode node) => node.GeneratorId is not ("hole" or "edgecut" or "edgering") && ShapeLibrary.Find(node.GeneratorId) is not null;

    /// <summary>The point of the bounds that stays: per axis the minimum (-1), the center (0) or the maximum (+1).</summary>
    public static Vector3 AnchorPoint(Bounds bounds, Vector3 anchor)
    {
        static float Pick(float min, float max, float a) => a < 0 ? min : a > 0 ? max : (min + max) / 2;
        return new Vector3(Pick(bounds.Min.X, bounds.Max.X, anchor.X), Pick(bounds.Min.Y, bounds.Max.Y, anchor.Y), Pick(bounds.Min.Z, bounds.Max.Z, anchor.Z));
    }

    /// <summary>
    /// How far the position has to move, in the local space of the shape, so that its anchor stays where it was when the parameters
    /// change to <paramref name="newParameters"/> (and the variables from <paramref name="oldVariables"/> to
    /// <paramref name="newVariables"/>). Null when it cannot be told (unknown shape, a value that does not work, an empty shape).
    /// </summary>
    public static Vector3? LocalShift(
        PrimitiveNode node, IReadOnlyDictionary<string, string> newParameters, IReadOnlyDictionary<string, double> oldVariables, IReadOnlyDictionary<string, double> newVariables)
    {
        if (!AppliesTo(node) || ShapeLibrary.Find(node.GeneratorId) is not { } generator) return null;

        var after = new PrimitiveNode(node.GeneratorId, node.Name);
        foreach (var (key, value) in newParameters) after.Parameters[key] = value;
        var oldValues = node.EvaluateParameters(oldVariables, out var oldErrors);
        var newValues = after.EvaluateParameters(newVariables, out var newErrors);
        if (oldErrors.Count > 0 || newErrors.Count > 0) return null;

        try
        {
            var before = generator.Generate(oldValues);
            var now = generator.Generate(newValues);
            if (before.IsEmpty || now.IsEmpty) return null;
            var shift = AnchorPoint(before.ComputeBounds(), node.GrowAnchor) - AnchorPoint(now.ComputeBounds(), node.GrowAnchor);
            return shift.Length() < Noise ? Vector3.Zero : shift;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return null; // a value the generator does not accept: the edit itself reports it
        }
    }

    /// <summary>The transform of the object moved by a shift in its local space (scaled and turned like the object).</summary>
    public static Transform Moved(PrimitiveNode node, Vector3 localShift)
    {
        var t = node.Transform;
        return new Transform(t.Position + Vector3.Transform(localShift * t.Scale, t.Rotation), t.Rotation, t.Scale);
    }

    /// <summary>
    /// The commands that keep the anchors of all shapes when the table of project variables changes to <paramref name="newTable"/>:
    /// one transform per shape that uses a variable and would otherwise grow around its origin.
    /// </summary>
    public static List<IDocumentCommand> ForVariableChange(FormwerkDocument document, IEnumerable<KeyValuePair<string, string>> newTable)
    {
        var commands = new List<IDocumentCommand>();
        var future = new FormwerkDocument();
        foreach (var (name, expression) in newTable) future.Variables[name] = expression;
        var oldVariables = document.ResolveVariables(out _);
        var newVariables = future.ResolveVariables(out _);

        foreach (var node in document.Root.Descendants().OfType<PrimitiveNode>())
        {
            if (!node.Parameters.Values.Any(text => ExpressionParser.UsedNames(text).Count > 0)) continue; // no variable, nothing changes
            if (LocalShift(node, node.Parameters, oldVariables, newVariables) is { } shift && shift != Vector3.Zero)
                commands.Add(new SetTransformCommand(document, node, Moved(node, shift)));
        }

        return commands;
    }
}
