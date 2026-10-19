using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Repair;

namespace Formwerk.App.ViewModels;

/// <summary>Checking selected meshes and repairing them with one click (F-IMP-03, F-REP-01, F-REP-02, F-REP-04).</summary>
public sealed partial class MainViewModel
{
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
            var results = await Task.Run(() => meshes.Select(m => (m.Id, Result: MeshRepair.Repair(m.Mesh))).ToList()).ConfigureAwait(true);

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
            }

            if (commands.Count == 0)
            {
                SetNotice(Texts.Get("Msg.RepairNothing"));
                return;
            }

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
            results.Sum(r => r.Actions.FlippedParts), results.Sum(r => r.Actions.RemovedLooseParts)));
        var left = RepairText.Problems([.. results.Select(r => r.After)]);
        var end = left.Length == 0 ? Texts.Get("Msg.RepairedFine") : Texts.Format("Msg.RepairedLeft", left);
        return Texts.Format("Msg.Repaired", label, done.Length == 0 ? "-" : done, end);
    }
}
