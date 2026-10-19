using System.Windows;
using System.Windows.Media;
using Formwerk.App.Views;

namespace Formwerk.Tests.App;

/// <summary>The program's own accent color instead of the one of Windows (F-VIS-07).</summary>
public class AccentTests
{
    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    [Fact]
    public void Blending_goes_from_the_first_color_to_the_second()
    {
        var a = Color.FromRgb(10, 100, 200);
        var b = Color.FromRgb(250, 0, 100);

        Assert.Equal(a, AccentShades.Blend(a, b, 0));
        Assert.Equal(b, AccentShades.Blend(a, b, 1));
        Assert.Equal(Color.FromRgb(130, 50, 150), AccentShades.Blend(a, b, 0.5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_shades_run_steadily_from_light_to_dark_around_the_accent(bool light)
    {
        var shades = AccentShades.From((light ? ThemePalette.Light : ThemePalette.Dark).Accent);

        var order = new[] { shades.Light3, shades.Light2, shades.Light1, shades.Accent, shades.Dark1, shades.Dark2, shades.Dark3 }.Select(Luminance).ToList();

        for (var i = 1; i < order.Count; i++) Assert.True(order[i] < order[i - 1], $"shade {i} is not darker than shade {i - 1}");
    }

    [Fact]
    public void The_accent_itself_is_the_one_of_the_palette()
    {
        Assert.Equal(ThemePalette.Dark.Accent, AccentShades.From(ThemePalette.Dark.Accent).Accent);
        Assert.Equal(ThemePalette.Light.Accent, AccentShades.From(ThemePalette.Light.Accent).Accent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_theme_gets_the_accent_and_all_its_shades_under_the_names_the_fluent_theme_reads(bool light)
    {
        var palette = light ? ThemePalette.Light : ThemePalette.Dark;
        var shades = AccentShades.From(palette.Accent);
        var resources = new ResourceDictionary();

        ThemeManager.ApplyPalette(resources, light);

        Assert.Equal(shades.Accent, (Color)resources["SystemAccentColor"]);
        Assert.Equal(shades.Light1, (Color)resources["SystemAccentColorLight1"]);
        Assert.Equal(shades.Light2, (Color)resources["SystemAccentColorLight2"]);
        Assert.Equal(shades.Light3, (Color)resources["SystemAccentColorLight3"]);
        Assert.Equal(shades.Dark1, (Color)resources["SystemAccentColorDark1"]);
        Assert.Equal(shades.Dark2, (Color)resources["SystemAccentColorDark2"]);
        Assert.Equal(shades.Dark3, (Color)resources["SystemAccentColorDark3"]);
        Assert.Equal(shades.Light1, (Color)resources["SystemAccentColorSecondary"]);
    }

    [Fact]
    public void Switching_the_theme_changes_the_accent_colors_too()
    {
        var resources = new ResourceDictionary();

        ThemeManager.ApplyPalette(resources, light: false);
        var dark = (Color)resources["SystemAccentColor"];
        ThemeManager.ApplyPalette(resources, light: true);

        Assert.NotEqual(dark, (Color)resources["SystemAccentColor"]);
        Assert.Equal(ThemePalette.Light.Accent, (Color)resources["SystemAccentColor"]);
    }

    // ---- the brushes of the Fluent theme -------------------------------------------------------------------

    private static (double H, double S, double L) Hsl(Color c) => AccentRecolor.ToHsl(c);

    [Fact]
    public void Hue_saturation_and_lightness_go_there_and_back()
    {
        foreach (var color in new[] { Color.FromRgb(178, 0, 156), Color.FromRgb(76, 154, 255), Color.FromRgb(31, 111, 235), Color.FromRgb(128, 128, 128), Color.FromRgb(0, 0, 0), Color.FromRgb(255, 255, 255) })
        {
            var (h, s, l) = Hsl(color);
            var back = AccentRecolor.FromHsl(h, s, l);

            Assert.True(Math.Abs(back.R - color.R) <= 1 && Math.Abs(back.G - color.G) <= 1 && Math.Abs(back.B - color.B) <= 1, $"{color} came back as {back}");
        }
    }

    [Fact]
    public void The_anchor_becomes_exactly_the_program_accent_and_its_neighbours_keep_their_distance_in_lightness()
    {
        var windows = Color.FromRgb(0xB2, 0x00, 0x9C);          // the accent of this computer, as the Fluent theme uses it
        var ours = ThemePalette.Dark.Accent;

        var anchor = AccentRecolor.Recolor(windows, windows, ours);
        var darker = AccentRecolor.Recolor(Color.FromRgb(0x7F, 0x00, 0x6F), windows, ours);
        var lighter = AccentRecolor.Recolor(Color.FromRgb(0xD6, 0x00, 0xBB), windows, ours);

        Assert.Equal(ours, anchor);
        Assert.True(Hsl(darker).L < Hsl(anchor).L && Hsl(lighter).L > Hsl(anchor).L);
        Assert.InRange(Math.Abs(Hsl(anchor).H - Hsl(darker).H), 0, 3); // all in the hue of the program accent
    }

    [Fact]
    public void Alpha_is_kept()
    {
        var windows = Color.FromRgb(0xB2, 0x00, 0x9C);

        var result = AccentRecolor.Recolor(Color.FromArgb(0x80, 0xB2, 0x00, 0x9C), windows, ThemePalette.Dark.Accent);

        Assert.Equal(0x80, result.A);
    }

    [Theory]
    [InlineData(0xB2, 0x00, 0x9C, true)]    // magenta shades of Windows
    [InlineData(0xD6, 0x00, 0xBB, true)]
    [InlineData(0x7F, 0x00, 0x6F, true)]
    [InlineData(0x80, 0x80, 0x80, false)]   // greys are not the accent
    [InlineData(0xFF, 0xFF, 0xFF, false)]
    [InlineData(0x00, 0x00, 0x00, false)]
    [InlineData(0xE5, 0x39, 0x35, false)]   // red (errors) is another hue
    [InlineData(0x2E, 0x9E, 0x4F, false)]   // green
    public void Only_colors_of_the_hue_of_the_windows_accent_count_as_accent(int r, int g, int b, bool accent)
    {
        var windows = Color.FromRgb(0xB2, 0x00, 0x9C);

        Assert.Equal(accent, AccentRecolor.IsAccent(Color.FromRgb((byte)r, (byte)g, (byte)b), windows));
    }

    [Fact]
    public void A_transparent_color_is_never_accent()
    {
        Assert.False(AccentRecolor.IsAccent(Color.FromArgb(0, 0xB2, 0x00, 0x9C), Color.FromRgb(0xB2, 0x00, 0x9C)));
    }

    // the real dictionaries of the Fluent theme (no Application is created: that would disturb the other tests)
    private static ResourceDictionary LoadFluent(bool light)
    {
        // registers the pack:// scheme and its resource loading, which the static constructor of Application does (without creating one)
        _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(Application).TypeHandle);
        return new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent." + (light ? "Light" : "Dark") + ".xaml"),
        };
    }

    [UiFact]
    public void In_the_real_fluent_theme_no_brush_keeps_the_hue_of_the_windows_accent()
    {
        foreach (var light in new[] { false, true })
        {
            var fluent = LoadFluent(light);
            var anchor = ((SolidColorBrush)fluent["AccentFillColorDefaultBrush"]).Color;       // as Windows has it
            var palette = light ? ThemePalette.Light : ThemePalette.Dark;
            var resources = new ResourceDictionary();

            ThemeManager.RecolorFluentAccent(resources, fluent, light);

            Assert.Equal(palette.Accent, ((SolidColorBrush)resources["AccentFillColorDefaultBrush"]).Color);
            Assert.Equal(0.9, ((SolidColorBrush)resources["AccentFillColorSecondaryBrush"]).Opacity);
            Assert.True(resources.Count > 15, $"only {resources.Count} brushes were recolored");
            if (Math.Abs(Hsl(anchor).H - Hsl(palette.Accent).H) < 20) continue; // Windows has a blue accent as well: nothing to tell apart

            // what a control finds first (our brushes) or else in the theme
            var leftover = fluent.Keys.Cast<object>().Select(k => k.ToString()!)
                .Where(k => (resources.Contains(k) ? resources[k] : fluent[k]) is SolidColorBrush b && AccentRecolor.IsAccent(b.Color, anchor)).ToList();
            Assert.True(leftover.Count == 0, (light ? "light: " : "dark: ") + string.Join(", ", leftover.Take(8)));
        }
    }

    [UiFact]
    public void Recoloring_twice_gives_the_same_and_switching_theme_removes_the_old_brushes()
    {
        var resources = new ResourceDictionary();
        var dark = LoadFluent(false);
        var light = LoadFluent(true);

        ThemeManager.RecolorFluentAccent(resources, dark, light: false);
        var first = resources.Count;
        ThemeManager.RecolorFluentAccent(resources, dark, light: false);
        Assert.Equal(first, resources.Count);

        ThemeManager.RecolorFluentAccent(resources, light, light: true);
        Assert.Equal(ThemePalette.Light.Accent, ((SolidColorBrush)resources["AccentFillColorDefaultBrush"]).Color);
        Assert.DoesNotContain(resources.Keys.Cast<object>(), k => !((light.Contains(k)) && light[k] is SolidColorBrush)); // only keys that the light theme has too
    }

    [UiFact]
    public void Accent_text_is_readable_on_the_window_in_both_themes()
    {
        foreach (var lightTheme in new[] { false, true })
        {
            var palette = lightTheme ? ThemePalette.Light : ThemePalette.Dark;
            var resources = new ResourceDictionary();
            ThemeManager.RecolorFluentAccent(resources, LoadFluent(lightTheme), lightTheme);

            var text = ((SolidColorBrush)resources["AccentTextFillColorPrimaryBrush"]).Color;

            var (hi, lo) = (Math.Max(Luminance(text), Luminance(palette.Window)), Math.Min(Luminance(text), Luminance(palette.Window)));
            Assert.True((hi + 0.05) / (lo + 0.05) >= 4.5, "accent text on the window, light theme: " + lightTheme);
        }
    }
}
