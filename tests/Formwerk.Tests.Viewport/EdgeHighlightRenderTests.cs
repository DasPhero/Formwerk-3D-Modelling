using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The edge under the mouse is drawn where the edge is: yellow when it can be cut, grey when it cannot (F-EDG-02).</summary>
public class EdgeHighlightRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static byte[] Render(ViewportEdgeHighlight? highlight)
    {
        byte[]? pixels = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new ViewportControl { Width = Width, Height = Height, EdgeHighlight = highlight };
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

    private static int Around(byte[] pixels, double cx, double cy, Func<byte, byte, byte, bool> test, int radius = 10)
    {
        var count = 0;
        for (var y = (int)cy - radius; y <= (int)cy + radius; y++)
        for (var x = (int)cx - radius; x <= (int)cx + radius; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            var i = (y * Width + x) * 4;
            if (test(pixels[i + 2], pixels[i + 1], pixels[i])) count++;
        }

        return count;
    }

    private static bool Yellow(byte r, byte g, byte b) => r > 200 && g > 160 && b < 130;

    private static bool Grey(byte r, byte g, byte b) => Math.Abs(r - g) < 14 && Math.Abs(g - b) < 22 && r is > 110 and < 190;

    private static (double X, double Y) Project(Vector3 point) => new OrbitCamera().Project(point, Width, Height)!.Value;

    [Fact]
    public void A_valid_edge_is_drawn_yellow_along_its_line()
    {
        Vector3 a = new(-30, 0, 10), b = new(30, 0, 10);
        var (x, y) = Project((a + b) / 2);

        var pixels = Render(new ViewportEdgeHighlight([a, b], false, true));

        Assert.True(Around(pixels, x, y, Yellow) >= 20, "the edge should be visible");
    }

    [Fact]
    public void An_edge_that_cannot_be_cut_is_drawn_grey()
    {
        Vector3 a = new(-30, 0, 10), b = new(30, 0, 10);
        var (x, y) = Project((a + b) / 2);

        var pixels = Render(new ViewportEdgeHighlight([a, b], false, false));

        Assert.True(Around(pixels, x, y, Grey) >= 20, "the grey edge should be visible");
        Assert.Equal(0, Around(pixels, x, y, Yellow));
    }

    [Fact]
    public void Without_a_highlight_nothing_is_drawn_there()
    {
        Vector3 a = new(-30, 0, 10), b = new(30, 0, 10);
        var (x, y) = Project((a + b) / 2);

        var pixels = Render(null);

        Assert.Equal(0, Around(pixels, x, y, Yellow));
    }

    [Fact]
    public void A_closed_outline_is_drawn_all_around()
    {
        var points = Enumerable.Range(0, 48).Select(i => new Vector3(25 * MathF.Cos(MathF.Tau * i / 48), 25 * MathF.Sin(MathF.Tau * i / 48), 10)).ToList();
        var (x, y) = Project(points[12]); // a quarter of the way round
        var (lx, ly) = Project((points[47] + points[0]) / 2); // the closing piece

        var pixels = Render(new ViewportEdgeHighlight(points, true, true));

        Assert.True(Around(pixels, x, y, Yellow) >= 8, "the circle should be visible everywhere");
        Assert.True(Around(pixels, lx, ly, Yellow) >= 4, "the circle should be closed");
    }

    [Fact]
    public void The_highlight_is_not_drawn_far_from_the_edge()
    {
        Vector3 a = new(-30, 0, 10), b = new(30, 0, 10);

        var pixels = Render(new ViewportEdgeHighlight([a, b], false, true));

        Assert.Equal(0, Around(pixels, 40, 40, Yellow));
    }
}
