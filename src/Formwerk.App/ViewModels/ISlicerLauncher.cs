namespace Formwerk.App.ViewModels;

/// <summary>Starts the user's slicer program with a model file. Real implementation in the app, faked in tests.</summary>
public interface ISlicerLauncher
{
    /// <summary>Starts <paramref name="program"/> with <paramref name="file"/> as its only argument and does not wait for it.</summary>
    /// <param name="error">Why it failed, in words for the user; null on success.</param>
    bool TryStart(string program, string file, out string? error);
}

/// <summary>Used when no launcher is given (tests that do not care): starting programs is not possible.</summary>
internal sealed class NoSlicerLauncher : ISlicerLauncher
{
    public bool TryStart(string program, string file, out string? error)
    {
        error = "Starting programs is not available.";
        return false;
    }
}
