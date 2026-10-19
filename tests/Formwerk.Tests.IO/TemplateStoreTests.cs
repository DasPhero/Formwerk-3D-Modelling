using System.Numerics;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.Tests.IO;

/// <summary>The folder of saved templates (F-TPL-01): saving, listing, names that are taken.</summary>
public sealed class TemplateStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-templates-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private TemplateStore Store() => new(_folder);

    private static FormwerkDocument Doc()
    {
        var doc = new FormwerkDocument();
        new AddNodeCommand(doc, doc.Root, new PrimitiveNode("box", "Box")).Do();
        return doc;
    }

    private static TemplateInfo Info(string name, string category = "", params string[] tags) =>
        new(name, "", category, tags, new Vector3(20, 20, 20), DateTime.UtcNow);

    // ---- names ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_template_needs_a_name(string? name)
    {
        Assert.NotNull(TemplateNames.Validate(name));
    }

    [Fact]
    public void A_name_with_control_characters_or_far_too_long_is_refused()
    {
        Assert.NotNull(TemplateNames.Validate("bell\u0007here"));
        Assert.Null(TemplateNames.Validate("two\nlines")); // a line break is just white space and becomes a blank
        Assert.Equal("two lines", TemplateNames.Clean("two\nlines"));
        Assert.NotNull(TemplateNames.Validate(new string('x', TemplateLimits.MaxNameLength + 1)));
        Assert.Null(TemplateNames.Validate("Hinge plate 2"));
        Assert.Null(TemplateNames.Validate(new string('x', TemplateLimits.MaxNameLength)));
    }

    [Theory]
    [InlineData("Hinge", new[] { "Hinge" }, "Hinge (2)")]
    [InlineData("Hinge", new[] { "Hinge", "Hinge (2)" }, "Hinge (3)")]
    [InlineData("Hinge (2)", new[] { "Hinge", "Hinge (2)" }, "Hinge (3)")]
    [InlineData("hinge", new[] { "HINGE" }, "hinge (2)")]
    [InlineData("Hinge", new[] { "Other" }, "Hinge")]
    public void A_taken_name_gets_the_next_free_number(string name, string[] taken, string expected)
    {
        Assert.Equal(expected, TemplateNames.Suggest(name, taken));
    }

    [Fact]
    public void Extra_white_space_in_a_name_is_cleaned()
    {
        Assert.Equal("Hinge plate", TemplateNames.Clean("  Hinge   plate \t"));
    }

    // ---- the folder ----------------------------------------------------------------------------------

    [Fact]
    public void A_saved_template_is_listed_with_its_description_and_preview()
    {
        var store = Store();

        var path = store.Save(Info("Hinge", "Mechanics", "a", "b"), Doc(), null, [9, 9]);

        var entry = Assert.Single(store.List());
        Assert.Equal(path, entry.Path);
        Assert.Equal("Hinge", entry.Info.Name);
        Assert.Equal("Mechanics", entry.Info.Category);
        Assert.Equal(["a", "b"], entry.Info.Tags);
        Assert.Equal([9, 9], entry.Preview);
        Assert.EndsWith(TemplateFile.Extension, path);
    }

    [Fact]
    public void An_empty_or_missing_folder_lists_nothing_and_saving_creates_it()
    {
        var store = Store();
        Assert.Empty(store.List());
        Assert.False(Directory.Exists(_folder));

        store.Save(Info("A"), Doc(), null, null);

        Assert.True(Directory.Exists(_folder));
    }

    [Fact]
    public void The_list_is_sorted_by_category_and_then_by_name_ignoring_case()
    {
        var store = Store();
        store.Save(Info("b", "Zeta"), Doc(), null, null);
        store.Save(Info("a", "Zeta"), Doc(), null, null);
        store.Save(Info("c", ""), Doc(), null, null);
        store.Save(Info("B", "alpha"), Doc(), null, null);

        Assert.Equal(["c", "B", "a", "b"], store.List().Select(e => e.Info.Name));
    }

    [Fact]
    public void A_file_that_is_not_a_template_is_skipped_and_not_deleted()
    {
        var store = Store();
        store.Save(Info("Good"), Doc(), null, null);
        var broken = Path.Combine(_folder, "broken.fwt");
        File.WriteAllText(broken, "this is not a template");
        File.WriteAllText(Path.Combine(_folder, "notes.txt"), "not even a template extension");

        Assert.Equal(["Good"], store.List().Select(e => e.Info.Name));
        Assert.True(File.Exists(broken));
    }

    [Fact]
    public void The_same_name_in_the_same_category_is_taken_but_not_in_another_category()
    {
        var store = Store();
        store.Save(Info("Hinge", "Mechanics"), Doc(), null, null);

        Assert.True(store.NameTaken("hinge", "mechanics"));
        Assert.False(store.NameTaken("Hinge", "Boxes"));
        Assert.False(store.NameTaken("Hinge 2", "Mechanics"));
    }

    [Fact]
    public void Saving_a_taken_name_is_refused_with_a_suggestion_in_the_message()
    {
        var store = Store();
        store.Save(Info("Hinge", "Mechanics"), Doc(), null, null);

        var error = Assert.Throws<InvalidOperationException>(() => store.Save(Info("hinge", "Mechanics"), Doc(), null, null));

        Assert.Contains("Hinge (2)", error.Message.Replace("hinge (2)", "Hinge (2)"));
        Assert.Single(store.List());
    }

    [Fact]
    public void The_suggestion_for_a_category_looks_at_that_category_only()
    {
        var store = Store();
        store.Save(Info("Hinge", "Mechanics"), Doc(), null, null);
        store.Save(Info("Hinge (2)", "Mechanics"), Doc(), null, null);

        Assert.Equal("Hinge (3)", store.SuggestName("Hinge", "Mechanics"));
        Assert.Equal("Hinge", store.SuggestName("Hinge", "Other"));
    }

    [Fact]
    public void The_categories_in_use_are_listed_once_each()
    {
        var store = Store();
        store.Save(Info("a", "Mechanics"), Doc(), null, null);
        store.Save(Info("b", "mechanics"), Doc(), null, null);
        store.Save(Info("c", "Boxes"), Doc(), null, null);
        store.Save(Info("d", ""), Doc(), null, null);

        Assert.Equal(["Boxes", "Mechanics"], store.Categories().Order());
    }

    [Fact]
    public void A_name_cannot_make_the_file_leave_the_folder()
    {
        var store = Store();

        var path = store.Save(Info("..\\..\\evil/name:*?"), Doc(), null, null);

        Assert.Equal(Path.GetFullPath(_folder), Path.GetDirectoryName(Path.GetFullPath(path)));
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Saving_leaves_no_temporary_file_and_two_templates_with_similar_names_do_not_collide()
    {
        var store = Store();
        store.Save(Info("A/B", "x"), Doc(), null, null);
        store.Save(Info("A?B", "y"), Doc(), null, null);

        Assert.Equal(2, Directory.GetFiles(_folder, "*.fwt").Length);
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void A_template_can_be_loaded_again_from_its_path()
    {
        var store = Store();
        var path = store.Save(Info("Hinge"), Doc(), null, null);

        var doc = TemplateFile.Load(path);

        Assert.Equal("Box", doc.Root.Children.Single().Name);
    }

    [Fact]
    public void The_default_folder_is_in_the_documents_of_the_user()
    {
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Formwerk", "Templates"),
            TemplateStore.DefaultFolder);
    }
}
