using System.Collections.ObjectModel;
using System.Numerics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Repair;
using Formwerk.Viewport;

namespace Formwerk.App.ViewModels;

/// <summary>One kind of problem in the banner: its name, how many there are and whether its places are shown in the view (F-REP-01).</summary>
public sealed partial class RepairProblemItem(ProblemKind kind, string label, int count) : ObservableObject
{
    public ProblemKind Kind { get; } = kind;

    public string Label { get; } = label;

    public int Count { get; } = count;

    [ObservableProperty]
    public partial bool IsShown { get; set; }
}

/// <summary>Checking selected meshes and repairing them with one click (F-IMP-03, F-REP-01, F-REP-02, F-REP-04).</summary>
public sealed partial class MainViewModel
{
    private ProblemKind? _shownProblem;
    private int _problemVersion;
    private readonly Dictionary<(Guid Mesh, ProblemKind Kind), ProblemPlaces> _placeCache = [];

    /// <summary>The kinds of problems of the selected meshes, as buttons of the banner.</summary>
    public ObservableCollection<RepairProblemItem> RepairProblems { get; } = [];

    /// <summary>The places of the shown kind of problem, in world coordinates; null when none is shown.</summary>
    [ObservableProperty]
    public partial ViewportProblemMarks? ProblemMarks { get; set; }

    /// <summary>Analyses by mesh id. A mesh never changes under its id, so an analysis stays valid.</summary>
    private readonly Dictionary<Guid, MeshReport> _reports = [];

    private Task _repairCheck = Task.CompletedTask;
    private string _repairCheckKey = "";
    private int _repairCheckVersion;
    private bool _canRepair;
    private bool _repairing;

    /// <summary>The banner above the 3D view: what is wrong with the selected mesh objects. Empty when nothing is wrong.</summary>
    [ObservableProperty]
    public partial string RepairBannerText { get; set; } = "";

    public bool IsRepairBannerVisible => !string.IsNullOrEmpty(RepairBannerText);

    partial void OnRepairBannerTextChanged(string value) => OnPropertyChanged(nameof(IsRepairBannerVisible));

    /// <summary>Completes when the check of the current selection is done (for tests).</summary>
    public async Task WaitForRepairCheckAsync()
    {
        Task current;
        do
        {
            current = _repairCheck;
            await current.ConfigureAwait(true);
        }
        while (current != _repairCheck);
    }

    /// <summary>The mesh objects among the selected objects, also inside selected groups.</summary>
    private List<MeshNode> SelectedMeshNodes() =>
        [.. SelectedTopLevel().SelectMany(n => n.Descendants().Prepend(n)).OfType<MeshNode>().Distinct()];

    /// <summary>Starts a check when the meshes of the selection are not the ones that were checked last. Cheap otherwise.</summary>
    private void QueueRepairCheck()
    {
        var nodes = SelectedMeshNodes();
        var key = string.Join(",", nodes.Select(n => $"{n.Id:N}:{n.MeshId:N}").Order()); // objects count, not only meshes: copies share one mesh
        if (key == _repairCheckKey) return;
        _repairCheckKey = key;
        ClearProblems();
        _repairCheck = CheckMeshesAsync(nodes, ++_repairCheckVersion);
    }

    private async Task CheckMeshesAsync(List<MeshNode> nodes, int version)
    {
        try
        {
            var missing = nodes.Select(n => n.MeshId).Distinct().Where(id => !_reports.ContainsKey(id) && Meshes.Contains(id)).ToList();
            if (missing.Count > 0)
            {
                var meshes = missing.Select(id => (Id: id, Mesh: Meshes.Get(id)!)).ToList();
                var reports = await Task.Run(() => meshes.Select(m => (m.Id, Report: MeshAnalyzer.Analyze(m.Mesh))).ToList()).ConfigureAwait(true);
                foreach (var (id, report) in reports) _reports[id] = report;
            }

            if (version != _repairCheckVersion) return; // the selection changed in the meantime
            ShowBanner(nodes);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            // The check is a convenience; a mesh that cannot be analysed just shows no banner.
            if (version == _repairCheckVersion) SetBanner("", canRepair: false);
        }
    }

    private void ShowBanner(List<MeshNode> nodes)
    {
        var damaged = nodes
            .Where(n => _reports.TryGetValue(n.MeshId, out var r) && !r.Skipped && r.ProblemCount > 0)
            .ToList();
        if (damaged.Count == 0)
        {
            SetBanner("", canRepair: false);
            return;
        }

        var reports = damaged.Select(n => _reports[n.MeshId]).ToArray();
        RebuildProblemList(reports);
        var problems = RepairText.Problems(reports);
        var text = damaged.Count == 1
            ? Texts.Format("Repair.Banner.One", damaged[0].Name, problems)
            : Texts.Format("Repair.Banner.Many", damaged.Count, problems);
        SetBanner(text, canRepair: reports.Any(r => r.HasRepairableProblems));
    }

    private void SetBanner(string text, bool canRepair)
    {
        RepairBannerText = text;
        _canRepair = canRepair;
        RepairMeshCommand.NotifyCanExecuteChanged();
    }

    private bool CanRepairMesh() => _canRepair && !_repairing;

    /// <summary>Repairs the damaged meshes of the selection in one undo step; the old meshes stay available for undo.</summary>
    [RelayCommand(CanExecute = nameof(CanRepairMesh))]
    private async Task RepairMesh()
    {
        var targets = SelectedMeshNodes()
            .Where(n => _reports.TryGetValue(n.MeshId, out var r) && r.HasRepairableProblems && Meshes.Contains(n.MeshId))
            .ToList();
        if (targets.Count == 0) return;

        _repairing = true;
        RepairMeshCommand.NotifyCanExecuteChanged();
        var label = targets.Count == 1 ? targets[0].Name : $"{targets.Count} objects";
        StatusText = Texts.Format("Status.Repairing", label);
        IsBusy = true;
        try
        {
            // objects that share a mesh (copies) are repaired once
            var meshIds = targets.Select(n => n.MeshId).Distinct().ToList();
            var meshes = meshIds.Select(id => (Id: id, Mesh: Meshes.Get(id)!)).ToList();
            var repairKernel = new SharedKernel(_kernel, _evaluatorLock); // the repair may unite overlapping bodies; the kernel is shared with the evaluator
            var results = await Task.Run(() => meshes.Select(m => (m.Id, Result: MeshRepair.Repair(m.Mesh, default, repairKernel))).ToList()).ConfigureAwait(true);

            var commands = new List<IDocumentCommand>();
            var summary = new List<RepairResult>();
            foreach (var (oldId, result) in results)
            {
                if (!result.Changed) continue;
                var newId = Guid.NewGuid();
                Meshes.Add(newId, result.Mesh);
                _reports[newId] = result.After;
                summary.Add(result);
                commands.AddRange(targets.Where(n => n.MeshId == oldId).Select(n => new SetMeshCommand(_document, n, newId, $"Repair {n.Name}")));
                foreach (var n in targets.Where(n => n.MeshId == oldId)) NoteRepair(n.Id, oldId, newId);
            }

            if (commands.Count == 0)
            {
                SetNotice(Texts.Get("Msg.RepairNothing"));
                return;
            }

            ClearProblems();
            _history.Execute(commands.Count == 1 ? commands[0] : new CompositeCommand($"Repair {label}", commands));
            SetNotice(RepairSummary(label, summary));
        }
        finally
        {
            _repairing = false;
            IsBusy = false;
            RepairMeshCommand.NotifyCanExecuteChanged();
            UpdateStatus();
        }
    }

    private static string RepairSummary(string label, List<RepairResult> results)
    {
        var done = RepairText.Actions(new RepairActions(
            results.Sum(r => r.Actions.WeldedVertices), results.Sum(r => r.Actions.RemovedDegenerateTriangles),
            results.Sum(r => r.Actions.RemovedDuplicateTriangles), results.Sum(r => r.Actions.FlippedTriangles),
            results.Sum(r => r.Actions.FilledHoles), results.Sum(r => r.Actions.AddedTriangles), results.Sum(r => r.Actions.UnfilledHoles),
            results.Sum(r => r.Actions.FlippedParts), results.Sum(r => r.Actions.RemovedLooseParts), results.Sum(r => r.Actions.UnitedBodies)));
        var left = RepairText.Problems([.. results.Select(r => r.After)]);
        var end = left.Length == 0 ? Texts.Get("Msg.RepairedFine") : Texts.Format("Msg.RepairedLeft", left);
        return Texts.Format("Msg.Repaired", label, done.Length == 0 ? "-" : done, end);
    }

    // ---- showing the places of a problem (F-REP-01) ------------------------------------------------

    private void RebuildProblemList(MeshReport[] reports)
    {
        RepairProblems.Clear();
        void Add(ProblemKind kind, int count)
        {
            if (count > 0) RepairProblems.Add(new RepairProblemItem(kind, Texts.Get($"Repair.Problem.{kind}"), count) { IsShown = kind == _shownProblem });
        }

        Add(ProblemKind.OpenEdges, reports.Sum(r => r.OpenEdges));
        Add(ProblemKind.NonManifoldEdges, reports.Sum(r => r.NonManifoldEdges));
        Add(ProblemKind.InconsistentEdges, reports.Sum(r => r.InconsistentEdges));
        Add(ProblemKind.DegenerateTriangles, reports.Sum(r => r.DegenerateTriangles));
        Add(ProblemKind.DuplicateTriangles, reports.Sum(r => r.DuplicateTriangles));
        Add(ProblemKind.NonManifoldVertices, reports.Sum(r => r.NonManifoldVertices));
        Add(ProblemKind.SelfIntersections, reports.Sum(r => r.SelfIntersections));
        Add(ProblemKind.DuplicateVertices, reports.Sum(r => r.DuplicateVertices));
        Add(ProblemKind.LooseSpecks, reports.Sum(r => r.TinyLooseParts));
        Add(ProblemKind.InvertedParts, reports.Sum(r => r.InvertedParts));
    }

    /// <summary>Nothing is shown any more and the list is empty (a new selection, a repair).</summary>
    private void ClearProblems()
    {
        _problemVersion++;
        _shownProblem = null;
        ProblemMarks = null;
        RepairProblems.Clear();
    }

    /// <summary>A click on a kind of problem in the banner: its places are shown in the view, a second click hides them.</summary>
    [RelayCommand]
    private void HighlightProblem(RepairProblemItem? item)
    {
        if (item is null) return;
        var show = !item.IsShown;
        foreach (var other in RepairProblems) other.IsShown = false;
        _problemVersion++;
        if (!show)
        {
            _shownProblem = null;
            ProblemMarks = null;
            return;
        }

        item.IsShown = true;
        _shownProblem = item.Kind;
        _repairCheck = Task.WhenAll(_repairCheck, ShowProblemAsync(item.Kind, _problemVersion));
    }

    /// <summary>Finds the places in every damaged mesh of the selection (on a background thread, once per mesh and kind) and shows them where the objects stand.</summary>
    private async Task ShowProblemAsync(ProblemKind kind, int version)
    {
        var nodes = SelectedMeshNodes()
            .Where(n => _reports.TryGetValue(n.MeshId, out var r) && !r.Skipped && r.ProblemCount > 0 && Meshes.Contains(n.MeshId))
            .ToList();
        var missing = nodes.Select(n => n.MeshId).Distinct().Where(id => !_placeCache.ContainsKey((id, kind))).ToList();
        if (missing.Count > 0)
        {
            var meshes = missing.Select(id => (Id: id, Mesh: Meshes.Get(id)!)).ToList();
            var found = await Task.Run(() => meshes.Select(m => (m.Id, Places: MeshAnalyzer.Locate(m.Mesh, kind))).ToList()).ConfigureAwait(true);
            foreach (var (id, places) in found) _placeCache[(id, kind)] = places;
        }

        if (version != _problemVersion) return; // another kind was clicked or the selection changed in the meantime

        var segments = new List<(Vector3, Vector3)>();
        var points = new List<Vector3>();
        var truncated = false;
        foreach (var node in nodes)
        {
            var places = _placeCache[(node.MeshId, kind)];
            var world = TransformOps.LocalToWorld(node);
            segments.AddRange(places.Segments.Select(s => (Vector3.Transform(s.From, world), Vector3.Transform(s.To, world))));
            points.AddRange(places.Points.Select(p => Vector3.Transform(p, world)));
            truncated |= places.Truncated;
        }

        ProblemMarks = new ViewportProblemMarks(segments, points);
        if (truncated) SetNotice(Texts.Format("Msg.ProblemPlacesTruncated", MeshAnalyzer.DefaultPlaceLimit));
    }
}

/// <summary>The boolean kernel of the program with a lock around every call: the evaluator uses the same kernel and it is not thread-safe.</summary>
internal sealed class SharedKernel(Formwerk.Geometry.IGeometryKernel inner, object gate) : Formwerk.Geometry.IGeometryKernel
{
    public string Name => inner.Name;

    public Formwerk.Geometry.MeshData Boolean(BooleanOperation operation, IReadOnlyList<Formwerk.Geometry.MeshData> operands, CancellationToken cancellationToken)
    {
        lock (gate) return inner.Boolean(operation, operands, cancellationToken);
    }
}
