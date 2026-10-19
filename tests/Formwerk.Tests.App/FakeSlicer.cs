using Formwerk.App.ViewModels;

namespace Formwerk.Tests.App;

/// <summary>Stand-in for the slicer launcher: records what would have been started and keeps a copy of the file as it was at that moment.</summary>
public sealed class FakeSlicer : ISlicerLauncher
{
    /// <summary>False makes the launch fail with <see cref="Error"/>.</summary>
    public bool Succeeds { get; set; } = true;

    public string Error { get; set; } = "The program could not be started.";

    public List<(string Program, string File, byte[]? Content)> Launches { get; } = [];

    public bool TryStart(string program, string file, out string? error)
    {
        Launches.Add((program, file, System.IO.File.Exists(file) ? System.IO.File.ReadAllBytes(file) : null));
        error = Succeeds ? null : Error;
        return Succeeds;
    }
}
