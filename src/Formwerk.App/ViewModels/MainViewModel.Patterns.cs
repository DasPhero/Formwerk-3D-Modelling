using System.Globalization;
using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Expressions;
using Formwerk.Core.Model;
using Formwerk.Features.Evaluation;
using Formwerk.Geometry;

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

    /// <summary>
    /// Makes the selected objects the source of a new circular pattern: three instances around a full circle, turned about the Z
    /// axis. The center is the axis of the biggest body next to the selection (a cylinder with a hole: the axis of the cylinder), or
    /// the origin when there is none; the pattern is put there and the objects keep their place in the scene, so their distance from
    /// the center is the radius. One undo step; radius, distance from the edge, count and angle are edited in the properties.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPattern))]
    private void CircularPattern()
    {
        var nodes = SelectedTopLevel();
        if (!SameParent(nodes)) return;

        var center = CircleCenter(nodes);
        var pattern = new PatternNode(UniqueName(Texts.Get("Name.Pattern")), PatternKind.Circular) { Transform = Transform.FromPosition(center) };
        pattern.Parameters["count"] = "3";
        pattern.Parameters["angle"] = "360";
        pattern.Parameters["axis"] = "2";

        var commands = new List<IDocumentCommand> { new WrapNodesCommand(_document, pattern, nodes) };
        commands.Add(new SetTransformsCommand(
            _document, nodes.Select(n => (n, n.Transform, n.Transform with { Position = n.Transform.Position - center })), "Move to the center"));
        _history.Execute(new CompositeCommand(Texts.Format("Name.CircularPattern", nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} objects"), commands));
        SelectOnly([pattern.Id]);
    }

    /// <summary>
    /// The center of a new circular pattern, in the space of the parent: the middle (in X and Y) of the biggest body that is not part
    /// of the selection and lies in the same container, with Z = 0; the origin without such a body.
    /// </summary>
    private Vector3 CircleCenter(IReadOnlyCollection<Node> nodes)
    {
        if (_lastResult is null) return Vector3.Zero;
        var selected = new HashSet<Guid>();
        foreach (var node in nodes) selected.UnionWith(node.Descendants().Select(d => d.Id).Prepend(node.Id));
        var parent = nodes.First().Parent;
        // the evaluated parts belong to a copy of the document (same ids): compare by id, not by reference
        var scope = parent is null || parent == _document.Root ? null : new HashSet<Guid>(parent.Descendants().Select(d => d.Id));

        var best = default(Bounds?);
        var bestArea = -1f;
        foreach (var part in _lastResult.Parts)
        {
            if (part.Kind != PartKind.Solid || part.Mesh.IsEmpty || selected.Contains(part.Source.Id)) continue;
            if (scope is not null && !scope.Contains(part.Source.Id)) continue;
            var box = part.Mesh.ComputeBounds();
            var area = box.Size.X * box.Size.Y;
            if (area <= bestArea) continue;
            best = box;
            bestArea = area;
        }

        if (best is not { } body) return Vector3.Zero;
        if (!Matrix4x4.Invert(TransformOps.ParentToWorld(nodes.First()), out var toParent)) return Vector3.Zero;
        var middle = Vector3.Transform(new Vector3(body.Center.X, body.Center.Y, 0), toParent);
        return new Vector3(middle.X, middle.Y, 0);
    }

    /// <summary>
    /// What a circular pattern is measured against (F-PAT-01): the distance from its center to the nearest outer edge of the biggest
    /// body that contains the center and is not part of the pattern, and half the width of the source. Null when there is no such body
    /// or nothing is evaluated yet.
    /// </summary>
    private PatternReference? ReferenceOf(PatternNode pattern)
    {
        if (_lastResult is null || pattern.Kind != PatternKind.Circular) return null;
        if (!Matrix4x4.Invert(TransformOps.LocalToWorld(pattern), out var toPattern)) return null;

        var own = new HashSet<Guid>(pattern.Descendants().Select(d => d.Id)) { pattern.Id };
        var axis = pattern.Parameters.TryGetValue("axis", out var text)
            && ExpressionParser.Evaluate(text, _document.ResolveVariables(out _)) is { IsSuccess: true } result ? (int)Math.Round(result.Value) : 2;

        // the plane of the circle: the two coordinates that are not the axis
        (float U, float V) Plane(Vector3 p) => axis switch { 0 => (p.Y, p.Z), 1 => (p.Z, p.X), _ => (p.X, p.Y) };

        (float MinU, float MaxU, float MinV, float MaxV) Box(MeshData mesh)
        {
            var b = mesh.ComputeBounds();
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            foreach (var x in new[] { b.Min.X, b.Max.X })
            foreach (var y in new[] { b.Min.Y, b.Max.Y })
            foreach (var z in new[] { b.Min.Z, b.Max.Z })
            {
                var (u, v) = Plane(Vector3.Transform(new Vector3(x, y, z), toPattern));
                minU = Math.Min(minU, u);
                maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v);
                maxV = Math.Max(maxV, v);
            }

            return (minU, maxU, minV, maxV);
        }

        double? outer = null;
        var bestArea = -1f;
        foreach (var part in _lastResult.Parts)
        {
            if (part.Kind != PartKind.Solid || part.Mesh.IsEmpty || own.Contains(part.Source.Id)) continue;
            var (minU, maxU, minV, maxV) = Box(part.Mesh);
            if (minU > 0 || maxU < 0 || minV > 0 || maxV < 0) continue; // the center must lie inside the body
            var area = (maxU - minU) * (maxV - minV);
            if (area <= bestArea) continue;
            bestArea = area;
            outer = Math.Min(Math.Min(-minU, maxU), Math.Min(-minV, maxV));
        }

        if (outer is null) return null;

        var half = 0.0;
        if (_lastResult.Parts.FirstOrDefault(p => own.Contains(p.Source.Id) && !p.Mesh.IsEmpty) is { } source)
        {
            var (minU, maxU, minV, maxV) = Box(source.Mesh);
            half = Math.Max(maxU - minU, maxV - minV) / 2;
        }

        return new PatternReference(outer.Value, half);
    }
}
