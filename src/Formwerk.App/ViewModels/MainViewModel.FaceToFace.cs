using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Face to face (F-SNP-03): the face of one object is put flush onto the face of another, with an optional gap or overlap.</summary>
public sealed partial class MainViewModel
{
    private FacePick? _faceToFaceFirst;
    private FieldViewModel? _faceGap;
    private string _faceGapText = "0";

    /// <summary>Waiting for the two clicks of "face to face": first the face of the object that moves, then the face of the other one.</summary>
    [ObservableProperty]
    public partial bool IsPickingFaceToFace { get; set; }

    /// <summary>The first click was made: the face of the object that moves is known and the face of the other one is asked for.</summary>
    public bool FaceToFaceHasFirst => _faceToFaceFirst is not null;

    /// <summary>
    /// The space between the two faces in mm: 0 is flush, positive leaves a gap (0.2 for a print fit), negative overlaps. A number or
    /// a formula (<c>Tolerance</c>, a project variable); it is kept for the next use.
    /// </summary>
    public FieldViewModel FaceGap => _faceGap ??= new FieldViewModel(Texts.Get("FaceToFace.Gap"), "mm", text =>
    {
        var result = NumericInput.Resolve(text, 0, _document.ResolveVariables(out _));
        if (!result.IsSuccess) return result.Error;
        _faceGapText = text.Trim();
        return null;
    });

    partial void OnIsPickingFaceToFaceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        ViewportDimensions = ComputeDimensions();
    }

    private bool CanFaceToFace() => _document.Root.Children.Count > 0;

    /// <summary>Starts the mode (or leaves it when it is on): click a face of the object that should move, then the face it should lie against.</summary>
    [RelayCommand(CanExecute = nameof(CanFaceToFace))]
    private void FaceToFace()
    {
        if (IsPickingFaceToFace)
        {
            EndFaceToFace();
            return;
        }

        IsPickingFace = false;
        IsPlacingHole = false;
        IsPlacingEdgeCut = false;
        IsPickingSectionFace = false;
        IsPickingMirrorFace = false;
        _faceToFaceFirst = null;
        FaceGap.SetFromModel(_faceGapText);
        IsPickingFaceToFace = true;
        OnPropertyChanged(nameof(FaceToFaceHasFirst));
        RefreshScaleHint();
    }

    private void EndFaceToFace()
    {
        EndFaceToFaceMode();
        RefreshScaleHint();
    }

    /// <summary>Leaves the mode without touching the hint (another face mode takes over, or the caller sets the hint).</summary>
    private void EndFaceToFaceMode()
    {
        if (!IsPickingFaceToFace && _faceToFaceFirst is null) return;
        _faceToFaceFirst = null;
        IsPickingFaceToFace = false;
        OnPropertyChanged(nameof(FaceToFaceHasFirst));
    }

    /// <summary>
    /// A click in the mode. The first one chooses the face of the object that moves (its outermost container moves, like with "face on
    /// floor"); the second one the face it is put against: the object is turned about the clicked point until the faces look against
    /// each other, and moved so that the clicked point is on the other face, in front of it by the gap. One undo step; the object that
    /// moved is selected. A click on something that is not in the scene is ignored, and so is a second click on the same object (the
    /// status bar says so); a locked object does not move.
    /// </summary>
    private void FaceToFaceClick(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is not { } clicked || pick.Normal.LengthSquared() < 1e-12f) return;

        if (_faceToFaceFirst is null)
        {
            _faceToFaceFirst = pick;
            OnPropertyChanged(nameof(FaceToFaceHasFirst));
            RefreshScaleHint();
            return;
        }

        var mover = OutermostContainer(_document.FindNode(_faceToFaceFirst.NodeId) ?? clicked);
        var target = OutermostContainer(clicked);
        if (mover == target)
        {
            SetNotice(Texts.Get("Msg.FaceToFaceSame"));
            return;
        }

        // a gap that does not work is shown red in its field: nothing is moved until it is corrected
        var gap = FaceGap.HasError ? EvalResult.Fail(FaceGap.Error!) : NumericInput.Resolve(_faceGapText, 0, _document.ResolveVariables(out _));
        if (!gap.IsSuccess)
        {
            FaceGap.Error = gap.Error;
            SetNotice(Texts.Format("Msg.FaceToFaceGap", gap.Error ?? ""));
            return;
        }

        var first = _faceToFaceFirst;
        EndFaceToFace();
        if (mover.Locked)
        {
            SetNotice(Texts.Format("Msg.FaceLocked", mover.Name));
            return;
        }

        var next = TransformOps.FaceToFace(
            mover, mover.Transform, FaceCenterSnapped(first), first.Normal, FaceCenterSnapped(pick), pick.Normal, (float)gap.Value);
        if (next == mover.Transform) return;
        _history.Execute(new SetTransformCommand(_document, mover, next, Texts.Format("Name.FaceToFace", mover.Name)));
        SelectOnly([mover.Id]);
    }

    private Node OutermostContainer(Node node)
    {
        while (node.Parent is { } parent && parent != _document.Root) node = parent;
        return node;
    }
}
