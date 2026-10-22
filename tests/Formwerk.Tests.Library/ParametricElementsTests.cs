using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Library;

namespace Formwerk.Tests.Library;

/// <summary>Library elements with parameters: "Box with Lid" with inner size, wall and play (F-PAR-05).</summary>
public class ParametricElementsTests
{
    private static readonly SceneEvaluator Evaluator = new(new ManifoldKernel());

    private static LibraryItem Item(string id) => LibraryCatalog.Find(id)!;

    private static ElementValues Values(LibraryItem item, params (string Name, string Value)[] changes)
    {
        var expressions = item.Parameters!.ToDictionary(p => p.Name, p => p.Default.ToString("R", System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in changes) expressions[name] = value;
        var numbers = expressions.ToDictionary(e => e.Key, e => double.Parse(e.Value, System.Globalization.CultureInfo.InvariantCulture), StringComparer.OrdinalIgnoreCase);
        return new ElementValues(expressions, numbers);
    }

    private static (FormwerkDocument Doc, Node Node) Insert(LibraryItem item, ElementValues values)
    {
        var doc = new FormwerkDocument();
        var node = item.CreateNode(values);
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));
        return (doc, node);
    }

    private static Bounds BoundsOf(FormwerkDocument doc, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        var result = Evaluator.Evaluate(doc);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        var parts = result.Parts.Where(p => p.Kind == PartKind.Solid && ids.Contains(p.Source.Id) && !p.Mesh.IsEmpty).Select(p => p.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static double VolumeOf(FormwerkDocument doc, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        return Evaluator.Evaluate(doc).Parts.Where(p => p.Kind == PartKind.Solid && ids.Contains(p.Source.Id)).Sum(p => p.Mesh.ComputeVolume());
    }

    [Fact]
    public void The_parametric_elements_have_parameters_with_valid_ranges_and_defaults()
    {
        var parametric = LibraryCatalog.Items.Where(i => i.IsParametric).ToList();

        Assert.Contains(parametric, i => i.Id == "box-with-lid");
        Assert.True(parametric.Count >= 3);
        Assert.All(parametric, i =>
        {
            Assert.All(i.Parameters!, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Label));
                Assert.InRange(p.Default, p.Min, p.Max);
            });
            Assert.Equal(i.Parameters!.Count, i.Parameters.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.NotNull(i.ParametricRecipe);
            Assert.NotNull(i.FootprintFor);
        });
        Assert.All(LibraryCatalog.Items.Where(i => !i.IsParametric), i => Assert.Null(i.ParametricRecipe));
    }

    [Fact]
    public void A_box_with_an_inner_size_of_50_30_20_and_a_wall_of_2_is_54_34_22_outside()
    {
        var item = Item("box-with-lid");
        var (doc, node) = Insert(item, Values(item, ("innerWidth", "50"), ("innerDepth", "30"), ("innerHeight", "20"), ("wall", "2")));
        var box = node.Descendants().Single(n => n.Name == "Box");

        var size = BoundsOf(doc, box).Size;

        Assert.Equal(54, size.X, 2);
        Assert.Equal(34, size.Y, 2);
        Assert.Equal(22, size.Z, 2);
    }

    [Fact]
    public void Other_values_give_other_sizes()
    {
        var item = Item("box-with-lid");
        var (doc, node) = Insert(item, Values(item, ("innerWidth", "40"), ("innerDepth", "20"), ("innerHeight", "10"), ("wall", "3")));

        var size = BoundsOf(doc, node.Descendants().Single(n => n.Name == "Box")).Size;

        Assert.Equal(46, size.X, 2);
        Assert.Equal(26, size.Y, 2);
        Assert.Equal(13, size.Z, 2);
    }

    [Fact]
    public void The_lid_has_a_lip_that_is_smaller_than_the_inside_by_the_play()
    {
        var item = Item("box-with-lid");
        var (doc, node) = Insert(item, Values(item, ("play", "0.3")));
        var lid = node.Descendants().Single(n => n.Name == "Lid");

        var volume = VolumeOf(doc, lid);

        var plate = 54.0 * 34 * 2;
        var lip = (50 - 0.6) * (30 - 0.6) * 2;
        Assert.Equal(plate + lip, volume, (plate + lip) * 0.001);
    }

    [Fact]
    public void The_box_and_the_lid_lie_side_by_side_and_the_whole_is_centered_with_the_declared_size()
    {
        var item = Item("box-with-lid");
        var values = Values(item, ("innerWidth", "40"), ("innerDepth", "20"), ("innerHeight", "10"));
        var (doc, node) = Insert(item, values);

        var bounds = BoundsOf(doc, node);
        var footprint = item.FootprintFor!(values);

        Assert.Equal(footprint.X, bounds.Size.X, 2);
        Assert.Equal(footprint.Y, bounds.Size.Y, 2);
        Assert.Equal(footprint.Z, bounds.Size.Z, 2);
        Assert.Equal(0, bounds.Center.X, 2);
        Assert.Equal(0, bounds.Center.Y, 2);
        Assert.Equal(0, bounds.Min.Z, 2);
        var box = BoundsOf(doc, node.Descendants().Single(n => n.Name == "Box"));
        var lid = BoundsOf(doc, node.Descendants().Single(n => n.Name == "Lid"));
        Assert.True(box.Max.X < lid.Min.X);
    }

    [Fact]
    public void A_project_variable_in_a_value_is_followed_when_the_variable_changes()
    {
        var item = Item("box-with-lid");
        var values = new ElementValues(
            Values(item).Expressions.ToDictionary(e => e.Key, e => e.Key == "wall" ? "Wandstaerke" : e.Value, StringComparer.OrdinalIgnoreCase),
            Values(item, ("wall", "3")).Numbers);
        var doc = new FormwerkDocument();
        doc.Variables["Wandstaerke"] = "3";
        var node = item.CreateNode(values);
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));
        var box = node.Descendants().Single(n => n.Name == "Box");

        Assert.Equal(56, BoundsOf(doc, box).Size.X, 2);

        doc.Variables["Wandstaerke"] = "4";
        Assert.Equal(58, BoundsOf(doc, box).Size.X, 2);
    }

    [Fact]
    public void The_defaults_make_the_same_element_as_the_default_recipe()
    {
        foreach (var item in LibraryCatalog.Items.Where(i => i.IsParametric))
        {
            var a = item.CreateNode();
            var b = item.CreateNode(Values(item));

            Assert.Equal(a.Descendants().Count(), b.Descendants().Count());
            Assert.Equal(a.Name, b.Name);
        }
    }

    [Fact]
    public void A_spacer_sleeve_takes_its_wall_from_the_two_diameters()
    {
        var item = Item("spacer-sleeve");
        var (doc, node) = Insert(item, Values(item, ("height", "12"), ("outerDiameter", "8"), ("holeDiameter", "3")));

        var volume = VolumeOf(doc, node);

        Assert.Equal(Math.PI * (4 * 4 - 1.5 * 1.5) * 12, volume, Math.PI * (16 - 2.25) * 12 * 0.01);
        Assert.Equal(12, BoundsOf(doc, node).Size.Z, 2);
    }

    [Fact]
    public void A_mounting_plate_has_its_four_holes_the_margin_away_from_the_edges()
    {
        var item = Item("mounting-plate");
        var values = Values(item, ("width", "80"), ("depth", "50"), ("thickness", "4"), ("holeDiameter", "5"), ("margin", "10"));
        var (doc, node) = Insert(item, values);

        var volume = VolumeOf(doc, node);
        var size = BoundsOf(doc, node).Size;

        Assert.Equal(80 * 50 * 4 - 4 * Math.PI * 2.5 * 2.5 * 4, volume, 80 * 50 * 4 * 0.01);
        Assert.Equal(80, size.X, 2);
        Assert.Equal(50, size.Y, 2);
        var holes = node.Descendants().Where(n => n.Role == NodeRole.Hole || n.Name.StartsWith("Hole")).ToList();
        Assert.Equal(4, holes.Count);
        Assert.All(holes, h => Assert.Equal(30, Math.Abs(h.Transform.Position.X), 3)); // 40 - margin 10
        Assert.All(holes, h => Assert.Equal(15, Math.Abs(h.Transform.Position.Y), 3)); // 25 - margin 10
    }
}
