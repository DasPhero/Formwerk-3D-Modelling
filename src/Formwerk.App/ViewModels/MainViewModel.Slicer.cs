using System.IO;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Features.Evaluation;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>3MF export and "Open in Slicer" (E-03).</summary>
public sealed partial class MainViewModel
{
    private readonly ISlicerLauncher _slicer;

    /// <summary>The calculated result as 3MF objects: every visible solid with its name and color; holes are already subtracted.</summary>
    private List<ThreeMfObject> ExportObjects() =>
        [.. (_lastResult?.Parts ?? []).Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty)
            .Select(p => new ThreeMfObject(p.Source.Name, p.Mesh, p.Source.Color))];

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export3mf()
    {
        if (ExportObjects().Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToExport"));
            return;
        }

        var suggestion = Path.GetFileNameWithoutExtension(DocumentName) + ThreeMfWriter.Extension;
        var path = _dialogs.PickSaveFile(Texts.Get("Dlg.Export3mfTitle"), Texts.Get("Dlg.Export3mfFilter"), suggestion);
        if (path is null) return;
        if (!path.EndsWith(ThreeMfWriter.Extension, StringComparison.OrdinalIgnoreCase)) path += ThreeMfWriter.Extension;

        if (await WriteThreeMfAsync(path) is { } count) SetNotice(Texts.Format("Msg.Exported3mf", count, path));
    }

    /// <summary>
    /// Exports the result to a temporary 3MF file and starts the slicer with it. Without a (valid) slicer path the user is asked once;
    /// the program is remembered after it has started.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task OpenInSlicer()
    {
        if (ExportObjects().Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToExport"));
            return;
        }

        var program = _user.SlicerPath;
        var ask = string.IsNullOrWhiteSpace(program) || !File.Exists(program);
        if (ask)
        {
            program = _dialogs.PickOpenFile(Texts.Get("Dlg.SlicerTitle"), Texts.Get("Settings.ProgramFilter"));
            if (string.IsNullOrWhiteSpace(program)) return;
        }

        var exe = program!;
        var file = Path.Combine(Path.GetTempPath(), "Formwerk", SafeFileName(Path.GetFileNameWithoutExtension(DocumentName)) + ThreeMfWriter.Extension);
        if (await WriteThreeMfAsync(file) is null) return;

        if (!_slicer.TryStart(exe, file, out var error))
        {
            _dialogs.ShowError(Texts.Format("Dlg.SlicerFailed", Path.GetFileName(exe), error ?? ""));
            return;
        }

        if (ask)
        {
            _user = _user with { SlicerPath = exe };
            SaveUserSettings();
            OnPropertyChanged(nameof(SlicerPath));
        }

        SetNotice(Texts.Format("Msg.OpenedInSlicer", Path.GetFileNameWithoutExtension(exe)));
    }

    /// <summary>Writes the 3MF on a background thread. Returns the number of objects, or null after showing why it failed.</summary>
    private async Task<int?> WriteThreeMfAsync(string path)
    {
        var objects = ExportObjects();
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await Task.Run(() => ThreeMfWriter.SaveAtomic(path, objects)).ConfigureAwait(true);
            return objects.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            SetNotice(Texts.Format("Msg.ExportFailed", ex.Message));
            return null;
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c)).Trim();
        return clean.Length == 0 ? "model" : clean;
    }
}
