using System.Numerics;
using Formwerk.Geometry;

namespace Formwerk.Viewport;

/// <summary>A rectangle on the screen in device independent pixels (origin top left).</summary>
public readonly record struct ScreenRect(double Left, double Top, double Right, double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;

    /// <summary>The rectangle between two corners, whichever corner was pressed first.</summary>
    public static ScreenRect FromCorners(double x1, double y1, double x2, double y2) =>
        new(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));

    public bool Contains(double x, double y) => x >= Left && x <= Right && y >= Top && y <= Bottom;
}

/// <summary>How a selection rectangle takes objects (F-SEL-01).</summary>
public enum MarqueeMode
{
    /// <summary>Dragged to the right: only objects completely inside.</summary>
    Window,

    /// <summary>Dragged to the left: every object the rectangle touches.</summary>
    Crossing,
}

/// <summary>Reported by the viewport when a rectangle was dragged: the objects taken, and whether they are added to the selection.</summary>
public sealed record MarqueeRequest(IReadOnlyList<Guid> NodeIds, bool Additive);

/// <summary>
/// Which objects a screen rectangle takes (F-SEL-01). Pure math on the displayed meshes and the camera. Hidden surfaces are not
/// considered: an object behind another one is taken too, as in most CAD programs without X-ray.
/// </summary>
public static class MarqueeSelection
{
    /// <summary>
    /// Meshes with up to this many triangles are tested triangle by triangle (a "touch" means the rectangle meets the drawn
    /// surface); bigger meshes use the box around them, which is cheap and good enough for a rough selection.
    /// </summary>
    public const int DetailTriangleLimit = 20_000;

    /// <summary>Left to right is a window, right to left a crossing (like AutoCAD).</summary>
    public static MarqueeMode ModeFor(double startX, double endX) => endX >= startX ? MarqueeMode.Window : MarqueeMode.Crossing;

    /// <summary>
    /// The ids of the objects taken by <paramref name="rect"/> in a view of <paramref name="width"/> x <paramref name="height"/>
    /// pixels, each once, in the order of <paramref name="items"/>. An object with several meshes needs all of them inside for a
    /// window and one of them touching for a crossing. Parts behind the camera cannot be seen and never count.
    /// </summary>
    public static IReadOnlyList<Guid> Select(
        IReadOnlyList<ViewportItem> items, OrbitCamera camera, double width, double height, ScreenRect rect, MarqueeMode mode)
    {
        var result = new List<Guid>();
        if (width <= 0 || height <= 0) return result;

        foreach (var group in items.Where(i => !i.Mesh.IsEmpty && !i.Dimmed).GroupBy(i => i.NodeId))
        {
            var taken = mode == MarqueeMode.Window
                ? group.All(i => IsInside(i.Mesh, camera, width, height, rect))
                : group.Any(i => Touches(i.Mesh, camera, width, height, rect));
            if (taken) result.Add(group.Key);
        }

        return result;
    }

    // ---- window ------------------------------------------------------------------------------------

    private static bool IsInside(MeshData mesh, OrbitCamera camera, double width, double height, ScreenRect rect)
    {
        if (mesh.TriangleCount > DetailTriangleLimit)
        {
            foreach (var corner in Corners(mesh.ComputeBounds()))
            {
                if (camera.Project(corner, width, height) is not { } p || !rect.Contains(p.X, p.Y)) return false;
            }

            return true;
        }

        foreach (var vertex in mesh.Vertices)
        {
            if (camera.Project(vertex, width, height) is not { } p || !rect.Contains(p.X, p.Y)) return false;
        }

        return true;
    }

    // ---- crossing ----------------------------------------------------------------------------------

    private static bool Touches(MeshData mesh, OrbitCamera camera, double width, double height, ScreenRect rect)
    {
        if (mesh.TriangleCount > DetailTriangleLimit) return BoxTouches(mesh.ComputeBounds(), camera, width, height, rect);

        var vertices = mesh.Vertices;
        var screen = new (double X, double Y)?[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            screen[i] = camera.Project(vertices[i], width, height);
            if (screen[i] is { } p && rect.Contains(p.X, p.Y)) return true;
        }

        var indices = mesh.Indices;
        for (var t = 0; t < indices.Length; t += 3)
        {
            // a triangle that reaches behind the camera is skipped: only its visible corners count (see above)
            if (screen[indices[t]] is not { } a || screen[indices[t + 1]] is not { } b || screen[indices[t + 2]] is not { } c) continue;
            if (TriangleTouches(a, b, c, rect)) return true;
        }

        return false;
    }

    private static bool BoxTouches(Bounds box, OrbitCamera camera, double width, double height, ScreenRect rect)
    {
        var left = double.MaxValue;
        var top = double.MaxValue;
        var right = double.MinValue;
        var bottom = double.MinValue;
        foreach (var corner in Corners(box))
        {
            if (camera.Project(corner, width, height) is not { } p) continue;
            left = Math.Min(left, p.X);
            right = Math.Max(right, p.X);
            top = Math.Min(top, p.Y);
            bottom = Math.Max(bottom, p.Y);
        }

        return left <= right && left <= rect.Right && right >= rect.Left && top <= rect.Bottom && bottom >= rect.Top;
    }

    private static IEnumerable<Vector3> Corners(Bounds box)
    {
        for (var i = 0; i < 8; i++)
            yield return new Vector3((i & 1) == 0 ? box.Min.X : box.Max.X, (i & 2) == 0 ? box.Min.Y : box.Max.Y, (i & 4) == 0 ? box.Min.Z : box.Max.Z);
    }

    /// <summary>True when the filled triangle and the rectangle share a point (no corner of the triangle is known to be inside).</summary>
    private static bool TriangleTouches((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, ScreenRect rect)
    {
        if (Math.Max(a.X, Math.Max(b.X, c.X)) < rect.Left || Math.Min(a.X, Math.Min(b.X, c.X)) > rect.Right) return false;
        if (Math.Max(a.Y, Math.Max(b.Y, c.Y)) < rect.Top || Math.Min(a.Y, Math.Min(b.Y, c.Y)) > rect.Bottom) return false;

        // an edge of the triangle crosses the rectangle, or the rectangle lies inside the triangle
        return SegmentTouches(a, b, rect) || SegmentTouches(b, c, rect) || SegmentTouches(c, a, rect)
               || PointInTriangle(rect.Left, rect.Top, a, b, c);
    }

    /// <summary>Liang-Barsky: does the segment share a point with the rectangle?</summary>
    private static bool SegmentTouches((double X, double Y) a, (double X, double Y) b, ScreenRect rect)
    {
        double t0 = 0;
        double t1 = 1;
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Clip(-dx, a.X - rect.Left, ref t0, ref t1)
               && Clip(dx, rect.Right - a.X, ref t0, ref t1)
               && Clip(-dy, a.Y - rect.Top, ref t0, ref t1)
               && Clip(dy, rect.Bottom - a.Y, ref t0, ref t1);
    }

    private static bool Clip(double p, double q, ref double t0, ref double t1)
    {
        if (p == 0) return q >= 0;
        var r = q / p;
        if (p < 0)
        {
            if (r > t1) return false;
            if (r > t0) t0 = r;
        }
        else
        {
            if (r < t0) return false;
            if (r < t1) t1 = r;
        }

        return true;
    }

    private static bool PointInTriangle(double x, double y, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
    {
        var d1 = (x - b.X) * (a.Y - b.Y) - (a.X - b.X) * (y - b.Y);
        var d2 = (x - c.X) * (b.Y - c.Y) - (b.X - c.X) * (y - c.Y);
        var d3 = (x - a.X) * (c.Y - a.Y) - (c.X - a.X) * (y - a.Y);
        var negative = d1 < 0 || d2 < 0 || d3 < 0;
        var positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    }
}
