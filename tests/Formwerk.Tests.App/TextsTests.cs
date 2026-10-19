using System.IO;
using System.Text.RegularExpressions;
using Formwerk.App.Resources;

namespace Formwerk.Tests.App;

public class TextsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Formwerk.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Formwerk.sln not found above the test output folder.");
    }

    [Fact]
    public void Every_text_used_in_xaml_exists_in_the_resource_file()
    {
        var keys = Texts.Keys().ToHashSet();
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Formwerk.App", "MainWindow.xaml"));

        var used = Regex.Matches(xaml, @"\{res:Loc ([A-Za-z0-9_.]+)\}").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(used);
        var missing = used.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Missing texts: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_text_used_in_code_exists_in_the_resource_file()
    {
        var keys = Texts.Keys().ToHashSet();
        var used = new HashSet<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot(), "src", "Formwerk.App"), "*.cs", SearchOption.AllDirectories)
                     .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(file), @"Texts\.(?:Get|Format)\(""([A-Za-z0-9_.]+)"""))
                used.Add(m.Groups[1].Value);
        }

        Assert.NotEmpty(used);
        var missing = used.Where(k => !keys.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Missing texts: " + string.Join(", ", missing));
    }

    [Fact]
    public void No_text_is_empty()
    {
        foreach (var key in Texts.Keys()) Assert.False(string.IsNullOrWhiteSpace(Texts.Get(key)), key);
    }

    [Fact]
    public void Missing_keys_are_visible_instead_of_empty()
    {
        Assert.Equal("!No.Such.Key!", Texts.Get("No.Such.Key"));
    }

    [Fact]
    public void Format_fills_placeholders()
    {
        Assert.Equal("3 objects selected.", Texts.Format("Props.MultipleSelected", 3));
    }
}
