using System.Globalization;
using System.Text;

namespace Formwerk.App.ViewModels;

/// <summary>A point of the section picture, in picture units (the picture is <see cref="HoleSection.Width"/> by <see cref="HoleSection.Height"/>).</summary>
public readonly record struct SectionPoint(double X, double Y);

/// <summary>
/// The cross-section picture of a hole (F-HOL-02): the body cut open along the axis of the hole, with the hole in it. Pure geometry
/// in picture units; the view draws <see cref="Body"/>, <see cref="Hole"/> and <see cref="Axis"/> as paths in the colors of the
/// theme. The whole hole always fits: a deep hole is drawn smaller.
/// </summary>
public sealed class HoleSection
{
    public const double Width = 140;
    public const double Height = 90;

    private const double Margin = 8;
    private const double Under = 8; // material drawn below the bottom of the hole

    private HoleSection(IReadOnlyList<SectionPoint> hole, double surfaceY, double left, double right, double bottom)
    {
        HolePolygon = hole;
        SurfaceY = surfaceY;
        BodyLeft = left;
        BodyRight = right;
        BodyBottom = bottom;
    }

    public IReadOnlyList<SectionPoint> HolePolygon { get; }

    /// <summary>Height of the surface of the body in the picture (the top of the body).</summary>
    public double SurfaceY { get; }

    public double BodyTop => SurfaceY;

    public double BodyLeft { get; }

    public double BodyRight { get; }

    public double BodyBottom { get; }

    /// <summary>The block of material, as path data.</summary>
    public string Body => Path([new(BodyLeft, BodyTop), new(BodyRight, BodyTop), new(BodyRight, BodyBottom), new(BodyLeft, BodyBottom)]);

    /// <summary>The hole, as path data (empty without an outline).</summary>
    public string Hole => Path(HolePolygon);

    /// <summary>The axis of the hole, dashed by the view.</summary>
    public string Axis => string.Create(CultureInfo.InvariantCulture, $"M {Width / 2:0.##},{SurfaceY - 6:0.##} L {Width / 2:0.##},{BodyBottom + 2:0.##}");

    /// <param name="profile">The outline of the hole: (distance from the axis, height), from the top on the axis around the outside to the bottom on the axis; the surface is at height 0.</param>
    public static HoleSection From(IReadOnlyList<(float R, float Z)> profile)
    {
        const double surfaceY = 14;
        if (profile.Count == 0) return new HoleSection([], surfaceY, Margin, Width - Margin, surfaceY + Under);

        var radius = profile.Max(p => (double)p.R);
        var depth = Math.Max(0.01, -profile.Min(p => (double)p.Z));
        var halfBody = Math.Max(radius * 1.6, radius + 1);
        var scale = Math.Min((Width / 2 - Margin) / halfBody, (Height - 2 - Under - surfaceY) / depth);
        var center = Width / 2;

        SectionPoint At(double r, double z) => new(center + r * scale, surfaceY - Math.Min(z, 0) * scale); // above the surface is cut off

        var polygon = new List<SectionPoint>();
        foreach (var (r, z) in profile) Add(polygon, At(r, z));
        for (var i = profile.Count - 1; i >= 0; i--)
        {
            if (profile[i].R > 0) Add(polygon, At(-profile[i].R, profile[i].Z));
        }

        return new HoleSection(polygon, surfaceY, center - halfBody * scale, center + halfBody * scale, surfaceY + depth * scale + Under);
    }

    private static void Add(List<SectionPoint> polygon, SectionPoint point)
    {
        if (polygon.Count > 0 && Math.Abs(polygon[^1].X - point.X) < 1e-9 && Math.Abs(polygon[^1].Y - point.Y) < 1e-9) return;
        polygon.Add(point);
    }

    private static string Path(IReadOnlyList<SectionPoint> points)
    {
        if (points.Count == 0) return "";
        var sb = new StringBuilder();
        for (var i = 0; i < points.Count; i++)
        {
            sb.Append(i == 0 ? "M " : " L ").Append(points[i].X.ToString("0.##", CultureInfo.InvariantCulture)).Append(',')
              .Append(points[i].Y.ToString("0.##", CultureInfo.InvariantCulture));
        }

        return sb.Append(" Z").ToString();
    }
}
