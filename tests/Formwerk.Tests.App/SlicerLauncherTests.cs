using System.IO;
using Formwerk.App.Views;

namespace Formwerk.Tests.App;

/// <summary>The real launcher starts a real process (Windows PowerShell stands in for the slicer).</summary>
public sealed class SlicerLauncherTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-launch-{Guid.NewGuid():N}");

    public SlicerLauncherTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static string PowerShell => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

    [Fact]
    public async Task The_program_is_started_with_the_file_as_its_argument()
    {
        if (!File.Exists(PowerShell) || _folder.Contains(' ')) return; // PowerShell would split a path with spaces itself

        var marker = Path.Combine(_folder, "marker.txt");
        var script = Path.Combine(_folder, "stand-in.ps1");
        File.WriteAllText(script, $"Set-Content -Path '{marker}' -Value 'started'");

        var started = new SlicerLauncher().TryStart(PowerShell, script, out var error);

        Assert.True(started, error);
        Assert.Null(error);
        for (var i = 0; i < 100 && !File.Exists(marker); i++) await Task.Delay(100); // the program runs on its own, we do not wait for it
        Assert.True(File.Exists(marker), "the stand-in program did not run with the file as argument");
    }

    [Fact]
    public void A_program_that_does_not_exist_is_reported_in_words()
    {
        var started = new SlicerLauncher().TryStart(Path.Combine(_folder, "no-such-slicer.exe"), Path.Combine(_folder, "x.3mf"), out var error);

        Assert.False(started);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void A_file_that_is_not_a_program_is_reported_in_words()
    {
        var notAProgram = Path.Combine(_folder, "notes.txt");
        File.WriteAllText(notAProgram, "text");

        var started = new SlicerLauncher().TryStart(notAProgram, Path.Combine(_folder, "x.3mf"), out var error);

        Assert.False(started);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}
