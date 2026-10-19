using System.Windows;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Microsoft.Win32;

namespace Formwerk.App.Views;

public sealed class DialogService : IDialogService
{
    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultFileName,
            AddExtension = true,
            OverwritePrompt = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickOpenFile(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public UnsavedChoice ConfirmUnsavedChanges(string documentName)
    {
        var answer = MessageBox.Show(
            Texts.Format("Dlg.Unsaved", documentName), Texts.Get("App.Title"),
            MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Yes);
        return answer switch
        {
            MessageBoxResult.Yes => UnsavedChoice.Save,
            MessageBoxResult.No => UnsavedChoice.Discard,
            _ => UnsavedChoice.Cancel,
        };
    }

    public bool ConfirmRecovery(DateTime savedLocalTime, string? documentName)
    {
        var text = documentName is null
            ? Texts.Format("Dlg.RecoverUntitled", savedLocalTime.ToString("g"))
            : Texts.Format("Dlg.Recover", documentName, savedLocalTime.ToString("g"));
        return MessageBox.Show(text, Texts.Get("App.Title"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes)
               == MessageBoxResult.Yes;
    }

    public IReadOnlyList<string> PickImportFiles(string title, string filter)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = true };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public ImportUnit? PickImportUnit(string modelName, double largestExtentInFile)
    {
        var window = new ImportUnitWindow(modelName, largestExtentInFile) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true ? window.Choice : null;
    }

    public void ShowError(string message) =>
        MessageBox.Show(message, Texts.Get("App.Title"), MessageBoxButton.OK, MessageBoxImage.Error);

    public bool ShowSaveTemplate(Formwerk.App.ViewModels.SaveTemplateViewModel viewModel) =>
        new SaveTemplateWindow(viewModel) { Owner = Application.Current?.MainWindow }.ShowDialog() == true;

    public SettingsResult? ShowSettings(Formwerk.Core.Model.DocumentSettings project, Formwerk.IO.UserSettings user)
    {
        var window = new SettingsWindow(new SettingsViewModel(project, user)) { Owner = Application.Current?.MainWindow };
        return window.ShowDialog() == true ? window.Result : null;
    }
}
