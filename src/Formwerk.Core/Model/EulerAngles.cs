using System.Numerics;

namespace Formwerk.Core.Model;

/// <summary>
/// Rotation as three angles in degrees for the properties panel. Order: first around X, then Y, then Z
/// (all around the fixed world axes, Z up). Gimbal lock at Y = ±90° is resolved by setting Z to 0.
/// </summary>
public readonly record struct EulerAngles(double X, double Y, double Z)
{
    public Quaternion ToQuaternion()
    {
        var qx = Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)ToRadians(X));
        var qy = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)ToRadians(Y));
        var qz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)ToRadians(Z));
        return Quaternion.Normalize(qz * qy * qx);
    }

    public static EulerAngles FromQuaternion(Quaternion rotation)
    {
        // Columns of the rotation matrix are the rotated basis vectors.
        var c0 = Vector3.Transform(Vector3.UnitX, rotation);
        var c1 = Vector3.Transform(Vector3.UnitY, rotation);
        var c2 = Vector3.Transform(Vector3.UnitZ, rotation);

        double r20 = c0.Z, r21 = c1.Z, r22 = c2.Z, r10 = c0.Y, r00 = c0.X, r11 = c1.Y, r12 = c2.Y;
        double x, y, z;
        if (Math.Abs(r20) < 0.999999)
        {
            y = Math.Asin(-r20);
            x = Math.Atan2(r21, r22);
            z = Math.Atan2(r10, r00);
        }
        else
        {
            y = r20 < 0 ? Math.PI / 2 : -Math.PI / 2;
            x = Math.Atan2(-r12, r11);
            z = 0;
        }

        return new EulerAngles(Clean(ToDegrees(x)), Clean(ToDegrees(y)), Clean(ToDegrees(z)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private static double ToDegrees(double radians) => radians * 180 / Math.PI;

    /// <summary>Removes floating-point noise such as -0 or 89.99999999.</summary>
    private static double Clean(double degrees)
    {
        var rounded = Math.Round(degrees, 4);
        return rounded == 0 ? 0 : rounded;
    }
}
