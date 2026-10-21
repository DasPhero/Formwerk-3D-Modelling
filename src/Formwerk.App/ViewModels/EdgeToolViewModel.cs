using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Expressions;

namespace Formwerk.App.ViewModels;

/// <summary>
/// What the next chamfer or fillet looks like (F-EDG-02): the type and the size, as an expression like in the properties panel, so a
/// project variable can be used. A new cutter copies these values; changing the tool never changes cutters that already exist.
/// </summary>
public sealed partial class EdgeToolViewModel : ObservableObject
{
    private readonly Func<IReadOnlyDictionary<string, double>> _variables;
    private string _sizeText = "1";

    /// <param name="variables">The project variables that the typed size may use.</param>
    public EdgeToolViewModel(Func<IReadOnlyDictionary<string, double>> variables)
    {
        _variables = variables;
        Size = new FieldViewModel(Texts.Get("Edge.Size"), "mm", Commit);
        Size.SetFromModel(_sizeText);
    }

    public IReadOnlyList<EdgeChoice> Types { get; } = [new(0, Texts.Get("Edge.Chamfer")), new(1, Texts.Get("Edge.Fillet"))];

    /// <summary>0 chamfer, 1 fillet.</summary>
    [ObservableProperty]
    public partial int EdgeType { get; set; } = 1;

    /// <summary>The distance along the faces of a chamfer, or the radius of a fillet.</summary>
    public FieldViewModel Size { get; }

    /// <summary>The size as it was typed (a number or a formula); what a new cutter stores.</summary>
    public string SizeExpression => _sizeText;

    private string? Commit(string text)
    {
        var result = NumericInput.Resolve(text, 1, _variables());
        if (!result.IsSuccess) return result.Error;
        if (!double.IsFinite(result.Value) || result.Value <= 0 || result.Value > 10000) return Texts.Get("Edge.Err.Size");
        _sizeText = text.Trim();
        return null;
    }
}
