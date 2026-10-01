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
public sealed class DefaultPriorityQueue<T> : IPriorityQueue<T>
{
    private readonly IComparer<T> _comparer;
    private int _count;
    private int _capacity;
    private T[] _items;

    public int Count => _count;

    public DefaultPriorityQueue(IComparer<T> comparer, int initialSize)
    {
        _comparer = ObjectUtil.checkNotNull(comparer, "comparer");
        _items = initialSize != 0
            ? new T[initialSize]
            : Array.Empty<T>();
        _capacity = _items.Length;
    }

    public DefaultPriorityQueue() : this(Comparer<T>.Default, 11)
    {
    }

    public int size()
    {
        return _count;
    }


    public bool isEmpty()
    {
        return _count == 0;
    }

    public bool tryRemove(T item)
    {
        return remove(item);
    }

    public bool tryEnqueue(T item)
    {
        return offer(item);
    }

    public bool tryDequeue(out T item)
    {
        if (_count == 0) { item = default; return false; }
        item = poll();
        return true;
    }

    public bool tryPeek(out T item)
    {
        item = peek();
        return _count != 0;
    }

    public bool contains(T o)
    {
        int index = indexOf(o);
        return index >= 0 && index < _count && EqualityComparer<T>.Default.Equals(o, _items[index]);
    }

    public bool containsTyped(T node) => contains(node);
    public bool removeTyped(T node) => remove(node);

    public void clear()
    {
        for (int i = 0; i < _count; i++) setIndex(_items[i], -1);
        Array.Clear(_items, 0, _count);
        _count = 0;
    }

    public int drain(IConsumer<T> consumer, int limit)
    {
        ObjectUtil.checkNotNull(consumer, nameof(consumer));
        ObjectUtil.checkPositiveOrZero(limit, nameof(limit));
        int drained = 0;
        while (drained < limit && tryDequeue(out var item))
        {
            consumer.accept(item);
            drained++;
        }
        return drained;
    }

    public void clearIgnoringIndexes()
    {
        _count = 0;
    }

    public bool offer(T e)
    {
        if (e == null) throw new ArgumentNullException(nameof(e));
        if (e is IPriorityQueueNode<T> node && node.priorityQueueIndex(this) != -1)
            throw new ArgumentException("Element already belongs to a priority queue.", nameof(e));
        int oldCount = _count;
        // Check that the array capacity is enough to hold values by doubling capacity.
        if (oldCount == _capacity)
        {
            growHeap();
        }

        _count = oldCount + 1;
        bubbleUp(oldCount, e);

        return true;
    }

    public T poll()
    {
        if (_count == 0)
        {
            return default;
        }

        T result = _items[0];
        setIndex(result, -1);
        int newCount = --_count;
        T lastItem = _items[newCount];
        _items[newCount] = default;
        // Make sure we don't add the last element back.
        if (newCount > 0)
        {
            trickleDown(0, lastItem);
        }

        return result;
    }

    public T peek()
    {
        return isEmpty() ? default : _items[0];
    }

    public bool remove(T item)
    {
        int index = indexOf(item);
        if (!contains(item))
        {
            return false;
        }

        setIndex(item, -1);
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
                trickleDown(index, last);
            }
            else bubbleUp(index, last);
        }

        return true;
    }

    public void priorityChanged(T item)
    {
        int index = indexOf(item);
        if (!contains(item))
        {
            return;
        }

        // Preserve the min-heap property by comparing the new priority with parents/children in the heap.
        if (index == 0)
        {
            trickleDown(index, item);
        }
        else
        {
            // Get the parent to see if min-heap properties are violated.
            int iParent = (index - 1) >>> 1;
            T parent = _items[iParent];
            if (_comparer.Compare(item, parent) < 0)
            {
                bubbleUp(index, item);
            }
            else
            {
                trickleDown(index, item);
            }
        }
    }

    private void growHeap()
    {
        int oldCapacity = _capacity;
        // Use a policy which allows for a 0 initial capacity. Same policy as JDK's priority queue, double when
        // "small", then grow by 50% when "large".
        _capacity = oldCapacity + (oldCapacity < 64 ? oldCapacity + 2 : (oldCapacity >> 1));
        var newHeap = new T[_capacity];
        Array.Copy(_items, 0, newHeap, 0, _count);
        _items = newHeap;
    }

    private void trickleDown(int index, T item)
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
            setIndex(childItem, index);
            // Move down k down the tree for the next iteration.
            index = childIndex;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _items[index] = item;
        setIndex(item, index);
    }

    private void bubbleUp(int index, T item)
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
            setIndex(parentItem, index);
            // Move k up the tree for the next iteration.
            index = parentIndex;
        }

        // We have found where node should live and still satisfy the min-heap property, so put it in the queue.
        _items[index] = item;
        setIndex(item, index);
    }

    private int indexOf(T item) => item is IPriorityQueueNode<T> node
        ? node.priorityQueueIndex(this) : Array.IndexOf(_items, item, 0, _count);

    private void setIndex(T item, int index)
    {
        if (item is IPriorityQueueNode<T> node) node.priorityQueueIndex(this, index);
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

    public T[] toArray()
    {
        return Arrays.copyOf(_items, _count);
    }
}
