using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The control really draws hatched ghosts (F-VIS-04): rendered into a bitmap and looked at.</summary>
public class GhostRenderTests
{
    private const int Width = 480;
    private const int Height = 360;

    private static T OnStaThread<T>(Func<T> work)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try { result = work(); }
            catch (Exception ex) { error = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new InvalidOperationException("Rendering failed", error);
        return result!;
    }

    private static byte[] Render(ViewportItemKind kind, bool dimmed = false)
    {
        return OnStaThread(() =>
        {
            var box = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 60, ["depth"] = 60, ["height"] = 60 });
            var control = new ViewportControl { Width = Width, Height = Height, Items = [new ViewportItem(Guid.NewGuid(), box, kind, 0xFFC0C0C0, Dimmed: dimmed)] };
            var size = new Size(Width, Height);
            control.Measure(size);
            control.Arrange(new Rect(size));
            control.UpdateLayout();
            control.SetView(ViewPreset.Isometric);
            control.FrameSelection();
            control.UpdateLayout();

            var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var pixels = new byte[Width * Height * 4];
            bitmap.CopyPixels(pixels, Width * 4, 0);

            // FORMWERK_SAVE_RENDER=folder: keep the pictures to look at them
            if (Environment.GetEnvironmentVariable("FORMWERK_SAVE_RENDER") is { Length: > 0 } folder)
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(folder, $"ghost-{kind}-{(dimmed ? "dimmed" : "normal")}.png"));
                encoder.Save(file);
            }

            return pixels;
        });
    }

    /// <summary>How often the red channel jumps by a clear amount along the middle line of the picture.</summary>
    private static int Jumps(byte[] pixels)
    {
        var count = 0;
        var y = Height / 2;
        var previous = pixels[(y * Width + 100) * 4 + 2];
        for (var x = 101; x < Width - 100; x++)
        {
            var red = pixels[(y * Width + x) * 4 + 2];
            if (Math.Abs(red - previous) >= 6) count++;
            previous = red;
        }

        return count;
    }

    [Fact]
    public void A_hole_ghost_is_drawn_with_stripes()
    {
        var pixels = Render(ViewportItemKind.Ghost);

        Assert.True(Jumps(pixels) >= 6, $"expected stripes, brightness jumps along the line: {Jumps(pixels)}");
    }

    [Fact]
    public void A_solid_is_drawn_without_stripes()
    {
        var pixels = Render(ViewportItemKind.Solid);

        Assert.True(Jumps(pixels) <= 4, $"a solid has a smooth surface, jumps: {Jumps(pixels)}");
    }

    [Fact]
    public void A_dimmed_ghost_is_not_hatched()
    {
        var dimmed = Jumps(Render(ViewportItemKind.Ghost, dimmed: true));
        var hatched = Jumps(Render(ViewportItemKind.Ghost));

        // the faint outline of the box still makes a few jumps; the stripes make clearly more
        Assert.True(dimmed < hatched, $"dimmed {dimmed}, hatched {hatched}");
    }
}
