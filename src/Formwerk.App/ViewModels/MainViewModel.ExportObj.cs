using System.IO;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>OBJ export (E-03): the same objects as the 3MF export, without colors.</summary>
public sealed partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task ExportObj()
    {
        var objects = ExportObjects().Select(o => new ObjObject(o.Name, o.Mesh)).ToList();
        if (objects.Count == 0)
        {
            SetNotice(Texts.Get("Msg.NothingToExport"));
            return;
        }

        var suggestion = Path.GetFileNameWithoutExtension(DocumentName) + ObjWriter.Extension;
        var path = _dialogs.PickSaveFile(Texts.Get("Dlg.ExportObjTitle"), Texts.Get("Dlg.ExportObjFilter"), suggestion);
        if (path is null) return;
        if (!path.EndsWith(ObjWriter.Extension, StringComparison.OrdinalIgnoreCase)) path += ObjWriter.Extension;

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            await Task.Run(() => ObjWriter.SaveAtomic(path, objects)).ConfigureAwait(true);
            SetNotice(Texts.Format("Msg.ExportedObj", objects.Count, path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            SetNotice(Texts.Format("Msg.ExportFailed", ex.Message));
        }
    }
}
