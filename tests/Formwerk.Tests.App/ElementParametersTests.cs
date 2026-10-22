using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Inserting library elements with parameters: a small form asks for the values (F-PAR-05).</summary>
public sealed class ElementParametersTests
{
    private readonly FakeDialogs _dialogs = new();

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static LibraryEntryViewModel Entry(MainViewModel vm, string id) => vm.LibraryEntries.Single(e => e.Item.Id == id);

    private static Bounds BoundsOf(MainViewModel vm, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static void Type(TemplateParametersViewModel form, string label, string text) =>
        form.Fields.Single(f => f.Label == label).Text = text;

    private async Task Insert(MainViewModel vm, string id)
    {
        vm.AddLibraryItemCommand.Execute(Entry(vm, id));
        await vm.WaitForEvaluationAsync();
    }

    // ---- the form --------------------------------------------------------------------------------

    [UiFact]
    public async Task The_form_has_one_field_per_parameter_with_the_defaults()
    {
        var vm = NewViewModel();
        TemplateParametersViewModel? seen = null;
        _dialogs.TemplateParametersScript = form =>
        {
            seen = form;
            return false;
        };

        await Insert(vm, "box-with-lid");

        Assert.NotNull(seen);
        Assert.Contains("Box with Lid", seen!.Title);
        var item = LibraryCatalog.Find("box-with-lid")!;
        Assert.Equal(item.Parameters!.Select(p => p.Label).ToArray(), seen.Fields.Select(f => f.Label).ToArray());
        Assert.Equal(["50", "30", "20", "2", "0.2"], seen.Fields.Select(f => f.Text).ToArray());
        Assert.All(seen.Fields, f => Assert.Equal("mm", f.Unit));
        Assert.True(seen.IsValid);
    }

    [UiFact]
    public async Task Cancelling_the_form_inserts_nothing()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;
        _dialogs.TemplateParametersScript = _ => false;

        await Insert(vm, "box-with-lid");

        Assert.Empty(vm.Document.Root.Children);
        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task A_value_that_does_not_work_shows_an_error_in_the_form_and_nothing_is_inserted()
    {
        var vm = NewViewModel();
        bool? valid = null;
        _dialogs.TemplateParametersScript = form =>
        {
            Type(form, form.Fields[3].Label, "0"); // the wall must be at least 0.4 mm
            valid = form.IsValid;
            return true; // something that confirms anyway
        };

        await Insert(vm, "box-with-lid");

        Assert.False(valid);
        Assert.Empty(vm.Document.Root.Children);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [UiFact]
    public async Task Elements_without_parameters_are_inserted_without_a_form()
    {
        var vm = NewViewModel();
        var asked = false;
        _dialogs.TemplateParametersScript = _ =>
        {
            asked = true;
            return true;
        };

        await Insert(vm, "hex-nut-m3");

        Assert.False(asked);
        Assert.Single(vm.Document.Root.Children);
    }

    // ---- the result ------------------------------------------------------------------------------

    [UiFact]
    public async Task An_inner_size_of_50_30_20_with_a_wall_of_2_gives_an_outer_size_of_54_34_22()
    {
        var vm = NewViewModel();
        _dialogs.TemplateParametersScript = form =>
        {
            Type(form, "Inner width", "50");
            Type(form, "Inner depth", "30");
            Type(form, "Inner height", "20");
            Type(form, "Wall", "2");
            return true;
        };
        var steps = vm.HistoryEntries.Count;

        await Insert(vm, "box-with-lid");

        var element = Assert.Single(vm.Document.Root.Children);
        Assert.Equal("Box with Lid", element.Name);
        var size = BoundsOf(vm, element.Descendants().Single(n => n.Name == "Box")).Size;
        Assert.Equal(54, size.X, 2);
        Assert.Equal(34, size.Y, 2);
        Assert.Equal(22, size.Z, 2);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([element.Id], vm.SelectedIds.ToArray());

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task A_project_variable_typed_into_the_form_stays_linked()
    {
        var vm = NewViewModel();
        vm.Document.Variables["Wand"] = "3";
        _dialogs.TemplateParametersScript = form =>
        {
            Type(form, "Wall", "Wand");
            return true;
        };

        await Insert(vm, "box-with-lid");
        var box = vm.Document.Root.Children[0].Descendants().Single(n => n.Name == "Box");
        Assert.Equal(56, BoundsOf(vm, box).Size.X, 2);

        vm.Document.Variables["Wand"] = "4";
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        Assert.Equal(58, BoundsOf(vm, box).Size.X, 2);
    }

    [UiFact]
    public async Task Two_elements_with_different_sizes_do_not_overlap()
    {
        var vm = NewViewModel();
        _dialogs.TemplateParametersScript = _ => true;
        await Insert(vm, "box-with-lid");
        _dialogs.TemplateParametersScript = form =>
        {
            Type(form, "Inner width", "100");
            return true;
        };

        await Insert(vm, "box-with-lid");

        var a = BoundsOf(vm, vm.Document.Root.Children[0]);
        var b = BoundsOf(vm, vm.Document.Root.Children[1]);
        Assert.True(a.Max.X + 6 <= b.Min.X + 0.01f || b.Max.X + 6 <= a.Min.X + 0.01f || a.Max.Y + 6 <= b.Min.Y + 0.01f || b.Max.Y + 6 <= a.Min.Y + 0.01f);
        Assert.Equal(["Box with Lid", "Box with Lid 2"], vm.Document.Root.Children.Select(n => n.Name).ToArray());
    }

    [UiFact]
    public async Task A_drop_asks_for_the_values_too_and_puts_the_element_at_the_mouse()
    {
        var vm = NewViewModel();
        var asked = 0;
        _dialogs.TemplateParametersScript = _ =>
        {
            asked++;
            return true;
        };

        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest("box-with-lid", new FacePick(Guid.Empty, new Vector3(100, 40, 0), Vector3.UnitZ)));
        await vm.WaitForEvaluationAsync();

        Assert.Equal(1, asked);
        var center = BoundsOf(vm, vm.Document.Root.Children[0]).Center;
        Assert.Equal(100, center.X, 1);
        Assert.Equal(40, center.Y, 1);
    }

    [UiFact]
    public async Task The_spacer_sleeve_and_the_mounting_plate_have_forms_with_their_own_parameters()
    {
        var vm = NewViewModel();
        var labels = new Dictionary<string, string[]>();
        _dialogs.TemplateParametersScript = form =>
        {
            labels[form.Title] = [.. form.Fields.Select(f => f.Label)];
            return true;
        };

        await Insert(vm, "spacer-sleeve");
        await Insert(vm, "mounting-plate");

        Assert.Equal(2, labels.Count);
        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Contains(labels.Values, l => l.Contains("Outer diameter"));
        Assert.Contains(labels.Values, l => l.Contains("Margin"));
    }
}
