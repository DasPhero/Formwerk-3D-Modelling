using System.IO;
using Formwerk.IO;
using Formwerk.Kernel;
using Formwerk.Repair;

namespace Formwerk.Tests.App;

/// <summary>
/// The repair corpus of spec section 10 (F-REP-02): damaged meshes in testdata/meshes/repair-corpus (made by a script, nothing from
/// outside), every one of which the repair is meant to fix, and two known limits that must stay reported.
/// </summary>
public class RepairCorpusTests
{
    private static string CorpusFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "testdata", "meshes", "repair-corpus");
            if (Directory.Exists(candidate)) return candidate;
        }

        throw new DirectoryNotFoundException("testdata/meshes/repair-corpus was not found above " + AppContext.BaseDirectory);
    }

    private static Formwerk.Geometry.MeshData Load(string path)
    {
        using var stream = File.OpenRead(path);
        return StlReader.Read(stream);
    }

    [Fact]
    public void The_corpus_has_at_least_twenty_damaged_meshes_and_every_one_has_a_problem_before_the_repair()
    {
        var files = Directory.GetFiles(CorpusFolder(), "*.stl");

        Assert.True(files.Length >= 20);
        foreach (var file in files)
            Assert.False(MeshAnalyzer.Analyze(Load(file)).IsHealthy, Path.GetFileName(file) + " is not damaged");
    }

    [Fact]
    public void At_least_95_percent_of_the_corpus_is_watertight_and_manifold_after_the_repair_and_all_of_it_here()
    {
        var kernel = new ManifoldKernel();
        var files = Directory.GetFiles(CorpusFolder(), "*.stl").Order().ToList();
        var failed = new List<string>();
        foreach (var file in files)
        {
            var result = MeshRepair.Repair(Load(file), kernel: kernel);
            if (!result.After.IsHealthy || !result.Mesh.IsWatertight()) failed.Add($"{Path.GetFileName(file)}: {result.After}");
        }

        var rate = 1.0 - (double)failed.Count / files.Count;
        Assert.True(rate >= 0.95, $"{failed.Count} of {files.Count} are not healthy after the repair: {string.Join(" | ", failed)}");
        Assert.Empty(failed); // the corpus is made of cases the repair is meant to fix
    }

    [Fact]
    public void The_repair_never_makes_a_mesh_bigger_than_the_sum_of_its_bodies()
    {
        var kernel = new ManifoldKernel();
        foreach (var file in Directory.GetFiles(CorpusFolder(), "*.stl"))
        {
            var mesh = Load(file);
            var result = MeshRepair.Repair(mesh, kernel: kernel);

            Assert.True(result.Mesh.ComputeBounds().Size.Length() <= mesh.ComputeBounds().Size.Length() + 1e-3f, Path.GetFileName(file));
        }
    }

    [Fact]
    public void The_known_limits_stay_reported_and_nothing_breaks()
    {
        var kernel = new ManifoldKernel();
        var folder = Path.Combine(CorpusFolder(), "known-limits");

        var bowTie = MeshRepair.Repair(Load(Path.Combine(folder, "bow-tie.stl")), kernel: kernel);
        Assert.Equal(1, bowTie.After.NonManifoldVertices);
        Assert.False(bowTie.After.IsHealthy);

        var folded = MeshRepair.Repair(Load(Path.Combine(folder, "folded-box.stl")), kernel: kernel);
        Assert.True(folded.After.SelfIntersections > 0);
        Assert.False(folded.After.IsHealthy);
    }
}
