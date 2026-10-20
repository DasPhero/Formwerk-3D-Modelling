using System.Numerics;
using Formwerk.Core.Expressions;

namespace Formwerk.Core.Model;

/// <summary>Project-wide print settings (F-SET-01).</summary>
public sealed class DocumentSettings
{
    /// <summary>Build volume X × Y × Z in mm.</summary>
    public Vector3 BuildVolume { get; set; } = new(256, 256, 256);

    public double NozzleDiameter { get; set; } = 0.4;

    /// <summary>Print tolerance added to holes and fits (F-HOL-04), mm.</summary>
    public double Tolerance { get; set; } = 0.2;

    public DocumentSettings Clone() => new() { BuildVolume = BuildVolume, NozzleDiameter = NozzleDiameter, Tolerance = Tolerance };

    public void CopyFrom(DocumentSettings other)
    {
        BuildVolume = other.BuildVolume;
        NozzleDiameter = other.NozzleDiameter;
        Tolerance = other.Tolerance;
    }

    public bool SameAs(DocumentSettings other) =>
        BuildVolume == other.BuildVolume && NozzleDiameter == other.NozzleDiameter && Tolerance == other.Tolerance;
}

public sealed class FormwerkDocument
{
    public FormwerkDocument()
    {
        Root = new GroupNode("Scene");
    }

    public GroupNode Root { get; }

    public DocumentSettings Settings { get; } = new();

    /// <summary>Project variables (F-PAR-03): name to expression. Later variables may use earlier ones.</summary>
    public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Incremented by every command; the evaluator uses it to detect stale results.</summary>
    public long Revision { get; private set; }

    public event EventHandler? Changed;

    /// <summary>Called by commands after they changed the document.</summary>
    public void NotifyChanged()
    {
        Revision++;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Evaluates the variable table. The order of the table does not matter: a variable may use one that stands below it. Variables
    /// that cannot be resolved are reported in <paramref name="errors"/> (their own error, "depends on a variable with an error", or a
    /// circular reference) and are missing from the result.
    /// </summary>
    public Dictionary<string, double> ResolveVariables(out Dictionary<string, string> errors)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Built in: the print tolerance of the settings (F-HOL-04). A variable with that name (from a hand-edited file) does not count.
        values[VariableNames.Tolerance] = Settings.Tolerance;
        var pending = new List<KeyValuePair<string, string>>();
        foreach (var variable in Variables)
        {
            if (VariableNames.BuiltIn.Contains(variable.Key, StringComparer.OrdinalIgnoreCase))
                errors[variable.Key] = $"'{variable.Key}' is built in (the print tolerance of the project settings); the variable is ignored.";
            else pending.Add(variable);
        }

        // Evaluate again and again until nothing new resolves: a variable that waits for a later one is picked up in the next round.
        var lastError = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool progress;
        do
        {
            progress = false;
            for (var i = 0; i < pending.Count;)
            {
                var (name, expression) = pending[i];
                var result = ExpressionParser.Evaluate(expression, values);
                if (result.IsSuccess)
                {
                    values[name] = result.Value;
                    pending.RemoveAt(i);
                    progress = true;
                }
                else
                {
                    lastError[name] = result.Error!;
                    i++;
                }
            }
        }
        while (progress && pending.Count > 0);

        if (pending.Count == 0) return values;

        // What is left has its own error (a typo, an unknown name), depends on one that has, or is part of a circle.
        var waiting = new HashSet<string>(pending.Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, expression) in pending)
        {
            if (!ExpressionParser.UsedNames(expression).Any(waiting.Contains)) errors[name] = lastError[name];
        }

        bool changed;
        do
        {
            changed = false;
            foreach (var (name, expression) in pending)
            {
                if (errors.ContainsKey(name)) continue;
                var broken = ExpressionParser.UsedNames(expression).FirstOrDefault(errors.ContainsKey);
                if (broken is null) continue;
                errors[name] = $"Depends on '{broken}', which has an error.";
                changed = true;
            }
        }
        while (changed);

        foreach (var (name, _) in pending)
        {
            if (!errors.ContainsKey(name)) errors[name] = "Circular reference between variables.";
        }

        return values;
    }

    /// <summary>
    /// Independent copy with the same node ids, settings and variables. Used to evaluate geometry on a
    /// background thread while the user keeps editing the original.
    /// </summary>
    public FormwerkDocument Snapshot()
    {
        var copy = new FormwerkDocument();
        copy.Settings.BuildVolume = Settings.BuildVolume;
        copy.Settings.NozzleDiameter = Settings.NozzleDiameter;
        copy.Settings.Tolerance = Settings.Tolerance;
        foreach (var (name, expression) in Variables) copy.Variables[name] = expression;
        foreach (var child in Root.Children) copy.Root.InsertChild(copy.Root.Children.Count, child.Clone());
        return copy;
    }

    /// <summary>
    /// Replaces the whole content (nodes, settings, variables) by that of <paramref name="other"/>, which is consumed.
    /// Used by New and Open; the caller clears the undo history. Raises <see cref="Changed"/> once.
    /// </summary>
    public void ReplaceWith(FormwerkDocument other)
    {
        ArgumentNullException.ThrowIfNull(other);
        if (other == this) return;

        foreach (var child in Root.Children.ToList()) Root.RemoveChild(child);
        foreach (var child in other.Root.Children.ToList())
        {
            other.Root.RemoveChild(child);
            Root.InsertChild(Root.Children.Count, child);
        }

        Settings.BuildVolume = other.Settings.BuildVolume;
        Settings.NozzleDiameter = other.Settings.NozzleDiameter;
        Settings.Tolerance = other.Settings.Tolerance;
        Variables.Clear();
        foreach (var (name, expression) in other.Variables) Variables[name] = expression;
        NotifyChanged();
    }

    public Node? FindNode(Guid id) => Root.Id == id ? Root : Root.Descendants().FirstOrDefault(n => n.Id == id);
}
