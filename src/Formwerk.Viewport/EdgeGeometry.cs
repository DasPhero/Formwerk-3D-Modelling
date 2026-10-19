using System.Numerics;
using System.Windows.Media.Media3D;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>How the view draws objects (F-VIS-03).</summary>
public enum ViewportDisplayMode
{
    /// <summary>Surfaces only.</summary>
    Shaded,

    /// <summary>Surfaces and their sharp edges (the default).</summary>
    ShadedWithEdges,

    /// <summary>Every triangle edge as a line; the surfaces are almost invisible but can still be clicked.</summary>
    Wireframe,

    /// <summary>Transparent surfaces with edges, to see inside.</summary>
    XRay,
}

/// <summary>
/// Turns edges into geometry. WPF's 3D has no line primitive, so an edge becomes two thin quads that cross each other along it:
/// seen from any side the line has about the same width.
/// </summary>
public static class EdgeGeometry
{
    /// <summary>Line width on screen in pixels.</summary>
    public const double LinePixels = 1.6;

    /// <summary>Half the width of a line in mm, for a given size of one pixel in mm.</summary>
    public static double HalfWidth(double worldPerPixel, double pixels = LinePixels) => Math.Max(worldPerPixel, 0) * pixels / 2;

    /// <summary>The lines keep their pixel width only roughly: they are rebuilt when the wanted width differs by more than a fifth.</summary>
    public static bool NeedsRebuild(double builtHalfWidth, double wantedHalfWidth) =>
        builtHalfWidth <= 0 || Math.Abs(wantedHalfWidth / builtHalfWidth - 1) > 0.2;

    public static MeshGeometry3D Build(IEnumerable<EdgeSet> edgeSets, double halfWidth)
    {
        var positions = new Point3DCollection();
        var indices = new System.Windows.Media.Int32Collection();
        var h = (float)halfWidth;

        foreach (var set in edgeSets)
        {
            for (var i = 0; i < set.Count; i++)
            {
                var (a, b) = set.Segment(i);
                var direction = b - a;
                var length = direction.Length();
                if (length < 1e-9f) continue;
                direction /= length;

                // two directions across the edge, at right angles to each other
                var helper = Math.Abs(direction.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
                var p = Vector3.Normalize(Vector3.Cross(direction, helper)) * h;
                var q = Vector3.Normalize(Vector3.Cross(direction, p)) * h;

                AddQuad(positions, indices, a - p, a + p, b + p, b - p);
                AddQuad(positions, indices, a - q, a + q, b + q, b - q);
            }
        }

        var geometry = new MeshGeometry3D { Positions = positions, TriangleIndices = indices };
        geometry.Freeze();
        return geometry;
    }

    private static void AddQuad(Point3DCollection positions, System.Windows.Media.Int32Collection indices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        var start = positions.Count;
        positions.Add(new Point3D(a.X, a.Y, a.Z));
        positions.Add(new Point3D(b.X, b.Y, b.Z));
        positions.Add(new Point3D(c.X, c.Y, c.Z));
        positions.Add(new Point3D(d.X, d.Y, d.Z));
        foreach (var i in new[] { 0, 1, 2, 0, 2, 3 }) indices.Add(start + i);
    }
}

/// <summary>Edge colors that stand out from the surface, whatever color the object has (F-VIS-02).</summary>
public static class EdgeColors
{
    /// <summary>
    /// Color (R, G, B) of the edges of an object with the given ARGB color: dark on light objects, light on dark ones.
    /// With <paramref name="seeThrough"/> (wireframe) the surface is almost invisible, so the lines have to stand out from the background: light.
    /// </summary>
    public static (byte R, byte G, byte B) For(uint argb, bool selected, bool seeThrough = false)
    {
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        var luminance = 0.2126 * r + 0.7152 * g + 0.0722 * b;
        var lightSurface = luminance > 105 && !seeThrough;
        return (lightSurface, selected) switch
        {
            (true, false) => (0x1A, 0x1C, 0x20),
            (true, true) => (0x0A, 0x24, 0x4A),
            (false, false) => (0xE6, 0xE9, 0xEE),
            (false, true) => (0xCF, 0xE4, 0xFF),
        };
    }
}
