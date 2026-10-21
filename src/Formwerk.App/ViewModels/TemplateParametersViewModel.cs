using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Expressions;
using Formwerk.Features.Shapes;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The small form that is shown when a template with shared parameters is inserted (F-TPL-06): one field per parameter, named like the
/// label chosen when the template was saved and preset with the stored value. It confirms only when every field is valid.
/// </summary>
public sealed class TemplateParametersViewModel : ObservableObject
{
    public TemplateParametersViewModel(string templateName, IReadOnlyList<FieldViewModel> fields)
    {
        Title = Texts.Format("Dlg.TemplateParametersTitle", templateName);
        Fields = fields;
        foreach (var field in fields)
        {
            field.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FieldViewModel.HasError)) OnPropertyChanged(nameof(IsValid));
            };
        }
    }

    public string Title { get; }

    public IReadOnlyList<FieldViewModel> Fields { get; }

    /// <summary>No field has an error.</summary>
    public bool IsValid => Fields.All(f => !f.HasError);

    /// <summary>
    /// Checks what was typed into a field like the properties panel does: a number, a formula (project and template variables), "+10"
    /// or "150%" relative to the stored value; the result must be in the range of the parameter. On success <paramref name="expression"/> is
    /// what is stored in the object (the typed text, or the calculated number for a relative value).
    /// </summary>
    /// <returns>The error to show, or null.</returns>
    public static string? Check(
        string text, ParameterDefinition definition, string label, string storedText, IReadOnlyDictionary<string, double> variables, out string expression)
    {
        expression = text.Trim();
        var stored = ExpressionParser.Evaluate(storedText, variables);
        var resolved = NumericInput.Resolve(expression, stored.IsSuccess ? stored.Value : definition.Default, variables);
        if (!resolved.IsSuccess) return resolved.Error;

        var number = definition.IsInteger ? Math.Round(resolved.Value) : resolved.Value;
        if (number < definition.Min || number > definition.Max)
            return Texts.Format("Err.Range", label, Format(definition.Min), Format(definition.Max));

        if (expression.StartsWith('+') || expression.EndsWith('%')) expression = resolved.Value.ToString("R", CultureInfo.InvariantCulture);
        return null;
    }

    private static string Format(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
