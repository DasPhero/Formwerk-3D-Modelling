using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;

namespace Formwerk.Library;

/// <summary>The categories of the library (F-LIB-02); "My Templates" are the user's own and are managed separately.</summary>
public enum LibraryCategory
{
    BasicShapes,
    Fasteners,
    Brackets,
    Enclosures,
    Mechanics,

    /// <summary>The templates the user saved (F-LIB-03); they are not part of the catalog but of the list in the program.</summary>
    MyTemplates,
}

public static class LibraryCategoryNames
{
    /// <summary>The English name of a category, also what the search matches.</summary>
    public static string DisplayName(this LibraryCategory category) => category switch
    {
        LibraryCategory.BasicShapes => "Basic Shapes",
        LibraryCategory.Fasteners => "Fasteners",
        LibraryCategory.Brackets => "Brackets",
        LibraryCategory.Enclosures => "Enclosures",
        LibraryCategory.Mechanics => "Mechanics",
        LibraryCategory.MyTemplates => "My Templates",
        _ => category.ToString(),
    };
}

/// <summary>A parameter of a library element that is asked when the element is inserted (F-PAR-05).</summary>
public sealed record ElementParameter(string Name, string Label, double Default, double Min, double Max, string Unit = "mm");

/// <summary>
/// The values for a library element with parameters: for every parameter what the user typed (<see cref="Expressions"/>: a number, a formula
/// or a project variable) and the number it comes to (<see cref="Numbers"/>, for positions that cannot be formulas).
/// </summary>
public sealed record ElementValues(IReadOnlyDictionary<string, string> Expressions, IReadOnlyDictionary<string, double> Numbers)
{
    /// <summary>The default values of the parameters.</summary>
    public static ElementValues Defaults(IReadOnlyList<ElementParameter> parameters) => new(
        parameters.ToDictionary(p => p.Name, p => p.Default.ToString("R", CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase),
        parameters.ToDictionary(p => p.Name, p => p.Default, StringComparer.OrdinalIgnoreCase));

    public double N(string name) => Numbers[name];

    /// <summary>
    /// The values again from the text that was saved with an object (a number, a formula or a project variable per parameter); a
    /// parameter that is not there gets its default. Null with the error when a text does not work or is out of range.
    /// </summary>
    public static ElementValues? Resolve(
        IReadOnlyList<ElementParameter> parameters, IReadOnlyDictionary<string, string> saved, IReadOnlyDictionary<string, double> variables, out string? error)
    {
        var expressions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        error = null;
        foreach (var parameter in parameters)
        {
            var text = saved.TryGetValue(parameter.Name, out var s) && !string.IsNullOrWhiteSpace(s)
                ? s.Trim()
                : parameter.Default.ToString("R", CultureInfo.InvariantCulture);
            var result = ExpressionParser.Evaluate(text, variables);
            if (!result.IsSuccess)
            {
                error = parameter.Label + ": " + result.Error;
                return null;
            }

            if (result.Value < parameter.Min || result.Value > parameter.Max)
            {
                error = parameter.Label + ": " + parameter.Min.ToString("0.###", CultureInfo.InvariantCulture) + " to " + parameter.Max.ToString("0.###", CultureInfo.InvariantCulture);
                return null;
            }

            expressions[parameter.Name] = text;
            numbers[parameter.Name] = result.Value;
        }

        return new ElementValues(expressions, numbers);
    }

    /// <summary>
    /// A formula in the names of the parameters ("innerWidth + 2*wall") as the text of a node parameter. When every parameter in it is a
    /// plain number the result is the number itself; otherwise the names are replaced by what was typed (in brackets), so a project
    /// variable in a value stays linked.
    /// </summary>
    public string Calc(string formula)
    {
        var plain = true;
        var text = Regex.Replace(formula, @"[A-Za-z_]\w*", match =>
        {
            if (!Expressions.TryGetValue(match.Value, out var expression)) return match.Value;
            if (!double.TryParse(expression, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) plain = false;
            return "(" + expression + ")";
        });

        if (plain && ExpressionParser.Evaluate(text) is { IsSuccess: true } result)
            return Math.Round(result.Value, 6).ToString("R", CultureInfo.InvariantCulture);
        return text;
    }
}

/// <summary>
/// One element of the library: a basic shape, or a ready-made parametric element (a tree of shapes and booleans that is inserted as
/// normal nodes, so every dimension stays editable). <see cref="Keywords"/> hold the other names of the element (also German ones), so
/// that a search for "Schraube" finds the screws.
/// </summary>
/// <param name="Id">Stable id (saved for the favorites).</param>
/// <param name="Footprint">The size in mm of the element as it is inserted (it rests on the plate, centered); used to find a free place for it.</param>
/// <param name="ShapeId">The generator of a basic shape; null for a recipe.</param>
/// <param name="Recipe">Builds a new, loose node tree of a ready-made element; null for a basic shape. An element with parameters has one as well (made from the defaults).</param>
/// <param name="Parameters">The parameters that are asked when an element with parameters is inserted (F-PAR-05); null for the others.</param>
/// <param name="ParametricRecipe">Builds the tree for the given values; null for an element without parameters.</param>
/// <param name="FootprintFor">The size the element has with the given values, for finding a place.</param>
public sealed record LibraryItem(
    string Id,
    string Name,
    LibraryCategory Category,
    IReadOnlyList<string> Keywords,
    string Description,
    Vector3 Footprint,
    string? ShapeId = null,
    Func<Node>? Recipe = null,
    IReadOnlyList<ElementParameter>? Parameters = null,
    Func<ElementValues, Node>? ParametricRecipe = null,
    Func<ElementValues, Vector3>? FootprintFor = null)
{
    public bool IsShape => ShapeId is not null;

    /// <summary>The element asks for its parameters when it is inserted.</summary>
    public bool IsParametric => Parameters is { Count: > 0 } && ParametricRecipe is not null;

    /// <summary>A new loose tree of an element with parameters for the given values.</summary>
    public Node CreateNode(ElementValues values) => ParametricRecipe is null ? CreateNode() : ParametricRecipe(values);

    /// <summary>A new loose node (tree) of this element with new ids, named like the element; the caller makes the name unique and adds it.</summary>
    public Node CreateNode()
    {
        if (Recipe is not null) return Recipe();

        var generator = ShapeLibrary.Find(ShapeId!) ?? throw new InvalidOperationException($"Unknown shape '{ShapeId}'.");
        var node = new PrimitiveNode(generator.Id, Name);
        foreach (var definition in generator.Parameters)
            node.Parameters[definition.Name] = definition.Default.ToString("R", CultureInfo.InvariantCulture);
        return node;
    }
}
