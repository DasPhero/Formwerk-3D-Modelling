using System.Text;

namespace Formwerk.Core.Expressions;

/// <summary>Rules for the names of project variables (F-PAR-03): the same as the expression parser reads a name.</summary>
public static class VariableNames
{
    public const int MaxLength = 64;

    private static readonly string[] Functions = ["min", "max", "round"];

    /// <summary>
    /// Null when <paramref name="name"/> can be used as a variable name, otherwise what is wrong. A name starts with a letter or an
    /// underscore, then letters, digits and underscores; it is not a function (<c>min</c>, <c>max</c>, <c>round</c>) and not one of
    /// <paramref name="existing"/> (ignoring case) except <paramref name="except"/>, the variable that is being renamed.
    /// </summary>
    public static string? Check(string? name, IEnumerable<string> existing, string? except = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Enter a name.";
        if (name.Length > MaxLength) return $"A name can have at most {MaxLength} characters.";
        if (!(char.IsLetter(name[0]) || name[0] == '_')) return "A name starts with a letter or an underscore.";
        if (name.Any(c => !(char.IsLetterOrDigit(c) || c == '_'))) return "A name can only contain letters, digits and underscores.";
        if (Functions.Contains(name, StringComparer.OrdinalIgnoreCase)) return $"'{name}' is a function and cannot be a variable name.";
        if (existing.Any(e => string.Equals(e, name, StringComparison.OrdinalIgnoreCase) && !string.Equals(e, except, StringComparison.OrdinalIgnoreCase)))
            return $"The name '{name}' is already used.";
        return null;
    }

    /// <summary>
    /// Replaces the variable <paramref name="oldName"/> by <paramref name="newName"/> in an expression: whole names only (ignoring case),
    /// and not where the name is called like a function. Used when a variable is renamed, so that linked fields stay linked.
    /// </summary>
    public static string ReplaceName(string text, string oldName, string newName)
    {
        var sb = new StringBuilder(text.Length);
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsAsciiDigit(c) || c == '.')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '.')) i++;
                sb.Append(text, start, i - start);
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_')) i++;
                var word = text[start..i];
                var after = i;
                while (after < text.Length && char.IsWhiteSpace(text[after])) after++;
                var isCall = after < text.Length && text[after] == '(';
                sb.Append(!isCall && string.Equals(word, oldName, StringComparison.OrdinalIgnoreCase) ? newName : word);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }
}
