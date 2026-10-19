using Formwerk.Core.Expressions;

namespace Formwerk.Tests.Core;

public class ExpressionTests
{
    private static readonly Dictionary<string, double> Vars = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Wandstaerke"] = 2,
        ["Schraube"] = 3.4,
        ["Toleranz"] = 0.2,
    };

    [Theory]
    [InlineData("20", 20)]
    [InlineData("20/2+3", 13)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("-5+2", -3)]
    [InlineData("2*-3", -6)]
    [InlineData("1.5+0.5", 2)]
    [InlineData("Wandstaerke*2", 4)]
    [InlineData("Schraube+Toleranz", 3.6)]
    [InlineData("min(3,4)", 3)]
    [InlineData("max(3,4,10)", 10)]
    [InlineData("round(2.6)", 3)]
    [InlineData("round(2.678,2)", 2.68)]
    [InlineData("2×3÷4", 1.5)]
    public void Evaluates_valid_expressions(string text, double expected)
    {
        var result = ExpressionParser.Evaluate(text, Vars);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Value, 9);
    }

    [Theory]
    [InlineData("")]
    [InlineData("2+")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("Unbekannt*2")]
    [InlineData("1/0")]
    [InlineData("foo(1)")]
    [InlineData("min()")]
    [InlineData("2 3")]
    public void Rejects_invalid_expressions_without_throwing(string text)
    {
        var result = ExpressionParser.Evaluate(text, Vars);
        Assert.False(result.IsSuccess);
        Assert.False(string.IsNullOrEmpty(result.Error));
    }

    [Theory]
    [InlineData("System.Environment.Exit(1)")]
    [InlineData("Process.Start(\"calc\")")]
    [InlineData("1;2")]
    [InlineData("\"abc\"")]
    public void Never_executes_code(string text)
    {
        Assert.False(ExpressionParser.Evaluate(text, Vars).IsSuccess);
    }

    [Fact]
    public void Deeply_nested_input_fails_cleanly()
    {
        var text = new string('(', 5000) + "1" + new string(')', 5000);
        Assert.False(ExpressionParser.Evaluate(text, Vars).IsSuccess);
    }

    // F-TRF-02: '+10' moves by exactly 10 mm relative to the current value.
    [Theory]
    [InlineData("+10", 5, 15)]
    [InlineData("+10", 0, 10)]
    [InlineData("25", 5, 25)]
    [InlineData("20/2+3", 5, 13)]
    [InlineData("-4", 5, -4)]
    public void Numeric_input_resolves_absolute_and_relative(string text, double current, double expected)
    {
        var result = NumericInput.Resolve(text, current, Vars);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Value, 9);
    }

    // F-SCL-04: '150%' is 1.5 times the current size.
    [Theory]
    [InlineData("150%", 20, 30)]
    [InlineData("50%", 20, 10)]
    public void Percent_input_scales_current_value(string text, double current, double expected)
    {
        var result = NumericInput.Resolve(text, current, Vars);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(expected, result.Value, 9);
    }
}
