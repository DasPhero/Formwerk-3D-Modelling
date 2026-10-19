using System.Numerics;

namespace Formwerk.Viewport;

public enum ViewCubeKind
{
    /// <summary>One of the six faces: looks straight at it.</summary>
    Face,

    /// <summary>Where two faces meet: looks at the edge from 45 degrees.</summary>
    Edge,

    /// <summary>Where three faces meet: an isometric view.</summary>
    Corner,
}

/// <summary>
/// One of the 26 fields of the ViewCube, named by the direction (components -1, 0 or 1; X right, Y back, Z up) from which it looks
/// at the model: (0, -1, 0) is the front face, (1, -1, 1) the corner front, right, top.
/// </summary>
public readonly record struct ViewCubeRegion(int X, int Y, int Z)
{
    public static IReadOnlyList<ViewCubeRegion> All { get; } = BuildAll();

    public Vector3 Direction => new(X, Y, Z);

    public ViewCubeKind Kind => ((X != 0 ? 1 : 0) + (Y != 0 ? 1 : 0) + (Z != 0 ? 1 : 0)) switch
    {
        1 => ViewCubeKind.Face,
        2 => ViewCubeKind.Edge,
        _ => ViewCubeKind.Corner,
    };

    private static List<ViewCubeRegion> BuildAll()
    {
        var all = new List<ViewCubeRegion>();
        for (var x = -1; x <= 1; x++)
        for (var y = -1; y <= 1; y++)
        for (var z = -1; z <= 1; z++)
        {
            if (x != 0 || y != 0 || z != 0) all.Add(new ViewCubeRegion(x, y, z));
        }

        return all;
    }
}

/// <summary>What a click on the ViewCube does and how its small camera looks (F-VIS-05, F-VIS-06).</summary>
public static class ViewCubeMath
{
    /// <summary>The cube (edge length 1) is seen from this far away with <see cref="FieldOfViewDegrees"/>: a little perspective, like a real cube.</summary>
    public const double CameraDistance = 3;

    public const double FieldOfViewDegrees = 30;

    /// <summary>Same limit as the camera: the top and bottom views stop just short of the pole so that the view never flips.</summary>
    private const double MaxPitch = 89.5;

    /// <summary>The camera state that looks from the side of <paramref name="region"/> at the same target from the same distance.</summary>
    public static CameraState StateFor(ViewCubeRegion region, CameraState current)
    {
        var length = Math.Sqrt(region.X * region.X + region.Y * region.Y + region.Z * region.Z); // in double: exactly 45 degrees for an edge
        var yaw = region.X == 0 && region.Y == 0 ? -90.0 : Math.Atan2(region.Y, region.X) * 180 / Math.PI; // straight above or below: as the keys 5 and 6
        var pitch = Math.Clamp(Math.Asin(region.Z / length) * 180 / Math.PI, -MaxPitch, MaxPitch);
        return current with { YawDegrees = yaw, PitchDegrees = pitch };
    }

    /// <summary>
    /// A double click is a click and then another one. The first click already started the glide, and the cube is turning when
    /// the second arrives, so only the first click of a series acts.
    /// </summary>
    public static bool IsActingClick(int clickCount) => clickCount <= 1;

    /// <summary>Where the small camera stands so that the cube at the origin is seen in the viewing direction of the main camera.</summary>
    public static Vector3 CameraPosition(Vector3 forward) => -forward * (float)CameraDistance;
}

/// <summary>One of the 54 fields (6 faces x 3 x 3): four corners, counter-clockwise seen from outside, and their texture coordinates.</summary>
public sealed record ViewCubeQuad(ViewCubeRegion Face, ViewCubeRegion Region, Vector3[] Corners, Vector2[] Uv);

public static class ViewCubeGeometry
{
    /// <summary>Half the edge length of the cube.</summary>
    public const float Half = 0.5f;

    /// <summary>Width of the strips along the edges of a face; the middle field is the rest.</summary>
    public const float Strip = 0.2f;

    public static IReadOnlyList<ViewCubeQuad> Quads { get; } = Build();

    /// <summary>The directions that are right and up on a face when it is seen from outside (the label is read in them).</summary>
    public static (Vector3 Right, Vector3 Up) FrameOf(ViewCubeRegion face) => (face.X, face.Y, face.Z) switch
    {
        (0, -1, 0) => (Vector3.UnitX, Vector3.UnitZ),   // front
        (0, 1, 0) => (-Vector3.UnitX, Vector3.UnitZ),   // back
        (1, 0, 0) => (Vector3.UnitY, Vector3.UnitZ),    // right
        (-1, 0, 0) => (-Vector3.UnitY, Vector3.UnitZ),  // left
        (0, 0, 1) => (Vector3.UnitX, Vector3.UnitY),    // top, front edge at the bottom
        (0, 0, -1) => (Vector3.UnitX, -Vector3.UnitY),  // bottom
        _ => throw new ArgumentException("Not a face.", nameof(face)),
    };

    private static (float From, float To) Range(int index) => index switch
    {
        -1 => (-Half, -Half + Strip),
        0 => (-Half + Strip, Half - Strip),
        _ => (Half - Strip, Half),
    };

    private static List<ViewCubeQuad> Build()
    {
        var quads = new List<ViewCubeQuad>();
        foreach (var face in ViewCubeRegion.All.Where(r => r.Kind == ViewCubeKind.Face))
        {
            var (right, up) = FrameOf(face);
            for (var j = -1; j <= 1; j++)
            for (var i = -1; i <= 1; i++)
            {
                var (u0, u1) = Range(i);
                var (v0, v1) = Range(j);
                Vector3 At(float u, float v) => face.Direction * Half + right * u + up * v;
                Vector2 Tex(float u, float v) => new(u + Half, 1 - (v + Half));

                var direction = face.Direction + right * i + up * j;
                var region = new ViewCubeRegion((int)MathF.Round(direction.X), (int)MathF.Round(direction.Y), (int)MathF.Round(direction.Z));
                quads.Add(new ViewCubeQuad(
                    face, region,
                    [At(u0, v0), At(u1, v0), At(u1, v1), At(u0, v1)],
                    [Tex(u0, v0), Tex(u1, v0), Tex(u1, v1), Tex(u0, v1)]));
            }
        }

        return quads;
    }
}
