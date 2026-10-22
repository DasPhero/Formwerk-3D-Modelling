using System.Numerics;
using System.Windows;
using System.Windows.Input;

namespace Formwerk.Viewport;

/// <summary>An element of the library that was dropped into the view: its id and where (a face of an object, or the plate when the node id is empty).</summary>
public sealed record LibraryDropRequest(string ItemId, FacePick Pick);

/// <summary>
/// Dragging an element of the library into the view (F-LIB-04): the drop reports the face of the nearest solid under the mouse, or, over
/// nothing, the point of the build plate (Z = 0) under it, with the up direction as its normal.
/// </summary>
public sealed partial class ViewportControl
{
    /// <summary>The data format of a dragged library element: its id as text.</summary>
    public const string LibraryItemFormat = "Formwerk.LibraryItem";

    public static readonly DependencyProperty LibraryDropCommandProperty = DependencyProperty.Register(
        nameof(LibraryDropCommand), typeof(ICommand), typeof(ViewportControl));

    /// <summary>Executed with a <see cref="LibraryDropRequest"/> when an element of the library is dropped into the view.</summary>
    public ICommand? LibraryDropCommand
    {
        get => (ICommand?)GetValue(LibraryDropCommandProperty);
        set => SetValue(LibraryDropCommandProperty, value);
    }

    protected override void OnDragEnter(DragEventArgs e) => Allow(e);

    protected override void OnDragOver(DragEventArgs e) => Allow(e);

    private void Allow(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(LibraryItemFormat)) return;
        e.Effects = LibraryDropCommand is null ? DragDropEffects.None : DragDropEffects.Copy;
        e.Handled = true;
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(LibraryItemFormat) || e.Data.GetData(LibraryItemFormat) is not string id) return;
        e.Handled = true;
        if (DropTarget(e.GetPosition(this)) is not { } pick) return;

        var request = new LibraryDropRequest(id, pick);
        if (LibraryDropCommand?.CanExecute(request) == true) LibraryDropCommand.Execute(request);
    }

    /// <summary>The face under the mouse, or else the point of the plate under it; null when the mouse looks away from the plate.</summary>
    private FacePick? DropTarget(Point position)
    {
        if (HitFace(position) is { } face) return face;
        var noSnap = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        return GizmoMath.IntersectPlane(MouseRay(position), Vector3.Zero, Vector3.UnitZ, out var hit)
            ? new FacePick(Guid.Empty, hit with { Z = 0 }, Vector3.UnitZ, noSnap)
            : null;
    }
}
