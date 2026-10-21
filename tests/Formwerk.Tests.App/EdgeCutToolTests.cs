using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The chamfer and fillet tool for selected edges (F-EDG-02).</summary>
public class EdgeCutToolTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static async Task<PrimitiveNode> Add(MainViewModel vm, string shape, params (string Name, string Value)[] parameters)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = (PrimitiveNode)vm.Document.Root.Children[^1];
        foreach (var (name, value) in parameters) node.Parameters[name] = value;
        vm.SelectOnly([node.Id]);
        if (parameters.Length > 0) vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();
        return node;
    }

    /// <summary>What is printed: the volume of all solid parts (holes and their ghosts do not count).</summary>
    private static double SolidVolume(MainViewModel vm) =>
        vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());

    private static async Task Cut(MainViewModel vm, FacePick pick)
    {
        vm.PlaceEdgeCut(pick);
        await vm.WaitForEvaluationAsync();
    }

    private static PrimitiveNode Cutter(MainViewModel vm) => vm.Document.Root.Descendants().OfType<PrimitiveNode>().Last(n => n.GeneratorId is "edgecut" or "edgering");

    private static double PolygonFactor(int n) => n / (2 * Math.PI) * Math.Sin(2 * Math.PI / n);

    // 20 x 20 x 20 box: the top face is at z = 20, the +Y edge of it at y = 10
    private static FacePick TopNearPlusY(Guid id) => new(id, new Vector3(0, 8, 20), Vector3.UnitZ);

    // ---- the mode -------------------------------------------------------------------------------------

    [UiFact]
    public async Task The_tool_is_a_mode_that_reports_clicks_as_faces_and_ends_with_the_button_or_esc()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.EdgeCutCommand.Execute(null);
        Assert.True(vm.IsPlacingEdgeCut);
        Assert.True(vm.IsFacePickActive);
        Assert.Contains("edge", vm.HintText, StringComparison.OrdinalIgnoreCase);

        vm.EdgeCutCommand.Execute(null);
        Assert.False(vm.IsPlacingEdgeCut);

        vm.EdgeCutCommand.Execute(null);
        vm.FacePickedCommand.Execute(null); // Esc
        Assert.False(vm.IsPlacingEdgeCut);
        Assert.False(vm.IsFacePickActive);
    }

    [UiFact]
    public async Task The_tool_needs_something_to_cut()
    {
        var vm = NewViewModel();
        Assert.False(vm.EdgeCutCommand.CanExecute(null));

        await Add(vm, "box");
        Assert.True(vm.EdgeCutCommand.CanExecute(null));
    }

    [UiFact]
    public async Task Only_one_face_mode_is_on_at_a_time()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.DrillCommand.Execute(null);
        vm.EdgeCutCommand.Execute(null);
        Assert.True(vm.IsPlacingEdgeCut);
        Assert.False(vm.IsPlacingHole);

        vm.DrillCommand.Execute(null);
        Assert.True(vm.IsPlacingHole);
        Assert.False(vm.IsPlacingEdgeCut);

        vm.EdgeCutCommand.Execute(null);
        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsPlacingEdgeCut);
        Assert.True(vm.IsPickingFace);

        vm.EdgeCutCommand.Execute(null);
        Assert.False(vm.IsPickingFace);

        vm.FaceToFaceCommand.Execute(null);
        Assert.False(vm.IsPlacingEdgeCut);
    }

    [UiFact]
    public async Task A_click_in_the_mode_cuts_the_edge_and_the_mode_stays_on()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeCutCommand.Execute(null);
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;

        vm.FacePickedCommand.Execute(TopNearPlusY(box.Id));
        await vm.WaitForEvaluationAsync();

        Assert.True(vm.IsPlacingEdgeCut);
        Assert.Equal(8000 - 2 * 2 / 2.0 * 20, SolidVolume(vm), 0.5);
    }

    // ---- the cut --------------------------------------------------------------------------------------

    [UiFact]
    public async Task A_chamfer_removes_a_triangle_along_the_edge()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;

        await Cut(vm, TopNearPlusY(box.Id));

        Assert.Equal(8000 - 2 * 2 / 2.0 * 20, SolidVolume(vm), 0.5);
        Assert.All(vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid), i => Assert.True(i.Mesh.IsWatertight()));
    }

    [UiFact]
    public async Task A_fillet_removes_the_corner_between_the_faces_and_the_arc()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "3";
        vm.EdgeTool.EdgeType = 1;

        await Cut(vm, TopNearPlusY(box.Id));

        var removed = (1 - Math.PI / 4) * 9 * 20;
        Assert.Equal(8000 - removed, SolidVolume(vm), removed * 0.035);
        Assert.All(vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid), i => Assert.True(i.Mesh.IsWatertight()));
    }

    [UiFact]
    public async Task A_vertical_edge_and_the_bottom_edge_work_the_same_way()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;

        await Cut(vm, new FacePick(box.Id, new Vector3(10, 8, 10), Vector3.UnitX)); // vertical edge at x = 10, y = 10
        var body = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid).NodeId;
        await Cut(vm, new FacePick(body, new Vector3(0, -8, 0), -Vector3.UnitZ));  // the bottom face, close to its -Y edge

        // both edges are 20 long and do not touch each other
        Assert.Equal(8000 - 2 * 20 - 2 * 20, SolidVolume(vm), 1.0);
    }

    [UiFact]
    public async Task The_cutter_is_a_hole_object_in_a_group_with_the_body_in_one_undo_step()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        var steps = vm.HistoryEntries.Count;

        await Cut(vm, TopNearPlusY(box.Id));

        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Contains(box, group.Children);
        var cutter = Cutter(vm);
        Assert.Same(group, cutter.Parent);
        Assert.Equal(NodeRole.Hole, cutter.Role);
        Assert.Equal("edgecut", cutter.GeneratorId);
        Assert.Equal("2", cutter.Parameters["edgeSize"]);
        Assert.Equal("0", cutter.Parameters["edgeType"]);
        Assert.Equal("20", cutter.Parameters["length"]);
        Assert.Equal("90", cutter.Parameters["angle"]);
        Assert.Equal([cutter.Id], vm.SelectedIds.ToArray());
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Single(vm.Document.Root.Children);
        Assert.Same(box, vm.Document.Root.Children[0]);
        Assert.Equal(8000, SolidVolume(vm), 0.01);
    }

    [UiFact]
    public async Task The_cutter_sits_on_the_edge()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;

        await Cut(vm, TopNearPlusY(box.Id));

        var ghost = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Ghost);
        var bounds = ghost.Mesh.ComputeBounds();
        Assert.Equal(-10.05f, bounds.Min.X, 0.01f);
        Assert.Equal(10.05f, bounds.Max.X, 0.01f);
        Assert.InRange(bounds.Max.Y, 10, 11);
        Assert.InRange(bounds.Max.Z, 20, 21);
        Assert.InRange(bounds.Min.Y, 8, 9);
        Assert.InRange(bounds.Min.Z, 18, 19);
    }

    [UiFact]
    public async Task A_second_cut_on_the_same_body_uses_the_same_group()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        await Cut(vm, TopNearPlusY(box.Id));
        var solid = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid);

        await Cut(vm, new FacePick(solid.NodeId, new Vector3(8, 0, 20), Vector3.UnitZ)); // the +X edge of the top

        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(2, group.Children.OfType<PrimitiveNode>().Count(n => n.GeneratorId == "edgecut"));
        // the top edge on the +X side is only the 18 mm of the top face: the chamfer face of the first cut continues it at an angle
        Assert.Equal(8000 - 40 - 36, SolidVolume(vm), 0.3);
    }

    [UiFact]
    public async Task Edges_of_a_box_with_a_hole_are_found_on_the_cut_mesh()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.PlaceHole(new FacePick(box.Id, new Vector3(0, 0, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        var solid = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid);
        var before = SolidVolume(vm);

        await Cut(vm, new FacePick(solid.NodeId, new Vector3(0, 8, 20), Vector3.UnitZ));

        Assert.Equal(before - 40, SolidVolume(vm), 0.5);
        var group = Assert.IsType<GroupNode>(Assert.Single(vm.Document.Root.Children));
        Assert.Equal(2, group.Children.Count(n => n.Role == NodeRole.Hole)); // the drilled hole and the cutter
    }

    // ---- round rims -----------------------------------------------------------------------------------

    [UiFact]
    public async Task The_rim_of_a_cylinder_gets_a_ring_cutter()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("height", "10"));
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 1;
        var before = SolidVolume(vm);

        await Cut(vm, new FacePick(cylinder.Id, new Vector3(9, 1, 10), Vector3.UnitZ));

        var cutter = Cutter(vm);
        Assert.Equal("edgering", cutter.GeneratorId);
        Assert.Equal(NodeRole.Hole, cutter.Role);
        Assert.Equal("10", cutter.Parameters["radius"]);
        Assert.Equal("0", cutter.Parameters["inside"]);
        Assert.Equal("64", cutter.Parameters["segments"]);

        const double s = 2, radius = 10;
        var removed = 2 * Math.PI * (radius - 0.2234 * s) * (1 - Math.PI / 4) * s * s * PolygonFactor(64);
        Assert.Equal(before - removed, SolidVolume(vm), removed * 0.04);
        Assert.All(vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid), i => Assert.True(i.Mesh.IsWatertight()));
    }

    [UiFact]
    public async Task The_chamfered_rim_of_a_cylinder_is_a_ring_with_a_triangle_cross_section()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("height", "10"));
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        var before = SolidVolume(vm);

        await Cut(vm, new FacePick(cylinder.Id, new Vector3(0, 9.5f, 10), Vector3.UnitZ));

        var removed = 2 * Math.PI * (10 - 2 / 3.0) * 2 * 2 / 2.0 * PolygonFactor(64);
        Assert.Equal(before - removed, SolidVolume(vm), removed * 0.01);
    }

    [UiFact]
    public async Task The_bottom_rim_works_as_well_as_the_top_one()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("height", "10"));
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        var before = SolidVolume(vm);

        await Cut(vm, new FacePick(cylinder.Id, new Vector3(0, 9.5f, 0), -Vector3.UnitZ));

        var removed = 2 * Math.PI * (10 - 2 / 3.0) * 2 * 2 / 2.0 * PolygonFactor(64);
        Assert.Equal(before - removed, SolidVolume(vm), removed * 0.01);
        var ghost = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Ghost).Mesh.ComputeBounds();
        Assert.True(ghost.Min.Z < 0 && ghost.Max.Z < 2.5f); // at the bottom, not the top: from just below the floor to 2 mm up
    }

    [UiFact]
    public async Task The_rim_of_a_hole_in_a_tube_cuts_outwards()
    {
        var vm = NewViewModel();
        var tube = await Add(vm, "tube", ("outerDiameter", "20"), ("wall", "2"), ("height", "10"));
        vm.EdgeTool.Size.Text = "1";
        vm.EdgeTool.EdgeType = 0;
        var before = SolidVolume(vm);

        await Cut(vm, new FacePick(tube.Id, new Vector3(8.4f, 0, 10), Vector3.UnitZ));

        var cutter = Cutter(vm);
        Assert.Equal("edgering", cutter.GeneratorId);
        Assert.Equal("1", cutter.Parameters["inside"]);
        Assert.Equal("8", cutter.Parameters["radius"]);
        var removed = 2 * Math.PI * (8 + 1 / 3.0) * 1 * 1 / 2.0 * PolygonFactor(64);
        Assert.Equal(before - removed, SolidVolume(vm), removed * 0.01);
    }

    // ---- what cannot be cut ---------------------------------------------------------------------------

    [UiFact]
    public async Task A_click_that_finds_no_face_says_so_and_changes_nothing()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var steps = vm.HistoryEntries.Count;

        vm.PlaceEdgeCut(new FacePick(box.Id, new Vector3(50, 50, 50), Vector3.UnitZ));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Single(vm.Document.Root.Children);
        Assert.Contains("flat face", vm.StatusText);
    }

    [UiFact]
    public async Task An_edge_of_a_sphere_is_refused_with_a_reason()
    {
        var vm = NewViewModel();
        var sphere = await Add(vm, "sphere");
        var steps = vm.HistoryEntries.Count;
        var item = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid);
        var triangle = item.Mesh.Indices.ToArray();
        var vertices = item.Mesh.Vertices.ToArray();
        var center = (vertices[triangle[0]] + vertices[triangle[1]] + vertices[triangle[2]]) / 3;
        var normal = Vector3.Normalize(Vector3.Cross(vertices[triangle[1]] - vertices[triangle[0]], vertices[triangle[2]] - vertices[triangle[0]]));

        vm.PlaceEdgeCut(new FacePick(sphere.Id, center, normal));

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Single(vm.Document.Root.Children);
        Assert.False(string.IsNullOrWhiteSpace(vm.StatusText));
    }

    [UiFact]
    public async Task A_slanted_rim_is_refused_with_a_reason()
    {
        var vm = NewViewModel();
        var frustum = await Add(vm, "frustum", ("bottomDiameter", "20"), ("topDiameter", "10"), ("height", "10"));

        vm.PlaceEdgeCut(new FacePick(frustum.Id, new Vector3(4.6f, 0, 10), Vector3.UnitZ));

        Assert.Single(vm.Document.Root.Children);
        Assert.Contains("right angle", vm.StatusText);
    }

    [UiFact]
    public async Task A_locked_body_is_not_cut()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        box.Locked = true;
        vm.Document.NotifyChanged();
        await vm.WaitForEvaluationAsync();

        vm.PlaceEdgeCut(TopNearPlusY(box.Id));

        Assert.Single(vm.Document.Root.Children);
        Assert.Contains("locked", vm.StatusText);
    }

    [UiFact]
    public async Task A_size_that_does_not_work_blocks_the_cut()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");

        foreach (var bad in new[] { "abc", "0", "-1", "" })
        {
            vm.EdgeTool.Size.Text = bad;
            Assert.True(vm.EdgeTool.Size.HasError, bad);
            vm.PlaceEdgeCut(TopNearPlusY(box.Id));
            Assert.Single(vm.Document.Root.Children);
        }
    }

    [UiFact]
    public async Task A_click_on_something_that_is_not_in_the_scene_is_ignored()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        var steps = vm.HistoryEntries.Count;

        vm.PlaceEdgeCut(new FacePick(Guid.NewGuid(), new Vector3(0, 8, 20), Vector3.UnitZ));

        Assert.Equal(steps, vm.HistoryEntries.Count);
    }

    // ---- the tool settings and the cutter in the properties -------------------------------------------

    [UiFact]
    public async Task The_size_can_be_a_formula_with_a_variable_and_is_kept_as_typed()
    {
        var vm = NewViewModel();
        vm.Variables.AddCommand.Execute(null);
        vm.Variables.Rows[^1].Name.Text = "soft";
        vm.Variables.Rows[^1].Expression.Text = "1.5";
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "soft * 2";
        vm.EdgeTool.EdgeType = 0;

        await Cut(vm, TopNearPlusY(box.Id));

        Assert.Equal("soft * 2", Cutter(vm).Parameters["edgeSize"]);
        Assert.Equal(8000 - 3 * 3 / 2.0 * 20, SolidVolume(vm), 0.5);
    }

    [UiFact]
    public async Task The_tool_settings_are_kept_for_the_next_cut_and_do_not_change_cutters_that_exist()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        await Cut(vm, TopNearPlusY(box.Id));
        var first = Cutter(vm);

        vm.EdgeTool.Size.Text = "4";
        vm.EdgeTool.EdgeType = 1;

        Assert.Equal("2", first.Parameters["edgeSize"]);
        Assert.Equal("0", first.Parameters["edgeType"]);
        Assert.Equal("4", vm.EdgeTool.Size.Text);
    }

    [UiFact]
    public async Task The_new_cutter_is_selected_and_its_size_and_type_can_be_changed_afterwards()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        await Cut(vm, TopNearPlusY(box.Id));

        Assert.True(vm.Properties!.HasEdgeChoices);
        Assert.False(vm.Properties.HasEdgeWhere); // a cutter has no "which edges"
        var size = vm.Properties.Parameters.Single(f => f.Label == "Edge size");
        Assert.True(size.IsVisible);
        Assert.Equal("2", size.Text);
        Assert.Equal(0, vm.Properties.EdgeType);
        Assert.DoesNotContain(vm.Properties.Parameters, f => f.IsVisible && f.Label == "Edge type");

        size.Text = "4";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(8000 - 4 * 4 / 2.0 * 20, SolidVolume(vm), 0.5);

        vm.Properties.EdgeType = 1; // a fillet of radius 4
        await vm.WaitForEvaluationAsync();
        var removed = (1 - Math.PI / 4) * 16 * 20;
        Assert.Equal(8000 - removed, SolidVolume(vm), removed * 0.035);
    }

    [UiFact]
    public async Task A_cutter_can_be_deleted_and_the_edge_is_sharp_again()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        await Cut(vm, TopNearPlusY(box.Id));

        vm.DeleteCommand.Execute(null); // the cutter is selected
        await vm.WaitForEvaluationAsync();

        Assert.Equal(8000, SolidVolume(vm), 0.01);
    }

    // ---- saving ---------------------------------------------------------------------------------------

    [UiFact]
    public async Task The_cutter_survives_saving_the_project()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.EdgeTool.Size.Text = "2";
        vm.EdgeTool.EdgeType = 0;
        await Cut(vm, TopNearPlusY(box.Id));

        var stream = new MemoryStream();
        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        var cutter = back.Root.Descendants().OfType<PrimitiveNode>().Single(n => n.GeneratorId == "edgecut");
        Assert.Equal(NodeRole.Hole, cutter.Role);
        Assert.Equal("2", cutter.Parameters["edgeSize"]);
        Assert.Equal("20", cutter.Parameters["length"]);
    }

    [Fact]
    public void The_tool_generators_are_known_to_the_shape_library_for_loading()
    {
        Assert.NotNull(ShapeLibrary.Find("edgecut"));
        Assert.NotNull(ShapeLibrary.Find("edgering"));
    }
}
