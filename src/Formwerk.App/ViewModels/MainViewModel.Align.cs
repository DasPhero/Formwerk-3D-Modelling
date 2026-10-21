using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;

namespace Formwerk.App.ViewModels;

/// <summary>Align and distribute several objects (F-SNP-04).</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// What the objects are aligned to: the first selected object (it does not move) when this is on, the box around all selected
    /// objects when it is off.
    /// </summary>
    [ObservableProperty]
    public partial bool AlignToFirst { get; set; }

    /// <summary>The align panel is shown while two or more objects are selected.</summary>
    public bool IsAlignVisible => SelectedTopLevel().Count >= 2;

    private bool CanAlign() => MeasuredSelection().Count >= 2;

    private bool CanDistribute() => MeasuredSelection().Count >= 3;

    /// <summary>
    /// Lines up the selected objects along one axis. The parameter is the axis and the edge, for example <c>X-Min</c>, <c>Y-Center</c>,
    /// <c>Z-Max</c>: the low edge, the middle or the high edge of the box of each object, to the first selected object or to the
    /// selection box (<see cref="AlignToFirst"/>). Groups, booleans, merges and patterns count as one object and move as a whole.
    /// One undo step; locked objects stay (the status bar says so).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAlign))]
    private void Align(string? what)
    {
        var parts = (what ?? "").Split('-');
        if (parts.Length != 2 || !TryAxis(parts[0], out var axis) || !Enum.TryParse<AlignMode>(parts[1], ignoreCase: true, out var mode)
            || !Enum.IsDefined(mode)) return;

        var objects = MeasuredSelection();
        var shifts = AlignMath.Align([.. objects.Select(o => Extent(o.Box, axis))], mode, AlignToFirst ? 0 : -1);
        Move(objects, axis, shifts, Texts.Format("Name.Align", what!.Replace('-', ' ')));
    }

    /// <summary>
    /// Spreads three or more selected objects along an axis (the parameter <c>X</c>, <c>Y</c> or <c>Z</c>) so that the gaps between
    /// them are equal; the two outermost objects stay where they are. One undo step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDistribute))]
    private void Distribute(string? axisName)
    {
        if (axisName is null || !TryAxis(axisName, out var axis)) return;
        var objects = MeasuredSelection();
        var shifts = AlignMath.Distribute([.. objects.Select(o => Extent(o.Box, axis))]);
        Move(objects, axis, shifts, Texts.Format("Name.Distribute", axisName.ToUpperInvariant()));
    }

    /// <summary>The selected top-level objects, in the order they were selected, with the box of their evaluated geometry.</summary>
    private List<(Node Node, (Vector3 Min, Vector3 Max) Box)> MeasuredSelection()
    {
        var result = new List<(Node, (Vector3, Vector3))>();
        if (_lastResult is null) return result;
        foreach (var node in SelectedTopLevel())
        {
            var ids = new HashSet<Guid>(node.Descendants().Select(d => d.Id)) { node.Id };
            var box = BoundsOf(_lastResult, ids, includeGhosts: false) ?? BoundsOf(_lastResult, ids, includeGhosts: true);
            if (box is { } measured) result.Add((node, measured));
        }

        return result;
    }

    private static (float Min, float Max) Extent((Vector3 Min, Vector3 Max) box, int axis) => axis switch
    {
        0 => (box.Min.X, box.Max.X),
        1 => (box.Min.Y, box.Max.Y),
        _ => (box.Min.Z, box.Max.Z),
    };

    private static bool TryAxis(string name, out int axis)
    {
        axis = name.ToUpperInvariant() switch { "X" => 0, "Y" => 1, "Z" => 2, _ => -1 };
        return axis >= 0;
    }

    private void Move(List<(Node Node, (Vector3 Min, Vector3 Max) Box)> objects, int axis, float[] shifts, string name)
    {
        var direction = axis switch { 0 => Vector3.UnitX, 1 => Vector3.UnitY, _ => Vector3.UnitZ };
        var changes = new List<(Node, Transform, Transform)>();
        var locked = new List<string>();
        for (var i = 0; i < objects.Count; i++)
        {
            if (Math.Abs(shifts[i]) < 1e-5f) continue;
            var node = objects[i].Node;
            if (node.Locked)
            {
                locked.Add(node.Name);
                continue;
            }

            changes.Add((node, node.Transform, TransformOps.MovedBy(node, node.Transform, direction * shifts[i])));
        }

        if (changes.Count > 0) _history.Execute(new SetTransformsCommand(_document, changes, name));
        if (locked.Count > 0) SetNotice(Texts.Format("Msg.AlignLocked", string.Join(", ", locked)));
    }
}
