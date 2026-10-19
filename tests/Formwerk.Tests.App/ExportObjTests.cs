using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>File > Export OBJ (E-03): the same result as the STL and 3MF export, as Wavefront OBJ.</summary>
public sealed class ExportObjTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-obj-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public ExportObjTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private string PathOf(string name) => Path.Combine(_folder, name);

    private MainViewModel NewViewModel(string? savePath) =>
        new(new ManifoldKernel(), new FakeDialogs { SavePath = savePath });

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    private static async Task<(MainViewModel Vm, Node Plate, Node Hole)> PlateWithHole(MainViewModel vm)
    {
        await Add(vm, "box");
        var plate = vm.Document.Root.Children[0];
        vm.Properties!.Parameters[0].Text = "40";
        vm.Properties.Parameters[1].Text = "40";
        vm.Properties.Parameters[2].Text = "10";
        await Add(vm, "cylinder");
        var hole = vm.Document.Root.Children[1];
        vm.Properties!.Position[0].Text = "0";
        vm.Properties.Position[1].Text = "0";
        vm.Properties.Position[2].Text = "-5";
        vm.Properties.Parameters[0].Text = "10";
        vm.Properties.Parameters[1].Text = "30";
        vm.Properties.IsHole = true;
        await vm.WaitForEvaluationAsync();
        return (vm, plate, hole);
    }

    private static string[] Lines(string path) => File.ReadAllText(path).Split('\n', StringSplitOptions.RemoveEmptyEntries);

    private static double PolygonArea(double radius) => 0.5 * 64 * radius * radius * Math.Sin(2 * Math.PI / 64);

    private static async Task Export(MainViewModel vm)
    {
        vm.ExportObjCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        await Task.Delay(50); // the file is written on a background thread
    }

    /// <summary>The new content is in place (read while the program replaces the file: a failed read means not yet).</summary>
    private static bool IsReplaced(string path)
    {
        try { return File.ReadAllText(path) != "old content" && !File.Exists(path + ".tmp"); }
        catch (IOException) { return false; }
    }

    private static async Task WaitForFile(string path)
    {
        for (var i = 0; i < 100 && !File.Exists(path); i++) await Task.Delay(50);
    }

    [UiFact]
    public async Task Every_visible_solid_becomes_a_named_object_in_the_file()
    {
        var path = PathOf("two.obj");
        var vm = NewViewModel(path);
        await Add(vm, "box");
        await Add(vm, "sphere");

        vm.ExportObjCommand.Execute(null);
        await WaitForFile(path);

        var lines = Lines(path);
        Assert.Equal(["o Box", "o Sphere"], lines.Where(l => l.StartsWith("o ")));
        Assert.Equal(vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.TriangleCount), lines.Count(l => l.StartsWith("f ")));
        Assert.Equal(vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.VertexCount), lines.Count(l => l.StartsWith("v ")));
    }

    [UiFact]
    public async Task A_hole_in_a_group_is_subtracted_in_the_obj_file()
    {
        var path = PathOf("plate.obj");
        var (vm, plate, hole) = await PlateWithHole(NewViewModel(path));
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.ExportObjCommand.Execute(null);
        await WaitForFile(path);

        using var stream = File.OpenRead(path);
        var mesh = ObjReader.Read(stream);
        var expected = 40 * 40 * 10 - PolygonArea(5) * 10;
        Assert.InRange(mesh.ComputeVolume(), expected - 0.5, expected + 0.5);
        Assert.True(mesh.TriangleCount > 12);
    }

    [UiFact]
    public async Task A_loose_hole_is_not_exported()
    {
        var path = PathOf("loose.obj");
        var (vm, _, _) = await PlateWithHole(NewViewModel(path));

        vm.ExportObjCommand.Execute(null);
        await WaitForFile(path);

        var lines = Lines(path);
        Assert.Equal(["o Box"], lines.Where(l => l.StartsWith("o ")));
        Assert.Equal(12, lines.Count(l => l.StartsWith("f ")));
    }

    [UiFact]
    public async Task The_file_name_gets_the_obj_extension_when_the_user_left_it_out()
    {
        var vm = NewViewModel(PathOf("model"));
        await Add(vm, "box");

        vm.ExportObjCommand.Execute(null);
        await WaitForFile(PathOf("model.obj"));

        Assert.True(File.Exists(PathOf("model.obj")));
        Assert.False(File.Exists(PathOf("model")));
    }

    [UiFact]
    public async Task The_status_bar_tells_how_many_objects_went_where()
    {
        var path = PathOf("status.obj");
        var vm = NewViewModel(path);
        await Add(vm, "box");
        await Add(vm, "cylinder");

        vm.ExportObjCommand.Execute(null);
        await WaitForFile(path);
        await Task.Delay(100);

        Assert.Contains("2", vm.StatusText);
        Assert.Contains(path, vm.StatusText);
    }

    [UiFact]
    public async Task Cancelling_the_dialog_writes_nothing()
    {
        var vm = NewViewModel(null);
        await Add(vm, "box");

        vm.ExportObjCommand.Execute(null);
        await Task.Delay(100);

        Assert.Empty(Directory.GetFiles(_folder));
    }

    [UiFact]
    public void With_nothing_to_export_the_command_is_not_available()
    {
        var vm = NewViewModel(PathOf("none.obj"));

        Assert.False(vm.ExportObjCommand.CanExecute(null));
    }

    [UiFact]
    public async Task An_existing_file_is_replaced_and_no_temporary_file_is_left()
    {
        var path = PathOf("again.obj");
        File.WriteAllText(path, "old content");
        var vm = NewViewModel(path);
        await Add(vm, "box");

        vm.ExportObjCommand.Execute(null);
        for (var i = 0; i < 100 && !IsReplaced(path); i++) await Task.Delay(50);

        Assert.StartsWith("# Formwerk", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }

    [UiFact]
    public async Task A_file_that_cannot_be_written_gives_a_message_and_no_crash()
    {
        var blocker = PathOf("blocker");
        File.WriteAllText(blocker, "I am a file, not a folder");
        var vm = NewViewModel(Path.Combine(blocker, "model.obj")); // the "folder" is a file
        await Add(vm, "box");

        vm.ExportObjCommand.Execute(null);
        for (var i = 0; i < 100 && !vm.StatusText.Contains("could not be saved"); i++) await Task.Delay(50);

        Assert.Contains("could not be saved", vm.StatusText);
        Assert.Equal("I am a file, not a folder", File.ReadAllText(blocker));
    }

    [Fact]
    public void SaveAtomic_in_the_writer_replaces_the_file_in_one_piece()
    {
        var path = PathOf("direct.obj");
        File.WriteAllText(path, "old");

        ObjWriter.SaveAtomic(path, [new ObjObject("A", new Formwerk.Geometry.MeshData([System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitX, System.Numerics.Vector3.UnitY], [0, 1, 2]))]);

        Assert.Contains("o A", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_folder));
    }
}
