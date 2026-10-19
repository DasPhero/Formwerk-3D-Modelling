using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

public class EulerAnglesTests
{
    private static void AssertVector(Vector3 expected, Vector3 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }

    [Fact]
    public void Single_axis_rotations_are_right_handed()
    {
        AssertVector(Vector3.UnitY, Vector3.Transform(Vector3.UnitX, new EulerAngles(0, 0, 90).ToQuaternion()));
        AssertVector(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, new EulerAngles(90, 0, 0).ToQuaternion()));
        AssertVector(Vector3.UnitX, Vector3.Transform(Vector3.UnitZ, new EulerAngles(0, 90, 0).ToQuaternion()));
    }

    [Fact]
    public void X_is_applied_before_Z()
    {
        // Y axis -> (X 90°) -> Z axis -> (Z 90°) -> still Z axis.
        AssertVector(Vector3.UnitZ, Vector3.Transform(Vector3.UnitY, new EulerAngles(90, 0, 90).ToQuaternion()));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(30, 0, 0)]
    [InlineData(0, 45, 0)]
    [InlineData(0, 0, -120)]
    [InlineData(10, 20, 30)]
    [InlineData(-170, 80, 100)]
    [InlineData(45, -60, 170)]
    public void Roundtrip_keeps_the_angles(double x, double y, double z)
    {
        var back = EulerAngles.FromQuaternion(new EulerAngles(x, y, z).ToQuaternion());
        Assert.Equal(x, back.X, 2);
        Assert.Equal(y, back.Y, 2);
        Assert.Equal(z, back.Z, 2);
    }

    [Fact]
    public void Gimbal_lock_still_describes_the_same_rotation()
    {
        var q = new EulerAngles(20, 90, 40).ToQuaternion();
        var back = EulerAngles.FromQuaternion(q).ToQuaternion();
        AssertVector(Vector3.Transform(new Vector3(1, 2, 3), q), Vector3.Transform(new Vector3(1, 2, 3), back));
    }

    [Fact]
    public void Identity_has_clean_zero_angles()
    {
        Assert.Equal(new EulerAngles(0, 0, 0), EulerAngles.FromQuaternion(Quaternion.Identity));
    }

    [Fact]
    public void Visibility_and_boolean_operation_commands_are_undoable()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var boolean = new BooleanNode("B", BooleanOperation.Union);
        history.Execute(new AddNodeCommand(doc, doc.Root, boolean));

        history.Execute(new SetVisibleCommand(doc, boolean, false));
        history.Execute(new SetBooleanOperationCommand(doc, boolean, BooleanOperation.Difference));
        Assert.False(boolean.Visible);
        Assert.Equal(BooleanOperation.Difference, boolean.Operation);

        history.Undo();
        history.Undo();
        Assert.True(boolean.Visible);
        Assert.Equal(BooleanOperation.Union, boolean.Operation);
    }
}
