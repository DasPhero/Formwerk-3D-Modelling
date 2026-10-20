using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>Patterns (F-PAT-02, F-PAT-03): the instance placements, and the pattern in the scene evaluation.</summary>
public class PatternTests
{
    private static PatternNode Linear(params (string, string)[] parameters)
    {
        var pattern = new PatternNode("Row", PatternKind.Linear);
        foreach (var (name, value) in parameters) pattern.Parameters[name] = value;
        return pattern;
    }

    private static List<Vector3> Offsets(PatternNode pattern, IReadOnlyDictionary<string, double>? variables = null)
    {
        var values = pattern.EvaluateParameters(variables, out var errors);
        Assert.Empty(errors);
        var matrices = PatternMath.Instances(pattern, values, out var problem);
        Assert.Null(problem);
        return [.. matrices.Select(m => Vector3.Transform(Vector3.Zero, m))];
    }

    private static bool Near(Vector3 a, Vector3 b) => Vector3.Distance(a, b) < 1e-4f;

    // ---- placements ----

    [Fact]
    public void A_row_has_the_given_count_and_spacing_along_X()
    {
        var offsets = Offsets(Linear(("count", "4"), ("spacing", "10")));

        Assert.Equal(4, offsets.Count);
        for (var i = 0; i < 4; i++) Assert.True(Near(offsets[i], new Vector3(10 * i, 0, 0)), offsets[i].ToString());
    }

    [Theory]
    [InlineData(0, 1, 0, 0)]
    [InlineData(1, 0, 1, 0)]
    [InlineData(2, 0, 0, 1)]
    public void The_direction_of_a_row_is_chosen_by_the_axis(int axis, float x, float y, float z)
    {
        var offsets = Offsets(Linear(("count", "3"), ("spacing", "7"), ("axis", axis.ToString())));

        Assert.True(Near(offsets[2], new Vector3(x, y, z) * 14));
    }

    [Fact]
    public void A_grid_of_4_by_3_with_10_mm_makes_12_instances_row_by_row()
    {
        var offsets = Offsets(Linear(("count", "4"), ("spacing", "10"), ("countY", "3"), ("spacingY", "10")));

        Assert.Equal(12, offsets.Count);
        Assert.True(Near(offsets[0], Vector3.Zero));
        Assert.True(Near(offsets[3], new Vector3(30, 0, 0))); // the end of the first row
        Assert.True(Near(offsets[4], new Vector3(0, 10, 0))); // the start of the second row
        Assert.True(Near(offsets[11], new Vector3(30, 20, 0)));
    }

    [Fact]
    public void The_total_length_is_spread_evenly_and_the_last_instance_ends_on_it()
    {
        var offsets = Offsets(Linear(("count", "5"), ("spacing", "40"), ("totalLength", "1")));

        Assert.True(Near(offsets[1], new Vector3(10, 0, 0)));
        Assert.True(Near(offsets[4], new Vector3(40, 0, 0)));
    }

    [Fact]
    public void The_total_length_of_a_single_instance_is_no_spacing()
    {
        var offsets = Offsets(Linear(("count", "1"), ("spacing", "40"), ("totalLength", "1")));

        Assert.Single(offsets);
        Assert.True(Near(offsets[0], Vector3.Zero));
    }

    [Fact]
    public void The_spacing_may_be_negative_and_the_row_runs_the_other_way()
    {
        var offsets = Offsets(Linear(("count", "3"), ("spacing", "-5")));

        Assert.True(Near(offsets[2], new Vector3(-10, 0, 0)));
    }

    [Fact]
    public void Skipped_instances_are_left_out_and_the_others_keep_their_place()
    {
        var pattern = Linear(("count", "5"), ("spacing", "10"));
        pattern.Skipped.Add(2);
        pattern.Skipped.Add(4);

        var offsets = Offsets(pattern);

        Assert.Equal(3, offsets.Count);
        Assert.True(Near(offsets[0], new Vector3(0, 0, 0)));
        Assert.True(Near(offsets[1], new Vector3(20, 0, 0)));
        Assert.True(Near(offsets[2], new Vector3(40, 0, 0)));
    }

    [Fact]
    public void Skipped_numbers_beyond_the_count_do_nothing()
    {
        var pattern = Linear(("count", "3"), ("spacing", "10"));
        pattern.Skipped.Add(9);

        Assert.Equal(3, Offsets(pattern).Count);
    }

    [Fact]
    public void Missing_parameters_use_the_defaults()
    {
        var offsets = Offsets(new PatternNode("P", PatternKind.Linear));

        Assert.Equal(3, offsets.Count); // count 3, spacing 15
        Assert.True(Near(offsets[1], new Vector3(15, 0, 0)));
    }

    [Fact]
    public void A_count_that_is_not_a_whole_number_is_rounded()
    {
        Assert.Equal(4, Offsets(Linear(("count", "3.6"))).Count);
    }

    [Fact]
    public void Variables_work_in_the_parameters()
    {
        var variables = new Dictionary<string, double> { ["Anzahl"] = 6, ["Abstand"] = 8 };

        var offsets = Offsets(Linear(("count", "Anzahl"), ("spacing", "Abstand*2")), variables);

        Assert.Equal(6, offsets.Count);
        Assert.True(Near(offsets[5], new Vector3(80, 0, 0)));
    }

    [Theory]
    [InlineData("0", "10")]
    [InlineData("1001", "10")]
    [InlineData("-2", "10")]
    public void A_count_out_of_range_is_a_problem(string count, string spacing)
    {
        var pattern = Linear(("count", count), ("spacing", spacing));
        var values = pattern.EvaluateParameters(null, out _);

        PatternMath.Instances(pattern, values, out var problem);

        Assert.NotNull(problem);
    }

    [Fact]
    public void More_than_a_thousand_instances_are_a_problem()
    {
        var pattern = Linear(("count", "40"), ("countY", "40"));
        var values = pattern.EvaluateParameters(null, out _);

        var matrices = PatternMath.Instances(pattern, values, out var problem);

        Assert.Contains("1000", problem);
        Assert.Single(matrices); // the source is still shown
    }

    [Fact]
    public void A_full_circle_is_divided_evenly_without_a_double_at_the_start()
    {
        var pattern = new PatternNode("Ring", PatternKind.Circular) { Parameters = { ["count"] = "3", ["angle"] = "360" } };
        var values = pattern.EvaluateParameters(null, out _);

        var matrices = PatternMath.Instances(pattern, values, out _);

        var onX = matrices.Select(m => Vector3.Transform(new Vector3(15, 0, 0), m)).ToList();
        Assert.True(Near(onX[0], new Vector3(15, 0, 0)));
        Assert.True(Near(onX[1], new Vector3(15 * MathF.Cos(MathF.PI * 2 / 3), 15 * MathF.Sin(MathF.PI * 2 / 3), 0))); // 120 degrees
        Assert.True(Near(onX[2], new Vector3(15 * MathF.Cos(MathF.PI * 4 / 3), 15 * MathF.Sin(MathF.PI * 4 / 3), 0)));
    }

    [Fact]
    public void A_part_circle_ends_on_the_angle_with_the_last_instance()
    {
        var pattern = new PatternNode("Arc", PatternKind.Circular) { Parameters = { ["count"] = "3", ["angle"] = "180" } };
        var values = pattern.EvaluateParameters(null, out _);

        var matrices = PatternMath.Instances(pattern, values, out _);

        Assert.True(Near(Vector3.Transform(new Vector3(10, 0, 0), matrices[1]), new Vector3(0, 10, 0))); // 90 degrees
        Assert.True(Near(Vector3.Transform(new Vector3(10, 0, 0), matrices[2]), new Vector3(-10, 0, 0))); // 180 degrees
    }

    [Fact]
    public void Changing_the_count_of_a_full_circle_from_3_to_6_gives_60_degree_steps()
    {
        var pattern = new PatternNode("Ring", PatternKind.Circular) { Parameters = { ["count"] = "6", ["angle"] = "360" } };
        var values = pattern.EvaluateParameters(null, out _);

        var matrices = PatternMath.Instances(pattern, values, out _);

        Assert.Equal(6, matrices.Count);
        Assert.True(Near(Vector3.Transform(new Vector3(10, 0, 0), matrices[1]), new Vector3(5, 5 * MathF.Sqrt(3), 0)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void A_circular_pattern_turns_about_the_chosen_axis(int axis)
    {
        var pattern = new PatternNode("Ring", PatternKind.Circular) { Parameters = { ["count"] = "4", ["angle"] = "360", ["axis"] = axis.ToString() } };
        var values = pattern.EvaluateParameters(null, out _);
        var turned = PatternMath.Instances(pattern, values, out _)[1]; // 90 degrees

        var axisVector = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        Assert.True(Near(Vector3.TransformNormal(axisVector, turned), axisVector)); // the axis itself does not move
        var other = axis == 2 ? Vector3.UnitX : Vector3.UnitZ;
        Assert.False(Near(Vector3.TransformNormal(other, turned), other));
    }

    [Fact]
    public void A_single_instance_of_a_circle_is_the_source()
    {
        var pattern = new PatternNode("Ring", PatternKind.Circular) { Parameters = { ["count"] = "1", ["angle"] = "90" } };
        var values = pattern.EvaluateParameters(null, out _);

        var matrices = PatternMath.Instances(pattern, values, out _);

        Assert.Single(matrices);
        Assert.Equal(Matrix4x4.Identity, matrices[0]);
    }

    [Fact]
    public void The_parameter_definitions_have_the_defaults_that_the_placements_use()
    {
        Assert.Contains(PatternParameters.For(PatternKind.Linear), d => d.Name == "count" && d.Default == 3);
        Assert.Contains(PatternParameters.For(PatternKind.Circular), d => d.Name == "angle" && d.Default == 360);
        Assert.DoesNotContain(PatternParameters.For(PatternKind.Circular), d => d.Name == "spacing");
    }

    // ---- in the scene ----

    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator = new(new ManifoldKernel());

    private PrimitiveNode Box(string width = "10", string depth = "10", string height = "10") =>
        new("box", "Box") { Parameters = { ["width"] = width, ["depth"] = depth, ["height"] = height } };

    private PatternNode PatternOf(PatternNode pattern, Node source, Node? parent = null)
    {
        _history.Execute(new AddNodeCommand(_doc, parent ?? _doc.Root, pattern));
        _history.Execute(new AddNodeCommand(_doc, pattern, source));
        return pattern;
    }

    private List<ScenePart> Parts() => [.. _evaluator.Evaluate(_doc).Parts];

    [Fact]
    public void A_pattern_of_a_box_gives_one_solid_per_instance()
    {
        PatternOf(Linear(("count", "4"), ("spacing", "15")), Box());

        var solids = Parts().Where(p => p.Kind == PartKind.Solid).ToList();

        Assert.Equal(4, solids.Count);
        Assert.Equal(4000, solids.Sum(p => (double)p.Mesh.ComputeVolume()), 2);
        Assert.Equal(55, solids.Max(p => p.Mesh.ComputeBounds().Max.X) - solids.Min(p => p.Mesh.ComputeBounds().Min.X), 3); // 3 x 15 + 10 (the box is 10 wide, from -5 to 5)
    }

    [Fact]
    public void The_instances_belong_to_the_source_node_so_a_click_finds_the_pattern()
    {
        var source = Box();
        PatternOf(Linear(("count", "3"), ("spacing", "15")), source);

        Assert.All(Parts(), part => Assert.Same(source, part.Source));
    }

    [Fact]
    public void A_grid_gives_all_the_instances()
    {
        PatternOf(Linear(("count", "4"), ("spacing", "15"), ("countY", "3"), ("spacingY", "15")), Box());

        Assert.Equal(12, Parts().Count(p => p.Kind == PartKind.Solid));
    }

    [Fact]
    public void A_skipped_instance_is_missing_in_the_scene()
    {
        var pattern = PatternOf(Linear(("count", "4"), ("spacing", "15")), Box());
        _history.Execute(new SetSkippedCommand(_doc, pattern, [2]));

        var xs = Parts().Select(p => p.Mesh.ComputeBounds().Center.X).Order().ToList();

        Assert.Equal(3, xs.Count);
        Assert.Equal([0f, 30f, 45f], xs.Select(x => MathF.Round(x)).ToArray());
    }

    [Fact]
    public void Changing_the_count_changes_the_number_of_instances_again_and_again()
    {
        var pattern = PatternOf(Linear(("count", "3"), ("spacing", "15")), Box());
        Assert.Equal(3, Parts().Count);

        _history.Execute(new SetParameterCommand(_doc, pattern, "count", "6"));
        Assert.Equal(6, Parts().Count);

        _history.Undo();
        Assert.Equal(3, Parts().Count);
    }

    [Fact]
    public void Changing_the_source_changes_every_instance()
    {
        var source = Box();
        PatternOf(Linear(("count", "3"), ("spacing", "20")), source);

        _history.Execute(new SetParameterCommand(_doc, source, "height", "30"));

        Assert.All(Parts(), part => Assert.Equal(30, part.Mesh.ComputeBounds().Size.Z, 3));
    }

    [Fact]
    public void The_pattern_can_be_moved_and_turned_like_a_group()
    {
        var pattern = PatternOf(Linear(("count", "3"), ("spacing", "20")), Box());
        _history.Execute(new SetTransformCommand(_doc, pattern, new Transform(new Vector3(0, 0, 50), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2), Vector3.One)));

        var centers = Parts().Select(p => p.Mesh.ComputeBounds().Center).ToList();

        Assert.All(centers, c => Assert.Equal(0, c.X, 3)); // the row now runs along Y
        Assert.Equal([0f, 20f, 40f], centers.Select(c => MathF.Round(c.Y)).Order().ToArray());
        Assert.All(centers, c => Assert.Equal(55, c.Z, 3));
    }

    [Fact]
    public void A_pattern_of_holes_in_a_group_cuts_the_body_at_every_instance()
    {
        var group = new GroupNode("Plate");
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, group));
        _history.Execute(new AddNodeCommand(_doc, group, Box("100", "30", "10")));
        var hole = new PrimitiveNode("cylinder", "Hole") { Role = NodeRole.Hole, Transform = Transform.FromPosition(new Vector3(-30, 0, 0)), Parameters = { ["diameter"] = "6", ["height"] = "30", ["segments"] = "64" } };
        PatternOf(Linear(("count", "4"), ("spacing", "20")), hole, group);
        double Volume() => Parts().Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());
        var oneHole = 30000 - 64 / 2.0 * 9 * Math.Sin(2 * Math.PI / 64) * 10; // plate minus one 6 mm hole through its 10 mm
        Assert.True(Volume() < oneHole - 2 * 250, Volume().ToString()); // clearly more than one hole is cut

        var removed = 30000 - Volume();
        var single = 30000 - oneHole;
        Assert.InRange(removed, 4 * single * 0.995, 4 * single * 1.005);
        Assert.Equal(4, Parts().Count(p => p.Kind == PartKind.Ghost)); // one ghost per instance
    }

    [Fact]
    public void A_hole_that_is_skipped_does_not_cut()
    {
        var group = new GroupNode("Plate");
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, group));
        _history.Execute(new AddNodeCommand(_doc, group, Box("100", "30", "10")));
        var hole = new PrimitiveNode("cylinder", "Hole") { Role = NodeRole.Hole, Transform = Transform.FromPosition(new Vector3(-30, 0, 0)), Parameters = { ["diameter"] = "6", ["height"] = "30", ["segments"] = "64" } };
        var pattern = PatternOf(Linear(("count", "4"), ("spacing", "20")), hole, group);
        double Volume() => Parts().Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());
        var all = Volume();

        _history.Execute(new SetSkippedCommand(_doc, pattern, [1, 3]));

        var removedNow = 30000 - Volume();
        Assert.InRange(removedNow, (30000 - all) / 2 * 0.99, (30000 - all) / 2 * 1.01);
    }

    [Fact]
    public void A_wrong_parameter_is_reported_and_the_source_is_still_shown()
    {
        PatternOf(Linear(("count", "0"), ("spacing", "15")), Box());

        var result = _evaluator.Evaluate(_doc);

        Assert.Single(result.Errors);
        Assert.Single(result.Parts, p => p.Kind == PartKind.Solid);
    }

    [Fact]
    public void An_expression_that_does_not_resolve_is_reported_and_the_source_is_still_shown()
    {
        PatternOf(Linear(("count", "Fehlt"), ("spacing", "15")), Box());

        var result = _evaluator.Evaluate(_doc);

        Assert.Single(result.Errors);
        Assert.Contains("Fehlt", result.Errors[0].Message);
        Assert.Single(result.Parts);
    }

    [Fact]
    public void A_variable_drives_the_count_in_one_run()
    {
        _history.Execute(new SetVariablesCommand(_doc, [new("Anzahl", "3")], "Add"));
        PatternOf(Linear(("count", "Anzahl"), ("spacing", "15")), Box());
        Assert.Equal(3, Parts().Count);

        _history.Execute(new SetVariablesCommand(_doc, [new("Anzahl", "5")], "Change"));

        Assert.Equal(5, Parts().Count);
    }

    [Fact]
    public void A_hidden_pattern_shows_nothing()
    {
        var pattern = PatternOf(Linear(("count", "3"), ("spacing", "15")), Box());
        _history.Execute(new SetVisibleCommand(_doc, pattern, false));

        Assert.Empty(Parts());
    }

    [Fact]
    public void A_pattern_inside_a_pattern_multiplies_the_instances()
    {
        var inner = Linear(("count", "3"), ("spacing", "15"));
        var outer = Linear(("count", "2"), ("spacing", "100"));
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, outer));
        _history.Execute(new AddNodeCommand(_doc, outer, inner));
        _history.Execute(new AddNodeCommand(_doc, inner, Box()));

        Assert.Equal(6, Parts().Count);
    }

    [Fact]
    public void Every_instance_is_a_closed_solid()
    {
        PatternOf(Linear(("count", "3"), ("spacing", "15")), Box());

        Assert.All(Parts(), part => Assert.True(part.Mesh.IsWatertight()));
    }
}
