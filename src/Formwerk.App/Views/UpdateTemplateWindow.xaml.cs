using System.Windows;
using System.Windows.Input;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>The "Update Template" dialog (F-TPL-05). View code only; everything else is in <see cref="UpdateTemplateViewModel"/>.</summary>
public partial class UpdateTemplateWindow : Window
{
    private readonly UpdateTemplateViewModel _viewModel;

    public UpdateTemplateWindow(UpdateTemplateViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_viewModel.CanConfirm) DialogResult = true;
    }

    // a double click on an entry confirms it (the click selected it already)
    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.CanConfirm && e.OriginalSource is DependencyObject { } source && IsInsideItem(source)) DialogResult = true;
    }

    private static bool IsInsideItem(DependencyObject source)
    {
        for (var node = source; node is not null; node = System.Windows.Media.VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.ListBoxItem) return true;
        }

        return false;
    }
}
