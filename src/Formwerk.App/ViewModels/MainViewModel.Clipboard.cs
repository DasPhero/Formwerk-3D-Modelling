using System.Numerics;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using Formwerk.App.Resources;
using Formwerk.Core.Commands;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.App.ViewModels;

/// <summary>Copy, paste and duplicate; Ctrl+D repeats the last offset (F-CPY-01).</summary>
public sealed partial class MainViewModel
{
    /// <summary>
    /// How far a new copy lands from its original, in world space: the offset of the last move (by handle or by typing a position)
    /// or of the last duplicate. Before any move: 15 mm along X.
    /// </summary>
    private Vector3 _lastOffset = new(15, 0, 0);

    /// <summary>What was copied: a snapshot of the objects (with their children), where they were in the world and the meshes they use.</summary>
    private sealed record ClipboardItem(Node Node, Matrix4x4 World, IReadOnlyDictionary<Guid, MeshData> Meshes);

    private List<ClipboardItem> _clipboard = [];
    private int _pasteCount;

    /// <summary>Called when the user moved an object: the next duplicate or paste uses this offset (a zero move changes nothing).</summary>
    private void RememberOffset(Vector3 worldDelta)
    {
        if (worldDelta.Length() > 1e-4f) _lastOffset = worldDelta;
    }

    // ---- duplicate -------------------------------------------------------------------------------

    /// <summary>
    /// Copies the selected objects next to themselves, moved by the last offset. The copies are selected, so pressing the key
    /// again continues the row at the same distance.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Duplicate()
    {
        var created = new List<Guid>();
        foreach (var node in SelectedTopLevel())
        {
            var copy = node.Clone(newIds: true);
            copy.Name = UniqueName(Texts.Format("Name.Copy", WithoutCopySuffix(node.Name)));
            copy.Transform = TransformOps.MovedBy(node, node.Transform, _lastOffset); // a world offset, whatever the parent's turn
            var parent = node.Parent!;
            _history.Execute(new AddNodeCommand(_document, parent, copy, parent.Children.ToList().IndexOf(node) + 1));
            created.Add(copy.Id);
        }

        SelectOnly(created);
    }

    // ---- copy and paste --------------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Copy()
    {
        var nodes = SelectedTopLevel();
        if (nodes.Count == 0) return;

        _clipboard = [.. nodes.Select(n => new ClipboardItem(n.Clone(newIds: false), TransformOps.LocalToWorld(n), MeshesOf(n)))];
        _pasteCount = 0;
        PasteCommand.NotifyCanExecuteChanged();
        SetNotice(Texts.Format("Msg.Copied", nodes.Count == 1 ? nodes[0].Name : $"{nodes.Count} objects"));
    }

    private bool CanPaste() => _clipboard.Count > 0;

    /// <summary>
    /// Puts copies of the copied objects into the scene, at the place they had in the world plus the last offset; pasting again moves
    /// one more offset on, so repeated pastes make a row. Works after New and Open too (the meshes of imported objects come along).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPaste))]
    private void Paste()
    {
        if (_clipboard.Count == 0) return;
        _pasteCount++;
        var shift = Matrix4x4.CreateTranslation(_lastOffset * _pasteCount);

        var commands = new List<IDocumentCommand>();
        var created = new List<Guid>();
        foreach (var item in _clipboard)
        {
            foreach (var (id, mesh) in item.Meshes)
            {
                if (!Meshes.Contains(id)) Meshes.Add(id, mesh); // the store was emptied by New or Open
            }

            var copy = item.Node.Clone(newIds: true);
            copy.Name = UniqueName(Texts.Format("Name.Copy", WithoutCopySuffix(item.Node.Name)));
            var world = item.World * shift;
            copy.Transform = Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation)
                ? new Transform(translation, Quaternion.Normalize(rotation), scale)
                : Transform.FromPosition(translation);
            commands.Add(new AddNodeCommand(_document, _document.Root, copy));
            created.Add(copy.Id);
        }

        var label = commands.Count == 1 ? _clipboard[0].Node.Name : $"{commands.Count} objects";
        _history.Execute(new CompositeCommand($"Paste {label}", commands));
        SelectOnly(created);
    }

    /// <summary>The meshes that the object and everything inside it use, so that a paste still has them after New or Open.</summary>
    private IReadOnlyDictionary<Guid, MeshData> MeshesOf(Node node)
    {
        var result = new Dictionary<Guid, MeshData>();
        foreach (var mesh in node.Descendants().Prepend(node).OfType<MeshNode>())
        {
            if (Meshes.Get(mesh.MeshId) is { } data) result[mesh.MeshId] = data;
        }

        return result;
    }

    /// <summary>"Box copy" and "Box copy 3" are copies of "Box", so that a copy of a copy is "Box copy 2" and not "Box copy copy".</summary>
    private static string WithoutCopySuffix(string name)
    {
        var suffix = Regex.Escape(Texts.Format("Name.Copy", ""));
        var match = Regex.Match(name, "^(?<base>.+?)" + suffix + @"(?: \d+)?$");
        return match.Success ? match.Groups["base"].Value : name;
    }
}
