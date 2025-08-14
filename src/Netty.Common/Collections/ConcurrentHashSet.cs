namespace Netty.NET.Common.Collections;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a thread-safe, unordered collection of unique items.
/// </summary>
/// <typeparam name="T">The type of elements in the hash set.</typeparam>
public class ConcurrentHashSet<T> : ISet<T>, IReadOnlyCollection<T> where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary;
    private const byte DummyValue = 0;

    public ConcurrentHashSet()
    {
        _dictionary = new ConcurrentDictionary<T, byte>();
    }

    public ConcurrentHashSet(IEqualityComparer<T> comparer)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(comparer);
    }

    public ConcurrentHashSet(IEnumerable<T> collection)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(collection.Select(item => new KeyValuePair<T, byte>(item, DummyValue)));
    }

    public ConcurrentHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(collection.Select(item => new KeyValuePair<T, byte>(item, DummyValue)), comparer);
    }

    public int Count => _dictionary.Count;

    public bool IsReadOnly => false;

    public bool Add(T item)
    {
        return _dictionary.TryAdd(item, DummyValue);
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    public void Clear()
    {
        _dictionary.Clear();
    }

    public bool Contains(T item)
    {
        return _dictionary.ContainsKey(item);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _dictionary.Keys.CopyTo(array, arrayIndex);
    }

    public bool Remove(T item)
    {
        return _dictionary.TryRemove(item, out _);
    }

    public IEnumerator<T> GetEnumerator()
    {
        return _dictionary.Keys.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public void UnionWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        foreach (var item in other)
        {
            Add(item);
        }
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        var otherSet = new HashSet<T>(other);
        var itemsToRemove = _dictionary.Keys.Where(item => !otherSet.Contains(item)).ToList();

        foreach (var item in itemsToRemove)
        {
            Remove(item);
        }
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        foreach (var item in other)
        {
            Remove(item);
        }
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));

        var otherHashSet = new HashSet<T>(other);
        var keysSnapshot = _dictionary.Keys.ToList();

        foreach (var item in otherHashSet)
        {
            if (keysSnapshot.Contains(item))
            {
                Remove(item);
            }
            else
            {
                Add(item);
            }
        }
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        var otherSet = new HashSet<T>(other);
        return _dictionary.Keys.All(otherSet.Contains);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return other.All(Contains);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        var otherSet = new HashSet<T>(other);
        return _dictionary.Count > otherSet.Count && otherSet.All(Contains);
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        var otherSet = new HashSet<T>(other);
        return _dictionary.Count < otherSet.Count && _dictionary.Keys.All(otherSet.Contains);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        return other.Any(Contains);
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        if (other == null) throw new ArgumentNullException(nameof(other));
        var otherSet = new HashSet<T>(other);
        return _dictionary.Count == otherSet.Count && _dictionary.Keys.All(otherSet.Contains);
    }
}