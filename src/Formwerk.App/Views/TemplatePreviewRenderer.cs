using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Formwerk.App.ViewModels;
using Formwerk.Viewport;

namespace Formwerk.App.Views;

/// <summary>Makes the picture of a template by drawing the items with a 3D view that is never shown (F-TPL-01).</summary>
public sealed class TemplatePreviewRenderer : ITemplatePreview
{
    private const int Size = 160;

    public byte[]? Render(IReadOnlyList<ViewportItem> items)
    {
        if (items.Count == 0) return null;
        try
        {
            var control = new ViewportControl { Width = Size, Height = Size, ShowViewCube = false, Items = [.. items.Select(i => i with { Dimmed = false })] };
            var size = new Size(Size, Size);
            control.Measure(size);
            control.Arrange(new Rect(size));
            control.UpdateLayout();
            control.SetView(ViewPreset.Isometric);
            control.FrameSelection();
            control.UpdateLayout();

            var bitmap = new RenderTargetBitmap(Size, Size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(control);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return null; // no picture is better than no template
        }
    }
}
