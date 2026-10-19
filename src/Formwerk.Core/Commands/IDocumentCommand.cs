namespace Formwerk.Core.Commands;

/// <summary>Every document change is a command (spec section 3/10).</summary>
public interface IDocumentCommand
{
    /// <summary>Shown in the history panel.</summary>
    string Name { get; }

    void Do();

    void Undo();
}
