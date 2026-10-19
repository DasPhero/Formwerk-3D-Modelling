using System.Globalization;

namespace Formwerk.Core.Expressions;

/// <summary>
/// Small, safe evaluator for parameter expressions (spec section 6, N-SEC-04):
/// numbers, + - * / (also × ÷), parentheses, variables, min/max/round. Never executes code.
/// </summary>
public static class ExpressionParser
{
    private const int MaxDepth = 64;
    private const int MaxLength = 2000;

    public static EvalResult Evaluate(string? text, IReadOnlyDictionary<string, double>? variables = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return EvalResult.Fail("Enter a value.");
        if (text.Length > MaxLength) return EvalResult.Fail("The expression is too long.");

        try
        {
            var parser = new Parser(text, variables);
            var value = parser.ParseAll();
            return double.IsFinite(value) ? EvalResult.Ok(value) : EvalResult.Fail("The result is not a valid number.");
        }
        catch (ExpressionException ex)
        {
            return EvalResult.Fail(ex.Message);
        }
    }

    private sealed class ExpressionException(string message) : Exception(message);

    private sealed class Parser(string text, IReadOnlyDictionary<string, double>? variables)
    {
        private int _pos;
        private int _depth;

        public double ParseAll()
        {
            var value = ParseSum();
            SkipSpace();
            if (_pos < text.Length) throw new ExpressionException($"Unexpected '{text[_pos]}' at position {_pos + 1}.");
            return value;
        }

        private double ParseSum()
        {
            var left = ParseProduct();
            while (true)
            {
                SkipSpace();
                if (Match('+')) left += ParseProduct();
                else if (Match('-')) left -= ParseProduct();
                else return left;
            }
        }

        private double ParseProduct()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipSpace();
                if (Match('*') || Match('×')) left *= ParseUnary();
                else if (Match('/') || Match('÷'))
                {
                    var right = ParseUnary();
                    if (right == 0) throw new ExpressionException("Division by zero.");
                    left /= right;
                }
                else return left;
            }
        }

        private double ParseUnary()
        {
            SkipSpace();
            if (Match('-')) return -Enter(ParseUnary);
            if (Match('+')) return Enter(ParseUnary);
            return ParsePrimary();
        }

        private double ParsePrimary()
        {
            SkipSpace();
            if (_pos >= text.Length) throw new ExpressionException("The expression ends unexpectedly.");

            var c = text[_pos];
            if (c == '(')
            {
                _pos++;
                var inner = Enter(ParseSum);
                SkipSpace();
                if (!Match(')')) throw new ExpressionException("Missing closing parenthesis.");
                return inner;
            }

            if (char.IsAsciiDigit(c) || c == '.') return ParseNumber();
            if (char.IsLetter(c) || c == '_') return ParseNameOrCall();
            throw new ExpressionException($"Unexpected '{c}' at position {_pos + 1}.");
        }

        private double ParseNumber()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsAsciiDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            var token = text[start.._pos];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                throw new ExpressionException($"'{token}' is not a valid number.");
            return value;
        }

        private double ParseNameOrCall()
        {
            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] == '_')) _pos++;
            var name = text[start.._pos];

            SkipSpace();
            if (_pos < text.Length && text[_pos] == '(')
            {
                _pos++;
                return Enter(() => ParseCall(name));
            }

            if (variables is not null && variables.TryGetValue(name, out var value)) return value;
            throw new ExpressionException($"Unknown variable '{name}'.");
        }

        private double ParseCall(string name)
        {
            var args = new List<double>();
            SkipSpace();
            if (!Match(')'))
            {
                do args.Add(ParseSum());
                while (SkipSpaceAndMatch(',') || SkipSpaceAndMatch(';'));
                SkipSpace();
                if (!Match(')')) throw new ExpressionException("Missing closing parenthesis.");
            }

            switch (name.ToLowerInvariant())
            {
                case "min" when args.Count >= 1: return args.Min();
                case "max" when args.Count >= 1: return args.Max();
                case "min" or "max": throw new ExpressionException($"'{name}' needs at least one value.");
                case "round" when args.Count == 1: return Math.Round(args[0], MidpointRounding.AwayFromZero);
                case "round" when args.Count == 2:
                    var digits = (int)Math.Clamp(args[1], 0, 10);
                    return Math.Round(args[0], digits, MidpointRounding.AwayFromZero);
                case "round": throw new ExpressionException("'round' takes a value and optionally the number of decimals.");
                default: throw new ExpressionException($"Unknown function '{name}'.");
            }
        }

        private double Enter(Func<double> parse)
        {
            if (++_depth > MaxDepth) throw new ExpressionException("The expression is nested too deeply.");
            try { return parse(); }
            finally { _depth--; }
        }

        private bool SkipSpaceAndMatch(char c)
        {
            SkipSpace();
            return Match(c);
        }

        private bool Match(char c)
        {
            if (_pos < text.Length && text[_pos] == c)
            {
                _pos++;
                return true;
            }

            return false;
        }

        private void SkipSpace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
