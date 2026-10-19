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
}
