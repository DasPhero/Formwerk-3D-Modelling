using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Saving the selection as a template and inserting it again (F-TPL-01, F-TPL-02, F-TPL-03).</summary>
public sealed class TemplateFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-tpl-flow-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private sealed class FakePreview : ITemplatePreview
    {
        public int Calls { get; private set; }

        public int LastItemCount { get; private set; }

        public byte[]? Render(IReadOnlyList<ViewportItem> items)
        {
            Calls++;
            LastItemCount = items.Count;
            return [1, 2, 3];
        }
    }

    private (MainViewModel Vm, FakeDialogs Dialogs, FakePreview Preview) NewViewModel(Func<SaveTemplateViewModel, bool>? script = null)
    {
        var dialogs = new FakeDialogs { SaveTemplateScript = script };
        var preview = new FakePreview();
        var vm = new MainViewModel(new ManifoldKernel(), dialogs, templates: new TemplateStore(_folder), templatePreview: preview);
        return (vm, dialogs, preview);
    }

    private static Func<SaveTemplateViewModel, bool> Named(string name, string category = "") => dialog =>
    {
        dialog.Name = name;
        dialog.Category = category;
        return true;
    };

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    /// <summary>A 40 x 40 x 10 plate and a 10 mm cylinder hole through it, grouped. Selected.</summary>
    private static async Task<Node> PlateWithHole(MainViewModel vm)
    {
        await Add(vm, "box");
        var plate = vm.Document.Root.Children[0];
        plate.Name = "Plate";
        vm.Properties!.Parameters[0].Text = "40";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Parameters[2].Text = "10";
        await Add(vm, "cylinder");
        var hole = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = plate.Transform.Position.X.ToString(System.Globalization.CultureInfo.InvariantCulture);
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "-5";
        vm.Properties.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "30";
        vm.Properties.IsHole = true;
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Children.OfType<GroupNode>().Single();
    }

    private static double SolidVolume(MainViewModel vm) =>
        vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

    // ---- saving --------------------------------------------------------------------------------------

    [UiFact]
    public async Task Saving_needs_a_selection()
    {
        var (vm, _, _) = NewViewModel(Named("X"));
        await Add(vm, "box");

        Assert.True(vm.SaveTemplateCommand.CanExecute(null));
        vm.SelectOnly([]);

        Assert.False(vm.SaveTemplateCommand.CanExecute(null));
    }

    [UiFact]
    public async Task A_saved_template_is_listed_under_my_templates_with_name_category_and_preview()
    {
        var (vm, _, preview) = NewViewModel(Named("Hinge plate", "Mechanics"));
        await PlateWithHole(vm);

        vm.SaveTemplateCommand.Execute(null);

        var item = Assert.Single(vm.Templates);
        Assert.Equal("Hinge plate", item.Name);
        Assert.Equal("Mechanics", item.Category);
        Assert.Equal([1, 2, 3], item.PreviewPng);
        Assert.Equal(1, preview.Calls);
        Assert.True(preview.LastItemCount >= 2, "the solid and the ghost of the hole are in the picture");
        Assert.Contains("Hinge plate", vm.StatusText);
    }

    [UiFact]
    public async Task Cancelling_the_dialog_saves_nothing()
    {
        var (vm, _, _) = NewViewModel(dialog => false);
        await Add(vm, "box");

        vm.SaveTemplateCommand.Execute(null);

        Assert.Empty(vm.Templates);
        Assert.False(Directory.Exists(_folder));
    }

    [UiFact]
    public async Task The_dialog_starts_with_the_name_of_a_single_object_and_knows_the_categories()
    {
        string? suggested = null;
        IReadOnlyList<string>? categories = null;
        var (vm, _, _) = NewViewModel(dialog =>
        {
            suggested = dialog.Name;
            categories = dialog.Categories;
            return false;
        });
        await Add(vm, "cylinder");

        vm.SaveTemplateCommand.Execute(null);

        Assert.Equal("Cylinder", suggested);
        Assert.Empty(categories!);
    }

    [UiFact]
    public async Task The_selection_is_saved_on_the_floor_and_centered_with_its_size()
    {
        var (vm, _, _) = NewViewModel(Named("Plate"));
        await PlateWithHole(vm);
        vm.Properties?.GetType(); // the group is selected

        vm.SaveTemplateCommand.Execute(null);

        var item = vm.Templates.Single();
        Assert.Equal(new Vector3(40, 40, 10), item.Size);
        var saved = TemplateFile.Load(item.Path);
        var root = Assert.IsType<GroupNode>(Assert.Single(saved.Root.Children));
        Assert.Equal(0, root.Transform.Position.X, 3);
        Assert.Equal(0, root.Transform.Position.Y, 3);
        Assert.Equal(0, root.Transform.Position.Z, 3);
    }

    [UiFact]
    public async Task A_taken_name_in_the_same_category_is_refused_with_the_suggestion()
    {
        var (vm, _, _) = NewViewModel(Named("Hinge", "Mechanics"));
        await Add(vm, "box");
        vm.SaveTemplateCommand.Execute(null);

        vm.SaveTemplateCommand.Execute(null); // the same name again

        Assert.Single(vm.Templates);
        Assert.Contains("already exists", vm.StatusText);
        Assert.Contains("Hinge (2)", vm.StatusText);
    }

    [UiFact]
    public async Task The_same_name_in_another_category_is_fine()
    {
        var category = "A";
        var (vm, _, _) = NewViewModel(dialog =>
        {
            dialog.Name = "Hinge";
            dialog.Category = category;
            return true;
        });
        await Add(vm, "box");
        vm.SaveTemplateCommand.Execute(null);

        category = "B";
        vm.SaveTemplateCommand.Execute(null);

        Assert.Equal(2, vm.Templates.Count);
    }

    [UiFact]
    public async Task Templates_that_are_already_in_the_folder_show_up_when_the_program_starts()
    {
        var (first, _, _) = NewViewModel(Named("Hinge"));
        await Add(first, "box");
        first.SaveTemplateCommand.Execute(null);

        var (second, _, _) = NewViewModel();

        Assert.Equal(["Hinge"], second.Templates.Select(t => t.Name));
    }

    // ---- inserting -----------------------------------------------------------------------------------

    [UiFact]
    public async Task Inserting_gives_an_object_with_the_template_name_on_the_floor()
    {
        var (vm, _, _) = NewViewModel(Named("Hinge plate"));
        await PlateWithHole(vm);
        vm.SaveTemplateCommand.Execute(null);
        var original = SolidVolume(vm);

        var (second, _, _) = NewViewModel(); // a new session: the template comes from the folder
        second.InsertTemplateCommand.Execute(second.Templates.Single());
        await second.WaitForEvaluationAsync();

        var inserted = Assert.Single(second.Document.Root.Children);
        Assert.Equal("Hinge plate", inserted.Name);
        Assert.Equal([inserted.Id], second.SelectedIds);
        Assert.Equal(original, SolidVolume(second), 3); // geometrically identical
        Assert.Equal(0, second.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Min(i => i.Mesh.ComputeBounds().Min.Z), 3);
    }

    [UiFact]
    public async Task The_structure_stays_editable_the_hole_diameter_changes_the_geometry_live()
    {
        var (vm, _, _) = NewViewModel(Named("Hinge plate"));
        await PlateWithHole(vm);
        vm.SaveTemplateCommand.Execute(null);

        var (second, _, _) = NewViewModel();
        second.InsertTemplateCommand.Execute(second.Templates.Single());
        await second.WaitForEvaluationAsync();
        var before = SolidVolume(second);

        var group = Assert.IsType<GroupNode>(second.Document.Root.Children.Single());
        var hole = group.Children.OfType<PrimitiveNode>().Single(n => n.Role == NodeRole.Hole);
        second.SelectOnly([hole.Id]);
        second.Properties!.Parameters[0].Text = "20"; // diameter
        await second.WaitForEvaluationAsync();

        Assert.True(SolidVolume(second) < before - 500, "a wider hole takes more away");
        Assert.Contains(second.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
    }

    [UiFact]
    public async Task Inserting_is_one_undo_step()
    {
        var (vm, _, _) = NewViewModel(Named("Box"));
        await Add(vm, "box");
        vm.SaveTemplateCommand.Execute(null);
        var before = vm.HistoryEntries.Count;

        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before + 1, vm.HistoryEntries.Count);
        Assert.Equal(2, vm.Document.Root.Children.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task Inserting_twice_gives_unique_names_and_does_not_overlap()
    {
        var (vm, _, _) = NewViewModel(Named("Box"));
        await Add(vm, "box");
        vm.SaveTemplateCommand.Execute(null);

        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();
        vm.InsertTemplateCommand.Execute(vm.Templates.Single());
        await vm.WaitForEvaluationAsync();

        var names = vm.Document.Root.Children.Select(n => n.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var bounds = vm.ViewportItems.Select(i => i.Mesh.ComputeBounds()).ToList();
        for (var i = 0; i < bounds.Count; i++)
        for (var j = i + 1; j < bounds.Count; j++)
        {
            var overlap = bounds[i].Min.X < bounds[j].Max.X && bounds[i].Max.X > bounds[j].Min.X
                          && bounds[i].Min.Y < bounds[j].Max.Y && bounds[i].Max.Y > bounds[j].Min.Y;
            Assert.False(overlap, $"object {i} overlaps object {j}");
        }
    }

    [UiFact]
    public async Task Several_objects_come_back_as_one_group_with_the_template_name()
    {
        var (vm, _, _) = NewViewModel(Named("Pair"));
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.SelectOnly(vm.Document.Root.Children.Select(n => n.Id));
        vm.SaveTemplateCommand.Execute(null);

        var (second, _, _) = NewViewModel();
        second.InsertTemplateCommand.Execute(second.Templates.Single());
        await second.WaitForEvaluationAsync();

        var group = Assert.IsType<GroupNode>(Assert.Single(second.Document.Root.Children));
        Assert.Equal("Pair", group.Name);
        Assert.Equal(["Box", "Sphere"], group.Children.Select(c => c.Name).Order());
    }

    [UiFact]
    public async Task Imported_meshes_come_along_into_a_new_session()
    {
        var (vm, _, _) = NewViewModel(Named("Imported"));
        var id = Guid.NewGuid();
        vm.Meshes.Add(id, new Formwerk.Geometry.MeshData(
            [new Vector3(0, 0, 0), new Vector3(10, 0, 0), new Vector3(0, 10, 0), new Vector3(0, 0, 10)],
            [0, 2, 1, 0, 1, 3, 1, 2, 3, 0, 3, 2]));
        new AddNodeCommand(vm.Document, vm.Document.Root, new MeshNode("Tetra", id)).Do();
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([vm.Document.Root.Children.Single().Id]);
        vm.SaveTemplateCommand.Execute(null);

        var (second, _, _) = NewViewModel();
        second.InsertTemplateCommand.Execute(second.Templates.Single());
        await second.WaitForEvaluationAsync();

        Assert.True(second.Meshes.Contains(id));
        Assert.NotEmpty(second.ViewportItems);
    }

    [UiFact]
    public async Task A_template_file_that_vanished_or_broke_gives_a_message_and_no_crash()
    {
        var (vm, _, _) = NewViewModel(Named("Box"));
        await Add(vm, "box");
        vm.SaveTemplateCommand.Execute(null);
        var item = vm.Templates.Single();
        var before = vm.Document.Root.Children.Count;

        File.WriteAllText(item.Path, "no longer a template");
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before, vm.Document.Root.Children.Count);
        Assert.Contains("template", vm.StatusText, StringComparison.OrdinalIgnoreCase);

        File.Delete(item.Path);
        vm.InsertTemplateCommand.Execute(item);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task Without_a_template_folder_the_commands_are_not_available()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        await Add(vm, "box");

        Assert.False(vm.SaveTemplateCommand.CanExecute(null));
        Assert.Empty(vm.Templates);
    }

    [UiFact]
    public void Inserting_nothing_does_nothing()
    {
        var (vm, _, _) = NewViewModel();

        vm.InsertTemplateCommand.Execute(null);

        Assert.Empty(vm.Document.Root.Children);
    }
}
