using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;

namespace Formwerk.App.ViewModels;

/// <summary>One line of the variable table: a name, an expression and what it comes to.</summary>
public sealed partial class VariableRowViewModel : ObservableObject
{
    internal VariableRowViewModel(FieldViewModel name, FieldViewModel expression, Action<VariableRowViewModel> delete)
    {
        Name = name;
        Expression = expression;
        DeleteCommand = new RelayCommand(() => delete(this));
    }

    /// <summary>The name as it is in the document (the field may show a name that was refused).</summary>
    internal string Key { get; set; } = "";

    public FieldViewModel Name { get; }

    public FieldViewModel Expression { get; }

    /// <summary>The value, or what is wrong when the variable cannot be resolved.</summary>
    [ObservableProperty]
    public partial string ValueText { get; set; } = "";

    [ObservableProperty]
    public partial bool HasValueError { get; set; }

    public IRelayCommand DeleteCommand { get; }
}

/// <summary>
/// The table of project variables (F-PAR-03): name and expression per line, edited like any field, every change one undo step.
/// The order does not matter for the result: a variable may use one that is further down.
/// </summary>
public sealed partial class VariablesViewModel : ObservableObject
{
    private readonly FormwerkDocument _document;
    private readonly UndoHistory _history;

    public VariablesViewModel(FormwerkDocument document, UndoHistory history)
    {
        _document = document;
        _history = history;
        AddCommand = new RelayCommand(Add);
        document.Changed += (_, _) => Refresh();
        Refresh();
    }

    public ObservableCollection<VariableRowViewModel> Rows { get; } = [];

    /// <summary>A note about the variable that is always there: the print tolerance of the project settings (F-HOL-04).</summary>
    [ObservableProperty]
    public partial string BuiltInText { get; set; } = "";

    public IRelayCommand AddCommand { get; }

    // ---- commands of the table ---------------------------------------------------------------

    private void Add()
    {
        var name = FreeName(Texts.Get("Var.NewName"));
        List<KeyValuePair<string, string>> table = [.. _document.Variables, new(name, "10")];
        _history.Execute(new SetVariablesCommand(_document, table, Texts.Format("Name.AddVariable", name)));
    }

    private void Delete(VariableRowViewModel row)
    {
        var table = _document.Variables.Where(v => !Same(v.Key, row.Key)).ToList();
        _history.Execute(new SetVariablesCommand(_document, table, Texts.Format("Name.DeleteVariable", row.Key)));
    }

    private string? SetExpression(VariableRowViewModel row, string text)
    {
        var expression = text.Trim();

        var table = _document.Variables.Select(v => Same(v.Key, row.Key) ? new KeyValuePair<string, string>(v.Key, expression) : v).ToList();
        if (Resolve(table).TryGetValue(row.Key, out var error)) return error; // the change would not work: it is not made

        var command = new SetVariablesCommand(_document, table, Texts.Format("Name.SetVariable", row.Key));
        if (!command.HasEffect) return null;

        // shapes that grow from a side or the center keep it when their size changes with the variable: one undo step
        var keep = GrowAnchoring.ForVariableChange(_document, table);
        if (keep.Count == 0) _history.Execute(command);
        else _history.Execute(new CompositeCommand(command.Name, [command, .. keep]));
        return null;
    }

    /// <summary>
    /// Renames a variable and the places that use it: the expressions of the other variables and the parameters of all shapes, so
    /// that linked fields stay linked. One undo step.
    /// </summary>
    private string? Rename(VariableRowViewModel row, string text)
    {
        var name = text.Trim();
        var error = VariableNames.Check(name, _document.Variables.Keys, except: row.Key);
        if (error is not null) return error;
        if (name == row.Key) return null;

        var table = _document.Variables.Select(v => Same(v.Key, row.Key)
            ? new KeyValuePair<string, string>(name, v.Value)
            : new KeyValuePair<string, string>(v.Key, VariableNames.ReplaceName(v.Value, row.Key, name))).ToList();

        var commands = new List<IDocumentCommand> { new SetVariablesCommand(_document, table, "Rename") };
        foreach (var node in _document.Root.Descendants().OfType<ParametricNode>())
        {
            foreach (var (parameter, expression) in node.Parameters.ToList())
            {
                if (ExpressionParser.UsedNames(expression).Any(used => Same(used, row.Key)))
                    commands.Add(new SetParameterCommand(_document, node, parameter, VariableNames.ReplaceName(expression, row.Key, name)));
            }
        }

        _history.Execute(new CompositeCommand(Texts.Format("Name.RenameVariable", row.Key, name), commands));
        return null;
    }

    // ---- showing the table -------------------------------------------------------------------

    /// <summary>
    /// Updates the table after any change of the document. With the same number of variables the lines are updated where they are
    /// (they stay in the order of the table, so a renamed variable keeps its line and the field that is being edited keeps its
    /// focus); when one was added or deleted the lines are made again.
    /// </summary>
    private void Refresh()
    {
        BuiltInText = Texts.Format("Var.BuiltIn", Math.Round(_document.Settings.Tolerance, 4).ToString("0.####", CultureInfo.InvariantCulture));
        var variables = _document.Variables.ToList();
        var values = _document.ResolveVariables(out var errors);

        if (variables.Count != Rows.Count)
        {
            Rows.Clear();
            foreach (var (name, _) in variables) Rows.Add(NewRow(name));
        }

        for (var i = 0; i < variables.Count; i++)
        {
            var (name, expression) = variables[i];
            var row = Rows[i];
            row.Key = name;
            row.Name.SetFromModel(name);
            row.Expression.SetFromModel(expression);
            row.HasValueError = errors.ContainsKey(name);
            row.ValueText = errors.TryGetValue(name, out var error)
                ? error
                : Math.Round(values[name], 4).ToString("0.####", CultureInfo.InvariantCulture);
        }
    }

    private VariableRowViewModel NewRow(string name)
    {
        VariableRowViewModel? row = null;
        var nameField = new FieldViewModel("", "", text => Rename(row!, text));
        var expressionField = new FieldViewModel("", "", text => SetExpression(row!, text));
        row = new VariableRowViewModel(nameField, expressionField, Delete) { Key = name };
        return row;
    }

    /// <summary>The errors that a table would have, without changing the document.</summary>
    private static Dictionary<string, string> Resolve(IEnumerable<KeyValuePair<string, string>> table)
    {
        var scratch = new FormwerkDocument();
        foreach (var (name, expression) in table) scratch.Variables[name] = expression;
        scratch.ResolveVariables(out var errors);
        return errors;
    }

    private string FreeName(string baseName)
    {
        if (!_document.Variables.Keys.Any(k => Same(k, baseName))) return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = baseName + i.ToString(CultureInfo.InvariantCulture);
            if (!_document.Variables.Keys.Any(k => Same(k, candidate))) return candidate;
        }
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
