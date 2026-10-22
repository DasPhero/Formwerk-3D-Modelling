using System.IO;
using System.IO.Compression;
using Formwerk.App.ViewModels;
using Formwerk.Core.Model;
using Formwerk.IO;
using Formwerk.Kernel;

namespace Formwerk.Tests.App;

/// <summary>The color of objects: palette, hex input, groups, undo, 3MF and project file (F-VIS-01).</summary>
public sealed class ColorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-color-{Guid.NewGuid():N}");
    private readonly FakeDialogs _dialogs = new();

    public ColorTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private MainViewModel NewViewModel() => new(new ManifoldKernel(), _dialogs);

    private static async Task<Node> Add(MainViewModel vm, string shape)
    {
        vm.AddShapeCommand.Execute(shape);
        await vm.WaitForEvaluationAsync();
        var node = vm.Document.Root.Children[^1];
        vm.SelectOnly([node.Id]);
        return node;
    }

    private const uint Default = 0xFFB0B8C4;

    // ---- palette ---------------------------------------------------------------------------------

    [UiFact]
    public void The_palette_has_twelve_distinct_named_colors_and_the_default_gray()
    {
        var vm = NewViewModel();

        Assert.Equal(12, vm.ColorSwatches.Count);
        Assert.Equal(12, vm.ColorSwatches.Select(s => s.Argb).Distinct().Count());
        Assert.All(vm.ColorSwatches, s => Assert.False(string.IsNullOrWhiteSpace(s.Name)));
        Assert.All(vm.ColorSwatches, s => Assert.Equal(0xFF000000u, s.Argb & 0xFF000000u));
        Assert.Contains(vm.ColorSwatches, s => s.Argb == Default);
    }

    [UiFact]
    public async Task Choosing_a_swatch_colors_the_selected_object_in_one_undo_step()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var swatch = vm.ColorSwatches.First(s => s.Argb != Default);
        var steps = vm.HistoryEntries.Count;

        vm.ApplyColorCommand.Execute(swatch);

        Assert.Equal(swatch.Argb, box.Color);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);
        Assert.Equal(swatch.Argb, vm.SelectionColor);

        vm.UndoCommand.Execute(null);
        Assert.Equal(Default, box.Color);
        Assert.Equal(Default, vm.SelectionColor);

        vm.RedoCommand.Execute(null);
        Assert.Equal(swatch.Argb, box.Color);
    }

    [UiFact]
    public async Task The_same_color_again_is_no_step()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var swatch = vm.ColorSwatches.First(s => s.Argb != Default);
        vm.ApplyColorCommand.Execute(swatch);
        var steps = vm.HistoryEntries.Count;

        vm.ApplyColorCommand.Execute(swatch);

        Assert.Equal(steps, vm.HistoryEntries.Count);
        Assert.Equal(swatch.Argb, box.Color);
    }

    // ---- several objects, groups -----------------------------------------------------------------

    [UiFact]
    public async Task Several_selected_objects_get_the_color_in_one_step()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box");
        var b = await Add(vm, "cylinder");
        vm.SelectOnly([a.Id, b.Id]);
        var swatch = vm.ColorSwatches.First(s => s.Argb != Default);
        var steps = vm.HistoryEntries.Count;

        vm.ApplyColorCommand.Execute(swatch);

        Assert.Equal(swatch.Argb, a.Color);
        Assert.Equal(swatch.Argb, b.Color);
        Assert.Equal(steps + 1, vm.HistoryEntries.Count);

        vm.UndoCommand.Execute(null);
        Assert.Equal(Default, a.Color);
        Assert.Equal(Default, b.Color);
    }

    [UiFact]
    public async Task A_group_passes_its_color_on_to_everything_inside()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box");
        var b = await Add(vm, "cylinder");
        vm.SelectOnly([a.Id, b.Id]);
        vm.GroupCommand.Execute(null);
        var group = vm.Document.Root.Children.Single();
        vm.SelectOnly([group.Id]);
        var swatch = vm.ColorSwatches.First(s => s.Argb != Default);

        vm.ApplyColorCommand.Execute(swatch);

        Assert.Equal(swatch.Argb, group.Color);
        Assert.Equal(swatch.Argb, a.Color);
        Assert.Equal(swatch.Argb, b.Color);

        vm.UndoCommand.Execute(null);
        Assert.Equal(Default, group.Color);
        Assert.Equal(Default, a.Color);
        Assert.Equal(Default, b.Color);
    }

    [UiFact]
    public async Task A_boolean_node_colors_its_operands()
    {
        var vm = NewViewModel();
        var a = await Add(vm, "box");
        var b = await Add(vm, "cylinder");
        vm.SelectOnly([a.Id, b.Id]);
        vm.BooleanCommand.Execute(BooleanOperation.Difference);
        await vm.WaitForEvaluationAsync();
        var node = vm.Document.Root.Children.Single();
        vm.SelectOnly([node.Id]);
        var swatch = vm.ColorSwatches.First(s => s.Argb != Default);

        vm.ApplyColorCommand.Execute(swatch);

        Assert.Equal(swatch.Argb, node.Color);
        Assert.All(node.Descendants(), d => Assert.Equal(swatch.Argb, d.Color));
    }

    // ---- shown color -----------------------------------------------------------------------------

    [UiFact]
    public async Task The_panel_shows_the_color_of_the_selection_or_mixed_or_nothing()
    {
        var vm = NewViewModel();
        Assert.False(vm.IsColorVisible);

        var a = await Add(vm, "box");
        var b = await Add(vm, "cylinder");
        Assert.True(vm.IsColorVisible);

        vm.SelectOnly([a.Id]);
        Assert.Equal(Default, vm.SelectionColor);
        Assert.Equal("B0B8C4", vm.ColorHex);
        Assert.False(vm.IsColorMixed);

        vm.ApplyColorCommand.Execute(vm.ColorSwatches.First(s => s.Argb != Default));
        vm.SelectOnly([a.Id, b.Id]);
        Assert.True(vm.IsColorMixed);
        Assert.Null(vm.SelectionColor);
        Assert.Equal("", vm.ColorHex);

        vm.SelectOnly([]);
        Assert.False(vm.IsColorVisible);
    }

    // ---- hex input -------------------------------------------------------------------------------

    [UiFact]
    public async Task A_complete_hex_value_is_applied_with_or_without_hash_and_in_any_case()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");

        vm.ColorHex = "FF8800";
        Assert.Equal(0xFFFF8800u, box.Color);
        Assert.Equal("", vm.ColorError);

        vm.ColorHex = "#00ff00";
        Assert.Equal(0xFF00FF00u, box.Color);
        Assert.Equal("", vm.ColorError);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0xFFFF8800u, box.Color);
        Assert.Equal("FF8800", vm.ColorHex);
    }

    [UiFact]
    public async Task An_invalid_hex_value_shows_an_error_and_changes_nothing()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        var steps = vm.HistoryEntries.Count;

        foreach (var text in new[] { "xyz", "12345", "GG0000", "1234567" })
        {
            vm.ColorHex = text;
            Assert.NotEqual("", vm.ColorError);
            Assert.Equal(Default, box.Color);
        }

        Assert.Equal(steps, vm.HistoryEntries.Count);

        vm.ColorHex = "";
        Assert.Equal("", vm.ColorError);
    }

    [UiFact]
    public async Task An_incomplete_value_shows_a_hint_until_it_is_complete()
    {
        var vm = NewViewModel();
        await Add(vm, "box");

        vm.ColorHex = "FF8";
        Assert.NotEqual("", vm.ColorError);
        vm.ColorHex = "FF8800";
        Assert.Equal("", vm.ColorError);
    }

    // ---- chain to the files ----------------------------------------------------------------------

    [UiFact]
    public async Task The_color_reaches_the_3mf_file()
    {
        var vm = NewViewModel();
        await Add(vm, "box");
        vm.ColorHex = "336699";
        await vm.WaitForEvaluationAsync();
        var path = Path.Combine(_folder, "colored.3mf");
        _dialogs.SavePath = path;

        await vm.Export3mfCommand.ExecuteAsync(null);

        using var zip = new ZipArchive(new MemoryStream(File.ReadAllBytes(path)));
        using var model = new StreamReader(zip.GetEntry("3D/3dmodel.model")!.Open());
        Assert.Contains("#336699FF", model.ReadToEnd());
    }

    [UiFact]
    public async Task The_color_survives_saving_and_loading_the_project()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");
        vm.ColorHex = "336699";
        using var stream = new MemoryStream();

        ProjectFile.Write(stream, vm.Document, vm.Meshes);
        stream.Position = 0;
        var back = ProjectFile.Read(stream, new Formwerk.Geometry.MeshStore());

        Assert.Equal(0xFF336699u, back.FindNode(box.Id)!.Color);
    }

    [UiFact]
    public async Task The_viewport_draws_the_new_color()
    {
        var vm = NewViewModel();
        var box = await Add(vm, "box");

        vm.ColorHex = "336699";
        await vm.WaitForEvaluationAsync();

        Assert.Contains(vm.ViewportItems, i => i.NodeId == box.Id && i.ColorArgb == 0xFF336699u);
    }
}
