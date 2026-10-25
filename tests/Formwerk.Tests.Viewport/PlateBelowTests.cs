using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>Looking from below the build plate lets the model show through (user wish): the rules, and a bitmap of the real control.</summary>
public class PlateBelowTests
{
    private const int Width = 480;
    private const int Height = 360;

    [Theory]
    [InlineData(100, false, 1.0)]
    [InlineData(0.5, false, 1.0)]
    [InlineData(0, false, 1.0)]
    [InlineData(-0.01, false, 1.0)] // on the plate: still solid
    [InlineData(-1, true, PlateRules.BelowOpacity)]
    [InlineData(-300, true, PlateRules.BelowOpacity)]
    public void The_plate_is_see_through_only_when_the_camera_is_below_it(double cameraZ, bool below, double opacity)
    {
        Assert.Equal(below, PlateRules.IsBelow(cameraZ));
        Assert.Equal(opacity, PlateRules.OpacityFor(cameraZ));
    }

    private static double CenterBrightness(ViewPreset view, bool saveAs = false)
    {
        double result = 0;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var box = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 60, ["depth"] = 60, ["height"] = 60 });
                var control = new ViewportControl
                {
                    Width = Width, Height = Height, ShowViewCube = false, ShowAxes = false,
                    Items = [new ViewportItem(Guid.NewGuid(), box, ViewportItemKind.Solid, 0xFFC0C0C0)],
                };
                var size = new Size(Width, Height);
                control.Measure(size);
                control.Arrange(new Rect(size));
                control.UpdateLayout();
                control.SetView(view);
                control.FrameSelection();
                control.UpdateLayout();

                var bitmap = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(control);
                var pixels = new byte[Width * Height * 4];
                bitmap.CopyPixels(pixels, Width * 4, 0);
                if (Environment.GetEnvironmentVariable("FORMWERK_SAVE_RENDER") is { Length: > 0 } folder)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(folder, $"plate-{view}.png"));
                    encoder.Save(file);
                }

                var i = (Height / 2 * Width + Width / 2) * 4;
                result = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3.0;
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
        return result;
    }

    [Fact]
    public void From_below_the_model_is_visible_through_the_plate()
    {
        var below = CenterBrightness(ViewPreset.Bottom);

        // the plate alone is dark (about 43); the light grey box shows through it
        Assert.True(below > 75, $"center from below {below:0}");
    }

    [Fact]
    public void From_above_the_model_is_drawn_as_before()
    {
        var top = CenterBrightness(ViewPreset.Top);

        Assert.True(top > 120, $"center from above {top:0}");
    }
}
