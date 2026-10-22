using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Quick orientation actions (F-ROT-04): 90 degree turns and "face on floor".</summary>
public sealed partial class MainViewModel
{
    /// <summary>"Face on floor" is waiting for a click on a face (one-shot mode).</summary>
    [ObservableProperty]
    public partial bool IsPickingFace { get; set; }

    // ---- 90 degrees ------------------------------------------------------------------------------

    /// <summary>
    /// Turns the selected objects by 90 degrees about a world axis through the center of the selection. <paramref name="direction"/>
    /// is "X", "Y" or "Z", with a leading "-" for the other way. One undo step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Rotate90(string? direction)
    {
        var (axis, degrees) = direction switch
        {
            "X" => (Vector3.UnitX, 90.0),
            "-X" => (Vector3.UnitX, -90.0),
            "Y" => (Vector3.UnitY, 90.0),
            "-Y" => (Vector3.UnitY, -90.0),
            "Z" => (Vector3.UnitZ, 90.0),
            "-Z" => (Vector3.UnitZ, -90.0),
            _ => (Vector3.Zero, 0.0),
        };
        if (axis == Vector3.Zero) return;

        var nodes = SelectedTopLevel().Where(n => !n.Locked).ToList();
        if (nodes.Count == 0) return;

        // the same center as the handles use; before the first evaluation the average of the origins will do
        var pivot = ComputePivot() ?? nodes.Aggregate(Vector3.Zero, (sum, n) => sum + TransformOps.WorldPosition(n)) / nodes.Count;
        var changes = nodes.Select(n => (n, n.Transform, TransformOps.RotatedAbout(n, n.Transform, pivot, axis, degrees))).ToList();
        var label = nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} objects";
        _history.Execute(new SetTransformsCommand(_document, changes, $"Rotate {label} {degrees:0}° about {direction!.TrimStart('-')}"));
    }

    // ---- face on floor ---------------------------------------------------------------------------

    private bool CanFaceOnFloor() => _document.Root.Children.Count > 0;

    /// <summary>Starts or cancels the mode in which the next click on a face lays that face flat on the build plate.</summary>
    [RelayCommand(CanExecute = nameof(CanFaceOnFloor))]
    private void FaceOnFloor()
    {
        IsPickingFace = !IsPickingFace;
        if (IsPickingFace)
        {
            IsPlacingHole = false;
            IsPlacingEdgeCut = false;
            IsPickingSectionFace = false;
            IsMeasuring = false;
            IsPickingPivot = false;
            IsPickingMirrorFace = false;
            EndFaceToFaceMode();
        }

        RefreshScaleHint(); // sets the hint for the mode, or the usual one again
    }

    /// <summary>What the viewport reports in the mode: the clicked face, or null when the user pressed Esc.</summary>
    [RelayCommand]
    private void FacePicked(FacePick? pick)
    {
        if (pick is null)
        {
            EndFacePick();
            IsPlacingHole = false; // Esc ends the hole tool, the edge tool, the mirror face mode and face to face as well
            IsPlacingEdgeCut = false;
            IsPickingSectionFace = false;
            IsMeasuring = false;
            IsPickingPivot = false;
            EndFaceToFaceMode();
            IsPickingMirrorFace = false;
            RefreshScaleHint();
            return;
        }

        if (IsPlacingHole) PlaceHole(pick);
        else if (IsPlacingEdgeCut) PlaceEdgeCut(pick);
        else if (IsPickingSectionFace) SectionFacePicked(pick);
        else if (IsPickingPivot) PivotPicked(pick);
        else if (IsMeasuring) MeasureClick(pick);
        else if (IsPickingMirrorFace) MirrorAtFace(pick);
        else if (IsPickingFaceToFace) FaceToFaceClick(pick);
        else LayFaceOnFloor(pick);
    }

    private void EndFacePick()
    {
        if (!IsPickingFace) return;
        IsPickingFace = false;
        RefreshScaleHint();
    }

    /// <summary>
    /// Turns the object that owns the face so that the face looks down and lies exactly on Z = 0 (F-ROT-04); the clicked point stays
    /// where it is on the plate. Operands of a boolean and parts of a group cannot move alone, so the outermost object in the scene
    /// turns. A pick of something that is not in the scene is ignored (the mode stays on).
    /// </summary>
    public void LayFaceOnFloor(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is not { } node) return;
        while (node.Parent is { } parent && parent != _document.Root) node = parent;

        EndFacePick();
        if (node.Locked)
        {
            SetNotice(Texts.Format("Msg.FaceLocked", node.Name));
            return;
        }

        var next = TransformOps.LaidOnFloor(node, node.Transform, pick.Point, pick.Normal);
        if (next == node.Transform)
        {
            SetNotice(Texts.Get("Msg.FaceAlready"));
            return;
        }

        _history.Execute(new SetTransformCommand(_document, node, next, $"Lay {node.Name} on a face"));
        SelectOnly([node.Id]);
    }
}
