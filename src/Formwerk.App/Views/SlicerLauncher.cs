using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Formwerk.App.ViewModels;

namespace Formwerk.App.Views;

/// <summary>Starts the slicer as an ordinary process. No shell, so the path of the model file is one argument and nothing else.</summary>
public sealed class SlicerLauncher : ISlicerLauncher
{
    public bool TryStart(string program, string file, out string? error)
    {
        error = null;
        try
        {
            var info = new ProcessStartInfo(program) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(program) ?? "" };
            info.ArgumentList.Add(file);
            using var process = Process.Start(info);
            if (process is null) error = "The program did not start.";
            return process is not null;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }
}
