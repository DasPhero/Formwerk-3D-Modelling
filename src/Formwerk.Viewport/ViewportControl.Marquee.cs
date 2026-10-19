using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Formwerk.Viewport;

/// <summary>
/// Rectangle selection (F-SEL-01). With the Select tool a left drag draws a rubber band instead of turning the camera; on release the
/// objects it takes are reported through <see cref="MarqueeCommand"/>. The math is in <see cref="MarqueeSelection"/>.
/// </summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty MarqueeCommandProperty = DependencyProperty.Register(
        nameof(MarqueeCommand), typeof(ICommand), typeof(ViewportControl));

    private static readonly Color MarqueeFill = Color.FromArgb(0x30, 0x4C, 0x9A, 0xFF);
    private static readonly DoubleCollection CrossingDashes = [4, 3];

    private readonly Rectangle _marqueeBox = new()
    {
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        Stroke = new SolidColorBrush(Accent),
        Fill = new SolidColorBrush(MarqueeFill),
        StrokeThickness = 1,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };

    public static readonly DependencyProperty OpenGroupCommandProperty = DependencyProperty.Register(
        nameof(OpenGroupCommand), typeof(ICommand), typeof(ViewportControl));

    private bool _marqueeActive;

    /// <summary>Executed with the node id (<c>Guid?</c>, null on empty space) of the item under the mouse when the user double clicks.</summary>
    public ICommand? OpenGroupCommand
    {
        get => (ICommand?)GetValue(OpenGroupCommandProperty);
        set => SetValue(OpenGroupCommandProperty, value);
    }

    /// <summary>Executed with a <see cref="MarqueeRequest"/> when the user has dragged a selection rectangle in the Select tool.</summary>
    public ICommand? MarqueeCommand
    {
        get => (ICommand?)GetValue(MarqueeCommandProperty);
        set => SetValue(MarqueeCommandProperty, value);
    }

    /// <summary>A left drag draws a rectangle in the Select tool; the other tools (and the face mode) keep orbiting.</summary>
    private bool IsMarqueeTool => ActiveTool == ViewportTool.Select && !FacePickMode && MarqueeCommand is not null;

    private void UpdateMarquee(Point position)
    {
        _marqueeActive = true;
        var rect = ScreenRect.FromCorners(_downMouse.X, _downMouse.Y, position.X, position.Y);
        // a dashed outline tells the crossing (right to left) from the window (left to right)
        _marqueeBox.StrokeDashArray = MarqueeSelection.ModeFor(_downMouse.X, position.X) == MarqueeMode.Crossing ? CrossingDashes : null;
        _marqueeBox.Margin = new Thickness(rect.Left, rect.Top, 0, 0);
        _marqueeBox.Width = rect.Width;
        _marqueeBox.Height = rect.Height;
        _marqueeBox.Visibility = Visibility.Visible;
    }

    private void CancelMarquee()
    {
        _marqueeActive = false;
        _marqueeBox.Visibility = Visibility.Collapsed;
    }

    private void EndMarquee(Point position)
    {
        CancelMarquee();
        var rect = ScreenRect.FromCorners(_downMouse.X, _downMouse.Y, position.X, position.Y);
        var mode = MarqueeSelection.ModeFor(_downMouse.X, position.X);
        var ids = MarqueeSelection.Select(Items ?? [], _orbit, Math.Max(ActualWidth, 1), Math.Max(ActualHeight, 1), rect, mode);

        var additive = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        var request = new MarqueeRequest(ids, additive);
        if (MarqueeCommand?.CanExecute(request) == true) MarqueeCommand.Execute(request);
    }
}
