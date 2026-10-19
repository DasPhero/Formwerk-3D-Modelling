using Formwerk.App.Resources;
using Formwerk.Core.Commands;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The option "keep on floor" (F-FLR-01). After a new command was executed (or when the option is switched on) the next evaluation
/// result is checked: every unlocked top-level object that does not rest on Z = 0 is moved there. The move joins the entry that
/// caused it, so one undo takes back both. Undo, redo, opening files and the like are not corrected.
/// </summary>
public sealed partial class MainViewModel
{
    private bool _floorPending;
    private IDocumentCommand? _floorTrigger;

    /// <summary>A new command was executed: its result is checked, and a correction joins that command's entry.</summary>
    private void ExpectFloorCheck(IDocumentCommand command)
    {
        if (!AlwaysOnFloor) return;
        _floorPending = true;
        _floorTrigger = command;
    }

    /// <summary>The option was switched on: check the scene as it is; a correction becomes an entry of its own.</summary>
    private void RequestFloorCheck()
    {
        _floorPending = true;
        _floorTrigger = null;
        _ = RequestEvaluation();
    }

    private void KeepOnFloor()
    {
        if (!_floorPending) return;
        _floorPending = false;
        var trigger = _floorTrigger;
        _floorTrigger = null;
        if (!AlwaysOnFloor) return;

        // objects that are only holes stay where they are: nothing is cut by them, and the user put them there
        var changes = FloorChanges(_document.Root.Children, holesToo: false);
        if (changes.Count == 0) return;

        var correction = new SetTransformsCommand(_document, changes, Texts.Get("Name.DropToFloor"));
        var entries = _history.Entries;
        if (trigger is not null && _history.CurrentIndex == entries.Count && ReferenceEquals(entries[^1], trigger))
            _history.AmendLast(correction);
        else
            _history.Execute(correction);
    }
}
