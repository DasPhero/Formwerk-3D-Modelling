using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;

namespace Formwerk.Features.Evaluation;

/// <summary>The editable values of a pattern (F-PAT-01, F-PAT-02): name, label, default and range of each parameter.</summary>
public static class PatternParameters
{
    private static readonly ParameterDefinition[] LinearDefinitions =
    [
        new("count", "Count", 3, 1, PatternNode.MaxInstances, IsInteger: true),
        new("spacing", "Spacing", 15, -10000, 10000, IsDetail: true),
        new("totalLength", "Spacing is the total length", 0, 0, 1, IsInteger: true),
        new("axis", "Direction", 0, 0, 2, IsInteger: true),
        new("countY", "Count, second direction", 1, 1, PatternNode.MaxInstances, IsInteger: true),
        new("spacingY", "Spacing, second direction", 15, -10000, 10000, IsDetail: true),
        new("totalLengthY", "Second spacing is the total length", 0, 0, 1, IsInteger: true),
        new("axisY", "Second direction", 1, 0, 2, IsInteger: true),
    ];

    private static readonly ParameterDefinition[] CircularDefinitions =
    [
        new("count", "Count", 3, 1, PatternNode.MaxInstances, IsInteger: true),
        new("angle", "Angle", 360, 0.01, 360, IsDetail: true, Unit: "°"),
        new("axis", "Axis", 2, 0, 2, IsInteger: true),
    ];

    public static IReadOnlyList<ParameterDefinition> For(PatternKind kind) => kind == PatternKind.Linear ? LinearDefinitions : CircularDefinitions;
}

/// <summary>Where the instances of a pattern are: one matrix per instance, in the space of the pattern (F-PAT-02, F-PAT-03).</summary>
public static class PatternMath
{
    /// <summary>
    /// The placement of every instance that is not skipped. The first instance is the source where it is (identity). Missing
    /// parameters use their defaults. When a value is out of range or there would be too many instances, <paramref name="problem"/>
    /// says so and only the source is returned, so that something is still shown.
    /// </summary>
    public static List<Matrix4x4> Instances(PatternNode pattern, IReadOnlyDictionary<string, double> values, out string? problem)
    {
        problem = null;
        var p = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in PatternParameters.For(pattern.Kind))
        {
            var value = values.TryGetValue(definition.Name, out var v) ? v : definition.Default;
            if (!double.IsFinite(value) || value < definition.Min || value > definition.Max)
            {
                problem = $"{definition.Label} must be between {definition.Min:0.###} and {definition.Max:0.###}.";
                return [Matrix4x4.Identity];
            }

            p[definition.Name] = definition.IsInteger ? Math.Round(value) : value;
        }

        var count = (int)p["count"];
        var countY = pattern.Kind == PatternKind.Linear ? (int)p["countY"] : 1;
        if ((long)count * countY > PatternNode.MaxInstances)
        {
            problem = $"A pattern can have at most {PatternNode.MaxInstances} instances.";
            return [Matrix4x4.Identity];
        }

        var all = pattern.Kind == PatternKind.Linear ? Linear(p, count, countY) : Circular(p, count);
        var result = new List<Matrix4x4>(all.Count);
        for (var i = 0; i < all.Count; i++)
        {
            if (!pattern.Skipped.Contains(i + 1)) result.Add(all[i]);
        }

        return result;
    }

    private static List<Matrix4x4> Linear(Dictionary<string, double> p, int count, int countY)
    {
        var first = Axis((int)p["axis"]) * Step(p["spacing"], p["totalLength"] >= 0.5, count);
        var second = Axis((int)p["axisY"]) * Step(p["spacingY"], p["totalLengthY"] >= 0.5, countY);
        var matrices = new List<Matrix4x4>(count * countY);
        for (var row = 0; row < countY; row++)
        {
            for (var column = 0; column < count; column++) matrices.Add(Matrix4x4.CreateTranslation(first * column + second * row));
        }

        return matrices;
    }

    private static List<Matrix4x4> Circular(Dictionary<string, double> p, int count)
    {
        var axis = Axis((int)p["axis"]);
        var angle = p["angle"];

        // a full circle is divided by the count (no instance twice on the start); a part circle ends on the angle with the last one
        var step = angle >= 360 - 1e-9 ? 360.0 / count : count > 1 ? angle / (count - 1) : 0;
        var matrices = new List<Matrix4x4>(count);
        for (var i = 0; i < count; i++)
        {
            matrices.Add(i == 0 ? Matrix4x4.Identity : Matrix4x4.CreateFromAxisAngle(axis, (float)(i * step * Math.PI / 180)));
        }

        return matrices;
    }

    /// <summary>The distance between two instances: the spacing, or the total length shared by the gaps.</summary>
    private static float Step(double spacing, bool totalLength, int count) =>
        (float)(totalLength ? (count > 1 ? spacing / (count - 1) : 0) : spacing);

    private static Vector3 Axis(int axis) => axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
}
