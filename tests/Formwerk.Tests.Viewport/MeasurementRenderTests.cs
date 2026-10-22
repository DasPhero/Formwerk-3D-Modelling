using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>Measurements are drawn where they are: lines and marks in the view, the value in a label next to them (E-07).</summary>
public class MeasurementRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static byte[] Render(IReadOnlyList<ViewportMeasurement>? measurements)
    {
        byte[]? pixels = null;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var control = new ViewportControl { Width = Width, Height = Height, Measurements = measurements };
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

    private static int Cyan(byte[] pixels, double cx, double cy, int radius = 10)
    {
        var count = 0;
        for (var y = (int)cy - radius; y <= (int)cy + radius; y++)
        for (var x = (int)cx - radius; x <= (int)cx + radius; x++)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) continue;
            var i = (y * Width + x) * 4;
            if (pixels[i] > 200 && pixels[i + 1] > 150 && pixels[i + 2] < 140) count++; // B, G high and R low in BGRA: the cyan of the measurements
        }

        return count;
    }

    private static (double X, double Y) Project(Vector3 point) => new OrbitCamera().Project(point, Width, Height)!.Value;

    private static readonly Vector3 A = new(-30, 0, 10), B = new(30, 0, 10);

    private static ViewportMeasurement Line(string? text = null, Vector3? labelPoint = null) =>
        new([A, B], [(A, B)], text, labelPoint ?? new Vector3(0, 0, 10));

    [Fact]
    public void A_line_is_drawn_between_its_points()
    {
        var (x, y) = Project((A + B) / 2);

        var pixels = Render([Line()]);

        Assert.True(Cyan(pixels, x, y) >= 15, "the line should be visible");
    }

    [Fact]
    public void The_marks_are_drawn_at_the_points()
    {
        var (x, y) = Project(A);

        var pixels = Render([Line()]);

        Assert.True(Cyan(pixels, x, y) >= 15, "the mark should be visible");
    }

    [Fact]
    public void A_single_mark_without_a_line_is_shown_too()
    {
        var (x, y) = Project(new Vector3(5, 5, 5));

        var pixels = Render([new ViewportMeasurement([new Vector3(5, 5, 5)], [], null, new Vector3(5, 5, 5))]);

        Assert.True(Cyan(pixels, x, y) >= 8);
    }

    [Fact]
    public void Without_measurements_nothing_is_drawn()
    {
        var (x, y) = Project((A + B) / 2);

        Assert.Equal(0, Cyan(Render(null), x, y));
        Assert.Equal(0, Cyan(Render([]), x, y));
    }

    [Fact]
    public void The_value_is_written_next_to_the_measurement()
    {
        var labelPoint = new Vector3(0, 0, 40); // far above the line, so that only the label can be there
        var (x, y) = Project(labelPoint);

        var withText = Render([Line("23.46 mm", labelPoint)]);
        var without = Render([Line(null, labelPoint)]);

        Assert.True(Cyan(withText, x, y, 40) > Cyan(without, x, y, 40) + 20, "the label should be drawn");
    }

    [Fact]
    public void Several_measurements_are_all_drawn()
    {
        Vector3 c = new(-20, 20, 0), d = new(20, 20, 0);
        var (x1, y1) = Project((A + B) / 2);
        var (x2, y2) = Project((c + d) / 2);

        var pixels = Render([Line(), new ViewportMeasurement([c, d], [(c, d)], null, c)]);

        Assert.True(Cyan(pixels, x1, y1) >= 15);
        Assert.True(Cyan(pixels, x2, y2) >= 15);
    }

    [Fact]
    public void A_measurement_is_not_drawn_far_from_its_line()
    {
        var pixels = Render([Line()]);

        Assert.Equal(0, Cyan(pixels, 40, 40));
    }

    [Fact]
    public void A_zero_length_line_does_not_break_the_drawing()
    {
        var pixels = Render([new ViewportMeasurement([A, A], [(A, A)], "0.00 mm", A)]);

        Assert.NotNull(pixels);
    }
}
