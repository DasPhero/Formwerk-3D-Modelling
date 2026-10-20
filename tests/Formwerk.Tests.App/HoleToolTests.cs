using Formwerk.App.ViewModels;
using Formwerk.Features.Shapes;

namespace Formwerk.Tests.App;

/// <summary>The settings of the hole tool (F-HOL-01, F-HOL-02): what the next hole will look like.</summary>
public class HoleToolTests
{
    private static HoleToolViewModel NewTool(Dictionary<string, double>? variables = null) =>
        new(() => variables ?? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void A_new_tool_makes_a_cylindrical_through_hole_for_an_M3_screw()
    {
        var tool = NewTool();

        Assert.Equal(HoleType.Cylindrical, tool.Type);
        Assert.True(tool.Through);
        Assert.Equal("3.4", tool.Diameter.Text);
        var parameters = tool.BuildParameters(throughDepth: 25);
        Assert.Equal("0", parameters["type"]);
        Assert.Equal("3.4", parameters["diameter"]);
        Assert.Equal("25", parameters["depth"]);
        Assert.Equal("1", parameters["through"]); // the evaluator keeps it through when the body changes
    }

    [Fact]
    public void The_diameter_accepts_expressions_and_variables()
    {
        var tool = NewTool(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Schraube"] = 3.4 });

        tool.Diameter.Text = "Schraube+0.2";

        Assert.Null(tool.Diameter.Error);
        Assert.Equal("Schraube+0.2", tool.BuildParameters(10)["diameter"]);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("5000")]
    public void A_diameter_that_cannot_be_drilled_is_refused_and_the_old_value_stays(string text)
    {
        var tool = NewTool();

        tool.Diameter.Text = text;

        Assert.NotNull(tool.Diameter.Error);
        Assert.Equal("3.4", tool.BuildParameters(10)["diameter"]);
    }

    [Fact]
    public void A_blind_hole_uses_the_typed_depth_and_a_through_hole_the_given_one()
    {
        var tool = NewTool();
        tool.Depth.Text = "7.5";

        Assert.Equal("30", tool.BuildParameters(30)["depth"]);

        tool.Through = false;
        Assert.Equal("7.5", tool.BuildParameters(30)["depth"]);
        Assert.Equal("0", tool.BuildParameters(30)["through"]);
    }

    [Fact]
    public void Each_type_shows_only_the_fields_it_needs()
    {
        var tool = NewTool();
        Assert.False(tool.ShowEndDiameter || tool.ShowHead || tool.ShowHeadAngle || tool.ShowHeadDepth);
        Assert.False(tool.ShowDepth); // through

        tool.Type = HoleType.Conical;
        Assert.True(tool.ShowEndDiameter);
        Assert.False(tool.ShowHead);

        tool.Type = HoleType.Countersink;
        Assert.True(tool.ShowHead);
        Assert.True(tool.ShowHeadAngle);
        Assert.False(tool.ShowHeadDepth);
        Assert.False(tool.ShowEndDiameter);

        tool.Type = HoleType.Counterbore;
        Assert.True(tool.ShowHead);
        Assert.True(tool.ShowHeadDepth);
        Assert.False(tool.ShowHeadAngle);

        tool.Through = false;
        Assert.True(tool.ShowDepth);
    }

    [Fact]
    public void The_parameters_hold_only_what_the_type_needs()
    {
        var tool = NewTool();
        Assert.Equal(["depth", "diameter", "through", "type"], tool.BuildParameters(10).Keys.Order().ToArray());

        tool.Type = HoleType.Countersink;
        Assert.Equal(["depth", "diameter", "sinkAngle", "sinkDiameter", "through", "type"], tool.BuildParameters(10).Keys.Order().ToArray());
        Assert.Equal("2", tool.BuildParameters(10)["type"]);

        tool.Type = HoleType.Counterbore;
        Assert.Equal(["depth", "diameter", "sinkDepth", "sinkDiameter", "through", "type"], tool.BuildParameters(10).Keys.Order().ToArray());

        tool.Type = HoleType.Conical;
        Assert.Equal(["depth", "diameter", "endDiameter", "through", "type"], tool.BuildParameters(10).Keys.Order().ToArray());
    }

    [Fact]
    public void Every_parameter_the_tool_makes_is_valid_for_the_generator()
    {
        foreach (var type in Enum.GetValues<HoleType>())
        {
            var tool = NewTool();
            tool.Type = type;
            var values = tool.BuildParameters(12).ToDictionary(p => p.Key, p => double.Parse(p.Value, System.Globalization.CultureInfo.InvariantCulture));

            Assert.True(ShapeLibrary.Hole.Generate(values).IsWatertight(), type.ToString());
        }
    }

    [Fact]
    public void The_cross_section_follows_the_settings()
    {
        var tool = NewTool();
        tool.Type = HoleType.Counterbore;
        tool.Through = false;
        tool.Depth.Text = "8";

        var profile = tool.Profile;

        Assert.Contains(profile, p => Math.Abs(p.Z + 8) < 1e-4);
        Assert.Contains(profile, p => Math.Abs(p.R - 3.2) < 1e-4);
    }

    // ---- screw presets (F-HOL-03) ----

    private static HoleToolViewModel ToolWithTolerance(double tolerance = 0.2) =>
        new(() => new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Tolerance"] = tolerance });

    [Fact]
    public void A_new_tool_has_no_preset()
    {
        var tool = ToolWithTolerance();

        Assert.Equal("", tool.PresetSize);
        Assert.False(tool.HasPreset);
        Assert.Equal("Manual", tool.ScrewChoices[0].Label);
        Assert.Equal(["", "M2", "M2.5", "M3", "M4", "M5", "M6", "M8"], tool.ScrewChoices.Select(c => c.Key).ToArray());
    }

    [Fact]
    public void Choosing_a_screw_fills_in_a_medium_clearance_hole()
    {
        var tool = ToolWithTolerance();

        tool.PresetSize = "M3";

        Assert.True(tool.HasPreset);
        Assert.Equal(HoleType.Cylindrical, tool.Type);
        Assert.True(tool.Through);
        Assert.Equal("3+2*Tolerance", tool.Diameter.Text);
        Assert.Null(tool.Diameter.Error);
        Assert.True(tool.Diameter.IsLinked); // the tolerance is in it
    }

    [Fact]
    public void Choosing_the_kind_changes_type_and_fields_at_once()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";

        tool.PresetKind = HolePresetKind.CountersunkHead;

        Assert.Equal(HoleType.Countersink, tool.Type);
        Assert.True(tool.Through);
        Assert.Equal("3+2*Tolerance", tool.Diameter.Text);
        Assert.Equal("5.5+Tolerance", tool.HeadDiameter.Text);
        Assert.Equal("90", tool.HeadAngle.Text);
        Assert.True(tool.ShowHead);
        Assert.True(tool.ShowHeadAngle);

        var parameters = tool.BuildParameters(20);
        Assert.Equal("2", parameters["type"]);
        Assert.Equal("5.5+Tolerance", parameters["sinkDiameter"]);
        Assert.Equal("1", parameters["through"]);
    }

    [Fact]
    public void A_core_hole_is_blind_and_shows_its_depth()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";

        tool.PresetKind = HolePresetKind.CoreHole;

        Assert.False(tool.Through);
        Assert.True(tool.ShowDepth);
        Assert.Equal("2.5", tool.Diameter.Text);
        Assert.Equal("9", tool.Depth.Text);
        Assert.Equal("0", tool.BuildParameters(50)["through"]);
        Assert.Equal("9", tool.BuildParameters(50)["depth"]);
    }

    [Fact]
    public void The_cap_head_preset_makes_a_counterbore()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M4";

        tool.PresetKind = HolePresetKind.CapHead;

        Assert.Equal(HoleType.Counterbore, tool.Type);
        Assert.Equal("7+2*Tolerance", tool.HeadDiameter.Text);
        Assert.Equal("4+Tolerance", tool.HeadDepth.Text);
        Assert.True(tool.ShowHeadDepth);
    }

    [Fact]
    public void The_picture_follows_the_preset()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";
        var plain = tool.Section.Hole;
        var changes = new List<string?>();
        tool.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        tool.PresetKind = HolePresetKind.CountersunkHead;

        Assert.Contains(nameof(HoleToolViewModel.Section), changes);
        Assert.NotEqual(plain, tool.Section.Hole);
    }

    [Fact]
    public void Fields_can_still_be_changed_after_a_preset()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";

        tool.Diameter.Text = "3.6";

        Assert.Equal("3.6", tool.BuildParameters(10)["diameter"]);
        Assert.Equal("M3", tool.PresetSize);
    }

    [Fact]
    public void Going_back_to_manual_leaves_the_fields_as_they_are()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";
        tool.PresetKind = HolePresetKind.CapHead;

        tool.PresetSize = "";

        Assert.False(tool.HasPreset);
        Assert.Equal(HoleType.Counterbore, tool.Type);
        Assert.Equal("3+2*Tolerance", tool.Diameter.Text);
    }

    [Fact]
    public void Every_screw_and_kind_fills_in_without_an_error()
    {
        foreach (var size in HolePresets.Sizes)
        {
            foreach (var kind in Enum.GetValues<HolePresetKind>())
            {
                var tool = ToolWithTolerance();
                tool.PresetSize = size.Name;
                tool.PresetKind = kind;

                Assert.Null(tool.Diameter.Error);
                Assert.Null(tool.HeadDiameter.Error);
                var values = tool.BuildParameters(12).ToDictionary(
                    p => p.Key,
                    p => Formwerk.Core.Expressions.ExpressionParser.Evaluate(p.Value, new Dictionary<string, double> { ["Tolerance"] = 0.2 }).Value,
                    StringComparer.OrdinalIgnoreCase);
                Assert.True(ShapeLibrary.Hole.Generate(values).IsWatertight(), $"{size.Name} {kind}");
            }
        }
    }

    [Fact]
    public void The_preset_kinds_have_texts()
    {
        Assert.Equal(Enum.GetValues<HolePresetKind>().Length, HolePresetKindItem.All().Count);
        Assert.All(HolePresetKindItem.All(), item => Assert.False(string.IsNullOrWhiteSpace(item.Label)));
    }

    // ---- hex nut trap (F-HOL-03) ----

    [Fact]
    public void A_nut_trap_shows_the_nut_fields_with_their_own_names()
    {
        var tool = ToolWithTolerance();

        tool.Type = HoleType.NutTrap;

        Assert.True(tool.ShowHead);
        Assert.True(tool.ShowHeadDepth);
        Assert.False(tool.ShowHeadAngle);
        Assert.False(tool.ShowEndDiameter);
        Assert.Equal("Nut width", tool.HeadDiameter.Label);
        Assert.Equal("Nut thickness", tool.HeadDepth.Label);

        tool.Type = HoleType.Counterbore;
        Assert.Equal("Head diameter", tool.HeadDiameter.Label);
        Assert.Equal("Head depth", tool.HeadDepth.Label);
    }

    [Fact]
    public void The_nut_trap_preset_fills_in_the_nut_of_the_screw()
    {
        var tool = ToolWithTolerance();
        tool.PresetSize = "M3";

        tool.PresetKind = HolePresetKind.HexNutTrap;

        Assert.Equal(HoleType.NutTrap, tool.Type);
        Assert.True(tool.Through);
        Assert.Equal("3+2*Tolerance", tool.Diameter.Text);
        Assert.Equal("5.5+Tolerance", tool.HeadDiameter.Text);
        Assert.Equal("2.4+Tolerance", tool.HeadDepth.Text);
        Assert.Equal("4", tool.BuildParameters(20)["type"]);
        Assert.Equal("Nut width", tool.HeadDiameter.Label);
    }

    [Fact]
    public void The_type_list_has_the_nut_trap_last()
    {
        Assert.Equal(HoleType.NutTrap, HoleTypeItem.All()[^1].Type);
        Assert.Equal(Enum.GetValues<HoleType>().Length, HoleTypeItem.All().Count);
    }
}
