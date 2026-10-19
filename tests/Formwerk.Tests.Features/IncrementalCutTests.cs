using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>
/// Dragging one hole of a plate with many holes (F-BOO-03, Q-03): after the second step of a drag the plate without the other holes
/// is remembered, so a step only has to cut the moved hole. The result must always equal a calculation from scratch.
/// </summary>
public class IncrementalCutTests
{
    private sealed class CountingKernel(IGeometryKernel inner) : IGeometryKernel
    {
        public int Differences { get; private set; }

        public int Unions { get; private set; }

        public string Name => inner.Name;

        public MeshData Boolean(BooleanOperation operation, IReadOnlyList<MeshData> operands, CancellationToken cancellationToken)
        {
            if (operation == BooleanOperation.Union) Unions++;
            else Differences++;
            return inner.Boolean(operation, operands, cancellationToken);
        }
    }

    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private PrimitiveNode _plate = null!;
    private GroupNode _group = null!;
    private readonly List<PrimitiveNode> _holes = [];

    private void Build(int holeCount)
    {
        _plate = new PrimitiveNode("box", "Plate") { Parameters = { ["width"] = "200", ["depth"] = "120", ["height"] = "8" } };
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, _plate));
        _group = new GroupNode("Plate with holes");
        _history.Execute(new WrapNodesCommand(_doc, _group, [_plate]));
        for (var i = 0; i < holeCount; i++)
        {
            var hole = new PrimitiveNode("cylinder", $"Hole{i}")
            {
                Role = NodeRole.Hole,
                Parameters = { ["diameter"] = "8", ["height"] = "20", ["segments"] = "32" },
                Transform = Transform.FromPosition(new Vector3(-90 + i % 10 * 20, -50 + i / 10 * 20, -5)),
            };
            _history.Execute(new AddNodeCommand(_doc, _group, hole));
            _holes.Add(hole);
        }
    }

    private void MoveHole(int index, float x, float y) =>
        _history.Execute(new SetTransformCommand(_doc, _holes[index], Transform.FromPosition(new Vector3(x, y, -5))));

    private static double Body(EvaluationResult result) => result.Parts.Where(p => p.Kind == PartKind.Solid).Sum(p => p.Mesh.ComputeVolume());

    /// <summary>The same document calculated from scratch by a new evaluator that has no memory.</summary>
    private double Fresh() => Body(new SceneEvaluator(new ManifoldKernel()).Evaluate(_doc));

    [Fact]
    public void A_drag_of_one_hole_gives_the_same_body_as_a_calculation_from_scratch_at_every_step()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);

        for (var step = 0; step < 6; step++)
        {
            MoveHole(0, -90 + step * 3, -50 + step * 2);
            var result = evaluator.Evaluate(_doc);

            Assert.False(result.HasErrors);
            Assert.Equal(Fresh(), Body(result), 3);
        }
    }

    [Fact]
    public void From_the_third_step_a_step_costs_one_cut_and_no_union()
    {
        Build(12);
        var kernel = new CountingKernel(new ManifoldKernel());
        var evaluator = new SceneEvaluator(kernel);
        evaluator.Evaluate(_doc);

        var calls = new List<int>();
        for (var step = 0; step < 5; step++)
        {
            var before = kernel.Differences;
            MoveHole(0, -90 + step * 3, -50);
            evaluator.Evaluate(_doc);
            calls.Add(kernel.Differences - before);
        }

        Assert.Equal(1, calls[3]);
        Assert.Equal(1, calls[4]);
        Assert.Equal(0, kernel.Unions); // all holes are apart
    }

    [Fact]
    public void Moving_another_hole_starts_again_and_is_still_right()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, -90 + step * 2, -50);
            evaluator.Evaluate(_doc);
        }

        for (var step = 0; step < 4; step++)
        {
            MoveHole(5, -10 + step * 2, -50 + 20);
            var result = evaluator.Evaluate(_doc);

            Assert.Equal(Fresh(), Body(result), 3);
        }
    }

    [Fact]
    public void A_moved_hole_that_overlaps_another_one_cuts_the_union_of_both()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);

        // hole 0 moves towards hole 1 (20 mm to the right) and over it
        for (var step = 0; step < 6; step++)
        {
            MoveHole(0, -90 + step * 4, -50);
            var result = evaluator.Evaluate(_doc);

            Assert.Equal(Fresh(), Body(result), 3);
        }
    }

    [Fact]
    public void Changing_the_plate_in_the_middle_of_a_drag_is_taken_into_account()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, -90 + step * 2, -50);
            evaluator.Evaluate(_doc);
        }

        _plate.Parameters["height"] = "5";
        _doc.NotifyChanged();
        var result = evaluator.Evaluate(_doc);
        Assert.Equal(Fresh(), Body(result), 3);

        MoveHole(0, -80, -50);
        Assert.Equal(Fresh(), Body(evaluator.Evaluate(_doc)), 3);
    }

    [Fact]
    public void Changing_the_size_of_the_moved_hole_or_adding_and_removing_holes_is_right_too()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, -90 + step * 2, -50);
            evaluator.Evaluate(_doc);
        }

        _holes[0].Parameters["diameter"] = "14";
        _doc.NotifyChanged();
        Assert.Equal(Fresh(), Body(evaluator.Evaluate(_doc)), 3);

        var extra = new PrimitiveNode("cylinder", "Extra") { Role = NodeRole.Hole, Parameters = { ["diameter"] = "6", ["height"] = "20", ["segments"] = "32" }, Transform = Transform.FromPosition(new Vector3(0, 40, -5)) };
        _history.Execute(new AddNodeCommand(_doc, _group, extra));
        Assert.Equal(Fresh(), Body(evaluator.Evaluate(_doc)), 3);

        _history.Execute(new RemoveNodeCommand(_doc, _holes[3]));
        Assert.Equal(Fresh(), Body(evaluator.Evaluate(_doc)), 3);
    }

    [Fact]
    public void Undo_in_the_middle_of_a_drag_gives_the_old_body_back()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        var start = Body(evaluator.Evaluate(_doc));
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, -90 + step * 2, -50);
            evaluator.Evaluate(_doc);
        }

        for (var i = 0; i < 4; i++) _history.Undo();

        Assert.Equal(start, Body(evaluator.Evaluate(_doc)), 3);
    }

    [Fact]
    public void The_ghosts_of_all_holes_stay_in_the_result()
    {
        Build(12);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);
        EvaluationResult? result = null;
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, -90 + step * 2, -50);
            result = evaluator.Evaluate(_doc);
        }

        Assert.Equal(12, result!.Parts.Count(p => p.Kind == PartKind.Ghost));
        Assert.Single(result.Parts, p => p.Kind == PartKind.Solid);
    }

    [Fact]
    public void A_hole_that_misses_the_plate_changes_nothing()
    {
        Build(3);
        var evaluator = new SceneEvaluator(new ManifoldKernel());
        evaluator.Evaluate(_doc);
        for (var step = 0; step < 4; step++)
        {
            MoveHole(0, 500 + step, 0); // far off the plate
            var result = evaluator.Evaluate(_doc);
            Assert.Equal(Fresh(), Body(result), 3);
        }
    }
}
