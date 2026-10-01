using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Netty.NET.Common.Collections;

public class LinkedHashMap<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> 
    where TKey : notnull
{
    public int Count => _linkedList.Count;
    public bool IsReadOnly => false;

    // todo: performance ..
    public ICollection<TKey> Keys => _linkedList.Select(x => x.Key).ToArray();
    public ICollection<TValue> Values => _linkedList.Select(x => x.Value).ToArray();
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _dictionary;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _linkedList;
    private readonly bool _accessOrder;

    public LinkedHashMap(int capacity = 16, bool accessOrder = false)
    {
        _dictionary = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity);
        _linkedList = new LinkedList<KeyValuePair<TKey, TValue>>();
        _accessOrder = accessOrder;
    }

    public LinkedHashMap(IDictionary<TKey, TValue> dictionary, bool accessOrder = false)
        : this(dictionary?.Count ?? throw new ArgumentNullException(nameof(dictionary)), accessOrder)
    {
        foreach (var pair in dictionary)
        {
            Add(pair.Key, pair.Value);
        }
    }
    public void Clear()
    {
        _dictionary.Clear();
        _linkedList.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return _dictionary.TryGetValue(item.Key, out var node) && EqualityComparer<TValue>.Default.Equals(node.Value.Value, item.Value);
    }

    public bool ContainsKey(TKey key)
    {
        return _dictionary.ContainsKey(key);
    }

    public void Add(KeyValuePair<TKey, TValue> item)
    {
        Add(item.Key, item.Value);
    }

    public void Add(TKey key, TValue value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (_dictionary.TryGetValue(key, out var existingNode))
        {
            if (_accessOrder) _linkedList.Remove(existingNode);
            existingNode.Value = new KeyValuePair<TKey, TValue>(key, value);
            if (_accessOrder) _linkedList.AddLast(existingNode);
        }
        else
        {
            var node = _linkedList.AddLast(new KeyValuePair<TKey, TValue>(key, value));
            _dictionary[key] = node;
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return Contains(item) && Remove(item.Key);
    }

    public bool Remove(TKey key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (_dictionary.Remove(key, out var node))
        {
            _linkedList.Remove(node);
            return true;
        }

        return false;
    }

    public bool TryGetValue(TKey key, out TValue value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        if (_dictionary.TryGetValue(key, out var node))
        {
            if (_accessOrder)
            {
                _linkedList.Remove(node);
                _linkedList.AddLast(node);
            }

            value = node.Value.Value;
            return true;
        }

        value = default;
        return false;
    }

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        if (arrayIndex < 0 || arrayIndex > array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("The destination array is too small.", nameof(array));
        foreach (var pair in this) array[arrayIndex++] = pair;
    }

    public override string ToString()
    {
        return "{" + string.Join(", ", this.Select(pair =>
            pair.Key + "=" + (pair.Value is null ? "null" : pair.Value.ToString()))) + "}";
    }
    public TValue this[TKey key]
    {
        get
        {
            if (TryGetValue(key, out TValue value))
                return value;
            throw new KeyNotFoundException($"The key '{key}' was not found.");
        }
        set => Add(key, value);
    }

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        var node = _linkedList.First;
        while (node != null)
        {
            yield return node.Value;
            node = node.Next;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
