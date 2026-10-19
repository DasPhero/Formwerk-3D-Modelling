namespace Formwerk.Core.Expressions;

public readonly record struct EvalResult(double Value, string? Error)
{
    public bool IsSuccess => Error is null;

    public static EvalResult Ok(double value) => new(value, null);

    public static EvalResult Fail(string error) => new(double.NaN, error);
}
