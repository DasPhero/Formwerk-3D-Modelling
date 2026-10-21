using System.IO.Compression;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>Shared parameters of a template: the ones that are asked when it is inserted (F-TPL-06).</summary>
public sealed class TemplateSharedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"formwerk-tplshared-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TemplateStore Store(string name = "library") => new(Path.Combine(_root, name));

    /// <summary>A scene with a box and a group (that holds a cylinder), so paths [0], [1] and [1, 0] exist.</summary>
    private static FormwerkDocument Doc(string width = "20")
    {
        var doc = new FormwerkDocument();
        new AddNodeCommand(doc, doc.Root, new PrimitiveNode("box", "Box") { Parameters = { ["width"] = width } }).Do();
        var group = new GroupNode("Group");
        new AddNodeCommand(doc, doc.Root, group).Do();
        new AddNodeCommand(doc, group, new PrimitiveNode("cylinder", "Cylinder")).Do();
        return doc;
    }

    private static TemplateInfo Info(string name, params SharedParameter[] shared) =>
        new(name, "", "", [], new Vector3(20, 20, 20), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc), 1, shared);

    private static SharedParameter Shared(string label, string parameter = "width", params int[] path) =>
        new(path.Length == 0 ? [0] : path, parameter, label);

    // ---- the file ------------------------------------------------------------------------------------

    [Fact]
    public void A_template_has_no_shared_parameters_by_default()
    {
        var store = Store();
        var path = store.Save(new TemplateInfo("Hinge", "", "", [], Vector3.One, DateTime.UtcNow), Doc(), null, null);

        Assert.Empty(TemplateFile.LoadInfo(path).Info.Shared ?? []);
    }

    [Fact]
    public void Shared_parameters_are_stored_and_read_back_in_order()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", Shared("Length"), Shared("Pin", "diameter", 1, 0)), Doc(), null, null);

        var shared = TemplateFile.LoadInfo(path).Info.Shared!;

        Assert.Equal(2, shared.Count);
        Assert.Equal("Length", shared[0].Label);
        Assert.Equal("width", shared[0].Parameter);
        Assert.Equal([0], shared[0].Path);
        Assert.Equal("Pin", shared[1].Label);
        Assert.Equal("diameter", shared[1].Parameter);
        Assert.Equal([1, 0], shared[1].Path);
    }

    [Fact]
    public void Labels_with_special_characters_survive()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", Shared("Länge \"außen\" <b> & ü")), Doc(), null, null);

        Assert.Equal("Länge \"außen\" <b> & ü", TemplateFile.LoadInfo(path).Info.Shared![0].Label);
    }

    [Fact]
    public void Duplicating_exporting_and_importing_keep_the_shared_parameters()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", Shared("Length")), Doc(), null, null);

        store.Duplicate(path);
        var destination = Path.Combine(_root, "export.fwt");
        store.Export(path, destination);
        var imported = Store("other").Import(destination);

        Assert.All(store.List(), e => Assert.Equal("Length", Assert.Single(e.Info.Shared!).Label));
        Assert.Equal("Length", Assert.Single(imported.Info.Shared!).Label);
    }

    [Fact]
    public void Renaming_keeps_the_shared_parameters_even_when_the_given_description_has_none()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", Shared("Length")), Doc(), null, null);

        store.UpdateInfo(path, new TemplateInfo("Door hinge", "", "", [], Vector3.One, DateTime.UtcNow));

        Assert.Equal("Length", Assert.Single(Assert.Single(store.List()).Info.Shared!).Label);
    }

    // ---- files that are wrong ------------------------------------------------------------------------

    [Theory]
    [InlineData("\"shared\": 5")]
    [InlineData("\"shared\": [ 5 ]")]
    [InlineData("\"shared\": [ { \"parameter\": \"width\", \"label\": \"L\" } ]")] // no path
    [InlineData("\"shared\": [ { \"path\": [], \"parameter\": \"width\", \"label\": \"L\" } ]")] // empty path
    [InlineData("\"shared\": [ { \"path\": [-1], \"parameter\": \"width\", \"label\": \"L\" } ]")]
    [InlineData("\"shared\": [ { \"path\": [\"a\"], \"parameter\": \"width\", \"label\": \"L\" } ]")]
    [InlineData("\"shared\": [ { \"path\": [0], \"parameter\": \"\", \"label\": \"L\" } ]")]
    [InlineData("\"shared\": [ { \"path\": [0], \"parameter\": \"width\", \"label\": \"\" } ]")]
    [InlineData("\"shared\": [ { \"path\": [0], \"parameter\": \"width\" } ]")] // no label
    [InlineData("\"shared\": [ { \"path\": [0], \"parameter\": 7, \"label\": \"L\" } ]")]
    public void A_shared_list_that_is_broken_makes_the_file_unusable(string sharedJson)
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        AddToInfo(path, sharedJson);

        Assert.Throws<TemplateFormatException>(() => TemplateFile.LoadInfo(path));
    }

    [Fact]
    public void Too_long_a_label_too_deep_a_path_or_too_many_entries_are_refused()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);

        AddToInfo(path, "\"shared\": [ { \"path\": [0], \"parameter\": \"width\", \"label\": \"" + new string('x', TemplateLimits.MaxSharedLabelLength + 1) + "\" } ]");
        Assert.Throws<TemplateFormatException>(() => TemplateFile.LoadInfo(path));

        var deep = string.Join(",", Enumerable.Repeat("0", TemplateLimits.MaxSharedPathDepth + 1));
        path = store.Save(Info("Latch"), Doc(), null, null);
        AddToInfo(path, "\"shared\": [ { \"path\": [" + deep + "], \"parameter\": \"width\", \"label\": \"L\" } ]");
        Assert.Throws<TemplateFormatException>(() => TemplateFile.LoadInfo(path));

        var many = string.Join(",", Enumerable.Range(0, TemplateLimits.MaxShared + 1).Select(i => "{ \"path\": [0], \"parameter\": \"width\", \"label\": \"L" + i + "\" }"));
        path = store.Save(Info("Bolt"), Doc(), null, null);
        AddToInfo(path, "\"shared\": [" + many + "]");
        Assert.Throws<TemplateFormatException>(() => TemplateFile.LoadInfo(path));
    }

    [Fact]
    public void A_broken_template_is_skipped_in_the_list_but_the_others_stay()
    {
        var store = Store();
        store.Save(Info("Good", Shared("Length")), Doc(), null, null);
        var bad = store.Save(Info("Bad"), Doc(), null, null);
        AddToInfo(bad, "\"shared\": 5");

        Assert.Equal(["Good"], store.List().Select(e => e.Info.Name).ToArray());
    }

    // ---- finding the node ----------------------------------------------------------------------------

    [Fact]
    public void A_path_finds_the_node_in_the_scene()
    {
        var doc = Doc();

        Assert.Same(doc.Root.Children[0], TemplateShared.Find(doc, Shared("L", "width", 0)));
        Assert.Same(((GroupNode)doc.Root.Children[1]).Children[0], TemplateShared.Find(doc, Shared("L", "diameter", 1, 0)));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(2)]
    public void A_path_outside_the_scene_finds_nothing(int index)
    {
        Assert.Null(TemplateShared.Find(Doc(), Shared("L", "width", index)));
    }

    [Fact]
    public void A_path_through_a_node_without_that_child_finds_nothing()
    {
        Assert.Null(TemplateShared.Find(Doc(), Shared("L", "width", 0, 0))); // the box has no children
        Assert.Null(TemplateShared.Find(Doc(), Shared("L", "width", 1, 3)));
    }

    [Fact]
    public void A_node_that_has_no_parameters_cannot_be_shared()
    {
        Assert.Null(TemplateShared.Find(Doc(), Shared("L", "width", 1))); // the group
    }

    // ---- updating ------------------------------------------------------------------------------------

    [Fact]
    public void Updating_keeps_the_shared_parameters_that_still_fit_and_drops_the_others()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", Shared("Length", "width", 0), Shared("Pin", "diameter", 1, 0)), Doc(), null, null);
        var smaller = new FormwerkDocument();
        new AddNodeCommand(smaller, smaller.Root, new PrimitiveNode("box", "Box")).Do(); // only the box is left

        var written = store.Update(path, smaller, null, new Vector3(20, 20, 20), null);

        var shared = Assert.Single(written.Shared!);
        Assert.Equal("Length", shared.Label);
        Assert.Equal("Length", Assert.Single(Assert.Single(store.List()).Info.Shared!).Label);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>Puts a JSON member (for example <c>"shared": ...</c>) into the description of a template file.</summary>
    private static void AddToInfo(string path, string member)
    {
        var bytes = File.ReadAllBytes(path);
        using var input = new MemoryStream(bytes);
        using var source = new ZipArchive(input, ZipArchiveMode.Read);
        using var output = new MemoryStream();
        using (var target = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var read = entry.Open();
                using var buffer = new MemoryStream();
                read.CopyTo(buffer);
                var data = buffer.ToArray();
                if (entry.FullName == "template.json")
                {
                    var json = Encoding.UTF8.GetString(data);
                    json = Regex.Replace(json, "\\}\\s*$", ",\n" + member.Replace("$", "$$") + "\n}");
                    data = Encoding.UTF8.GetBytes(json);
                }

                var copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using var write = copy.Open();
                write.Write(data);
            }
        }

        File.WriteAllBytes(path, output.ToArray());
    }
}
