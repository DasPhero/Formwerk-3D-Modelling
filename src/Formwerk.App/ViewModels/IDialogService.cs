namespace Formwerk.App.ViewModels;

public enum UnsavedChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>Everything that needs a modal window. Implemented with real dialogs in the app and faked in tests.</summary>
public interface IDialogService
{
    /// <summary>Returns the chosen path or null when the user cancelled.</summary>
    string? PickSaveFile(string title, string filter, string defaultFileName);

    /// <summary>Returns the chosen path or null when the user cancelled.</summary>
    string? PickOpenFile(string title, string filter);

    UnsavedChoice ConfirmUnsavedChanges(string documentName);

    /// <summary>True when the user wants the autosaved work back.</summary>
    bool ConfirmRecovery(DateTime savedLocalTime, string? documentName);

    void ShowError(string message);

    /// <summary>A yes/no question in the style of the program (for example before a template is deleted); true for yes.</summary>
    bool Confirm(string message);

    /// <summary>A small question with one line of text to type (the name of a folder); null when cancelled.</summary>
    string? AskText(string title, string prompt, string initial);

    /// <summary>Lets the user choose model files to import; empty when cancelled.</summary>
    IReadOnlyList<string> PickImportFiles(string title, string filter);

    /// <summary>
    /// Asks in which unit a model was designed (F-IMP-02). Null when the user cancelled the import.
    /// <paramref name="largestExtentInFile"/> is the size of the model's longest side as it stands in the file.
    /// </summary>
    ImportUnit? PickImportUnit(string modelName, double largestExtentInFile);

    /// <summary>Shows the settings dialog with the current values. Null when the user cancelled.</summary>
    /// <summary>The "Save as Template" dialog (F-TPL-01); true when the user confirmed a valid description.</summary>
    bool ShowSaveTemplate(SaveTemplateViewModel viewModel);

    /// <summary>Asks which template the selection becomes a new version of; true when the user confirmed a choice.</summary>
    bool ShowUpdateTemplate(UpdateTemplateViewModel viewModel);

    /// <summary>The form with the shared parameters of a template that is inserted (F-TPL-06); true when the user confirmed valid values.</summary>
    bool ShowTemplateParameters(TemplateParametersViewModel viewModel);

    SettingsResult? ShowSettings(Formwerk.Core.Model.DocumentSettings project, Formwerk.IO.UserSettings user);
}

/// <summary>The unit a model file was designed in.</summary>
public enum ImportUnit
{
    Millimeters,
    Centimeters,
    Inches,
}

public static class ImportUnitExtensions
{
    /// <summary>How many millimetres one unit of the file is.</summary>
    public static double ToMillimeters(this ImportUnit unit) => unit switch
    {
        ImportUnit.Centimeters => 10,
        ImportUnit.Inches => 25.4,
        _ => 1,
    };
}
