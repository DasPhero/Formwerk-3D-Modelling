using System.Numerics;

namespace Formwerk.Viewport;

/// <summary>
/// The hatching of hole ghosts (F-VIS-04): diagonal stripes that are planes across the direction (1, 1, 1). The texture coordinate
/// is a function of the world position alone, so the stripes have the same width on every object and face, and they do not
/// swim when the object is moved or the camera turns.
/// </summary>
public static class GhostHatch
{
    /// <summary>Upper limit of stripes on one object (a very large one gets wider stripes instead of a huge gradient).</summary>
    public const int MaxPeriods = 120;

    /// <summary>Distance in mm from one stripe to the next.</summary>
    public const float PeriodMm = 5;

    /// <summary>The coordinate along the stripe pattern: 1 per period, measured across the stripes.</summary>
    public static float Coordinate(Vector3 position) => (position.X + position.Y + position.Z) / (1.7320508f * PeriodMm);

    /// <summary>
    /// How the stripe coordinates of one object are laid onto the texture: WPF 3D clamps coordinates outside 0..1, so they are
    /// mapped to 0..1 and the brush holds <see cref="HatchLayout.Periods"/> stripes inside that range.
    /// </summary>
    public static HatchLayout LayoutFor(IEnumerable<float> coordinates)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var c in coordinates)
        {
            min = Math.Min(min, c);
            max = Math.Max(max, c);
        }

        if (min > max) return new HatchLayout(0, 0, 1);
        var range = max - min;
        return new HatchLayout(min, range, Math.Clamp((int)Math.Round(range), 1, MaxPeriods));
    }
}

/// <summary>Maps stripe coordinates to texture coordinates (0..1) and says how many stripes the brush must repeat.</summary>
public readonly record struct HatchLayout(float Min, float Range, int Periods)
{
    /// <summary>The texture coordinate of a stripe coordinate; the middle when the object has no extent across the stripes.</summary>
    public float Map(float coordinate) => Range > 1e-6f ? (coordinate - Min) / Range : 0.5f;
}
