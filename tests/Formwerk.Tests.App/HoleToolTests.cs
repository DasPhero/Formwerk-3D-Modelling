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
        Assert.Equal(["depth", "diameter", "type"], tool.BuildParameters(10).Keys.Order().ToArray());

        tool.Type = HoleType.Countersink;
        Assert.Equal(["depth", "diameter", "sinkAngle", "sinkDiameter", "type"], tool.BuildParameters(10).Keys.Order().ToArray());
        Assert.Equal("2", tool.BuildParameters(10)["type"]);

        tool.Type = HoleType.Counterbore;
        Assert.Equal(["depth", "diameter", "sinkDepth", "sinkDiameter", "type"], tool.BuildParameters(10).Keys.Order().ToArray());

        tool.Type = HoleType.Conical;
        Assert.Equal(["depth", "diameter", "endDiameter", "type"], tool.BuildParameters(10).Keys.Order().ToArray());
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
}
