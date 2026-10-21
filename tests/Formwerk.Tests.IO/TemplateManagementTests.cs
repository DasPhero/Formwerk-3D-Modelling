using System.IO.Compression;
using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>Managing and exchanging templates (F-TPL-04, F-TPL-07): rename, describe, categorize, duplicate, delete, export, import.</summary>
public sealed class TemplateManagementTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"formwerk-tplman-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Folder(string name = "library") => Path.Combine(_root, name);

    private TemplateStore Store(string name = "library") => new(Folder(name));

    private static FormwerkDocument Doc(string width = "20")
    {
        var doc = new FormwerkDocument();
        var box = new PrimitiveNode("box", "Box") { Parameters = { ["width"] = width } };
        new AddNodeCommand(doc, doc.Root, box).Do();
        return doc;
    }

    private static TemplateInfo Info(string name, string category = "", string description = "", params string[] tags) =>
        new(name, description, category, tags, new Vector3(20, 20, 20), new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

    private static string Save(TemplateStore store, string name, string category = "", string width = "20", byte[]? preview = null) =>
        store.Save(Info(name, category), Doc(width), null, preview);

    // ---- changing the description --------------------------------------------------------------------

    [Fact]
    public void Renaming_changes_the_name_and_keeps_the_objects_and_the_preview()
    {
        var store = Store();
        var path = Save(store, "Hinge", width: "33", preview: [1, 2, 3, 4]);

        store.UpdateInfo(path, Info("Door hinge"));

        var entry = Assert.Single(store.List());
        Assert.Equal("Door hinge", entry.Info.Name);
        Assert.Equal(path, entry.Path); // the same file
        Assert.Equal([1, 2, 3, 4], entry.Preview);
        Assert.Equal("33", ((PrimitiveNode)TemplateFile.Load(path).Root.Children[0]).Parameters["width"]);
    }

    [Fact]
    public void Description_category_and_tags_can_be_changed()
    {
        var store = Store();
        var path = Save(store, "Hinge");

        store.UpdateInfo(path, Info("Hinge", "Mechanics", "Opens and closes", "door", "metal"));

        var info = Assert.Single(store.List()).Info;
        Assert.Equal("Mechanics", info.Category);
        Assert.Equal("Opens and closes", info.Description);
        Assert.Equal(["door", "metal"], info.Tags);
    }

    [Fact]
    public void The_size_and_the_creation_date_stay_as_given()
    {
        var store = Store();
        var path = Save(store, "Hinge");
        var created = new DateTime(2020, 5, 17, 8, 30, 0, DateTimeKind.Utc);

        store.UpdateInfo(path, new TemplateInfo("Hinge", "", "", [], new Vector3(1, 2, 3), created));

        var info = Assert.Single(store.List()).Info;
        Assert.Equal(new Vector3(1, 2, 3), info.Size);
        Assert.Equal(created, info.CreatedUtc);
    }

    [Fact]
    public void A_name_that_another_template_has_in_the_category_is_refused_with_a_suggestion()
    {
        var store = Store();
        Save(store, "Hinge");
        var other = Save(store, "Latch");

        var error = Assert.Throws<InvalidOperationException>(() => store.UpdateInfo(other, Info("hinge")));

        Assert.Contains("Hinge (2)", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["Hinge", "Latch"], store.List().Select(e => e.Info.Name).ToArray()); // nothing changed
    }

    [Fact]
    public void The_own_name_is_no_conflict_so_description_and_case_can_change()
    {
        var store = Store();
        var path = Save(store, "Hinge");

        store.UpdateInfo(path, Info("HINGE", description: "new text"));

        Assert.Equal("HINGE", Assert.Single(store.List()).Info.Name);
    }

    [Fact]
    public void The_same_name_is_fine_in_another_category()
    {
        var store = Store();
        Save(store, "Hinge", "Doors");
        var other = Save(store, "Latch", "Windows");

        store.UpdateInfo(other, Info("Hinge", "Windows"));

        Assert.Equal(2, store.List().Count(e => e.Info.Name == "Hinge"));
    }

    [Fact]
    public void Changing_the_category_into_one_where_the_name_is_taken_is_refused()
    {
        var store = Store();
        Save(store, "Hinge", "Doors");
        var other = Save(store, "Hinge", "Windows");

        Assert.Throws<InvalidOperationException>(() => store.UpdateInfo(other, Info("Hinge", "Doors")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_template_cannot_lose_its_name(string name)
    {
        var store = Store();
        var path = Save(store, "Hinge");

        Assert.Throws<ArgumentException>(() => store.UpdateInfo(path, Info(name)));
        Assert.Equal("Hinge", Assert.Single(store.List()).Info.Name);
    }

    [Fact]
    public void A_file_outside_the_folder_is_not_touched()
    {
        var store = Store();
        Directory.CreateDirectory(_root);
        var outside = Path.Combine(_root, "outside.fwt");
        TemplateFile.SaveAtomic(outside, Doc(), null, Info("Outside"), null);
        var before = File.ReadAllBytes(outside);

        Assert.Throws<ArgumentException>(() => store.UpdateInfo(outside, Info("Changed")));

        Assert.Equal(before, File.ReadAllBytes(outside));
    }

    [Fact]
    public void A_failed_change_leaves_the_file_as_it_was_and_no_temporary_file_behind()
    {
        var store = Store();
        var path = Save(store, "Hinge");
        var before = File.ReadAllBytes(path);

        Assert.Throws<ArgumentException>(() => store.UpdateInfo(path, Info("")));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(Folder(), "*.tmp"));
    }

    // ---- duplicate -----------------------------------------------------------------------------------

    [Fact]
    public void A_duplicate_is_a_new_file_with_the_next_free_name_and_the_same_content()
    {
        var store = Store();
        var path = Save(store, "Hinge", "Doors", width: "44", preview: [9, 9]);

        var copyPath = store.Duplicate(path);

        Assert.NotEqual(path, copyPath);
        var entries = store.List();
        Assert.Equal(2, entries.Count);
        var copy = entries.Single(e => e.Path == copyPath);
        Assert.Equal("Hinge (2)", copy.Info.Name);
        Assert.Equal("Doors", copy.Info.Category);
        Assert.Equal([9, 9], copy.Preview);
        Assert.Equal("44", ((PrimitiveNode)TemplateFile.Load(copyPath).Root.Children[0]).Parameters["width"]);
    }

    [Fact]
    public void Duplicating_again_counts_on()
    {
        var store = Store();
        var path = Save(store, "Hinge");

        store.Duplicate(path);
        store.Duplicate(path);

        Assert.Equal(["Hinge", "Hinge (2)", "Hinge (3)"], store.List().Select(e => e.Info.Name).ToArray());
    }

    [Fact]
    public void Duplicating_leaves_the_original_untouched()
    {
        var store = Store();
        var path = Save(store, "Hinge");
        var before = File.ReadAllBytes(path);

        store.Duplicate(path);

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ---- delete --------------------------------------------------------------------------------------

    [Fact]
    public void Deleting_removes_the_file_and_only_that_one()
    {
        var store = Store();
        var keep = Save(store, "Latch");
        var path = Save(store, "Hinge");

        store.Delete(path);

        Assert.False(File.Exists(path));
        Assert.Equal(keep, Assert.Single(store.List()).Path);
    }

    // ---- export --------------------------------------------------------------------------------------

    [Fact]
    public void An_exported_template_is_a_normal_file_that_can_be_read_without_the_library()
    {
        var store = Store();
        var path = Save(store, "Hinge", "Doors", width: "55", preview: [7]);
        Directory.CreateDirectory(Folder("elsewhere"));
        var target = Path.Combine(Folder("elsewhere"), "hinge.fwt");

        store.Export(path, target);

        var (info, preview) = TemplateFile.LoadInfo(target);
        Assert.Equal("Hinge", info.Name);
        Assert.Equal([7], preview);
        Assert.Equal("55", ((PrimitiveNode)TemplateFile.Load(target).Root.Children[0]).Parameters["width"]);
        Assert.True(File.Exists(path)); // the library keeps its copy
    }

    [Fact]
    public void Exporting_over_an_existing_file_replaces_it_and_leaves_no_temporary_file()
    {
        var store = Store();
        var path = Save(store, "Hinge");
        Directory.CreateDirectory(Folder("elsewhere"));
        var target = Path.Combine(Folder("elsewhere"), "hinge.fwt");
        File.WriteAllText(target, "old content");

        store.Export(path, target);

        Assert.Equal("Hinge", TemplateFile.LoadInfo(target).Info.Name);
        Assert.Empty(Directory.GetFiles(Folder("elsewhere"), "*.tmp"));
    }

    // ---- import --------------------------------------------------------------------------------------

    [Fact]
    public void A_template_from_another_library_can_be_imported_and_inserted()
    {
        var other = Store("other");
        var source = Save(other, "Hinge", "Doors", width: "66", preview: [5, 5]);
        var mine = Store();

        var result = mine.Import(source);

        Assert.False(result.Renamed);
        var entry = Assert.Single(mine.List());
        Assert.Equal(result.Path, entry.Path);
        Assert.Equal("Hinge", entry.Info.Name);
        Assert.Equal("Doors", entry.Info.Category);
        Assert.Equal([5, 5], entry.Preview);
        Assert.Equal("66", ((PrimitiveNode)TemplateFile.Load(entry.Path).Root.Children[0]).Parameters["width"]);
        Assert.StartsWith(Path.GetFullPath(Folder()), Path.GetFullPath(entry.Path), StringComparison.OrdinalIgnoreCase); // inside the library
    }

    [Fact]
    public void Importing_a_template_whose_name_is_taken_gives_it_the_next_free_name_and_says_so()
    {
        var other = Store("other");
        var source = Save(other, "Hinge", "Doors", width: "66");
        var mine = Store();
        Save(mine, "Hinge", "Doors", width: "11");

        var result = mine.Import(source);

        Assert.True(result.Renamed);
        Assert.Equal("Hinge (2)", result.Info.Name);
        Assert.Equal(["Hinge", "Hinge (2)"], mine.List().Select(e => e.Info.Name).ToArray());
        Assert.Equal("11", ((PrimitiveNode)TemplateFile.Load(mine.List().Single(e => e.Info.Name == "Hinge").Path).Root.Children[0]).Parameters["width"]); // the own one stays
        Assert.Equal("66", ((PrimitiveNode)TemplateFile.Load(result.Path).Root.Children[0]).Parameters["width"]);
    }

    [Fact]
    public void Importing_the_same_file_twice_makes_two_templates()
    {
        var other = Store("other");
        var source = Save(other, "Hinge");
        var mine = Store();

        mine.Import(source);
        mine.Import(source);

        Assert.Equal(["Hinge", "Hinge (2)"], mine.List().Select(e => e.Info.Name).ToArray());
    }

    [Fact]
    public void The_library_folder_is_created_when_the_first_template_comes_in()
    {
        var other = Store("other");
        var source = Save(other, "Hinge");
        var mine = Store("not yet there");
        Assert.False(Directory.Exists(mine.Folder));

        mine.Import(source);

        Assert.Single(mine.List());
    }

    [Fact]
    public void A_file_that_is_no_template_is_refused_and_nothing_is_added()
    {
        var mine = Store();
        Directory.CreateDirectory(_root);
        var junk = Path.Combine(_root, "junk.fwt");
        File.WriteAllText(junk, "this is not a zip");

        Assert.Throws<TemplateFormatException>(() => mine.Import(junk));

        Assert.Empty(mine.List());
    }

    [Fact]
    public void A_zip_without_a_description_is_refused()
    {
        var mine = Store();
        Directory.CreateDirectory(_root);
        var fake = Path.Combine(_root, "fake.fwt");
        using (var zip = ZipFile.Open(fake, ZipArchiveMode.Create)) zip.CreateEntry("something.txt");

        Assert.Throws<TemplateFormatException>(() => mine.Import(fake));
    }

    [Fact]
    public void A_template_with_a_damaged_project_part_is_refused()
    {
        var other = Store("other");
        var source = Save(other, "Hinge");
        var broken = Path.Combine(_root, "broken.fwt");
        using (var zipIn = ZipFile.OpenRead(source))
        using (var zipOut = ZipFile.Open(broken, ZipArchiveMode.Create))
        {
            foreach (var entry in zipIn.Entries)
            {
                var copy = zipOut.CreateEntry(entry.FullName);
                using var to = copy.Open();
                if (entry.FullName == "project.fwp") to.Write([1, 2, 3, 4]);
                else
                {
                    using var from = entry.Open();
                    from.CopyTo(to);
                }
            }
        }

        var mine = Store();

        Assert.Throws<TemplateFormatException>(() => mine.Import(broken));
        Assert.Empty(mine.List());
    }

    [Fact]
    public void A_missing_file_is_a_clear_error()
    {
        var mine = Store();

        Assert.Throws<FileNotFoundException>(() => mine.Import(Path.Combine(_root, "nothing.fwt")));
    }

    [Fact]
    public void Export_then_import_gives_the_same_template_on_another_machine()
    {
        var mine = Store();
        var path = Save(mine, "Hinge", "Doors", width: "77", preview: [3, 1, 4]);
        Directory.CreateDirectory(Folder("usb"));
        var file = Path.Combine(Folder("usb"), "Hinge.fwt");
        mine.Export(path, file);

        var theirs = Store("other machine");
        var result = theirs.Import(file);

        Assert.False(result.Renamed);
        var entry = Assert.Single(theirs.List());
        Assert.Equal("Hinge", entry.Info.Name);
        Assert.Equal([3, 1, 4], entry.Preview);
        Assert.Equal("77", ((PrimitiveNode)TemplateFile.Load(entry.Path).Root.Children[0]).Parameters["width"]);
    }
}
