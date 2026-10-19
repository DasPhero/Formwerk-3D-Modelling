using System.Windows;
using System.Windows.Media;

namespace Formwerk.Viewport;

/// <summary>The light theme of the 3D view (F-VIS-07): background gradient, plate and grid; the dark theme is the default.</summary>
public sealed partial class ViewportControl
{
    public static readonly DependencyProperty LightThemeProperty = DependencyProperty.Register(
        nameof(LightTheme), typeof(bool), typeof(ViewportControl),
        new PropertyMetadata(false, (d, _) => ((ViewportControl)d).OnThemeChanged()));

    /// <summary>A light background and plate instead of the dark ones.</summary>
    public bool LightTheme
    {
        get => (bool)GetValue(LightThemeProperty);
        set => SetValue(LightThemeProperty, value);
    }

    private readonly record struct BedColors(Color Plate, Color Minor, Color Major, Color Border, Color Frame)
    {
        public static BedColors Dark { get; } = new(
            Color.FromRgb(0x2B, 0x2D, 0x32), Color.FromRgb(0x3A, 0x3D, 0x44), Color.FromRgb(0x55, 0x59, 0x62),
            Color.FromRgb(0x7B, 0x84, 0x93), Color.FromRgb(0x3F, 0x46, 0x52));

        public static BedColors Light { get; } = new(
            Color.FromRgb(0xE3, 0xE6, 0xEC), Color.FromRgb(0xC9, 0xCD, 0xD5), Color.FromRgb(0xA9, 0xAF, 0xBA),
            Color.FromRgb(0x5E, 0x67, 0x76), Color.FromRgb(0xB4, 0xBA, 0xC6));
    }

    private static Brush BackgroundFor(bool light) => light
        ? new LinearGradientBrush(Color.FromRgb(0xF2, 0xF4, 0xF8), Color.FromRgb(0xCD, 0xD2, 0xDB), 90)
        : new LinearGradientBrush(Color.FromRgb(0x30, 0x33, 0x39), Color.FromRgb(0x1B, 0x1C, 0x20), 90);

    private void OnThemeChanged()
    {
        Background = BackgroundFor(LightTheme);
        RebuildBed();
        UpdateGizmoTransform(); // markers and dimension lines have their own colors
    }
}
