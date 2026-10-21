using System.Numerics;

namespace Formwerk.Geometry;

/// <summary>Why a click did not give an edge that can be chamfered or rounded (F-EDG-02).</summary>
public enum EdgeProblem
{
    None,

    /// <summary>The click is not on a flat face of the mesh.</summary>
    NoFace,

    /// <summary>The face has no edge next to a closed surface.</summary>
    NoEdge,

    /// <summary>An inner edge: the material is on the far side of the corner.</summary>
    Concave,

    /// <summary>The two faces meet at almost a straight angle (more than 170 degrees): nothing to cut.</summary>
    AlmostFlat,

    /// <summary>The two faces meet at less than 20 degrees: a cutter would be unbounded.</summary>
    TooSharp,

    /// <summary>A round edge next to a wall that is not at a right angle to the face (a cone, a sphere).</summary>
    NotRightAngle,

    /// <summary>The edge is a curve that is not a circle, or the faces next to it are not flat.</summary>
    Curved,
}

/// <summary>A straight edge between two flat faces: both ends, the outward normals of the clicked face (A) and of the one behind the edge (B), and the angle between the faces inside the material in degrees.</summary>
public sealed record StraightEdge(Vector3 Start, Vector3 End, Vector3 NormalA, Vector3 NormalB, float InnerAngle);

/// <summary>
/// The round edge of a flat face next to a cylinder wall at a right angle: the center of the circle (in the plane of the face), the
/// outward normal of the face (the axis), the radius, and whether the material is outside the circle (the rim of a hole). The rim has
/// <paramref name="Segments"/> corners; <paramref name="Reference"/> is the unit vector from the center to one of them (in the plane of the face).
/// </summary>
public sealed record RimEdge(Vector3 Center, Vector3 Axis, float Radius, bool IsHole, int Segments, Vector3 Reference);

/// <summary>The outline of the edge that a click would take (a line, or the whole circle of a rim), to be shown before the click, also when the edge cannot be cut.</summary>
public sealed record EdgePreview(IReadOnlyList<Vector3> Points, bool Closed);

/// <summary>What <see cref="EdgeFinder"/> found: a straight edge, a round rim, or the reason why there is none; <paramref name="Preview"/> is null only when there is no face.</summary>
public sealed record EdgeFindResult(StraightEdge? Straight, RimEdge? Rim, EdgeProblem Problem, EdgePreview? Preview = null)
{
    public static EdgeFindResult Fail(EdgeProblem problem, EdgePreview? preview = null) => new(null, null, problem, preview);
}

/// <summary>
/// Finds the edge that a click on a face means (F-EDG-02): the boundary edge of the clicked flat face that is nearest to the click.
/// Straight pieces in a row with the same neighbouring plane are one edge; the closed boundary of a face that is a circle next to a
/// cylinder wall is a rim. Inner edges, almost flat edges and curves are refused with a reason.
/// </summary>
public static class EdgeFinder
{
    private const float SameDirection = 0.9995f; // about 1.8 degrees, like the faces of FaceFinder
    private const float SamePlane = 0.02f;
    private const float SeedPlane = 0.05f;
    private const float Collinear = 0.9999f;
    private const int MinRimSegments = 16;
    private const float MinInnerAngle = 20;
    private const float MaxInnerAngle = 170;
    private const float SmoothTurn = 25; // degrees: a boundary that turns less than this at a vertex is a curve, not a corner

    private readonly record struct Boundary(int A, int B, int TriIn, int TriOut);

    public static EdgeFindResult Find(MeshData mesh, Vector3 point, Vector3 normal)
    {
        if (mesh.IsEmpty || normal.LengthSquared() < 1e-12f) return EdgeFindResult.Fail(EdgeProblem.NoFace);
        normal = Vector3.Normalize(normal);
        var vertices = mesh.Vertices.ToArray();
        var indices = mesh.Indices.ToArray();
        var count = mesh.TriangleCount;

        // vertices at the same position are the same vertex
        var keys = new Dictionary<(int, int, int), int>();
        var positions = new List<Vector3>();
        int Id(Vector3 v)
        {
            var key = ((int)MathF.Round(v.X * 1000), (int)MathF.Round(v.Y * 1000), (int)MathF.Round(v.Z * 1000));
            if (!keys.TryGetValue(key, out var id))
            {
                id = positions.Count;
                keys[key] = id;
                positions.Add(v);
            }

            return id;
        }

        var corners = new int[count][];
        var normals = new Vector3[count];
        var byEdge = new Dictionary<long, List<int>>();
        static long Key(int a, int b) => (long)Math.Min(a, b) << 32 | (uint)Math.Max(a, b);
        for (var t = 0; t < count; t++)
        {
            var c = new[] { Id(vertices[indices[3 * t]]), Id(vertices[indices[3 * t + 1]]), Id(vertices[indices[3 * t + 2]]) };
            corners[t] = c;
            if (c[0] == c[1] || c[1] == c[2] || c[0] == c[2]) continue; // a triangle without area
            normals[t] = TriangleNormal(vertices[indices[3 * t]], vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]]);
            for (var k = 0; k < 3; k++)
            {
                var key = Key(c[k], c[(k + 1) % 3]);
                if (!byEdge.TryGetValue(key, out var list)) byEdge[key] = list = [];
                list.Add(t);
            }
        }

        // the clicked triangle
        var seed = -1;
        var best = float.MaxValue;
        for (var t = 0; t < count; t++)
        {
            if (normals[t] == Vector3.Zero || Vector3.Dot(normals[t], normal) < SameDirection) continue;
            var a = vertices[indices[3 * t]];
            var distance = MathF.Abs(Vector3.Dot(point - a, normals[t]));
            if (distance > SeedPlane || distance >= best) continue;
            if (!FaceFinder.Inside(point, a, vertices[indices[3 * t + 1]], vertices[indices[3 * t + 2]], 1e-3f)) continue;
            seed = t;
            best = distance;
        }

        if (seed < 0) return EdgeFindResult.Fail(EdgeProblem.NoFace);

        // the flat face: triangles of the same plane and direction that hang together with the clicked one
        var faceNormal = normals[seed];
        var origin = vertices[indices[3 * seed]];
        bool InFace(int t)
        {
            if (normals[t] == Vector3.Zero || Vector3.Dot(normals[t], faceNormal) < SameDirection) return false;
            for (var k = 0; k < 3; k++)
            {
                if (MathF.Abs(Vector3.Dot(vertices[indices[3 * t + k]] - origin, faceNormal)) > SamePlane) return false;
            }

            return true;
        }

        var region = new HashSet<int> { seed };
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        while (queue.Count > 0)
        {
            var c = corners[queue.Dequeue()];
            for (var k = 0; k < 3; k++)
            {
                if (!byEdge.TryGetValue(Key(c[k], c[(k + 1) % 3]), out var neighbours)) continue;
                foreach (var neighbour in neighbours)
                {
                    if (!region.Contains(neighbour) && InFace(neighbour))
                    {
                        region.Add(neighbour);
                        queue.Enqueue(neighbour);
                    }
                }
            }
        }

        // the boundary of the face, each edge in the direction of its triangle (the face is on the left, seen from outside)
        var boundary = new List<Boundary>();
        var starting = new Dictionary<int, List<int>>();
        var ending = new Dictionary<int, List<int>>();
        foreach (var t in region)
        {
            var c = corners[t];
            for (var k = 0; k < 3; k++)
            {
                int a = c[k], b = c[(k + 1) % 3];
                var outside = -1;
                var interior = false;
                foreach (var u in byEdge[Key(a, b)])
                {
                    if (u == t) continue;
                    if (region.Contains(u)) interior = true;
                    else outside = u;
                }

                if (interior || outside < 0) continue;
                var index = boundary.Count;
                boundary.Add(new Boundary(a, b, t, outside));
                if (!starting.TryGetValue(a, out var s)) starting[a] = s = [];
                s.Add(index);
                if (!ending.TryGetValue(b, out var e)) ending[b] = e = [];
                e.Add(index);
            }
        }

        if (boundary.Count == 0) return EdgeFindResult.Fail(EdgeProblem.NoEdge);

        // the edge nearest to the click
        var first = 0;
        var nearest = float.MaxValue;
        for (var i = 0; i < boundary.Count; i++)
        {
            var distance = DistanceToSegment(point, positions[boundary[i].A], positions[boundary[i].B]);
            if (distance >= nearest) continue;
            nearest = distance;
            first = i;
        }

        Vector3 Direction(int i) => Vector3.Normalize(positions[boundary[i].B] - positions[boundary[i].A]);

        // is the neighbour behind the face (convex), in front of it (an inner edge) or in its plane?
        float Behind(int i)
        {
            var e = boundary[i];
            var other = corners[e.TriOut].First(v => v != e.A && v != e.B);
            return -Vector3.Dot(positions[other] - positions[e.A], faceNormal);
        }

        // 1. a closed boundary with many corners that is a circle: the rim of a cylinder
        var loop = Walk(first, boundary, starting);
        if (loop is not null && loop.Count >= MinRimSegments)
        {
            var circle = new EdgePreview([.. loop.Select(i => positions[boundary[i].A])], true);
            var rim = TryRim(loop, boundary, positions, normals, faceNormal, Behind, out var rimProblem);
            if (rim is not null) return new EdgeFindResult(null, rim, EdgeProblem.None, circle);
            if (rimProblem != EdgeProblem.None) return EdgeFindResult.Fail(rimProblem, circle);
        }

        // 2. a straight edge between two flat faces; pieces in a row, in line, with the same plane behind them, are one edge
        var neighbourNormal = normals[boundary[first].TriOut];
        var direction = Direction(first);
        bool Continues(int candidate) =>
            Vector3.Dot(Direction(candidate), direction) >= Collinear && Vector3.Dot(normals[boundary[candidate].TriOut], neighbourNormal) >= SameDirection;

        var last = first;
        for (var guard = 0; guard < boundary.Count; guard++)
        {
            var next = starting.GetValueOrDefault(boundary[last].B)?.Where(c => c != first && Continues(c)).Cast<int?>().FirstOrDefault();
            if (next is null) break;
            last = next.Value;
        }

        var firstPiece = first;
        for (var guard = 0; guard < boundary.Count; guard++)
        {
            var previous = ending.GetValueOrDefault(boundary[firstPiece].A)?.Where(c => c != last && Continues(c)).Cast<int?>().FirstOrDefault();
            if (previous is null) break;
            firstPiece = previous.Value;
        }

        var line = new EdgePreview([positions[boundary[firstPiece].A], positions[boundary[last].B]], false);

        var behind = Behind(first);
        if (behind < -1e-3f) return EdgeFindResult.Fail(EdgeProblem.Concave, line);
        if (behind <= 1e-3f) return EdgeFindResult.Fail(EdgeProblem.NoEdge, line);

        var inner = 180 - MathF.Acos(Math.Clamp(Vector3.Dot(faceNormal, neighbourNormal), -1f, 1f)) * 180 / MathF.PI;
        if (inner > MaxInnerAngle) return EdgeFindResult.Fail(EdgeProblem.AlmostFlat, line);
        if (inner < MinInnerAngle) return EdgeFindResult.Fail(EdgeProblem.TooSharp, line);

        // a boundary that bends only a little where the pieces end is a curve (an ellipse, a sphere), not a corner
        if (Bends(boundary[last].B, last, starting, boundary, Direction) || Bends(boundary[firstPiece].A, firstPiece, ending, boundary, Direction))
            return EdgeFindResult.Fail(EdgeProblem.Curved, line);

        return new EdgeFindResult(new StraightEdge(positions[boundary[firstPiece].A], positions[boundary[last].B], faceNormal, neighbourNormal, inner), null, EdgeProblem.None, line);
    }
    /// <summary>True when the boundary continues at <paramref name="vertex"/> with a turn of less than <see cref="SmoothTurn"/> degrees (a smooth curve).</summary>
    private static bool Bends(int vertex, int edge, Dictionary<int, List<int>> index, List<Boundary> boundary, Func<int, Vector3> direction)
    {
        if (!index.TryGetValue(vertex, out var others)) return false;
        foreach (var other in others)
        {
            if (other == edge) continue;
            var turn = MathF.Acos(Math.Clamp(Vector3.Dot(direction(edge), direction(other)), -1f, 1f)) * 180 / MathF.PI;
            if (turn < SmoothTurn && turn > 0.5f) return true;
        }

        return false;
    }

    /// <summary>The edges that follow each other from <paramref name="first"/> until the boundary closes; null when it does not close.</summary>
    private static List<int>? Walk(int first, List<Boundary> boundary, Dictionary<int, List<int>> starting)
    {
        var loop = new List<int> { first };
        var current = first;
        for (var guard = 0; guard <= boundary.Count; guard++)
        {
            if (!starting.TryGetValue(boundary[current].B, out var next)) return null;
            var following = next.FirstOrDefault(n => n != current, -1);
            if (following < 0) return null;
            if (following == first) return loop;
            loop.Add(following);
            current = following;
        }

        return null;
    }

    /// <summary>
    /// A closed boundary that is a circle around a point, with a wall at a right angle that is a cylinder (its normals point away from
    /// the axis or towards it) is a rim. <paramref name="problem"/> says why a circle is none (a slanted wall, an inner edge).
    /// </summary>
    private static RimEdge? TryRim(
        List<int> loop, List<Boundary> boundary, List<Vector3> positions, Vector3[] normals, Vector3 faceNormal, Func<int, float> behind, out EdgeProblem problem)
    {
        problem = EdgeProblem.None;
        var points = loop.Select(i => positions[boundary[i].A]).ToList();
        var center = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Count;
        var radii = points.Select(p => Vector3.Distance(p, center)).ToList();
        var radius = radii.Average();
        if (radius < 1e-3f || radii.Any(r => MathF.Abs(r - radius) > 0.01f * radius + 1e-3f)) return null; // not a circle: perhaps a curve of another kind

        // the wall: at a right angle to the face and a cylinder around the axis
        var sum = 0f;
        foreach (var i in loop)
        {
            var wall = normals[boundary[i].TriOut];
            if (MathF.Abs(Vector3.Dot(wall, faceNormal)) > 0.02f)
            {
                problem = EdgeProblem.NotRightAngle;
                return null;
            }

            var middle = (positions[boundary[i].A] + positions[boundary[i].B]) / 2 - center;
            middle -= faceNormal * Vector3.Dot(middle, faceNormal);
            if (middle.LengthSquared() < 1e-12f) return null;
            var radial = Vector3.Dot(wall, Vector3.Normalize(middle));
            if (MathF.Abs(radial) < 0.9f)
            {
                problem = EdgeProblem.Curved;
                return null;
            }

            sum += radial;
        }

        // the wall must lie behind the face all around, else it is an inner edge (a boss standing on the face)
        if (loop.Any(i => behind(i) < -1e-3f))
        {
            problem = EdgeProblem.Concave;
            return null;
        }

        var reference = points[0] - center;
        reference -= faceNormal * Vector3.Dot(reference, faceNormal);
        return new RimEdge(center, faceNormal, radius, sum < 0, loop.Count, Vector3.Normalize(reference));
    }

    private static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var length = cross.Length();
        return length < 1e-12f ? Vector3.Zero : cross / length;
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        var length = ab.LengthSquared();
        var t = length < 1e-12f ? 0 : Math.Clamp(Vector3.Dot(p - a, ab) / length, 0f, 1f);
        return Vector3.Distance(p, a + ab * t);
    }
}
