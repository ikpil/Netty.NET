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
using Netty.NET.Common.Functional;

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
    private readonly IComparer<T> _comparer;
    private int _count;
    private T[] _items;

    public int Count => _count;

    public DefaultPriorityQueue(IComparer<T> comparer, int initialSize)
    {
        _comparer = ObjectUtil.CheckNotNull(comparer, "comparer");
        if (initialSize < 0) throw new ArgumentOutOfRangeException(nameof(initialSize));
        _items = initialSize != 0
            ? new T[initialSize]
            : Array.Empty<T>();
    }

    public DefaultPriorityQueue() : this(Comparer<T>.Default, 11)
    {
    }

    public int Size()
    {
        return _count;
    }


    public bool IsEmpty()
    {
        return _count == 0;
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
        if (_count == 0) { item = default; return false; }
        item = Poll();
        return true;
    }

    public bool TryPeek(out T item)
    {
        item = Peek();
        return _count != 0;
    }

    public bool Contains(T o)
    {
        int index = IndexOf(o);
        // An index identifies ownership of this reference, not a value-equal
        // node whose stored index happens to point at a current member.
        return index >= 0 && index < _count && ReferenceEquals(o, _items[index]);
    }

    public bool ContainsTyped(T node) => Contains(node);
    public bool RemoveTyped(T node) => Remove(node);

    public void Clear()
    {
        for (int i = 0; i < _count; i++) SetIndex(_items[i], -1);
        Array.Clear(_items, 0, _count);
        _count = 0;
    }

    public int Drain(IConsumer<T> consumer, int limit)
    {
        ObjectUtil.CheckNotNull(consumer, nameof(consumer));
        ObjectUtil.CheckPositiveOrZero(limit, nameof(limit));
        int drained = 0;
        while (drained < limit && TryDequeue(out var item))
        {
            consumer.Accept(item);
            drained++;
        }
        return drained;
    }

    public void ClearIgnoringIndexes()
    {
        _count = 0;
    }

    public bool Offer(T e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (e is not IPriorityQueueNode<T> node)
            throw new ArgumentException("Element must provide indexed queue membership.", nameof(e));
        if (node.PriorityQueueIndex(this) != -1)
            throw new ArgumentException("Element already belongs to a priority queue.", nameof(e));
        int oldCount = _count;
        // Check that the array capacity is enough to hold values by doubling capacity.
        if (oldCount == _items.Length)
        {
            GrowHeap();
        }

        _count = oldCount + 1;
        BubbleUp(oldCount, e);

        return true;
    }

    public T Poll()
    {
        if (_count == 0)
        {
            return default;
        }

        T result = _items[0];
        SetIndex(result, -1);
        int newCount = --_count;
        T lastItem = _items[newCount];
        _items[newCount] = default;
        // Make sure we don't add the last element back.
        if (newCount > 0)
        {
            TrickleDown(0, lastItem);
        }

        return result;
    }

    public T Peek()
    {
        return IsEmpty() ? default : _items[0];
    }

    public bool Remove(T item)
    {
        int index = IndexOf(item);
        if (!Contains(item))
        {
            return false;
        }

        SetIndex(item, -1);
        _count--;
        // If there are no node left, or this is the last node in the array just remove and return.
        if (index == _count)
        {
            _items[index] = default;
        }
        else
        {
            // Move the last element where node currently lives in the array.
            T last = _items[_count];
            _items[_count] = default;
            // priorityQueueIndex will be updated below in bubbleUp or bubbleDown
            // Make sure the moved node still preserves the min-heap properties.
            if (_comparer.Compare(item, last) < 0)
            {
                TrickleDown(index, last);
            }
            else BubbleUp(index, last);
        }

        return true;
    }

    public void PriorityChanged(T item)
    {
        int index = IndexOf(item);
        if (!Contains(item))
        {
            return;
        }

        // Preserve the min-heap property by comparing the new priority with parents/children in the heap.
        if (index == 0)
        {
            TrickleDown(index, item);
        }
        else
        {
            // Get the parent to see if min-heap properties are violated.
            int iParent = (index - 1) >>> 1;
            T parent = _items[iParent];
            if (_comparer.Compare(item, parent) < 0)
            {
                BubbleUp(index, item);
            }
            else
            {
                TrickleDown(index, item);
            }
        }
    }

    private void GrowHeap()
    {
        int oldCapacity = _items.Length;
        // Use a policy which allows for a 0 initial capacity. Same policy as JDK's priority queue, double when
        // "small", then grow by 50% when "large".
        if (oldCapacity == Array.MaxLength) throw new OutOfMemoryException();
        long newCapacity = (long)oldCapacity + (oldCapacity < 64 ? oldCapacity + 2 : (oldCapacity >> 1));
        // Keep capacity owned by the array, so failed allocation cannot leave
        // a second capacity field inconsistent with the actual storage.
        Array.Resize(ref _items, (int)Math.Min(Array.MaxLength, newCapacity));
    }

    private void TrickleDown(int index, T item)
    {
        int middleIndex = _count >> 1;
        while (index < middleIndex)
        {
            // Compare node to the children of index k.
            int childIndex = (index << 1) + 1;
            T childItem = _items[childIndex];
            int rightChildIndex = childIndex + 1;
            // Make sure we get the smallest child to compare against.
            if (rightChildIndex < _count
                && _comparer.Compare(childItem, _items[rightChildIndex]) > 0)
            {
                childIndex = rightChildIndex;
                childItem = _items[rightChildIndex];
            }

            // If the bubbleDown node is less than or equal to the smallest child then we will preserve the min-heap
            // property by inserting the bubbleDown node here.
            if (_comparer.Compare(item, childItem) <= 0)
            {
                break;
            }

            // Bubble the child up.
            _items[index] = childItem;
            SetIndex(childItem, index);
            // Move down k down the tree for the next iteration.
            index = childIndex;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _items[index] = item;
        SetIndex(item, index);
    }

    private void BubbleUp(int index, T item)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) >> 1;
            T parentItem = _items[parentIndex];
            // If the bubbleUp node is less than the parent, then we have found a spot to insert and still maintain
            // min-heap properties.
            if (_comparer.Compare(item, parentItem) >= 0)
            {
                break;
            }

            // Bubble the parent down.
            _items[index] = parentItem;
            SetIndex(parentItem, index);
            // Move k up the tree for the next iteration.
            index = parentIndex;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _items[index] = item;
        SetIndex(item, index);
    }

    private int IndexOf(T item) => item is IPriorityQueueNode<T> node
        ? node.PriorityQueueIndex(this) : -1;

    private void SetIndex(T item, int index)
    {
        if (item is IPriorityQueueNode<T> node) node.PriorityQueueIndex(this, index);
    }

    /**
     * This iterator does not return elements in any particular order.
     */
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < _count; i++)
        {
            yield return _items[i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public T[] ToArray()
    {
        return Arrays.CopyOf(_items, _count);
    }
}
