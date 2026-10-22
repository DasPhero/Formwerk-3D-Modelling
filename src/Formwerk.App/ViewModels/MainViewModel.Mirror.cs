using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Mirroring (F-PAT-04): a linked mirror, an independent mirrored copy, and both at a face.</summary>
public sealed partial class MainViewModel
{
    private bool _mirrorFaceCopy;

    /// <summary>Waiting for a click on the face that the mirror plane should lie on (one shot; Esc cancels).</summary>
    [ObservableProperty]
    public partial bool IsPickingMirrorFace { get; set; }

    partial void OnIsPickingMirrorFaceChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        ViewportDimensions = ComputeDimensions();
    }

    /// <summary>
    /// A mirror that stays linked: the selection stays and its mirror image is added next to it; changing the original changes the
    /// image. The plane is perpendicular to X through the middle of the biggest body next to the selection (a plate with a hole: the
    /// plate is symmetric), or through the origin. One undo step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void LinkedMirror() => MakeMirror(copy: false, face: null);

    /// <summary>
    /// An independent mirrored copy: a copy of the selection is mirrored and put next to the originals, which stay untouched. The copy
    /// has nothing to do with the original afterwards. One undo step.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void MirroredCopy() => MakeMirror(copy: true, face: null);

    /// <summary>Like <see cref="LinkedMirror"/>, but the plane is the face that the user clicks next.</summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void LinkedMirrorAtFace() => StartMirrorFace(copy: false);

    /// <summary>Like <see cref="MirroredCopy"/>, but the plane is the face that the user clicks next.</summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void MirroredCopyAtFace() => StartMirrorFace(copy: true);

    private void StartMirrorFace(bool copy)
    {
        IsPickingFace = false;
        IsPlacingHole = false;
        IsPlacingEdgeCut = false;
        IsPickingSectionFace = false;
        IsMeasuring = false;
        EndFaceToFaceMode();
        _mirrorFaceCopy = copy;
        IsPickingMirrorFace = true;
        RefreshScaleHint();
    }

    /// <summary>The click of the face mode: mirrors the selection at the clicked face. A pick of something that is not in the scene is ignored.</summary>
    private void MirrorAtFace(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is null || pick.Normal.LengthSquared() < 1e-12f) return;

        IsPickingMirrorFace = false;
        RefreshScaleHint();
        if (SelectedTopLevel() is not { Count: > 0 } nodes || !SameParent(nodes))
        {
            SetNotice(Texts.Get("Msg.MirrorNothing"));
            return;
        }

        MakeMirror(_mirrorFaceCopy, (pick.Point, Vector3.Normalize(pick.Normal)));
    }

    private void MakeMirror(bool copy, (Vector3 Point, Vector3 Normal)? face)
    {
        var nodes = SelectedTopLevel();
        if (nodes.Count == 0 || !SameParent(nodes)) return;
        var first = nodes[0];

        // where the plane is, in the space of the parent: through a point, its normal along the local X axis of the mirror node
        Matrix4x4 plane;
        if (face is { } at)
        {
            var world = Matrix4x4.CreateFromQuaternion(AlignDirection(Vector3.UnitX, at.Normal)) * Matrix4x4.CreateTranslation(at.Point);
            if (!Matrix4x4.Invert(TransformOps.ParentToWorld(first), out var toParent)) return;
            plane = world * toParent;
        }
        else
        {
            plane = Matrix4x4.CreateTranslation(BodyCenterNear(nodes));
        }

        var mirror = new MirrorNode(UniqueName(Texts.Get("Name.Mirror"))) { Transform = ToTransform(plane) };
        mirror.Parameters["axis"] = "0";
        mirror.Parameters["copyOnly"] = copy ? "1" : "0";

        // the objects keep their place in the scene: their transforms become relative to the mirror node
        if (!Matrix4x4.Invert(mirror.Transform.ToMatrix(), out var inverse)) return;
        var commands = new List<IDocumentCommand>();
        if (copy)
        {
            commands.Add(new AddNodeCommand(_document, first.Parent!, mirror));
            foreach (var node in nodes)
            {
                var clone = node.Clone(newIds: true);
                clone.Name = UniqueName(node.Name);
                clone.Transform = ToTransform(node.Transform.ToMatrix() * inverse);
                commands.Add(new AddNodeCommand(_document, mirror, clone));
            }
        }
        else
        {
            commands.Add(new WrapNodesCommand(_document, mirror, nodes));
            commands.Add(new SetTransformsCommand(
                _document, nodes.Select(n => (n, n.Transform, ToTransform(n.Transform.ToMatrix() * inverse))), "Move into the mirror"));
        }

        var label = nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} objects";
        _history.Execute(new CompositeCommand(Texts.Format(copy ? "Name.MirroredCopy" : "Name.LinkedMirror", label), commands));
        SelectOnly([mirror.Id]);
    }

    private static Transform ToTransform(Matrix4x4 matrix) =>
        Matrix4x4.Decompose(matrix, out var scale, out var rotation, out var translation)
            ? new Transform(translation, Quaternion.Normalize(rotation), scale)
            : Transform.FromPosition(matrix.Translation);

    /// <summary>The turn that takes the direction <paramref name="from"/> to the direction <paramref name="to"/> (both unit vectors).</summary>
    private static Quaternion AlignDirection(Vector3 from, Vector3 to)
    {
        var cosine = Math.Clamp(Vector3.Dot(from, to), -1f, 1f);
        if (cosine > 0.999999f) return Quaternion.Identity;
        if (cosine < -0.999999f)
        {
            var across = Math.Abs(from.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitY; // any direction that is not along 'from'
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, across)), MathF.PI);
        }

        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, to)), MathF.Acos(cosine));
    }
}
