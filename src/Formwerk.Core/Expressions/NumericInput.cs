namespace Formwerk.Core.Expressions;

/// <summary>
/// Interprets text typed into a numeric field (Leitprinzip 2):
/// "+10" is relative to the current value (F-TRF-02), "150%" scales it (F-SCL-04),
/// anything else is an absolute expression. A leading "-" is a negative absolute value.
/// </summary>
public static class NumericInput
{
    public static EvalResult Resolve(string? text, double current, IReadOnlyDictionary<string, double>? variables = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return EvalResult.Fail("Enter a value.");
        var trimmed = text.Trim();

        if (trimmed.EndsWith('%'))
        {
            var percent = ExpressionParser.Evaluate(trimmed[..^1], variables);
            return percent.IsSuccess ? EvalResult.Ok(current * percent.Value / 100.0) : percent;
        }

        if (trimmed[0] == '+')
        {
            var delta = ExpressionParser.Evaluate(trimmed[1..], variables);
            return delta.IsSuccess ? EvalResult.Ok(current + delta.Value) : delta;
        }

        return ExpressionParser.Evaluate(trimmed, variables);
    }
}
