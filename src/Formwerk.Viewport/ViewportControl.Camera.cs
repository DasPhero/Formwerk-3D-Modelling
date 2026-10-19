using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Formwerk.Viewport;

/// <summary>Perspective or orthographic projection and animated view changes (F-VIS-06).</summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty OrthographicProperty = DependencyProperty.Register(
        nameof(Orthographic), typeof(bool), typeof(ViewportControl),
        new PropertyMetadata(false, (d, _) => ((ViewportControl)d).UpdateCamera()));

    private readonly OrthographicCamera _orthographicCamera = new() { UpDirection = new Vector3D(0, 0, 1) };
    private readonly Stopwatch _glideClock = new();
    private CameraState _glideFrom;
    private CameraState _glideTo;
    private bool _gliding;

    /// <summary>Parallel projection instead of perspective. The width of the view at the camera target stays the same.</summary>
    public bool Orthographic
    {
        get => (bool)GetValue(OrthographicProperty);
        set => SetValue(OrthographicProperty, value);
    }

    /// <summary>Moves the camera to a new state with a short glide; without a laid out view (or a state that is already there) it just jumps.</summary>
    private void GlideTo(CameraState goal)
    {
        StopGlide();
        var start = _orbit.State;
        if (!CameraAnimation.IsMoving(start, goal) || !IsLoaded || ActualWidth <= 0)
        {
            _orbit.State = goal;
            UpdateCamera();
            return;
        }

        _glideFrom = start;
        _glideTo = goal;
        _glideClock.Restart();
        _gliding = true;
        CompositionTarget.Rendering += OnGlideFrame;
    }

    private void OnGlideFrame(object? sender, EventArgs e)
    {
        var progress = CameraAnimation.Progress(_glideClock.Elapsed.TotalSeconds);
        _orbit.State = CameraAnimation.Interpolate(_glideFrom, _glideTo, progress);
        UpdateCamera();
        if (progress >= 1) StopGlide();
    }

    /// <summary>Ends a running glide where it is (any mouse action takes the camera over at once).</summary>
    private void StopGlide()
    {
        if (!_gliding) return;
        _gliding = false;
        CompositionTarget.Rendering -= OnGlideFrame;
        _glideClock.Stop();
    }
}
