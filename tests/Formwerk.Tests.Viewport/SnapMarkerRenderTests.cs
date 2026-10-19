using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The snap point marker is really drawn where the point is (F-SNP-01): the control rendered into a bitmap.</summary>
public class SnapMarkerRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static byte[] Render(IReadOnlyList<Vector3>? markers)
    {
        byte[]? pixels = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new ViewportControl { Width = Width, Height = Height, SnapMarkers = markers };
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

    /// <summary>Pixels of the marker color (a warm yellow) in a window around a point of the picture.</summary>
    private static int YellowPixelsAround(byte[] pixels, double cx, double cy, int radius = 14)
    {
        var count = 0;
        for (var y = (int)cy - radius; y <= (int)cy + radius; y++)
        for (var x = (int)cx - radius; x <= (int)cx + radius; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            var i = (y * Width + x) * 4;
            if (pixels[i + 2] > 200 && pixels[i + 1] > 160 && pixels[i] < 130) count++;
        }

        return count;
    }

    [Fact]
    public void A_marker_is_drawn_at_the_projected_position_of_its_point()
    {
        var point = new Vector3(40, 20, 15);
        var (x, y) = new OrbitCamera().Project(point, Width, Height)!.Value; // the control starts with the default camera

        var pixels = Render([point]);

        Assert.True(YellowPixelsAround(pixels, x, y) >= 12, "the marker should be visible at the point");
    }

    [Fact]
    public void Without_markers_nothing_yellow_is_drawn_there()
    {
        var point = new Vector3(40, 20, 15);
        var (x, y) = new OrbitCamera().Project(point, Width, Height)!.Value;

        var pixels = Render(null);

        Assert.Equal(0, YellowPixelsAround(pixels, x, y));
    }

    [Fact]
    public void The_marker_is_not_drawn_far_from_its_point()
    {
        var point = new Vector3(40, 20, 15);
        var pixels = Render([point]);

        Assert.Equal(0, YellowPixelsAround(pixels, 40, 40));
    }
}
