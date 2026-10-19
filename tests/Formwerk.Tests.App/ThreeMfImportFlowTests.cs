using System.IO;
using System.Numerics;
using Formwerk.App.Resources;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>Importing 3MF files through the view model (F-IMP-01, F-IMP-02, F-FLR-01).</summary>
public sealed class ThreeMfImportFlowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-import3mf-app-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public ThreeMfImportFlowTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static MeshData Box(double w, double d, double h) =>
        new BoxGenerator().Generate(new Dictionary<string, double> { ["width"] = w, ["depth"] = d, ["height"] = h });

    private string Write(string name, params ThreeMfObject[] objects)
    {
        var path = Path.Combine(_folder, name);
        ThreeMfWriter.SaveAtomic(path, objects);
        return path;
    }

    private static MeshData At(MeshData mesh, float x, float y, float z) => mesh.Transformed(Matrix4x4.CreateTranslation(x, y, z));

    private static Bounds WorldBounds(MainViewModel vm, Guid nodeId)
    {
        var parts = vm.ViewportItems.Where(i => i.NodeId == nodeId && !i.Mesh.IsEmpty).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(parts.Select(b => b.Min).Aggregate(Vector3.Min), parts.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    // ---- one object ------------------------------------------------------------------------------

    [UiFact]
    public async Task A_single_object_becomes_a_plain_object_with_its_name_and_color()
    {
        var vm = NewViewModel();
        var path = Write("part.3mf", new ThreeMfObject("Bracket", At(Box(30, 20, 10), 100, 100, 50), 0xFF3366CC));

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        var node = Assert.IsType<MeshNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("Bracket", node.Name);
        Assert.Equal(0xFF3366CCu, node.Color);
        var bounds = WorldBounds(vm, node.Id);
        Assert.Equal(0, bounds.Min.Z, 3);               // lands on the plate
        Assert.Equal(30, bounds.Size.X, 3);
        Assert.Contains("Imported", vm.StatusText);
        Assert.Equal([node.Id], vm.SelectedIds.ToArray());
    }

    [UiFact]
    public async Task The_unit_of_a_3mf_file_is_trusted_and_no_question_is_asked()
    {
        var vm = NewViewModel();
        // 0.5 mm would be "too small" for an STL and trigger the unit question; a 3MF says what it is
        var path = Write("tiny.3mf", new ThreeMfObject("Tiny", Box(0.5, 0.5, 0.5), 0xFF000000));

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        Assert.Empty(_dialogs.UnitPrompts);
        var node = vm.Document.Root.Children.Single();
        Assert.Equal(0.5, WorldBounds(vm, node.Id).Size.X, 3);
    }

    [UiFact]
    public async Task A_file_in_inches_is_converted_to_millimeters()
    {
        var model = "<model unit=\"inch\" xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources><object id=\"1\" type=\"model\" name=\"Inch part\"><mesh><vertices>"
            + "<vertex x=\"0\" y=\"0\" z=\"0\"/><vertex x=\"1\" y=\"0\" z=\"0\"/><vertex x=\"0\" y=\"1\" z=\"0\"/><vertex x=\"0\" y=\"0\" z=\"1\"/></vertices>"
            + "<triangles><triangle v1=\"0\" v2=\"2\" v3=\"1\"/><triangle v1=\"0\" v2=\"1\" v3=\"3\"/><triangle v1=\"1\" v2=\"2\" v3=\"3\"/><triangle v1=\"2\" v2=\"0\" v3=\"3\"/></triangles>"
            + "</mesh></object></resources><build><item objectid=\"1\"/></build></model>";
        var path = Path.Combine(_folder, "inch.3mf");
        using (var stream = File.Create(path))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open())) writer.Write(model);
        var vm = NewViewModel();

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(25.4, WorldBounds(vm, vm.Document.Root.Children.Single().Id).Size.X, 2);
        Assert.Empty(_dialogs.UnitPrompts);
    }

    // ---- several objects -------------------------------------------------------------------------

    [UiFact]
    public async Task Several_objects_become_one_group_that_keeps_their_arrangement()
    {
        var vm = NewViewModel();
        var path = Write("assembly.3mf",
            new ThreeMfObject("Base", At(Box(40, 40, 10), 200, 200, 0), 0xFF112233),
            new ThreeMfObject("Lid", At(Box(20, 20, 5), 250, 200, 10), 0xFF445566));

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("assembly", group.Name);
        Assert.Equal(["Base", "Lid"], group.Children.Select(c => c.Name).ToArray());
        Assert.Equal([0xFF112233u, 0xFF445566u], group.Children.Select(c => c.Color).ToArray());

        var baseBounds = WorldBounds(vm, group.Children[0].Id);
        var lidBounds = WorldBounds(vm, group.Children[1].Id);
        Assert.Equal(0, baseBounds.Min.Z, 3);                                   // the assembly stands on the plate
        Assert.Equal(10, lidBounds.Min.Z, 3);                                   // the lid still sits on top of the base
        Assert.Equal(50, lidBounds.Center.X - baseBounds.Center.X, 3);          // and 50 mm to the side, as in the file
        Assert.Equal(0, lidBounds.Center.Y - baseBounds.Center.Y, 3);
        Assert.Equal([group.Id], vm.SelectedIds.ToArray());
    }

    [UiFact]
    public async Task The_whole_group_is_added_and_removed_in_one_undo_step()
    {
        var vm = NewViewModel();
        var before = vm.HistoryEntries.Count;
        var path = Write("two.3mf", new ThreeMfObject("A", Box(10, 10, 10), 0xFF000000), new ThreeMfObject("B", At(Box(10, 10, 10), 30, 0, 0), 0xFF000000));

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(before + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.Document.Root.Children);
        Assert.Empty(vm.ViewportItems);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(2, group.Children.Count);
        Assert.Equal(2, vm.ViewportItems.Count(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid));
    }

    [UiFact]
    public async Task Importing_the_same_file_twice_gives_unique_names_and_separate_places()
    {
        var vm = NewViewModel();
        var path = Write("pair.3mf", new ThreeMfObject("Left", Box(10, 10, 10), 0xFF000000), new ThreeMfObject("Right", At(Box(10, 10, 10), 20, 0, 0), 0xFF000000));

        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();
        await vm.ImportFilesAsync([path]);
        await vm.WaitForEvaluationAsync();

        var names = vm.Document.Root.Descendants().Select(n => n.Name).ToList();
        Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var first = WorldBounds(vm, vm.Document.Root.Children[0].Children[0].Id);
        var second = WorldBounds(vm, vm.Document.Root.Children[1].Children[0].Id);
        Assert.False(first.Min.X < second.Max.X && first.Max.X > second.Min.X, "the second import landed on the first");
    }

    [UiFact]
    public async Task Stl_and_3mf_files_can_be_imported_together()
    {
        var stl = Path.Combine(_folder, "plate.stl");
        using (var stream = File.Create(stl)) StlWriter.WriteBinary(stream, [Box(20, 20, 20)]);
        var vm = NewViewModel();

        await vm.ImportFilesAsync([stl, Write("part.3mf", new ThreeMfObject("Part", Box(10, 10, 10), 0xFF000000))]);
        await vm.WaitForEvaluationAsync();

        Assert.Equal(["plate", "Part"], vm.Document.Root.Children.Select(n => n.Name).ToArray());
        Assert.Equal(2, vm.SelectedIds.Count);
    }

    // ---- errors ----------------------------------------------------------------------------------

    [UiFact]
    public async Task A_broken_3mf_file_is_reported_and_changes_nothing()
    {
        var path = Path.Combine(_folder, "broken.3mf");
        File.WriteAllText(path, "this is not a 3mf file");
        var vm = NewViewModel();

        await vm.ImportFilesAsync([path]);

        var message = Assert.Single(_dialogs.Errors);
        Assert.Contains("broken.3mf", message);
        Assert.Contains("3MF", message);
        Assert.Empty(vm.Document.Root.Children);
    }

    [UiFact]
    public async Task A_hostile_3mf_file_is_refused_with_a_message()
    {
        var secret = Path.Combine(_folder, "secret.txt");
        File.WriteAllText(secret, "TOP-SECRET-CONTENT");
        var model = $"<?xml version=\"1.0\"?><!DOCTYPE model [<!ENTITY xxe SYSTEM \"{new Uri(secret).AbsoluteUri}\">]><model xmlns=\"http://schemas.microsoft.com/3dmanufacturing/core/2015/02\"><resources><object id=\"1\" name=\"&xxe;\"/></resources><build><item objectid=\"1\"/></build></model>";
        var path = Path.Combine(_folder, "xxe.3mf");
        using (var stream = File.Create(path))
        using (var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("3D/3dmodel.model").Open())) writer.Write(model);
        var vm = NewViewModel();

        await vm.ImportFilesAsync([path]);

        Assert.DoesNotContain("TOP-SECRET-CONTENT", Assert.Single(_dialogs.Errors));
        Assert.Empty(vm.Document.Root.Children);
    }

    // ---- with the rest of the program ------------------------------------------------------------

    [UiFact]
    public async Task What_the_program_exports_as_3mf_can_be_imported_again()
    {
        var first = NewViewModel();
        first.AddShapeCommand.Execute("box");
        await first.WaitForEvaluationAsync();
        first.AddShapeCommand.Execute("cylinder");
        await first.WaitForEvaluationAsync();
        var exported = Path.Combine(_folder, "scene.3mf");
        _dialogs.SavePath = exported;
        await first.Export3mfCommand.ExecuteAsync(null);

        var second = NewViewModel();
        await second.ImportFilesAsync([exported]);
        await second.WaitForEvaluationAsync();

        var group = Assert.IsType<GroupNode>(Assert.Single(second.Document.Root.Children));
        Assert.Equal(["Box", "Cylinder"], group.Children.Select(c => c.Name).ToArray());
        var original = first.ViewportItems.Where(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());
        var imported = second.ViewportItems.Where(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());
        Assert.Equal(original, imported, 2);
    }

    [UiFact]
    public async Task An_imported_group_survives_save_and_open()
    {
        var vm = NewViewModel();
        await vm.ImportFilesAsync([Write("two.3mf", new ThreeMfObject("A", Box(10, 10, 10), 0xFF123456), new ThreeMfObject("B", At(Box(5, 5, 5), 30, 0, 0), 0xFF654321))]);
        await vm.WaitForEvaluationAsync();
        var project = Path.Combine(_folder, "saved.fwp");
        _dialogs.SavePath = project;
        Assert.True(vm.Save());

        var other = NewViewModel();
        Assert.True(other.OpenPath(project));
        await other.WaitForEvaluationAsync();

        var group = Assert.IsType<GroupNode>(Assert.Single(other.Document.Root.Children));
        Assert.Equal([0xFF123456u, 0xFF654321u], group.Children.Select(c => c.Color).ToArray());
        Assert.Equal(2, other.ViewportItems.Count(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid));
    }

    [UiFact]
    public void The_file_dialog_and_the_unsupported_message_mention_3mf()
    {
        Assert.Contains("*.3mf", Texts.Get("Dlg.ImportFilter"));
        Assert.Contains("3MF", Texts.Get("Dlg.ImportUnsupported"));
    }
}
