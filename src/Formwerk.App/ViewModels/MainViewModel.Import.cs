using System.Globalization;
using System.IO;
using System.Numerics;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;
using Formwerk.IO;

namespace Formwerk.App.ViewModels;

/// <summary>Importing STL, OBJ and 3MF files (F-IMP-01, F-IMP-02, F-FLR-01).</summary>
public sealed partial class MainViewModel
{
    /// <summary>A model smaller than this or larger than <see cref="MaxPlausibleSizeMm"/> triggers the unit question.</summary>
    private const double MinPlausibleSizeMm = 1;

    private const double MaxPlausibleSizeMm = 1000;

    private readonly SemaphoreSlim _importGate = new(1, 1);

    [RelayCommand]
    private async Task Import()
    {
        var files = _dialogs.PickImportFiles(Texts.Get("Dlg.ImportTitle"), Texts.Get("Dlg.ImportFilter"));
        await ImportFilesAsync(files);
    }

    /// <summary>
    /// Imports the given files one after another (dialog, drag and drop). Unsupported files are reported together; the others
    /// are still imported. The imported objects are selected at the end.
    /// </summary>
    public async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        var files = paths.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        if (files.Count == 0) return;

        var unsupported = files.Where(f => !MeshImport.IsSupported(f)).ToList();
        if (unsupported.Count > 0)
            _dialogs.ShowError(Texts.Format("Dlg.ImportUnsupported", string.Join("\n", unsupported.Select(Path.GetFileName))));

        await _importGate.WaitAsync();
        try
        {
            var imported = new List<Guid>();
            foreach (var file in files.Where(MeshImport.IsSupported))
            {
                if (await ImportOneAsync(file) is { } id) imported.Add(id);
            }

            if (imported.Count > 0) SelectOnly(imported);
        }
        finally
        {
            _importGate.Release();
        }
    }

    private async Task<Guid?> ImportOneAsync(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        StatusText = Texts.Format("Status.Importing", name);
        IsBusy = true;
        try
        {
            IReadOnlyList<ImportedMesh> imported;
            try
            {
                imported = await Task.Run(() => MeshImport.LoadAll(path)).ConfigureAwait(true);
            }
            catch (MeshImportException ex)
            {
                _dialogs.ShowError(Texts.Format("Dlg.ImportFailed", Path.GetFileName(path), ex.Message));
                return null;
            }

            imported = [.. imported.Where(m => !m.Mesh.IsEmpty)];
            if (imported.Count == 0) return null;

            // F-IMP-02: STL and OBJ do not say their unit; sizes outside 1 mm to 1000 mm usually mean another unit.
            // A 3MF file states its unit, so its meshes are already in millimetres.
            var factor = 1.0;
            if (!imported[0].UnitKnown)
            {
                var extent = MeshPlacement.LargestExtent(imported[0].Mesh);
                if (extent < MinPlausibleSizeMm || extent > MaxPlausibleSizeMm)
                {
                    if (_dialogs.PickImportUnit(imported[0].Name, extent) is not { } unit) return null;
                    factor = unit.ToMillimeters();
                }
            }

            // F-FLR-01: every object lands on the plate with its origin at the center of its bottom face, like every generated shape.
            // Several objects (3MF) keep the arrangement they have in the file: each gets the offset to the common bottom center.
            var prepared = await Task.Run(() =>
            {
                var items = new List<(ImportedMesh Source, MeshData Placed, Vector3 BottomCenter)>();
                var min = new Vector3(float.MaxValue);
                var max = new Vector3(float.MinValue);
                foreach (var source in imported)
                {
                    var mesh = factor == 1 ? source.Mesh : MeshPlacement.Scaled(source.Mesh, factor);
                    var b = mesh.ComputeBounds();
                    min = Vector3.Min(min, b.Min);
                    max = Vector3.Max(max, b.Max);
                    items.Add((source, MeshPlacement.PlaceOnPlate(mesh), new Vector3(b.Center.X, b.Center.Y, b.Min.Z)));
                }

                var origin = new Vector3((min.X + max.X) / 2, (min.Y + max.Y) / 2, min.Z);
                var triangles = items.Sum(i => (long)i.Placed.TriangleCount);
                var closed = triangles > 3_000_000 || items.All(i => i.Placed.IsWatertight());
                return (Items: items, Origin: origin, Size: max - min, Triangles: triangles, Closed: closed);
            }).ConfigureAwait(true);

            var spot = FreeSpot(prepared.Size);
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var children = new List<Node>();
            foreach (var (source, placed, bottomCenter) in prepared.Items)
            {
                var id = Guid.NewGuid();
                Meshes.Add(id, placed);
                var childName = prepared.Items.Count == 1 ? UniqueName(source.Name) : UniqueNameAmong(source.Name, usedNames);
                var node = new MeshNode(childName, id) { Transform = Transform.FromPosition(bottomCenter - prepared.Origin) };
                if (source.ColorArgb is { } color) node.Color = 0xFF000000u | color;
                children.Add(node);
            }

            // One object becomes a plain object; several (3MF) become one group, added in one undo step.
            Node top;
            if (children.Count == 1)
            {
                top = children[0];
                top.Transform = Transform.FromPosition(spot);
                _history.Execute(new AddNodeCommand(_document, _document.Root, top));
            }
            else
            {
                top = new GroupNode(UniqueName(name)) { Transform = Transform.FromPosition(spot) };
                var commands = new List<IDocumentCommand> { new AddNodeCommand(_document, _document.Root, top) };
                commands.AddRange(children.Select(child => new AddNodeCommand(_document, top, child)));
                _history.Execute(new CompositeCommand($"Add {top.Name}", commands));
            }

            // The next file looks for a free spot in the evaluated scene, so this object must be part of it first.
            await WaitForEvaluationAsync().ConfigureAwait(true);

            var triangleText = prepared.Triangles.ToString("N0", CultureInfo.InvariantCulture);
            SetNotice(Texts.Format(prepared.Closed ? "Msg.Imported" : "Msg.ImportedOpen", top.Name, triangleText));
            return top.Id;
        }
        finally
        {
            IsBusy = false;
            UpdateStatus();
        }
    }

    /// <summary>A name that is neither used in the project nor among the names already handed out for this import.</summary>
    private string UniqueNameAmong(string baseName, HashSet<string> used)
    {
        var candidate = UniqueName(baseName);
        for (var i = 2; !used.Add(candidate); i++) candidate = UniqueName($"{baseName} {i}");
        return candidate;
    }
}
