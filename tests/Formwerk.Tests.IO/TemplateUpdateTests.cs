using System.IO.Compression;
using System.Numerics;
using System.Text;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>Updating a template with a new version of its objects (F-TPL-05).</summary>
public sealed class TemplateUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"formwerk-tplupd-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private TemplateStore Store(string name = "library") => new(Path.Combine(_root, name));

    private static FormwerkDocument Doc(string width = "20")
    {
        var doc = new FormwerkDocument();
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = width } };
        new AddNodeCommand(doc, doc.Root, box).Do();
        return doc;
    }

    private static TemplateInfo Info(string name, string category = "", string description = "", params string[] tags) =>
        new(name, description, category, tags, new Vector3(20, 20, 20), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

    private static string Width(string path) => ((PrimitiveNode)TemplateFile.Load(path).Root.Children[0]).Parameters["width"];

    // ---- the version number --------------------------------------------------------------------------

    [Fact]
    public void A_new_template_has_version_1()
    {
        var store = Store();
        store.Save(Info("Hinge"), Doc(), null, null);

        Assert.Equal(1, Assert.Single(store.List()).Info.Version);
    }

    [Fact]
    public void The_version_is_stored_in_the_file()
    {
        var store = Store();
        var path = store.Save(Info("Hinge") with { Version = 7 }, Doc(), null, null);

        Assert.Equal(7, TemplateFile.LoadInfo(path).Info.Version);
    }

    [Fact]
    public void A_file_without_a_version_counts_as_version_1()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        StripVersion(path);

        Assert.Equal(1, TemplateFile.LoadInfo(path).Info.Version);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("1000000")]
    [InlineData("\"two\"")]
    [InlineData("1.5")]
    public void A_version_that_makes_no_sense_is_refused(string value)
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        SetVersionText(path, value);

        Assert.Throws<TemplateFormatException>(() => TemplateFile.LoadInfo(path));
    }

    [Fact]
    public void Renaming_a_template_keeps_its_version()
    {
        var store = Store();
        var path = store.Save(Info("Hinge") with { Version = 4 }, Doc(), null, null);

        store.UpdateInfo(path, Info("Door hinge")); // the description given has version 1

        Assert.Equal(4, Assert.Single(store.List()).Info.Version);
    }

    [Fact]
    public void Duplicating_and_exporting_keep_the_version()
    {
        var store = Store();
        var path = store.Save(Info("Hinge") with { Version = 4 }, Doc(), null, null);

        store.Duplicate(path);
        var destination = Path.Combine(_root, "export.fwt");
        store.Export(path, destination);
        var imported = Store("other").Import(destination);

        Assert.All(store.List(), e => Assert.Equal(4, e.Info.Version));
        Assert.Equal(4, imported.Info.Version);
    }

    // ---- updating ------------------------------------------------------------------------------------

    [Fact]
    public void Updating_replaces_the_objects_raises_the_version_and_keeps_the_description()
    {
        var store = Store();
        var path = store.Save(Info("Hinge", "Doors", "Opens", "door", "metal"), Doc("20"), null, [1, 2, 3]);

        store.Update(path, Doc("45"), null, new Vector3(45, 20, 20), [9, 9]);

        var entry = Assert.Single(store.List());
        Assert.Equal(path, entry.Path); // the same file
        Assert.Equal("45", Width(path));
        Assert.Equal(2, entry.Info.Version);
        Assert.Equal("Hinge", entry.Info.Name);
        Assert.Equal("Doors", entry.Info.Category);
        Assert.Equal("Opens", entry.Info.Description);
        Assert.Equal(["door", "metal"], entry.Info.Tags);
        Assert.Equal(new Vector3(45, 20, 20), entry.Info.Size);
        Assert.Equal([9, 9], entry.Preview);
    }

    [Fact]
    public void Every_update_raises_the_version_by_one_and_keeps_the_creation_date()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        var created = store.List()[0].Info.CreatedUtc;

        store.Update(path, Doc("1"), null, new Vector3(1, 1, 1), null);
        store.Update(path, Doc("2"), null, new Vector3(2, 1, 1), null);

        var info = Assert.Single(store.List()).Info;
        Assert.Equal(3, info.Version);
        Assert.Equal(created, info.CreatedUtc);
    }

    [Fact]
    public void Updating_without_a_new_picture_leaves_the_template_without_one()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, [1, 2, 3]);

        store.Update(path, Doc("30"), null, new Vector3(30, 20, 20), null);

        Assert.Null(Assert.Single(store.List()).Preview); // an old picture would show the old shape
    }

    [Fact]
    public void Other_templates_are_not_touched_by_an_update()
    {
        var store = Store();
        var hinge = store.Save(Info("Hinge"), Doc("20"), null, null);
        var latch = store.Save(Info("Latch"), Doc("33"), null, null);

        store.Update(hinge, Doc("45"), null, new Vector3(45, 20, 20), null);

        Assert.Equal("33", Width(latch));
        Assert.Equal(1, store.List().Single(e => e.Info.Name == "Latch").Info.Version);
    }

    [Fact]
    public void Updating_a_file_outside_the_library_is_refused()
    {
        var store = Store();
        store.Save(Info("Hinge"), Doc(), null, null);
        var outside = Path.Combine(_root, "elsewhere.fwt");
        TemplateFile.SaveAtomic(outside, Doc("20"), null, Info("Outside"), null);

        Assert.Throws<ArgumentException>(() => store.Update(outside, Doc("45"), null, new Vector3(45, 20, 20), null));
        Assert.Equal("20", Width(outside));
    }

    [Fact]
    public void A_damaged_file_cannot_be_updated_and_stays_as_it_is()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        File.WriteAllText(path, "not a zip");

        Assert.Throws<TemplateFormatException>(() => store.Update(path, Doc("45"), null, new Vector3(45, 20, 20), null));
        Assert.Equal("not a zip", File.ReadAllText(path));
    }

    [Fact]
    public void Updating_a_file_that_is_gone_fails_without_creating_it()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);
        File.Delete(path);

        Assert.ThrowsAny<IOException>(() => store.Update(path, Doc("45"), null, new Vector3(45, 20, 20), null));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void Imported_meshes_are_part_of_the_new_version()
    {
        var store = Store();
        var path = store.Save(Info("Part"), Doc(), null, null);
        var meshes = new MeshStore();
        var doc = new FormwerkDocument();
        var meshId = Guid.NewGuid();
        meshes.Add(meshId, new MeshData([Vector3.Zero, Vector3.UnitX, Vector3.UnitY], [0, 1, 2]));
        new AddNodeCommand(doc, doc.Root, new MeshNode("Imported", meshId)).Do();

        store.Update(path, doc, meshes, new Vector3(10, 10, 10), null);

        var loaded = new MeshStore();
        var back = TemplateFile.Load(path, loaded);
        var node = Assert.IsType<MeshNode>(back.Root.Children[0]);
        Assert.NotNull(loaded.Get(node.MeshId));
    }

    // ---- helpers -------------------------------------------------------------------------------------

    private static void StripVersion(string path) => EditInfo(path, json => System.Text.RegularExpressions.Regex.Replace(json, ",\\s*\"version\":\\s*[^,\\n}]*", ""));

    private static void SetVersionText(string path, string value) =>
        EditInfo(path, json => System.Text.RegularExpressions.Regex.Replace(json, "\"version\":\\s*[^,\\n}]*", "\"version\": " + value));

    private static void EditInfo(string path, Func<string, string> change)
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
                if (entry.FullName == "template.json") data = Encoding.UTF8.GetBytes(change(Encoding.UTF8.GetString(data)));
                var copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                using var write = copy.Open();
                write.Write(data);
            }
        }

        File.WriteAllBytes(path, output.ToArray());
    }
}
