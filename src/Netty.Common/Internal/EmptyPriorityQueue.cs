/*
 * Copyright 2017 The Netty Project
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
using System.Collections.ObjectModel;
using System.Linq;

namespace Netty.NET.Common.Internal;

public sealed class EmptyPriorityQueue<T> : IPriorityQueue<T>
{
    private static readonly EmptyPriorityQueue<T> INSTANCE = new EmptyPriorityQueue<T>();

    private EmptyPriorityQueue()
    {
    }

    /**
     * Returns an unmodifiable empty {@link PriorityQueue}.
     */
    public static EmptyPriorityQueue<T> instance()
    {
        return INSTANCE;
    }

    public bool removeTyped(T node)
    {
        return false;
    }

    public bool containsTyped(T node)
    {
        return false;
    }

    public void priorityChanged(T node)
    {
    }

    public int size()
    {
        return 0;
    }

    public bool isEmpty()
    {
        return true;
    }

    public bool contains(object o)
    {
        return false;
    }

    public bool add(T t)
    {
        return false;
    }

    public bool remove(object o)
    {
        return false;
    }

    public bool containsAll(ICollection<T> c)
    {
        return false;
    }

    public bool addAll(Collection<T> c)
    {
        return false;
    }

    public bool removeAll(Collection<T> c)
    {
        return false;
    }

    public bool retainAll(Collection<T> c)
    {
        return false;
    }

    public void clear()
    {
    }

    public void clearIgnoringIndexes()
    {
    }

    public override bool Equals(object o)
    {
        return o is IPriorityQueue<T> q && q.isEmpty();
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public bool offer(T t)
    {
        return false;
    }

    public T remove()
    {
        throw new InvalidOperationException();
    }

    public T poll()
    {
        return default;
    }

    public T element()
    {
        throw new InvalidOperationException();
    }

    public T peek()
    {
        return default;
    }

    public IEnumerator<T> GetEnumerator()
    {
        return Enumerable.Empty<T>().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }


    public override string ToString()
    {
        return typeof(EmptyPriorityQueue<T>).Name;
    }

    public T[] ToArray()
    {
        return Array.Empty<T>();
    }

    // @Override
    // public <T1> T1[] toArray(T1[] a) {
    //     if (a.length > 0) {
    //         a[0] = null;
    //     }
    //     return a;
    // }
}