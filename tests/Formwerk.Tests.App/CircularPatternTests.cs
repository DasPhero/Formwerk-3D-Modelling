using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>Circular patterns (F-PAT-01): turn the selection about an axis, radius and distance from the outer edge, count and angle stay editable.</summary>
public class CircularPatternTests
{
    private static MainViewModel NewViewModel() => new(new ManifoldKernel(), new FakeDialogs());

    private static FieldViewModel Field(PropertiesViewModel props, string name)
    {
        var index = PatternParameters.For(PatternKind.Circular).ToList().FindIndex(d => d.Name == name);
        return props.Parameters[index];
    }

    /// <summary>A cylinder of 40 mm at (30, 20) with a 3.4 mm hole 15 mm from its axis, grouped. The hole is selected.</summary>
    private static async Task<(MainViewModel Vm, Node Cylinder, Node Hole)> CylinderWithHole()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();
        var cylinder = vm.Document.Root.Children[0];
        vm.SelectOnly([cylinder.Id]);
        vm.Properties!.Parameters[0].Text = "40"; // diameter
        vm.Properties.Parameters[1].Text = "10"; // height
        vm.Properties.Position[0].Text = "30";
        vm.Properties.Position[1].Text = "20";
        await vm.WaitForEvaluationAsync();
        vm.PlaceHole(new FacePick(cylinder.Id, new Vector3(45, 20, 10), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        var hole = vm.Document.Root.Descendants().Single(n => n is PrimitiveNode { GeneratorId: "hole" });
        vm.SelectOnly([hole.Id]);
        return (vm, cylinder, hole);
    }

    private static async Task<PatternNode> MakeCircular(MainViewModel vm)
    {
        vm.CircularPatternCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        return vm.Document.Root.Descendants().OfType<PatternNode>().Single();
    }

    private static List<Vector3> GhostCenters(MainViewModel vm) =>
        [.. vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Ghost).Select(i => i.Mesh.ComputeBounds().Center)];

    private static double Angle(Vector3 point, Vector3 center) =>
        Math.Round((Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI + 360) % 360) % 360; // whole degrees, 360 is 0

    [UiFact]
    public async Task The_selection_becomes_three_around_a_full_circle_in_one_undo_step()
    {
        var (vm, _, hole) = await CylinderWithHole();
        var steps = vm.HistoryEntries.Count;

        var pattern = await MakeCircular(vm);

        Assert.Equal(PatternKind.Circular, pattern.Kind);
        Assert.Equal("3", pattern.Parameters["count"]);
        Assert.Equal("360", pattern.Parameters["angle"]);
        Assert.Equal("2", pattern.Parameters["axis"]);
        Assert.Same(hole, Assert.Single(pattern.Children));
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal([pattern.Id], vm.SelectedIds);

        vm.UndoCommand.Execute(null);
        await vm.WaitForEvaluationAsync();
        Assert.Same(hole, hole.Parent!.Children.Single(c => c == hole));
        Assert.Empty(vm.Document.Root.Descendants().OfType<PatternNode>());
    }

    [UiFact]
    public async Task The_center_snaps_to_the_axis_of_the_body_next_to_the_source()
    {
        var (vm, _, hole) = await CylinderWithHole();
        var holeWorld = TransformOps.WorldPosition(hole);

        var pattern = await MakeCircular(vm);

        Assert.Equal(new Vector3(30, 20, 0), pattern.Transform.Position); // the axis of the cylinder
        Assert.True(Vector3.Distance(TransformOps.WorldPosition(hole), holeWorld) < 1e-3f, "the hole did not move");
        Assert.True(Vector3.Distance(hole.Transform.Position, new Vector3(15, 0, 10)) < 1e-3f);
    }

    [UiFact]
    public async Task Without_a_body_next_to_it_the_center_is_the_origin()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.Properties!.Position[0].Text = "30";

        var pattern = await MakeCircular(vm);

        Assert.Equal(Vector3.Zero, pattern.Transform.Position);
        Assert.Equal(30, box.Transform.Position.X, 3); // the box keeps its place, 30 mm from the center
        Assert.Equal("30", vm.Properties!.RadiusField!.Text);
    }

    [UiFact]
    public async Task The_radius_is_the_distance_of_the_source_from_the_axis_and_can_be_changed()
    {
        var (vm, _, hole) = await CylinderWithHole();
        await MakeCircular(vm);

        Assert.Equal("15", vm.Properties!.RadiusField!.Text);

        vm.Properties.RadiusField!.Text = "10";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(10, hole.Transform.Position.X, 3);
        Assert.Equal(0, hole.Transform.Position.Y, 3);
        Assert.Equal(10, hole.Transform.Position.Z, 3); // the height of the hole does not change
        Assert.Equal("10", vm.Properties!.RadiusField!.Text);

        vm.UndoCommand.Execute(null);
        Assert.Equal(15, hole.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task A_source_on_the_axis_moves_out_along_X_when_it_gets_a_radius()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        await MakeCircular(vm);
        Assert.Equal("0", vm.Properties!.RadiusField!.Text);

        vm.Properties.RadiusField!.Text = "25";

        Assert.Equal(25, box.Transform.Position.X, 3);
        Assert.Equal(0, box.Transform.Position.Y, 3);
    }

    [UiFact]
    public async Task A_negative_radius_is_refused()
    {
        var (vm, _, hole) = await CylinderWithHole();
        await MakeCircular(vm);

        vm.Properties!.RadiusField!.Text = "-3";

        Assert.NotNull(vm.Properties.RadiusField!.Error);
        Assert.Equal(15, hole.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task Five_millimetres_from_the_edge_of_a_40_mm_cylinder_is_a_radius_of_15()
    {
        var (vm, _, hole) = await CylinderWithHole();
        await MakeCircular(vm);

        var props = vm.Properties!;
        Assert.True(props.HasEdgeReference);
        Assert.Equal("5", props.EdgeDistanceField!.Text); // 20 - 15

        props.EdgeDistanceField!.Text = "3";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(17, hole.Transform.Position.X, 3);
        Assert.Equal("17", vm.Properties!.RadiusField!.Text);

        vm.Properties.EdgeDistanceField!.Text = "5";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(15, hole.Transform.Position.X, 3);
    }

    [UiFact]
    public async Task The_distance_can_be_measured_to_the_edge_of_the_hole_instead_of_its_center()
    {
        var (vm, _, hole) = await CylinderWithHole();
        await MakeCircular(vm);
        var props = vm.Properties!;

        props.EdgeToHoleEdge = true;

        Assert.Equal(3.3, double.Parse(vm.Properties!.EdgeDistanceField!.Text, System.Globalization.CultureInfo.InvariantCulture), 2); // 20 - 15 - 1.7
        vm.Properties.EdgeDistanceField!.Text = "5";
        await vm.WaitForEvaluationAsync();
        Assert.Equal(13.3, hole.Transform.Position.X, 2); // 20 - 5 - 1.7
    }

    [UiFact]
    public async Task Three_holes_five_millimetres_from_the_edge_sit_every_120_degrees_on_a_radius_of_15()
    {
        var (vm, _, _) = await CylinderWithHole();
        await MakeCircular(vm);
        vm.Properties!.EdgeDistanceField!.Text = "5";
        await vm.WaitForEvaluationAsync();

        var center = new Vector3(30, 20, 0);
        var ghosts = GhostCenters(vm);

        Assert.Equal(3, ghosts.Count);
        var angles = ghosts.Select(g => Angle(g, center)).Order().ToArray();
        Assert.Equal([0.0, 120.0, 240.0], angles);
        Assert.All(ghosts, g => Assert.Equal(15, Math.Sqrt((g.X - 30) * (g.X - 30) + (g.Y - 20) * (g.Y - 20)), 2));
    }

    [UiFact]
    public async Task The_three_holes_cut_the_body_three_times()
    {
        var (vm, _, _) = await CylinderWithHole();
        double Volume() => vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => (double)i.Mesh.ComputeVolume());
        var oneHole = Volume();

        await MakeCircular(vm);
        var threeHoles = Volume();

        var whole = 64 / 2.0 * 20 * 20 * Math.Sin(2 * Math.PI / 64) * 10; // the cylinder is a 64-gon
        Assert.InRange((whole - threeHoles) / (whole - oneHole), 2.8, 3.2);
    }

    [UiFact]
    public async Task Changing_the_count_from_3_to_6_gives_a_60_degree_division()
    {
        var (vm, _, _) = await CylinderWithHole();
        await MakeCircular(vm);

        Field(vm.Properties!, "count").Text = "6";
        await vm.WaitForEvaluationAsync();

        var center = new Vector3(30, 20, 0);
        var angles = GhostCenters(vm).Select(g => Angle(g, center)).Order().ToArray();
        Assert.Equal([0.0, 60.0, 120.0, 180.0, 240.0, 300.0], angles);
    }

    [UiFact]
    public async Task A_part_circle_ends_on_the_angle()
    {
        var (vm, _, _) = await CylinderWithHole();
        await MakeCircular(vm);

        Field(vm.Properties!, "angle").Text = "180";
        await vm.WaitForEvaluationAsync();

        var center = new Vector3(30, 20, 0);
        var angles = GhostCenters(vm).Select(g => Angle(g, center)).Order().ToArray();
        Assert.Equal([0.0, 90.0, 180.0], angles);
    }

    [UiFact]
    public async Task The_circular_pattern_shows_angle_and_axis_but_no_spacing_or_second_direction()
    {
        var (vm, _, _) = await CylinderWithHole();
        await MakeCircular(vm);

        var props = vm.Properties!;

        Assert.True(props.IsPattern);
        Assert.True(props.IsCircularPattern);
        Assert.False(props.IsLinearPattern);
        Assert.Equal(["count", "angle", "axis"], PatternParameters.For(PatternKind.Circular).Select(d => d.Name).ToArray());
        Assert.True(Field(props, "angle").IsVisible);
        Assert.False(Field(props, "axis").IsVisible); // the axis has its own choice
        Assert.Equal(2, props.PatternAxis); // Z
    }

    [UiFact]
    public async Task The_axis_can_be_changed_to_turn_about_Y()
    {
        var (vm, _, _) = await CylinderWithHole();
        var pattern = await MakeCircular(vm);

        vm.Properties!.PatternAxis = 1;
        await vm.WaitForEvaluationAsync();

        Assert.Equal("1", pattern.Parameters["axis"]);
        Assert.Equal(3, GhostCenters(vm).Count);
    }

    [UiFact]
    public async Task Without_a_body_around_the_center_there_is_no_distance_from_the_edge()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        vm.SelectOnly([vm.Document.Root.Children[0].Id]);
        await MakeCircular(vm);

        Assert.False(vm.Properties!.HasEdgeReference);
    }

    [UiFact]
    public async Task A_circular_pattern_of_bodies_is_a_ring_of_solids()
    {
        var vm = NewViewModel();
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.SelectOnly([box.Id]);
        vm.Properties!.Position[0].Text = "40";
        await MakeCircular(vm);
        Field(vm.Properties!, "count").Text = "4";
        await vm.WaitForEvaluationAsync();

        var solids = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Select(i => i.Mesh.ComputeBounds().Center).ToList();

        Assert.Equal(4, solids.Count);
        Assert.All(solids, c => Assert.Equal(40, Math.Sqrt(c.X * c.X + c.Y * c.Y), 2));
    }

    [UiFact]
    public async Task The_circular_pattern_survives_save_and_load()
    {
        var (vm, _, _) = await CylinderWithHole();
        var pattern = await MakeCircular(vm);
        Field(vm.Properties!, "count").Text = "5";
        var path = Path.Combine(Path.GetTempPath(), $"circ-{Guid.NewGuid():N}.fwp");

        Formwerk.IO.ProjectFile.SaveAtomic(path, vm.Document);
        var loaded = Formwerk.IO.ProjectFile.Load(path);
        File.Delete(path);

        var copy = loaded.Root.Descendants().OfType<PatternNode>().Single();
        Assert.Equal(PatternKind.Circular, copy.Kind);
        Assert.Equal("5", copy.Parameters["count"]);
        Assert.Equal(pattern.Transform, copy.Transform);
    }
}
