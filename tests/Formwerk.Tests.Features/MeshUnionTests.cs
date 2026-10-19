using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>
/// Uniting many shapes quickly (F-BOO-03, Q-03): pieces whose boxes do not touch cannot meet, so they are simply put together;
/// only groups of pieces that overlap or touch go to the boolean engine.
/// </summary>
public class MeshUnionTests
{
    private static MeshData Cylinder(float x, float y, float diameter = 5, int segments = 32) =>
        new CylinderGenerator().Generate(new Dictionary<string, double> { ["diameter"] = diameter, ["height"] = 20, ["segments"] = segments })
            .Transformed(Matrix4x4.CreateTranslation(x, y, 0));

    private static MeshData Box(float x0, float x1) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = x1 - x0, ["depth"] = 10, ["height"] = 10 })
            .Transformed(Matrix4x4.CreateTranslation((x0 + x1) / 2, 0, 0));

    private sealed class CountingKernel(IGeometryKernel inner) : IGeometryKernel
    {
        public int Unions { get; private set; }

        public int Differences { get; private set; }

        public List<int> UnionSizes { get; } = [];

        public string Name => inner.Name;

        public MeshData Boolean(BooleanOperation operation, IReadOnlyList<MeshData> operands, CancellationToken cancellationToken)
        {
            if (operation == BooleanOperation.Union)
            {
                Unions++;
                UnionSizes.Add(operands.Count);
            }
            else
            {
                Differences++;
            }

            return inner.Boolean(operation, operands, cancellationToken);
        }
    }

    // ---- which pieces belong together --------------------------------------------------------------------

    [Fact]
    public void Pieces_that_are_far_apart_are_all_alone()
    {
        var groups = MeshUnion.Partition([Cylinder(0, 0), Cylinder(50, 0), Cylinder(0, 50)]);

        Assert.Equal(3, groups.Count);
        Assert.All(groups, g => Assert.Single(g));
    }

    [Fact]
    public void Pieces_that_overlap_belong_together_and_a_chain_is_one_group()
    {
        // 0 and 4 overlap, 4 and 8 overlap, 0 and 8 do not: all three are one group through the one in the middle
        var groups = MeshUnion.Partition([Cylinder(0, 0), Cylinder(4, 0), Cylinder(8, 0), Cylinder(100, 100)]);

        Assert.Equal(2, groups.Count);
        Assert.Equal([0, 1, 2], groups[0]);
        Assert.Equal([3], groups[1]);
    }

    [Fact]
    public void Pieces_that_only_touch_belong_together()
    {
        var groups = MeshUnion.Partition([Box(0, 10), Box(10, 20)]);

        Assert.Single(groups);
    }

    [Fact]
    public void A_tiny_gap_is_still_a_gap()
    {
        var groups = MeshUnion.Partition([Box(0, 10), Box(10.5f, 20)]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void Pieces_with_the_same_x_but_far_apart_in_y_are_separate()
    {
        var groups = MeshUnion.Partition([Cylinder(0, 0), Cylinder(0, 100)]);

        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void The_groups_come_in_the_order_of_their_first_piece_and_keep_the_order_inside()
    {
        var groups = MeshUnion.Partition([Cylinder(100, 0), Cylinder(0, 0), Cylinder(101, 1), Cylinder(1, 1)]);

        Assert.Equal([[0, 2], [1, 3]], groups);
    }

    // ---- uniting ---------------------------------------------------------------------------------------------

    [Fact]
    public void Separate_pieces_are_put_together_without_calling_the_boolean_engine()
    {
        var pieces = Enumerable.Range(0, 10).Select(i => Cylinder(i * 20, 0)).ToList();
        var kernel = new CountingKernel(new ManifoldKernel());

        var result = MeshUnion.Compute(pieces, group => kernel.Boolean(BooleanOperation.Union, group, CancellationToken.None));

        Assert.Equal(0, kernel.Unions);
        Assert.Equal(pieces.Sum(p => p.TriangleCount), result.TriangleCount);
        Assert.Equal(pieces.Sum(p => p.ComputeVolume()), result.ComputeVolume(), 3);
        Assert.True(result.IsWatertight());
    }

    [Fact]
    public void Only_the_groups_that_overlap_go_to_the_engine_and_the_result_equals_the_full_union()
    {
        // two overlapping pairs and three loose pieces
        var pieces = new List<MeshData> { Cylinder(0, 0), Cylinder(3, 0), Cylinder(50, 0), Cylinder(100, 0), Cylinder(103, 1), Cylinder(150, 0) };
        var kernel = new CountingKernel(new ManifoldKernel());

        var result = MeshUnion.Compute(pieces, group => kernel.Boolean(BooleanOperation.Union, group, CancellationToken.None));
        var full = new ManifoldKernel().Boolean(BooleanOperation.Union, pieces, CancellationToken.None);

        Assert.Equal([2, 2], kernel.UnionSizes);
        Assert.Equal(full.ComputeVolume(), result.ComputeVolume(), 2);
        Assert.True(result.IsWatertight());
    }

    [Fact]
    public void Everything_overlapping_is_one_call_like_before()
    {
        var pieces = new List<MeshData> { Cylinder(0, 0), Cylinder(2, 0), Cylinder(4, 0) };
        var kernel = new CountingKernel(new ManifoldKernel());

        MeshUnion.Compute(pieces, group => kernel.Boolean(BooleanOperation.Union, group, CancellationToken.None));

        Assert.Equal([3], kernel.UnionSizes);
    }

    [Fact]
    public void A_single_piece_is_returned_as_it_is_and_nothing_gives_nothing()
    {
        var piece = Cylinder(0, 0);

        Assert.Same(piece, MeshUnion.Compute([piece], _ => throw new InvalidOperationException("not needed")));
        Assert.True(MeshUnion.Compute([], _ => throw new InvalidOperationException("not needed")).IsEmpty);
    }

    [Fact]
    public void Empty_pieces_are_ignored()
    {
        var piece = Cylinder(0, 0);

        var result = MeshUnion.Compute([MeshData.Empty, piece, MeshData.Empty], _ => throw new InvalidOperationException("not needed"));

        Assert.Same(piece, result);
    }

    [Fact]
    public void A_failure_of_the_engine_is_not_hidden()
    {
        var pieces = new List<MeshData> { Cylinder(0, 0), Cylinder(2, 0) };

        Assert.Throws<GeometryException>(() => MeshUnion.Compute(pieces, _ => throw new GeometryException("boom")));
    }

    // ---- in the scene ----------------------------------------------------------------------------------------

    private static (FormwerkDocument Doc, GroupNode Group) PlateWith(Func<int, Vector3> holeAt, int holes, float diameter = 5)
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var plate = new PrimitiveNode("box", "Plate") { Parameters = { ["width"] = "200", ["depth"] = "120", ["height"] = "8" } };
        history.Execute(new AddNodeCommand(doc, doc.Root, plate));
        var group = new GroupNode("Plate with holes");
        history.Execute(new WrapNodesCommand(doc, group, [plate]));
        for (var i = 0; i < holes; i++)
        {
            var hole = new PrimitiveNode("cylinder", $"Hole{i}")
            {
                Role = NodeRole.Hole,
                Parameters = { ["diameter"] = diameter.ToString(System.Globalization.CultureInfo.InvariantCulture), ["height"] = "20", ["segments"] = "32" },
                Transform = Transform.FromPosition(holeAt(i)),
            };
            history.Execute(new AddNodeCommand(doc, group, hole));
        }

        return (doc, group);
    }

    [Fact]
    public void Many_separate_holes_cut_the_plate_without_any_union_and_give_the_same_body()
    {
        var (doc, _) = PlateWith(i => new Vector3(-90 + i % 10 * 20, -50 + i / 10 * 20, -5), 50);
        var kernel = new CountingKernel(new ManifoldKernel());

        var result = new SceneEvaluator(kernel).Evaluate(doc);

        Assert.False(result.HasErrors);
        Assert.Equal(0, kernel.Unions);
        Assert.Equal(1, kernel.Differences);
        var solid = result.Parts.Single(p => p.Kind == PartKind.Solid).Mesh;
        var hole = new CylinderGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 5, ["height"] = 20, ["segments"] = 32 }).ComputeVolume();
        Assert.Equal(200 * 120 * 8 - 50 * hole * (8.0 / 20.0), solid.ComputeVolume(), 1);
        Assert.True(solid.IsWatertight());
        Assert.Equal(50, result.Parts.Count(p => p.Kind == PartKind.Ghost));
    }

    [Fact]
    public void Overlapping_holes_still_cut_the_union_of_both()
    {
        var (doc, _) = PlateWith(i => new Vector3(i * 4, 0, -5), 2, diameter: 10);

        var result = new SceneEvaluator(new ManifoldKernel()).Evaluate(doc);

        Assert.False(result.HasErrors);
        var single = new CylinderGenerator().Generate(new Dictionary<string, double> { ["diameter"] = 10, ["height"] = 20, ["segments"] = 32 });
        var cutter = new ManifoldKernel().Boolean(BooleanOperation.Union, [single, single.Transformed(Matrix4x4.CreateTranslation(4, 0, 0))], CancellationToken.None);
        var plate = new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = 200, ["depth"] = 120, ["height"] = 8 });
        var expected = new ManifoldKernel().Boolean(BooleanOperation.Difference, [plate, cutter.Transformed(Matrix4x4.CreateTranslation(0, 0, -5))], CancellationToken.None);
        Assert.Equal(expected.ComputeVolume(), result.Parts.Single(p => p.Kind == PartKind.Solid).Mesh.ComputeVolume(), 2);
    }

    [Fact]
    public void A_merge_of_separate_solids_with_separate_holes_makes_no_union_either()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var parts = new List<Node>();
        for (var i = 0; i < 4; i++)
        {
            var box = new PrimitiveNode("box", $"Box{i}") { Parameters = { ["width"] = "10", ["depth"] = "10", ["height"] = "10" }, Transform = Transform.FromPosition(new Vector3(i * 30, 0, 0)) };
            history.Execute(new AddNodeCommand(doc, doc.Root, box));
            parts.Add(box);
        }

        history.Execute(new WrapNodesCommand(doc, new MergeNode("Merged"), parts));
        var kernel = new CountingKernel(new ManifoldKernel());

        var result = new SceneEvaluator(kernel).Evaluate(doc);

        Assert.False(result.HasErrors);
        Assert.Equal(0, kernel.Unions);
        Assert.Equal(4000, result.Parts.Single(p => p.Kind == PartKind.Solid).Mesh.ComputeVolume(), 2);
    }
}
