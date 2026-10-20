using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;

namespace Formwerk.Tests.Core;

/// <summary>Project variables (F-PAR-03): which names an expression uses, order independent resolving, name rules, the command.</summary>
public class VariableTests
{
    [Theory]
    [InlineData("Wandstaerke*2", new[] { "Wandstaerke" })]
    [InlineData("Schraube + Toleranz", new[] { "Schraube", "Toleranz" })]
    [InlineData("min(A, B) + max(3, C)", new[] { "A", "B", "C" })]
    [InlineData("round(Breite / 3, 1)", new[] { "Breite" })]
    [InlineData("A + a + A", new[] { "A" })] // once, ignoring case
    [InlineData("20", new string[0])]
    [InlineData("2.5*3", new string[0])]
    [InlineData("_x1 + y_2", new[] { "_x1", "y_2" })]
    [InlineData("", new string[0])]
    [InlineData("(A+", new[] { "A" })] // a broken expression still shows what it refers to
    public void UsedNames_lists_the_variables_of_an_expression(string text, string[] expected) =>
        Assert.Equal(expected, ExpressionParser.UsedNames(text));

    [Fact]
    public void UsedNames_of_null_is_empty() => Assert.Empty(ExpressionParser.UsedNames(null));

    [Fact]
    public void A_variable_may_use_one_that_comes_later_in_the_table()
    {
        var doc = new FormwerkDocument();
        doc.Variables["Aussen"] = "Innen + 2*Wand";
        doc.Variables["Innen"] = "50";
        doc.Variables["Wand"] = "2";

        var values = doc.ResolveVariables(out var errors);

        Assert.Empty(errors);
        Assert.Equal(54, values["Aussen"]);
    }

    [Fact]
    public void A_chain_in_reverse_order_resolves()
    {
        var doc = new FormwerkDocument();
        doc.Variables["D"] = "C+1";
        doc.Variables["C"] = "B+1";
        doc.Variables["B"] = "A+1";
        doc.Variables["A"] = "1";

        var values = doc.ResolveVariables(out var errors);

        Assert.Empty(errors);
        Assert.Equal(4, values["D"]);
    }

    [Fact]
    public void An_unknown_name_is_reported_for_that_variable_and_its_users()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "Fehlt + 1";
        doc.Variables["B"] = "A * 2";
        doc.Variables["C"] = "5";

        var values = doc.ResolveVariables(out var errors);

        Assert.Contains("Fehlt", errors["A"]);
        Assert.True(errors.ContainsKey("B"));
        Assert.False(errors.ContainsKey("C"));
        Assert.Equal(5, values["C"]);
        Assert.False(values.ContainsKey("A"));
    }

    [Fact]
    public void A_circular_reference_is_reported_and_does_not_hang()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "B + 1";
        doc.Variables["B"] = "A + 1";
        doc.Variables["C"] = "3";

        var values = doc.ResolveVariables(out var errors);

        Assert.Contains("ircular", errors["A"]);
        Assert.Contains("ircular", errors["B"]);
        Assert.Equal(3, values["C"]);
    }

    [Fact]
    public void A_variable_that_refers_to_itself_is_circular()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "A + 1";

        doc.ResolveVariables(out var errors);

        Assert.Contains("ircular", errors["A"]);
    }

    [Fact]
    public void A_syntax_error_in_one_variable_leaves_the_others_alone()
    {
        var doc = new FormwerkDocument();
        doc.Variables["Kaputt"] = "2 +* 3";
        doc.Variables["Gut"] = "7";

        var values = doc.ResolveVariables(out var errors);

        Assert.True(errors.ContainsKey("Kaputt"));
        Assert.Equal(7, values["Gut"]);
    }

    [Fact]
    public void Names_are_found_ignoring_case()
    {
        var doc = new FormwerkDocument();
        doc.Variables["Wand"] = "2";
        doc.Variables["Dick"] = "WAND*2";

        var values = doc.ResolveVariables(out var errors);

        Assert.Empty(errors);
        Assert.Equal(4, values["dick"]);
    }

    [Theory]
    [InlineData("Wandstaerke", true)]
    [InlineData("_hidden", true)]
    [InlineData("a1", true)]
    [InlineData("Größe", true)]
    [InlineData("", false)]
    [InlineData("  ", false)]
    [InlineData("1abc", false)]
    [InlineData("a b", false)]
    [InlineData("a-b", false)]
    [InlineData("a.b", false)]
    [InlineData("min", false)]
    [InlineData("MAX", false)]
    [InlineData("Round", false)]
    public void Names_follow_the_rules_of_the_expression_parser(string name, bool valid)
    {
        var error = VariableNames.Check(name, []);

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void A_name_that_exists_is_refused_ignoring_case_but_the_own_name_is_fine()
    {
        string[] existing = ["Wand", "Breite"];

        Assert.NotNull(VariableNames.Check("wand", existing));
        Assert.Null(VariableNames.Check("Tiefe", existing));
        Assert.Null(VariableNames.Check("Wand", existing, except: "Wand")); // renaming to the same name or only its case
        Assert.Null(VariableNames.Check("WAND", existing, except: "Wand"));
        Assert.NotNull(VariableNames.Check("Breite", existing, except: "Wand"));
    }

    [Fact]
    public void A_name_is_too_long_after_64_characters()
    {
        Assert.Null(VariableNames.Check(new string('a', 64), []));
        Assert.NotNull(VariableNames.Check(new string('a', 65), []));
    }

    [Fact]
    public void Whole_words_are_replaced_when_a_variable_is_renamed()
    {
        Assert.Equal("Dicke*2 + Dicke", VariableNames.ReplaceName("Wand*2 + wand", "Wand", "Dicke"));
        Assert.Equal("Wandstaerke + Wand2 + x_Wand", VariableNames.ReplaceName("Wandstaerke + Wand2 + x_Wand", "Wand", "Dicke")); // parts of other names stay
        Assert.Equal("min(Dicke, 3)", VariableNames.ReplaceName("min(Wand, 3)", "Wand", "Dicke"));
        Assert.Equal("20", VariableNames.ReplaceName("20", "Wand", "Dicke"));
    }

    [Fact]
    public void A_function_with_the_name_of_a_variable_is_not_replaced()
    {
        // a name that is called like a function is a function, not the variable
        Assert.Equal("Wand(3)", VariableNames.ReplaceName("Wand(3)", "Wand", "Dicke"));
        Assert.Equal("Wand(3) + Dicke", VariableNames.ReplaceName("Wand(3) + Wand", "Wand", "Dicke"));
    }


    // ---- the built-in variable Tolerance (F-HOL-04) ----

    [Fact]
    public void Tolerance_is_a_variable_in_every_expression_with_the_value_of_the_settings()
    {
        var doc = new FormwerkDocument();
        doc.Settings.Tolerance = 0.3;
        doc.Variables["Loch"] = "3 + 2*Tolerance";

        var values = doc.ResolveVariables(out var errors);

        Assert.Empty(errors);
        Assert.Equal(0.3, values["Tolerance"]);
        Assert.Equal(3.6, values["Loch"], 9);
    }

    [Fact]
    public void Tolerance_is_found_ignoring_case()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "TOLERANCE * 10";

        var values = doc.ResolveVariables(out var errors);

        Assert.Empty(errors);
        Assert.Equal(2, values["A"], 9); // the default tolerance is 0.2
    }

    [Fact]
    public void Changing_the_tolerance_changes_the_hash_of_the_nodes_that_use_it_and_of_no_other()
    {
        var doc = new FormwerkDocument();
        var linked = new PrimitiveNode("cylinder", "Linked") { Parameters = { ["diameter"] = "3+Tolerance", ["height"] = "10" } };
        var plain = new PrimitiveNode("cylinder", "Plain") { Parameters = { ["diameter"] = "3", ["height"] = "10" } };
        var before = (linked.ComputeHash(doc.ResolveVariables(out _)), plain.ComputeHash(doc.ResolveVariables(out _)));

        doc.Settings.Tolerance = 0.4;
        var values = doc.ResolveVariables(out _);

        Assert.NotEqual(before.Item1, linked.ComputeHash(values));
        Assert.Equal(before.Item2, plain.ComputeHash(values));
    }

    [Fact]
    public void A_variable_may_not_have_the_name_of_a_built_in_one()
    {
        Assert.NotNull(VariableNames.Check("Tolerance", []));
        Assert.NotNull(VariableNames.Check("tolerance", []));
        Assert.Contains("built in", VariableNames.Check("Tolerance", [])!);
        Assert.Null(VariableNames.Check("Tolerance2", []));
    }

    [Fact]
    public void A_file_with_a_variable_called_Tolerance_gets_an_error_for_it_and_the_setting_still_wins()
    {
        var doc = new FormwerkDocument();
        doc.Variables["Tolerance"] = "5";
        doc.Variables["A"] = "Tolerance * 10";

        var values = doc.ResolveVariables(out var errors);

        Assert.Contains("built in", errors["Tolerance"]);
        Assert.False(errors.ContainsKey("A"));
        Assert.Equal(0.2, values["Tolerance"]);
        Assert.Equal(2, values["A"], 9);
    }

    // ---- the command ----

    private static List<KeyValuePair<string, string>> Table(params (string, string)[] items) =>
        [.. items.Select(i => new KeyValuePair<string, string>(i.Item1, i.Item2))];

    [Fact]
    public void SetVariables_replaces_the_table_and_undo_brings_it_back_in_the_same_order()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "1";
        doc.Variables["B"] = "2";
        var history = new UndoHistory();

        history.Execute(new SetVariablesCommand(doc, Table(("C", "3"), ("B", "2"), ("A", "9")), "Change variables"));
        Assert.Equal(["C", "B", "A"], doc.Variables.Keys.ToArray());
        Assert.Equal("9", doc.Variables["A"]);

        history.Undo();
        Assert.Equal(["A", "B"], doc.Variables.Keys.ToArray());
        Assert.Equal("1", doc.Variables["A"]);

        history.Redo();
        Assert.Equal(["C", "B", "A"], doc.Variables.Keys.ToArray());
    }

    [Fact]
    public void SetVariables_keeps_the_order_also_after_deleting_and_adding()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new SetVariablesCommand(doc, Table(("A", "1"), ("B", "2"), ("C", "3")), "Set"));
        history.Execute(new SetVariablesCommand(doc, Table(("A", "1"), ("C", "3")), "Delete B"));
        history.Execute(new SetVariablesCommand(doc, Table(("A", "1"), ("C", "3"), ("D", "4")), "Add D"));

        Assert.Equal(["A", "C", "D"], doc.Variables.Keys.ToArray());
    }

    [Fact]
    public void SetVariables_tells_the_document_that_it_changed_and_undo_does_too()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        var changes = 0;
        doc.Changed += (_, _) => changes++;

        history.Execute(new SetVariablesCommand(doc, Table(("A", "1")), "Add"));
        history.Undo();

        Assert.Equal(2, changes);
    }

    [Fact]
    public void SetVariables_knows_when_nothing_changes()
    {
        var doc = new FormwerkDocument();
        doc.Variables["A"] = "1";

        Assert.False(new SetVariablesCommand(doc, Table(("A", "1")), "Same").HasEffect);
        Assert.True(new SetVariablesCommand(doc, Table(("A", "2")), "Other value").HasEffect);
        Assert.True(new SetVariablesCommand(doc, Table(("a", "1")), "Other case").HasEffect);
        Assert.True(new SetVariablesCommand(doc, Table(("A", "1"), ("B", "1")), "More").HasEffect);
    }

    [Fact]
    public void A_variable_change_updates_every_linked_node_in_one_evaluation_run()
    {
        var doc = new FormwerkDocument();
        var history = new UndoHistory();
        history.Execute(new SetVariablesCommand(doc, Table(("Wandstaerke", "2")), "Add"));
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = "Wandstaerke*10", ["depth"] = "20", ["height"] = "Wandstaerke*5" } };
        var other = new PrimitiveNode("box", "Other") { Parameters = { ["width"] = "30", ["depth"] = "20", ["height"] = "10" } };
        var before = (box.ComputeHash(doc.ResolveVariables(out _)), other.ComputeHash(doc.ResolveVariables(out _)));

        history.Execute(new SetVariablesCommand(doc, Table(("Wandstaerke", "3")), "Change"));
        var values = doc.ResolveVariables(out _);

        Assert.NotEqual(before.Item1, box.ComputeHash(values));
        Assert.Equal(before.Item2, other.ComputeHash(values));
        Assert.Equal(30, box.EvaluateParameters(values, out _)["width"]);
    }
}
