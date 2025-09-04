using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Netty.NET.Common.Collections;

public class LinkedHashMap<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
{
    public int Count => _linkedList.Count;
    public bool IsReadOnly => false;

    public ICollection<TKey> Keys => _linkedList.Select(x => x.Key).ToArray();
    public ICollection<TValue> Values => _linkedList.Select(x => x.Value).ToArray();

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
    {
        if (dictionary is LinkedHashMap<TKey, TValue> linkedHashMap)
        {
            _dictionary = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(linkedHashMap._dictionary);
            _linkedList = new LinkedList<KeyValuePair<TKey, TValue>>(linkedHashMap._linkedList);
            _accessOrder = linkedHashMap._accessOrder;
        }
        else
        {
            foreach (var knv in dictionary)
            {
                Add(knv.Key, knv.Value);
            }
        }
    }

    public void Clear()
    {
        _dictionary.Clear();
        _linkedList.Clear();
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        return _dictionary.TryGetValue(item.Key, out var node) && node.Value.Value.Equals(item.Value);
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
            _linkedList.Remove(existingNode);
            existingNode.Value = new KeyValuePair<TKey, TValue>(key, value);
            _linkedList.AddLast(existingNode);
        }
        else
        {
            var node = _linkedList.AddLast(new KeyValuePair<TKey, TValue>(key, value));
            _dictionary[key] = node;
        }
    }

    public bool Remove(KeyValuePair<TKey, TValue> item)
    {
        return Remove(item.Key);
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
        var maxLen = Math.Max(array.Length, arrayIndex + Count);
        using var enumerator = GetEnumerator();
        for (var i = arrayIndex; i < maxLen; i++)
        {
            array[i] = enumerator.Current;
            enumerator.MoveNext();
        }
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