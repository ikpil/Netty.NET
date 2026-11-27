/*
 * Copyright 2015 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License,
 * version 2.0 (the "License"); you may not use this file except in compliance
 * with the License. You may obtain a copy of the License at:
 *
 *   https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

namespace Netty.NET.Common.Internal;

/**
 * A priority queue which uses natural ordering of elements. Elements are also required to be of type
 * {@link PriorityQueueNode} for the purpose of maintaining the index in the priority queue.
 * @param <T> The object that is maintained in the queue.
 */
// CLR: This heap is for mutable reference nodes with indexed removal. Use the
// BCL PriorityQueue<TElement, TPriority> for ordinary value/immutable entries.
public sealed class DefaultPriorityQueue<T> : IPriorityQueue<T> where T : class
{
    private readonly IComparer<T> _comparator;
    private T[] _queue;
    private int _size;

    public int Count => _size;

    public DefaultPriorityQueue(IComparer<T> comparator, int initialSize)
    {
        _comparator = ObjectUtil.CheckNotNull(comparator, "comparator");
        if (initialSize < 0) throw new ArgumentOutOfRangeException(nameof(initialSize));
        _queue = initialSize != 0
            ? new T[initialSize]
            : Array.Empty<T>();
    }

    public DefaultPriorityQueue() : this(Comparer<T>.Default, 11)
    {
    }

    public int Size()
    {
        return _size;
    }


    public bool IsEmpty()
    {
        return _size == 0;
    }

    public bool TryRemove(T item)
    {
        return Remove(item);
    }

    public bool TryEnqueue(T item)
    {
        return Offer(item);
    }

    public bool TryDequeue(out T item)
    {
        if (_size == 0) { item = default; return false; }
        item = Poll();
        return true;
    }

    public bool TryPeek(out T item)
    {
        item = Peek();
        return _size != 0;
    }

    public bool Contains(T o)
    {
        int index = IndexOf(o);
        // An index identifies ownership of this reference, not a value-equal
        // node whose stored index happens to point at a current member.
        return index >= 0 && index < _size && ReferenceEquals(o, _queue[index]);
    }

    public bool ContainsTyped(T node)
    {
        return Contains(node);
    }

    public bool RemoveTyped(T node)
    {
        return Remove(node);
    }

    public void Clear()
    {
        for (int i = 0; i < _size; i++) SetIndex(_queue[i], -1);
        Array.Clear(_queue, 0, _size);
        _size = 0;
    }

    public void ClearIgnoringIndexes()
    {
        _size = 0;
    }

    public bool Offer(T e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (e is not IPriorityQueueNode<T> node)
            throw new ArgumentException("Element must provide indexed queue membership.", nameof(e));
        if (node.PriorityQueueIndex(this) != -1)
            throw new ArgumentException("e.priorityQueueIndex(): " + node.PriorityQueueIndex(this).ToString(CultureInfo.InvariantCulture) +
                    " (expected: " + (-1).ToString(CultureInfo.InvariantCulture) + ") + e: " + e);
        // Check that the array capacity is enough to hold values by doubling capacity.
        if (_size >= _queue.Length)
        {
            GrowHeap();
        }

        BubbleUp(_size++, e);
        return true;
    }

    public T Poll()
    {
        if (_size == 0)
        {
            return default;
        }

        T result = _queue[0];
        SetIndex(result, -1);
        T last = _queue[--_size];
        _queue[_size] = default;
        if (_size != 0)
        { // Make sure we don't add the last element back.
            BubbleDown(0, last);
        }

        return result;
    }

    public T Peek()
    {
        return IsEmpty() ? default : _queue[0];
    }

    public bool Remove(T node)
    {
        int i = IndexOf(node);
        if (!Contains(node))
        {
            return false;
        }

        SetIndex(node, -1);
        if (--_size == 0 || _size == i)
        {
            // If there are no node left, or this is the last node in the array just remove and return.
            _queue[i] = default;
            return true;
        }

        // Move the last element where node currently lives in the array.
        T moved = _queue[i] = _queue[_size];
        _queue[_size] = default;
        // priorityQueueIndex will be updated below in bubbleUp or bubbleDown

        // Make sure the moved node still preserves the min-heap properties.
        if (_comparator.Compare(node, moved) < 0)
        {
            BubbleDown(i, moved);
        }
        else
        {
            BubbleUp(i, moved);
        }

        return true;
    }

    public void PriorityChanged(T node)
    {
        int i = IndexOf(node);
        if (!Contains(node))
        {
            return;
        }

        // Preserve the min-heap property by comparing the new priority with parents/children in the heap.
        if (i == 0)
        {
            BubbleDown(i, node);
        }
        else
        {
            // Get the parent to see if min-heap properties are violated.
            int iParent = (i - 1) >>> 1;
            T parent = _queue[iParent];
            if (_comparator.Compare(node, parent) < 0)
            {
                BubbleUp(i, node);
            }
            else
            {
                BubbleDown(i, node);
            }
        }
    }

    private void GrowHeap()
    {
        int oldCapacity = _queue.Length;
        // Use a policy which allows for a 0 initial capacity. Same policy as JDK's priority queue, double when
        // "small", then grow by 50% when "large".
        if (oldCapacity == Array.MaxLength) throw new OutOfMemoryException();
        long newCapacity = (long)oldCapacity + (oldCapacity < 64 ? oldCapacity + 2 : (oldCapacity >> 1));
        // Keep capacity owned by the array, so failed allocation cannot leave
        // a second capacity field inconsistent with the actual storage.
        Array.Resize(ref _queue, (int)Math.Min(Array.MaxLength, newCapacity));
    }

    private void BubbleDown(int k, T node)
    {
        int half = _size >>> 1;
        while (k < half)
        {
            // Compare node to the children of index k.
            int iChild = (k << 1) + 1;
            T child = _queue[iChild];

            // Make sure we get the smallest child to compare against.
            int rightChild = iChild + 1;
            if (rightChild < _size && _comparator.Compare(child, _queue[rightChild]) > 0)
            {
                child = _queue[iChild = rightChild];
            }

            // If the bubbleDown node is less than or equal to the smallest child then we will preserve the min-heap
            // property by inserting the bubbleDown node here.
            if (_comparator.Compare(node, child) <= 0)
            {
                break;
            }

            // Bubble the child up.
            _queue[k] = child;
            SetIndex(child, k);

            // Move down k down the tree for the next iteration.
            k = iChild;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _queue[k] = node;
        SetIndex(node, k);
    }

    private void BubbleUp(int k, T node)
    {
        while (k > 0)
        {
            int iParent = (k - 1) >>> 1;
            T parent = _queue[iParent];

            // If the bubbleUp node is less than the parent, then we have found a spot to insert and still maintain
            // min-heap properties.
            if (_comparator.Compare(node, parent) >= 0)
            {
                break;
            }

            // Bubble the parent down.
            _queue[k] = parent;
            SetIndex(parent, k);

            // Move k up the tree for the next iteration.
            k = iParent;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _queue[k] = node;
        SetIndex(node, k);
    }

    private int IndexOf(T item)
    {
        return item is IPriorityQueueNode<T> node
            ? node.PriorityQueueIndex(this) : -1;
    }

    private void SetIndex(T item, int index)
    {
        if (item is IPriorityQueueNode<T> node) node.PriorityQueueIndex(this, index);
    }

    /**
     * This iterator does not return elements in any particular order.
     */
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _size; i++)
        {
            yield return _queue[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public T[] ToArray()
    {
        return Arrays.CopyOf(_queue, _size);
    }
}
