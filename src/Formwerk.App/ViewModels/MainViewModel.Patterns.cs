using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.App.ViewModels;

/// <summary>Patterns (F-PAT-01..03): the selection becomes a feature that repeats it and stays editable.</summary>
public sealed partial class MainViewModel
{
    private bool CanPattern() => HasSelection() && SameParent(SelectedTopLevel());

    /// <summary>
    /// Makes the selected objects (which must share a parent) the source of a new linear pattern: a row of three along X, with a
    /// spacing a little wider than the objects. The pattern takes the place of the objects in the tree, so a hole in a group stays
    /// in that group and keeps cutting. One undo step; the pattern is selected, and its count, spacing, direction and skipped
    /// instances are edited in the properties.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void LinearPattern()
    {
        var nodes = SelectedTopLevel();
        if (!SameParent(nodes)) return;

        var pattern = new PatternNode(UniqueName(Texts.Get("Name.Pattern")), PatternKind.Linear);
        pattern.Parameters["count"] = "3";
        pattern.Parameters["spacing"] = DefaultSpacing(nodes).ToString("0.###", CultureInfo.InvariantCulture);

        _history.Execute(new WrapNodesCommand(_document, pattern, nodes));
        SelectOnly([pattern.Id]);
    }

    /// <summary>The width of the selection along X plus 5 mm, but at least 10 mm, so that the first instances do not touch.</summary>
    private double DefaultSpacing(IEnumerable<Node> nodes)
    {
        if (_lastResult is null) return 15;
        var ids = new HashSet<Guid>();
        foreach (var node in nodes) ids.UnionWith(node.Descendants().Select(d => d.Id).Prepend(node.Id));
        if (BoundsOf(_lastResult, ids, includeGhosts: true) is not { } box) return 15;
        return Math.Max(10, Math.Ceiling(box.Max.X - box.Min.X + 5));
    }
}
