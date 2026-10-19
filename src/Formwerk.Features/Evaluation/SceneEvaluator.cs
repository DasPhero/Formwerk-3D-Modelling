using System.Numerics;
using System.Text;
using Formwerk.Core.Model;
using Formwerk.Features.Shapes;
using Formwerk.Geometry;

namespace Formwerk.Features.Evaluation;

/// <summary>How a part takes part in the scene.</summary>
public enum PartKind
{
    /// <summary>Printed material.</summary>
    Solid,

    /// <summary>Negative object that still waits for something to cut.</summary>
    Hole,

    /// <summary>Hole that was already applied; only shown as a ghost (F-VIS-04).</summary>
    Ghost,
}

/// <summary>A mesh in world space produced from the node tree. <see cref="Source"/> is the node that owns it.</summary>
public sealed record ScenePart(Node Source, MeshData Mesh, PartKind Kind);

/// <summary>A problem with one node; the node keeps its last valid geometry (F-BOO-05).</summary>
public sealed record EvaluationError(Node Node, string Message);

public sealed record EvaluationResult(IReadOnlyList<ScenePart> Parts, IReadOnlyList<EvaluationError> Errors)
{
    public bool HasErrors => Errors.Count > 0;
}

/// <summary>
/// Turns the node tree into meshes (spec section 6, "Auswertung").
/// <list type="bullet">
/// <item>Primitive: generator mesh with the node transform. Mesh: the imported mesh from the <see cref="MeshStore"/> with the node transform.</item>
/// <item>Group: children stay separate objects; holes inside the group cut the solids of the same group.</item>
/// <item>Boolean: children are the operands (first child is the minuend); their solid/hole flag does not matter.</item>
/// <item>Merge: union of the solid children minus the union of the hole children, as one body.</item>
/// </list>
/// Results are cached; a failing operation keeps the last valid geometry and reports an error.
/// Not thread-safe: use one evaluator per evaluation thread.
/// </summary>
public sealed class SceneEvaluator(IGeometryKernel kernel, MeshStore? meshes = null)
{
    private const int MaxCacheEntries = 512;

    private readonly Dictionary<string, MeshData> _cache = [];
    private readonly Dictionary<Guid, MeshData> _lastValid = [];

    public int CacheHits { get; private set; }

    public EvaluationResult Evaluate(FormwerkDocument document, CancellationToken cancellationToken = default)
    {
        var variables = document.ResolveVariables(out var variableErrors);
        var context = new Context(variables, [], cancellationToken);
        foreach (var (name, message) in variableErrors)
            context.Errors.Add(new EvaluationError(document.Root, $"Variable '{name}': {message}"));

        var parts = new List<ScenePart>();
        foreach (var child in document.Root.Children) parts.AddRange(Eval(child, context));

        // Holes at the top level have nothing to cut; they are shown as ghosts only.
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind == PartKind.Hole) parts[i] = parts[i] with { Kind = PartKind.Ghost };
        }

        return new EvaluationResult(parts, context.Errors);
    }

    private sealed record Context(
        IReadOnlyDictionary<string, double> Variables, List<EvaluationError> Errors, CancellationToken Token);

    private List<ScenePart> Eval(Node node, Context c)
    {
        c.Token.ThrowIfCancellationRequested();
        if (!node.Visible) return [];

        return node switch
        {
            PrimitiveNode p => EvalPrimitive(p, c),
            GroupNode g => EvalGroup(g, c),
            BooleanNode b => EvalBoolean(b, c),
            MergeNode m => EvalMerge(m, c),
            MeshNode mesh => EvalMesh(mesh, c),
            _ => Unsupported(node, c),
        };
    }

    private static PartKind KindOf(Node node) => node.Role == NodeRole.Hole ? PartKind.Hole : PartKind.Solid;

    private static List<ScenePart> Unsupported(Node node, Context c)
    {
        c.Errors.Add(new EvaluationError(node, $"'{node.Name}' cannot be displayed yet."));
        return [];
    }

    private List<ScenePart> EvalPrimitive(PrimitiveNode node, Context c)
    {
        var generator = ShapeLibrary.Find(node.GeneratorId);
        if (generator is null)
        {
            c.Errors.Add(new EvaluationError(node, $"Unknown shape '{node.GeneratorId}'."));
            return [];
        }

        var values = node.EvaluateParameters(c.Variables, out var parameterErrors);
        if (parameterErrors.Count > 0)
        {
            var (name, message) = parameterErrors.First();
            c.Errors.Add(new EvaluationError(node, $"{name}: {message}"));
            return LastValid(node, node.Transform.ToMatrix(), KindOf(node));
        }

        var key = PrimitiveKey(generator, values);
        if (!_cache.TryGetValue(key, out var local))
        {
            try
            {
                local = generator.Generate(values);
            }
            catch (ArgumentException ex)
            {
                c.Errors.Add(new EvaluationError(node, ex.Message));
                return LastValid(node, node.Transform.ToMatrix(), KindOf(node));
            }

            Store(key, local);
        }
        else
        {
            CacheHits++;
        }

        _lastValid[node.Id] = local;
        return [new ScenePart(node, local.Transformed(node.Transform.ToMatrix()), KindOf(node))];
    }

    private List<ScenePart> EvalMesh(MeshNode node, Context c)
    {
        if (meshes is null || !meshes.TryGet(node.MeshId, out var mesh))
        {
            c.Errors.Add(new EvaluationError(node, $"The mesh of '{node.Name}' is missing."));
            return [];
        }

        _lastValid[node.Id] = mesh;
        return mesh.IsEmpty ? [] : [new ScenePart(node, mesh.Transformed(node.Transform.ToMatrix()), KindOf(node))];
    }

    private List<ScenePart> EvalGroup(GroupNode group, Context c)
    {
        var parts = new List<ScenePart>();
        foreach (var child in group.Children) parts.AddRange(Eval(child, c));
        parts = ResolveThroughHoles(group, parts, c);

        var holes = parts.Where(p => p.Kind == PartKind.Hole).ToList();
        if (holes.Count > 0)
        {
            var keyed = KeyHoles(holes);
            MeshData? cutter = null;
            var cutterDone = false;
            MeshData? Cutter() // all holes united; only needed when a solid cannot be cut incrementally
            {
                if (!cutterDone)
                {
                    cutter = SafeUnion(group, holes.Select(h => h.Mesh).ToList(), c);
                    cutterDone = true;
                }

                return cutter;
            }

            var occurrences = new Dictionary<Guid, int>();
            var result = new List<ScenePart>(parts.Count);
            foreach (var part in parts)
            {
                if (part.Kind == PartKind.Hole)
                {
                    result.Add(part with { Kind = PartKind.Ghost });
                }
                else if (part.Kind == PartKind.Solid)
                {
                    var occurrence = occurrences[part.Source.Id] = occurrences.GetValueOrDefault(part.Source.Id) + 1;
                    var cut = CutSolid((group.Id, part.Source.Id, occurrence), part, keyed, Cutter, c);
                    if (cut is null) result.Add(part);
                    else if (!cut.IsEmpty) result.Add(part with { Mesh = cut });
                }
                else
                {
                    result.Add(part);
                }
            }

            parts = result;
        }

        return Finish(group, parts);
    }


    // ---- holes that go through all (F-HOL-02) -------------------------------------------------------

    /// <summary>
    /// A hole with <c>through = 1</c> is as deep as it takes to leave the solids of its container on the far side: from its
    /// origin along its axis to the farthest corner of the boxes of those solids, plus 1 mm. So the hole stays through when the body
    /// is resized or moved. A hole that is not in a group or merge (it cuts nothing, it is only a ghost) keeps its stored depth.
    /// </summary>
    private List<ScenePart> ResolveThroughHoles(Node container, List<ScenePart> parts, Context c)
    {
        if (!parts.Any(p => p.Kind == PartKind.Hole && p.Source is PrimitiveNode { GeneratorId: "hole" })) return parts;

        var corners = new List<Vector3>();
        foreach (var solid in parts.Where(p => p.Kind == PartKind.Solid && !p.Mesh.IsEmpty))
        {
            var b = solid.Mesh.ComputeBounds();
            foreach (var x in new[] { b.Min.X, b.Max.X })
            foreach (var y in new[] { b.Min.Y, b.Max.Y })
            foreach (var z in new[] { b.Min.Z, b.Max.Z }) corners.Add(new Vector3(x, y, z));
        }

        if (corners.Count == 0) return parts;

        var result = new List<ScenePart>(parts.Count);
        foreach (var part in parts)
        {
            result.Add(part.Kind == PartKind.Hole && part.Source is PrimitiveNode { GeneratorId: "hole" } hole
                       && ThroughPart(container, part, hole, corners, c) is { } deeper
                ? deeper
                : part);
        }

        return result;
    }

    private ScenePart? ThroughPart(Node container, ScenePart part, PrimitiveNode hole, List<Vector3> corners, Context c)
    {
        var values = hole.EvaluateParameters(c.Variables, out var errors);
        if (errors.Count > 0 || values.GetValueOrDefault("through") < 0.5) return null;

        // hole space to the space of the container (the container's own transform is applied later, to all parts together)
        var matrix = Matrix4x4.Identity;
        for (Node? node = hole; node is not null && node != container; node = node.Parent) matrix *= node.Transform.ToMatrix();

        var origin = Vector3.Transform(Vector3.Zero, matrix);
        var axisVector = Vector3.TransformNormal(Vector3.UnitZ, matrix);
        var scale = axisVector.Length();
        if (scale < 1e-9f) return null;
        var axis = axisVector / scale;

        var reach = corners.Max(corner => Vector3.Dot(origin - corner, axis)) + 1f;
        values["depth"] = Math.Clamp(Math.Max(reach, 0.1f) / scale, 0.01, 10000);

        var generator = ShapeLibrary.Hole;
        var key = PrimitiveKey(generator, values);
        if (!_cache.TryGetValue(key, out var local))
        {
            try
            {
                local = generator.Generate(values);
            }
            catch (ArgumentException)
            {
                return null;
            }

            Store(key, local);
        }

        return part with { Mesh = local.Transformed(matrix) };
    }

    // ---- cutting holes out of a solid, incrementally while one hole is dragged (F-BOO-03, Q-03) ----------------

    private readonly record struct HoleKey(Guid Node, int Occurrence);

    private sealed record KeyedHole(HoleKey Key, long Hash, MeshData Mesh);

    /// <summary>What was cut last time out of one solid: enough to see that only one hole changed since, and the solid without the others.</summary>
    private sealed class CutState
    {
        public long SolidHash;
        public Dictionary<HoleKey, long> Holes = [];
        public HoleKey? LastMoved;
        public MeshData? Base;
        public long BaseOthers;
    }

    private const int MaxCutStates = 64;
    private const int MaxBaseTriangles = 300_000;
    private readonly Dictionary<(Guid Group, Guid Solid, int Occurrence), CutState> _cuts = [];

    private static List<KeyedHole> KeyHoles(IReadOnlyList<ScenePart> holes)
    {
        var occurrences = new Dictionary<Guid, int>();
        var keyed = new List<KeyedHole>(holes.Count);
        foreach (var hole in holes)
        {
            var occurrence = occurrences[hole.Source.Id] = occurrences.GetValueOrDefault(hole.Source.Id) + 1;
            keyed.Add(new KeyedHole(new HoleKey(hole.Source.Id, occurrence), MeshHash(hole.Mesh), hole.Mesh));
        }

        return keyed;
    }

    /// <summary>A hash of the geometry (FNV-1a over the vertex bits and the indices); a changed mesh gives another value.</summary>
    private static long MeshHash(MeshData mesh)
    {
        unchecked
        {
            var h = (long)-3750763034362895579L;
            foreach (var v in mesh.Vertices)
            {
                h = (h ^ BitConverter.SingleToInt32Bits(v.X)) * 1099511628211L;
                h = (h ^ BitConverter.SingleToInt32Bits(v.Y)) * 1099511628211L;
                h = (h ^ BitConverter.SingleToInt32Bits(v.Z)) * 1099511628211L;
            }

            foreach (var i in mesh.Indices) h = (h ^ i) * 1099511628211L;
            return h;
        }
    }

    private static long OthersHash(Dictionary<HoleKey, long> holes, HoleKey except)
    {
        unchecked
        {
            long sum = 0;
            foreach (var (key, hash) in holes)
            {
                if (key == except) continue;
                sum += (hash ^ key.Node.GetHashCode() * 31L ^ key.Occurrence) * unchecked((long)0x9E3779B97F4A7C15UL); // order does not matter
            }

            return sum;
        }
    }

    /// <summary>
    /// The solid with all holes cut out, or null when that failed (the error is reported). When exactly one hole differs from the last
    /// calculation (it is being dragged) the solid without all the other holes is remembered from the second step on, and a step
    /// only cuts the moved hole out of it. Anything else is calculated from scratch, so the result never depends on the history.
    /// </summary>
    private MeshData? CutSolid((Guid Group, Guid Solid, int Occurrence) id, ScenePart part, List<KeyedHole> holes, Func<MeshData?> cutter, Context c)
    {
        var solidHash = MeshHash(part.Mesh);
        var current = holes.ToDictionary(h => h.Key, h => h.Hash);
        _cuts.TryGetValue(id, out var previous);

        HoleKey? moved = null;
        if (previous is not null && previous.SolidHash == solidHash && previous.Holes.Count == current.Count
            && current.Keys.All(previous.Holes.ContainsKey))
        {
            var changed = current.Where(kv => previous.Holes[kv.Key] != kv.Value).Select(kv => kv.Key).ToList();
            if (changed.Count == 1) moved = changed[0];
        }

        var next = new CutState { SolidHash = solidHash, Holes = current };
        MeshData? result = null;
        var attempted = false;

        if (moved is { } hole)
        {
            next.LastMoved = hole;
            var movedMesh = holes.First(h => h.Key == hole).Mesh;
            var othersHash = OthersHash(current, hole);
            if (previous!.Base is { } remembered && previous.BaseOthers == othersHash)
            {
                attempted = true;
                result = SafeBoolean(part.Source, BooleanOperation.Difference, [remembered, movedMesh], c);
                next.Base = remembered;
                next.BaseOthers = othersHash;
            }
            else if (previous.LastMoved == hole && part.Mesh.TriangleCount <= MaxBaseTriangles)
            {
                // the same hole moved twice in a row: this is a drag, remember the plate without the other holes
                attempted = true;
                var others = holes.Where(h => h.Key != hole).Select(h => h.Mesh).ToList();
                MeshData? baseMesh = part.Mesh;
                if (others.Count > 0)
                {
                    var cutOthers = SafeUnion(part.Source, others, c);
                    baseMesh = cutOthers is null ? null : SafeBoolean(part.Source, BooleanOperation.Difference, [part.Mesh, cutOthers], c);
                }

                if (baseMesh is not null)
                {
                    result = SafeBoolean(part.Source, BooleanOperation.Difference, [baseMesh, movedMesh], c);
                    next.Base = baseMesh;
                    next.BaseOthers = othersHash;
                }
            }
        }

        if (!attempted)
        {
            if (cutter() is not { } all)
            {
                _cuts.Remove(id);
                return null;
            }

            result = SafeBoolean(part.Source, BooleanOperation.Difference, [part.Mesh, all], c);
        }

        if (result is null)
        {
            _cuts.Remove(id);
            return null;
        }

        if (_cuts.Count >= MaxCutStates && !_cuts.ContainsKey(id)) _cuts.Clear();
        _cuts[id] = next;
        return result;
    }

    private List<ScenePart> EvalBoolean(BooleanNode node, Context c)
    {
        var operands = new List<MeshData>();
        foreach (var child in node.Children)
        {
            // The operation decides what an operand does, so a child that is flagged as a hole is an operand like any other.
            var solids = Eval(child, c).Where(p => p.Kind != PartKind.Ghost).Select(p => p.Mesh).ToList();
            if (solids.Count == 0) continue;
            operands.Add(solids.Count == 1 ? solids[0] : SafeUnion(child, solids, c) ?? MeshData.Empty);
        }

        var mesh = Cached(node, c, () =>
            operands.Count == 0 ? MeshData.Empty : kernel.Boolean(node.Operation, operands, c.Token));

        return Single(node, mesh);
    }

    private List<ScenePart> EvalMerge(MergeNode node, Context c)
    {
        var parts = new List<ScenePart>();
        foreach (var child in node.Children) parts.AddRange(Eval(child, c));
        parts = ResolveThroughHoles(node, parts, c);
        var solids = parts.Where(p => p.Kind == PartKind.Solid).Select(p => p.Mesh).ToList();
        var holes = parts.Where(p => p.Kind == PartKind.Hole).Select(p => p.Mesh).ToList();

        var mesh = Cached(node, c, () =>
        {
            if (solids.Count == 0) return MeshData.Empty;
            var body = Unite(solids, c);
            if (holes.Count == 0) return body;
            var cutter = Unite(holes, c);
            return kernel.Boolean(BooleanOperation.Difference, [body, cutter], c.Token);
        });

        var result = Single(node, mesh);
        result.AddRange(parts.Where(p => p.Kind == PartKind.Hole)
            .Select(p => p with { Mesh = p.Mesh.Transformed(node.Transform.ToMatrix()), Kind = PartKind.Ghost }));
        return result;
    }

    /// <summary>Applies the node transform and role to parts that are given in the node's local space.</summary>
    private static List<ScenePart> Finish(Node node, List<ScenePart> localParts)
    {
        var matrix = node.Transform.ToMatrix();
        var holeGroup = node.Role == NodeRole.Hole;
        var result = new List<ScenePart>(localParts.Count);
        foreach (var part in localParts)
        {
            var kind = holeGroup && part.Kind == PartKind.Solid ? PartKind.Hole : part.Kind;
            result.Add(part with { Mesh = part.Mesh.Transformed(matrix), Kind = kind });
        }

        return result;
    }

    private static List<ScenePart> Single(Node node, MeshData localMesh) =>
        localMesh.IsEmpty ? [] : [new ScenePart(node, localMesh.Transformed(node.Transform.ToMatrix()), KindOf(node))];

    private MeshData Cached(Node node, Context c, Func<MeshData> compute)
    {
        var key = "N|" + node.ComputeHash(c.Variables) + "|" + VisibilitySignature(node);
        if (_cache.TryGetValue(key, out var hit))
        {
            CacheHits++;
            _lastValid[node.Id] = hit;
            return hit;
        }

        try
        {
            var result = compute();
            Store(key, result);
            _lastValid[node.Id] = result;
            return result;
        }
        catch (GeometryException ex)
        {
            c.Errors.Add(new EvaluationError(node, ex.Message));
            return _lastValid.GetValueOrDefault(node.Id) ?? MeshData.Empty;
        }
    }

    private List<ScenePart> LastValid(Node node, System.Numerics.Matrix4x4 matrix, PartKind kind) =>
        _lastValid.TryGetValue(node.Id, out var mesh) ? [new ScenePart(node, mesh.Transformed(matrix), kind)] : [];

    /// <summary>The union of the meshes; pieces that cannot meet are put together without the boolean engine (F-BOO-03).</summary>
    private MeshData Unite(IReadOnlyList<MeshData> meshes, Context c) =>
        MeshUnion.Compute(meshes, group => kernel.Boolean(BooleanOperation.Union, group, c.Token));

    private MeshData? SafeUnion(Node owner, IReadOnlyList<MeshData> meshes, Context c)
    {
        try
        {
            return Unite(meshes, c);
        }
        catch (GeometryException ex)
        {
            c.Errors.Add(new EvaluationError(owner, ex.Message));
            return null;
        }
    }

    private MeshData? SafeBoolean(Node owner, BooleanOperation op, IReadOnlyList<MeshData> operands, Context c)
    {
        try
        {
            return kernel.Boolean(op, operands, c.Token);
        }
        catch (GeometryException ex)
        {
            c.Errors.Add(new EvaluationError(owner, ex.Message));
            return null;
        }
    }

    private void Store(string key, MeshData mesh)
    {
        if (_cache.Count >= MaxCacheEntries) _cache.Clear();
        _cache[key] = mesh;
    }

    private static string PrimitiveKey(IShapeGenerator generator, IReadOnlyDictionary<string, double> values)
    {
        var sb = new StringBuilder("P|").Append(generator.Id);
        foreach (var definition in generator.Parameters)
        {
            var value = values.TryGetValue(definition.Name, out var v) ? v : definition.Default;
            sb.Append('|').Append(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Visibility changes the result of a container but is not part of the node hash.</summary>
    private static string VisibilitySignature(Node node)
    {
        var sb = new StringBuilder();
        foreach (var d in node.Descendants()) sb.Append(d.Visible ? '1' : '0');
        return sb.ToString();
    }
}
