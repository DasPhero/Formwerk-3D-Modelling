using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>
/// Orbit camera around a target with Z up (F-VIS-06). Yaw 0° looks from +X, -90° from -Y (front); pitch 90° looks from above.
/// </summary>
public sealed class OrbitCamera
{
    public const double FieldOfViewDegrees = 45;
    private const double MinPitch = -89.5;
    private const double MaxPitch = 89.5;

    public Vector3 Target { get; set; } = new(0, 0, 30);

    public double Distance { get; set; } = 420;

    public double YawDegrees { get; set; } = -55;

    public double PitchDegrees { get; set; } = 28;

    /// <summary>
    /// Orthographic projection: parallel rays, an object looks the same size at any depth. The width of the view at the target is
    /// the same as in perspective, so switching the projection does not make the picture jump.
    /// </summary>
    public bool IsOrthographic { get; set; }

    /// <summary>Target, distance and direction together (for animations).</summary>
    public CameraState State
    {
        get => new(Target, Distance, YawDegrees, PitchDegrees);
        set => (Target, Distance, YawDegrees, PitchDegrees) = (value.Target, value.Distance, value.YawDegrees, value.PitchDegrees);
    }

    public Vector3 Position
    {
        get
        {
            var yaw = YawDegrees * Math.PI / 180;
            var pitch = PitchDegrees * Math.PI / 180;
            var dir = new Vector3(
                (float)(Math.Cos(pitch) * Math.Cos(yaw)),
                (float)(Math.Cos(pitch) * Math.Sin(yaw)),
                (float)Math.Sin(pitch));
            return Target + dir * (float)Distance;
        }
    }

    public Vector3 Forward => Vector3.Normalize(Target - Position);

    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));

    public Vector3 Up => Vector3.Cross(Right, Forward);

    /// <summary>
    /// The ray through a pixel of a view that is <paramref name="width"/> by <paramref name="height"/> pixels: from the camera in
    /// perspective, parallel to the viewing direction from the pixel's place in the camera plane when orthographic.
    /// </summary>
    public Ray CreateRay(double pixelX, double pixelY, double width, double height)
    {
        var halfWidth = Math.Tan(FieldOfViewDegrees * Math.PI / 360); // WPF's field of view is the horizontal one
        var x = 2 * pixelX / width - 1;
        var y = 1 - 2 * pixelY / height;
        var aspect = width / height;
        if (IsOrthographic)
        {
            var half = halfWidth * Distance;
            return new Ray(Position + Right * (float)(x * half) + Up * (float)(y * half / aspect), Forward);
        }

        var direction = Forward + Right * (float)(x * halfWidth) + Up * (float)(y * halfWidth / aspect);
        return new Ray(Position, Vector3.Normalize(direction));
    }

    /// <summary>
    /// How many mm one screen pixel is worth at the depth of <paramref name="point"/> (0 when the point is behind the camera). The
    /// same everywhere in the orthographic view.
    /// </summary>
    public double WorldPerPixelAt(Vector3 point, double viewportWidth)
    {
        var depth = Vector3.Dot(point - Position, Forward);
        if (depth <= 1e-6f || viewportWidth <= 0) return 0;
        return 2 * (IsOrthographic ? Distance : depth) * Math.Tan(FieldOfViewDegrees * Math.PI / 360) / viewportWidth;
    }

    /// <summary>Inverse of <see cref="CreateRay"/>: the pixel a world point appears at, or null when it is behind the camera.</summary>
    public (double X, double Y)? Project(Vector3 point, double width, double height)
    {
        var v = point - Position;
        var depth = Vector3.Dot(v, Forward);
        if (depth <= 1e-6f) return null;
        var halfWidth = Math.Tan(FieldOfViewDegrees * Math.PI / 360);
        var scale = IsOrthographic ? Distance : depth;
        var x = Vector3.Dot(v, Right) / (scale * halfWidth);
        var y = Vector3.Dot(v, Up) / (scale * halfWidth / (width / height));
        return ((x + 1) / 2 * width, (1 - y) / 2 * height);
    }

    public void Orbit(double deltaX, double deltaY)
    {
        YawDegrees -= deltaX * 0.4;
        PitchDegrees = Math.Clamp(PitchDegrees + deltaY * 0.4, MinPitch, MaxPitch);
    }

    /// <summary>Moves the target parallel to the screen by the given pixel distance.</summary>
    public void Pan(double deltaX, double deltaY, double viewportWidth)
    {
        if (viewportWidth <= 0) return;
        var unitsPerPixel = 2 * Distance * Math.Tan(FieldOfViewDegrees * Math.PI / 360) / viewportWidth;
        Target += (Right * (float)(-deltaX * unitsPerPixel)) + (Up * (float)(deltaY * unitsPerPixel));
    }

    /// <summary>Positive steps zoom in. One wheel notch is one step.</summary>
    public void Zoom(double steps) => Distance = Math.Clamp(Distance * Math.Pow(0.88, steps), 1, 100000);

    /// <summary>
    /// Zooms like <see cref="Zoom"/> and moves the target so that the point under the cursor (on the plane through the target,
    /// perpendicular to the view) stays under the cursor. Without a view size it is a plain zoom.
    /// </summary>
    public void ZoomAt(double steps, double pixelX, double pixelY, double width, double height)
    {
        var old = Distance;
        Zoom(steps);
        if (width <= 0 || height <= 0) return;

        var factor = Distance / old; // after clamping, so that nothing drifts at the limits
        if (Math.Abs(factor - 1) < 1e-12) return;

        var halfWidth = Math.Tan(FieldOfViewDegrees * Math.PI / 360);
        var x = 2 * pixelX / width - 1;
        var y = 1 - 2 * pixelY / height;
        var offset = Right * (float)(x * halfWidth * old) + Up * (float)(y * halfWidth * old / (width / height));
        Target += offset * (float)(1 - factor);
    }

    /// <summary>The state in which the bounds fill the view, looking in the current direction.</summary>
    public CameraState FramedState(Bounds bounds)
    {
        var radius = Math.Max(bounds.Size.Length() / 2, 5);
        return State with
        {
            Target = bounds.Center,
            Distance = Math.Clamp(radius / Math.Sin(FieldOfViewDegrees * Math.PI / 360) * 1.1, 10, 100000),
        };
    }

    public void Frame(Bounds bounds) => State = FramedState(bounds);

    /// <summary>The state that looks at the same target from the same distance in the direction of a standard view.</summary>
    public CameraState PresetState(ViewPreset preset)
    {
        var (yaw, pitch) = preset switch
        {
            ViewPreset.Front => (-90.0, 0.0),
            ViewPreset.Back => (90.0, 0.0),
            ViewPreset.Left => (180.0, 0.0),
            ViewPreset.Right => (0.0, 0.0),
            ViewPreset.Top => (-90.0, MaxPitch),
            ViewPreset.Bottom => (-90.0, MinPitch),
            _ => (-55.0, 28.0),
        };
        return State with { YawDegrees = yaw, PitchDegrees = pitch };
    }

    public void SetPreset(ViewPreset preset) => State = PresetState(preset);
}
