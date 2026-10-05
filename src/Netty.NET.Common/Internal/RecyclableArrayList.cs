/*
 * Copyright 2013 The Netty Project
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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Netty.NET.Common.Internal;

/**
 * A simple list which is recyclable. This implementation does not allow {@code null} elements to be added.
 */
public sealed class RecyclableArrayList : Collection<object>
{
    private static readonly int DEFAULT_INITIAL_CAPACITY = 8;

    private static readonly ObjectPool<RecyclableArrayList> RECYCLER = ObjectPool.NewPool(
        new AnonymousObjectCreator<RecyclableArrayList>(x => new RecyclableArrayList(x))
    );

    private bool _insertSinceRecycled;
    private readonly List<object> _list;
    private readonly IObjectPoolHandle<RecyclableArrayList> _handle;

    /**
     * Create a new empty {@link RecyclableArrayList} instance with the given capacity.
     */
    public static RecyclableArrayList NewInstance(int minCapacity)
    {
        // Validate the CLR capacity argument before claiming a pooled instance.
        ArgumentOutOfRangeException.ThrowIfNegative(minCapacity);
        RecyclableArrayList ret = RECYCLER.Get();
        ret._list.EnsureCapacity(minCapacity);
        return ret;
    }

    /**
     * Create a new empty {@link RecyclableArrayList} instance
     */
    public static RecyclableArrayList NewInstance()
    {
        return NewInstance(DEFAULT_INITIAL_CAPACITY);
    }

    private RecyclableArrayList(IObjectPoolHandle<RecyclableArrayList> handle)
        : base(new List<object>(DEFAULT_INITIAL_CAPACITY))
    {
        _handle = handle;
        _list = (List<object>)Items;
    }

    public void AddRange(IEnumerable<object> items)
    {
        object[] snapshot = Snapshot(items);
        _list.AddRange(snapshot);
        if (snapshot.Length != 0) _insertSinceRecycled = true;
    }

    public void InsertRange(int index, IEnumerable<object> items)
    {
        object[] snapshot = Snapshot(items);
        _list.InsertRange(index, snapshot);
        if (snapshot.Length != 0) _insertSinceRecycled = true;
    }

    private static object[] Snapshot(IEnumerable<object> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        // Materialize once before mutation: CLR enumerables may be single-use,
        // throw midway, or alias this list. The snapshot keeps failure atomic.
        object[] snapshot = items.ToArray();
        // produce less garbage
        for (int i = 0; i < snapshot.Length; i++)
        {
            if (snapshot[i] == null) throw new ArgumentException("items contains null values", nameof(items));
        }
        return snapshot;
    }

    protected override void InsertItem(int index, object item)
    {
        // Collection<T> routes both generic and non-generic writes through these
        // hooks, so interface callers cannot bypass the non-null/tracking rules.
        ArgumentNullException.ThrowIfNull(item);
        base.InsertItem(index, item);
        _insertSinceRecycled = true;
    }

    protected override void SetItem(int index, object item)
    {
        ArgumentNullException.ThrowIfNull(item);
        base.SetItem(index, item);
        _insertSinceRecycled = true;
    }

    /**
     * Returns {@code true} if any elements where added or set. This will be reset once {@link #recycle()} was called.
     */
    public bool InsertSinceRecycled => _insertSinceRecycled;

    /**
     * Clear and recycle this instance.
     */
    public void Recycle()
    {
        _list.Clear();
        _insertSinceRecycled = false;
        _handle.Recycle(this);
    }
}
