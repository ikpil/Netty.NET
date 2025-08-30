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
using System.IO;

namespace Netty.NET.Common.Internal;

/**
 * A simple list which is recyclable. This implementation does not allow {@code null} elements to be added.
 */
public class RecyclableArrayList
{
    private static readonly int DEFAULT_INITIAL_CAPACITY = 8;

    private static readonly ObjectPool<RecyclableArrayList> RECYCLER = ObjectPool.newPool(
        new AnonymousObjectCreator<RecyclableArrayList>(x => new RecyclableArrayList(x))
    );

    private bool _insertSinceRecycled;
    private readonly List<object> _list;
    private readonly IObjectPoolHandle<RecyclableArrayList> _handle;

    /**
     * Create a new empty {@link RecyclableArrayList} instance with the given capacity.
     */
    public static RecyclableArrayList newInstance(int minCapacity)
    {
        RecyclableArrayList ret = RECYCLER.get();
        ret._list.EnsureCapacity(minCapacity);
        return ret;
    }

    /**
     * Create a new empty {@link RecyclableArrayList} instance
     */
    public static RecyclableArrayList newInstance<T>()
    {
        return newInstance(DEFAULT_INITIAL_CAPACITY);
    }

    private RecyclableArrayList(IObjectPoolHandle<RecyclableArrayList> handle)
        : this(handle, DEFAULT_INITIAL_CAPACITY)
    {
    }

    private RecyclableArrayList(IObjectPoolHandle<RecyclableArrayList> handle, int initialCapacity)
    {
        _handle = handle;
        _list = new List<object>(initialCapacity);
    }

    public bool addAll<T>(Collection<T> c) where T : class
    {
        checkNullElements(c);
        _list.AddRange(c);
        _insertSinceRecycled = true;
        return true;
    }

    public bool addAll<T>(int index, Collection<T> c) where T : class
    {
        checkNullElements(c);
        _list.InsertRange(index, c);
        _insertSinceRecycled = true;
        return true;
    }

    private static void checkNullElements<T>(Collection<T> c) where T : class
    {
        if (c is IList<T> list)
        {
            // produce less garbage
            int size = list.Count;
            for (int i = 0; i < size; i++)
            {
                if (list[i] == null)
                {
                    throw new ArgumentException("c contains null values");
                }
            }
        }
        else
        {
            foreach (object element in c)
            {
                if (element == null)
                {
                    throw new ArgumentException("c contains null values");
                }
            }
        }
    }

    public bool add(object element)
    {
        _list.Add(ObjectUtil.checkNotNull(element, "element"));
        _insertSinceRecycled = true;
        return true;
    }

    public void add(int index, object element)
    {
        _list.Insert(index, ObjectUtil.checkNotNull(element, "element"));
        _insertSinceRecycled = true;
    }

    public object set(int index, object element)
    {
        object old = _list[index];
        _list[index] = ObjectUtil.checkNotNull(element, "element");
        _insertSinceRecycled = true;
        return old;
    }

    /**
     * Returns {@code true} if any elements where added or set. This will be reset once {@link #recycle()} was called.
     */
    public bool insertSinceRecycled()
    {
        return _insertSinceRecycled;
    }

    /**
     * Clear and recycle this instance.
     */
    public bool recycle()
    {
        _list.Clear();
        _insertSinceRecycled = false;
        _handle.recycle(this);
        return true;
    }
}