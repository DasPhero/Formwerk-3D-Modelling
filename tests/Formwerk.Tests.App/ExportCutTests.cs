using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>What ends up in the STL file: the calculated result, with holes already subtracted (E-03).</summary>
public sealed class ExportCutTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"formwerk-export-{Guid.NewGuid():N}.stl");

    public void Dispose() => File.Delete(_path);

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs { SavePath = _path });

    private static async Task Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
    }

    /// <summary>A 40 x 40 x 10 plate and a 10 mm cylinder (64 sides) through it, set to be a hole. Both stay unselected-grouped.</summary>
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
        vm.Properties.Parameters[0].Text = "10"; // diameter
        vm.Properties.Parameters[1].Text = "30"; // height, sticks out on both sides
        vm.Properties.IsHole = true;
        await vm.WaitForEvaluationAsync();
        return (vm, plate, hole);
    }

    /// <summary>The 64-gon the cylinder generator makes, not a true circle.</summary>
    private static double PolygonArea(double radius) => 0.5 * 64 * radius * radius * Math.Sin(2 * Math.PI / 64);

    private static (int Triangles, double Volume) ReadStl(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var count = (int)BitConverter.ToUInt32(bytes, 80);
        Assert.Equal(StlWriter.SizeFor(count), bytes.Length);

        double volume = 0;
        for (var t = 0; t < count; t++)
        {
            var o = 84 + t * 50 + 12; // skip the normal
            var a = new Vector3(BitConverter.ToSingle(bytes, o), BitConverter.ToSingle(bytes, o + 4), BitConverter.ToSingle(bytes, o + 8));
            var b = new Vector3(BitConverter.ToSingle(bytes, o + 12), BitConverter.ToSingle(bytes, o + 16), BitConverter.ToSingle(bytes, o + 20));
            var c = new Vector3(BitConverter.ToSingle(bytes, o + 24), BitConverter.ToSingle(bytes, o + 28), BitConverter.ToSingle(bytes, o + 32));
            volume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        return (count, volume);
    }

    [UiFact]
    public async Task A_hole_in_a_group_is_subtracted_in_the_exported_file()
    {
        var (vm, plate, hole) = await PlateWithHole(NewViewModel());
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        var (triangles, volume) = ReadStl(_path);
        var expected = 40 * 40 * 10 - PolygonArea(5) * 10;
        Assert.InRange(volume, expected - 0.5, expected + 0.5);
        Assert.True(triangles > 12, "the plate has a hole, so it has more than the 12 triangles of a plain box");
        // the transparent ghost of the hole is not part of the file: only the cut plate is
        Assert.Equal(vm.ViewportItems.Single(i => i.Kind == Formwerk.Viewport.ViewportItemKind.Solid).Mesh.TriangleCount, triangles);
    }

    [UiFact]
    public async Task A_boolean_difference_is_exported_as_one_cut_body()
    {
        var (vm, plate, hole) = await PlateWithHole(NewViewModel());
        vm.IsRotateTool = false; // keep the default tool; irrelevant for the export
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Difference);
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        var (_, volume) = ReadStl(_path);
        var expected = 40 * 40 * 10 - PolygonArea(5) * 10;
        Assert.InRange(volume, expected - 0.5, expected + 0.5);
    }

    [UiFact]
    public async Task A_merge_with_a_hole_is_exported_cut()
    {
        var (vm, plate, hole) = await PlateWithHole(NewViewModel());
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.MergeCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        var (_, volume) = ReadStl(_path);
        var expected = 40 * 40 * 10 - PolygonArea(5) * 10;
        Assert.InRange(volume, expected - 0.5, expected + 0.5);
    }

    // A hole that is not grouped with anything has nothing to cut: neither the cut nor the hole is in the file.
    [UiFact]
    public async Task A_loose_hole_cuts_nothing_and_is_not_exported()
    {
        var (vm, _, _) = await PlateWithHole(NewViewModel());

        vm.ExportStlCommand.Execute(null);

        var (triangles, volume) = ReadStl(_path);
        Assert.Equal(12, triangles);
        Assert.Equal(40 * 40 * 10, volume, 1);
    }

    [UiFact]
    public async Task Hidden_objects_are_not_exported()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "sphere");
        vm.Nodes[1].Visible = false;
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        Assert.Equal(12, ReadStl(_path).Triangles); // only the box
    }

    [UiFact]
    public async Task Separate_objects_are_written_into_one_file_without_being_merged()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        await Add(vm, "box");

        vm.ExportStlCommand.Execute(null);

        var (triangles, volume) = ReadStl(_path);
        Assert.Equal(24, triangles);
        Assert.Equal(2 * 20 * 20 * 20, volume, 1);
    }

    [UiFact]
    public async Task The_export_follows_the_latest_edit_of_the_hole()
    {
        var (vm, plate, hole) = await PlateWithHole(NewViewModel());
        vm.SelectOnly([plate.Id, hole.Id]);
        vm.GroupCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([hole.Id]);
        vm.Properties!.Parameters[0].Text = "20"; // a bigger hole
        await vm.WaitForEvaluationAsync();

        vm.ExportStlCommand.Execute(null);

        var (_, volume) = ReadStl(_path);
        var expected = 40 * 40 * 10 - PolygonArea(10) * 10;
        Assert.InRange(volume, expected - 0.5, expected + 0.5);
    }
}
