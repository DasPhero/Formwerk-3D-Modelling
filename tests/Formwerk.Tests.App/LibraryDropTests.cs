using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Dragging an element of the library into the view: on a face it stands on it along its normal (F-LIB-04).</summary>
public sealed class LibraryDropTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task Drop(MainViewModel vm, string itemId, FacePick pick)
    {
        vm.DropLibraryItemCommand.Execute(new LibraryDropRequest(itemId, pick));
        await vm.WaitForEvaluationAsync();
    }

    private static FacePick Plate(float x, float y, bool noSnap = false) => new(Guid.Empty, new Vector3(x, y, 0), Vector3.UnitZ, noSnap);

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Children[^1];
    }

    private static List<Vector3> VerticesOf(MainViewModel vm, Node node)
    {
        var ids = node.Descendants().Append(node).Select(n => n.Id).ToHashSet();
        return [.. vm.ViewportItems.Where(i => ids.Contains(i.NodeId) && i.Kind == ViewportItemKind.Solid).SelectMany(i => i.Mesh.Vertices.ToArray())];
    }

    // ---- on the plate ----------------------------------------------------------------------------

    [UiFact]
    public async Task A_drop_on_the_plate_puts_the_element_upright_at_the_snapped_mouse_position()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;

        await Drop(vm, "cylinder", Plate(23.4f, -7.6f));

        var node = Assert.IsType<PrimitiveNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal("Cylinder", node.Name);
        Assert.Equal(new Vector3(23, -8, 0), node.Transform.Position); // the grid is 1 mm
        Assert.Equal(Quaternion.Identity, node.Transform.Rotation);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([node.Id], vm.SelectedIds.ToArray());
    }

    [UiFact]
    public async Task The_grid_step_decides_and_alt_or_no_grid_snap_keeps_the_exact_point()
    {
        var vm = NewViewModel();
        vm.GridStep = 5;
        await Drop(vm, "box", Plate(23.4f, -7.6f));
        Assert.Equal(new Vector3(25, -10, 0), vm.Document.Root.Children[0].Transform.Position);

        await Drop(vm, "box", Plate(23.4f, -7.6f, noSnap: true));
        Assert.Equal(new Vector3(23.4f, -7.6f, 0), vm.Document.Root.Children[1].Transform.Position);

        vm.SnapToGrid = false;
        await Drop(vm, "box", Plate(23.4f, -7.6f));
        Assert.Equal(new Vector3(23.4f, -7.6f, 0), vm.Document.Root.Children[2].Transform.Position);
    }

    // ---- on a face -------------------------------------------------------------------------------

    [UiFact]
    public async Task A_cylinder_dropped_on_a_slanted_face_stands_perpendicular_on_it()
    {
        var vm = NewViewModel();
        var wedge = await Add(vm, "wedge");
        var normal = Vector3.Normalize(new Vector3(0, -1, 1)); // the ramp of the wedge rises towards the back
        var point = new Vector3(0, 0, 10);

        await Drop(vm, "cylinder", new FacePick(wedge.Id, point, normal));

        var cylinder = vm.Document.Root.Children[^1];
        Assert.Equal("Cylinder", cylinder.Name);
        Assert.Equal(point, cylinder.Transform.Position);
        var up = Vector3.Transform(Vector3.UnitZ, cylinder.Transform.Rotation);
        Assert.True(Vector3.Distance(normal, up) < 1e-4f, $"up axis {up}");

        // the mesh: its base lies in the face (nothing below it), its height runs along the normal, its width across it
        var vertices = VerticesOf(vm, cylinder);
        var along = vertices.Select(v => Vector3.Dot(v - point, normal)).ToList();
        Assert.Equal(0, along.Min(), 2);
        Assert.Equal(20, along.Max(), 2); // the height of the default cylinder
        Assert.All(vertices, v => Assert.True((v - point - normal * Vector3.Dot(v - point, normal)).Length() <= 10.01f)); // radius 10 around the axis
    }

    [UiFact]
    public async Task A_ready_made_element_dropped_on_a_side_face_lies_along_its_normal()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var origin = box.Transform.Position;

        await Drop(vm, "hex-nut-m3", new FacePick(box.Id, origin + new Vector3(10, 0, 10), Vector3.UnitX));

        var nut = vm.Document.Root.Children[^1];
        Assert.Equal("Hex Nut M3", nut.Name);
        var xs = VerticesOf(vm, nut).Select(v => v.X).ToList();
        Assert.Equal(origin.X + 10, xs.Min(), 2); // the nut sits on the right face and sticks out 2.4 mm
        Assert.Equal(origin.X + 12.4f, xs.Max(), 2);
    }

    [UiFact]
    public async Task A_downward_face_makes_the_element_hang_down()
    {
        var vm = NewViewModel();

        await Drop(vm, "cylinder", new FacePick(Guid.NewGuid(), new Vector3(0, 0, 30), -Vector3.UnitZ));

        var cylinder = vm.Document.Root.Children[0];
        var zs = VerticesOf(vm, cylinder).Select(v => v.Z).ToList();
        Assert.Equal(30, zs.Max(), 2);
        Assert.Equal(10, zs.Min(), 2);
    }

    // ---- other cases -----------------------------------------------------------------------------

    [UiFact]
    public async Task An_unknown_element_or_no_request_changes_nothing()
    {
        var vm = NewViewModel();
        var steps = vm.HistoryEntries.Count;

        await Drop(vm, "no-such-element", Plate(0, 0));
        vm.DropLibraryItemCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.Document.Root.Children);
        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    [UiFact]
    public async Task Undo_takes_the_dropped_element_away_and_the_name_stays_unique()
    {
        var vm = NewViewModel();

        await Drop(vm, "cylinder", Plate(0, 0));
        await Drop(vm, "cylinder", Plate(30, 0));
        Assert.Equal(["Cylinder", "Cylinder 2"], vm.Document.Root.Children.Select(n => n.Name).ToArray());

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.Document.Root.Children);

        vm.RedoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Equal(2, vm.Document.Root.Children.Count);
    }

    [UiFact]
    public async Task The_turn_of_a_dropped_element_survives_saving_and_loading()
    {
        var vm = NewViewModel();
        var normal = Vector3.Normalize(new Vector3(0, -1, 1));
        await Drop(vm, "cylinder", new FacePick(Guid.NewGuid(), new Vector3(1, 2, 3), normal));
        var node = vm.Document.Root.Children[0];
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        var loaded = back.FindNode(node.Id)!;
        Assert.True(Vector3.Distance(normal, Vector3.Transform(Vector3.UnitZ, loaded.Transform.Rotation)) < 1e-3f);
        Assert.Equal(new Vector3(1, 2, 3), loaded.Transform.Position);
    }
}
