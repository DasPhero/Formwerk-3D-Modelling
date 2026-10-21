using Formwerk.Core.Model;

namespace Formwerk.IO;

/// <summary>Finds the object that a <see cref="SharedParameter"/> belongs to (F-TPL-06).</summary>
public static class TemplateShared
{
    /// <summary>
    /// The object at the path of <paramref name="shared"/> in the scene of <paramref name="document"/>, or null when there is none or it
    /// has no parameters (a group). The parameter name is not checked here: only the shape knows which names exist.
    /// </summary>
    public static ParametricNode? Find(FormwerkDocument document, SharedParameter shared) => Find(document.Root, shared.Path, 0) as ParametricNode;

    /// <summary>The object at <paramref name="path"/> below <paramref name="start"/>, beginning with step <paramref name="from"/>; null when there is none.</summary>
    public static Node? Find(Node start, IReadOnlyList<int> path, int from)
    {
        var node = start;
        for (var i = from; i < path.Count; i++)
        {
            if (path[i] < 0 || path[i] >= node.Children.Count) return null;
            node = node.Children[path[i]];
        }

        return node;
    }
}