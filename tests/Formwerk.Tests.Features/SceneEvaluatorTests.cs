using System.Diagnostics;
using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

public class SceneEvaluatorTests
{
    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator = new(new ManifoldKernel());

    private PrimitiveNode Box(string name, string w, string d, string h, Vector3? at = null)
    {
        var node = new PrimitiveNode("box", name)
        {
            Parameters = { ["width"] = w, ["depth"] = d, ["height"] = h },
        };
        if (at is not null) node.Transform = Transform.FromPosition(at.Value);
        return node;
    }

    private PrimitiveNode Cylinder(string name, string diameter, string height, Vector3? at = null)
    {
        var node = new PrimitiveNode("cylinder", name)
        {
            Parameters = { ["diameter"] = diameter, ["height"] = height, ["segments"] = "64" },
        };
        if (at is not null) node.Transform = Transform.FromPosition(at.Value);
        return node;
    }

    private void Add(Node parent, Node node) => _history.Execute(new AddNodeCommand(_doc, parent, node));

    /// <summary>Area of the 64-gon the cylinder helper produces (not of a true circle).</summary>
    private static double PolygonArea(double radius) => 0.5 * 64 * radius * radius * Math.Sin(2 * Math.PI / 64);

    private static double Volume(EvaluationResult result, PartKind kind) =>
        result.Parts.Where(p => p.Kind == kind).Sum(p => p.Mesh.ComputeVolume());

    [Fact]
    public void Single_primitive_is_evaluated_with_its_transform()
    {
        Add(_doc.Root, Box("Box", "10", "20", "30", new Vector3(100, 0, 0)));

        var result = _evaluator.Evaluate(_doc);

        var part = Assert.Single(result.Parts);
        Assert.Equal(PartKind.Solid, part.Kind);
        Assert.Equal(6000, part.Mesh.ComputeVolume(), 2);
        Assert.Equal(100, part.Mesh.ComputeBounds().Center.X, 3);
        Assert.False(result.HasErrors);
    }

    // F-BOO-01: a cylinder set to Hole and grouped with a box cuts the box immediately.
    [Fact]
    public void Hole_in_a_group_cuts_the_solids_of_that_group()
    {
        var box = Box("Box", "40", "40", "10");
        var hole = Cylinder("Hole", "10", "30", new Vector3(0, 0, -10));
        Add(_doc.Root, box);
        Add(_doc.Root, hole);
        _history.Execute(new SetRoleCommand(_doc, hole, NodeRole.Hole));
        _history.Execute(new WrapNodesCommand(_doc, new GroupNode("Group"), [box, hole]));

        var result = _evaluator.Evaluate(_doc);

        var expected = 40 * 40 * 10 - PolygonArea(5) * 10;
        Assert.InRange(Volume(result, PartKind.Solid), expected - 0.5, expected + 0.5);
        Assert.Single(result.Parts, p => p.Kind == PartKind.Ghost); // F-VIS-04: the hole stays visible as a ghost
        Assert.DoesNotContain(result.Parts, p => p.Kind == PartKind.Hole);
    }

    [Fact]
    public void Hole_outside_of_any_group_is_only_a_ghost()
    {
        var hole = Cylinder("Hole", "10", "30");
        Add(_doc.Root, Box("Box", "40", "40", "10"));
        Add(_doc.Root, hole);
        _history.Execute(new SetRoleCommand(_doc, hole, NodeRole.Hole));

        var result = _evaluator.Evaluate(_doc);

        Assert.Equal(16000, Volume(result, PartKind.Solid), 1);
        Assert.Single(result.Parts, p => p.Kind == PartKind.Ghost);
    }

    // F-BOO-02: operands remain individually editable; the result follows.
    [Fact]
    public void Boolean_difference_follows_its_operands()
    {
        var box = Box("Box", "40", "40", "10");
        var hole = Cylinder("Cutter", "10", "30", new Vector3(0, 0, -10));
        Add(_doc.Root, box);
        Add(_doc.Root, hole);
        var diff = new BooleanNode("Difference", BooleanOperation.Difference);
        _history.Execute(new WrapNodesCommand(_doc, diff, [box, hole]));

        var before = Volume(_evaluator.Evaluate(_doc), PartKind.Solid);
        Assert.InRange(before, 16000 - PolygonArea(5) * 10 - 0.5, 16000 - PolygonArea(5) * 10 + 0.5);

        _history.Execute(new SetParameterCommand(_doc, hole, "diameter", "20"));
        var after = Volume(_evaluator.Evaluate(_doc), PartKind.Solid);
        Assert.InRange(after, 16000 - PolygonArea(10) * 10 - 0.5, 16000 - PolygonArea(10) * 10 + 0.5);

        _history.Undo();
        Assert.Equal(before, Volume(_evaluator.Evaluate(_doc), PartKind.Solid), 1);
    }

    // The Difference node says what happens; an operand flagged as a hole must not silently disappear.
    [Fact]
    public void A_boolean_operand_flagged_as_hole_is_still_an_operand()
    {
        var box = Box("Box", "40", "40", "10");
        var cutter = Cylinder("Cutter", "10", "30", new Vector3(0, 0, -10));
        Add(_doc.Root, box);
        Add(_doc.Root, cutter);
        _history.Execute(new SetRoleCommand(_doc, cutter, NodeRole.Hole));
        _history.Execute(new WrapNodesCommand(_doc, new BooleanNode("Difference", BooleanOperation.Difference), [box, cutter]));

        var result = _evaluator.Evaluate(_doc);

        var solid = Assert.Single(result.Parts);
        Assert.Equal(PartKind.Solid, solid.Kind);
        Assert.InRange(solid.Mesh.ComputeVolume(), 16000 - PolygonArea(5) * 10 - 0.5, 16000 - PolygonArea(5) * 10 + 0.5);
    }

    [Fact]
    public void Boolean_union_and_intersection_are_supported()
    {
        var a = Box("A", "10", "10", "10");
        var b = Box("B", "10", "10", "10", new Vector3(5, 0, 0));
        Add(_doc.Root, a);
        Add(_doc.Root, b);
        var op = new BooleanNode("Op", BooleanOperation.Union);
        _history.Execute(new WrapNodesCommand(_doc, op, [a, b]));

        Assert.Equal(1500, Volume(_evaluator.Evaluate(_doc), PartKind.Solid), 2);

        op.Operation = BooleanOperation.Intersection;
        _doc.NotifyChanged();
        Assert.Equal(500, Volume(_evaluator.Evaluate(_doc), PartKind.Solid), 2);
    }

    [Fact]
    public void Merge_unites_solids_and_subtracts_holes_as_one_body()
    {
        var a = Box("A", "10", "10", "10");
        var b = Box("B", "10", "10", "10", new Vector3(10, 0, 0));
        var hole = Box("Hole", "4", "4", "20", new Vector3(5, 0, -5));
        Add(_doc.Root, a);
        Add(_doc.Root, b);
        Add(_doc.Root, hole);
        _history.Execute(new SetRoleCommand(_doc, hole, NodeRole.Hole));
        _history.Execute(new WrapNodesCommand(_doc, new MergeNode("Merged"), [a, b, hole]));

        var result = _evaluator.Evaluate(_doc);

        Assert.Equal(2000 - 4 * 4 * 10, Volume(result, PartKind.Solid), 2);
        Assert.Single(result.Parts, p => p.Kind == PartKind.Solid);
    }

    // Spec F-GRP-02 flavour: unwrapping a merge restores separate objects.
    [Fact]
    public void Unwrapping_restores_the_separate_objects()
    {
        var a = Box("A", "10", "10", "10");
        var b = Box("B", "10", "10", "10", new Vector3(5, 0, 0));
        Add(_doc.Root, a);
        Add(_doc.Root, b);
        var merge = new MergeNode("Merged");
        _history.Execute(new WrapNodesCommand(_doc, merge, [a, b]));
        Assert.Single(_evaluator.Evaluate(_doc).Parts);

        _history.Execute(new UnwrapNodeCommand(_doc, merge));

        var result = _evaluator.Evaluate(_doc);
        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(2000, Volume(result, PartKind.Solid), 2);
    }

    [Fact]
    public void Group_transform_moves_all_children()
    {
        var box = Box("Box", "10", "10", "10");
        Add(_doc.Root, box);
        var group = new GroupNode("G") { Transform = Transform.FromPosition(new Vector3(0, 50, 0)) };
        _history.Execute(new WrapNodesCommand(_doc, group, [box]));

        var part = Assert.Single(_evaluator.Evaluate(_doc).Parts);

        Assert.Equal(50, part.Mesh.ComputeBounds().Center.Y, 3);
        Assert.Same(box, part.Source);
    }

    [Fact]
    public void Hidden_nodes_are_skipped_and_the_cache_notices_it()
    {
        var a = Box("A", "10", "10", "10");
        var b = Box("B", "10", "10", "10", new Vector3(5, 0, 0));
        Add(_doc.Root, a);
        Add(_doc.Root, b);
        var op = new BooleanNode("Op", BooleanOperation.Union);
        _history.Execute(new WrapNodesCommand(_doc, op, [a, b]));
        Assert.Equal(1500, Volume(_evaluator.Evaluate(_doc), PartKind.Solid), 2);

        b.Visible = false;

        Assert.Equal(1000, Volume(_evaluator.Evaluate(_doc), PartKind.Solid), 2);
    }

    // F-PAR-03: one variable change updates every linked object in a single evaluation.
    [Fact]
    public void Changing_a_variable_updates_all_linked_objects()
    {
        _doc.Variables["Wand"] = "2";
        Add(_doc.Root, Box("Linked1", "Wand*10", "10", "10"));
        Add(_doc.Root, Box("Linked2", "10", "Wand", "10"));
        Add(_doc.Root, Box("Fixed", "10", "10", "10"));

        var before = _evaluator.Evaluate(_doc).Parts.Select(p => p.Mesh.ComputeVolume()).ToArray();
        _doc.Variables["Wand"] = "3";
        var after = _evaluator.Evaluate(_doc).Parts.Select(p => p.Mesh.ComputeVolume()).ToArray();

        Assert.Equal(new[] { 2000.0, 200.0, 1000.0 }, before.Select(v => Math.Round(v)).ToArray());
        Assert.Equal(new[] { 3000.0, 300.0, 1000.0 }, after.Select(v => Math.Round(v)).ToArray());
    }

    [Fact]
    public void Unknown_shape_and_bad_parameters_are_reported_not_thrown()
    {
        var unknown = new PrimitiveNode("teapot", "Teapot");
        var bad = Box("Bad", "10", "10", "banana");
        var negative = Box("Neg", "-5", "10", "10");
        Add(_doc.Root, unknown);
        Add(_doc.Root, bad);
        Add(_doc.Root, negative);
        Add(_doc.Root, Box("Good", "10", "10", "10"));

        var result = _evaluator.Evaluate(_doc);

        Assert.Equal(3, result.Errors.Count);
        Assert.Contains(result.Errors, e => e.Node == unknown && e.Message.Contains("Unknown shape"));
        Assert.Contains(result.Errors, e => e.Node == bad && e.Message.Contains("height"));
        Assert.Contains(result.Errors, e => e.Node == negative && e.Message.Contains("must be between"));
        Assert.Single(result.Parts); // the valid object is still shown
    }

    [Fact]
    public void Failed_expression_keeps_the_last_valid_geometry()
    {
        var box = Box("Box", "10", "10", "10");
        Add(_doc.Root, box);
        Assert.Single(_evaluator.Evaluate(_doc).Parts);

        _history.Execute(new SetParameterCommand(_doc, box, "width", "1/0"));
        var result = _evaluator.Evaluate(_doc);

        Assert.True(result.HasErrors);
        var part = Assert.Single(result.Parts);
        Assert.Equal(1000, part.Mesh.ComputeVolume(), 2);
    }

    // F-BOO-05: scheduling a failing boolean keeps the last good result and marks the node.
    [Fact]
    public void Failing_boolean_keeps_the_last_valid_result_and_reports_the_node()
    {
        var kernel = new SwitchableKernel(new ManifoldKernel());
        var evaluator = new SceneEvaluator(kernel);
        var box = Box("Box", "20", "20", "20");
        var hole = Cylinder("Cutter", "10", "40", new Vector3(0, 0, -10));
        Add(_doc.Root, box);
        Add(_doc.Root, hole);
        var diff = new BooleanNode("Difference", BooleanOperation.Difference);
        _history.Execute(new WrapNodesCommand(_doc, diff, [box, hole]));

        var good = evaluator.Evaluate(_doc);
        var goodVolume = Volume(good, PartKind.Solid);
        Assert.False(good.HasErrors);

        kernel.Fail = true;
        _history.Execute(new SetParameterCommand(_doc, hole, "diameter", "12"));
        var failed = evaluator.Evaluate(_doc);

        var error = Assert.Single(failed.Errors);
        Assert.Same(diff, error.Node);
        Assert.Equal(goodVolume, Volume(failed, PartKind.Solid), 3);

        kernel.Fail = false;
        var recovered = evaluator.Evaluate(_doc);
        Assert.False(recovered.HasErrors);
        Assert.NotEqual(goodVolume, Volume(recovered, PartKind.Solid), 1);
    }

    [Fact]
    public void Unchanged_subtrees_are_served_from_the_cache()
    {
        var box = Box("Box", "20", "20", "20");
        var hole = Cylinder("Cutter", "10", "40", new Vector3(0, 0, -10));
        Add(_doc.Root, box);
        Add(_doc.Root, hole);
        _history.Execute(new WrapNodesCommand(_doc, new BooleanNode("D", BooleanOperation.Difference), [box, hole]));
        _evaluator.Evaluate(_doc);
        var hitsBefore = _evaluator.CacheHits;

        _evaluator.Evaluate(_doc);

        Assert.True(_evaluator.CacheHits > hitsBefore);
    }

    [Fact]
    public void Cancelled_evaluation_throws()
    {
        Add(_doc.Root, Box("Box", "10", "10", "10"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() => _evaluator.Evaluate(_doc, cts.Token));
    }

    // F-BOO-03: moving a hole re-evaluates a typical model in under 200 ms.
    [Fact]
    public void Moving_a_hole_updates_a_typical_model_quickly()
    {
        var plate = Box("Plate", "100", "60", "8");
        var group = new GroupNode("Bracket");
        Add(_doc.Root, plate);
        _history.Execute(new WrapNodesCommand(_doc, group, [plate]));
        var holes = new List<PrimitiveNode>();
        for (var i = 0; i < 4; i++)
        {
            var hole = Cylinder($"Hole{i}", "6", "20", new Vector3(-30 + i * 20, 0, -5));
            hole.Role = NodeRole.Hole;
            Add(group, hole);
            holes.Add(hole);
        }

        _evaluator.Evaluate(_doc); // first run, fills the caches

        // Wall-clock timing is noisy when many test projects run at once, so the fastest of five moves counts:
        // a real slowdown of the evaluator shows up in every run, a busy machine only in some.
        var best = long.MaxValue;
        EvaluationResult? result = null;
        for (var i = 0; i < 5; i++)
        {
            var sw = Stopwatch.StartNew();
            _history.Execute(new SetTransformCommand(_doc, holes[0], Transform.FromPosition(new Vector3(-35 + i, 5, -5))));
            result = _evaluator.Evaluate(_doc);
            sw.Stop();
            best = Math.Min(best, sw.ElapsedMilliseconds);
        }

        Assert.False(result!.HasErrors);
        Assert.True(best < 200, $"best of 5 moves took {best} ms");
    }

    // N-PRF-02 / Q-03: a plate with 50 holes, one of them is moved
    [Fact]
    public void Moving_one_of_fifty_holes_updates_the_model_quickly()
    {
        var plate = Box("Plate", "200", "120", "8");
        var group = new GroupNode("Plate with holes");
        Add(_doc.Root, plate);
        _history.Execute(new WrapNodesCommand(_doc, group, [plate]));
        var holes = new List<PrimitiveNode>();
        for (var i = 0; i < 50; i++)
        {
            var hole = Cylinder($"Hole{i}", "5", "20", new Vector3(-90 + i % 10 * 20, -50 + i / 10 * 20, -5));
            hole.Parameters["segments"] = "32"; // as in Q-03
            hole.Role = NodeRole.Hole;
            Add(group, hole);
            holes.Add(hole);
        }

        _evaluator.Evaluate(_doc); // first run, fills the caches

        var times = new List<long>();
        EvaluationResult? result = null;
        for (var i = 0; i < 7; i++)
        {
            var sw = Stopwatch.StartNew();
            _history.Execute(new SetTransformCommand(_doc, holes[0], Transform.FromPosition(new Vector3(-90 + i * 0.5f, -50, -5))));
            result = _evaluator.Evaluate(_doc);
            sw.Stop();
            times.Add(sw.ElapsedMilliseconds);
        }

        Assert.False(result!.HasErrors);
        Assert.True(times.Min() < 200, $"moves took {string.Join(", ", times)} ms");
    }

    // ---- imported meshes ----------------------------------------------------------------------------

    private MeshStore _meshes = new();

    private SceneEvaluator MeshEvaluator() => new(new ManifoldKernel(), _meshes);

    private MeshNode ImportedBox(string name, double size = 20, Vector3? at = null)
    {
        var mesh = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = size, ["depth"] = size, ["height"] = size });
        var id = Guid.NewGuid();
        _meshes.Add(id, mesh);
        var node = new MeshNode(name, id);
        if (at is not null) node.Transform = Transform.FromPosition(at.Value);
        return node;
    }

    [Fact]
    public void An_imported_mesh_is_shown_with_its_transform()
    {
        Add(_doc.Root, ImportedBox("Imported", 20, new Vector3(100, 0, 0)));

        var result = MeshEvaluator().Evaluate(_doc);

        var part = Assert.Single(result.Parts);
        Assert.Equal(8000, part.Mesh.ComputeVolume(), 2);
        Assert.Equal(100, part.Mesh.ComputeBounds().Center.X, 3);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void A_mesh_that_is_missing_from_the_store_is_reported()
    {
        Add(_doc.Root, new MeshNode("Lost", Guid.NewGuid()));
        Add(_doc.Root, Box("Fine", "10", "10", "10"));

        var result = MeshEvaluator().Evaluate(_doc);

        Assert.Single(result.Parts); // the other object is still shown
        Assert.Contains("missing", Assert.Single(result.Errors).Message);
    }

    [Fact]
    public void An_evaluator_without_a_store_reports_imported_objects_as_missing()
    {
        Add(_doc.Root, ImportedBox("Imported"));

        var result = new SceneEvaluator(new ManifoldKernel()).Evaluate(_doc);

        Assert.Empty(result.Parts);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void An_imported_mesh_can_be_cut_by_a_hole_in_the_same_group()
    {
        var imported = ImportedBox("Imported", 40);
        var hole = Cylinder("Hole", "10", "60", new Vector3(0, 0, -10));
        Add(_doc.Root, imported);
        Add(_doc.Root, hole);
        _history.Execute(new SetRoleCommand(_doc, hole, NodeRole.Hole));
        _history.Execute(new WrapNodesCommand(_doc, new GroupNode("Group"), [imported, hole]));

        var result = MeshEvaluator().Evaluate(_doc);

        var solid = Assert.Single(result.Parts, p => p.Kind == PartKind.Solid);
        Assert.InRange(solid.Mesh.ComputeVolume(), 64000 - PolygonArea(5) * 40 - 1, 64000 - PolygonArea(5) * 40 + 1);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void An_imported_mesh_works_as_a_boolean_operand()
    {
        var box = Box("Box", "40", "40", "40");
        var imported = ImportedBox("Imported", 20, new Vector3(25, 0, 10)); // sticks out of the box by 15 mm
        Add(_doc.Root, box);
        Add(_doc.Root, imported);
        _history.Execute(new WrapNodesCommand(_doc, new BooleanNode("Union", BooleanOperation.Union), [box, imported]));

        var result = MeshEvaluator().Evaluate(_doc);

        var solid = Assert.Single(result.Parts);
        Assert.Equal(70000, solid.Mesh.ComputeVolume(), 0); // 64000 + 8000 - 2000 overlap
        Assert.True(solid.Mesh.IsWatertight());
    }

    // A mesh with a hole in its surface cannot be used for booleans: the user gets a message, the scene keeps its last result.
    [Fact]
    public void An_open_mesh_in_a_boolean_gives_an_error_and_keeps_the_previous_result()
    {
        var closed = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 20, ["depth"] = 20, ["height"] = 20 });
        var open = new MeshData(closed.Vertices.ToArray(), closed.Indices[..^3].ToArray());
        var id = Guid.NewGuid();
        _meshes.Add(id, open);

        var box = Box("Box", "40", "40", "40");
        var broken = new MeshNode("Open mesh", id);
        Add(_doc.Root, box);
        Add(_doc.Root, broken);
        _history.Execute(new WrapNodesCommand(_doc, new BooleanNode("Difference", BooleanOperation.Difference), [box, broken]));

        var result = MeshEvaluator().Evaluate(_doc);

        var error = Assert.Single(result.Errors);
        Assert.Contains("watertight", error.Message);
        Assert.Empty(result.Parts); // nothing valid to fall back to yet
    }

    [Fact]
    public void Duplicated_imported_objects_share_one_mesh()
    {
        var node = ImportedBox("Imported");
        Add(_doc.Root, node);
        var copy = (MeshNode)node.Clone(newIds: true);
        copy.Transform = Transform.FromPosition(new Vector3(50, 0, 0));
        Add(_doc.Root, copy);

        var result = MeshEvaluator().Evaluate(_doc);

        Assert.Equal(2, result.Parts.Count);
        Assert.Equal(1, _meshes.Count);
    }

    private sealed class SwitchableKernel(IGeometryKernel inner) : IGeometryKernel
    {
        public bool Fail { get; set; }

        public string Name => "switchable";

        public MeshData Boolean(BooleanOperation operation, IReadOnlyList<MeshData> operands, CancellationToken cancellationToken) =>
            Fail ? throw new GeometryException("Simulated failure.") : inner.Boolean(operation, operands, cancellationToken);
    }
}
