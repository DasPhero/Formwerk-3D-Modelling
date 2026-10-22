using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The measuring tool: distance, faces, angle and diameter in the 3D view (E-07).</summary>
public class MeasureTests
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

    private static void Pick(MainViewModel vm, Guid id, Vector3 point, Vector3 normal, bool noSnap = false) =>
        vm.FacePickedCommand.Execute(new FacePick(id, point, normal, noSnap));

    private static async Task<MainViewModel> Measuring(int mode, params (string Name, string Value)[] boxParameters)
    {
        var vm = NewViewModel();
        await Add(vm, "box", boxParameters);
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = mode;
        return vm;
    }

    /// <summary>A pick on the piece of the mesh that is nearest to a point (its center, with its normal).</summary>
    private static FacePick PickNear(MainViewModel vm, Guid id, Vector3 near)
    {
        var best = float.MaxValue;
        FacePick? result = null;
        foreach (var item in vm.ViewportItems.Where(i => i.NodeId == id && i.Kind == ViewportItemKind.Solid))
        {
            var vertices = item.Mesh.Vertices.ToArray();
            var indices = item.Mesh.Indices.ToArray();
            for (var t = 0; t < indices.Length; t += 3)
            {
                Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                var center = (a + b + c) / 3;
                var distance = Vector3.Distance(center, near);
                if (distance >= best) continue;
                best = distance;
                result = new FacePick(id, center, Vector3.Normalize(Vector3.Cross(b - a, c - a)));
            }
        }

        return result!;
    }

    private const int Distance = 0, Faces = 1, Angle = 2, Diameter = 3;

    // ---- the mode -------------------------------------------------------------------------------------

    [UiFact]
    public async Task The_tool_is_a_mode_that_reports_clicks_as_faces_and_ends_with_the_button_or_esc()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.MeasureCommand.Execute(null);
        Assert.True(vm.IsMeasuring);
        Assert.True(vm.IsFacePickActive);
        Assert.Contains("point", vm.HintText, StringComparison.OrdinalIgnoreCase);

        vm.MeasureCommand.Execute(null);
        Assert.False(vm.IsMeasuring);

        vm.MeasureCommand.Execute(null);
        vm.FacePickedCommand.Execute(null); // Esc
        Assert.False(vm.IsMeasuring);
        Assert.False(vm.IsFacePickActive);
    }

    [UiFact]
    public async Task The_tool_needs_something_to_measure()
    {
        var vm = NewViewModel();
        Assert.False(vm.MeasureCommand.CanExecute(null));

        await Add(vm, "box");
        Assert.True(vm.MeasureCommand.CanExecute(null));
    }

    [UiFact]
    public async Task It_is_one_of_the_exclusive_face_modes()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.DrillCommand.Execute(null);
        vm.MeasureCommand.Execute(null);
        Assert.True(vm.IsMeasuring);
        Assert.False(vm.IsPlacingHole);

        vm.EdgeCutCommand.Execute(null);
        Assert.False(vm.IsMeasuring);

        vm.MeasureCommand.Execute(null);
        vm.SectionAtFaceCommand.Execute(null);
        Assert.False(vm.IsMeasuring);

        vm.MeasureCommand.Execute(null);
        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsMeasuring);

        vm.MeasureCommand.Execute(null);
        vm.FaceToFaceCommand.Execute(null);
        Assert.False(vm.IsMeasuring);

        vm.MeasureCommand.Execute(null);
        vm.DrillCommand.Execute(null);
        Assert.False(vm.IsMeasuring);
    }

    [UiFact]
    public async Task The_modes_are_distance_faces_angle_and_diameter()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        Assert.Equal(["Distance", "Faces", "Angle", "Diameter"], vm.MeasureModes.Select(m => m.Label).ToArray());
        Assert.Equal(Distance, vm.MeasureMode);
    }

    // ---- distance -------------------------------------------------------------------------------------

    [UiFact]
    public async Task Two_corners_give_their_distance_with_the_parts_along_the_axes()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);   // snaps to the corner (10, 10, 20)
        Pick(vm, box, new Vector3(-9, -9, 20), Vector3.UnitZ); // and (-10, -10, 20)

        var result = Assert.Single(vm.Measurements);
        Assert.Contains("28.28", result.Text);
        Assert.Contains("20.00", result.Text);
    }

    [UiFact]
    public async Task Corners_on_different_faces_give_the_distance_in_space()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, -9, 0), -Vector3.UnitZ);

        Assert.Contains("34.64", Assert.Single(vm.Measurements).Text);
    }

    [UiFact]
    public async Task Alt_or_shift_takes_the_clicked_point_without_snapping()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ, noSnap: true);
        Pick(vm, box, new Vector3(-9, -9, 20), Vector3.UnitZ, noSnap: true);

        Assert.Contains("25.46", Assert.Single(vm.Measurements).Text); // 18 * sqrt(2)
    }

    [UiFact]
    public async Task A_point_that_is_not_near_a_snap_point_stays_where_it_was_clicked()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(5, 0, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-5, 0, 20), Vector3.UnitZ);

        Assert.Contains("10.00", Assert.Single(vm.Measurements).Text);
    }

    [UiFact]
    public async Task The_first_click_only_shows_a_mark_and_asks_for_the_second_point()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        var first = vm.HintText;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);

        Assert.Empty(vm.Measurements);
        Assert.NotEqual(first, vm.HintText);
        Assert.Contains("second", vm.HintText, StringComparison.OrdinalIgnoreCase);
        var pending = Assert.Single(vm.ViewportMeasurements);
        Assert.Single(pending.Marks);
        Assert.Null(pending.Text);
    }

    [UiFact]
    public async Task A_finished_measurement_is_drawn_with_its_value()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);

        var drawing = Assert.Single(vm.ViewportMeasurements);
        Assert.Equal(2, drawing.Marks.Count);
        Assert.Single(drawing.Segments);
        Assert.Equal("20.00 mm", drawing.Text);
    }

    [UiFact]
    public async Task Several_measurements_stay_in_the_list_in_the_order_they_were_made()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;

        foreach (var (a, b) in new[] { (new Vector3(9, 9, 20), new Vector3(-9, 9, 20)), (new Vector3(9, 9, 20), new Vector3(9, -9, 20)), (new Vector3(5, 0, 20), new Vector3(-5, 0, 20)) })
        {
            Pick(vm, box, a, Vector3.UnitZ);
            Pick(vm, box, b, Vector3.UnitZ);
        }

        Assert.Equal(3, vm.Measurements.Count);
        Assert.Contains("20.00", vm.Measurements[0].Text);
        Assert.Contains("10.00", vm.Measurements[2].Text);
        Assert.Equal(3, vm.ViewportMeasurements.Count);
    }

    // ---- faces ----------------------------------------------------------------------------------------

    [UiFact]
    public async Task Two_parallel_faces_give_their_distance()
    {
        var vm = await Measuring(Faces);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(3, 4, 0), -Vector3.UnitZ);
        Pick(vm, box, new Vector3(-6, 2, 20), Vector3.UnitZ);

        Assert.Contains("20.00", Assert.Single(vm.Measurements).Text);
        Assert.Contains("distance", vm.Measurements[0].Text, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task The_distance_of_two_parallel_faces_is_exact_to_a_hundredth()
    {
        var vm = await Measuring(Faces, ("width", "33.37"));
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(-16.685f, 3, 8), -Vector3.UnitX);
        Pick(vm, box, new Vector3(16.685f, -2, 12), Vector3.UnitX);

        Assert.Contains("33.37", Assert.Single(vm.Measurements).Text);
    }

    [UiFact]
    public async Task Two_faces_that_meet_give_the_angle_between_them()
    {
        var vm = await Measuring(Faces);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(2, 2, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(10, 3, 9), Vector3.UnitX);

        var text = Assert.Single(vm.Measurements).Text;
        Assert.Contains("90.00", text);
        Assert.Contains("angle", text, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task The_angle_of_neighbouring_walls_of_a_hexagon_is_120_degrees()
    {
        var vm = NewViewModel();
        var prism = await Add(vm, "prism");
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = Faces;

        // two neighbouring walls: the pieces nearest to the middle of two sides of the hexagon (diameter 20)
        var a = PickNear(vm, prism.Id, new Vector3(8.66f * MathF.Cos(0.5236f), 8.66f * MathF.Sin(0.5236f), 10));
        var b = PickNear(vm, prism.Id, new Vector3(8.66f * MathF.Cos(1.5708f), 8.66f * MathF.Sin(1.5708f), 10));
        vm.FacePickedCommand.Execute(a);
        vm.FacePickedCommand.Execute(b);

        Assert.Contains("120.00", Assert.Single(vm.Measurements).Text);
    }

    // ---- angle ----------------------------------------------------------------------------------------

    [UiFact]
    public async Task Three_corners_give_the_angle_at_the_middle_one()
    {
        var vm = await Measuring(Angle);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Assert.Empty(vm.Measurements);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        Assert.Empty(vm.Measurements);
        Pick(vm, box, new Vector3(-9, -9, 20), Vector3.UnitZ);

        Assert.Contains("90.00", Assert.Single(vm.Measurements).Text);
        var drawing = Assert.Single(vm.ViewportMeasurements);
        Assert.Equal(3, drawing.Marks.Count);
        Assert.Equal(2, drawing.Segments.Count);
        Assert.Equal("90.00°", drawing.Text);
    }

    [UiFact]
    public async Task The_angle_asks_for_three_points_in_turn()
    {
        var vm = await Measuring(Angle);
        var box = vm.Document.Root.Children[0].Id;
        var hints = new List<string> { vm.HintText };

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        hints.Add(vm.HintText);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        hints.Add(vm.HintText);

        Assert.Equal(3, hints.Distinct().Count());
        Assert.Contains("corner", hints[1], StringComparison.OrdinalIgnoreCase);
    }

    // ---- diameter -------------------------------------------------------------------------------------

    [UiFact]
    public async Task A_click_on_the_wall_of_a_cylinder_gives_its_diameter()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("diameter", "17.37"), ("height", "12"), ("segments", "128"));
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = Diameter;

        vm.FacePickedCommand.Execute(PickNear(vm, cylinder.Id, new Vector3(0, 8.685f, 6)));

        var result = Assert.Single(vm.Measurements);
        Assert.Contains("17.37", result.Text);
        var drawing = Assert.Single(vm.ViewportMeasurements);
        Assert.Equal("Ø 17.37 mm", drawing.Text);
        Assert.NotEmpty(drawing.Segments);
    }

    [UiFact]
    public async Task A_click_on_the_wall_of_a_drilled_hole_gives_the_diameter_of_the_hole()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.PlaceHole(new FacePick(box.Id, new Vector3(0, 0, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        var solid = vm.ViewportItems.First(i => i.Kind == ViewportItemKind.Solid).NodeId;
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = Diameter;

        vm.FacePickedCommand.Execute(PickNear(vm, solid, new Vector3(1.7f, 0, 10)));

        var text = Assert.Single(vm.Measurements).Text;
        Assert.Contains("3.40", text);
        Assert.Contains("hole", text, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task A_flat_face_close_to_a_round_edge_gives_the_diameter_of_the_edge()
    {
        var vm = NewViewModel();
        var cylinder = await Add(vm, "cylinder", ("height", "10"));
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = Diameter;

        Pick(vm, cylinder.Id, new Vector3(9.2f, 1, 10), Vector3.UnitZ);

        Assert.Contains("20.00", Assert.Single(vm.Measurements).Text);
    }

    [UiFact]
    public async Task The_hole_of_a_tube_is_measured_from_its_rim()
    {
        var vm = NewViewModel();
        var tube = await Add(vm, "tube", ("outerDiameter", "20"), ("wall", "2"), ("height", "10"));
        vm.MeasureCommand.Execute(null);
        vm.MeasureMode = Diameter;

        Pick(vm, tube.Id, new Vector3(8.4f, 0, 10), Vector3.UnitZ);

        Assert.Contains("16.00", Assert.Single(vm.Measurements).Text);
    }

    [UiFact]
    public async Task Something_that_is_not_round_gives_a_message_and_no_measurement()
    {
        var vm = await Measuring(Diameter);
        var box = vm.Document.Root.Children[0].Id;

        Pick(vm, box, new Vector3(10, 0, 10), Vector3.UnitX);

        Assert.Empty(vm.Measurements);
        Assert.Contains("round", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ---- results and housekeeping ---------------------------------------------------------------------

    [UiFact]
    public async Task Clearing_removes_the_results_and_the_marks()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ); // a third click: a new measurement is begun

        vm.ClearMeasurementsCommand.Execute(null);

        Assert.Empty(vm.Measurements);
        Assert.Empty(vm.ViewportMeasurements);
        Assert.True(vm.IsMeasuring);
    }

    [UiFact]
    public async Task Ending_the_tool_clears_the_measurements()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);

        vm.MeasureCommand.Execute(null);

        Assert.Empty(vm.Measurements);
        Assert.Empty(vm.ViewportMeasurements);
    }

    [UiFact]
    public async Task A_change_of_the_model_clears_the_measurements_because_they_would_be_wrong()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        Assert.Single(vm.Measurements);

        vm.Properties!.Position[0].Text = "5";
        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.Measurements);
        Assert.Empty(vm.ViewportMeasurements);
    }

    [UiFact]
    public async Task Changing_the_mode_forgets_the_points_that_were_clicked_but_keeps_the_results()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ); // the first point of a second measurement

        vm.MeasureMode = Faces;

        Assert.Single(vm.Measurements);
        Assert.Single(vm.ViewportMeasurements); // only the finished one: the pending mark is gone
        Pick(vm, box, new Vector3(0, 0, 20), Vector3.UnitZ);
        Assert.Single(vm.Measurements); // one face is not a measurement yet
    }

    [UiFact]
    public async Task Measuring_changes_nothing_in_the_document()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0].Id;
        var steps = vm.HistoryEntries.Count;
        var revision = vm.Document.Revision;

        Pick(vm, box, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box, new Vector3(-9, 9, 20), Vector3.UnitZ);
        vm.ClearMeasurementsCommand.Execute(null);

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal(revision, vm.Document.Revision);
    }

    [UiFact]
    public async Task A_click_on_something_that_is_not_in_the_scene_is_ignored()
    {
        var vm = await Measuring(Distance);

        Pick(vm, Guid.NewGuid(), new Vector3(9, 9, 20), Vector3.UnitZ);

        Assert.Empty(vm.ViewportMeasurements);
    }

    [UiFact]
    public async Task A_locked_object_can_be_measured()
    {
        var vm = await Measuring(Distance);
        var box = vm.Document.Root.Children[0];
        box.Locked = true;

        Pick(vm, box.Id, new Vector3(9, 9, 20), Vector3.UnitZ);
        Pick(vm, box.Id, new Vector3(-9, 9, 20), Vector3.UnitZ);

        Assert.Single(vm.Measurements);
    }
}
