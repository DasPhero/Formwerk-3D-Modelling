using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The properties of a selected hole (F-HOL-02): the type is a choice, only the fields of the type show, the picture follows.</summary>
public class HolePropertiesTests
{
    private static async Task<(MainViewModel Vm, PrimitiveNode Hole)> Scene()
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var box = vm.Document.Root.Children[0];
        vm.PlaceHole(new FacePick(box.Id, new System.Numerics.Vector3(0, 0, 20), System.Numerics.Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        return (vm, (PrimitiveNode)vm.Document.Root.Descendants().Single(n => n is PrimitiveNode { GeneratorId: "hole" }));
    }

    private static FieldViewModel Field(PropertiesViewModel props, string parameter)
    {
        var index = ShapeLibrary.Hole.Parameters.ToList().FindIndex(p => p.Name == parameter);
        return props.Parameters[index];
    }

    [UiFact]
    public async Task A_hole_offers_the_type_as_a_choice_and_other_shapes_do_not()
    {
        var (vm, hole) = await Scene();
        vm.SelectOnly([hole.Id]);
        Assert.True(vm.Properties!.IsHoleShape);
        Assert.Equal(HoleType.Cylindrical, vm.Properties.HoleType);
        Assert.Equal(4, vm.Properties.HoleTypes.Count);

        vm.SelectOnly([vm.Document.Root.Descendants().First(n => n is PrimitiveNode { GeneratorId: "box" }).Id]);
        Assert.False(vm.Properties!.IsHoleShape);
    }

    [UiFact]
    public async Task Choosing_a_type_changes_the_hole_in_one_undo_step()
    {
        var (vm, hole) = await Scene();
        vm.SelectOnly([hole.Id]);
        var steps = vm.HistoryEntries.Count;

        vm.Properties!.HoleType = HoleType.Countersink;
        await vm.WaitForEvaluationAsync();

        Assert.Equal("2", hole.Parameters["type"]);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal(HoleType.Countersink, vm.Properties!.HoleType);

        vm.UndoCommand.Execute(null);
        Assert.Equal("0", hole.Parameters["type"]);
        Assert.Equal(HoleType.Cylindrical, vm.Properties!.HoleType);
    }

    [UiFact]
    public async Task Only_the_fields_of_the_type_are_visible()
    {
        var (vm, hole) = await Scene();
        vm.SelectOnly([hole.Id]);
        var props = vm.Properties!;

        Assert.False(Field(props, "type").IsVisible); // the choice replaces the number
        Assert.True(Field(props, "diameter").IsVisible);
        Assert.True(Field(props, "depth").IsVisible);
        Assert.False(Field(props, "endDiameter").IsVisible);
        Assert.False(Field(props, "sinkDiameter").IsVisible);
        Assert.False(Field(props, "sinkAngle").IsVisible);
        Assert.False(Field(props, "sinkDepth").IsVisible);

        props.HoleType = HoleType.Conical;
        Assert.True(Field(vm.Properties!, "endDiameter").IsVisible);
        Assert.False(Field(vm.Properties!, "sinkDiameter").IsVisible);

        vm.Properties!.HoleType = HoleType.Countersink;
        Assert.True(Field(vm.Properties!, "sinkDiameter").IsVisible);
        Assert.True(Field(vm.Properties!, "sinkAngle").IsVisible);
        Assert.False(Field(vm.Properties!, "sinkDepth").IsVisible);

        vm.Properties!.HoleType = HoleType.Counterbore;
        Assert.True(Field(vm.Properties!, "sinkDepth").IsVisible);
        Assert.False(Field(vm.Properties!, "sinkAngle").IsVisible);
    }

    [UiFact]
    public async Task The_head_angle_is_in_degrees()
    {
        var (vm, hole) = await Scene();
        vm.SelectOnly([hole.Id]);

        Assert.Equal("°", Field(vm.Properties!, "sinkAngle").Unit);
        Assert.Equal("mm", Field(vm.Properties!, "diameter").Unit);
        Assert.Equal("", Field(vm.Properties!, "segments").Unit);
    }

    [UiFact]
    public async Task The_picture_follows_the_type_and_the_sizes()
    {
        var (vm, hole) = await Scene();
        vm.SelectOnly([hole.Id]);
        var plain = vm.Properties!.Section!;

        vm.Properties!.HoleType = HoleType.Counterbore;
        var bored = vm.Properties!.Section!;

        Assert.NotEqual(plain.Hole, bored.Hole);

        Field(vm.Properties!, "sinkDiameter").Text = "10";
        Assert.NotEqual(bored.Hole, vm.Properties!.Section!.Hole);
    }

    [UiFact]
    public async Task Other_shapes_have_no_picture()
    {
        var (vm, _) = await Scene();
        vm.SelectOnly([vm.Document.Root.Descendants().First(n => n is PrimitiveNode { GeneratorId: "box" }).Id]);

        Assert.Null(vm.Properties!.Section);
    }

    [UiFact]
    public void The_tool_has_a_picture_that_follows_its_settings()
    {
        var tool = new HoleToolViewModel(() => new Dictionary<string, double>());
        var changes = new List<string?>();
        tool.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var plain = tool.Section.Hole;

        tool.Type = HoleType.Counterbore;

        Assert.Contains(nameof(HoleToolViewModel.Section), changes);
        Assert.NotEqual(plain, tool.Section.Hole);

        changes.Clear();
        tool.HeadDiameter.Text = "9";
        Assert.Contains(nameof(HoleToolViewModel.Section), changes);
    }

    [UiFact]
    public void A_through_hole_is_drawn_deeper_than_the_widest_part_so_that_the_picture_stays_readable()
    {
        var tool = new HoleToolViewModel(() => new Dictionary<string, double>()) { Type = HoleType.Counterbore };
        tool.Depth.Text = "1";

        tool.Through = true;

        Assert.True(tool.Profile.Min(p => p.Z) <= -2 * 6.4);
        tool.Through = false;
        Assert.Equal(-1, tool.Profile.Min(p => p.Z), 3);
    }
}
