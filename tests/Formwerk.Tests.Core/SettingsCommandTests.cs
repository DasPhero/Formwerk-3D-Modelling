using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class SettingsCommandTests
{
    private static DocumentSettings Settings(float x, float y, float z, double nozzle = 0.4, double tolerance = 0.2) =>
        new() { BuildVolume = new Vector3(x, y, z), NozzleDiameter = nozzle, Tolerance = tolerance };

    [Fact]
    public void Changing_the_settings_is_one_undoable_step()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var revision = doc.Revision;

        history.Execute(new SetSettingsCommand(doc, Settings(180, 200, 220, 0.6, 0.3)));

        Assert.Equal(new Vector3(180, 200, 220), doc.Settings.BuildVolume);
        Assert.Equal(0.6, doc.Settings.NozzleDiameter);
        Assert.Equal(0.3, doc.Settings.Tolerance);
        Assert.Equal(revision + 1, doc.Revision);
        Assert.Equal("Change settings", history.Entries[^1].Name);

        history.Undo();
        Assert.Equal(new Vector3(256, 256, 256), doc.Settings.BuildVolume);
        Assert.Equal(0.4, doc.Settings.NozzleDiameter);
        Assert.Equal(0.2, doc.Settings.Tolerance);

        history.Redo();
        Assert.Equal(new Vector3(180, 200, 220), doc.Settings.BuildVolume);
    }

    [Fact]
    public void The_command_takes_a_copy_so_later_changes_of_the_source_do_not_leak_in()
    {
        var doc = new FormwerkDocument();
        var source = Settings(100, 100, 100);
        var command = new SetSettingsCommand(doc, source, "Set build volume");
        source.BuildVolume = new Vector3(999, 999, 999);

        new UndoHistory().Execute(command);

        Assert.Equal(new Vector3(100, 100, 100), doc.Settings.BuildVolume);
        Assert.Equal("Set build volume", command.Name);
    }

    [Fact]
    public void Identical_settings_report_no_effect()
    {
        var doc = new FormwerkDocument();
        Assert.False(new SetSettingsCommand(doc, doc.Settings.Clone()).HasEffect);
        Assert.True(new SetSettingsCommand(doc, Settings(100, 256, 256)).HasEffect);
    }

    [Fact]
    public void Clone_and_CopyFrom_are_independent_copies()
    {
        var a = Settings(10, 20, 30, 0.5, 0.1);
        var b = a.Clone();
        b.BuildVolume = new Vector3(1, 1, 1);
        Assert.Equal(new Vector3(10, 20, 30), a.BuildVolume);

        var c = new DocumentSettings();
        c.CopyFrom(a);
        Assert.True(c.SameAs(a));
        Assert.False(c.SameAs(b));
    }

    [Fact]
    public void Settings_survive_the_snapshot()
    {
        var doc = new FormwerkDocument();
        new UndoHistory().Execute(new SetSettingsCommand(doc, Settings(150, 160, 170, 0.8, 0.35)));

        var snapshot = doc.Snapshot();

        Assert.True(snapshot.Settings.SameAs(doc.Settings));
    }
}
