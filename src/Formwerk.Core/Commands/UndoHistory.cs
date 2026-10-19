namespace Formwerk.Core.Commands;

/// <summary>
/// Unlimited linear undo/redo with random access (E-01).
/// <see cref="CurrentIndex"/> is the number of applied commands: 0 = initial state.
/// </summary>
public sealed class UndoHistory
{
    private readonly List<IDocumentCommand> _entries = [];

    public IReadOnlyList<IDocumentCommand> Entries => _entries;

    public int CurrentIndex { get; private set; }

    public bool CanUndo => CurrentIndex > 0;

    public bool CanRedo => CurrentIndex < _entries.Count;

    public event EventHandler? Changed;

    /// <summary>Raised after a new command was executed (not for undo, redo, jumping or <see cref="AmendLast"/>), after <see cref="Changed"/>.</summary>
    public event EventHandler<IDocumentCommand>? Executed;

    public void Execute(IDocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        command.Do();

        if (CurrentIndex < _entries.Count)
            _entries.RemoveRange(CurrentIndex, _entries.Count - CurrentIndex);
        _entries.Add(command);
        CurrentIndex = _entries.Count;
        OnChanged();
        Executed?.Invoke(this, command);
    }

    /// <summary>
    /// Executes <paramref name="extra"/> and joins it to the last entry, so that one undo takes back both (a change that follows from
    /// the last command, like putting a turned object back on the plate). The entry keeps its name. Only the last applied entry can
    /// be amended.
    /// </summary>
    /// <exception cref="InvalidOperationException">There is no entry, or the history is not at its end.</exception>
    public void AmendLast(IDocumentCommand extra)
    {
        ArgumentNullException.ThrowIfNull(extra);
        if (_entries.Count == 0 || CurrentIndex != _entries.Count)
            throw new InvalidOperationException("Only the last applied entry of the history can be amended.");

        extra.Do();
        var last = _entries[^1];
        _entries[^1] = new CompositeCommand(last.Name, [last, extra]);
        OnChanged();
    }

    /// <summary>Forgets all entries without undoing them (used when a different document is loaded).</summary>
    public void Clear()
    {
        _entries.Clear();
        CurrentIndex = 0;
        OnChanged();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        _entries[CurrentIndex - 1].Undo();
        CurrentIndex--;
        OnChanged();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        _entries[CurrentIndex].Do();
        CurrentIndex++;
        OnChanged();
    }

    /// <summary>Jumps to the state after <paramref name="index"/> applied commands.</summary>
    public void JumpTo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, _entries.Count);

        while (CurrentIndex > index)
        {
            _entries[CurrentIndex - 1].Undo();
            CurrentIndex--;
        }

        while (CurrentIndex < index)
        {
            _entries[CurrentIndex].Do();
            CurrentIndex++;
        }

        OnChanged();
    }

    private void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
