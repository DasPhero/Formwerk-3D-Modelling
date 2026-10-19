using System.Numerics;
using System.Windows.Media.Media3D;
using Formwerk.Viewport;

namespace Formwerk.Tests.Viewport;

/// <summary>The matrix that moves a dragged object on screen must do the same as the matrix the document uses (ADR 0005).</summary>
public class PreviewMathTests
{
    private static void AssertSame(Vector3 expected, Point3D actual)
    {
        Assert.Equal(expected.X, actual.X, 3);
        Assert.Equal(expected.Y, actual.Y, 3);
        Assert.Equal(expected.Z, actual.Z, 3);
    }

    [Theory]
    [InlineData(10, 0, 0)]
    [InlineData(-3.5, 7, 100)]
    public void A_translation_moves_a_point_the_same_way(float x, float y, float z)
    {
        var matrix = Matrix4x4.CreateTranslation(x, y, z);
        var point = new Vector3(1, 2, 3);

        AssertSame(Vector3.Transform(point, matrix), PreviewMath.ToMatrix3D(matrix).Transform(new Point3D(1, 2, 3)));
    }

    [Fact]
    public void A_rotation_about_a_pivot_with_a_move_gives_the_same_point_in_both_worlds()
    {
        var pivot = new Vector3(5, -2, 10);
        var matrix = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateRotationZ(MathF.PI / 3) * Matrix4x4.CreateRotationX(0.4f)
                     * Matrix4x4.CreateTranslation(pivot) * Matrix4x4.CreateTranslation(3, 4, 5);

        foreach (var point in new[] { new Vector3(0, 0, 0), new Vector3(10, 20, 30), new Vector3(-7, 3, 1.5f) })
            AssertSame(Vector3.Transform(point, matrix), PreviewMath.ToMatrix3D(matrix).Transform(new Point3D(point.X, point.Y, point.Z)));
    }

    [Fact]
    public void The_identity_stays_the_identity_and_a_chain_of_matrices_composes_in_the_same_order()
    {
        Assert.True(PreviewMath.ToMatrix3D(Matrix4x4.Identity).IsIdentity);

        var first = Matrix4x4.CreateScale(2, 3, 4);
        var second = Matrix4x4.CreateTranslation(10, 0, 0);
        var wpf = PreviewMath.ToMatrix3D(first) * PreviewMath.ToMatrix3D(second);

        AssertSame(Vector3.Transform(new Vector3(1, 1, 1), first * second), wpf.Transform(new Point3D(1, 1, 1)));
    }
}
