using System.Numerics;

namespace Formwerk.Viewport;

/// <summary>Where the orbit camera is: the point it looks at, how far away, and the viewing direction in degrees.</summary>
public readonly record struct CameraState(Vector3 Target, double Distance, double YawDegrees, double PitchDegrees);

/// <summary>The glide between two camera states when the view changes (F-VIS-06). Pure math; the control drives it with the clock.</summary>
public static class CameraAnimation
{
    /// <summary>A quarter of a second: noticeable, never in the way.</summary>
    public const double DurationSeconds = 0.25;

    private const double Epsilon = 1e-4;

    /// <summary>How far the animation has come (0 to 1) after the given time.</summary>
    public static double Progress(double elapsedSeconds) => Math.Clamp(elapsedSeconds / DurationSeconds, 0, 1);

    /// <summary>
    /// The state at <paramref name="t"/> (0 = start, 1 = end, clamped), eased out: quick first, gentle at the end. The viewing
    /// direction turns the short way round; the start and the end are returned exactly.
    /// </summary>
    public static CameraState Interpolate(CameraState from, CameraState to, double t)
    {
        if (t <= 0) return from;
        if (t >= 1) return to;

        var eased = 1 - Math.Pow(1 - t, 3);
        return new CameraState(
            Vector3.Lerp(from.Target, to.Target, (float)eased),
            from.Distance + (to.Distance - from.Distance) * eased,
            from.YawDegrees + ShortestTurn(from.YawDegrees, to.YawDegrees) * eased,
            from.PitchDegrees + (to.PitchDegrees - from.PitchDegrees) * eased);
    }

    /// <summary>False when the two states are the same view (nothing to animate).</summary>
    public static bool IsMoving(CameraState from, CameraState to) =>
        Vector3.Distance(from.Target, to.Target) > Epsilon
        || Math.Abs(from.Distance - to.Distance) > Epsilon
        || Math.Abs(ShortestTurn(from.YawDegrees, to.YawDegrees)) > Epsilon
        || Math.Abs(from.PitchDegrees - to.PitchDegrees) > Epsilon;

    /// <summary>The turn in degrees, between -180 and 180, that leads from one yaw to the other.</summary>
    private static double ShortestTurn(double from, double to)
    {
        var delta = (to - from) % 360;
        if (delta > 180) delta -= 360;
        else if (delta < -180) delta += 360;
        return delta;
    }
}
