using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Formwerk.App.ViewModels;

namespace Formwerk.App;

/// <summary>View glue only: forwards camera requests to the viewport and single-key shortcuts to commands.</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Closing the window asks about unsaved changes; the decision itself lives in the view model.
        Closing += (_, e) =>
        {
            if (DataContext is MainViewModel vm && !vm.TryClose()) e.Cancel = true;
        };

        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old)
            {
                old.ViewPresetRequested -= OnViewPresetRequested;
                old.FrameSelectionRequested -= OnFrameSelectionRequested;
            }

            if (e.NewValue is MainViewModel current)
            {
                current.ViewPresetRequested += OnViewPresetRequested;
                current.FrameSelectionRequested += OnFrameSelectionRequested;
            }
        };
    }

    private void OnViewPresetRequested(object? sender, Formwerk.Viewport.ViewPreset preset) => Viewport.SetView(preset);

    private void OnFrameSelectionRequested(object? sender, EventArgs e) => Viewport.FrameSelection();

    private void OnExit(object sender, RoutedEventArgs e) => Close();

    /// <summary>Files dragged over the window can be imported (copy cursor); anything else is refused.</summary>
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && DataContext is MainViewModel vm) await vm.ImportFilesAsync(files);
    }

    /// <summary>The template folder may have changed while another program was in front.</summary>
    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.RefreshTemplatesCommand.Execute(null);
    }

    /// <summary>Esc leaves an open group (F-BOO-04) unless something else in the view already used the key.</summary>
    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || e.Handled || e.OriginalSource is TextBoxBase) return;
        if (DataContext is MainViewModel { IsGroupOpen: true } vm)
        {
            vm.ExitGroupCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Single-key shortcuts must not fire while the user types into a text box.</summary>
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBoxBase || Keyboard.Modifiers != ModifierKeys.None) return;
        if (DataContext is not MainViewModel vm) return;

        switch (e.Key)
        {
            case Key.H when vm.ToggleHoleCommand.CanExecute(null):
                vm.ToggleHoleCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Q:
                vm.ActiveTool = Formwerk.Viewport.ViewportTool.Select;
                e.Handled = true;
                break;
            case Key.W:
                vm.ActiveTool = Formwerk.Viewport.ViewportTool.Move;
                e.Handled = true;
                break;
            case Key.E:
                vm.ActiveTool = Formwerk.Viewport.ViewportTool.Rotate;
                e.Handled = true;
                break;
            case Key.R:
                vm.ActiveTool = Formwerk.Viewport.ViewportTool.Scale;
                e.Handled = true;
                break;
            case Key.P:
                vm.ToggleProjectionCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Z:
                vm.CycleDisplayModeCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.B when vm.DropToFloorCommand.CanExecute(null):
                vm.DropToFloorCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Delete when vm.DeleteCommand.CanExecute(null):
                vm.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
