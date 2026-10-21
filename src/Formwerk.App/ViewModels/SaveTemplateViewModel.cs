using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The "Save as Template" dialog (F-TPL-01): name (required), description, category and tags (optional). A name that is already used in
/// the category is refused and the next free one ("Name (2)") is offered. The dialog window only shows this and confirms when it is valid.
/// </summary>
public sealed partial class SaveTemplateViewModel : ObservableObject
{
    private readonly TemplateStore _store;
    private readonly string? _exceptPath;

    public SaveTemplateViewModel(TemplateStore store, string suggestedName)
    {
        _store = store;
        Categories = store.Categories();
        Name = suggestedName;
        Validate();
    }

    /// <summary>Edit mode (F-TPL-04): the dialog starts with what the template has now, and its own name is no conflict.</summary>
    public SaveTemplateViewModel(TemplateStore store, TemplateInfo existing, string path)
    {
        _store = store;
        _exceptPath = path;
        Title = Texts.Get("Dlg.TemplatePropertiesTitle");
        Categories = store.Categories();
        Name = existing.Name;
        Description = existing.Description;
        Category = existing.Category;
        TagsText = string.Join(", ", existing.Tags);
        Validate();
    }

    /// <summary>The window title.</summary>
    public string Title { get; } = Texts.Get("Dlg.SaveTemplateTitle");

    public IReadOnlyList<string> Categories { get; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Description { get; set; } = "";

    [ObservableProperty]
    public partial string Category { get; set; } = "";

    /// <summary>Tags separated by commas or semicolons.</summary>
    [ObservableProperty]
    public partial string TagsText { get; set; } = "";

    /// <summary>Why the dialog cannot be confirmed; null when it can.</summary>
    [ObservableProperty]
    public partial string? Error { get; set; }

    /// <summary>The next free name when the name is taken in the category.</summary>
    [ObservableProperty]
    public partial string? Suggestion { get; set; }

    [ObservableProperty]
    public partial bool IsValid { get; set; }

    partial void OnNameChanged(string value) => Validate();

    partial void OnDescriptionChanged(string value) => Validate();

    partial void OnCategoryChanged(string value) => Validate();

    partial void OnTagsTextChanged(string value) => Validate();

    [RelayCommand]
    private void UseSuggestion()
    {
        if (Suggestion is { } suggestion) Name = suggestion;
    }

    private List<string>? ParseTags()
    {
        var tags = (TagsText ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Select(TemplateNames.Clean).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return tags.Count > TemplateLimits.MaxTags || tags.Any(t => t.Length > TemplateLimits.MaxTagLength || t.Any(char.IsControl)) ? null : tags;
    }

    private void Validate()
    {
        Suggestion = null;
        var name = TemplateNames.Clean(Name);
        var category = TemplateNames.Clean(Category);
        Error =
            name.Length == 0 ? Texts.Get("Tpl.Err.NameRequired")
            : name.Length > TemplateLimits.MaxNameLength ? Texts.Format("Tpl.Err.NameLong", TemplateLimits.MaxNameLength)
            : TemplateNames.Validate(name) is not null ? Texts.Get("Tpl.Err.NameChars")
            : category.Length > TemplateLimits.MaxCategoryLength || category.Any(char.IsControl) ? Texts.Format("Tpl.Err.Category", TemplateLimits.MaxCategoryLength)
            : (Description ?? "").Length > TemplateLimits.MaxDescriptionLength ? Texts.Format("Tpl.Err.Description", TemplateLimits.MaxDescriptionLength)
            : ParseTags() is null ? Texts.Format("Tpl.Err.Tags", TemplateLimits.MaxTags, TemplateLimits.MaxTagLength)
            : null;

        if (Error is null && (_exceptPath is null ? _store.NameTaken(name, category) : _store.NameTaken(name, category, _exceptPath)))
        {
            Suggestion = _store.SuggestName(name, category);
            Error = Texts.Format("Tpl.Err.Taken", name);
        }

        IsValid = Error is null;
    }

    /// <summary>The description to save; <paramref name="size"/> is the size of what is saved.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not valid.</exception>
    public TemplateInfo Build(Vector3 size)
    {
        if (!IsValid) throw new InvalidOperationException("The template description is not valid.");
        return new TemplateInfo(TemplateNames.Clean(Name), (Description ?? "").Trim(), TemplateNames.Clean(Category), ParseTags() ?? [], size, DateTime.UtcNow);
    }
}
