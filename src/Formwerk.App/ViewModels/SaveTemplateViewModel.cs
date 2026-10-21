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

    public SaveTemplateViewModel(TemplateStore store, string suggestedName, IReadOnlyList<SharedParameterChoice>? sharedChoices = null)
    {
        _store = store;
        Categories = store.Categories();
        SharedChoices = sharedChoices ?? [];
        foreach (var choice in SharedChoices) choice.PropertyChanged += (_, _) => Validate();
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
        SharedChoices = []; // what is asked on insert is chosen when saving (the file keeps it)
        Name = existing.Name;
        Description = existing.Description;
        Category = existing.Category;
        TagsText = string.Join(", ", existing.Tags);
        Validate();
    }

    /// <summary>The window title.</summary>
    public string Title { get; } = Texts.Get("Dlg.SaveTemplateTitle");

    public IReadOnlyList<string> Categories { get; }

    /// <summary>The numeric parameters of the saved objects that can be asked when the template is inserted (F-TPL-06); empty when editing.</summary>
    public IReadOnlyList<SharedParameterChoice> SharedChoices { get; }

    public bool HasSharedChoices => SharedChoices.Count > 0;

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

    private List<SharedParameter>? SharedParameters() =>
        SharedChoices.Count == 0
            ? null
            : [.. SharedChoices.Where(c => c.IsShared).Select(c => new SharedParameter(c.Path, c.Parameter, TemplateNames.Clean(c.Label)))];

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
            : SharedProblem();

        if (Error is null && (_exceptPath is null ? _store.NameTaken(name, category) : _store.NameTaken(name, category, _exceptPath)))
        {
            Suggestion = _store.SuggestName(name, category);
            Error = Texts.Format("Tpl.Err.Taken", name);
        }

        IsValid = Error is null;
    }

    /// <summary>Why the ticked parameters cannot be saved (no label, a label twice, too long, too many); null when they are fine.</summary>
    private string? SharedProblem()
    {
        var ticked = SharedChoices.Where(c => c.IsShared).ToList();
        if (ticked.Count > TemplateLimits.MaxShared) return Texts.Format("Tpl.Err.SharedCount", TemplateLimits.MaxShared);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var choice in ticked)
        {
            var label = TemplateNames.Clean(choice.Label);
            if (label.Length == 0) return Texts.Format("Tpl.Err.SharedLabel", choice.Display);
            if (label.Length > TemplateLimits.MaxSharedLabelLength || label.Any(char.IsControl)) return Texts.Format("Tpl.Err.SharedLong", TemplateLimits.MaxSharedLabelLength);
            if (!seen.Add(label)) return Texts.Format("Tpl.Err.SharedDuplicate", label);
        }

        return null;
    }

    /// <summary>The description to save; <paramref name="size"/> is the size of what is saved.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not valid.</exception>
    public TemplateInfo Build(Vector3 size)
    {
        if (!IsValid) throw new InvalidOperationException("The template description is not valid.");
        return new TemplateInfo(TemplateNames.Clean(Name), (Description ?? "").Trim(), TemplateNames.Clean(Category), ParseTags() ?? [], size, DateTime.UtcNow, 1, SharedParameters());
    }
}
