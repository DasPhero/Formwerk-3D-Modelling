using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Hole tool (F-HOL-01, F-HOL-02, F-HOL-05): click a face, get a hole perpendicular to it that cuts the body.</summary>
public sealed partial class MainViewModel
{
    private HoleToolViewModel? _holeTool;

    /// <summary>The "drill a hole" tool waits for a click on a face (it stays on until Esc or the button, so several holes can follow).</summary>
    [ObservableProperty]
    public partial bool IsPlacingHole { get; set; }

    /// <summary>The viewport reports clicks as faces instead of selecting (hole tool or "face on floor").</summary>
    public bool IsFacePickActive => IsPickingFace || IsPlacingHole || IsPickingMirrorFace || IsPickingFaceToFace || IsPlacingEdgeCut || IsPickingSectionFace;

    /// <summary>The settings of the next hole.</summary>
    public HoleToolViewModel HoleTool => _holeTool ??= new HoleToolViewModel(() => _document.ResolveVariables(out _));

    partial void OnIsPlacingHoleChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        ViewportDimensions = ComputeDimensions();
    }

    private bool CanDrill() => _document.Root.Children.Count > 0;

    /// <summary>Starts or ends the hole tool.</summary>
    [RelayCommand(CanExecute = nameof(CanDrill))]
    private void Drill()
    {
        IsPlacingHole = !IsPlacingHole;
        if (IsPlacingHole)
        {
            IsPickingFace = false;
            IsPickingMirrorFace = false;
            IsPlacingEdgeCut = false;
            IsPickingSectionFace = false;
            EndFaceToFaceMode();
        }

        RefreshScaleHint();
    }

    /// <summary>
    /// Puts a hole on the face that was clicked (F-HOL-01): the point is where the hole starts, the axis is the normal of the face,
    /// so the hole is perpendicular to the face, also on a slanted one. A hole cuts the solids of the group it sits in (Q-01), so it
    /// goes into the group or merge that contains the clicked part; a body that is not in one yet is put into a new group together
    /// with the hole. Everything is one undo step. The hole becomes the selected object, so it can be moved, changed or deleted at
    /// once (F-HOL-05). A pick of something that is not in the scene is ignored; a locked body gets no hole.
    /// </summary>
    public void PlaceHole(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is not { } picked || pick.Normal.LengthSquared() < 1e-12f) return;

        for (var node = picked; node is not null && node != _document.Root; node = node.Parent)
        {
            if (!node.Locked) continue;
            SetNotice(Texts.Format("Msg.HoleLocked", node.Name));
            return;
        }

        // the container that the hole joins: the nearest group or merge around the clicked part, else a new group around the whole object
        Node? container = null;
        for (var node = picked; node is not null && node != _document.Root; node = node.Parent)
        {
            if (node is GroupNode or MergeNode)
            {
                container = node;
                break;
            }
        }

        var top = picked;
        while (top.Parent is { } parent && parent != _document.Root) top = parent;
        var wrap = container is null ? new GroupNode(UniqueName(Texts.Get("Name.Group"))) : null;
        var target = container ?? wrap!;

        var normal = Vector3.Normalize(pick.Normal);
        var point = SnappedHolePoint(pick, normal);
        var world = Matrix4x4.CreateFromQuaternion(AlignAxis(normal)) * Matrix4x4.CreateTranslation(point);
        var parentToWorld = container is null ? Matrix4x4.Identity : TransformOps.LocalToWorld(container);
        var invertible = Matrix4x4.Invert(parentToWorld, out var worldToParent);
        var local = invertible ? world * worldToParent : world;
        var transform = Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation)
            ? new Transform(translation, Quaternion.Normalize(rotation), scale)
            : Transform.FromPosition(invertible ? Vector3.Transform(point, worldToParent) : point);

        var hole = new PrimitiveNode("hole", UniqueName(Texts.Get("Name.Hole"))) { Role = NodeRole.Hole, Transform = transform };
        foreach (var (name, expression) in HoleTool.BuildParameters(ThroughDepth(container ?? top, normal))) hole.Parameters[name] = expression;

        var commands = new List<IDocumentCommand>();
        if (wrap is not null) commands.Add(new WrapNodesCommand(_document, wrap, [top]));
        commands.Add(new AddNodeCommand(_document, target, hole));
        _history.Execute(new CompositeCommand(Texts.Format("Name.DrillHole", target.Name), commands));
        SelectOnly([hole.Id]);
    }

    /// <summary>
    /// Where the hole really starts (F-HOL-01). "Snap to objects": a click close to the center of the flat face (within 15% of its
    /// size, 1 to 8 mm) goes to the center. "Snap to grid": otherwise the two directions in the face are rounded to the grid step
    /// (for a face that looks along a world axis), as long as the rounded point is still on the face. Alt or Shift while clicking,
    /// a face that cannot be found (a slanted click that hits no face) or both snaps off keep the clicked point.
    /// </summary>
    private Vector3 SnappedHolePoint(FacePick pick, Vector3 normal)
    {
        if (pick.NoSnap || (!SnapToObjects && !SnapToGrid)) return pick.Point;

        FaceInfo? face = null;
        foreach (var item in ViewportItems)
        {
            if (item.NodeId != pick.NodeId || item.Kind != ViewportItemKind.Solid) continue;
            face = FaceFinder.Find(item.Mesh, pick.Point, normal);
            if (face is not null) break;
        }

        if (face is null) return pick.Point;

        if (SnapToObjects)
        {
            var radius = Math.Clamp(0.15f * face.Extent, 1f, 8f);
            if (Vector3.Distance(pick.Point, face.Center) <= radius) return face.Center;
        }

        if (SnapToGrid && GridStep > 0)
        {
            var step = (float)GridStep;
            float Round(float value) => MathF.Round(value / step) * step;
            var n = Vector3.Abs(face.Normal);
            Vector3? snapped = n.Z > 0.9999f ? new Vector3(Round(pick.Point.X), Round(pick.Point.Y), pick.Point.Z)
                : n.Y > 0.9999f ? new Vector3(Round(pick.Point.X), pick.Point.Y, Round(pick.Point.Z))
                : n.X > 0.9999f ? new Vector3(pick.Point.X, Round(pick.Point.Y), Round(pick.Point.Z))
                : null;
            if (snapped is { } candidate && face.Contains(candidate)) return candidate;
        }

        return pick.Point;
    }


    /// <summary>
    /// The clicked point of a face, or the center of that face when the click is close to it (within 15% of the face size, 1 to 8 mm)
    /// and "Snap to objects" is on and Alt/Shift was not held. Used by "face to face", so that faces that were clicked near their
    /// middles end up exactly centered on each other.
    /// </summary>
    private Vector3 FaceCenterSnapped(FacePick pick)
    {
        if (pick.NoSnap || !SnapToObjects || pick.Normal.LengthSquared() < 1e-12f) return pick.Point;
        var normal = Vector3.Normalize(pick.Normal);
        foreach (var item in ViewportItems)
        {
            if (item.NodeId != pick.NodeId || item.Kind != ViewportItemKind.Solid) continue;
            if (FaceFinder.Find(item.Mesh, pick.Point, normal) is not { } face) continue;
            var radius = Math.Clamp(0.15f * face.Extent, 1f, 8f);
            return Vector3.Distance(pick.Point, face.Center) <= radius ? face.Center : pick.Point;
        }

        return pick.Point;
    }
    /// <summary>
    /// How deep a hole has to be to leave the body on the far side: the extent of the body along the axis plus a little. The depth is
    /// stored as a plain number; for a body that is not evaluated yet 10 mm.
    /// </summary>
    private double ThroughDepth(Node body, Vector3 axis)
    {
        if (_lastResult is null) return 10;
        var ids = new HashSet<Guid>(body.Descendants().Select(d => d.Id)) { body.Id };
        if (BoundsOf(_lastResult, ids, includeGhosts: false) is not { } box) return 10;

        var size = box.Max - box.Min;
        var extent = Math.Abs(axis.X) * size.X + Math.Abs(axis.Y) * size.Y + Math.Abs(axis.Z) * size.Z;
        return Math.Max(0.1, Math.Ceiling((extent + 1) * 10) / 10);
    }

    /// <summary>The turn that takes the +Z axis of a hole to the direction <paramref name="axis"/> (a unit vector).</summary>
    private static Quaternion AlignAxis(Vector3 axis)
    {
        var cosine = Math.Clamp(axis.Z, -1f, 1f);
        if (cosine > 0.999999f) return Quaternion.Identity;
        if (cosine < -0.999999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, axis)), MathF.Acos(cosine));
    }
}
