using Formwerk.App.ViewModels;

namespace Formwerk.Tests.App;

/// <summary>Scriptable stand-in for the real dialogs; records what the view model asked.</summary>
public sealed class FakeDialogs : IDialogService
{
    public string? SavePath { get; set; }

    public string? OpenPath { get; set; }

    /// <summary>What the user does in the "Save as Template" dialog; null cancels it.</summary>
    public Func<SaveTemplateViewModel, bool>? SaveTemplateScript { get; set; }

    public bool ShowSaveTemplate(SaveTemplateViewModel viewModel) => SaveTemplateScript?.Invoke(viewModel) ?? false;

    public UnsavedChoice UnsavedAnswer { get; set; } = UnsavedChoice.Cancel;

    public bool RecoveryAnswer { get; set; } = true;

    public List<string> Errors { get; } = [];

    public int UnsavedPrompts { get; private set; }

    public int RecoveryPrompts { get; private set; }

    public int SavePrompts { get; private set; }

    public string? LastUnsavedDocumentName { get; private set; }

    public string? LastRecoveryDocumentName { get; private set; }

    public string? PickSaveFile(string title, string filter, string defaultFileName)
    {
        SavePrompts++;
        return SavePath;
    }

    public int OpenPrompts { get; private set; }

    public string? PickOpenFile(string title, string filter)
    {
        OpenPrompts++;
        return OpenPath;
    }

    public UnsavedChoice ConfirmUnsavedChanges(string documentName)
    {
        UnsavedPrompts++;
        LastUnsavedDocumentName = documentName;
        return UnsavedAnswer;
    }

    public bool ConfirmRecovery(DateTime savedLocalTime, string? documentName)
    {
        RecoveryPrompts++;
        LastRecoveryDocumentName = documentName;
        return RecoveryAnswer;
    }

    public void ShowError(string message) => Errors.Add(message);

    /// <summary>What the file picker returns for Import.</summary>
    public List<string> FilesToImport { get; } = [];

    /// <summary>The answer to the unit question; null means the user cancelled.</summary>
    public ImportUnit? UnitAnswer { get; set; } = ImportUnit.Millimeters;

    public List<(string Name, double Extent)> UnitPrompts { get; } = [];

    public IReadOnlyList<string> PickImportFiles(string title, string filter) => [.. FilesToImport];

    public ImportUnit? PickImportUnit(string modelName, double largestExtentInFile)
    {
        UnitPrompts.Add((modelName, largestExtentInFile));
        return UnitAnswer;
    }

    /// <summary>What the settings dialog returns; null means the user cancelled.</summary>
    public SettingsResult? SettingsAnswer { get; set; }

    public List<(Formwerk.Core.Model.DocumentSettings Project, Formwerk.IO.UserSettings User)> SettingsPrompts { get; } = [];

    public SettingsResult? ShowSettings(Formwerk.Core.Model.DocumentSettings project, Formwerk.IO.UserSettings user)
    {
        SettingsPrompts.Add((project, user));
        return SettingsAnswer;
    }
}
