using System.Windows.Media;

namespace Formwerk.App.Views;

/// <summary>
/// Moves colors from the accent of Windows to the accent of the program (F-VIS-07): the hue is replaced, the differences in lightness
/// to the "anchor" (the default accent fill of the theme) are kept, so that hover, pressed, text and selection shades stay in order.
/// </summary>
public static class AccentRecolor
{
    private const double HueTolerance = 14;

    public static (double H, double S, double L) ToHsl(Color c)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min) return (0, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? ((g - b) / d + (g < b ? 6 : 0)) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Color FromHsl(double h, double s, double l, byte alpha = 255)
    {
        h = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);
        double r, g, b;
        if (s == 0)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = Channel(p, q, h + 1.0 / 3);
            g = Channel(p, q, h);
            b = Channel(p, q, h - 1.0 / 3);
        }

        return Color.FromArgb(alpha, (byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    private static double Channel(double p, double q, double t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 0.5) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    /// <summary>A visible, colorful color in the hue of <paramref name="windowsAccent"/> (greys, white, black and other hues are not the accent).</summary>
    public static bool IsAccent(Color color, Color windowsAccent)
    {
        if (color.A == 0) return false;
        var (h, s, l) = ToHsl(color);
        if (s < 0.3 || l < 0.04 || l > 0.96) return false;
        var difference = Math.Abs(h - ToHsl(windowsAccent).H);
        return Math.Min(difference, 360 - difference) <= HueTolerance;
    }

    /// <summary>
    /// <paramref name="color"/> in the hue of <paramref name="ours"/>, moved in lightness by the same amount as
    /// <paramref name="windowsAnchor"/> is moved to <paramref name="ours"/>; alpha is kept.
    /// </summary>
    public static Color Recolor(Color color, Color windowsAnchor, Color ours)
    {
        var (_, _, anchorL) = ToHsl(windowsAnchor);
        var (ourH, ourS, ourL) = ToHsl(ours);
        var (_, _, l) = ToHsl(color);
        return FromHsl(ourH, ourS, l + (ourL - anchorL), color.A);
    }
}
