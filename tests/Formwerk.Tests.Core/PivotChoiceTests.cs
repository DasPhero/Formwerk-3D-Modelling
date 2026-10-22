using System.Numerics;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>The corners of a bounding box as rotation pivots (F-ROT-03).</summary>
public class PivotChoiceTests
{
    private static readonly Vector3 Min = new(-1, -2, -3);
    private static readonly Vector3 Max = new(4, 5, 6);

    [Theory]
    [InlineData(0, -1, -2, -3)]
    [InlineData(1, 4, -2, -3)]
    [InlineData(2, -1, 5, -3)]
    [InlineData(3, 4, 5, -3)]
    [InlineData(4, -1, -2, 6)]
    [InlineData(5, 4, -2, 6)]
    [InlineData(6, -1, 5, 6)]
    [InlineData(7, 4, 5, 6)]
    public void The_bits_of_the_corner_pick_min_or_max_of_each_axis(int corner, float x, float y, float z)
    {
        Assert.Equal(new Vector3(x, y, z), PivotChoice.Corner(Min, Max, corner));
    }

    [Fact]
    public void A_corner_outside_zero_to_seven_is_taken_modulo_eight()
    {
        Assert.Equal(PivotChoice.Corner(Min, Max, 1), PivotChoice.Corner(Min, Max, 9));
        Assert.Equal(PivotChoice.Corner(Min, Max, 7), PivotChoice.Corner(Min, Max, -1));
    }

    [Fact]
    public void The_eight_corners_are_all_different_for_a_box_with_a_size()
    {
        var corners = Enumerable.Range(0, 8).Select(i => PivotChoice.Corner(Min, Max, i)).Distinct().ToList();

        Assert.Equal(8, corners.Count);
    }

    [Fact]
    public void The_center_is_the_middle_of_the_box()
    {
        Assert.Equal(new Vector3(1.5f, 1.5f, 1.5f), PivotChoice.Center(Min, Max));
    }
}
