namespace Formwerk.Library;

/// <summary>Searching and filtering the library (F-LIB-02).</summary>
public static class LibrarySearch
{
    /// <summary>
    /// The items that match, in the order they were given. Every word of <paramref name="query"/> has to occur (any case) in the name, the
    /// category name or one of the keywords of an item; an empty query matches everything. <paramref name="category"/> keeps one category,
    /// <paramref name="onlyIds"/> keeps only items with these ids (the favorites).
    /// </summary>
    public static IReadOnlyList<LibraryItem> Filter(
        IEnumerable<LibraryItem> items, string? query, LibraryCategory? category = null, IReadOnlySet<string>? onlyIds = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var words = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return [.. items.Where(item =>
            (category is null || item.Category == category)
            && (onlyIds is null || onlyIds.Contains(item.Id))
            && words.All(word => Matches(item, word)))];
    }

    private static bool Matches(LibraryItem item, string word) =>
        item.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
        || item.Category.DisplayName().Contains(word, StringComparison.OrdinalIgnoreCase)
        || item.Keywords.Any(k => k.Contains(word, StringComparison.OrdinalIgnoreCase));
}
