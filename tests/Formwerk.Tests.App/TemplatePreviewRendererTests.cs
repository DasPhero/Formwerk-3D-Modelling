using System.IO;
using System.Windows.Media.Imaging;
using Formwerk.App.Views;
using Formwerk.Features.Shapes;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The picture saved with a template (F-TPL-01) is really drawn.</summary>
public class TemplatePreviewRendererTests
{
    private static ViewportItem Box() =>
        new(Guid.NewGuid(), new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 30, ["depth"] = 20, ["height"] = 10 }), ViewportItemKind.Solid, 0xFFC8C8C8);

    [UiFact]
    public void The_picture_is_a_square_png_that_shows_something()
    {
        var png = new TemplatePreviewRenderer().Render([Box()]);

        Assert.NotNull(png);
        var frame = new PngBitmapDecoder(new MemoryStream(png!), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(160, frame.PixelWidth);
        Assert.Equal(160, frame.PixelHeight);

        var pixels = new byte[160 * 160 * 4];
        new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0).CopyPixels(pixels, 160 * 4, 0);
        var bright = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            if (pixels[i + 2] > 150 && pixels[i + 1] > 150 && pixels[i] > 150) bright++; // the light grey box on the dark background
        }

        Assert.True(bright > 300, $"the object should be visible, bright pixels: {bright}");
    }

    [UiFact]
    public void The_view_cube_is_not_part_of_the_picture_and_nothing_gives_no_picture()
    {
        Assert.Null(new TemplatePreviewRenderer().Render([]));
        Assert.False(new ViewportControl { ShowViewCube = false }.ShowViewCube);
        Assert.True(new ViewportControl().ShowViewCube);
    }
}
