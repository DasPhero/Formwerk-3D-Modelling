using System.Numerics;
using System.Text;
using Formwerk.Core.Model;
using Formwerk.Geometry;

namespace Formwerk.IO;

/// <summary>A template in the folder: its file, description and preview.</summary>
public sealed record TemplateEntry(string Path, TemplateInfo Info, byte[]? Preview);

/// <summary>What happened when a template file was imported: where it is now, how it is called, and whether its name had to change.</summary>
public sealed record TemplateImportResult(string Path, TemplateInfo Info, bool Renamed);

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

    /// <summary>Like <see cref="NameTaken(string,string)"/>, but the template at <paramref name="exceptPath"/> does not count (it is the one being edited).</summary>
    public bool NameTaken(string name, string category, string exceptPath)
    {
        var except = System.IO.Path.GetFullPath(exceptPath);
        return List().Any(e => !string.Equals(System.IO.Path.GetFullPath(e.Path), except, StringComparison.OrdinalIgnoreCase) && Same(e.Info, name, category));
    }

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

    // ---- managing and exchanging (F-TPL-04, F-TPL-07) ------------------------------------------------

    /// <summary>
    /// Changes the name, description, category and tags of a template (and its size and date as given) without touching its objects
    /// and preview. The file stays where it is. A name is refused when ANOTHER template has it in the category.
    /// </summary>
    /// <exception cref="ArgumentException">The name is not valid, or the file is not in the library folder.</exception>
    /// <exception cref="InvalidOperationException">Another template has this name in the category (the message offers the next free one).</exception>
    public void UpdateInfo(string path, TemplateInfo info)
    {
        EnsureInFolder(path);
        if (TemplateNames.Validate(info.Name) is { } problem) throw new ArgumentException(problem, nameof(info));

        var full = System.IO.Path.GetFullPath(path);
        if (List().Any(e => !string.Equals(System.IO.Path.GetFullPath(e.Path), full, StringComparison.OrdinalIgnoreCase) && Same(e.Info, info.Name, info.Category)))
            throw new InvalidOperationException($"A template named '{TemplateNames.Clean(info.Name)}' already exists in this category. Try '{SuggestName(info.Name, info.Category)}'.");

        TemplateFile.ReplaceInfo(path, info with { Version = TemplateFile.LoadInfo(path).Info.Version }); // renaming is no new version
    }

    /// <summary>
    /// Saves a new version of a template (F-TPL-05): the objects, size and picture are replaced, the version number goes up by one;
    /// name, description, category, tags and creation date stay. The file stays where it is and is replaced atomically. A
    /// <paramref name="preview"/> of null leaves the template without a picture (the old one would show the old shape).
    /// </summary>
    /// <exception cref="ArgumentException">The file is not in the library folder.</exception>
    /// <exception cref="TemplateFormatException">The existing file is not a sound template (it is left as it is).</exception>
    /// <exception cref="IOException">The file cannot be read or written (a missing file is not created).</exception>
    /// <returns>The description that was written.</returns>
    public TemplateInfo Update(string path, FormwerkDocument document, MeshStore? meshes, Vector3 size, byte[]? preview)
    {
        EnsureInFolder(path);
        var (old, _) = TemplateFile.LoadInfo(path);
        var info = old with { Size = size, Version = Math.Min(old.Version + 1, TemplateLimits.MaxVersion) };
        TemplateFile.SaveAtomic(path, document, meshes, info, preview);
        return info;
    }

    /// <summary>A copy of a template under the next free name of its category ("Name (2)"). Returns the path of the copy.</summary>
    public string Duplicate(string path)
    {
        EnsureInFolder(path);
        var (info, _) = TemplateFile.LoadInfo(path);
        var name = SuggestName(info.Name, info.Category);
        var target = System.IO.Path.Combine(Folder, FileNameFor(name));
        TemplateFile.SaveCopy(path, target, info with { Name = name, CreatedUtc = DateTime.UtcNow });
        return target;
    }

    /// <summary>Writes a template of the library as a single <c>.fwt</c> file to <paramref name="destination"/> (atomically, an existing file is replaced).</summary>
    public void Export(string path, string destination)
    {
        EnsureInFolder(path);
        TemplateFile.LoadInfo(path); // only a sound template leaves the library
        ProjectFile.SaveAtomic(destination, stream =>
        {
            using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            source.CopyTo(stream);
        });
    }

    /// <summary>
    /// Takes a template file from anywhere into the library. The file is checked like any template (the description and the objects must
    /// be sound); when its name is taken in its category it comes in under the next free name. Nothing is added when the file is bad.
    /// </summary>
    /// <exception cref="FileNotFoundException">There is no such file.</exception>
    /// <exception cref="TemplateFormatException">The file is not a sound template.</exception>
    public TemplateImportResult Import(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("The file does not exist.", sourcePath);
        var length = new FileInfo(sourcePath).Length;
        if (length > ProjectFile.Limits.MaxFileBytes + TemplateLimits.MaxPreviewBytes + TemplateLimits.MaxInfoBytes)
            throw new TemplateFormatException("The file is too large to be a template.");

        var (info, _) = TemplateFile.LoadInfo(sourcePath);
        TemplateFile.Load(sourcePath); // the objects must be readable, too: a damaged file must not get into the library

        var name = SuggestName(info.Name, info.Category);
        var final = info with { Name = name };
        Directory.CreateDirectory(Folder);
        var target = System.IO.Path.Combine(Folder, FileNameFor(name));
        TemplateFile.SaveCopy(sourcePath, target, final);
        return new TemplateImportResult(target, final, name != TemplateNames.Clean(info.Name));
    }

    private void EnsureInFolder(string path)
    {
        if (!string.Equals(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)), System.IO.Path.GetFullPath(Folder), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The file is not in the template folder.", nameof(path));
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
