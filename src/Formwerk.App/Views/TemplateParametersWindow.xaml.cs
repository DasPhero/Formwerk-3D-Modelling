using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>The form of a template with shared parameters (F-TPL-06). View code only; everything else is in <see cref="TemplateParametersViewModel"/>.</summary>
public partial class TemplateParametersWindow : Window
{
    private readonly TemplateParametersViewModel _viewModel;
    private bool _focused;

    public TemplateParametersWindow(TemplateParametersViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsValid) DialogResult = true;
    }

    // the first field gets the focus with its value selected, so typing replaces the stored value
    private void OnFieldLoaded(object sender, RoutedEventArgs e)
    {
        if (_focused || sender is not TextBox box) return;
        _focused = true;
        Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
}
