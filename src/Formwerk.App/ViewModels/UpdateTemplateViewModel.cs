using CommunityToolkit.Mvvm.ComponentModel;
using Formwerk.App.Resources;

namespace Formwerk.App.ViewModels;

/// <summary>
/// The "Update Template" dialog (F-TPL-05): which existing template the selected objects become a new version of. The template whose
/// name the selected object carries is preselected; the dialog only confirms with a template chosen.
/// </summary>
public sealed partial class UpdateTemplateViewModel : ObservableObject
{
    public UpdateTemplateViewModel(IReadOnlyList<TemplateItemViewModel> templates, TemplateItemViewModel? preselected)
    {
        Templates = templates;
        Selected = preselected;
    }

    public IReadOnlyList<TemplateItemViewModel> Templates { get; }

    /// <summary>The template that gets the new version; null while nothing is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(Summary))]
    public partial TemplateItemViewModel? Selected { get; set; }

    public bool CanConfirm => Selected is not null;

    /// <summary>What confirming does, in one sentence (the version number is raised, name and description stay).</summary>
    public string Summary => Selected is { } item
        ? Texts.Format("Dlg.UpdateTemplateSummary", item.Name, item.Info.Version, item.Info.Version + 1)
        : Texts.Get("Dlg.UpdateTemplateChoose");
}
