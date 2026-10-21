using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>Chamfer and fillet on selected edges (F-EDG-02): click an edge, get a cutter that stays in the tree as a hole object.</summary>
public sealed partial class MainViewModel
{
    private EdgeToolViewModel? _edgeTool;

    /// <summary>The "chamfer / fillet" tool waits for a click near an edge (it stays on until Esc or the button, so several edges can follow).</summary>
    [ObservableProperty]
    public partial bool IsPlacingEdgeCut { get; set; }

    /// <summary>The settings of the next cutter.</summary>
    public EdgeToolViewModel EdgeTool => _edgeTool ??= new EdgeToolViewModel(() => _document.ResolveVariables(out _));

    /// <summary>Above this many triangles in the clicked part the edge is not looked for at every mouse move (it would make the mouse sticky); a click still finds it.</summary>
    public const int EdgeHoverTriangleLimit = 60_000;

    /// <summary>The edge under the mouse while the tool is on: yellow when a click would cut it, grey when it cannot be cut; null when there is none.</summary>
    [ObservableProperty]
    public partial ViewportEdgeHighlight? EdgeHighlight { get; set; }

    partial void OnIsPlacingEdgeCutChanged(bool value)
    {
        OnPropertyChanged(nameof(IsFacePickActive));
        ViewportDimensions = ComputeDimensions();
        if (!value) EdgeHighlight = null;
    }

    /// <summary>
    /// The mouse is over a face in a face mode (about every 40 ms), or over nothing (null). In the edge tool the edge that a click would
    /// take is shown; one that cannot be cut is shown grey and the hint line says why. Other modes ignore it.
    /// </summary>
    [RelayCommand]
    private void FaceHovered(FacePick? pick)
    {
        if (!IsPlacingEdgeCut || pick is null || _document.FindNode(pick.NodeId) is null || pick.Normal.LengthSquared() < 1e-12f)
        {
            ClearEdgeHover();
            return;
        }

        var found = FindEdge(pick, EdgeHoverTriangleLimit);
        if (found.Preview is not { } preview)
        {
            ClearEdgeHover();
            return;
        }

        var valid = found.Problem == EdgeProblem.None;
        EdgeHighlight = new ViewportEdgeHighlight(preview.Points, preview.Closed, valid);
        HintText = valid ? Texts.Get("Hint.EdgeCutPlace") : ProblemText(found.Problem);
    }

    private void ClearEdgeHover()
    {
        if (EdgeHighlight is null) return;
        EdgeHighlight = null;
        if (IsPlacingEdgeCut) RefreshScaleHint();
    }

    /// <summary>What <see cref="EdgeFinder"/> makes of the click on the mesh of the clicked part; parts with more triangles than <paramref name="triangleLimit"/> are skipped.</summary>
    private EdgeFindResult FindEdge(FacePick pick, int triangleLimit = int.MaxValue)
    {
        var found = EdgeFindResult.Fail(EdgeProblem.NoFace);
        foreach (var item in ViewportItems)
        {
            if (item.NodeId != pick.NodeId || item.Kind != ViewportItemKind.Solid || item.Mesh.TriangleCount > triangleLimit) continue;
            var attempt = EdgeFinder.Find(item.Mesh, pick.Point, pick.Normal);
            if (attempt.Problem == EdgeProblem.None || found.Problem == EdgeProblem.NoFace) found = attempt;
            if (attempt.Problem == EdgeProblem.None) break;
        }

        return found;
    }

    private bool CanEdgeCut() => _document.Root.Children.Count > 0;

    /// <summary>Starts or ends the edge tool.</summary>
    [RelayCommand(CanExecute = nameof(CanEdgeCut))]
    private void EdgeCut()
    {
        IsPlacingEdgeCut = !IsPlacingEdgeCut;
        if (IsPlacingEdgeCut)
        {
            IsPickingFace = false;
            IsPlacingHole = false;
            IsPickingMirrorFace = false;
            EndFaceToFaceMode();
        }

        RefreshScaleHint();
    }

    /// <summary>
    /// Chamfers or rounds the edge that a click on a face means: the nearest edge of that face (<see cref="EdgeFinder"/>). A cutter body
    /// (a wedge for a straight edge, a ring for the round rim of a cylinder) is put on the edge as a hole object, so the result stays
    /// editable: size and type can be changed in the properties, and deleting the cutter gives the sharp edge back. Like a drilled hole it
    /// cuts the solids of its group, so it goes into the group or merge around the clicked part; a body that is not in one yet is put
    /// into a new group together with it. One undo step; the cutter is selected. Edges that cannot be cut (inner edges, curves, slanted
    /// round edges) and clicks that find no face change nothing and say why in the status bar; a locked body is not cut.
    /// </summary>
    public void PlaceEdgeCut(FacePick pick)
    {
        if (_document.FindNode(pick.NodeId) is not { } picked || pick.Normal.LengthSquared() < 1e-12f) return;

        for (var node = picked; node is not null && node != _document.Root; node = node.Parent)
        {
            if (!node.Locked) continue;
            SetNotice(Texts.Format("Msg.EdgeLocked", node.Name));
            return;
        }

        if (EdgeTool.Size.HasError)
        {
            SetNotice(Texts.Format("Msg.EdgeSize", EdgeTool.Size.Error ?? ""));
            return;
        }

        var found = FindEdge(pick);
        if (found.Problem != EdgeProblem.None)
        {
            SetNotice(ProblemText(found.Problem));
            return;
        }

        var kind = EdgeTool.EdgeType;
        var cutter = new PrimitiveNode(found.Straight is not null ? "edgecut" : "edgering", UniqueName(Texts.Get("Name.EdgeCut"))) { Role = NodeRole.Hole };
        Matrix4x4 world;
        if (found.Straight is { } edge)
        {
            // X along the edge, Z away from the material between the faces; the cutter starts at the start of the edge
            var x = Vector3.Normalize(edge.End - edge.Start);
            var bisector = edge.NormalA + edge.NormalB;
            var z = Vector3.Normalize(bisector - x * Vector3.Dot(bisector, x));
            world = Frame(x, Vector3.Cross(z, x), z, edge.Start);
            cutter.Parameters["length"] = Number(Vector3.Distance(edge.Start, edge.End));
            cutter.Parameters["angle"] = Number(edge.InnerAngle);
        }
        else
        {
            // Z along the axis, X to a corner of the rim, so the cutter has its corners where the wall has them
            var rim = found.Rim!;
            var z = Vector3.Normalize(rim.Axis);
            var x = Vector3.Normalize(rim.Reference - z * Vector3.Dot(rim.Reference, z));
            world = Frame(x, Vector3.Cross(z, x), z, rim.Center);
            cutter.Parameters["radius"] = Number(rim.Radius);
            cutter.Parameters["inside"] = rim.IsHole ? "1" : "0";
            cutter.Parameters["segments"] = rim.Segments.ToString(CultureInfo.InvariantCulture);
        }

        cutter.Parameters["edgeSize"] = EdgeTool.SizeExpression;
        cutter.Parameters["edgeType"] = kind.ToString(CultureInfo.InvariantCulture);

        // the container that the cutter joins: the nearest group or merge around the clicked part, else a new group around the whole object
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

        var parentToWorld = container is null ? Matrix4x4.Identity : TransformOps.LocalToWorld(container);
        var invertible = Matrix4x4.Invert(parentToWorld, out var worldToParent);
        var local = invertible ? world * worldToParent : world;
        cutter.Transform = Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation)
            ? new Transform(translation, Quaternion.Normalize(rotation), SnapScale(scale))
            : Transform.FromPosition(Vector3.Transform(Vector3.Zero, local));

        var commands = new List<IDocumentCommand>();
        if (wrap is not null) commands.Add(new WrapNodesCommand(_document, wrap, [top]));
        commands.Add(new AddNodeCommand(_document, target, cutter));
        _history.Execute(new CompositeCommand(kind == 0 ? Texts.Format("Name.EdgeChamfer", target.Name) : Texts.Format("Name.EdgeFillet", target.Name), commands));
        SelectOnly([cutter.Id]);
        EdgeHighlight = null; // the edge is cut now
        SetNotice(Texts.Format("Msg.EdgeCut", cutter.Name));
    }

    /// <summary>What the status bar says when a click gives no edge that can be cut.</summary>
    private static string ProblemText(EdgeProblem problem) => problem switch
    {
        EdgeProblem.NoEdge => Texts.Get("Msg.Edge.NoEdge"),
        EdgeProblem.Concave => Texts.Get("Msg.Edge.Concave"),
        EdgeProblem.AlmostFlat => Texts.Get("Msg.Edge.AlmostFlat"),
        EdgeProblem.TooSharp => Texts.Get("Msg.Edge.TooSharp"),
        EdgeProblem.NotRightAngle => Texts.Get("Msg.Edge.NotRightAngle"),
        EdgeProblem.Curved => Texts.Get("Msg.Edge.Curved"),
        _ => Texts.Get("Msg.Edge.NoFace"),
    };

    /// <summary>The matrix that puts a local frame (the three axes and the origin) into space.</summary>
    private static Matrix4x4 Frame(Vector3 x, Vector3 y, Vector3 z, Vector3 origin) =>
        new(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, origin.X, origin.Y, origin.Z, 1);

    /// <summary>A scale that is 1 up to rounding noise is exactly 1 (the cutter must not be stretched by float errors).</summary>
    private static Vector3 SnapScale(Vector3 scale) =>
        MathF.Abs(scale.X - 1) < 1e-4f && MathF.Abs(scale.Y - 1) < 1e-4f && MathF.Abs(scale.Z - 1) < 1e-4f ? Vector3.One : scale;

    private static string Number(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);
}
