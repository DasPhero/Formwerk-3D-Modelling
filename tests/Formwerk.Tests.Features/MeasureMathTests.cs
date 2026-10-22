using System.Numerics;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>The arithmetic of the measuring tool (E-07).</summary>
public class MeasureMathTests
{
    // ---- points ---------------------------------------------------------------------------------------

    [Fact]
    public void The_distance_of_two_points_comes_with_its_parts_along_the_axes()
    {
        var result = MeasureMath.Distance(new Vector3(1, 2, 3), new Vector3(4, 6, 3));

        Assert.Equal(5, result.Length, 1e-5);
        Assert.Equal(new Vector3(3, 4, 0), result.Delta);
    }

    [Fact]
    public void The_parts_keep_their_sign()
    {
        var result = MeasureMath.Distance(new Vector3(10, 0, 5), new Vector3(0, 2, 0));

        Assert.Equal(new Vector3(-10, 2, -5), result.Delta);
        Assert.Equal(Math.Sqrt(129), result.Length, 1e-4);
    }

    [Fact]
    public void The_same_point_has_the_distance_zero()
    {
        Assert.Equal(0, MeasureMath.Distance(new Vector3(3, 3, 3), new Vector3(3, 3, 3)).Length);
    }

    // ---- faces ----------------------------------------------------------------------------------------

    [Fact]
    public void Two_opposite_parallel_faces_have_the_distance_of_their_planes()
    {
        // the bottom and the top of a box 20 high, clicked at different places
        var relation = MeasureMath.Faces(new Vector3(3, 4, 0), -Vector3.UnitZ, new Vector3(-7, 2, 20), Vector3.UnitZ);

        Assert.True(relation.Parallel);
        Assert.Equal(20, relation.Distance, 1e-5);
    }

    [Fact]
    public void Faces_that_look_the_same_way_are_parallel_too()
    {
        var relation = MeasureMath.Faces(new Vector3(0, 0, 5), Vector3.UnitZ, new Vector3(9, 9, 8.25f), Vector3.UnitZ);

        Assert.True(relation.Parallel);
        Assert.Equal(3.25, relation.Distance, 1e-5);
    }

    [Fact]
    public void The_distance_is_measured_across_the_faces_not_along_the_line_between_the_clicks()
    {
        var relation = MeasureMath.Faces(new Vector3(0, 0, 0), -Vector3.UnitX, new Vector3(12.5f, 100, -50), Vector3.UnitX);

        Assert.Equal(12.5, relation.Distance, 1e-5);
    }

    [Fact]
    public void Faces_at_a_right_angle_are_not_parallel_and_meet_at_90_degrees()
    {
        var relation = MeasureMath.Faces(Vector3.Zero, Vector3.UnitZ, new Vector3(5, 0, -3), Vector3.UnitX);

        Assert.False(relation.Parallel);
        Assert.Equal(90, relation.InsideAngle, 1e-3);
    }

    [Fact]
    public void The_angle_is_the_one_inside_the_material_where_the_faces_meet()
    {
        // neighbouring walls of a hexagon: their normals are 60 degrees apart, the angle inside is 120 degrees
        var n1 = new Vector3(1, 0, 0);
        var n2 = new Vector3(MathF.Cos(MathF.PI / 3), MathF.Sin(MathF.PI / 3), 0);

        var relation = MeasureMath.Faces(Vector3.Zero, n1, Vector3.One, n2);

        Assert.False(relation.Parallel);
        Assert.Equal(120, relation.InsideAngle, 1e-3);
    }

    [Fact]
    public void Faces_that_are_almost_parallel_count_as_parallel()
    {
        var tilted = Vector3.Normalize(new Vector3(0.0001f, 0, 1));

        Assert.True(MeasureMath.Faces(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0, 10), tilted).Parallel);
        Assert.False(MeasureMath.Faces(Vector3.Zero, Vector3.UnitZ, new Vector3(0, 0, 10), Vector3.Normalize(new Vector3(0.05f, 0, 1))).Parallel);
    }

    [Fact]
    public void The_normals_need_not_be_unit_vectors()
    {
        var relation = MeasureMath.Faces(Vector3.Zero, new Vector3(0, 0, -7), new Vector3(0, 0, 20), new Vector3(0, 0, 3));

        Assert.Equal(20, relation.Distance, 1e-5);
    }

    [Fact]
    public void The_distance_of_the_faces_of_a_real_box_is_exact_to_a_hundredth()
    {
        var box = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 33.37, ["depth"] = 10, ["height"] = 10 });
        var left = FaceFinder.Find(box, new Vector3(-16.685f, 0, 5), -Vector3.UnitX)!;
        var right = FaceFinder.Find(box, new Vector3(16.685f, 0, 5), Vector3.UnitX)!;

        var relation = MeasureMath.Faces(new Vector3(-16.685f, 0, 5), left.Normal, new Vector3(16.685f, 0, 5), right.Normal);

        Assert.True(relation.Parallel);
        Assert.Equal(33.37, Math.Round(relation.Distance, 2), 1e-9);
    }

    // ---- angle of three points ------------------------------------------------------------------------

    [Fact]
    public void Three_points_at_a_right_angle()
    {
        Assert.Equal(90, MeasureMath.Angle(new Vector3(10, 0, 0), Vector3.Zero, new Vector3(0, 7, 0)), 1e-3);
    }

    [Fact]
    public void A_straight_line_has_180_degrees_and_a_fold_has_0()
    {
        Assert.Equal(180, MeasureMath.Angle(new Vector3(-5, 0, 0), Vector3.Zero, new Vector3(5, 0, 0)), 1e-3);
        Assert.Equal(0, MeasureMath.Angle(new Vector3(5, 0, 0), Vector3.Zero, new Vector3(9, 0, 0)), 1e-3);
    }

    [Fact]
    public void The_angle_is_the_same_in_space_and_whichever_leg_comes_first()
    {
        var a = new Vector3(1, 1, 1);
        var vertex = new Vector3(0, 0, 0);
        var c = new Vector3(1, -1, 0);

        Assert.Equal(MeasureMath.Angle(a, vertex, c), MeasureMath.Angle(c, vertex, a), 1e-4);
        Assert.Equal(Math.Acos(Vector3.Dot(Vector3.Normalize(a), Vector3.Normalize(c))) * 180 / Math.PI, MeasureMath.Angle(a, vertex, c), 1e-3);
    }

    [Fact]
    public void A_leg_of_length_zero_gives_no_angle()
    {
        Assert.Equal(0, MeasureMath.Angle(Vector3.Zero, Vector3.Zero, new Vector3(1, 0, 0)));
    }
}
