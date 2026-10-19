using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;
using Formwerk.Core.Expressions;
using Formwerk.Features.Shapes;

namespace Formwerk.App.ViewModels;

/// <summary>One entry of the type choice of the hole tool.</summary>
public sealed record HoleTypeItem(HoleType Type, string Label)
{
    /// <summary>The four types, with their texts, for a choice list.</summary>
    public static IReadOnlyList<HoleTypeItem> All() =>
    [
        new(HoleType.Cylindrical, Texts.Get("Hole.Type.Cylindrical")),
        new(HoleType.Conical, Texts.Get("Hole.Type.Conical")),
        new(HoleType.Countersink, Texts.Get("Hole.Type.Countersink")),
        new(HoleType.Counterbore, Texts.Get("Hole.Type.Counterbore")),
    ];
}

/// <summary>
/// What the next hole looks like (F-HOL-01, F-HOL-02): the type and the sizes, as expressions like in the properties panel, so a
/// project variable can be used. A new hole copies these values; changing the tool never changes holes that already exist.
/// </summary>
public sealed partial class HoleToolViewModel : ObservableObject
{
    private static readonly string[] SizeNames = ["diameter", "endDiameter", "depth", "sinkDiameter", "sinkAngle", "sinkDepth"];

    private readonly Func<IReadOnlyDictionary<string, double>> _variables;
    private readonly Dictionary<string, string> _texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FieldViewModel> _fields = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="variables">The project variables that the typed expressions may use.</param>
    public HoleToolViewModel(Func<IReadOnlyDictionary<string, double>> variables)
    {
        _variables = variables;
        foreach (var name in SizeNames)
        {
            var definition = Definition(name);
            _texts[name] = definition.Default.ToString("0.###", CultureInfo.InvariantCulture);
            var field = new FieldViewModel(definition.Label, definition.Unit ?? "mm", text => Commit(definition, text));
            field.SetFromModel(_texts[name]);
            _fields[name] = field;
        }
    }

    public IReadOnlyList<HoleTypeItem> Types { get; } = HoleTypeItem.All();

    [ObservableProperty]
    public partial HoleType Type { get; set; }

    /// <summary>The hole goes all the way through the body that was clicked (its depth is worked out when the hole is placed).</summary>
    [ObservableProperty]
    public partial bool Through { get; set; } = true;

    public FieldViewModel Diameter => _fields["diameter"];

    public FieldViewModel EndDiameter => _fields["endDiameter"];

    public FieldViewModel Depth => _fields["depth"];

    public FieldViewModel HeadDiameter => _fields["sinkDiameter"];

    public FieldViewModel HeadAngle => _fields["sinkAngle"];

    public FieldViewModel HeadDepth => _fields["sinkDepth"];

    public bool ShowDepth => !Through;

    public bool ShowEndDiameter => Type == HoleType.Conical;

    public bool ShowHead => Type is HoleType.Countersink or HoleType.Counterbore;

    public bool ShowHeadAngle => Type == HoleType.Countersink;

    public bool ShowHeadDepth => Type == HoleType.Counterbore;

    partial void OnTypeChanged(HoleType value)
    {
        OnPropertyChanged(nameof(ShowEndDiameter));
        OnPropertyChanged(nameof(ShowHead));
        OnPropertyChanged(nameof(ShowHeadAngle));
        OnPropertyChanged(nameof(ShowHeadDepth));
        PictureChanged();
    }

    partial void OnThroughChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowDepth));
        PictureChanged();
    }

    private void PictureChanged()
    {
        OnPropertyChanged(nameof(Profile));
        OnPropertyChanged(nameof(Section));
    }

    /// <summary>
    /// The outline of the hole for the cross-section picture (distance from the axis, height; the surface is at 0). A through hole has
    /// no depth of its own, so it is drawn as deep as 2.5 times its widest part, which keeps the picture readable.
    /// </summary>
    public IReadOnlyList<(float R, float Z)> Profile
    {
        get
        {
            var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["type"] = (int)Type };
            var variables = _variables();
            foreach (var name in SizeNames)
            {
                var result = ExpressionParser.Evaluate(_texts[name], variables);
                if (result.IsSuccess) values[name] = result.Value;
            }

            if (Through)
            {
                var widest = Math.Max(values.GetValueOrDefault("diameter", 3.4), ShowHead ? values.GetValueOrDefault("sinkDiameter", 0) : 0);
                values["depth"] = Math.Clamp(widest * 2.5, 0.01, 10000);
            }

            try
            {
                return ShapeLibrary.Hole.Profile(values);
            }
            catch (ArgumentException)
            {
                return ShapeLibrary.Hole.Profile(new Dictionary<string, double> { ["type"] = (int)Type });
            }
        }
    }

    /// <summary>The cross-section picture of the next hole.</summary>
    public HoleSection Section => HoleSection.From(Profile);

    private string? Commit(ParameterDefinition definition, string text)
    {
        var result = NumericInput.Resolve(text, double.TryParse(_texts[definition.Name], NumberStyles.Float, CultureInfo.InvariantCulture, out var current) ? current : definition.Default, _variables());
        if (!result.IsSuccess) return result.Error;
        if (!double.IsFinite(result.Value) || result.Value < definition.Min || result.Value > definition.Max)
            return Texts.Format("Hole.Err.Range", definition.Label, definition.Min.ToString("0.###", CultureInfo.InvariantCulture), definition.Max.ToString("0.###", CultureInfo.InvariantCulture));

        // "+1" and "150%" are relative to the old value; the stored text is then the plain result
        var trimmed = text.Trim();
        _texts[definition.Name] = trimmed.StartsWith('+') || trimmed.EndsWith('%')
            ? Math.Round(result.Value, 3).ToString("0.###", CultureInfo.InvariantCulture)
            : trimmed;
        PictureChanged();
        return null;
    }

    /// <summary>
    /// The parameter expressions for a new hole node: only what the type needs. <paramref name="throughDepth"/> is used when
    /// <see cref="Through"/> is on.
    /// </summary>
    public Dictionary<string, string> BuildParameters(double throughDepth)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["type"] = ((int)Type).ToString(CultureInfo.InvariantCulture),
            ["diameter"] = _texts["diameter"],
            // a through hole also keeps the depth it had when it was placed: it is what a hole outside a group shows and what comes back
            // when "through all" is switched off; inside a group the evaluator makes the hole as deep as the body needs
            ["depth"] = Through ? Math.Round(throughDepth, 2).ToString("0.##", CultureInfo.InvariantCulture) : _texts["depth"],
            ["through"] = Through ? "1" : "0",
        };
        if (ShowEndDiameter) parameters["endDiameter"] = _texts["endDiameter"];
        if (ShowHead) parameters["sinkDiameter"] = _texts["sinkDiameter"];
        if (ShowHeadAngle) parameters["sinkAngle"] = _texts["sinkAngle"];
        if (ShowHeadDepth) parameters["sinkDepth"] = _texts["sinkDepth"];
        return parameters;
    }

    private static ParameterDefinition Definition(string name) => ShapeLibrary.Hole.Parameters.First(p => p.Name == name);
}
