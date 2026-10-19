using System.Text;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>A template in the folder: its file, description and preview.</summary>
public sealed record TemplateEntry(string Path, TemplateInfo Info, byte[]? Preview);

/// <summary>
/// The folder of saved templates (F-TPL-01, F-TPL-02): by default Documents\Formwerk\Templates. Listing never fails because of
/// one bad file; saving writes atomically and refuses a name that is already taken in the same category.
/// </summary>
public sealed class TemplateStore(string folder)
{
    public static string DefaultFolder { get; } =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Formwerk", "Templates");

    public string Folder { get; } = folder;

    /// <summary>All readable templates, by category (those without one first) and name. Files that are not templates are skipped and left alone.</summary>
    public IReadOnlyList<TemplateEntry> List()
    {
        if (!Directory.Exists(Folder)) return [];

        var entries = new List<TemplateEntry>();
        foreach (var path in Directory.EnumerateFiles(Folder, "*" + TemplateFile.Extension))
        {
            try
            {
                var (info, preview) = TemplateFile.LoadInfo(path);
                entries.Add(new TemplateEntry(path, info, preview));
            }
            catch (Exception ex) when (ex is TemplateFormatException or IOException or UnauthorizedAccessException)
            {
                // not a template, or not readable right now: not worth stopping the list
            }
        }

        return [.. entries.OrderBy(e => e.Info.Category, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Info.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public IReadOnlyList<string> Categories() =>
        [.. List().Select(e => e.Info.Category).Where(c => c.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];

    public bool NameTaken(string name, string category) => List().Any(e => Same(e.Info, name, category));

    /// <summary>The name itself when it is free in the category, otherwise "Name (2)" and so on.</summary>
    public string SuggestName(string name, string category) =>
        TemplateNames.Suggest(name, List().Where(e => string.Equals(e.Info.Category, TemplateNames.Clean(category), StringComparison.OrdinalIgnoreCase)).Select(e => e.Info.Name));

    /// <exception cref="InvalidOperationException">The name is taken in the category (the message offers the next free one).</exception>
    /// <returns>The path of the new file.</returns>
    public string Save(TemplateInfo info, FormwerkDocument document, MeshStore? meshes, byte[]? preview)
    {
        if (TemplateNames.Validate(info.Name) is { } problem) throw new ArgumentException(problem, nameof(info));
        if (NameTaken(info.Name, info.Category))
            throw new InvalidOperationException($"A template named '{TemplateNames.Clean(info.Name)}' already exists in this category. Try '{SuggestName(info.Name, info.Category)}'.");

        Directory.CreateDirectory(Folder);
        var path = System.IO.Path.Combine(Folder, FileNameFor(info.Name));
        TemplateFile.SaveAtomic(path, document, meshes, info, preview);
        return path;
    }

    public void Delete(string path)
    {
        if (string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), System.IO.Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            File.Delete(path);
    }

    private static bool Same(TemplateInfo info, string name, string category) =>
        string.Equals(info.Name, TemplateNames.Clean(name), StringComparison.OrdinalIgnoreCase)
        && string.Equals(info.Category, TemplateNames.Clean(category), StringComparison.OrdinalIgnoreCase);

    /// <summary>A safe file name: letters, digits, blank, dash and underscore of the name, plus a number so that similar names do not collide.</summary>
    private static string FileNameFor(string name)
    {
        var safe = new StringBuilder();
        foreach (var c in TemplateNames.Clean(name))
        {
            if (char.IsLetterOrDigit(c) || c is ' ' or '-' or '_') safe.Append(c);
        }

        var stem = safe.ToString().Trim();
        if (stem.Length > 40) stem = stem[..40].TrimEnd();
        if (stem.Length == 0) stem = "template";
        return stem + "-" + Guid.NewGuid().ToString("N")[..8] + TemplateFile.Extension;
    }
}
