using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>The pattern node (F-PAT-02, F-PAT-03): a container that repeats its children; count, spacing and skipped instances stay editable.</summary>
public class PatternNodeTests
{
    private static PatternNode Pattern(PatternKind kind = PatternKind.Linear)
    {
        var pattern = new PatternNode("Pattern", kind) { Parameters = { ["count"] = "4", ["spacing"] = "12" } };
        pattern.Skipped.Add(2);
        return pattern;
    }

    private static PrimitiveNode Box() => new("box", "Box") { Parameters = { ["width"] = "10", ["depth"] = "10", ["height"] = "10" } };

    [Fact]
    public void A_pattern_can_hold_children_and_is_a_node_with_parameters()
    {
        var pattern = Pattern();

        Assert.True(pattern.CanHaveChildren);
        Assert.IsAssignableFrom<ParametricNode>(pattern);
        Assert.IsAssignableFrom<ParametricNode>(Box());
        Assert.Equal(PatternKind.Linear, pattern.Kind);
    }

    [Fact]
    public void A_copy_keeps_kind_parameters_skipped_instances_and_children()
    {
        var pattern = Pattern(PatternKind.Circular);
        var doc = new FormwerkDocument();
        new AddNodeCommand(doc, doc.Root, pattern).Do();
        new AddNodeCommand(doc, pattern, Box()).Do();

        var copy = (PatternNode)pattern.Clone(newIds: true);

        Assert.NotEqual(pattern.Id, copy.Id);
        Assert.Equal(PatternKind.Circular, copy.Kind);
        Assert.Equal("4", copy.Parameters["count"]);
        Assert.Equal([2], copy.Skipped.ToArray());
        Assert.Single(copy.Children);
        Assert.NotEqual(pattern.Children[0].Id, copy.Children[0].Id);

        copy.Skipped.Add(3);
        copy.Parameters["count"] = "9";
        Assert.Equal([2], pattern.Skipped.ToArray()); // the copy is independent
        Assert.Equal("4", pattern.Parameters["count"]);
    }

    [Fact]
    public void The_hash_follows_everything_that_changes_the_result_and_nothing_else()
    {
        var pattern = Pattern();
        var hash = pattern.ComputeHash();

        pattern.Name = "Row of holes";
        pattern.Color = 0xFF112233;
        Assert.Equal(hash, pattern.ComputeHash());

        pattern.Parameters["count"] = "5";
        Assert.NotEqual(hash, pattern.ComputeHash());
        pattern.Parameters["count"] = "4";
        Assert.Equal(hash, pattern.ComputeHash());

        pattern.Skipped.Add(3);
        Assert.NotEqual(hash, pattern.ComputeHash());
        pattern.Skipped.Remove(3);
        Assert.Equal(hash, pattern.ComputeHash());

        Assert.NotEqual(hash, Pattern(PatternKind.Circular).ComputeHash());
    }

    [Fact]
    public void The_hash_follows_the_variables_in_the_parameters()
    {
        var pattern = new PatternNode("P", PatternKind.Linear) { Parameters = { ["count"] = "Anzahl" } };

        var a = pattern.ComputeHash(new Dictionary<string, double> { ["Anzahl"] = 3 });
        var b = pattern.ComputeHash(new Dictionary<string, double> { ["Anzahl"] = 4 });

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void The_hash_follows_the_children()
    {
        var doc = new FormwerkDocument();
        var pattern = Pattern();
        new AddNodeCommand(doc, doc.Root, pattern).Do();
        var box = Box();
        new AddNodeCommand(doc, pattern, box).Do();
        var hash = pattern.ComputeHash();

        box.Parameters["width"] = "11";

        Assert.NotEqual(hash, pattern.ComputeHash());
    }

    [Fact]
    public void A_parameter_of_a_pattern_is_changed_by_the_normal_command_and_undone()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var pattern = Pattern();

        history.Execute(new SetParameterCommand(doc, pattern, "count", "7"));
        Assert.Equal("7", pattern.Parameters["count"]);

        history.Undo();
        Assert.Equal("4", pattern.Parameters["count"]);
    }

    [Fact]
    public void Skipped_instances_are_set_by_a_command_and_undone()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var pattern = Pattern();
        var changes = 0;
        doc.Changed += (_, _) => changes++;

        history.Execute(new SetSkippedCommand(doc, pattern, [5, 3, 5]));
        Assert.Equal([3, 5], pattern.Skipped.ToArray());

        history.Undo();
        Assert.Equal([2], pattern.Skipped.ToArray());
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Skipping_knows_when_nothing_changes()
    {
        var doc = new FormwerkDocument();
        var pattern = Pattern();

        Assert.False(new SetSkippedCommand(doc, pattern, [2]).HasEffect);
        Assert.True(new SetSkippedCommand(doc, pattern, [2, 3]).HasEffect);
    }

    [Theory]
    [InlineData("2", new[] { 2 })]
    [InlineData("2, 5", new[] { 2, 5 })]
    [InlineData("5 2 2", new[] { 2, 5 })]
    [InlineData("2-4", new[] { 2, 3, 4 })]
    [InlineData("1,3-4, 9", new[] { 1, 3, 4, 9 })]
    [InlineData("", new int[0])]
    [InlineData("   ", new int[0])]
    public void The_skip_text_is_read_as_numbers_and_ranges(string text, int[] expected)
    {
        var result = PatternNode.ParseSkipped(text, out var error);

        Assert.Null(error);
        Assert.Equal(expected, result.ToArray());
    }

    [Theory]
    [InlineData("a")]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("3-1")]
    [InlineData("2,,x")]
    [InlineData("1.5")]
    [InlineData("1-2-3")]
    [InlineData("999999")]
    public void A_skip_text_that_is_no_list_of_instances_gives_an_error(string text)
    {
        PatternNode.ParseSkipped(text, out var error);

        Assert.NotNull(error);
    }

    [Fact]
    public void The_skip_list_is_written_as_short_as_possible()
    {
        Assert.Equal("", PatternNode.FormatSkipped([]));
        Assert.Equal("2", PatternNode.FormatSkipped([2]));
        Assert.Equal("2, 5", PatternNode.FormatSkipped([5, 2]));
        Assert.Equal("2-4, 7", PatternNode.FormatSkipped([2, 3, 4, 7]));
        Assert.Equal("1, 3", PatternNode.FormatSkipped([1, 3]));
        Assert.Equal("1-2", PatternNode.FormatSkipped([1, 2])); // two in a row
    }

    [Fact]
    public void Writing_and_reading_the_skip_list_gives_the_same_list()
    {
        int[] original = [1, 2, 3, 6, 8, 9, 10, 20];

        var back = PatternNode.ParseSkipped(PatternNode.FormatSkipped(original), out var error);

        Assert.Null(error);
        Assert.Equal(original, back.ToArray());
    }

    [Fact]
    public void Wrapping_nodes_in_a_pattern_is_one_undo_step_with_the_normal_wrap_command()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var box = Box();
        history.Execute(new AddNodeCommand(doc, doc.Root, box));
        var pattern = new PatternNode("Pattern", PatternKind.Linear);

        history.Execute(new WrapNodesCommand(doc, pattern, [box]));
        Assert.Same(pattern, Assert.Single(doc.Root.Children));
        Assert.Same(box, Assert.Single(pattern.Children));

        history.Undo();
        Assert.Same(box, Assert.Single(doc.Root.Children));
    }
}
