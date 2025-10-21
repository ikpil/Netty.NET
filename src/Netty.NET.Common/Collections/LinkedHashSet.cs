using System;
using System.Collections;
using System.Collections.Generic;

namespace Netty.NET.Common.Collections;

public class LinkedHashSet<T> : ISet<T> where T : notnull
{
    private readonly Dictionary<T, LinkedListNode<T>> _dictionary;
    private readonly LinkedList<T> _linkedList;

    public int Count => _linkedList.Count;
    public bool IsReadOnly => false;

    public LinkedHashSet()
        : this(0, comparer: null)
    {
    }

    public LinkedHashSet(int capacity, IEqualityComparer<T>? comparer = null)
    {
        _dictionary = new Dictionary<T, LinkedListNode<T>>(capacity, comparer);
        _linkedList = new LinkedList<T>();
    }

    public LinkedHashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer = null)
        : this(0, comparer)
    {
        foreach (var item in collection)
            Add(item);
    }

    // --- Core operations ---

    public bool Add(T item)
    {
        if (_dictionary.ContainsKey(item))
            return false;

        var node = _linkedList.AddLast(item);
        _dictionary[item] = node;
        return true;
    }

    void ICollection<T>.Add(T item) => Add(item);

    public bool Remove(T item)
    {
        if (!_dictionary.TryGetValue(item, out var node))
            return false;

        _dictionary.Remove(item);
        _linkedList.Remove(node);
        return true;
    }

    public bool Contains(T item) => _dictionary.ContainsKey(item);

    public void Clear()
    {
        _dictionary.Clear();
        _linkedList.Clear();
    }

    public void CopyTo(T[] array, int arrayIndex) => _linkedList.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => _linkedList.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // --- Set operations (optimized) ---

    public void UnionWith(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        // self-union is a no-op; but no special case needed (Add ignores duplicates)
        foreach (var item in other) Add(item);
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));

        // If other is same instance, it's a no-op.
        if (ReferenceEquals(other, this)) return;

        // Build fast lookup of other using same comparer.
        var otherSet = ToLookupSet(other);

        // Iterate nodes safely while removing.
        var node = _linkedList.First;
        while (node != null)
        {
            var next = node.Next;
            if (!otherSet.Contains(node.Value))
            {
                _dictionary.Remove(node.Value);
                _linkedList.Remove(node);
            }

            node = next;
        }
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));

        // A \ A = empty
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (var item in other)
        {
            if (_dictionary.TryGetValue(item, out var node))
            {
                _dictionary.Remove(item);
                _linkedList.Remove(node);
            }
        }
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));

        // A ⊕ A = ∅, but enumerating self while mutating would break.
        if (ReferenceEquals(other, this))
        {
            Clear();
            return;
        }

        foreach (var item in other)
        {
            if (!Remove(item))
                Add(item);
        }
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return true;

        var otherSet = ToLookupSet(other);
        if (Count > otherSet.Count) return false;

        foreach (var item in _dictionary.Keys)
            if (!otherSet.Contains(item))
                return false;

        return true;
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return true;

        foreach (var item in other)
            if (!_dictionary.ContainsKey(item))
                return false;

        return true;
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return false;

        var otherSet = ToLookupSet(other);
        if (Count >= otherSet.Count) return false;

        foreach (var item in _dictionary.Keys)
            if (!otherSet.Contains(item))
                return false;

        return true;
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return false;

        var (allContained, distinctCount) = AllContainedAndDistinctCount(other);
        return allContained && Count > distinctCount;
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return Count > 0;

        foreach (var item in other)
            if (_dictionary.ContainsKey(item))
                return true;

        return false;
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        if (other is null) throw new ArgumentNullException(nameof(other));
        if (ReferenceEquals(other, this)) return true;

        var otherSet = ToLookupSet(other);
        if (Count != otherSet.Count) return false;

        foreach (var item in _dictionary.Keys)
            if (!otherSet.Contains(item))
                return false;

        return true;
    }

    // --- Helpers ---

    private HashSet<T> ToLookupSet(IEnumerable<T> source)
    {
        // Use the same equality comparer as our dictionary for consistency.
        return source as HashSet<T> is { } hs && Equals(hs.Comparer, _dictionary.Comparer)
            ? hs
            : new HashSet<T>(source, _dictionary.Comparer);
    }

    private (bool allContained, int distinctCount) AllContainedAndDistinctCount(IEnumerable<T> source)
    {
        var seen = new HashSet<T>(_dictionary.Comparer);
        foreach (var item in source)
        {
            if (seen.Add(item))
            {
                if (!_dictionary.ContainsKey(item))
                    return (false, seen.Count); // early exit
            }
        }

        return (true, seen.Count);
    }
}