using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Formwerk.Viewport;

namespace Formwerk.App.Views;

/// <summary>
/// Makes a button of the library a drag source (F-LIB-04): after the mouse moved a few pixels with the left button held, the id of the
/// element is dragged into the view. A simple click still presses the button.
/// </summary>
public static class LibraryDragBehavior
{
    public static readonly DependencyProperty ItemIdProperty = DependencyProperty.RegisterAttached(
        "ItemId", typeof(string), typeof(LibraryDragBehavior), new PropertyMetadata(null, OnChanged));

    private static Point? _start;

    public static string? GetItemId(DependencyObject d) => (string?)d.GetValue(ItemIdProperty);

    public static void SetItemId(DependencyObject d, string? value) => d.SetValue(ItemIdProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        button.PreviewMouseLeftButtonDown -= OnDown;
        button.PreviewMouseMove -= OnMove;
        if (e.NewValue is null) return;
        button.PreviewMouseLeftButtonDown += OnDown;
        button.PreviewMouseMove += OnMove;
    }

    private static void OnDown(object sender, MouseButtonEventArgs e) => _start = e.GetPosition(null);

    private static void OnMove(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || _start is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        if (GetItemId(button) is not { } id) return;

        var now = e.GetPosition(null);
        if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        _start = null;
        DragDrop.DoDragDrop(button, new DataObject(ViewportControl.LibraryItemFormat, id), DragDropEffects.Copy);
    }
}
