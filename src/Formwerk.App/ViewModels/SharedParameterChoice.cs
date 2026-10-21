using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;

namespace Formwerk.App.ViewModels;

/// <summary>
/// One numeric parameter of the objects that are saved as a template, as a line of the "Save as Template" dialog (F-TPL-06): a check
/// box (is it asked when the template is inserted?) and the label that the form will show. <see cref="Path"/> is the place of the
/// object in the template scene (index of the saved object, then child indices), see <c>SharedParameter</c>.
/// </summary>
public sealed partial class SharedParameterChoice : ObservableObject
{
    public SharedParameterChoice(IReadOnlyList<int> path, string nodeName, string parameter, string parameterLabel, string label)
    {
        Path = path;
        NodeName = nodeName;
        Parameter = parameter;
        ParameterLabel = parameterLabel;
        Label = label;
    }

    public IReadOnlyList<int> Path { get; }

    /// <summary>The name of the object that has the parameter.</summary>
    public string NodeName { get; }

    /// <summary>The name of the parameter inside the object ("width").</summary>
    public string Parameter { get; }

    /// <summary>What the properties panel calls the parameter ("Width").</summary>
    public string ParameterLabel { get; }

    /// <summary>The line as it is listed: the object and its parameter.</summary>
    public string Display => $"{NodeName}: {ParameterLabel}";

    /// <summary>The parameter is asked when the template is inserted.</summary>
    [ObservableProperty]
    public partial bool IsShared { get; set; }

    /// <summary>What the insert form calls the field.</summary>
    [ObservableProperty]
    public partial string Label { get; set; }

    /// <summary>The parameters of an object that can be asked for with a number: not the ones that choose a kind (type of hole, direction).</summary>
    public static IReadOnlyList<ParameterDefinition> DefinitionsOf(Node node)
    {
        var all = node switch
        {
            PrimitiveNode primitive => ShapeLibrary.Find(primitive.GeneratorId)?.Parameters,
            PatternNode pattern => PatternParameters.For(pattern.Kind),
            _ => null,
        };
        return all is null ? [] : [.. all.Where(d => !IsChoice(d))];
    }

    /// <summary>A whole number from 0 to a few: a list of kinds or an on/off switch, not something to type.</summary>
    private static bool IsChoice(ParameterDefinition definition) => definition.IsInteger && definition.Min == 0 && definition.Max <= 4;

    /// <summary>
    /// The choices for the saved top-level objects (in the order of the template scene): every numeric parameter of every object, also
    /// inside groups, patterns and mirrors. None is ticked. A label that more than one parameter has gets the object name in front.
    /// </summary>
    public static IReadOnlyList<SharedParameterChoice> For(IReadOnlyList<Node> topLevel)
    {
        var found = new List<(List<int> Path, Node Node, ParameterDefinition Definition)>();

        void Visit(Node node, List<int> path)
        {
            foreach (var definition in DefinitionsOf(node)) found.Add((path, node, definition));
            for (var i = 0; i < node.Children.Count; i++) Visit(node.Children[i], [.. path, i]);
        }

        for (var i = 0; i < topLevel.Count; i++) Visit(topLevel[i], [i]);

        var repeated = found.GroupBy(f => f.Definition.Label, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return [.. found.Select(f => new SharedParameterChoice(
            f.Path, f.Node.Name, f.Definition.Name, f.Definition.Label,
            repeated.Contains(f.Definition.Label) ? $"{f.Node.Name}: {f.Definition.Label}" : f.Definition.Label))];
    }
}
