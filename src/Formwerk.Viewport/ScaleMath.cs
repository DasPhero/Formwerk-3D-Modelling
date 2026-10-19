namespace Formwerk.Viewport;

/// <summary>How far to scale for a given mouse movement (F-SCL-01, F-SCL-03).</summary>
public static class ScaleMath
{
    /// <summary>
    /// Scale factor for dragging a face handle. <paramref name="side"/> is +1 for the maximum face and -1 for the minimum face,
    /// <paramref name="drag"/> is how far the mouse moved the handle along the axis (positive = along the axis direction).
    /// Dragging outwards enlarges. With <paramref name="fromCenter"/> both faces move, so the size changes twice as fast.
    /// </summary>
    public static double FaceFactor(double extent, int side, double drag, bool fromCenter)
    {
        if (extent <= 1e-9) return 1;
        var newExtent = extent + side * drag * (fromCenter ? 2 : 1);
        return Math.Clamp(newExtent / extent, MinFactor, MaxFactor);
    }

    /// <summary>Factor for dragging a corner handle: where the mouse is along the line from the fixed point through the handle, relative to the start.</summary>
    public static double CornerFactor(double parameterNow, double parameterAtStart)
    {
        if (Math.Abs(parameterAtStart) <= 1e-9) return 1;
        return Math.Clamp(parameterNow / parameterAtStart, MinFactor, MaxFactor);
    }

    /// <summary>Dragging past the opposite side does not mirror; the size just stays tiny.</summary>
    public const double MinFactor = 0.001;

    public const double MaxFactor = 1000;
}
