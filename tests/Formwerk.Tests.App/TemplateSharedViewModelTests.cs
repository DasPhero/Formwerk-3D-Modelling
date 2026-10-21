using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Shared parameters of a template: chosen when saving, asked when inserting (F-TPL-06).</summary>
public sealed class TemplateSharedViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-tplshared-vm-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private (MainViewModel Vm, FakeDialogs Dialogs) NewViewModel()
    {
        var dialogs = new FakeDialogs();
        return (new MainViewModel(new ManifoldKernel(), dialogs, templates: new TemplateStore(_folder)), dialogs);
    }

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = vm.Document.Root.Children[^1];
        vm.SelectOnly([node.Id]);
        return node;
    }

    private static SharedParameterChoice Choice(SaveTemplateViewModel d, string parameter, string? nodeName = null) =>
        d.SharedChoices.Single(c => c.Parameter == parameter && (nodeName is null || c.NodeName == nodeName));

    /// <summary>Saves the selection as "Part"; <paramref name="choose"/> ticks the shared parameters in the dialog.</summary>
    private static TemplateItemViewModel Save(MainViewModel vm, FakeDialogs dialogs, Action<SaveTemplateViewModel> choose, string name = "Part")
    {
        dialogs.SaveTemplateScript = d =>
        {
            d.Name = name;
            choose(d);
            return true;
        };
        vm.SaveTemplateCommand.Execute(null);
        return vm.Templates.Single(t => t.Name == name);
    }

    private static double WidthOf(MainViewModel vm, Node node) =>
        vm.ViewportItems.Where(i => i.NodeId == node.Id).Max(i => (double)i.Mesh.ComputeBounds().Size.X);

    /// <summary>Puts <paramref name="child"/> into <paramref name="parent"/> (only the structure matters, so a scratch document does).</summary>
    private static void Put(Node parent, Node child) => new Formwerk.Core.Commands.AddNodeCommand(new FormwerkDocument(), parent, child).Do();
    // ---- the choices in the dialog --------------------------------------------------------------------

    [Fact]
    public void A_box_offers_its_dimensions_and_none_is_ticked()
    {
        var choices = SharedParameterChoice.For([new PrimitiveNode("box", "Box")]);

        Assert.Equal(["Width", "Depth", "Height", "Edge size"], choices.Select(c => c.ParameterLabel).ToArray());
        Assert.Equal(["width", "depth", "height", "edgeSize"], choices.Select(c => c.Parameter).ToArray());
        Assert.All(choices, c => Assert.False(c.IsShared));
        Assert.All(choices, c => Assert.Equal([0], c.Path));
        Assert.All(choices, c => Assert.Equal("Box", c.NodeName));
    }

    [Fact]
    public void Choice_like_parameters_are_not_offered()
    {
        var hole = SharedParameterChoice.For([new PrimitiveNode(ShapeLibrary.Hole.Id, "Hole")]).Select(c => c.Parameter).ToList();
        var pattern = SharedParameterChoice.For([new PatternNode("Row", PatternKind.Linear)]).Select(c => c.Parameter).ToList();

        Assert.Contains("diameter", hole);
        Assert.Contains("depth", hole);
        Assert.DoesNotContain("type", hole); // a list of kinds, not a number
        Assert.DoesNotContain("through", hole);
        Assert.Contains("count", pattern);
        Assert.Contains("spacing", pattern);
        Assert.DoesNotContain("axis", pattern);
        Assert.DoesNotContain("totalLength", pattern);
    }

    [Fact]
    public void Objects_inside_groups_are_offered_with_their_path()
    {
        var group = new GroupNode("Group");
        var inner = new GroupNode("Inner");
        var cylinder = new PrimitiveNode("cylinder", "Cylinder");
        Put(group, new PrimitiveNode("box", "Box"));
        Put(group, inner);
        Put(inner, cylinder);

        var choices = SharedParameterChoice.For([new PrimitiveNode("sphere", "Sphere"), group]);

        Assert.Equal([0], choices.First(c => c.NodeName == "Sphere").Path);
        Assert.Equal([1, 0], choices.First(c => c.NodeName == "Box").Path);
        Assert.Equal([1, 1, 0], choices.First(c => c.NodeName == "Cylinder").Path);
        Assert.DoesNotContain(choices, c => c.NodeName is "Group" or "Inner"); // groups have no parameters
    }

    [Fact]
    public void A_label_that_two_objects_have_gets_the_object_name_in_front()
    {
        var group = new GroupNode("Group");
        Put(group, new PrimitiveNode("box", "Plate"));
        Put(group, new PrimitiveNode("cylinder", "Pin"));

        var choices = SharedParameterChoice.For([group]);

        Assert.Equal("Plate: Height", choices.First(c => c.NodeName == "Plate" && c.Parameter == "height").Label);
        Assert.Equal("Pin: Height", choices.First(c => c.NodeName == "Pin" && c.Parameter == "height").Label);
        Assert.Equal("Width", choices.First(c => c.Parameter == "width").Label); // only the box has a width
        Assert.Equal("Diameter", choices.First(c => c.Parameter == "diameter").Label);
    }

    [UiFact]
    public async Task The_save_dialog_offers_the_parameters_of_the_selection()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        SaveTemplateViewModel? seen = null;
        dialogs.SaveTemplateScript = d =>
        {
            seen = d;
            return false;
        };

        vm.SaveTemplateCommand.Execute(null);

        Assert.True(seen!.HasSharedChoices);
        Assert.Equal(4, seen.SharedChoices.Count);
        Assert.True(seen.IsValid);
    }

    // ---- ticking and saving ---------------------------------------------------------------------------

    [UiFact]
    public async Task Ticked_parameters_are_stored_with_their_label_and_path()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");

        var item = Save(vm, dialogs, d =>
        {
            Choice(d, "height").IsShared = true;
            Choice(d, "width").IsShared = true;
            Choice(d, "width").Label = "  Length   of the   plate ";
        });

        var shared = item.Info.Shared!;
        Assert.Equal(2, shared.Count);
        Assert.Equal(["width", "height"], shared.Select(s => s.Parameter).ToArray()); // in the order of the list, not of ticking
        Assert.Equal("Length of the plate", shared[0].Label);
        Assert.Equal([0], shared[0].Path);
        Assert.Equal("Height", shared[1].Label);
    }

    [UiFact]
    public async Task Without_a_tick_nothing_is_shared_and_inserting_asks_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = Save(vm, dialogs, _ => { });
        var asked = 0;
        dialogs.TemplateParametersScript = _ =>
        {
            asked++;
            return true;
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.Empty(item.Info.Shared ?? []);
        Assert.Equal(0, asked);
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task A_ticked_parameter_needs_a_label()
    {
        var (vm, _) = NewViewModel();
        await Add(vm, "box");
        var dialog = new SaveTemplateViewModel(new TemplateStore(_folder), "Part", SharedParameterChoice.For([vm.Document.Root.Children[0]]));

        var width = Choice(dialog, "width");
        width.IsShared = true;
        width.Label = "  ";

        Assert.False(dialog.IsValid);
        Assert.NotNull(dialog.Error);

        width.Label = "Length";
        Assert.True(dialog.IsValid);
    }

    [UiFact]
    public async Task An_empty_label_does_not_matter_while_the_parameter_is_not_ticked()
    {
        var (vm, _) = NewViewModel();
        await Add(vm, "box");
        var dialog = new SaveTemplateViewModel(new TemplateStore(_folder), "Part", SharedParameterChoice.For([vm.Document.Root.Children[0]]));

        Choice(dialog, "width").Label = "";

        Assert.True(dialog.IsValid);
    }

    [UiFact]
    public async Task Two_ticked_parameters_cannot_have_the_same_label()
    {
        var (vm, _) = NewViewModel();
        await Add(vm, "box");
        var dialog = new SaveTemplateViewModel(new TemplateStore(_folder), "Part", SharedParameterChoice.For([vm.Document.Root.Children[0]]));

        Choice(dialog, "width").IsShared = true;
        Choice(dialog, "depth").IsShared = true;
        Choice(dialog, "depth").Label = "width"; // the same, whatever the case

        Assert.False(dialog.IsValid);

        Choice(dialog, "depth").IsShared = false; // not asked any more: no conflict
        Assert.True(dialog.IsValid);
    }

    [UiFact]
    public async Task A_label_that_is_too_long_is_refused()
    {
        var (vm, _) = NewViewModel();
        await Add(vm, "box");
        var dialog = new SaveTemplateViewModel(new TemplateStore(_folder), "Part", SharedParameterChoice.For([vm.Document.Root.Children[0]]));

        Choice(dialog, "width").IsShared = true;
        Choice(dialog, "width").Label = new string('x', TemplateLimits.MaxSharedLabelLength + 1);

        Assert.False(dialog.IsValid);
    }

    [UiFact]
    public async Task The_properties_dialog_offers_no_choices_and_renaming_keeps_the_shared_parameters()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = Save(vm, dialogs, d => Choice(d, "width").IsShared = true);
        SaveTemplateViewModel? seen = null;
        dialogs.SaveTemplateScript = d =>
        {
            seen = d;
            d.Name = "Renamed";
            return true;
        };

        item.EditCommand.Execute(null);

        Assert.False(seen!.HasSharedChoices);
        var renamed = Assert.Single(vm.Templates);
        Assert.Equal("Renamed", renamed.Name);
        Assert.Equal("width", Assert.Single(renamed.Info.Shared!).Parameter);
    }

    [UiFact]
    public async Task Updating_a_template_keeps_the_shared_parameters_that_still_fit()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        Save(vm, dialogs, d => Choice(d, "width").IsShared = true);
        await Add(vm, "box"); // another box becomes the new version
        dialogs.UpdateTemplateScript = d =>
        {
            d.Selected = d.Templates.Single();
            return true;
        };

        vm.UpdateTemplateCommand.Execute(null);

        Assert.Equal("width", Assert.Single(Assert.Single(vm.Templates).Info.Shared!).Parameter);
    }

    // ---- inserting ------------------------------------------------------------------------------------

    private static TemplateItemViewModel SaveBoxWithWidth(MainViewModel vm, FakeDialogs dialogs, string label = "Length") =>
        Save(vm, dialogs, d =>
        {
            Choice(d, "width").IsShared = true;
            Choice(d, "width").Label = label;
        });

    [UiFact]
    public async Task Inserting_shows_a_form_with_only_the_shared_parameters_preset_with_the_stored_values()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "33"; // the width
        await vm.WaitForEvaluationAsync();
        var item = SaveBoxWithWidth(vm, dialogs, "Length");
        TemplateParametersViewModel? seen = null;
        dialogs.TemplateParametersScript = d =>
        {
            seen = d;
            return false;
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.NotNull(seen);
        var field = Assert.Single(seen!.Fields);
        Assert.Equal("Length", field.Label);
        Assert.Equal("mm", field.Unit);
        Assert.Equal("33", field.Text);
        Assert.Contains("Part", seen.Title);
        Assert.True(seen.IsValid);
    }

    [UiFact]
    public async Task A_parameter_that_was_never_changed_is_preset_with_its_default()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        TemplateParametersViewModel? seen = null;
        dialogs.TemplateParametersScript = d =>
        {
            seen = d;
            return false;
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.Equal("20", seen!.Fields[0].Text);
    }

    [UiFact]
    public async Task Confirming_puts_the_typed_value_into_the_inserted_object_in_one_undo_step()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        dialogs.TemplateParametersScript = d =>
        {
            d.Fields[0].Text = "45";
            return true;
        };
        var steps = vm.HistoryEntries.Count;

        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();

        var inserted = Assert.IsType<PrimitiveNode>(vm.Document.Root.Children[^1]);
        Assert.Equal("45", inserted.Parameters["width"]);
        Assert.Equal(45, WidthOf(vm, inserted), 0.01);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.Document.Root.Children); // the inserted object is gone with one undo
    }

    [UiFact]
    public async Task Cancelling_the_form_inserts_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        dialogs.TemplateParametersScript = _ => false;
        var steps = vm.HistoryEntries.Count;

        vm.InsertTemplateCommand.Execute(item);

        Assert.Single(vm.Document.Root.Children);
        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task A_value_that_does_not_work_is_marked_and_confirming_it_inserts_nothing()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        bool? validWhileWrong = null;
        dialogs.TemplateParametersScript = d =>
        {
            d.Fields[0].Text = "abc";
            validWhileWrong = d.IsValid;
            Assert.True(d.Fields[0].HasError);
            return true; // something confirms anyway
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.False(validWhileWrong);
        Assert.Single(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task A_value_outside_the_range_is_refused_with_the_range()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs, "Length");
        TemplateParametersViewModel? seen = null;
        dialogs.TemplateParametersScript = d =>
        {
            seen = d;
            d.Fields[0].Text = "0";
            return false;
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.False(seen!.IsValid);
        Assert.Contains("Length", seen.Fields[0].Error);
    }

    [UiFact]
    public async Task The_form_becomes_valid_again_when_the_value_is_corrected()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        var states = new List<bool>();
        dialogs.TemplateParametersScript = d =>
        {
            d.Fields[0].Text = "abc";
            states.Add(d.IsValid);
            d.Fields[0].Text = "12";
            states.Add(d.IsValid);
            return false;
        };

        vm.InsertTemplateCommand.Execute(item);

        Assert.Equal([false, true], states);
    }

    [UiFact]
    public async Task A_relative_value_works_on_the_stored_one()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        dialogs.TemplateParametersScript = d =>
        {
            d.Fields[0].Text = "+10"; // 20 + 10
            return true;
        };

        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(30, WidthOf(vm, vm.Document.Root.Children[^1]), 0.01);
    }

    [UiFact]
    public async Task A_formula_may_use_the_variables_of_the_template_and_they_come_along()
    {
        var (vm, dialogs) = NewViewModel();
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[^1].Name.Text = "wall";
        vm.Variables.Rows[^1].Expression.Text = "3";
        await Add(vm, "box");
        vm.Properties!.Parameters[0].Text = "wall * 4";
        await vm.WaitForEvaluationAsync();
        var item = SaveBoxWithWidth(vm, dialogs);

        // a fresh project that does not have the variable
        var (other, otherDialogs) = NewViewModel();
        var imported = other.Templates.Single();
        Assert.Equal(item.Path, imported.Path);
        otherDialogs.TemplateParametersScript = d =>
        {
            Assert.Equal("wall * 4", d.Fields[0].Text);
            d.Fields[0].Text = "wall * 5";
            Assert.True(d.IsValid);
            return true;
        };

        other.InsertTemplateCommand.Execute(imported);
        await other.WaitForEvaluationAsync();

        Assert.Equal("3", other.Document.Variables["wall"]);
        Assert.Equal(15, WidthOf(other, other.Document.Root.Children[^1]), 0.01);
    }

    [UiFact]
    public async Task Objects_in_other_places_of_a_template_with_several_objects_get_their_values()
    {
        var (vm, dialogs) = NewViewModel();
        var box = await Add(vm, "box");
        var cylinder = await Add(vm, "cylinder");
        vm.SelectOnly([box.Id, cylinder.Id]);
        var item = Save(vm, dialogs, d =>
        {
            Choice(d, "diameter").IsShared = true;
            Choice(d, "diameter").Label = "Pin diameter";
        });
        dialogs.TemplateParametersScript = d =>
        {
            Assert.Equal("Pin diameter", d.Fields[0].Label);
            d.Fields[0].Text = "7";
            return true;
        };

        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();

        var group = Assert.IsType<GroupNode>(vm.Document.Root.Children[^1]);
        var pin = group.Children.OfType<PrimitiveNode>().Single(c => c.GeneratorId == "cylinder");
        Assert.Equal("7", pin.Parameters["diameter"]);
        var original = vm.Document.Root.Children.OfType<PrimitiveNode>().Single(c => c.GeneratorId == "cylinder");
        Assert.False(original.Parameters.ContainsKey("diameter") && original.Parameters["diameter"] == "7"); // the project object is not touched
    }

    [UiFact]
    public async Task Several_shared_parameters_are_listed_in_order_and_all_applied()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = Save(vm, dialogs, d =>
        {
            Choice(d, "width").IsShared = true;
            Choice(d, "height").IsShared = true;
        });
        dialogs.TemplateParametersScript = d =>
        {
            Assert.Equal(["Width", "Height"], d.Fields.Select(f => f.Label).ToArray());
            d.Fields[0].Text = "11";
            d.Fields[1].Text = "12";
            return true;
        };

        vm.InsertTemplateCommand.Execute(item);

        var inserted = Assert.IsType<PrimitiveNode>(vm.Document.Root.Children[^1]);
        Assert.Equal("11", inserted.Parameters["width"]);
        Assert.Equal("12", inserted.Parameters["height"]);
    }

    [UiFact]
    public async Task Shared_entries_that_fit_nothing_are_skipped_and_without_any_left_there_is_no_form()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = Save(vm, dialogs, _ => { });
        // a template file made by hand: one path that is not there, one parameter that the shape does not have
        var doc = TemplateFile.Load(item.Path);
        var info = item.Info with
        {
            Shared =
            [
                new SharedParameter([4], "width", "Nowhere"),
                new SharedParameter([0], "color", "No such parameter"),
            ],
        };
        TemplateFile.SaveAtomic(item.Path, doc, null, info, null);
        vm.RefreshTemplatesCommand.Execute(null);
        var asked = 0;
        dialogs.TemplateParametersScript = _ =>
        {
            asked++;
            return true;
        };

        vm.InsertTemplateCommand.Execute(vm.Templates.Single());

        Assert.Equal(0, asked);
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task A_valid_entry_next_to_a_broken_one_still_asks()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = Save(vm, dialogs, _ => { });
        var doc = TemplateFile.Load(item.Path);
        var info = item.Info with
        {
            Shared = [new SharedParameter([4], "width", "Nowhere"), new SharedParameter([0], "height", "Height")],
        };
        TemplateFile.SaveAtomic(item.Path, doc, null, info, null);
        vm.RefreshTemplatesCommand.Execute(null);
        TemplateParametersViewModel? seen = null;
        dialogs.TemplateParametersScript = d =>
        {
            seen = d;
            return false;
        };

        vm.InsertTemplateCommand.Execute(vm.Templates.Single());

        Assert.Equal(["Height"], seen!.Fields.Select(f => f.Label).ToArray());
    }

    [UiFact]
    public async Task Inserting_the_same_template_twice_asks_twice_and_the_values_are_independent()
    {
        var (vm, dialogs) = NewViewModel();
        await Add(vm, "box");
        var item = SaveBoxWithWidth(vm, dialogs);
        var answers = new Queue<string>(["30", "40"]);
        dialogs.TemplateParametersScript = d =>
        {
            d.Fields[0].Text = answers.Dequeue();
            return true;
        };

        vm.InsertTemplateCommand.Execute(item);
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();

        var added = vm.Document.Root.Children.OfType<PrimitiveNode>().Skip(1).ToList();
        Assert.Equal(["30", "40"], added.Select(n => n.Parameters["width"]).ToArray());
    }
}
