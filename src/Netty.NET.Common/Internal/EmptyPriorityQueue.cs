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
    // CLR generic types cannot share Java's erased, unchecked-cast singleton.
    // Each closed T has one immutable instance; queues use object identity.
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

    public override string ToString()
    {
        return nameof(EmptyPriorityQueue<T>);
    }
}
