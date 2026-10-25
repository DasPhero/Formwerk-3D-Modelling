using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Library;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>An object made from a library element with parameters remembers its values and they can be changed in the properties (F-PAR-05).</summary>
public sealed class ElementEditTests
{
    private readonly FakeDialogs _dialogs = new() { TemplateParametersScript = _ => true };

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static LibraryEntryViewModel Entry(MainViewModel vm, string id) => vm.LibraryEntries.Single(e => e.Item.Id == id);

    private static async Task<Node> Insert(MainViewModel vm, string id)
    {
        vm.AddLibraryItemCommand.Execute(Entry(vm, id));
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Children[^1];
    }

    private static FieldViewModel Field(MainViewModel vm, string label) => vm.Properties!.ElementFields.Single(f => f.Label == label);

    private static Bounds BoundsOf(MainViewModel vm, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        var parts = vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    [UiFact]
    public async Task An_inserted_element_with_parameters_remembers_its_values()
    {
        var vm = NewViewModel();
        _dialogs.TemplateParametersScript = form =>
        {
            form.Fields.Single(f => f.Label == "Cable diameter").Text = "10";
            return true;
        };

        var clip = await Insert(vm, "cable-clip");

        Assert.Equal("cable-clip", clip.Element!.ElementId);
        Assert.Equal("10", clip.Element.Values["cable"]);
        Assert.Equal("2", clip.Element.Values["wall"]);
    }

    [UiFact]
    public async Task Elements_without_parameters_and_basic_shapes_have_no_element_section()
    {
        var vm = NewViewModel();
        var nut = await Insert(vm, "hex-nut-m3");
        Assert.Null(nut.Element);

        vm.SelectOnly([nut.Id]);
        Assert.False(vm.Properties!.HasElementFields);
    }

    [UiFact]
    public async Task The_properties_of_the_object_show_one_field_per_parameter_with_its_values()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");

        vm.SelectOnly([clip.Id]);

        Assert.True(vm.Properties!.HasElementFields);
        Assert.Equal("Element: Cable Clip", vm.Properties.ElementTitle);
        Assert.Equal(LibraryCatalog.Find("cable-clip")!.Parameters!.Select(p => p.Label).ToArray(), vm.Properties.ElementFields.Select(f => f.Label).ToArray());
        Assert.Equal("8", Field(vm, "Cable diameter").Text);
        Assert.Equal("3.4", Field(vm, "Screw hole").Text);
    }

    [UiFact]
    public async Task A_bigger_cable_makes_a_bigger_hole_but_the_screw_holes_keep_their_size_and_it_is_one_undo_step()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");
        vm.SelectOnly([clip.Id]);
        var id = clip.Id;
        var widthBefore = BoundsOf(vm, clip).Size.X;
        var steps = vm.HistoryEntries.Count;

        Field(vm, "Cable diameter").Text = "14";
        await vm.WaitForEvaluationAsync();

        Assert.Same(clip, vm.Document.Root.Children[^1]);
        Assert.Equal(id, clip.Id);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        var holes = clip.Descendants().Where(n => n.Name.StartsWith("Screw Hole")).Cast<PrimitiveNode>().ToList();
        Assert.Equal(2, holes.Count);
        Assert.All(holes, h => Assert.Equal("3.4", h.Parameters["diameter"])); // the same as before
        Assert.Equal("14", ((PrimitiveNode)clip.Descendants().Single(n => n.Name == "Cable Hole")).Parameters["diameter"]);
        Assert.True(BoundsOf(vm, clip).Size.X > widthBefore + 5); // the plate grew with the ring
        Assert.Equal("14", clip.Element!.Values["cable"]);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal("8", ((PrimitiveNode)clip.Descendants().Single(n => n.Name == "Cable Hole")).Parameters["diameter"]);
        Assert.Equal("8", clip.Element!.Values["cable"]);
    }

    [UiFact]
    public async Task Changing_a_value_keeps_name_color_and_position_and_puts_the_scale_back_to_one()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");
        clip.Name = "Cable holder left";
        clip.Color = 0xFF336699;
        clip.Transform = new Transform(new Vector3(30, 20, 0), Quaternion.Identity, new Vector3(2, 2, 2));
        vm.SelectOnly([clip.Id]);

        Field(vm, "Height").Text = "12";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Cable holder left", clip.Name);
        Assert.Equal(0xFF336699u, clip.Color);
        Assert.Equal(new Vector3(30, 20, 0), clip.Transform.Position);
        Assert.Equal(Vector3.One, clip.Transform.Scale);
        Assert.Equal(12, BoundsOf(vm, clip).Size.Z, 1);
    }

    [UiFact]
    public async Task A_value_that_does_not_work_shows_an_error_and_changes_nothing()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");
        vm.SelectOnly([clip.Id]);
        var steps = vm.HistoryEntries.Count;

        Field(vm, "Cable diameter").Text = "1"; // the cable must be at least 2 mm
        Assert.False(string.IsNullOrEmpty(Field(vm, "Cable diameter").Error));
        Field(vm, "Ring wall").Text = "abc";

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal("8", clip.Element!.Values["cable"]);
    }

    [UiFact]
    public async Task A_project_variable_in_a_value_stays_linked()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");
        vm.Document.Variables["Kabel"] = "11";
        vm.SelectOnly([clip.Id]);

        Field(vm, "Cable diameter").Text = "Kabel + 1";
        await vm.WaitForEvaluationAsync();

        Assert.Equal("Kabel + 1", clip.Element!.Values["cable"]);
        Assert.Contains("(Kabel + 1)", ((PrimitiveNode)clip.Descendants().Single(n => n.Name == "Cable Hole")).Parameters["diameter"]);
        Assert.True(vm.Properties!.ElementFields.First(f => f.Label == "Cable diameter").IsLinked);
    }

    [UiFact]
    public async Task The_values_survive_saving_duplicating_and_loading()
    {
        var vm = NewViewModel();
        var clip = await Insert(vm, "cable-clip");
        vm.SelectOnly([clip.Id]);
        Field(vm, "Cable diameter").Text = "9";

        var copy = clip.Clone(newIds: true);
        Assert.Equal("9", copy.Element!.Values["cable"]);

        using var stream = new MemoryStream();
        ProjectFile.Write(stream, vm.Document);
        stream.Position = 0;
        var loaded = ProjectFile.Read(stream);
        Assert.Equal("9", loaded.Root.Children.Single().Element!.Values["cable"]);
    }
}
