using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>Mirroring (F-PAT-04): a linked mirror shows the source and its mirror image, a copy only the image.</summary>
public class MirrorTests
{
    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator = new(new ManifoldKernel());

    private static PrimitiveNode Box(float x = 0, float y = 0, float z = 0, string width = "10", string height = "10") =>
        new("box", "Box")
        {
            Transform = Transform.FromPosition(new Vector3(x, y, z)),
            Parameters = { ["width"] = width, ["depth"] = "10", ["height"] = height },
        };

    private MirrorNode MirrorOf(Node source, string axis = "0", bool copyOnly = false, Vector3? at = null, Node? parent = null)
    {
        var mirror = new MirrorNode("Mirror") { Parameters = { ["axis"] = axis, ["copyOnly"] = copyOnly ? "1" : "0" } };
        if (at is not null) mirror.Transform = Transform.FromPosition(at.Value);
        _history.Execute(new AddNodeCommand(_doc, parent ?? _doc.Root, mirror));
        _history.Execute(new AddNodeCommand(_doc, mirror, source));
        return mirror;
    }

    private List<ScenePart> Parts() => [.. _evaluator.Evaluate(_doc).Parts];

    private static List<Vector3> Centers(IEnumerable<ScenePart> parts) =>
        [.. parts.Select(p => p.Mesh.ComputeBounds().Center).OrderBy(c => c.X).ThenBy(c => c.Y).ThenBy(c => c.Z)];

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-3f;

    [Fact]
    public void A_linked_mirror_shows_the_source_and_its_image_on_the_other_side_of_the_plane()
    {
        MirrorOf(Box(x: 20));

        var centers = Centers(Parts().Where(p => p.Kind == PartKind.Solid));

        Assert.Equal(2, centers.Count);
        Assert.True(Near(centers[0], new Vector3(-20, 0, 5)), centers[0].ToString());
        Assert.True(Near(centers[1], new Vector3(20, 0, 5)), centers[1].ToString());
    }

    [Fact]
    public void A_copy_shows_only_the_image()
    {
        MirrorOf(Box(x: 20), copyOnly: true);

        var centers = Centers(Parts());

        Assert.Single(centers);
        Assert.True(Near(centers[0], new Vector3(-20, 0, 5)));
    }

    [Fact]
    public void The_plane_goes_through_the_origin_of_the_mirror_node()
    {
        MirrorOf(Box(x: 10), at: new Vector3(10, 0, 0)); // the box is at 20 in the world, the plane at 10

        var centers = Centers(Parts());

        Assert.True(Near(centers[0], new Vector3(0, 0, 5)), centers[0].ToString());
        Assert.True(Near(centers[1], new Vector3(20, 0, 5)));
    }

    [Theory]
    [InlineData("1", 0f, 20f, 0f)]
    [InlineData("2", 0f, 0f, 20f)]
    public void The_axis_chooses_the_normal_of_the_plane(string axis, float x, float y, float z)
    {
        MirrorOf(Box(x: x, y: y, z: z), axis, copyOnly: true);

        var image = Centers(Parts()).Single();

        // the box is 10 deep and high, its center is 5 above its origin: a mirror in Z turns that around as well
        var expected = new Vector3(0, -y, axis == "2" ? -(z + 5) : 5);
        Assert.True(Near(image, expected), $"{image} instead of {expected}");
    }

    [Fact]
    public void The_image_is_a_proper_closed_solid_of_the_same_size_not_turned_inside_out()
    {
        MirrorOf(Box(x: 20, width: "14", height: "9"));

        var solids = Parts().Where(p => p.Kind == PartKind.Solid).ToList();

        Assert.All(solids, p => Assert.True(p.Mesh.IsWatertight()));
        Assert.All(solids, p => Assert.Equal(14 * 10 * 9, p.Mesh.ComputeVolume(), 2)); // a positive volume: the winding was flipped
    }

    [Fact]
    public void Changing_the_original_changes_the_linked_image()
    {
        var box = Box(x: 20);
        MirrorOf(box);

        _history.Execute(new SetParameterCommand(_doc, box, "height", "30"));

        Assert.All(Parts(), p => Assert.Equal(30, p.Mesh.ComputeBounds().Size.Z, 3));
    }

    [Fact]
    public void A_mirror_can_be_turned_so_that_its_plane_turns_too()
    {
        var mirror = MirrorOf(Box(x: 20), copyOnly: true);
        _history.Execute(new SetTransformCommand(_doc, mirror, new Transform(Vector3.Zero, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)));

        var image = Centers(Parts()).Single();

        // turned by 90 degrees, the box is at Y = 20 in the world and the plane is the one perpendicular to Y: the image is at Y = -20
        Assert.True(Near(image, new Vector3(0, -20, 5)), image.ToString());
    }

    [Fact]
    public void A_hole_in_a_mirror_cuts_at_both_places()
    {
        var group = new GroupNode("Plate");
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, group));
        _history.Execute(new AddNodeCommand(_doc, group, new PrimitiveNode("box", "Plate") { Parameters = { ["width"] = "100", ["depth"] = "30", ["height"] = "10" } }));
        var hole = new PrimitiveNode("cylinder", "Hole")
        {
            Role = NodeRole.Hole,
            Transform = Transform.FromPosition(new Vector3(20, 0, 0)),
            Parameters = { ["diameter"] = "6", ["height"] = "30", ["segments"] = "64" },
        };
        MirrorOf(hole, parent: group);

        var parts = Parts();
        var volume = parts.Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());

        var oneHole = 64 / 2.0 * 9 * Math.Sin(2 * Math.PI / 64) * 10;
        Assert.InRange(30000 - volume, 2 * oneHole * 0.995, 2 * oneHole * 1.005);
        var ghosts = parts.Where(p => p.Kind == PartKind.Ghost).Select(p => MathF.Round(p.Mesh.ComputeBounds().Center.X)).Order().ToArray();
        Assert.Equal([-20f, 20f], ghosts);
    }

    [Fact]
    public void A_through_hole_in_a_mirror_keeps_its_depth_and_place_at_the_image()
    {
        var group = new GroupNode("Plate");
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, group));
        _history.Execute(new AddNodeCommand(_doc, group, new PrimitiveNode("box", "Plate") { Parameters = { ["width"] = "100", ["depth"] = "30", ["height"] = "10" } }));
        var hole = new PrimitiveNode("hole", "Hole")
        {
            Role = NodeRole.Hole,
            // drilled from below: the axis points up into the plate
            Transform = new Transform(new Vector3(20, 0, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI), Vector3.One),
            Parameters = { ["diameter"] = "4", ["depth"] = "1", ["through"] = "1", ["segments"] = "64" },
        };
        MirrorOf(hole, parent: group);

        var volume = Parts().Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());

        var bore = 64 / 2.0 * 4 * Math.Sin(2 * Math.PI / 64) * 10; // r = 2, through the 10 mm plate
        Assert.InRange(30000 - volume, 2 * bore * 0.995, 2 * bore * 1.005);
    }

    [Fact]
    public void An_axis_out_of_range_is_reported_and_the_source_is_shown_alone()
    {
        MirrorOf(Box(x: 20), axis: "5");

        var result = _evaluator.Evaluate(_doc);

        Assert.Single(result.Errors);
        Assert.Single(result.Parts);
    }

    [Fact]
    public void An_expression_that_does_not_resolve_is_reported_and_the_source_is_shown_alone()
    {
        MirrorOf(Box(x: 20), axis: "Fehlt");

        var result = _evaluator.Evaluate(_doc);

        Assert.Contains("Fehlt", result.Errors[0].Message);
        Assert.Single(result.Parts);
    }

    [Fact]
    public void A_hidden_mirror_shows_nothing()
    {
        var mirror = MirrorOf(Box(x: 20));
        _history.Execute(new SetVisibleCommand(_doc, mirror, false));

        Assert.Empty(Parts());
    }

    [Fact]
    public void A_pattern_of_a_mirror_repeats_both_halves()
    {
        var pattern = new PatternNode("Row", PatternKind.Linear) { Parameters = { ["count"] = "3", ["spacing"] = "100", ["axis"] = "1" } };
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, pattern));
        var mirror = new MirrorNode("Mirror") { Parameters = { ["axis"] = "0", ["copyOnly"] = "0" } };
        _history.Execute(new AddNodeCommand(_doc, pattern, mirror));
        _history.Execute(new AddNodeCommand(_doc, mirror, Box(x: 20)));

        Assert.Equal(6, Parts().Count);
    }

    [Fact]
    public void The_mirror_parameters_have_defaults_and_ranges()
    {
        Assert.Contains(MirrorParameters.Definitions, d => d.Name == "axis" && d.Default == 0 && d.Max == 2 && d.IsInteger);
        Assert.Contains(MirrorParameters.Definitions, d => d.Name == "copyOnly" && d.Default == 0 && d.Max == 1 && d.IsInteger);
    }
}
