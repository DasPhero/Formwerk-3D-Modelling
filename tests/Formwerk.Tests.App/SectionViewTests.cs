using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Kernel;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>The section view: a cutting plane through the model, the cut surface filled (E-06).</summary>
public sealed class SectionViewTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-section-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static (MainViewModel Vm, FakeDialogs Dialogs) NewViewModel()
    {
        var dialogs = new FakeDialogs();
        return (new MainViewModel(new ManifoldKernel(), dialogs), dialogs);
    }

    private static async Task<PrimitiveNode> AddBox(MainViewModel vm)
    {
        vm.AddShapeCommand.Execute("box");
        await vm.WaitForEvaluationAsync();
        var node = (PrimitiveNode)vm.Document.Root.Children[^1];
        vm.SelectOnly([node.Id]);
        return node;
    }

    private static IEnumerable<ViewportItem> Caps(MainViewModel vm) => vm.ViewportItems.Where(i => i.ColorArgb == MainViewModel.SectionColor);

    private static IEnumerable<ViewportItem> Bodies(MainViewModel vm) => vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid && i.ColorArgb != MainViewModel.SectionColor);

    private static Bounds BodyBounds(MainViewModel vm)
    {
        var all = Bodies(vm).Select(i => i.Mesh.ComputeBounds()).ToList();
        return new Bounds(all.Select(b => b.Min).Aggregate(Vector3.Min), all.Select(b => b.Max).Aggregate(Vector3.Max));
    }

    private static async Task Show(MainViewModel vm)
    {
        vm.IsSectionActive = true;
        await vm.WaitForEvaluationAsync();
    }

    // ---- switching it on and off ----------------------------------------------------------------------

    [UiFact]
    public async Task Switching_it_on_cuts_the_model_in_the_middle_and_fills_the_cut()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);

        await Show(vm);

        Assert.Equal(0, vm.SectionAxis); // across X
        Assert.Equal(0, BodyBounds(vm).Max.X, 1e-3);
        Assert.Equal(-10, BodyBounds(vm).Min.X, 1e-3);
        var cap = Assert.Single(Caps(vm));
        Assert.Equal(400, cap.Mesh.ComputeSurfaceArea(), 0.05);
    }

    [UiFact]
    public async Task Switching_it_off_brings_the_whole_model_back()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);
        await Show(vm);

        vm.IsSectionActive = false;
        await vm.WaitForEvaluationAsync();

        Assert.Empty(Caps(vm));
        var item = Assert.Single(vm.ViewportItems, i => i.Kind == ViewportItemKind.Solid);
        Assert.Equal(box.Id, item.NodeId);
        Assert.Equal(8000, item.Mesh.ComputeVolume(), 0.01);
    }

    [UiFact]
    public async Task The_cut_surface_belongs_to_its_object_so_that_a_click_on_it_selects_the_object()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);

        await Show(vm);

        Assert.Equal(box.Id, Assert.Single(Caps(vm)).NodeId);
    }

    [UiFact]
    public async Task The_section_is_a_view_only_and_changes_nothing_in_the_document()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        var steps = vm.HistoryEntries.Count;
        var revision = vm.Document.Revision;

        await Show(vm);
        vm.SectionPosition = 3;
        vm.SectionFlip = true;
        vm.SectionAxis = 2;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal(revision, vm.Document.Revision);
    }

    [UiFact]
    public async Task Export_still_writes_the_whole_model()
    {
        var (vm, dialogs) = NewViewModel();
        await AddBox(vm);
        await Show(vm);
        Directory.CreateDirectory(_folder);
        dialogs.SavePath = Path.Combine(_folder, "model.stl");

        vm.ExportStlCommand.Execute(null);

        var bytes = File.ReadAllBytes(dialogs.SavePath);
        Assert.Equal(12, (int)BitConverter.ToUInt32(bytes, 80)); // the 12 triangles of the whole box, not the cut one
    }

    // ---- moving the plane -----------------------------------------------------------------------------

    [UiFact]
    public async Task Moving_the_plane_moves_the_cut()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionPosition = 4;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(4, BodyBounds(vm).Max.X, 1e-3);
        Assert.Equal(400, Assert.Single(Caps(vm)).Mesh.ComputeSurfaceArea(), 0.05);
    }

    [UiFact]
    public async Task A_plane_beyond_the_model_shows_all_of_it_without_a_cut_surface()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionPosition = 50;
        await vm.WaitForEvaluationAsync();

        Assert.Empty(Caps(vm));
        Assert.Equal(10, BodyBounds(vm).Max.X, 1e-3);
    }

    [UiFact]
    public async Task The_other_side_keeps_the_other_part()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);
        vm.SectionPosition = 4;

        vm.SectionFlip = true;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(4, BodyBounds(vm).Min.X, 1e-3);
        Assert.Equal(10, BodyBounds(vm).Max.X, 1e-3);
        Assert.Single(Caps(vm));
    }

    [UiFact]
    public async Task Another_axis_cuts_across_that_axis_and_starts_in_the_middle_of_the_model()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionAxis = 2;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(10, vm.SectionPosition, 0.5);
        Assert.Equal(10, BodyBounds(vm).Max.Z, 0.5);
        Assert.Equal(0, BodyBounds(vm).Min.Z, 1e-3);
    }

    [UiFact]
    public async Task The_slider_range_is_the_extent_of_the_model_along_the_axis()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);

        await Show(vm);
        Assert.InRange(vm.SectionMin, -12, -10);
        Assert.InRange(vm.SectionMax, 10, 12);

        vm.SectionAxis = 2;
        Assert.InRange(vm.SectionMin, -2, 0);
        Assert.InRange(vm.SectionMax, 20, 22);
    }

    [UiFact]
    public async Task Quick_moves_end_with_the_last_one()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        foreach (var x in new[] { -8.0, -5, -2, 1, 3, 6 }) vm.SectionPosition = x;
        await vm.WaitForEvaluationAsync();

        Assert.Equal(6, BodyBounds(vm).Max.X, 1e-3);
    }

    [UiFact]
    public async Task A_new_object_is_part_of_the_section_view_while_it_is_on()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);
        vm.SectionPosition = 200; // beyond everything: nothing is cut away, so the new object must show up whole

        vm.AddShapeCommand.Execute("cylinder");
        await vm.WaitForEvaluationAsync();

        Assert.Equal(2, Bodies(vm).Select(i => i.NodeId).Distinct().Count());
        Assert.Empty(Caps(vm));
    }

    [UiFact]
    public async Task An_object_that_is_added_in_front_of_the_plane_is_cut_away()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);
        vm.SectionPosition = -200; // in front of everything

        await vm.WaitForEvaluationAsync();

        Assert.Empty(vm.ViewportItems);
    }

    [UiFact]
    public async Task Without_objects_the_view_works_and_has_nothing_to_show()
    {
        var (vm, _) = NewViewModel();

        await Show(vm);

        Assert.Empty(vm.ViewportItems);
        vm.SectionPosition = 5;
        await vm.WaitForEvaluationAsync();
        Assert.Empty(vm.ViewportItems);
    }

    // ---- what the section is for ----------------------------------------------------------------------

    [UiFact]
    public async Task A_drilled_hole_shows_as_a_gap_in_the_cut_surface()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);
        vm.PlaceHole(new FacePick(box.Id, new Vector3(0, 0, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();

        await Show(vm); // across X through the middle: the hole (3.4 mm wide, through the whole box by default) is cut in half

        var cap = Assert.Single(Caps(vm));
        Assert.InRange(cap.Mesh.ComputeSurfaceArea(), 328, 336); // 20 x 20 minus 3.4 x 20
    }

    [UiFact]
    public async Task The_hole_ghost_is_cut_too_so_that_it_does_not_hide_the_section()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);
        vm.PlaceHole(new FacePick(box.Id, new Vector3(0, 0, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();

        await Show(vm);

        var ghost = vm.ViewportItems.Single(i => i.Kind == ViewportItemKind.Ghost);
        Assert.True(ghost.Mesh.ComputeBounds().Max.X <= 1e-3f);
    }

    // ---- typing the position --------------------------------------------------------------------------

    [UiFact]
    public async Task The_position_can_be_typed()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionPositionField.Text = "5";
        await vm.WaitForEvaluationAsync();

        Assert.Equal(5, vm.SectionPosition, 1e-9);
        Assert.Equal(5, BodyBounds(vm).Max.X, 1e-3);
    }

    [UiFact]
    public async Task A_formula_works_as_the_position_and_a_wrong_text_is_refused()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionPositionField.Text = "2 * 3";
        Assert.Equal(6, vm.SectionPosition, 1e-9);
        Assert.False(vm.SectionPositionField.HasError);

        vm.SectionPositionField.Text = "abc";
        Assert.True(vm.SectionPositionField.HasError);
        Assert.Equal(6, vm.SectionPosition, 1e-9);
    }

    [UiFact]
    public async Task The_field_shows_the_position_when_the_slider_moves()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.SectionPosition = 3.25;

        Assert.Equal("3.25", vm.SectionPositionField.Text);
    }

    // ---- at a face ------------------------------------------------------------------------------------

    [UiFact]
    public async Task A_click_on_a_face_puts_the_plane_on_that_face_and_it_can_be_moved_in_from_there()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);

        vm.SectionAtFaceCommand.Execute(null);
        Assert.True(vm.IsPickingSectionFace);
        Assert.True(vm.IsFacePickActive);
        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(10, 2, 7), Vector3.UnitX));
        await vm.WaitForEvaluationAsync();

        Assert.False(vm.IsPickingSectionFace);
        Assert.True(vm.IsSectionActive);
        Assert.Equal(3, vm.SectionAxis);
        Assert.Equal(10, vm.SectionPosition, 1e-4);
        Assert.Empty(Caps(vm)); // the plane only touches the model

        vm.SectionPosition = 4;
        await vm.WaitForEvaluationAsync();
        Assert.Equal(4, BodyBounds(vm).Max.X, 1e-3);
        Assert.Single(Caps(vm));
    }

    [UiFact]
    public async Task A_slanted_face_gives_a_slanted_plane()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);
        var normal = Vector3.Normalize(new Vector3(1, 0, 1));

        vm.SectionAtFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(new FacePick(box.Id, new Vector3(5, 0, 15), normal));
        vm.SectionPosition = 9;
        await vm.WaitForEvaluationAsync();

        var plane = new SectionPlane(normal, 9);
        Assert.All(Bodies(vm).SelectMany(i => i.Mesh.Vertices.ToArray()), v => Assert.True(plane.Distance(v) <= 1e-3f));
        Assert.All(Caps(vm).SelectMany(i => i.Mesh.Vertices.ToArray()), v => Assert.Equal(0, plane.Distance(v), 1e-3));
    }

    [UiFact]
    public async Task The_face_mode_is_one_of_the_exclusive_face_modes_and_esc_ends_it()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);

        vm.SectionAtFaceCommand.Execute(null);
        vm.DrillCommand.Execute(null);
        Assert.False(vm.IsPickingSectionFace);
        Assert.True(vm.IsPlacingHole);

        vm.SectionAtFaceCommand.Execute(null);
        Assert.False(vm.IsPlacingHole);
        vm.EdgeCutCommand.Execute(null);
        Assert.False(vm.IsPickingSectionFace);

        vm.SectionAtFaceCommand.Execute(null);
        vm.FaceOnFloorCommand.Execute(null);
        Assert.False(vm.IsPickingSectionFace);

        vm.SectionAtFaceCommand.Execute(null);
        vm.FaceToFaceCommand.Execute(null);
        Assert.False(vm.IsPickingSectionFace);

        vm.SectionAtFaceCommand.Execute(null);
        vm.FacePickedCommand.Execute(null); // Esc
        Assert.False(vm.IsPickingSectionFace);
        Assert.False(vm.IsFacePickActive);
    }

    [UiFact]
    public async Task The_face_mode_says_what_to_click()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);

        vm.SectionAtFaceCommand.Execute(null);

        Assert.Contains("face", vm.HintText, StringComparison.OrdinalIgnoreCase);
    }

    [UiFact]
    public async Task Closing_the_panel_switches_the_view_off()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);

        vm.CloseSectionCommand.Execute(null);
        await vm.WaitForEvaluationAsync();

        Assert.False(vm.IsSectionActive);
        Assert.Empty(Caps(vm));
    }

    [UiFact]
    public async Task Showing_it_again_remembers_the_position()
    {
        var (vm, _) = NewViewModel();
        await AddBox(vm);
        await Show(vm);
        vm.SectionPosition = 4;
        vm.IsSectionActive = false;

        await Show(vm);

        Assert.Equal(4, vm.SectionPosition, 1e-9);
        Assert.Equal(4, BodyBounds(vm).Max.X, 1e-3);
    }

    [UiFact]
    public async Task Hiding_the_holes_still_cuts_the_solids()
    {
        var (vm, _) = NewViewModel();
        var box = await AddBox(vm);
        vm.PlaceHole(new FacePick(box.Id, new Vector3(0, 0, 20), Vector3.UnitZ));
        await vm.WaitForEvaluationAsync();
        await Show(vm);

        vm.ShowHoles = false;
        await vm.WaitForEvaluationAsync();

        Assert.DoesNotContain(vm.ViewportItems, i => i.Kind == ViewportItemKind.Ghost);
        Assert.Single(Caps(vm));
    }
}
