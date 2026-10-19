using System.Windows;
using System.Windows.Input;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>The "Save as Template" dialog (F-TPL-01). View code only; everything else is in <see cref="SaveTemplateViewModel"/>.</summary>
public partial class SaveTemplateWindow : Window
{
    private readonly SaveTemplateViewModel _viewModel;

    public SaveTemplateWindow(SaveTemplateViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        // after the window is really shown: the suggested name is selected, so typing replaces it
        Loaded += (_, _) => Dispatcher.BeginInvoke(() =>
        {
            NameBox.Focus();
            Keyboard.Focus(NameBox);
            NameBox.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsValid) DialogResult = true;
    }
}
