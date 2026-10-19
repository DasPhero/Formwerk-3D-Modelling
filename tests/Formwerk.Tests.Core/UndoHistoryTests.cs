using Formwerk.Core.Commands;

namespace Formwerk.Tests.Core;

public class UndoHistoryTests
{
    private sealed class SetValue(Cell cell, int value, string name = "Set") : IDocumentCommand
    {
        private int _old;
        public string Name => name;
        public void Do() { _old = cell.Value; cell.Value = value; }
        public void Undo() => cell.Value = _old;
    }

    private sealed class Cell { public int Value; }

    [Fact]
    public void Execute_applies_and_undo_redo_restore()
    {
        var cell = new Cell();
        var history = new UndoHistory();

        history.Execute(new SetValue(cell, 5));
        history.Execute(new SetValue(cell, 9));
        Assert.Equal(9, cell.Value);

        history.Undo();
        Assert.Equal(5, cell.Value);
        history.Undo();
        Assert.Equal(0, cell.Value);
        Assert.False(history.CanUndo);

        history.Redo();
        history.Redo();
        Assert.Equal(9, cell.Value);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Executing_after_undo_discards_redo_branch()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        history.Execute(new SetValue(cell, 1));
        history.Execute(new SetValue(cell, 2));
        history.Undo();

        history.Execute(new SetValue(cell, 7));

        Assert.False(history.CanRedo);
        Assert.Equal(2, history.Entries.Count);
    }

    // E-01: click on a history entry jumps to that state.
    [Fact]
    public void JumpTo_moves_forward_and_backward()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        history.Execute(new SetValue(cell, 1, "A"));
        history.Execute(new SetValue(cell, 2, "B"));
        history.Execute(new SetValue(cell, 3, "C"));

        history.JumpTo(1);
        Assert.Equal(1, cell.Value);
        Assert.Equal(1, history.CurrentIndex);

        history.JumpTo(3);
        Assert.Equal(3, cell.Value);

        history.JumpTo(0);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void JumpTo_rejects_out_of_range_index()
    {
        var history = new UndoHistory();
        Assert.Throws<ArgumentOutOfRangeException>(() => history.JumpTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => history.JumpTo(-1));
    }

    [Fact]
    public void History_is_unlimited()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        for (var i = 1; i <= 10_000; i++) history.Execute(new SetValue(cell, i));
        Assert.Equal(10_000, history.Entries.Count);
        history.JumpTo(0);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void Changed_event_fires_on_every_state_change()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        var count = 0;
        history.Changed += (_, _) => count++;

        history.Execute(new SetValue(cell, 1));
        history.Undo();
        history.Redo();

        Assert.Equal(3, count);
    }

    [Fact]
    public void Clear_forgets_everything_without_undoing()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        var changed = 0;
        history.Execute(new SetValue(cell, 5));
        history.Changed += (_, _) => changed++;

        history.Clear();

        Assert.Equal(5, cell.Value);
        Assert.Empty(history.Entries);
        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Equal(0, history.CurrentIndex);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Failing_command_is_not_recorded()
    {
        var history = new UndoHistory();
        Assert.Throws<InvalidOperationException>(() => history.Execute(new Failing()));
        Assert.False(history.CanUndo);
        Assert.Empty(history.Entries);
    }

    // ---- Executed and AmendLast (F-FLR-01: a follow-up change joins the entry that caused it) ------

    [Fact]
    public void Executed_is_raised_for_new_commands_only()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        var seen = new List<string>();
        history.Executed += (_, command) => seen.Add(command.Name);

        history.Execute(new SetValue(cell, 1, "one"));
        history.Undo();
        history.Redo();
        history.JumpTo(0);
        history.Execute(new SetValue(cell, 2, "two"));

        Assert.Equal(["one", "two"], seen);
    }

    [Fact]
    public void AmendLast_joins_the_command_to_the_last_entry()
    {
        var a = new Cell();
        var b = new Cell();
        var history = new UndoHistory();
        history.Execute(new SetValue(a, 5, "Turn"));

        history.AmendLast(new SetValue(b, 7));

        Assert.Equal(7, b.Value);
        var entry = Assert.Single(history.Entries);
        Assert.Equal("Turn", entry.Name);
        Assert.Equal(1, history.CurrentIndex);

        history.Undo();
        Assert.Equal(0, a.Value);
        Assert.Equal(0, b.Value);

        history.Redo();
        Assert.Equal(5, a.Value);
        Assert.Equal(7, b.Value);
    }

    [Fact]
    public void AmendLast_tells_listeners_and_does_not_raise_Executed()
    {
        var history = new UndoHistory();
        history.Execute(new SetValue(new Cell(), 1));
        var changed = 0;
        var executed = 0;
        history.Changed += (_, _) => changed++;
        history.Executed += (_, _) => executed++;

        history.AmendLast(new SetValue(new Cell(), 2));

        Assert.Equal(1, changed);
        Assert.Equal(0, executed);
    }

    [Fact]
    public void AmendLast_can_be_repeated()
    {
        var cell = new Cell();
        var other = new Cell();
        var history = new UndoHistory();
        history.Execute(new SetValue(cell, 1));
        history.AmendLast(new SetValue(other, 2));
        history.AmendLast(new SetValue(other, 3));

        Assert.Single(history.Entries);
        Assert.Equal(3, other.Value);
        history.Undo();
        Assert.Equal(0, other.Value);
        Assert.Equal(0, cell.Value);
    }

    [Fact]
    public void AmendLast_needs_a_last_entry_that_is_applied()
    {
        var history = new UndoHistory();
        Assert.Throws<InvalidOperationException>(() => history.AmendLast(new SetValue(new Cell(), 1)));

        history.Execute(new SetValue(new Cell(), 1));
        history.Undo();
        Assert.Throws<InvalidOperationException>(() => history.AmendLast(new SetValue(new Cell(), 2)));
    }

    [Fact]
    public void A_failing_amendment_leaves_the_entry_as_it_was()
    {
        var cell = new Cell();
        var history = new UndoHistory();
        history.Execute(new SetValue(cell, 4));

        Assert.Throws<InvalidOperationException>(() => history.AmendLast(new Failing()));

        Assert.Equal(4, cell.Value);
        history.Undo();
        Assert.Equal(0, cell.Value);
    }

    private sealed class Failing : IDocumentCommand
    {
        public string Name => "Failing";
        public void Do() => throw new InvalidOperationException("boom");
        public void Undo() { }
    }
}
