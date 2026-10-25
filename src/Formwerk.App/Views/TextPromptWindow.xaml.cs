using System.Windows;

namespace Formwerk.App.Views;

/// <summary>A small window with one line of text to type, for example the name of a library folder. View code only.</summary>
public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        Prompt.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    /// <summary>What was typed; valid when the window was closed with OK.</summary>
    public string Answer { get; private set; } = "";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Answer = Input.Text;
        DialogResult = true;
    }
}
