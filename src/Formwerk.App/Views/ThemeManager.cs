using System.Windows;
using System.Windows.Media;

namespace Formwerk.App.Views;

/// <summary>The colors of one theme: greys and one accent (spec section 7), plus the signal color for errors.</summary>
public sealed record ThemePalette(Color Window, Color Panel, Color PanelBorder, Color Text, Color MutedText, Color Accent, Color Error)
{
    /// <summary>The default (F-VIS-07).</summary>
    public static ThemePalette Dark { get; } = new(
        Color.FromRgb(0x1E, 0x1F, 0x22), Color.FromRgb(0x2B, 0x2D, 0x30), Color.FromRgb(0x39, 0x3B, 0x40), Color.FromRgb(0xDF, 0xE1, 0xE5),
        Color.FromRgb(0x9D, 0xA0, 0xA8), Color.FromRgb(0x4C, 0x9A, 0xFF), Color.FromRgb(0xF7, 0x70, 0x67));

    public static ThemePalette Light { get; } = new(
        Color.FromRgb(0xF3, 0xF4, 0xF6), Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0xD0, 0xD3, 0xD9), Color.FromRgb(0x1E, 0x1F, 0x22),
        Color.FromRgb(0x5F, 0x63, 0x68), Color.FromRgb(0x1F, 0x6F, 0xEB), Color.FromRgb(0xC6, 0x28, 0x28));
}

/// <summary>The accent color and the six shades around it that the Fluent theme reads (hover, pressed, selection, focus).</summary>
public sealed record AccentShades(Color Accent, Color Light1, Color Light2, Color Light3, Color Dark1, Color Dark2, Color Dark3)
{
    /// <summary>Three steps towards white and three towards black.</summary>
    public static AccentShades From(Color accent) => new(
        accent,
        Blend(accent, Colors.White, 0.15), Blend(accent, Colors.White, 0.30), Blend(accent, Colors.White, 0.45),
        Blend(accent, Colors.Black, 0.15), Blend(accent, Colors.Black, 0.30), Blend(accent, Colors.Black, 0.45));

    /// <summary>From <paramref name="a"/> (t = 0) to <paramref name="b"/> (t = 1).</summary>
    public static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
}

/// <summary>Switches the whole program between the dark and the light theme at run time (F-VIS-07).</summary>
public static class ThemeManager
{
    /// <summary>The standard controls follow the Fluent theme mode, our own surfaces the palette brushes.</summary>
    public static void Apply(Application application, bool light)
    {
#pragma warning disable WPF0001 // the theme mode of Application is the supported way to switch the Fluent theme
        application.ThemeMode = light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        ApplyPalette(application.Resources, light);
    }

    /// <summary>
    /// Gives the palette brushes of <paramref name="resources"/> the colors of the theme. All XAML refers to them with DynamicResource, so
    /// every open window follows at once.
    /// </summary>
    public static void ApplyPalette(ResourceDictionary resources, bool light)
    {
        var palette = light ? ThemePalette.Light : ThemePalette.Dark;
        Set(resources, "WindowBrush", palette.Window);
        Set(resources, "PanelBrush", palette.Panel);
        Set(resources, "PanelBorderBrush", palette.PanelBorder);
        Set(resources, "TextBrush", palette.Text);
        Set(resources, "MutedTextBrush", palette.MutedText);
        Set(resources, "AccentBrush", palette.Accent);
        Set(resources, "ErrorBrush", palette.Error);

        // the Fluent theme takes the accent color of Windows unless these are set: the program has its own
        var shades = AccentShades.From(palette.Accent);
        resources["SystemAccentColor"] = shades.Accent;
        resources["SystemAccentColorLight1"] = shades.Light1;
        resources["SystemAccentColorLight2"] = shades.Light2;
        resources["SystemAccentColorLight3"] = shades.Light3;
        resources["SystemAccentColorDark1"] = shades.Dark1;
        resources["SystemAccentColorDark2"] = shades.Dark2;
        resources["SystemAccentColorDark3"] = shades.Dark3;
        resources["SystemAccentColorSecondary"] = shades.Light1;
    }

    /// <summary>A new brush under the same key: everything that refers to it with DynamicResource (all windows) takes it over at once.</summary>
    private static void Set(ResourceDictionary resources, string key, Color color) => resources[key] = new SolidColorBrush(color);
}
