namespace Formwerk.Core.Commands;

/// <summary>Several commands that appear as one entry in the history (for example a resize that changes parameters and position).</summary>
public sealed class CompositeCommand(string name, IEnumerable<IDocumentCommand> commands) : IDocumentCommand
{
    private readonly List<IDocumentCommand> _commands = [.. commands];

    public string Name { get; } = name;

    public int Count => _commands.Count;

    public void Do()
    {
        foreach (var command in _commands) command.Do();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--) _commands[i].Undo();
    }
}
