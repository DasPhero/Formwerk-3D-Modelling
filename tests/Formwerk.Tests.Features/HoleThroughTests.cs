using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>A hole that goes "through all" reaches the far side of the body whatever its size (F-HOL-02).</summary>
public class HoleThroughTests
{
    private const double Diameter = 4;

    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator = new(new ManifoldKernel());

    private PrimitiveNode Box(string height) => new("box", "Box")
    {
        Parameters = { ["width"] = "20", ["depth"] = "20", ["height"] = height },
    };

    /// <summary>A hole at the bottom of the box (Z = 0) that points down into the box, like one drilled from below: the axis is turned about X by 180 degrees.</summary>
    private PrimitiveNode HoleFromBelow(bool through, string depth = "5")
    {
        var hole = new PrimitiveNode("hole", "Hole")
        {
            Role = NodeRole.Hole,
            Transform = new Transform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), Vector3.One),
            Parameters = { ["diameter"] = "4", ["depth"] = depth, ["through"] = through ? "1" : "0", ["segments"] = "64" },
        };
        return hole;
    }

    private Node Container<T>(T container, params Node[] children) where T : Node
    {
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, container));
        foreach (var child in children) _history.Execute(new AddNodeCommand(_doc, container, child));
        return container;
    }

    private double SolidVolume() =>
        _evaluator.Evaluate(_doc).Parts.Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());

    /// <summary>Volume of a 64-gon prism like the hole cutter, for the given length.</summary>
    private static double Bore(double length) => 64 / 2.0 * (Diameter / 2) * (Diameter / 2) * Math.Sin(2 * Math.PI / 64) * length;

    [Fact]
    public void A_hole_that_is_not_through_cuts_as_deep_as_its_depth()
    {
        Container(new GroupNode("G"), Box("20"), HoleFromBelow(through: false));

        Assert.Equal(8000 - Bore(5), SolidVolume(), 1);
    }

    [Fact]
    public void A_through_hole_goes_all_the_way_through_the_body_even_with_a_small_stored_depth()
    {
        Container(new GroupNode("G"), Box("20"), HoleFromBelow(through: true));

        Assert.Equal(8000 - Bore(20), SolidVolume(), 1);
    }

    [Fact]
    public void A_through_hole_keeps_going_through_when_the_body_grows()
    {
        var box = Box("20");
        Container(new GroupNode("G"), box, HoleFromBelow(through: true));
        Assert.Equal(8000 - Bore(20), SolidVolume(), 1);

        _history.Execute(new SetParameterCommand(_doc, box, "height", "40"));

        Assert.Equal(16000 - Bore(40), SolidVolume(), 1);
    }

    [Fact]
    public void A_hole_with_a_depth_does_not_follow_the_body()
    {
        var box = Box("20");
        Container(new GroupNode("G"), box, HoleFromBelow(through: false, depth: "21"));

        _history.Execute(new SetParameterCommand(_doc, box, "height", "40"));

        Assert.Equal(16000 - Bore(21), SolidVolume(), 1);
    }

    [Fact]
    public void A_through_hole_in_a_merge_goes_through_too()
    {
        Container(new MergeNode("M"), Box("20"), HoleFromBelow(through: true));

        Assert.Equal(8000 - Bore(20), SolidVolume(), 1);
    }

    [Fact]
    public void A_through_hole_cuts_every_solid_of_the_group_in_its_way()
    {
        var upper = Box("10");
        upper.Transform = Transform.FromPosition(new Vector3(0, 0, 25)); // a second body above the first, with a gap
        Container(new GroupNode("G"), Box("20"), upper, HoleFromBelow(through: true));

        Assert.Equal(8000 + 4000 - Bore(20) - Bore(10), SolidVolume(), 1);
    }

    [Fact]
    public void The_ghost_of_a_through_hole_shows_how_far_it_reaches()
    {
        Container(new GroupNode("G"), Box("20"), HoleFromBelow(through: true));

        var ghost = _evaluator.Evaluate(_doc).Parts.Single(p => p.Kind == PartKind.Ghost);

        Assert.True(ghost.Mesh.ComputeBounds().Max.Z >= 20); // up through the whole body, the hole points up from below here
    }

    [Fact]
    public void A_hole_outside_a_group_is_only_a_ghost_with_its_stored_depth()
    {
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, Box("20")));
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, HoleFromBelow(through: true, depth: "5")));

        var ghost = _evaluator.Evaluate(_doc).Parts.Single(p => p.Kind == PartKind.Ghost);

        Assert.Equal(6, ghost.Mesh.ComputeBounds().Size.Z, 3); // 5 deep and 1 mm overshoot
    }

    [Fact]
    public void A_slanted_through_hole_reaches_the_far_corner()
    {
        var hole = HoleFromBelow(through: true, depth: "1");
        hole.Transform = new Transform(new Vector3(0, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 3 / 4), Vector3.One); // 135 degrees: the axis points down and to +X
        Container(new GroupNode("G"), Box("20"), hole);

        var ghost = _evaluator.Evaluate(_doc).Parts.Single(p => p.Kind == PartKind.Ghost);
        var axis = Vector3.Transform(Vector3.UnitZ, hole.Transform.Rotation);
        var far = new Vector3(-10, 10, 20); // the corner of the box that is farthest along the axis
        var reach = ghost.Mesh.Vertices.ToArray().Max(v => Vector3.Dot(hole.Transform.Position - v, axis));

        Assert.True(reach >= Vector3.Dot(hole.Transform.Position - far, axis), reach.ToString());
    }

    [Fact]
    public void The_cut_does_not_depend_on_the_group_being_scaled()
    {
        var group = (GroupNode)Container(new GroupNode("G"), Box("20"), HoleFromBelow(through: true));
        group.Transform = group.Transform with { Scale = new Vector3(2, 2, 2) };

        Assert.Equal(8 * (8000 - Bore(20)), SolidVolume(), 0);
    }

    [Fact]
    public void The_hole_generator_knows_the_through_flag_as_a_yes_or_no_value()
    {
        var through = ShapeLibrary.Hole.Parameters.Single(p => p.Name == "through");

        Assert.True(through.IsInteger);
        Assert.Equal(0, through.Default);
        Assert.Equal(1, through.Max);
        Assert.Throws<ArgumentException>(() => ShapeLibrary.Hole.Generate(new Dictionary<string, double> { ["through"] = 2 }));
    }
}
