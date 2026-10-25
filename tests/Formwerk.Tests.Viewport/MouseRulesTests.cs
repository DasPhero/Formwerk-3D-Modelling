using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The mouse rules of the viewport: camera gestures (also without a middle button), moving objects by dragging them, hover markers.</summary>
public class MouseRulesTests
{
    [Theory]
    [InlineData(true, false, false, false, false, CameraGesture.Orbit)]
    [InlineData(false, false, true, false, false, CameraGesture.Orbit)]
    [InlineData(false, true, false, false, false, CameraGesture.Pan)]
    [InlineData(false, false, true, true, false, CameraGesture.Pan)]
    [InlineData(true, false, false, false, true, CameraGesture.Pan)] // Space + left drag: for laptops
    [InlineData(true, false, false, true, false, CameraGesture.Orbit)] // Shift alone does not pan with the left button
    [InlineData(false, false, false, false, false, CameraGesture.None)]
    [InlineData(false, false, false, true, true, CameraGesture.None)]
    public void The_camera_gesture_follows_the_buttons_and_keys(bool left, bool middle, bool right, bool shift, bool space, CameraGesture expected) =>
        Assert.Equal(expected, MouseRules.CameraGestureFor(left, middle, right, shift, space));

    [Theory]
    [InlineData(ViewportTool.Move, false, false, false, false, true)]
    [InlineData(ViewportTool.Select, false, false, false, false, true)]
    [InlineData(ViewportTool.Rotate, false, false, false, false, false)]
    [InlineData(ViewportTool.Scale, false, false, false, false, false)]
    [InlineData(ViewportTool.Move, true, false, false, false, false)] // picking a face
    [InlineData(ViewportTool.Move, false, true, false, false, false)] // Ctrl or Shift: the click changes the selection
    [InlineData(ViewportTool.Move, false, false, true, false, false)] // Space: pan
    [InlineData(ViewportTool.Move, false, false, false, true, false)] // dimmed object outside the open group
    public void A_left_drag_on_an_object_moves_it_only_in_the_right_situation(ViewportTool tool, bool facePick, bool additive, bool space, bool dimmed, bool expected) =>
        Assert.Equal(expected, MouseRules.CanStartFreeDrag(tool, facePick, additive, space, dimmed));

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(59, false, true)]
    [InlineData(60, false, true)]
    [InlineData(61, false, false)]
    [InlineData(400, false, false)]
    [InlineData(400, true, true)] // the nearest one is always the highlighted one
    public void Hover_markers_are_only_drawn_near_the_mouse(double distance, bool nearest, bool expected) =>
        Assert.Equal(expected, MouseRules.ShowHoverMarker(distance, nearest));
}
