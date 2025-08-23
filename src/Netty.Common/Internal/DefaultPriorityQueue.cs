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

    public DefaultPriorityQueue(IComparer<T> comparer, int initialSize)
    {
        _comparer = ObjectUtil.checkNotNull(comparer, "comparer");
        _items = initialSize != 0
            ? new T[initialSize]
            : Array.Empty<T>();
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

    public bool contains(T o)
    {
        return 0 <= Array.IndexOf(_items, o);
    }

    public void clear()
    {
        _count = 0;
        Array.Clear(_items, 0, 0);
    }

    public void clearIgnoringIndexes()
    {
        _count = 0;
    }


    public bool offer(T e)
    {
        int oldCount = _count;
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
        T result = peek();
        if (result == null)
        {
            return default;
        }

        int newCount = --_count;
        T lastItem = _items[newCount];
        _items[newCount] = default;
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
        int index = Array.IndexOf(_items, item);
        if (index == -1)
        {
            return false;
        }

        _count--;
        if (index == _count)
        {
            _items[index] = default;
        }
        else
        {
            T last = _items[_count];
            _items[_count] = default;
            trickleDown(index, last);
            if (_items[index].Equals(last))
            {
                bubbleUp(index, last);
            }
        }

        return true;
    }

    public void priorityChanged(T item)
    {
        int index = Array.IndexOf(_items, item);
        if (-1 >= index)
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
        _capacity = oldCapacity + (oldCapacity <= 64 ? oldCapacity + 2 : (oldCapacity >> 1));
        var newHeap = new T[_capacity];
        Array.Copy(_items, 0, newHeap, 0, _count);
        _items = newHeap;
    }

    private void trickleDown(int index, T item)
    {
        int middleIndex = _count >> 1;
        while (index < middleIndex)
        {
            int childIndex = (index << 1) + 1;
            T childItem = _items[childIndex];
            int rightChildIndex = childIndex + 1;
            if (rightChildIndex < _count
                && _comparer.Compare(childItem, _items[rightChildIndex]) > 0)
            {
                childIndex = rightChildIndex;
                childItem = _items[rightChildIndex];
            }

            if (_comparer.Compare(item, childItem) <= 0)
            {
                break;
            }

            _items[index] = childItem;
            index = childIndex;
        }

        _items[index] = item;
    }

    private void bubbleUp(int index, T item)
    {
        while (index > 0)
        {
            int parentIndex = (index - 1) >> 1;
            T parentItem = _items[parentIndex];
            if (_comparer.Compare(item, parentItem) >= 0)
            {
                break;
            }

            _items[index] = parentItem;
            index = parentIndex;
        }

        _items[index] = item;
    }

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
        return Arrays.copyOf(_items, _count);
    }
}