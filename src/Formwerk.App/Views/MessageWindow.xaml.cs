using System.Windows;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>A question or message in the theme of the program. View code only; everything else is in <see cref="MessageViewModel"/>.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow(MessageViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.Answered += (_, _) =>
        {
            DialogResult = true;
        };
    }

    /// <summary>Shows the window above the main window and returns the id of the chosen button (what closing means if none).</summary>
    public static string Ask(MessageViewModel viewModel)
    {
        var owner = Application.Current?.MainWindow;
        new MessageWindow(viewModel) { Owner = owner is { IsVisible: true } ? owner : null }.ShowDialog();
        return viewModel.Result;
    }
}
