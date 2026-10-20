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
        new(HoleType.NutTrap, Texts.Get("Hole.Type.NutTrap")),
    ];
}

/// <summary>One entry of the screw choice of the hole tool: a size such as "M3", or "" for no preset.</summary>
public sealed record ScrewChoice(string Key, string Label);

/// <summary>One entry of the "for" choice of the hole tool: what the hole is for.</summary>
public sealed record HolePresetKindItem(HolePresetKind Kind, string Label)
{
    public static IReadOnlyList<HolePresetKindItem> All() =>
    [
        new(HolePresetKind.ClearanceFine, Texts.Get("Hole.Kind.ClearanceFine")),
        new(HolePresetKind.ClearanceMedium, Texts.Get("Hole.Kind.ClearanceMedium")),
        new(HolePresetKind.ClearanceCoarse, Texts.Get("Hole.Kind.ClearanceCoarse")),
        new(HolePresetKind.CountersunkHead, Texts.Get("Hole.Kind.CountersunkHead")),
        new(HolePresetKind.CapHead, Texts.Get("Hole.Kind.CapHead")),
        new(HolePresetKind.CoreHole, Texts.Get("Hole.Kind.CoreHole")),
        new(HolePresetKind.HeatSetInsert, Texts.Get("Hole.Kind.HeatSetInsert")),
        new(HolePresetKind.HexNutTrap, Texts.Get("Hole.Kind.HexNutTrap")),
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

    /// <summary>"Manual" and the screw sizes M2 to M8 (F-HOL-03).</summary>
    public IReadOnlyList<ScrewChoice> ScrewChoices { get; } =
        [new("", Texts.Get("Hole.Preset.Manual")), .. HolePresets.Sizes.Select(s => new ScrewChoice(s.Name, s.Name))];

    public IReadOnlyList<HolePresetKindItem> PresetKinds { get; } = HolePresetKindItem.All();

    /// <summary>The screw size of the preset, or "" for none. Choosing one fills in the tool; the fields can be changed afterwards.</summary>
    [ObservableProperty]
    public partial string PresetSize { get; set; } = "";

    /// <summary>What the hole is for (clearance, countersunk head, ...); only used together with a screw size.</summary>
    [ObservableProperty]
    public partial HolePresetKind PresetKind { get; set; } = HolePresetKind.ClearanceMedium;

    public bool HasPreset => PresetSize.Length > 0;

    partial void OnPresetSizeChanged(string value)
    {
        OnPropertyChanged(nameof(HasPreset));
        ApplyPreset();
    }

    partial void OnPresetKindChanged(HolePresetKind value) => ApplyPreset();

    /// <summary>
    /// Fills the type, through or blind and the sizes of the tool from the preset. The sizes are formulas with <c>Tolerance</c> in
    /// them where the print tolerance matters (F-HOL-04), so a hole made from them follows the project setting.
    /// </summary>
    private void ApplyPreset()
    {
        if (HolePresets.Create(PresetSize, PresetKind) is not { } preset) return;
        Type = preset.Type;
        Through = preset.Through;
        foreach (var (name, expression) in preset.Parameters) _fields[name].Text = expression;
    }

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

    public bool ShowHead => Type is HoleType.Countersink or HoleType.Counterbore or HoleType.NutTrap;

    public bool ShowHeadAngle => Type == HoleType.Countersink;

    public bool ShowHeadDepth => Type is HoleType.Counterbore or HoleType.NutTrap;

    partial void OnTypeChanged(HoleType value)
    {
        OnPropertyChanged(nameof(ShowEndDiameter));
        OnPropertyChanged(nameof(ShowHead));
        OnPropertyChanged(nameof(ShowHeadAngle));
        OnPropertyChanged(nameof(ShowHeadDepth));
        HeadDiameter.Label = HoleGenerator.LabelFor("sinkDiameter", value); // "Nut width" and "Nut thickness" for a nut trap
        HeadDepth.Label = HoleGenerator.LabelFor("sinkDepth", value);
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
        _fields[definition.Name].ShowLinks(ExpressionParser.UsedNames(_texts[definition.Name]));
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
