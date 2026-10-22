using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>Placing something with its base on a face and its up axis along the face normal (F-LIB-04).</summary>
public class FacePlacementTests
{
    private static Vector3 UpAxisOf(Transform transform) => Vector3.Transform(Vector3.UnitZ, transform.Rotation);

    private static void AssertNear(Vector3 expected, Vector3 actual, float tolerance = 1e-4f)
    {
        Assert.True(Vector3.Distance(expected, actual) < tolerance, $"expected {expected}, got {actual}");
    }

    [Fact]
    public void An_upward_face_gives_an_upright_placement_at_the_point()
    {
        var transform = FacePlacement.OnFace(new Vector3(5, -3, 20), Vector3.UnitZ);

        Assert.Equal(new Vector3(5, -3, 20), transform.Position);
        AssertNear(Vector3.UnitZ, UpAxisOf(transform));
        Assert.Equal(Vector3.One, transform.Scale);
    }

    [Theory]
    [InlineData(1, 0, 0)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void The_up_axis_ends_up_along_the_normal_of_a_side_or_bottom_face(float x, float y, float z)
    {
        var normal = new Vector3(x, y, z);

        var transform = FacePlacement.OnFace(new Vector3(1, 2, 3), normal);

        AssertNear(normal, UpAxisOf(transform));
        Assert.Equal(new Vector3(1, 2, 3), transform.Position);
    }

    [Fact]
    public void A_slanted_face_gives_a_placement_perpendicular_to_it()
    {
        var normal = Vector3.Normalize(new Vector3(0, -1, 1));

        var transform = FacePlacement.OnFace(new Vector3(0, 0, 10), normal);

        AssertNear(normal, UpAxisOf(transform));
        // the base circle lies in the face: its points are perpendicular to the normal
        var along = Vector3.Transform(Vector3.UnitX, transform.Rotation);
        Assert.Equal(0, Vector3.Dot(along, normal), 4);
    }

    [Fact]
    public void A_normal_that_is_not_a_unit_vector_is_normalized_and_none_gives_upright()
    {
        AssertNear(Vector3.UnitX, UpAxisOf(FacePlacement.OnFace(Vector3.Zero, new Vector3(5, 0, 0))));
        AssertNear(Vector3.UnitZ, UpAxisOf(FacePlacement.OnFace(Vector3.Zero, Vector3.Zero)));
        AssertNear(Vector3.UnitZ, UpAxisOf(FacePlacement.OnFace(Vector3.Zero, new Vector3(float.NaN, 0, 1))));
    }

    [Fact]
    public void A_nearly_downward_normal_does_not_flip_unpredictably()
    {
        var normal = Vector3.Normalize(new Vector3(0.0001f, 0, -1));

        var transform = FacePlacement.OnFace(Vector3.Zero, normal);

        AssertNear(normal, UpAxisOf(transform), 1e-3f);
        Assert.True(float.IsFinite(transform.Rotation.X) && float.IsFinite(transform.Rotation.W));
    }
}
