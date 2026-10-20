using Formwerk.Core.Expressions;
using Formwerk.Features.Shapes;

namespace Formwerk.Tests.Features;

/// <summary>Screw presets for holes (F-HOL-03): sizes M2 to M8, what each kind of hole needs, and the print tolerance in it (F-HOL-04).</summary>
public class HolePresetsTests
{
    private static Dictionary<string, double> Tolerance(double value) =>
        new(StringComparer.OrdinalIgnoreCase) { [VariableNames.Tolerance] = value };

    /// <summary>The numbers of a preset for a given print tolerance.</summary>
    private static Dictionary<string, double> Values(HolePreset preset, double tolerance = 0.2)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["type"] = (int)preset.Type };
        foreach (var (name, expression) in preset.Parameters)
        {
            var result = ExpressionParser.Evaluate(expression, Tolerance(tolerance));
            Assert.True(result.IsSuccess, $"{name} = {expression}: {result.Error}");
            values[name] = result.Value;
        }

        return values;
    }

    [Fact]
    public void The_sizes_are_M2_to_M8_from_small_to_large()
    {
        Assert.Equal(["M2", "M2.5", "M3", "M4", "M5", "M6", "M8"], HolePresets.Sizes.Select(s => s.Name).ToArray());
        Assert.Equal(HolePresets.Sizes.Select(s => s.Diameter).Order().ToArray(), HolePresets.Sizes.Select(s => s.Diameter).ToArray());
    }

    [Fact]
    public void Every_size_and_kind_makes_a_cutter_that_the_generator_accepts()
    {
        foreach (var size in HolePresets.Sizes)
        {
            foreach (var kind in Enum.GetValues<HolePresetKind>())
            {
                var preset = HolePresets.Create(size.Name, kind)!;

                var mesh = ShapeLibrary.Hole.Generate(Values(preset));

                Assert.True(mesh.IsWatertight(), $"{size.Name} {kind}");
                Assert.True(mesh.ComputeVolume() > 0, $"{size.Name} {kind}");
            }
        }
    }

    [Fact]
    public void M3_countersunk_gives_a_through_hole_with_a_countersink_without_any_other_input()
    {
        var preset = HolePresets.Create("M3", HolePresetKind.CountersunkHead)!;
        var values = Values(preset);

        Assert.Equal(HoleType.Countersink, preset.Type);
        Assert.True(preset.Through);
        Assert.Equal(3.4, values["diameter"], 6); // medium clearance: 3 + 2 x 0.2
        Assert.Equal(5.7, values["sinkDiameter"], 6); // head 5.5 + 0.2
        Assert.Equal(90, values["sinkAngle"]);
    }

    [Theory]
    [InlineData(HolePresetKind.ClearanceFine, 3.2)]
    [InlineData(HolePresetKind.ClearanceMedium, 3.4)]
    [InlineData(HolePresetKind.ClearanceCoarse, 3.6)]
    public void The_clearance_holes_for_M3_are_fine_medium_and_coarse(HolePresetKind kind, double expected)
    {
        var preset = HolePresets.Create("M3", kind)!;

        Assert.Equal(HoleType.Cylindrical, preset.Type);
        Assert.True(preset.Through);
        Assert.Equal(expected, Values(preset)["diameter"], 6);
    }

    [Fact]
    public void M3_cap_head_gives_a_counterbore_for_the_head()
    {
        var preset = HolePresets.Create("M3", HolePresetKind.CapHead)!;
        var values = Values(preset);

        Assert.Equal(HoleType.Counterbore, preset.Type);
        Assert.True(preset.Through);
        Assert.Equal(3.4, values["diameter"], 6);
        Assert.Equal(5.9, values["sinkDiameter"], 6); // head 5.5 + 2 x 0.2
        Assert.Equal(3.2, values["sinkDepth"], 6); // head height 3 + 0.2
    }

    [Fact]
    public void M3_core_hole_is_a_blind_hole_with_the_tap_drill_size()
    {
        var preset = HolePresets.Create("M3", HolePresetKind.CoreHole)!;
        var values = Values(preset);

        Assert.Equal(HoleType.Cylindrical, preset.Type);
        Assert.False(preset.Through);
        Assert.Equal(2.5, values["diameter"], 6);
        Assert.Equal(9, values["depth"], 6); // three times the thread diameter
    }

    [Fact]
    public void M3_heat_set_insert_is_a_blind_hole_for_the_insert()
    {
        var preset = HolePresets.Create("M3", HolePresetKind.HeatSetInsert)!;
        var values = Values(preset);

        Assert.Equal(HoleType.Cylindrical, preset.Type);
        Assert.False(preset.Through);
        Assert.Equal(4.0, values["diameter"], 6);
        Assert.Equal(6, values["depth"], 6); // insert 5 mm long and 1 mm room for the melted plastic
    }

    [Fact]
    public void The_holes_that_a_screw_passes_through_follow_the_print_tolerance()
    {
        foreach (var kind in new[] { HolePresetKind.ClearanceFine, HolePresetKind.ClearanceMedium, HolePresetKind.ClearanceCoarse, HolePresetKind.CountersunkHead, HolePresetKind.CapHead })
        {
            var preset = HolePresets.Create("M4", kind)!;

            Assert.Contains("Tolerance", preset.Parameters["diameter"]);
            Assert.True(Values(preset, 0.4)["diameter"] > Values(preset, 0.2)["diameter"], kind.ToString());
            Assert.True(Values(preset, 0.0)["diameter"] >= 4, kind.ToString()); // never smaller than the thread
        }
    }

    [Fact]
    public void The_head_recesses_follow_the_print_tolerance_too()
    {
        var countersunk = HolePresets.Create("M5", HolePresetKind.CountersunkHead)!;
        var cap = HolePresets.Create("M5", HolePresetKind.CapHead)!;

        Assert.True(Values(countersunk, 0.5)["sinkDiameter"] > Values(countersunk, 0.2)["sinkDiameter"]);
        Assert.True(Values(cap, 0.5)["sinkDiameter"] > Values(cap, 0.2)["sinkDiameter"]);
        Assert.True(Values(cap, 0.5)["sinkDepth"] > Values(cap, 0.2)["sinkDepth"]);
    }

    [Fact]
    public void The_core_hole_and_the_insert_are_sized_for_the_screw_and_the_insert_not_by_the_tolerance()
    {
        foreach (var kind in new[] { HolePresetKind.CoreHole, HolePresetKind.HeatSetInsert })
        {
            var preset = HolePresets.Create("M5", kind)!;

            Assert.DoesNotContain(preset.Parameters.Values, text => text.Contains("Tolerance"));
            Assert.Equal(Values(preset, 0.2)["diameter"], Values(preset, 0.6)["diameter"]);
        }
    }

    [Fact]
    public void A_larger_screw_gets_a_larger_hole_of_every_kind()
    {
        foreach (var kind in Enum.GetValues<HolePresetKind>())
        {
            var diameters = HolePresets.Sizes.Select(s => Values(HolePresets.Create(s.Name, kind)!)["diameter"]).ToList();

            Assert.Equal(diameters.Order().ToList(), diameters);
        }
    }

    [Fact]
    public void A_countersink_always_reaches_the_head_diameter_wider_than_the_hole()
    {
        foreach (var size in HolePresets.Sizes)
        {
            var values = Values(HolePresets.Create(size.Name, HolePresetKind.CountersunkHead)!);

            Assert.True(values["sinkDiameter"] > values["diameter"], size.Name);
        }
    }

    [Fact]
    public void A_cap_head_recess_is_wider_than_the_hole_and_deeper_than_the_head()
    {
        foreach (var size in HolePresets.Sizes)
        {
            var values = Values(HolePresets.Create(size.Name, HolePresetKind.CapHead)!);

            Assert.True(values["sinkDiameter"] > values["diameter"], size.Name);
            Assert.True(values["sinkDepth"] > size.CapHeadHeight, size.Name);
        }
    }

    [Fact]
    public void An_unknown_size_gives_no_preset()
    {
        Assert.Null(HolePresets.Create("M99", HolePresetKind.ClearanceMedium));
        Assert.Null(HolePresets.Create("", HolePresetKind.ClearanceMedium));
    }

    [Fact]
    public void The_size_is_found_ignoring_case()
    {
        Assert.NotNull(HolePresets.Create("m3", HolePresetKind.CapHead));
    }

    [Fact]
    public void Numbers_are_written_with_a_dot()
    {
        var preset = HolePresets.Create("M2.5", HolePresetKind.CoreHole)!;

        Assert.Equal("2.05", preset.Parameters["diameter"]);
        Assert.DoesNotContain(',', preset.Parameters["depth"]);
    }
}
