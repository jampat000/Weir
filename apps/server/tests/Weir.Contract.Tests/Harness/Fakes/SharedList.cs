namespace Weir.Contract.Tests.Harness.Fakes;

/// <summary>
/// A list a test edits while the fake server's request threads read it. Every operation takes the lock;
/// read it through <see cref="Snapshot"/>.
/// </summary>
public sealed class SharedList<T>
{
    private readonly object _gate = new();
    private readonly List<T> _items = [];

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void Add(T item)
    {
        lock (_gate)
        {
            _items.Add(item);
        }
    }

    /// <summary>Makes the list hold exactly these items.</summary>
    public void Replace(IEnumerable<T> items)
    {
        lock (_gate)
        {
            var replacement = items.ToList();
            _items.Clear();
            _items.AddRange(replacement);
        }
    }

    /// <summary>Removes every item that satisfies <paramref name="matches"/>; returns how many went.</summary>
    public int RemoveAll(Predicate<T> matches)
    {
        lock (_gate)
        {
            return _items.RemoveAll(matches);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_gate)
        {
            return [.. _items];
        }
    }
}
