using System.Collections;

namespace Formwerk.App.ViewModels;

/// <summary>
/// A set of ids that remembers the order in which they were added: the selection (F-SNP-04 needs "the first selected object").
/// Adding an id that is in the set already keeps its place.
/// </summary>
public sealed class OrderedIdSet : IReadOnlyCollection<Guid>
{
    private readonly List<Guid> _items = [];
    private readonly HashSet<Guid> _lookup = [];

    public int Count => _items.Count;

    public bool Contains(Guid id) => _lookup.Contains(id);

    public bool Add(Guid id)
    {
        if (!_lookup.Add(id)) return false;
        _items.Add(id);
        return true;
    }

    public bool Remove(Guid id)
    {
        if (!_lookup.Remove(id)) return false;
        _items.Remove(id);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _lookup.Clear();
    }

    /// <summary>Removes all ids for which <paramref name="match"/> is true; returns how many.</summary>
    public int RemoveWhere(Predicate<Guid> match)
    {
        var removed = _items.Where(id => match(id)).ToList();
        foreach (var id in removed) Remove(id);
        return removed.Count;
    }

    public IEnumerator<Guid> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
