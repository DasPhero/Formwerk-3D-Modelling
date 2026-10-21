namespace Formwerk.Core.Model;

/// <summary>Which edge of the objects is lined up (F-SNP-04).</summary>
public enum AlignMode
{
    /// <summary>The low edge along the axis.</summary>
    Min,

    /// <summary>The middle along the axis.</summary>
    Center,

    /// <summary>The high edge along the axis.</summary>
    Max,
}

/// <summary>
/// Aligning and distributing objects along one axis (F-SNP-04). Every object is given by the extent of its box along the axis
/// (<c>Min</c> to <c>Max</c> in mm); the result is how far each object has to move along the axis.
/// </summary>
public static class AlignMath
{
    /// <summary>
    /// Lines up the <paramref name="mode"/> edge of all objects with that of the reference: the object with the index
    /// <paramref name="reference"/> (the first selected one, it does not move), or the box around all of them when the index is not
    /// that of an object (use -1).
    /// </summary>
    public static float[] Align(IReadOnlyList<(float Min, float Max)> extents, AlignMode mode, int reference)
    {
        var shifts = new float[extents.Count];
        if (extents.Count == 0) return shifts;

        var (low, high) = reference >= 0 && reference < extents.Count
            ? extents[reference]
            : (extents.Min(e => e.Min), extents.Max(e => e.Max));
        var target = Edge(low, high, mode);
        for (var i = 0; i < extents.Count; i++) shifts[i] = target - Edge(extents[i].Min, extents[i].Max, mode);
        return shifts;
    }

    /// <summary>
    /// Spreads three or more objects so that the gaps between their boxes are equal. The objects are taken in the order they have on the
    /// axis; the first and the last stay where they are. When the objects are wider together than the room between the outer
    /// ones the gap is negative: they overlap by the same amount everywhere. Fewer than three objects do not move.
    /// </summary>
    public static float[] Distribute(IReadOnlyList<(float Min, float Max)> extents)
    {
        var shifts = new float[extents.Count];
        if (extents.Count < 3) return shifts;

        var order = Enumerable.Range(0, extents.Count)
            .OrderBy(i => extents[i].Min + extents[i].Max) // by center: robust for boxes of different width
            .ThenBy(i => i)
            .ToList();

        var first = extents[order[0]];
        var last = extents[order[^1]];
        var widths = order.Sum(i => extents[i].Max - extents[i].Min);
        var gap = (last.Max - first.Min - widths) / (order.Count - 1);

        var position = first.Min;
        foreach (var index in order)
        {
            shifts[index] = position - extents[index].Min;
            position += extents[index].Max - extents[index].Min + gap;
        }

        shifts[order[0]] = 0; // the outer ones stay exactly where they are (no rounding noise)
        shifts[order[^1]] = 0;
        return shifts;
    }

    private static float Edge(float min, float max, AlignMode mode) => mode switch
    {
        AlignMode.Min => min,
        AlignMode.Max => max,
        _ => (min + max) / 2,
    };
}
