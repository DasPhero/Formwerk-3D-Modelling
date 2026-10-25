namespace Formwerk.Viewport;

/// <summary>What a mouse drag does to the camera.</summary>
public enum CameraGesture
{
    None,
    Orbit,
    Pan,
}

/// <summary>
/// The rules of the mouse in the viewport, free of any window so that they can be tested: which drag turns or pans the camera, when
/// a left drag on an object moves it.
/// </summary>
public static class MouseRules
{
    /// <summary>
    /// Middle drag, Shift + right drag and Space + left drag pan (the last one for laptops without a middle button); right drag and
    /// left drag orbit; nothing pressed does nothing.
    /// </summary>
    public static CameraGesture CameraGestureFor(bool left, bool middle, bool right, bool shift, bool space)
    {
        if (middle || (right && shift) || (left && space)) return CameraGesture.Pan;
        if (right || left) return CameraGesture.Orbit;
        return CameraGesture.None;
    }

    /// <summary>
    /// A left press on an object starts moving it (instead of turning the camera) in the Move and Select tool, unless a face is being
    /// picked, a modifier makes the click a selection change, Space asks for a pan, or the object is dimmed (outside the open group).
    /// </summary>
    public static bool CanStartFreeDrag(ViewportTool tool, bool facePickMode, bool additive, bool space, bool dimmed) =>
        tool is ViewportTool.Move or ViewportTool.Select && !facePickMode && !additive && !space && !dimmed;
}
