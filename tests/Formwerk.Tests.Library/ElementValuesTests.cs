using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Library;

namespace Formwerk.Tests.Library;

/// <summary>Values of library elements with parameters, read again from the text saved with an object, and the cable clip (F-PAR-05).</summary>
public class ElementValuesTests
{
    private static readonly SceneEvaluator Evaluator = new(new ManifoldKernel());

    private static ElementValues Values(params (string Name, string Text)[] given)
    {
        var item = LibraryCatalog.Find("cable-clip")!;
        var saved = given.ToDictionary(g => g.Name, g => g.Text, StringComparer.OrdinalIgnoreCase);
        return ElementValues.Resolve(item.Parameters!, saved, new Dictionary<string, double>(), out var error)
               ?? throw new InvalidOperationException(error);
    }

    private static double VolumeOf(Node node)
    {
        var doc = new FormwerkDocument();
        new UndoHistory().Execute(new AddNodeCommand(doc, doc.Root, node));
        var result = Evaluator.Evaluate(doc);
        Assert.False(result.HasErrors, string.Join("; ", result.Errors.Select(e => e.Message)));
        return result.Parts.Where(p => p.Kind == PartKind.Solid).Sum(p => p.Mesh.ComputeVolume());
    }

    [Fact]
    public void Missing_values_take_the_default_and_the_text_is_kept()
    {
        var values = Values(("cable", "10"), ("wall", " 3 "));

        Assert.Equal(10, values.N("cable"));
        Assert.Equal("3", values.Expressions["wall"]);
        Assert.Equal(8, values.N("height")); // default
        Assert.Equal(3.4, values.N("screw"));
    }

    [Fact]
    public void A_formula_or_variable_is_evaluated_and_kept_as_text()
    {
        var item = LibraryCatalog.Find("cable-clip")!;
        var saved = new Dictionary<string, string> { ["cable"] = "Kabel*2", ["wall"] = "1+1" };

        var values = ElementValues.Resolve(item.Parameters!, saved, new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Kabel"] = 5 }, out var error)!;

        Assert.Null(error);
        Assert.Equal(10, values.N("cable"));
        Assert.Equal("Kabel*2", values.Expressions["cable"]);
        Assert.Equal(2, values.N("wall"));
    }

    [Theory]
    [InlineData("cable", "1")] // below the minimum
    [InlineData("cable", "5000")] // above the maximum
    [InlineData("cable", "abc")]
    [InlineData("wall", "Unknown*2")]
    public void A_value_out_of_range_or_not_working_gives_an_error_and_no_values(string name, string text)
    {
        var item = LibraryCatalog.Find("cable-clip")!;

        var values = ElementValues.Resolve(item.Parameters!, new Dictionary<string, string> { [name] = text }, new Dictionary<string, double>(), out var error);

        Assert.Null(values);
        Assert.False(string.IsNullOrEmpty(error));
    }

    private static double ExpectedClipVolume(double cable, double wall = 2, double height = 8, double baseThickness = 2, double screw = 3.4, double gap = 1.5)
    {
        var w = cable + 2 * wall + 2 * gap + 2 * screw + 4;
        var d = Math.Max(cable + 2 * wall, screw + 4);
        var plate = (w * d - Math.PI * cable * cable / 4) * baseThickness;
        var ring = Math.PI * ((cable / 2 + wall) * (cable / 2 + wall) - cable * cable / 4) * (height - baseThickness);
        return plate + ring - 2 * Math.PI * screw * screw / 4 * baseThickness;
    }

    [Theory]
    [InlineData("8")]
    [InlineData("14")]
    [InlineData("25")]
    public void The_cable_clip_has_the_volume_of_a_ring_on_a_plate_with_two_screw_holes_of_the_same_size(string cable)
    {
        var item = LibraryCatalog.Find("cable-clip")!;
        var node = item.CreateNode(Values(("cable", cable)));

        var volume = VolumeOf(node);

        var expected = ExpectedClipVolume(double.Parse(cable, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(expected, volume, expected * 0.02);
    }

    [Fact]
    public void The_converted_elements_keep_their_old_default_shapes()
    {
        static Node Default(string id) { var item = LibraryCatalog.Find(id)!; return item.CreateNode(ElementValues.Defaults(item.Parameters!)); }

        Assert.Equal(60 * 40 * 25 - 56 * 36 * 23.0, VolumeOf(Default("open-box")), 1);
        Assert.Equal(Math.PI * (20 * 20 * 20 - 18 * 18 * 18.0), VolumeOf(Default("round-box")), Math.PI * 20 * 20 * 20 * 0.01);
        Assert.Equal(40 * 25 * 3 + 40 * 3 * 27.0 - 2 * Math.PI * 4 * 3, VolumeOf(Default("l-bracket")), 40 * 25 * 3 * 0.02);
        Assert.Equal(30 * 20 * 20 - 24 * 20 * 17.0, VolumeOf(Default("u-bracket")), 1);
    }
}
