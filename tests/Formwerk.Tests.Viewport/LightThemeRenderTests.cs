using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The 3D view in the light theme (F-VIS-07): a light background and a light plate, rendered into a bitmap.</summary>
public class LightThemeRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static byte[] Render(bool light, IReadOnlyList<Vector3>? markers = null)
    {
        byte[]? pixels = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new ViewportControl { Width = Width, Height = Height, LightTheme = light, SnapMarkers = markers };
                var size = new Size(Width, Height);
                control.Measure(size);
                control.Arrange(new Rect(size));
                control.UpdateLayout();
                var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(control);
                pixels = new byte[Width * Height * 4];
                bitmap.CopyPixels(pixels, Width * 4, 0);
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Rendering failed", error);
        return pixels!;
    }

    private static double Brightness(byte[] pixels, int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3.0;
    }

    [Fact]
    public void The_dark_theme_has_a_dark_background_and_the_light_theme_a_light_one()
    {
        var dark = Render(light: false);
        var light = Render(light: true);

        Assert.True(Brightness(dark, 20, 20) < 90, $"dark corner {Brightness(dark, 20, 20):0}");
        Assert.True(Brightness(light, 20, 20) > 170, $"light corner {Brightness(light, 20, 20):0}");
        Assert.True(Brightness(light, 20, Height - 20) > 150, "the whole background is light, not only the top");
    }

    [Fact]
    public void The_plate_is_light_in_the_light_theme()
    {
        var dark = Render(light: false);
        var light = Render(light: true);

        // somewhere on the plate in front of the camera
        Assert.True(Brightness(dark, Width / 2, Height - 60) < 90);
        Assert.True(Brightness(light, Width / 2, Height - 60) > 150);
    }

    [Fact]
    public void A_marker_can_be_told_from_the_light_plate()
    {
        var point = new Vector3(40, 20, 15);
        var (x, y) = new OrbitCamera().Project(point, Width, Height)!.Value;
        var with = Render(light: true, [point]);
        var without = Render(light: true);

        // the marker (a yellow cube) changes the picture clearly at its place
        var changed = 0;
        for (var dy = -10; dy <= 10; dy++)
        for (var dx = -10; dx <= 10; dx++)
        {
            if (Math.Abs(Brightness(with, (int)x + dx, (int)y + dy) - Brightness(without, (int)x + dx, (int)y + dy)) > 30) changed++;
        }

        Assert.True(changed >= 10, $"changed pixels around the marker: {changed}");
    }
}
