using System.Globalization;

namespace Formwerk.Features.Shapes;

/// <summary>What a hole is for (F-HOL-03): the preset kinds of the hole tool.</summary>
public enum HolePresetKind
{
    /// <summary>The screw passes through with a little play: thread diameter + 1 x print tolerance.</summary>
    ClearanceFine,

    /// <summary>Thread diameter + 2 x print tolerance.</summary>
    ClearanceMedium,

    /// <summary>Thread diameter + 3 x print tolerance.</summary>
    ClearanceCoarse,

    /// <summary>Through hole with a 90 degree countersink for a countersunk screw (ISO 7046).</summary>
    CountersunkHead,

    /// <summary>Through hole with a counterbore for the head of a socket head cap screw (ISO 4762).</summary>
    CapHead,

    /// <summary>Blind hole for a self-tapping screw: the tap drill size.</summary>
    CoreHole,

    /// <summary>Blind hole for a heat-set threaded insert (typical sizes).</summary>
    HeatSetInsert,

    /// <summary>Through hole with a hexagonal pocket for a standard nut (ISO 4032): width across the flats and thickness.</summary>
    HexNutTrap,
}

/// <summary>
/// One metric screw size with the measures the presets need, all in mm: the thread diameter, the tap drill size, the head of a
/// countersunk screw (ISO 7046) and of a socket head cap screw (ISO 4762), typical sizes of a heat-set insert and the hexagon nut
/// (ISO 4032: width across the flats and thickness).
/// </summary>
public sealed record ScrewSize(
    string Name, double Diameter, double TapDrill, double CountersunkHeadDiameter, double CapHeadDiameter, double CapHeadHeight,
    double InsertDiameter, double InsertLength, double NutWidth, double NutThickness);

/// <summary>
/// The result of a preset: the type of the hole, whether it goes through, and the parameters as expressions (text), so that the print
/// tolerance stays in them as the variable <c>Tolerance</c> and changing the tolerance changes the hole (F-HOL-04).
/// </summary>
public sealed record HolePreset(HoleType Type, bool Through, IReadOnlyDictionary<string, string> Parameters);

/// <summary>Screw presets M2 to M8 for the hole tool (F-HOL-03).</summary>
public static class HolePresets
{
    public static IReadOnlyList<ScrewSize> Sizes { get; } =
    [
        //        name    d     tap   csk    cap    k    insert  length  nut AF  nut m
        new("M2",   2.0, 1.60, 3.8, 3.8, 2.0, 3.2, 3.0, 4.0, 1.6),
        new("M2.5", 2.5, 2.05, 4.7, 4.5, 2.5, 3.6, 4.0, 5.0, 2.0),
        new("M3",   3.0, 2.50, 5.5, 5.5, 3.0, 4.0, 5.0, 5.5, 2.4),
        new("M4",   4.0, 3.30, 8.4, 7.0, 4.0, 5.6, 6.0, 7.0, 3.2),
        new("M5",   5.0, 4.20, 9.3, 8.5, 5.0, 6.4, 8.0, 8.0, 4.0),
        new("M6",   6.0, 5.00, 11.3, 10.0, 6.0, 8.0, 9.0, 10.0, 5.0),
        new("M8",   8.0, 6.80, 15.8, 13.0, 8.0, 9.6, 12.0, 13.0, 6.5),
    ];

    public static ScrewSize? Find(string name) =>
        Sizes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The hole for a screw size and kind, or null for an unknown size.</summary>
    public static HolePreset? Create(string size, HolePresetKind kind)
    {
        if (Find(size) is not { } s) return null;
        var d = N(s.Diameter);

        // the tolerance is added where the screw must pass or fit in: clearance, heads. A core hole and an insert are sized for the
        // screw or the insert itself, not by play, so the tolerance is not added there.
        return kind switch
        {
            HolePresetKind.ClearanceFine => Through(HoleType.Cylindrical, ("diameter", $"{d}+Tolerance")),
            HolePresetKind.ClearanceMedium => Through(HoleType.Cylindrical, ("diameter", $"{d}+2*Tolerance")),
            HolePresetKind.ClearanceCoarse => Through(HoleType.Cylindrical, ("diameter", $"{d}+3*Tolerance")),
            HolePresetKind.CountersunkHead => Through(
                HoleType.Countersink, ("diameter", $"{d}+2*Tolerance"), ("sinkDiameter", $"{N(s.CountersunkHeadDiameter)}+Tolerance"), ("sinkAngle", "90")),
            HolePresetKind.CapHead => Through(
                HoleType.Counterbore, ("diameter", $"{d}+2*Tolerance"), ("sinkDiameter", $"{N(s.CapHeadDiameter)}+2*Tolerance"),
                ("sinkDepth", $"{N(s.CapHeadHeight)}+Tolerance")),
            HolePresetKind.CoreHole => Blind(HoleType.Cylindrical, ("diameter", N(s.TapDrill)), ("depth", N(3 * s.Diameter))),
            HolePresetKind.HeatSetInsert => Blind(HoleType.Cylindrical, ("diameter", N(s.InsertDiameter)), ("depth", N(s.InsertLength + 1))),
            HolePresetKind.HexNutTrap => Through(
                HoleType.NutTrap, ("diameter", $"{d}+2*Tolerance"), ("sinkDiameter", $"{N(s.NutWidth)}+Tolerance"), ("sinkDepth", $"{N(s.NutThickness)}+Tolerance")),
            _ => null,
        };
    }

    private static HolePreset Through(HoleType type, params (string, string)[] parameters) => new(type, true, ToDictionary(parameters));

    private static HolePreset Blind(HoleType type, params (string, string)[] parameters) => new(type, false, ToDictionary(parameters));

    private static Dictionary<string, string> ToDictionary((string, string)[] parameters) =>
        parameters.ToDictionary(p => p.Item1, p => p.Item2, StringComparer.OrdinalIgnoreCase);

    /// <summary>A number as text with a dot and no trailing zeros.</summary>
    private static string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
