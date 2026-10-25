using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Formwerk.App.ViewModels;
using Formwerk.Viewport;

namespace Formwerk.App.Views;

/// <summary>
/// Makes the button of an own library folder a drop target: a tile that is dragged from the library (the data written by
/// <see cref="LibraryDragBehavior"/>) and dropped on it is reported to the command with the folder name. View code only; the view model
/// decides what happens. <see cref="IsDragOverProperty"/> is true while a tile is over the button, for highlighting.
/// </summary>
public static class LibraryFolderDropBehavior
{
    public static readonly DependencyProperty FolderProperty = DependencyProperty.RegisterAttached(
        "Folder", typeof(string), typeof(LibraryFolderDropBehavior), new PropertyMetadata(null, OnFolderChanged));

    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command", typeof(ICommand), typeof(LibraryFolderDropBehavior), new PropertyMetadata(null));

    public static readonly DependencyProperty IsDragOverProperty = DependencyProperty.RegisterAttached(
        "IsDragOver", typeof(bool), typeof(LibraryFolderDropBehavior), new PropertyMetadata(false));

    public static string? GetFolder(DependencyObject d) => (string?)d.GetValue(FolderProperty);

    public static void SetFolder(DependencyObject d, string? value) => d.SetValue(FolderProperty, value);

    public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);

    public static bool GetIsDragOver(DependencyObject d) => (bool)d.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject d, bool value) => d.SetValue(IsDragOverProperty, value);

    private static void OnFolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button button) return;
        button.DragEnter -= OnDragOver;
        button.DragOver -= OnDragOver;
        button.DragLeave -= OnDragLeave;
        button.Drop -= OnDrop;
        button.AllowDrop = e.NewValue is not null;
        if (e.NewValue is null) return;
        button.DragEnter += OnDragOver;
        button.DragOver += OnDragOver;
        button.DragLeave += OnDragLeave;
        button.Drop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var ok = e.Data.GetDataPresent(ViewportControl.LibraryItemFormat);
        e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
        SetIsDragOver((DependencyObject)sender, ok);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e) => SetIsDragOver((DependencyObject)sender, false);

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var button = (Button)sender;
        SetIsDragOver(button, false);
        if (GetFolder(button) is not { } folder || e.Data.GetData(ViewportControl.LibraryItemFormat) is not string id) return;
        var command = GetCommand(button);
        var drop = new LibraryFolderDrop(folder, id);
        if (command?.CanExecute(drop) == true) command.Execute(drop);
        e.Handled = true;
    }
}
