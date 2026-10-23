using System.IO;
using Formwerk.App.ViewModels;
using Formwerk.Kernel;
using Formwerk.Repair;
using Formwerk.Viewport;

namespace Formwerk.Tests.App;

/// <summary>
/// The damaged sample files in testdata/repair (used for the manual check, docs/abnahme-p2.md) really show the problems that the
/// check list says, when they are imported like a user does (F-IMP-03, F-REP-01, F-REP-02).
/// </summary>
public sealed class RepairSampleFilesTests
{
    private static string SampleFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", "repair");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("testdata/repair was not found above " + AppContext.BaseDirectory);
    }

    private static async Task<MainViewModel> Import(string file)
    {
        var vm = new MainViewModel(new ManifoldKernel(), new FakeDialogs());
        await vm.ImportFilesAsync([Path.Combine(SampleFolder(), file)]);
        await vm.WaitForEvaluationAsync();
        await vm.WaitForRepairCheckAsync();
        return vm;
    }

    private static ProblemKind[] Kinds(MainViewModel vm) => [.. vm.RepairProblems.Select(p => p.Kind)];

    [UiFact]
    public async Task Every_sample_file_shows_its_problem_and_the_repair_helps_where_it_can()
    {
        var expected = new (string File, ProblemKind Kind, bool Repairable)[]
        {
            ("box-with-hole.stl", ProblemKind.OpenEdges, true),
            ("overlapping-boxes.stl", ProblemKind.SelfIntersections, true),
            ("doubled-triangle.stl", ProblemKind.DuplicateTriangles, true),
            ("turned-triangle.stl", ProblemKind.InconsistentEdges, true),
            ("speck.stl", ProblemKind.LooseSpecks, true),
            ("bow-tie.stl", ProblemKind.NonManifoldVertices, false),
        };

        foreach (var (file, kind, repairable) in expected)
        {
            var vm = await Import(file);

            Assert.Contains(kind, Kinds(vm));
            Assert.True(vm.IsRepairBannerVisible, file);
            Assert.Equal(repairable, vm.RepairMeshCommand.CanExecute(null));

            if (!repairable) continue;
            await vm.RepairMeshCommand.ExecuteAsync(null);
            await vm.WaitForEvaluationAsync();
            await vm.WaitForRepairCheckAsync();
            Assert.Empty(vm.RepairProblems);
            Assert.False(vm.IsRepairBannerVisible, file);
        }
    }

    [UiFact]
    public async Task The_overlapping_boxes_become_one_body_with_less_volume_than_the_two_together()
    {
        var vm = await Import("overlapping-boxes.stl");

        await vm.RepairMeshCommand.ExecuteAsync(null);
        await vm.WaitForEvaluationAsync();

        var volume = vm.ViewportItems.Where(i => i.Kind == ViewportItemKind.Solid).Sum(i => i.Mesh.ComputeVolume());
        Assert.True(volume > 8000 && volume < 16000, $"volume {volume}"); // two boxes of 8000, overlapping by 12 x 14 x 9 = 1512
        Assert.Equal(16000 - 1512, volume, 1);
    }

    [UiFact]
    public async Task The_bow_tie_shows_one_touching_point_that_can_be_marked()
    {
        var vm = await Import("bow-tie.stl");

        var item = vm.RepairProblems.Single(p => p.Kind == ProblemKind.NonManifoldVertices);
        Assert.Equal(1, item.Count);
        vm.HighlightProblemCommand.Execute(item);
        await vm.WaitForRepairCheckAsync();
        Assert.Single(vm.ProblemMarks!.Points);
    }
}
