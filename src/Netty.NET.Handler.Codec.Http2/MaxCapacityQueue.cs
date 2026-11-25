/*
 * Copyright 2019 The Netty Project
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

namespace Netty.NET.Handler.Codec.Http2;

// CLR: java.util.Queue has no BCL interface counterpart. The wrapped Queue<E> is the BCL Queue<T>, the counterpart of
// the ArrayDeque the callers pass (docs/handler-porting.md: "ArrayDeque is Queue<T>; peek/poll are TryPeek/TryDequeue,
// and remove() is Dequeue(), which also throws when the queue is empty"). The Queue/Collection methods keep their Java
// names (Add, Offer, Remove(), Poll, Element, Peek, Size, IsEmpty, Contains, ToArray, Remove(object), ContainsAll,
// AddAll, RemoveAll, RetainAll, Clear) and delegate to the wrapped queue; iterator() is GetEnumerator() of
// IEnumerable<E> (Queue extends Iterable). The BCL Queue<T> cannot remove an arbitrary element, so remove(Object),
// removeAll and retainAll rebuild it in order with the elements that stay (ArrayDeque removes the first occurrence and
// keeps the order of the others). Collection<?> is the non-generic ICollection and Collection<? extends E> the covariant
// IReadOnlyCollection<E> (docs/codec-http-porting.md, Shared CLR adaptations). IllegalStateException is
// InvalidOperationException. Object[] toArray() is object[] ToArray() and <T> T[] toArray(T[]) keeps the
// Collection.toArray(T[]) contract: the given array when it is large enough (null after the last element), else a new
// array.
internal sealed class MaxCapacityQueue<E> : IEnumerable<E>
{
    private readonly Queue<E> queue;
    private readonly int maxCapacity;

    internal MaxCapacityQueue(Queue<E> queue, int maxCapacity)
    {
        this.queue = queue;
        this.maxCapacity = maxCapacity;
    }

    public bool Add(E element)
    {
        if (Offer(element))
        {
            return true;
        }
        throw new InvalidOperationException();
    }

    public bool Offer(E element)
    {
        if (maxCapacity <= queue.Count)
        {
            return false;
        }
        queue.Enqueue(element);
        return true;
    }

    public E Remove()
    {
        return queue.Dequeue();
    }

    public E Poll()
    {
        return queue.TryDequeue(out E element) ? element : default;
    }

    public E Element()
    {
        return queue.Peek();
    }

    public E Peek()
    {
        return queue.TryPeek(out E element) ? element : default;
    }

    public int Size()
    {
        return queue.Count;
    }

    public bool IsEmpty()
    {
        return queue.Count == 0;
    }

    public bool Contains(object o)
    {
        return o is E e && queue.Contains(e);
    }

    public IEnumerator<E> GetEnumerator()
    {
        return queue.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public object[] ToArray()
    {
        object[] array = new object[queue.Count];
        int i = 0;
        foreach (E e in queue)
        {
            array[i++] = e;
        }
        return array;
    }

    public T[] ToArray<T>(T[] a)
    {
        int size = queue.Count;
        T[] array = a.Length >= size ? a : new T[size];
        int i = 0;
        foreach (E e in queue)
        {
            array[i++] = (T) (object) e;
        }
        if (array.Length > size)
        {
            array[size] = default;
        }
        return array;
    }

    public bool Remove(object o)
    {
        bool removed = false;
        return Rebuild(e => !removed && Equals(e, o) && (removed = true));
    }

    public bool ContainsAll(ICollection c)
    {
        foreach (object o in c)
        {
            if (!Contains(o))
            {
                return false;
            }
        }
        return true;
    }

    public bool AddAll(IReadOnlyCollection<E> c)
    {
        if (maxCapacity >= Size() + c.Count)
        {
            bool modified = false;
            foreach (E e in c)
            {
                queue.Enqueue(e);
                modified = true;
            }
            return modified;
        }
        throw new InvalidOperationException();
    }

    public bool RemoveAll(ICollection c)
    {
        return Rebuild(e => ContainsElement(c, e));
    }

    public bool RetainAll(ICollection c)
    {
        return Rebuild(e => !ContainsElement(c, e));
    }

    public void Clear()
    {
        queue.Clear();
    }

    // CLR: removes the elements for which drop returns true and keeps the others in order; true if any was removed.
    private bool Rebuild(Func<E, bool> drop)
    {
        int count = queue.Count;
        bool modified = false;
        for (int i = 0; i < count; i++)
        {
            E e = queue.Dequeue();
            if (drop(e))
            {
                modified = true;
            }
            else
            {
                queue.Enqueue(e);
            }
        }
        return modified;
    }

    private static bool ContainsElement(ICollection c, E e)
    {
        foreach (object o in c)
        {
            if (Equals(o, e))
            {
                return true;
            }
        }
        return false;
    }
}
