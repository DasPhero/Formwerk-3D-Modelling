using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>Points of a bounding box that can be the pivot of a rotation (F-ROT-03).</summary>
public static class PivotChoice
{
    /// <summary>The middle of the box.</summary>
    public static Vector3 Center(Vector3 min, Vector3 max) => (min + max) * 0.5f;

    /// <summary>
    /// A corner of the box: bit 0 of <paramref name="corner"/> picks X max (else X min), bit 1 Y max, bit 2 Z max. Numbers outside 0 to 7 are
    /// taken modulo 8.
    /// </summary>
    public static Vector3 Corner(Vector3 min, Vector3 max, int corner)
    {
        var c = ((corner % 8) + 8) % 8;
        return new Vector3((c & 1) != 0 ? max.X : min.X, (c & 2) != 0 ? max.Y : min.Y, (c & 4) != 0 ? max.Z : min.Z);
    }
}
