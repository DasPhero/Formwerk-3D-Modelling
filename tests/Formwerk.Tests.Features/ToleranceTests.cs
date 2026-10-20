using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Kernel;

namespace Formwerk.Tests.Features;

/// <summary>The print tolerance as a variable (F-HOL-04): changing it recomputes everything that uses it.</summary>
public class ToleranceTests
{
    private readonly FormwerkDocument _doc = new();
    private readonly UndoHistory _history = new();
    private readonly SceneEvaluator _evaluator = new(new ManifoldKernel());

    private double WidthOf(Node node) =>
        _evaluator.Evaluate(_doc).Parts.Where(p => p.Source.Id == node.Id).Max(p => (double)p.Mesh.ComputeBounds().Size.X);

    [Fact]
    public void A_size_with_Tolerance_follows_the_setting_and_does_not_come_from_a_stale_cache()
    {
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = "20+Tolerance", ["depth"] = "20", ["height"] = "20" } };
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, box));
        Assert.Equal(20.2, WidthOf(box), 3);

        var settings = _doc.Settings.Clone();
        settings.Tolerance = 0.5;
        _history.Execute(new SetSettingsCommand(_doc, settings));

        Assert.Equal(20.5, WidthOf(box), 3);

        _history.Undo();
        Assert.Equal(20.2, WidthOf(box), 3);
    }

    [Fact]
    public void The_tolerance_also_reaches_a_hole_in_a_group()
    {
        var group = new GroupNode("G");
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = "20", ["depth"] = "20", ["height"] = "20" } };
        var hole = new PrimitiveNode("hole", "Hole")
        {
            Role = NodeRole.Hole,
            Transform = Transform.FromPosition(new System.Numerics.Vector3(0, 0, 20)),
            Parameters = { ["diameter"] = "3+2*Tolerance", ["depth"] = "30", ["segments"] = "64" },
        };
        _history.Execute(new AddNodeCommand(_doc, _doc.Root, group));
        _history.Execute(new AddNodeCommand(_doc, group, box));
        _history.Execute(new AddNodeCommand(_doc, group, hole));
        double Volume() => _evaluator.Evaluate(_doc).Parts.Where(p => p.Kind == PartKind.Solid).Sum(p => (double)p.Mesh.ComputeVolume());
        var small = 8000 - Volume();

        var settings = _doc.Settings.Clone();
        settings.Tolerance = 0.5; // the hole becomes 4.0 instead of 3.4 wide
        _history.Execute(new SetSettingsCommand(_doc, settings));
        var large = 8000 - Volume();

        Assert.InRange(large / small, Math.Pow(4.0 / 3.4, 2) * 0.99, Math.Pow(4.0 / 3.4, 2) * 1.01);
    }
}
