using System.Windows;
using System.Windows.Controls;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Microsoft.Win32;

namespace Formwerk.App.Views;

/// <summary>The settings dialog (F-SET-01, F-SET-02). View code only; validation and the result come from <see cref="SettingsViewModel"/>.</summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) =>
        {
            BuildXBox.Focus();
            BuildXBox.SelectAll();
        };
    }

    /// <summary>The confirmed values; null unless the dialog was closed with OK.</summary>
    public SettingsResult? Result { get; private set; }

    private void OnPresetChosen(object sender, SelectionChangedEventArgs e)
    {
        if (PresetBox.SelectedItem is PrinterPreset preset) _viewModel.ApplyPresetCommand.Execute(preset);
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = Texts.Get("Settings.Slicer"), Filter = Texts.Get("Settings.ProgramFilter"), CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) _viewModel.SlicerPath = dialog.FileName;
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = _viewModel.Result();
        if (Result is not null) DialogResult = true;
    }
}
