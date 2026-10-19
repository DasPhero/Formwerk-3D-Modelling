using System.IO;
using System.Numerics;
using Formwerk.App.ViewModels;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.IO;

namespace Formwerk.Tests.App;

/// <summary>The "Save as Template" dialog (F-TPL-01): name required, taken names refused with a suggestion.</summary>
public sealed class SaveTemplateViewModelTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"formwerk-tpl-dialog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private TemplateStore Store() => new(_folder);

    private static void Save(TemplateStore store, string name, string category = "")
    {
        var doc = new FormwerkDocument();
        new AddNodeCommand(doc, doc.Root, new PrimitiveNode("box", "Box")).Do();
        store.Save(new TemplateInfo(name, "", category, [], new Vector3(20, 20, 20), DateTime.UtcNow), doc, null, null);
    }

    [UiFact]
    public void The_suggested_name_is_filled_in_and_valid()
    {
        var vm = new SaveTemplateViewModel(Store(), "Hinge");

        Assert.Equal("Hinge", vm.Name);
        Assert.True(vm.IsValid);
        Assert.Null(vm.Error);
    }

    [UiFact]
    public void Without_a_name_the_dialog_cannot_be_confirmed()
    {
        var vm = new SaveTemplateViewModel(Store(), "Hinge") { Name = "   " };

        Assert.False(vm.IsValid);
        Assert.Contains("name", vm.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<InvalidOperationException>(() => vm.Build(new Vector3(1, 1, 1)));
    }

    [UiFact]
    public void A_taken_name_is_refused_and_the_next_free_one_is_suggested()
    {
        var store = Store();
        Save(store, "Hinge", "Mechanics");
        var vm = new SaveTemplateViewModel(store, "x") { Category = "Mechanics" };

        vm.Name = "hinge";

        Assert.False(vm.IsValid);
        Assert.Contains("already exists", vm.Error);
        Assert.Equal("hinge (2)", vm.Suggestion);
    }

    [UiFact]
    public void Taking_the_suggestion_makes_the_dialog_valid()
    {
        var store = Store();
        Save(store, "Hinge", "Mechanics");
        var vm = new SaveTemplateViewModel(store, "Hinge") { Category = "Mechanics" };
        Assert.False(vm.IsValid);

        vm.UseSuggestionCommand.Execute(null);

        Assert.Equal("Hinge (2)", vm.Name);
        Assert.True(vm.IsValid);
        Assert.Null(vm.Suggestion);
    }

    [UiFact]
    public void The_same_name_is_fine_in_another_category_and_the_check_follows_the_category()
    {
        var store = Store();
        Save(store, "Hinge", "Mechanics");
        var vm = new SaveTemplateViewModel(store, "Hinge");
        Assert.True(vm.IsValid); // no category: nothing there with this name

        vm.Category = "Mechanics";
        Assert.False(vm.IsValid);

        vm.Category = "Boxes";
        Assert.True(vm.IsValid);
    }

    [UiFact]
    public void The_categories_in_use_are_offered()
    {
        var store = Store();
        Save(store, "a", "Mechanics");
        Save(store, "b", "Boxes");

        Assert.Equal(["Boxes", "Mechanics"], new SaveTemplateViewModel(store, "x").Categories.Order());
    }

    [UiFact]
    public void Tags_are_split_cleaned_and_made_unique()
    {
        var vm = new SaveTemplateViewModel(Store(), "x") { TagsText = " plate, hinge ;Plate,, small " };

        Assert.True(vm.IsValid);
        Assert.Equal(["plate", "hinge", "small"], vm.Build(Vector3.One).Tags);
    }

    [UiFact]
    public void Too_many_or_too_long_tags_and_texts_are_refused()
    {
        var vm = new SaveTemplateViewModel(Store(), "x");

        vm.TagsText = string.Join(",", Enumerable.Range(0, TemplateLimits.MaxTags + 1).Select(i => "t" + i));
        Assert.False(vm.IsValid);

        vm.TagsText = new string('t', TemplateLimits.MaxTagLength + 1);
        Assert.False(vm.IsValid);

        vm.TagsText = "";
        vm.Description = new string('d', TemplateLimits.MaxDescriptionLength + 1);
        Assert.False(vm.IsValid);

        vm.Description = "";
        vm.Category = new string('c', TemplateLimits.MaxCategoryLength + 1);
        Assert.False(vm.IsValid);

        vm.Category = "ok";
        Assert.True(vm.IsValid);
    }

    [UiFact]
    public void The_result_carries_the_cleaned_texts_and_the_size()
    {
        var vm = new SaveTemplateViewModel(Store(), "  Hinge   plate ") { Description = "A plate", Category = " Mechanics ", TagsText = "a" };

        var info = vm.Build(new Vector3(40, 20, 5));

        Assert.Equal("Hinge plate", info.Name);
        Assert.Equal("A plate", info.Description);
        Assert.Equal("Mechanics", info.Category);
        Assert.Equal(new Vector3(40, 20, 5), info.Size);
        Assert.True((DateTime.UtcNow - info.CreatedUtc).TotalMinutes < 1);
    }
}
