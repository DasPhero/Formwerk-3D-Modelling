using System.Numerics;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Tests.Features;

/// <summary>Where a shape grows from when a dimension changes: the anchor point of its bounds before and after (user wish).</summary>
public class GrowAnchoringTests
{
    private static readonly IReadOnlyDictionary<string, double> NoVariables = new Dictionary<string, double>();

    private static PrimitiveNode Box(double width = 50, double depth = 40, double height = 20, Vector3? anchor = null)
    {
        var box = new PrimitiveNode("box", "Box") { GrowAnchor = anchor ?? PrimitiveNode.DefaultGrowAnchor };
        box.Parameters["width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture);
        box.Parameters["depth"] = depth.ToString(System.Globalization.CultureInfo.InvariantCulture);
        box.Parameters["height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return box;
    }

    private static Dictionary<string, string> With(PrimitiveNode node, string name, string value) =>
        new(node.Parameters, StringComparer.OrdinalIgnoreCase) { [name] = value };

    [Fact]
    public void The_default_anchor_keeps_the_center_in_X_and_Y_and_the_bottom_in_Z_so_nothing_moves()
    {
        var box = Box();

        foreach (var (name, value) in new[] { ("width", "80"), ("depth", "10"), ("height", "35") })
        {
            var shift = GrowAnchoring.LocalShift(box, With(box, name, value), NoVariables, NoVariables);
            Assert.NotNull(shift);
            Assert.True(shift!.Value.Length() < 1e-4f, $"{name}: {shift}");
        }
    }

    [Theory]
    [InlineData(-1, 5)] // the left side stays: its center moves 5 to the right, so the position moves 5 to the right
    [InlineData(0, 0)]
    [InlineData(1, -5)]
    public void A_wider_box_keeps_its_left_center_or_right_side_where_it_was(int anchorX, float expected)
    {
        var box = Box(width: 50, anchor: new Vector3(anchorX, 0, -1));

        var shift = GrowAnchoring.LocalShift(box, With(box, "width", "60"), NoVariables, NoVariables)!.Value;

        Assert.Equal(expected, shift.X, 3);
        Assert.Equal(0, shift.Y, 3);
        Assert.Equal(0, shift.Z, 3);
    }

    [Theory]
    [InlineData(-1, 0)] // the bottom stays on the plate
    [InlineData(0, -5)] // the center stays: the position goes down by half of the growth
    [InlineData(1, -10)] // the top stays
    public void A_taller_box_keeps_its_bottom_center_or_top_where_it_was(int anchorZ, float expected)
    {
        var box = Box(height: 20, anchor: new Vector3(0, 0, anchorZ));

        var shift = GrowAnchoring.LocalShift(box, With(box, "height", "30"), NoVariables, NoVariables)!.Value;

        Assert.Equal(expected, shift.Z, 3);
    }

    [Fact]
    public void A_variable_that_changes_is_taken_into_account()
    {
        var box = Box(anchor: new Vector3(-1, 0, -1));
        box.Parameters["width"] = "Breite";
        var before = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Breite"] = 50 };
        var after = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Breite"] = 70 };

        var shift = GrowAnchoring.LocalShift(box, box.Parameters, before, after)!.Value;

        Assert.Equal(10, shift.X, 3);
    }

    [Fact]
    public void A_value_that_does_not_evaluate_gives_no_shift()
    {
        var box = Box(anchor: new Vector3(-1, 0, -1));

        Assert.Null(GrowAnchoring.LocalShift(box, With(box, "width", "unknown*2"), NoVariables, NoVariables));
        Assert.Null(GrowAnchoring.LocalShift(new PrimitiveNode("no-such-shape", "X"), new Dictionary<string, string>(), NoVariables, NoVariables));
    }

    [Fact]
    public void The_position_moves_through_the_rotation_and_scale_of_the_object()
    {
        var node = Box();
        node.Transform = new Transform(new Vector3(100, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), new Vector3(2, 2, 2));

        var moved = GrowAnchoring.Moved(node, new Vector3(5, 0, 0)); // 5 mm along the local X: scaled by 2, then turned to world Y

        Assert.Equal(100, moved.Position.X, 3);
        Assert.Equal(10, moved.Position.Y, 3);
        Assert.Equal(node.Transform.Rotation, moved.Rotation);
        Assert.Equal(node.Transform.Scale, moved.Scale);
    }

    [Fact]
    public void The_anchor_point_of_bounds_is_a_side_or_the_center_per_axis()
    {
        var bounds = new Bounds(new Vector3(-10, 0, 5), new Vector3(30, 20, 15));

        Assert.Equal(new Vector3(-10, 10, 15), GrowAnchoring.AnchorPoint(bounds, new Vector3(-1, 0, 1)));
        Assert.Equal(new Vector3(30, 0, 10), GrowAnchoring.AnchorPoint(bounds, new Vector3(1, -1, 0)));
    }
}
