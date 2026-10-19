using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace Formwerk.App.Resources;

/// <summary>Access to <c>Strings.resx</c>, the single place for all UI text.</summary>
public static class Texts
{
    private static readonly ResourceManager Manager = new("Formwerk.App.Resources.Strings", typeof(Texts).Assembly);

    public static string Get(string key) =>
        Manager.GetString(key, CultureInfo.InvariantCulture) ?? $"!{key}!";

    public static string Format(string key, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, Get(key), args);

    /// <summary>All keys, used by tests to check that XAML and code only refer to existing texts.</summary>
    public static IEnumerable<string> Keys()
    {
        var set = Manager.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        if (set is null) yield break;
        foreach (System.Collections.DictionaryEntry entry in set) yield return (string)entry.Key;
    }
}

/// <summary>XAML: <c>{res:Loc Menu.File}</c> reads the text from <c>Strings.resx</c>.</summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider) => Texts.Get(Key);
}
