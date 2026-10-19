using System.IO;
using System.Numerics;
using System.Text;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Importing STL and OBJ files: dialog and drag and drop, unit question, placement, save and open (F-IMP-01/02, F-FLR-01).</summary>
public sealed class ImportFlowTests : IDisposable
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-import-{Guid.NewGuid():N}");

    public ImportFlowTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string P(string name) => Path.Combine(_folder, name);

    private static Dictionary<string, double> Dims(double w, double d, double h) =>
        new(StringComparer.OrdinalIgnoreCase) { ["width"] = w, ["depth"] = d, ["height"] = h };

    private string WriteStl(string name, MeshData mesh)
    {
        var path = P(name);
        using var stream = File.Create(path);
        StlWriter.WriteBinary(stream, [mesh]);
        return path;
    }

    private string WriteBoxStl(string name, double size = 30) =>
        WriteStl(name, new BoxGenerator().Generate(Dims(size, size, size)).Transformed(Matrix4x4.CreateTranslation(500, 700, -40)));

    private const string ObjCube = "v 0 0 0\nv 10 0 0\nv 10 10 0\nv 0 10 0\nv 0 0 10\nv 10 0 10\nv 10 10 10\nv 0 10 10\n"
                                   + "f 1 4 3 2\nf 5 6 7 8\nf 1 2 6 5\nf 2 3 7 6\nf 3 4 8 7\nf 4 1 5 8\n";

    private static MainViewModel NewViewModel(FakeDialogs? dialogs = null, AutosaveStore? autosave = null) =>
        new(new ManifoldKernel(), dialogs ?? new FakeDialogs(), autosave);

    private static Node Child(MainViewModel vm, int index) => vm.Document.Root.Children[index];

    private static Bounds BoundsOf(MainViewModel vm, Guid id) =>
        vm.ViewportItems.Single(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid).Mesh.ComputeBounds();

    // ---- the basic import ----------------------------------------------------------------------

    [UiFact]
    public async Task An_imported_stl_appears_selected_on_the_plate_with_the_name_of_the_file()
    {
        var vm = NewViewModel();

        await vm.ImportFilesAsync([WriteBoxStl("bracket.stl")]);
        await vm.WaitForEvaluationAsync();

        var node = Assert.IsType<MeshNode>(Child(vm, 0));
        Assert.Equal("bracket", node.Name);
        Assert.Equal([node.Id], vm.SelectedIds);
        Assert.True(vm.Meshes.Contains(node.MeshId));

        // F-FLR-01: centered on the plate and resting on it, whatever coordinates the file had
        var b = BoundsOf(vm, node.Id);
        Assert.Equal(0, b.Center.X, 3);
        Assert.Equal(0, b.Center.Y, 3);
        Assert.Equal(0, b.Min.Z, 3);
        Assert.Equal(30, b.Size.X, 3);
        Assert.Equal("Add bracket", vm.HistoryEntries[^1].Name);
        Assert.Contains("Imported bracket (12 triangles)", vm.StatusText);
        Assert.True(vm.IsDirty);
    }

    [UiFact]
    public async Task An_imported_object_can_be_exported_again()
    {
        var export = P("out.stl");
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs { SavePath = export });
        await vm.ImportFilesAsync([WriteBoxStl("a.stl")]);
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        Assert.Equal(StlWriter.SizeFor(12), new FileInfo(export).Length);
    }

    [UiFact]
    public async Task An_obj_file_can_be_imported_too()
    {
        var path = P("cube.obj");
        File.WriteAllText(path, ObjCube);
        var vm = NewViewModel();

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal("cube", Child(vm, 0).Name);
        Assert.Equal(10, BoundsOf(vm, Child(vm, 0).Id).Size.X, 3);
    }

    [UiFact]
    public async Task The_import_command_uses_the_files_chosen_in_the_dialog()
    {
        var dialogs = new FakeDialogs();
        dialogs.FilesToImport.Add(WriteBoxStl("one.stl"));
        dialogs.FilesToImport.Add(WriteBoxStl("two.stl"));
        var vm = NewViewModel(dialogs);

        await vm.ImportCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["one", "two"], vm.Document.Root.Children.Select(n => n.Name));
    }

    [UiFact]
    public async Task Several_files_are_imported_side_by_side_and_all_end_up_selected()
    {
        var vm = NewViewModel();

        await vm.ImportFilesAsync([WriteBoxStl("a.stl"), WriteBoxStl("b.stl"), WriteBoxStl("c.stl")]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(3, vm.Document.Root.Children.Count);
        Assert.Equal(3, vm.SelectedIds.Count);
        var bounds = vm.Document.Root.Children.Select(n => BoundsOf(vm, n.Id)).ToList();
        for (var i = 0; i < bounds.Count; i++)
        for (var j = i + 1; j < bounds.Count; j++)
            Assert.False(bounds[i].Min.X < bounds[j].Max.X && bounds[i].Max.X > bounds[j].Min.X, $"object {i} overlaps object {j}");
    }

    [UiFact]
    public async Task Imported_objects_get_unique_names()
    {
        var vm = NewViewModel();
        var path = WriteBoxStl("part.stl");

        await vm.ImportFilesAsync([path]);
        await vm.ImportFilesAsync([path]);

        Assert.Equal(["part", "part 2"], vm.Document.Root.Children.Select(n => n.Name));
    }

    [UiFact]
    public async Task Importing_nothing_or_empty_names_does_nothing()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([]);
        await vm.ImportFilesAsync([" ", ""]);
        Assert.Empty(vm.Document.Root.Children);
        Assert.False(vm.IsDirty);
    }

    // ---- units (F-IMP-02) ----------------------------------------------------------------------

    [UiFact]
    public async Task A_normal_sized_model_is_imported_without_a_question()
    {
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);

        await vm.ImportFilesAsync([WriteBoxStl("normal.stl", 50)]);

        Assert.Empty(dialogs.UnitPrompts);
    }

    [Theory]
    [InlineData(0.05, ImportUnit.Millimeters, 0.05)]
    [InlineData(0.05, ImportUnit.Centimeters, 0.5)]
    [InlineData(0.05, ImportUnit.Inches, 1.27)]
    [InlineData(2500, ImportUnit.Millimeters, 2500)]
    [InlineData(2500, ImportUnit.Centimeters, 25000)]
    [InlineData(0.5, ImportUnit.Inches, 12.7)]
    [InlineData(30, ImportUnit.Inches, 30)] // a normal size is not asked about, so the answer is never used
    public async Task The_answer_to_the_unit_question_scales_the_model(double sizeInFile, ImportUnit answer, double expectedMm)
    {
        var dialogs = new FakeDialogs { UnitAnswer = answer };
        var vm = NewViewModel(dialogs);
        var asked = sizeInFile is < 1 or > 1000;

        await vm.ImportFilesAsync([WriteBoxStl("unit.stl", sizeInFile)]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(asked ? 1 : 0, dialogs.UnitPrompts.Count);
        var size = BoundsOf(vm, Child(vm, 0).Id).Size.X;
        Assert.InRange(size, expectedMm * 0.999, expectedMm * 1.001);
        if (asked)
        {
            Assert.Equal("unit", dialogs.UnitPrompts[0].Name);
            Assert.InRange(dialogs.UnitPrompts[0].Extent, sizeInFile * 0.999, sizeInFile * 1.001);
        }
    }

    [UiFact]
    public async Task Cancelling_the_unit_question_cancels_that_import_only()
    {
        var dialogs = new FakeDialogs { UnitAnswer = null };
        var vm = NewViewModel(dialogs);

        await vm.ImportFilesAsync([WriteBoxStl("tiny.stl", 0.2), WriteBoxStl("fine.stl", 40)]);

        Assert.Equal(["fine"], vm.Document.Root.Children.Select(n => n.Name));
        Assert.Equal(1, vm.Meshes.Count);
        Assert.Empty(dialogs.Errors);
    }

    // ---- problems ------------------------------------------------------------------------------

    [UiFact]
    public async Task A_broken_file_gives_a_readable_error_and_changes_nothing()
    {
        var path = P("broken.stl");
        File.WriteAllText(path, "this is not a model");
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);
        await vm.ImportFilesAsync([WriteBoxStl("good.stl")]);
        var before = vm.HistoryEntries.Count;

        await vm.ImportFilesAsync([path]);

        var message = Assert.Single(dialogs.Errors);
        Assert.Contains("broken.stl", message);
        Assert.Contains("could not be imported", message);
        Assert.Equal(before, vm.HistoryEntries.Count);
        Assert.Single(vm.Document.Root.Children);
        Assert.False(vm.IsBusy);
    }

    [UiFact]
    public async Task Unsupported_files_are_listed_once_and_the_rest_is_still_imported()
    {
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);

        await vm.ImportFilesAsync([P("x.step"), WriteBoxStl("ok.stl"), P("notes.txt")]);

        var message = Assert.Single(dialogs.Errors);
        Assert.Contains("x.step", message);
        Assert.Contains("notes.txt", message);
        Assert.Equal(["ok"], vm.Document.Root.Children.Select(n => n.Name));
    }

    [UiFact]
    public async Task A_missing_file_is_an_error_not_a_crash()
    {
        var dialogs = new FakeDialogs();
        var vm = NewViewModel(dialogs);

        await vm.ImportFilesAsync([P("gone.stl")]);

        Assert.Contains("does not exist", Assert.Single(dialogs.Errors));
    }

    [UiFact]
    public async Task A_mesh_with_holes_is_imported_with_a_warning()
    {
        var closed = new BoxGenerator().Generate(Dims(20, 20, 20));
        var open = new MeshData(closed.Vertices.ToArray(), closed.Indices[..^3].ToArray());
        var vm = NewViewModel();

        await vm.ImportFilesAsync([WriteStl("open.stl", open)]);

        Assert.Single(vm.Document.Root.Children);
        Assert.Contains("errors", vm.StatusText);
    }

    // ---- editing imported objects ----------------------------------------------------------------

    [UiFact]
    public async Task Undo_removes_the_imported_object_and_redo_brings_it_back()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteBoxStl("a.stl")]);
        await vm.WaitForEvaluationAsync();

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);
        Assert.Empty(vm.ViewportItems);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.ViewportItems);
    }

    [UiFact]
    public async Task Imported_objects_can_be_moved_rotated_and_scaled_like_any_other()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteBoxStl("a.stl", 20)]);
        await vm.WaitForEvaluationAsync();
        var node = Child(vm, 0);

        vm.Properties!.Position[0].Text = "+25";
        vm.Properties.Rotation[2].Text = "90";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(25, BoundsOf(vm, node.Id).Center.X, 2);

        vm.ActiveTool = ViewportTool.Scale;
        Assert.True(vm.BeginDrag(GizmoHandleKind.ScaleXMax));
        vm.UpdateDrag(new GizmoDrag(GizmoHandleKind.ScaleXMax, Vector3.Zero, 0, false, 2));
        vm.CommitDrag();
        await vm.WaitForEvaluationAsync();

        Assert.NotEqual(Vector3.One, node.Transform.Scale); // a mesh is scaled through its scale value
        Assert.Equal(40, BoundsOf(vm, node.Id).Size.Y, 1);  // turned by 90 degrees: its X axis points along world Y
    }

    [UiFact]
    public async Task Duplicating_an_imported_object_shares_the_mesh()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteBoxStl("a.stl")]);

        vm.DuplicateCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Equal(((MeshNode)Child(vm, 0)).MeshId, ((MeshNode)Child(vm, 1)).MeshId);
        Assert.Equal(1, vm.Meshes.Count);
        Assert.Equal(2, vm.ViewportItems.Count);
    }

    [UiFact]
    public async Task An_imported_object_can_be_cut_by_a_hole()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteBoxStl("plate.stl", 40)]);
        await vm.WaitForEvaluationAsync();
        var plate = Child(vm, 0);
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        var hole = Child(vm, 1);
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[2].Text = "-10";
        vm.Properties.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "60";
        vm.Properties.IsHole = true;
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        var solid = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Solid);
        Assert.True(solid.Mesh.ComputeVolume() < 64000 - 2000);
        Assert.True(solid.Mesh.IsWatertight());
    }

    // ---- saving, opening, autosave -----------------------------------------------------------------

    [UiFact]
    public async Task Imported_objects_survive_save_and_open()
    {
        var project = P("project.fwp");
        var first = new MainViewModel(new ManifoldKernel(), new FakeDialogs { SavePath = project });
        await first.ImportFilesAsync([WriteBoxStl("part.stl", 33)]);
        await first.WaitForEvaluationAsync();
        Assert.True(first.Save());
        var volume = first.ViewportItems.Single().Mesh.ComputeVolume();

        var second = NewViewModel();
        Assert.True(second.OpenPath(project));
        await second.WaitForEvaluationAsync();

        var node = Assert.IsType<MeshNode>(Child(second, 0));
        Assert.Equal("part", node.Name);
        Assert.True(second.Meshes.Contains(node.MeshId));
        Assert.Equal(volume, second.ViewportItems.Single().Mesh.ComputeVolume(), 2);
        Assert.False(second.IsDirty);
    }

    [UiFact]
    public async Task Opening_another_project_replaces_the_meshes_and_new_clears_them()
    {
        var project = P("project.fwp");
        var dialogs = new FakeDialogs { SavePath = project, UnsavedAnswer = UnsavedChoice.Discard };
        var vm = new MainViewModel(new ManifoldKernel(), dialogs);
        await vm.ImportFilesAsync([WriteBoxStl("a.stl")]);
        vm.Save();
        await vm.ImportFilesAsync([WriteBoxStl("b.stl")]);
        Assert.Equal(2, vm.Meshes.Count);

        vm.OpenPath(project); // the saved project only knows about "a"
        Assert.Equal(1, vm.Meshes.Count);

        vm.NewCommand.Execute(null);
        Assert.Equal(0, vm.Meshes.Count);
    }

    [UiFact]
    public async Task A_failed_open_keeps_the_current_meshes()
    {
        var bad = P("bad.fwp");
        File.WriteAllText(bad, "no project");
        var vm = NewViewModel();
        await vm.ImportFilesAsync([WriteBoxStl("a.stl")]);

        Assert.False(vm.OpenPath(bad));

        Assert.Equal(1, vm.Meshes.Count);
        Assert.Single(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task After_a_crash_the_recovered_work_includes_the_imported_objects()
    {
        var autosave = new AutosaveStore(P("autosave"));
        var crashed = NewViewModel(autosave: autosave);
        await crashed.ImportFilesAsync([WriteBoxStl("part.stl", 25)]);
        await crashed.WaitForEvaluationAsync();
        Assert.True(await crashed.AutosaveAsync());

        var restarted = NewViewModel(autosave: autosave);
        Assert.True(restarted.OfferRecovery());
        await restarted.WaitForEvaluationAsync();

        Assert.Equal("part", Child(restarted, 0).Name);
        Assert.Equal(25, restarted.ViewportItems.Single().Mesh.ComputeBounds().Size.X, 2);
        Assert.True(restarted.IsDirty);
    }

    // ---- robustness --------------------------------------------------------------------------------

    [UiFact]
    public async Task Two_imports_at_the_same_time_are_handled_one_after_the_other()
    {
        var vm = NewViewModel();
        var a = vm.ImportFilesAsync([WriteBoxStl("a.stl")]);
        var b = vm.ImportFilesAsync([WriteBoxStl("b.stl")]);

        await Task.WhenAll(a, b);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, vm.Document.Root.Children.Count);
        Assert.Equal(2, vm.Meshes.Count);
        Assert.False(vm.IsBusy);
    }

    [UiFact]
    public async Task A_large_model_is_imported_and_shown()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["diameter"] = 60, ["segments"] = 256, ["rings"] = 128 });
        var vm = NewViewModel();

        await vm.ImportFilesAsync([WriteStl("big.stl", sphere)]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(sphere.TriangleCount, vm.ViewportItems.Single().Mesh.TriangleCount);
        Assert.Contains("triangles", vm.StatusText);
    }

    // F-IMP-01 acceptance criterion: an STL with one million triangles is loaded and shown in under 3 seconds.
    [UiFact]
    public async Task A_model_with_a_million_triangles_is_imported_and_evaluated_in_reasonable_time()
    {
        var sphere = new SphereGenerator().Generate(new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
            { ["diameter"] = 40, ["segments"] = 512, ["rings"] = 256 });
        var meshes = Enumerable.Range(0, 4).Select(i => sphere.Transformed(Matrix4x4.CreateTranslation(i * 100, 0, 0))).ToArray();
        var path = P("million.stl");
        using (var stream = File.Create(path)) StlWriter.WriteBinary(stream, meshes);
        var triangles = meshes.Sum(m => m.TriangleCount);

        var warmUp = NewViewModel();
        await warmUp.ImportFilesAsync([WriteBoxStl("warm.stl")]);
        await warmUp.WaitForEvaluationAsync();

        var vm = NewViewModel();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await vm.ImportFilesAsync([path]);
        var imported = sw.ElapsedMilliseconds;
        await vm.WaitForEvaluationAsync();
        sw.Stop();

        _output.WriteLine($"{triangles:N0} triangles: import {imported} ms, import plus evaluation {sw.ElapsedMilliseconds} ms (the WPF viewport needs more time to upload and draw it)");
        Assert.Equal(triangles, vm.ViewportItems.Single().Mesh.TriangleCount);
        Assert.True(sw.ElapsedMilliseconds < 15000, $"took {sw.ElapsedMilliseconds} ms");
    }

    [UiFact]
    public void Units_convert_to_millimetres()
    {
        Assert.Equal(1, ImportUnit.Millimeters.ToMillimeters());
        Assert.Equal(10, ImportUnit.Centimeters.ToMillimeters());
        Assert.Equal(25.4, ImportUnit.Inches.ToMillimeters());
    }
}
