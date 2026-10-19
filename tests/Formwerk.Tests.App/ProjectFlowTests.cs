using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>New, open, save, unsaved-changes protection, autosave and crash recovery (E-05, N-ROB-02).</summary>
public sealed class ProjectFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-flow-{Guid.NewGuid():N}");
    private readonly AutosaveStore _autosave;

    public ProjectFlowTests()
    {
        Directory.CreateDirectory(_folder);
        _autosave = new AutosaveStore(Path.Combine(_folder, "autosave"));
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string P(string name) => Path.Combine(_folder, name);

    private MainViewModel NewViewModel(FakeDialogs dialogs) => new(new ManifoldKernel(), dialogs, _autosave);

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private bool AutosaveExists => _autosave.TryGetRecovery(out _);

    // ---- title and dirty state -------------------------------------------------------------------

    [UiFact]
    public void A_new_project_is_untitled_and_clean()
    {
        var vm = NewViewModel(new FakeDialogs());
        Assert.Equal("Untitled - Formwerk", vm.WindowTitle);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.FilePath);
    }

    [UiFact]
    public async Task Editing_marks_the_project_as_modified_in_the_title()
    {
        var vm = NewViewModel(new FakeDialogs());
        await Add(vm, "box");

        Assert.True(vm.IsDirty);
        Assert.Equal("Untitled* - Formwerk", vm.WindowTitle);
    }

    [UiFact]
    public async Task Saving_writes_the_file_clears_the_marker_and_names_the_window()
    {
        var path = P("bracket.fwp");
        var vm = NewViewModel(new FakeDialogs { SavePath = path });
        await Add(vm, "box");

        Assert.True(vm.Save());

        Assert.True(File.Exists(path));
        Assert.Equal(path, vm.FilePath);
        Assert.False(vm.IsDirty);
        Assert.Equal("bracket.fwp - Formwerk", vm.WindowTitle);
        Assert.Contains("Saved", vm.StatusText);
        Assert.NotNull(ProjectFile.Load(path));
    }

    [UiFact]
    public async Task Missing_extension_is_added()
    {
        var vm = NewViewModel(new FakeDialogs { SavePath = P("noextension") });
        await Add(vm, "box");

        vm.Save();

        Assert.True(File.Exists(P("noextension.fwp")));
        Assert.EndsWith("noextension.fwp", vm.FilePath);
    }

    [UiFact]
    public async Task Undoing_back_to_the_saved_state_makes_it_clean_again()
    {
        var vm = NewViewModel(new FakeDialogs { SavePath = P("a.fwp") });
        await Add(vm, "box");
        vm.Save();

        await Add(vm, "sphere");
        Assert.True(vm.IsDirty);

        vm.UndoCommand.Execute(null);
        Assert.False(vm.IsDirty);

        vm.RedoCommand.Execute(null);
        Assert.True(vm.IsDirty);
    }

    // A new edit after undoing past the saved state replaces the saved branch: it can never be clean by position alone.
    [UiFact]
    public async Task A_different_edit_at_the_same_history_position_is_still_modified()
    {
        var vm = NewViewModel(new FakeDialogs { SavePath = P("a.fwp") });
        await Add(vm, "box");
        vm.Save();

        vm.UndoCommand.Execute(null);
        await Add(vm, "sphere"); // same history length as when saved, different content

        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public async Task Cancelling_save_as_changes_nothing()
    {
        var dialogs = new FakeDialogs { SavePath = null };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        Assert.False(vm.Save());

        Assert.Null(vm.FilePath);
        Assert.True(vm.IsDirty);
        Assert.Empty(dialogs.Errors);
    }

    [UiFact]
    public async Task Saving_again_does_not_ask_for_a_file_name()
    {
        var dialogs = new FakeDialogs { SavePath = P("a.fwp") };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");
        vm.Save();
        await Add(vm, "sphere");

        vm.SaveProjectCommand.Execute(null);

        Assert.Equal(1, dialogs.SavePrompts);
        Assert.False(vm.IsDirty);
        Assert.Equal(2, ProjectFile.Load(P("a.fwp")).Root.Children.Count);
    }

    [UiFact]
    public async Task Save_as_asks_again_and_switches_to_the_new_file()
    {
        var dialogs = new FakeDialogs { SavePath = P("a.fwp") };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");
        vm.Save();

        dialogs.SavePath = P("b.fwp");
        vm.SaveProjectAsCommand.Execute(null);

        Assert.Equal(P("b.fwp"), vm.FilePath);
        Assert.True(File.Exists(P("a.fwp")));
        Assert.True(File.Exists(P("b.fwp")));
    }

    [UiFact]
    public async Task Failing_save_shows_a_readable_error_and_keeps_the_work_marked_unsaved()
    {
        var dialogs = new FakeDialogs { SavePath = P(Path.Combine("no-such-folder", "x.fwp")) };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        Assert.False(vm.Save());

        Assert.Single(dialogs.Errors);
        Assert.Contains("could not be saved", dialogs.Errors[0]);
        Assert.Null(vm.FilePath);
        Assert.True(vm.IsDirty);
    }

    // ---- opening ---------------------------------------------------------------------------------

    private async Task<string> SavedProject(string name, params string[] shapes)
    {
        var path = P(name);
        var vm = NewViewModel(new FakeDialogs { SavePath = path });
        foreach (var shape in shapes) await Add(vm, shape);
        vm.Document.Variables["Wand"] = "2";
        vm.Document.Settings.Tolerance = 0.3;
        vm.Save();
        return path;
    }

    [UiFact]
    public async Task Opening_restores_the_project_clean_without_history_and_shows_it()
    {
        var path = await SavedProject("model.fwp", "box", "cylinder");
        var vm = NewViewModel(new FakeDialogs());

        Assert.True(vm.OpenPath(path));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Equal(2, vm.ViewportItems.Count);
        Assert.Equal("2", vm.Document.Variables["Wand"]);
        Assert.Equal(0.3, vm.Document.Settings.Tolerance);
        Assert.Equal(path, vm.FilePath);
        Assert.False(vm.IsDirty);
        Assert.Equal("model.fwp - Formwerk", vm.WindowTitle);
        Assert.False(vm.UndoCommand.CanExecute(null)); // loading cannot be undone
        Assert.Empty(vm.SelectedIds);
        Assert.Null(vm.Properties);
        Assert.Contains("Opened", vm.StatusText);
    }

    [UiFact]
    public async Task Opened_project_can_be_edited_and_undone_normally()
    {
        var path = await SavedProject("model.fwp", "box");
        var vm = NewViewModel(new FakeDialogs());
        vm.OpenPath(path);

        vm.SelectOnly([vm.Document.Root.Children[0].Id]);
        vm.Properties!.Parameters[0].Text = "44";
        await vm.WaitForEvaluationAsync();

        Assert.True(vm.IsDirty);
        Assert.Equal(44, vm.ViewportItems.Single().Mesh.ComputeBounds().Size.X, 3);
        vm.UndoCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Save_then_open_gives_the_same_geometry()
    {
        var path = P("round.fwp");
        var first = NewViewModel(new FakeDialogs { SavePath = path });
        await Add(first, "box");
        first.Properties!.Position[0].Text = "+12";
        first.Properties.Parameters[2].Text = "35";
        await first.WaitForEvaluationAsync();
        first.Save();
        var expected = first.ViewportItems.Single().Mesh;

        var second = NewViewModel(new FakeDialogs());
        second.OpenPath(path);
        await second.WaitForEvaluationAsync();
        var actual = second.ViewportItems.Single().Mesh;

        Assert.Equal(expected.ComputeVolume(), actual.ComputeVolume(), 3);
        Assert.Equal(expected.ComputeBounds(), actual.ComputeBounds());
    }

    [UiFact]
    public async Task Damaged_file_shows_a_readable_error_and_keeps_the_current_project()
    {
        var bad = P("bad.fwp");
        File.WriteAllText(bad, "this is not a project");
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        Assert.False(vm.OpenPath(bad));

        Assert.Single(dialogs.Errors);
        Assert.Contains("could not be opened", dialogs.Errors[0]);
        Assert.Single(vm.Document.Root.Children);
        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public async Task Open_command_asks_about_unsaved_changes_first()
    {
        var other = await SavedProject("other.fwp", "sphere");
        var dialogs = new FakeDialogs { OpenPath = other, UnsavedAnswer = UnsavedChoice.Cancel };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        vm.OpenCommand.Execute(null);
        Assert.Equal(1, dialogs.UnsavedPrompts);
        Assert.Equal("Untitled", dialogs.LastUnsavedDocumentName);
        Assert.Equal("box", ((PrimitiveNode)vm.Document.Root.Children.Single()).GeneratorId); // cancelled: untouched

        dialogs.UnsavedAnswer = UnsavedChoice.Discard;
        vm.OpenCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal("sphere", ((PrimitiveNode)vm.Document.Root.Children.Single()).GeneratorId);
    }

    [UiFact]
    public async Task Open_command_can_save_first()
    {
        var other = await SavedProject("other.fwp", "sphere");
        var target = P("mine.fwp");
        var dialogs = new FakeDialogs { OpenPath = other, SavePath = target, UnsavedAnswer = UnsavedChoice.Save };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        vm.OpenCommand.Execute(null);

        Assert.True(File.Exists(target));
        Assert.Equal("box", ((PrimitiveNode)ProjectFile.Load(target).Root.Children.Single()).GeneratorId);
        Assert.Equal(other, vm.FilePath);
    }

    [UiFact]
    public void Open_command_without_changes_does_not_ask()
    {
        var dialogs = new FakeDialogs { OpenPath = null };
        var vm = NewViewModel(dialogs);

        vm.OpenCommand.Execute(null);

        Assert.Equal(0, dialogs.UnsavedPrompts);
    }

    // ---- new -------------------------------------------------------------------------------------

    [UiFact]
    public async Task New_project_discards_after_confirmation()
    {
        var dialogs = new FakeDialogs { UnsavedAnswer = UnsavedChoice.Discard };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        vm.NewCommand.Execute(null);

        Assert.Empty(vm.Document.Root.Children);
        Assert.Empty(vm.Nodes);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.FilePath);
        Assert.False(vm.UndoCommand.CanExecute(null));
    }

    [UiFact]
    public async Task New_project_can_be_cancelled()
    {
        var dialogs = new FakeDialogs { UnsavedAnswer = UnsavedChoice.Cancel };
        var vm = NewViewModel(dialogs);
        await Add(vm, "box");

        vm.NewCommand.Execute(null);

        Assert.Single(vm.Document.Root.Children);
        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public async Task New_project_on_a_clean_project_does_not_ask()
    {
        var path = await SavedProject("a.fwp", "box");
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);
        vm.OpenPath(path);

        vm.NewCommand.Execute(null);

        Assert.Equal(0, dialogs.UnsavedPrompts);
        Assert.Empty(vm.Document.Root.Children);
        Assert.Equal("Untitled - Formwerk", vm.WindowTitle);
    }

    // ---- closing ---------------------------------------------------------------------------------

    [UiFact]
    public void Closing_a_clean_project_needs_no_question()
    {
        var dialogs = new FakeDialogs();
        Assert.True(NewViewModel(dialogs).TryClose());
        Assert.Equal(0, dialogs.UnsavedPrompts);
    }

    [Theory]
    [InlineData(UnsavedChoice.Cancel, false)]
    [InlineData(UnsavedChoice.Discard, true)]
    public async Task Closing_with_unsaved_changes_follows_the_answer(UnsavedChoice answer, bool closes)
    {
        var vm = NewViewModel(new FakeDialogs { UnsavedAnswer = answer });
        await Add(vm, "box");

        Assert.Equal(closes, vm.TryClose());
    }

    [UiFact]
    public async Task Closing_and_saving_writes_the_file()
    {
        var path = P("close.fwp");
        var vm = NewViewModel(new FakeDialogs { UnsavedAnswer = UnsavedChoice.Save, SavePath = path });
        await Add(vm, "box");

        Assert.True(vm.TryClose());
        Assert.True(File.Exists(path));
    }

    [UiFact]
    public async Task Closing_does_not_happen_when_the_save_was_cancelled()
    {
        var vm = NewViewModel(new FakeDialogs { UnsavedAnswer = UnsavedChoice.Save, SavePath = null });
        await Add(vm, "box");

        Assert.False(vm.TryClose());
    }

    // ---- autosave and recovery (E-05) ------------------------------------------------------------

    [UiFact]
    public async Task Autosave_writes_only_when_there_is_unsaved_work()
    {
        var vm = NewViewModel(new FakeDialogs());
        Assert.False(await vm.AutosaveAsync());
        Assert.False(AutosaveExists);

        await Add(vm, "box");
        Assert.True(await vm.AutosaveAsync());
        Assert.True(AutosaveExists);
    }

    [UiFact]
    public async Task Saving_removes_the_autosave()
    {
        var vm = NewViewModel(new FakeDialogs { SavePath = P("a.fwp") });
        await Add(vm, "box");
        await vm.AutosaveAsync();

        vm.Save();

        Assert.False(AutosaveExists);
    }

    [UiFact]
    public async Task Undoing_back_to_the_saved_state_removes_the_stale_autosave()
    {
        var vm = NewViewModel(new FakeDialogs { SavePath = P("a.fwp") });
        await Add(vm, "box");
        vm.Save();
        await Add(vm, "sphere");
        await vm.AutosaveAsync();
        Assert.True(AutosaveExists);

        vm.UndoCommand.Execute(null);

        Assert.False(AutosaveExists);
    }

    [UiFact]
    public async Task A_deliberate_exit_leaves_nothing_to_recover()
    {
        var vm = NewViewModel(new FakeDialogs { UnsavedAnswer = UnsavedChoice.Discard });
        await Add(vm, "box");
        await vm.AutosaveAsync();

        vm.TryClose();

        Assert.False(AutosaveExists);
    }

    // After a forced termination the next start offers the autosaved work.
    [UiFact]
    public async Task After_a_crash_the_next_start_offers_recovery_and_restores_the_work()
    {
        var crashed = NewViewModel(new FakeDialogs());
        await Add(crashed, "box");
        await Add(crashed, "cylinder");
        await crashed.AutosaveAsync();
        // ... the process is killed here: no TryClose, no clean-up.

        var dialogs = new FakeDialogs { RecoveryAnswer = true };
        var restarted = NewViewModel(dialogs);
        var recovered = restarted.OfferRecovery();
        await restarted.WaitForEvaluationAsync();

        Assert.True(recovered);
        Assert.Equal(1, dialogs.RecoveryPrompts);
        Assert.Null(dialogs.LastRecoveryDocumentName);
        Assert.Equal(2, restarted.Document.Root.Children.Count);
        Assert.Equal(2, restarted.ViewportItems.Count);
        Assert.True(restarted.IsDirty); // not in any file yet
        Assert.Equal("Untitled* - Formwerk", restarted.WindowTitle);
        Assert.Contains("Recovered", restarted.StatusText);
        Assert.True(AutosaveExists); // kept until the user saves
    }

    [UiFact]
    public async Task Recovered_work_that_belonged_to_a_file_is_saved_back_to_that_file()
    {
        var path = await SavedProject("work.fwp", "box");
        var crashed = NewViewModel(new FakeDialogs());
        crashed.OpenPath(path);
        crashed.AddShapeCommand.Execute("sphere");
        await crashed.WaitForEvaluationAsync();
        await crashed.AutosaveAsync();

        var dialogs = new FakeDialogs();
        var restarted = NewViewModel(dialogs);
        restarted.OfferRecovery();

        Assert.Equal("work.fwp", dialogs.LastRecoveryDocumentName);
        Assert.Equal(path, restarted.FilePath);
        Assert.Equal(2, restarted.Document.Root.Children.Count);

        restarted.SaveProjectCommand.Execute(null);

        Assert.Equal(2, ProjectFile.Load(path).Root.Children.Count);
        Assert.False(restarted.IsDirty);
        Assert.False(AutosaveExists);
    }

    [UiFact]
    public async Task Declining_recovery_deletes_the_autosave()
    {
        var crashed = NewViewModel(new FakeDialogs());
        await Add(crashed, "box");
        await crashed.AutosaveAsync();

        var restarted = NewViewModel(new FakeDialogs { RecoveryAnswer = false });

        Assert.False(restarted.OfferRecovery());
        Assert.Empty(restarted.Document.Root.Children);
        Assert.False(AutosaveExists);
    }

    [UiFact]
    public void Nothing_to_recover_means_no_question()
    {
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);

        Assert.False(vm.OfferRecovery());
        Assert.Equal(0, dialogs.RecoveryPrompts);
    }

    [UiFact]
    public async Task Damaged_autosave_is_reported_and_removed()
    {
        var crashed = NewViewModel(new FakeDialogs());
        await Add(crashed, "box");
        await crashed.AutosaveAsync();
        File.WriteAllText(Path.Combine(_autosave.Directory, "autosave.fwp"), "garbage");

        var dialogs = new FakeDialogs();
        var restarted = NewViewModel(dialogs);

        Assert.False(restarted.OfferRecovery());
        Assert.Single(dialogs.Errors);
        Assert.False(AutosaveExists);
    }

    [UiFact]
    public async Task Autosave_does_not_block_editing_and_captures_the_state_at_the_time_of_the_call()
    {
        var vm = NewViewModel(new FakeDialogs());
        await Add(vm, "box");

        var pending = vm.AutosaveAsync();
        await Add(vm, "sphere"); // edit while the file is being written
        await pending;

        Assert.Single(_autosave.Load().Root.Children);
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task Viewport_selection_and_properties_reset_after_opening()
    {
        var path = await SavedProject("a.fwp", "box");
        var vm = NewViewModel(new FakeDialogs());
        await Add(vm, "sphere");
        Assert.NotNull(vm.Properties);

        vm.OpenCommand.Execute(null); // the unsaved-changes prompt is cancelled: the selection must survive
        Assert.NotNull(vm.Properties);

        vm.OpenPath(path);
        Assert.Null(vm.Properties);
        Assert.Empty(vm.SelectedIds);
    }
}
