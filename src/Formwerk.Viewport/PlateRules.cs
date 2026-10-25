namespace Formwerk.Viewport;

/// <summary>When and how much the build plate is see-through: from below it must not hide the underside of the model.</summary>
public static class PlateRules
{
    /// <summary>How much of the plate is seen when the camera is below it (0 = invisible, 1 = solid).</summary>
    public const double BelowOpacity = 0.2;

    /// <summary>The camera is below the plane of the plate (Z = 0).</summary>
    public static bool IsBelow(double cameraZ) => cameraZ < -0.05;

    public static double OpacityFor(double cameraZ) => IsBelow(cameraZ) ? BelowOpacity : 1;
}
