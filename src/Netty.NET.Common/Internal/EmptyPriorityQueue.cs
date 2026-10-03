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
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Internal;

public sealed class EmptyPriorityQueue<T> : IPriorityQueue<T>
{
    private static readonly EmptyPriorityQueue<T> INSTANCE = new EmptyPriorityQueue<T>();

    public int Count => 0;

    private EmptyPriorityQueue()
    {
    }

    /**
     * Returns an unmodifiable empty {@link PriorityQueue}.
     */
    public static EmptyPriorityQueue<T> Instance()
    {
        return INSTANCE;
    }

    public bool Remove(T node)
    {
        return false;
    }

    public bool Contains(T node)
    {
        return false;
    }

    public void PriorityChanged(T node)
    {
    }

    public int Size()
    {
        return 0;
    }

    public bool IsEmpty()
    {
        return true;
    }

    public T[] ToArray()
    {
        return Array.Empty<T>();
    }

    public void ClearIgnoringIndexes()
    {
    }

    public override bool Equals(object o)
    {
        return o is IPriorityQueue<T> q && q.IsEmpty();
    }

    public override int GetHashCode()
    {
        return 0;
    }

    public bool Offer(T t)
    {
        return false;
    }

    public T Remove()
    {
        throw new InvalidOperationException();
    }

    public T Poll()
    {
        return default;
    }

    public T Element()
    {
        throw new InvalidOperationException();
    }

    public T Peek()
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

    public bool TryRemove(T item)
    {
        return false;
    }

    public bool TryEnqueue(T item)
    {
        return false;
    }

    public bool TryDequeue(out T item)
    {
        item = default;
        return false;
    }

    public bool TryPeek(out T item)
    {
        item = default;
        return false;
    }

    public void Clear()
    {
    }

    public int Drain(IConsumer<T> consumer, int limit)
    {
        return 0;
    }

    public override string ToString()
    {
        return typeof(EmptyPriorityQueue<T>).Name;
    }
}
