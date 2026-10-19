using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>
/// Open groups (F-BOO-04, F-BOO-02 in the 3D view): a double click on a part of a group, boolean or merge opens that container. The
/// rest of the scene is dimmed, and inside the direct parts of the container are selected one by one. Esc leaves.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The group, boolean or merge that is open, or null.</summary>
    [ObservableProperty]
    public partial Guid? OpenContainerId { get; set; }

    public bool IsGroupOpen => OpenContainerId is not null;

    private Node? OpenContainer => OpenContainerId is { } id ? _document.FindNode(id) : null;

    partial void OnOpenContainerIdChanged(Guid? value) => OnPropertyChanged(nameof(IsGroupOpen));

    /// <summary>
    /// A double click in the view: <paramref name="id"/> is the item under the mouse. A part of a container opens that container (the
    /// part is selected); empty space leaves the open group; a single top-level object has nothing to open.
    /// </summary>
    [RelayCommand]
    private void OpenGroup(Guid? id)
    {
        if (id is not { } nodeId)
        {
            if (IsGroupOpen) SetOpenContainer(null);
            return;
        }

        var node = _document.FindNode(nodeId);
        if (node is null) return;
        var container = node.CanHaveChildren ? node : node.Parent;
        if (container is null || container == _document.Root || container == OpenContainer) return;

        SetOpenContainer(container);
        var part = node;
        while (part.Parent is { } parent && parent != container) part = parent;
        SelectOnly(part == container ? [] : [part.Id]);
    }

    /// <summary>Esc: one level up (a nested group opens its parent group), after the last one back to the whole scene. The container is selected.</summary>
    [RelayCommand]
    private void ExitGroup()
    {
        if (OpenContainer is not { } container)
        {
            if (IsGroupOpen) SetOpenContainer(null);
            return;
        }

        SetOpenContainer(container.Parent is { } parent && parent != _document.Root ? parent : null);
        SelectOnly([container.Id]);
    }

    private void SetOpenContainer(Node? container)
    {
        OpenContainerId = container?.Id;
        if (_lastResult is not null) BuildViewportItems(_lastResult);
        RefreshScaleHint();
    }

    /// <summary>Show the hatched ghosts of holes (F-VIS-04). Off: only the solids are drawn.</summary>
    [ObservableProperty]
    public partial bool ShowHoles { get; set; } = true;

    partial void OnShowHolesChanged(bool value)
    {
        if (_lastResult is not null) BuildViewportItems(_lastResult);
    }

    private void BuildViewportItems(EvaluationResult result)
    {
        HashSet<Guid>? inside = null;
        if (OpenContainer is { } open) inside = [open.Id, .. open.Descendants().Select(n => n.Id)];

        ViewportItems = [.. result.Parts.Where(p => ShowHoles || p.Kind == PartKind.Solid).Select(p => new ViewportItem(
            p.Source.Id, p.Mesh, p.Kind == PartKind.Solid ? ViewportItemKind.Solid : ViewportItemKind.Ghost, p.Source.Color,
            Outside: p.Kind == PartKind.Solid && _outside.Contains(p.Source.Id),
            Dimmed: inside is not null && !inside.Contains(p.Source.Id)))];
    }
}
