using System.Numerics;
using System.Windows;
using System.Windows.Media.Imaging;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The places of a mesh problem are drawn red where they are (F-REP-01).</summary>
public class ProblemMarksRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static byte[] Render(ViewportProblemMarks? marks)
    {
        byte[]? pixels = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new ViewportControl { Width = Width, Height = Height, ProblemMarks = marks };
                var size = new Size(Width, Height);
                control.Measure(size);
                control.Arrange(new Rect(size));
                control.UpdateLayout();
                var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
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

    private static int Red(byte[] pixels, double cx, double cy, int radius = 10)
    {
        var count = 0;
        for (var y = (int)cy - radius; y <= (int)cy + radius; y++)
        for (var x = (int)cx - radius; x <= (int)cx + radius; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            var i = (y * Width + x) * 4;
            if (pixels[i + 2] > 200 && pixels[i + 1] < 120 && pixels[i] < 120) count++; // R high, G and B low in BGRA
        }

        return count;
    }

    private static (double X, double Y) Project(Vector3 point) => new OrbitCamera().Project(point, Width, Height)!.Value;

    private static readonly Vector3 A = new(-30, 0, 10), B = new(30, 0, 10);

    [Fact]
    public void A_bar_is_drawn_along_its_segment()
    {
        var (x, y) = Project((A + B) / 2);

        var pixels = Render(new ViewportProblemMarks([(A, B)], []));

        Assert.True(Red(pixels, x, y) >= 15, "the bar should be visible");
    }

    [Fact]
    public void A_point_is_drawn_as_a_mark()
    {
        var point = new Vector3(5, 5, 5);
        var (x, y) = Project(point);

        var pixels = Render(new ViewportProblemMarks([], [point]));

        Assert.True(Red(pixels, x, y) >= 8, "the mark should be visible");
    }

    [Fact]
    public void Without_marks_or_with_empty_marks_nothing_is_drawn()
    {
        var (x, y) = Project((A + B) / 2);

        Assert.Equal(0, Red(Render(null), x, y));
        Assert.Equal(0, Red(Render(new ViewportProblemMarks([], [])), x, y));
    }
}
