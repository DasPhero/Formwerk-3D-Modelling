using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>An axis-aligned box in world space (min and max corner, mm).</summary>
public readonly record struct SnapBox(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) * 0.5f;

    public SnapBox Translated(Vector3 delta) => new(Min + delta, Max + delta);

    public SnapBox Union(SnapBox other) => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    public static float Component(Vector3 v, int axis) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };
}

/// <summary>
/// A guide shown while an alignment is active: a rectangle in the plane perpendicular to <see cref="Axis"/> at
/// <see cref="Coordinate"/>, spanning <see cref="Min"/> to <see cref="Max"/> in the two other axes.
/// </summary>
public readonly record struct SnapGuide(int Axis, float Coordinate, Vector3 Min, Vector3 Max);

/// <param name="Delta">The movement after snapping.</param>
/// <param name="SnappedAxes">1 for every axis that snapped, 0 for the others.</param>
/// <param name="Marker">The snap point (world position) when a whole point of another object was hit, else null (F-SNP-01).</param>
public sealed record SnapResult(Vector3 Delta, Vector3 SnappedAxes, IReadOnlyList<SnapGuide> Guides, Vector3? Marker = null);

/// <summary>
/// Snaps a moving selection to other objects (F-SNP-02): per axis, the minimum, center and maximum of the moving box are
/// compared with those of every target box, so edges line up flush and centers line up exactly (the guide lines of
/// presentation programs). The closest match within the tolerance wins. The floor (lowest point to Z = 0) and the bed
/// center lines (center to X = 0 and Y = 0) count as targets, too.
/// Priorities (F-SNP-06): a whole point of one other object (corner, edge middle, face center or center, see <see cref="Lattice"/>)
/// beats alignments with edges or faces of different objects, which beat the grid (applied by the caller to the axes left over).
/// </summary>
public static class ObjectSnapper
{
    /// <summary>The 27 snap points of a box (F-SNP-01): 8 corners, 12 edge middles, 6 face centers and the center.</summary>
    public static IReadOnlyList<Vector3> Lattice(SnapBox box)
    {
        var points = new List<Vector3>(27);
        float[] xs = [box.Min.X, box.Center.X, box.Max.X];
        float[] ys = [box.Min.Y, box.Center.Y, box.Max.Y];
        float[] zs = [box.Min.Z, box.Center.Z, box.Max.Z];
        foreach (var x in xs)
        foreach (var y in ys)
        foreach (var z in zs) points.Add(new Vector3(x, y, z));
        return points;
    }

    /// <param name="own">Bounds of the moving selection at the start of the drag.</param>
    /// <param name="delta">The raw movement so far.</param>
    /// <param name="freeAxes">1 for each axis the handle moves along, 0 for the others (those are not snapped).</param>
    /// <param name="targets">Bounds of the other objects.</param>
    /// <param name="tolerance">Largest distance in mm that still snaps (screen pixels times world size of a pixel).</param>
    /// <param name="includeBed">Also snap to the floor and the bed center lines.</param>
    public static SnapResult Snap(SnapBox own, Vector3 delta, Vector3 freeAxes, IReadOnlyList<SnapBox> targets, float tolerance, bool includeBed = true)
    {
        var result = delta;
        var snapped = Vector3.Zero;
        var matches = new List<(int Axis, float Coordinate, SnapBox? Target)>();
        Vector3? marker = null;

        // 1. a whole point of one object: every axis (also those the handle cannot move) lines up within the tolerance
        if (TryPoint(own.Translated(delta), freeAxes, targets, tolerance) is { } point)
        {
            result = delta + point.Correction;
            for (var axis = 0; axis < 3; axis++)
            {
                if (SnapBox.Component(freeAxes, axis) == 0) continue;
                snapped = SetComponent(snapped, axis, 1);
                matches.Add((axis, SnapBox.Component(point.Marker, axis), point.Target));
            }

            marker = point.Marker;
        }

        // 2. otherwise alignments per axis (edges, faces, centers; the floor and the bed center lines)
        for (var axis = 0; axis < 3 && marker is null; axis++)
        {
            if (SnapBox.Component(freeAxes, axis) == 0) continue;

            var moved = own.Translated(delta);
            float[] ownCoordinates =
            [
                SnapBox.Component(moved.Min, axis),
                SnapBox.Component(moved.Center, axis),
                SnapBox.Component(moved.Max, axis),
            ];

            var bestDistance = float.MaxValue;
            var bestCorrection = 0f;
            var bestCoordinate = 0f;
            SnapBox? bestTarget = null;

            void Consider(float ownCoordinate, float targetCoordinate, SnapBox? target)
            {
                var correction = targetCoordinate - ownCoordinate;
                var distance = Math.Abs(correction);
                if (distance > tolerance || distance >= bestDistance) return;
                bestDistance = distance;
                bestCorrection = correction;
                bestCoordinate = targetCoordinate;
                bestTarget = target;
            }

            foreach (var target in targets)
            {
                float[] targetCoordinates =
                [
                    SnapBox.Component(target.Min, axis),
                    SnapBox.Component(target.Center, axis),
                    SnapBox.Component(target.Max, axis),
                ];
                foreach (var o in ownCoordinates)
                foreach (var t in targetCoordinates) Consider(o, t, target);
            }

            if (includeBed)
            {
                if (axis == 2) Consider(ownCoordinates[0], 0, null); // the lowest point goes onto the plate
                else Consider(ownCoordinates[1], 0, null);           // the center goes onto the bed center line
            }

            if (bestDistance == float.MaxValue) continue;

            result = axis switch
            {
                0 => result with { X = result.X + bestCorrection },
                1 => result with { Y = result.Y + bestCorrection },
                _ => result with { Z = result.Z + bestCorrection },
            };
            snapped = axis switch { 0 => snapped with { X = 1 }, 1 => snapped with { Y = 1 }, _ => snapped with { Z = 1 } };
            matches.Add((axis, bestCoordinate, bestTarget));
        }

        var guides = new List<SnapGuide>();
        if (matches.Count > 0)
        {
            var final = own.Translated(result);
            foreach (var (axis, coordinate, target) in matches)
            {
                var span = target is { } t ? final.Union(t) : final;
                var min = axis switch { 0 => span.Min with { X = coordinate }, 1 => span.Min with { Y = coordinate }, _ => span.Min with { Z = coordinate } };
                var max = axis switch { 0 => span.Max with { X = coordinate }, 1 => span.Max with { Y = coordinate }, _ => span.Max with { Z = coordinate } };
                guides.Add(new SnapGuide(axis, coordinate, min, max));
            }
        }

        return new SnapResult(result, snapped, guides, marker);
    }

    private static Vector3 SetComponent(Vector3 v, int axis, float value) => axis switch
    {
        0 => v with { X = value },
        1 => v with { Y = value },
        _ => v with { Z = value },
    };

    /// <summary>
    /// The best whole point: the target for which on every axis some coordinate of the moving box (min, center, max) comes within the
    /// tolerance of a coordinate of the target. Axes the handle cannot move must line up already. The score is the squared
    /// movement needed; the lowest wins.
    /// </summary>
    private static (Vector3 Correction, Vector3 Marker, SnapBox Target)? TryPoint(SnapBox moved, Vector3 freeAxes, IReadOnlyList<SnapBox> targets, float tolerance)
    {
        (Vector3 Correction, Vector3 Marker, SnapBox Target)? best = null;
        var bestScore = float.MaxValue;
        foreach (var target in targets)
        {
            var correction = Vector3.Zero;
            var marker = Vector3.Zero;
            var score = 0f;
            var matched = true;
            for (var axis = 0; axis < 3 && matched; axis++)
            {
                float[] own = [SnapBox.Component(moved.Min, axis), SnapBox.Component(moved.Center, axis), SnapBox.Component(moved.Max, axis)];
                float[] other = [SnapBox.Component(target.Min, axis), SnapBox.Component(target.Center, axis), SnapBox.Component(target.Max, axis)];
                var bestDistance = float.MaxValue;
                var bestCorrection = 0f;
                var bestCoordinate = 0f;
                foreach (var o in own)
                foreach (var t in other)
                {
                    var distance = Math.Abs(t - o);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    bestCorrection = t - o;
                    bestCoordinate = t;
                }

                if (bestDistance > tolerance)
                {
                    matched = false;
                    break;
                }

                marker = SetComponent(marker, axis, bestCoordinate);
                if (SnapBox.Component(freeAxes, axis) != 0)
                {
                    correction = SetComponent(correction, axis, bestCorrection);
                    score += bestCorrection * bestCorrection;
                }
            }

            if (!matched || score >= bestScore) continue;
            bestScore = score;
            best = (correction, marker, target);
        }

        return best;
    }
}
