/*
 * Copyright 2014 The Netty Project
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
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using Netty.NET.Common.Concurrent;

namespace Netty.NET.Common.Internal;

/**
 * The internal data structure that stores the thread-local variables for Netty and all {@link FastThreadLocal}s.
 * Note that this class is for internal use only and is subject to change at any time.  Use {@link FastThreadLocal}
 * unless you know what you are doing.
 */
public sealed class InternalThreadLocalMap
{
    [ThreadStatic]
    private static InternalThreadLocalMap _slowThreadLocalMap;

    private static int nextIndex;
    // Initialize the CLR limit before reserving the shared removal-registry index.
    private static readonly int MAX_INDEXED_VARIABLE_COUNT = Array.MaxLength;

    // Internal use only.
    public static readonly int VARIABLES_TO_REMOVE_INDEX = NextVariableIndex();

    private static readonly int INDEXED_VARIABLE_TABLE_INITIAL_SIZE = 32;

    /** Internal use only. */
    public static readonly object UNSET = new object();

    /** Used by {@link FastThreadLocal} */
    private object[] indexedVariables;

    // Core thread-locals
    private int _localChannelReaderStackDepth;
    private ConditionalWeakTable<Type, StrongBox<bool>> _handlerSharableCache;

    private InternalThreadLocalMap()
    {
        indexedVariables = NewIndexedVariableTable();
    }

    public static InternalThreadLocalMap GetIfSet()
    {
        var thread = FastThreadLocalThread.CurrentFastThreadLocalThread();
        return thread == null ? _slowThreadLocalMap : thread.ThreadLocalMap();
    }

    public static InternalThreadLocalMap Get()
    {
        var thread = FastThreadLocalThread.CurrentFastThreadLocalThread();
        if (thread == null) return SlowGet();
        var map = thread.ThreadLocalMap();
        if (map == null) thread.SetThreadLocalMap(map = new InternalThreadLocalMap());
        return map;
    }

    private static InternalThreadLocalMap SlowGet()
    {
        InternalThreadLocalMap ret = _slowThreadLocalMap;
        if (ret == null)
        {
            ret = new InternalThreadLocalMap();
            _slowThreadLocalMap = ret;
        }

        return ret;
    }

    public static void Remove()
    {
        var thread = FastThreadLocalThread.CurrentFastThreadLocalThread();
        if (thread == null) _slowThreadLocalMap = null;
        else thread.SetThreadLocalMap(null);
    }

    public static void Destroy()
    {
        // Match ThreadLocal.remove(): detach only the caller's fallback slot, without removal callbacks.
        _slowThreadLocalMap = null;
    }

    public static int NextVariableIndex()
    {
        while (true)
        {
            int index = Volatile.Read(ref nextIndex);
            if (index >= MAX_INDEXED_VARIABLE_COUNT || index < 0)
                throw new InvalidOperationException("too many thread-local indexed variables");
            // The single non-generic counter never advances past the array bound.
            if (Interlocked.CompareExchange(ref nextIndex, index + 1, index) == index)
                return index;
        }
    }

    public static int LastVariableIndex()
    {
        return Volatile.Read(ref nextIndex) - 1;
    }


    private static object[] NewIndexedVariableTable()
    {
        object[] array = new object[INDEXED_VARIABLE_TABLE_INITIAL_SIZE];
        Array.Fill(array, UNSET);
        return array;
    }

    public int Size()
    {
        int count = 0;

        if (_localChannelReaderStackDepth != 0)
        {
            count++;
        }

        if (_handlerSharableCache != null)
        {
            count++;
        }

        object v = IndexedVariable(VARIABLES_TO_REMOVE_INDEX);
        if (v != null && v != UNSET)
        {
            //@SuppressWarnings("unchecked")
            HashSet<IFastThreadLocal> variablesToRemove = (HashSet<IFastThreadLocal>)v;
            count += variablesToRemove.Count;
        }

        return count;
    }

    public ConditionalWeakTable<Type, StrongBox<bool>> HandlerSharableCache()
    {
        var cache = _handlerSharableCache;
        if (cache == null)
        {
            // Weak identity keys preserve collectible type lifetime; values hold only the cached flag.
            _handlerSharableCache = cache = new ConditionalWeakTable<Type, StrongBox<bool>>();
        }

        return cache;
    }

    public int LocalChannelReaderStackDepth()
    {
        return _localChannelReaderStackDepth;
    }

    public void SetLocalChannelReaderStackDepth(int localChannelReaderStackDepth)
    {
        _localChannelReaderStackDepth = localChannelReaderStackDepth;
    }

    public object IndexedVariable(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        object[] lookup = indexedVariables;
        return index < lookup.Length ? lookup[index] : UNSET;
    }

    /**
     * @return {@code true} if and only if a new thread-local variable has been created
     */
    public bool SetIndexedVariable(int index, object value)
    {
        return GetAndSetIndexedVariable(index, value) == UNSET;
    }

    /**
     * @return {@link InternalThreadLocalMap#UNSET} if and only if a new thread-local variable has been created.
     */
    public object GetAndSetIndexedVariable(int index, object value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        object[] lookup = indexedVariables;
        if (index < lookup.Length)
        {
            object oldValue = lookup[index];
            lookup[index] = value;
            return oldValue;
        }

        ExpandIndexedVariableTableAndSet(index, value);
        return UNSET;
    }

    private void ExpandIndexedVariableTableAndSet(int index, object value)
    {
        object[] oldArray = indexedVariables;
        int oldCapacity = oldArray.Length;
        int newCapacity = IndexedVariableTableCapacity(index);
        // Publish only after native allocation, copy and sentinel initialization succeed.
        object[] newArray = oldArray;
        Array.Resize(ref newArray, newCapacity);
        Array.Fill(newArray, UNSET, oldCapacity, newCapacity - oldCapacity);
        newArray[index] = value;
        indexedVariables = newArray;
    }

    private static int IndexedVariableTableCapacity(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (index >= MAX_INDEXED_VARIABLE_COUNT)
            throw new ArgumentOutOfRangeException(nameof(index));
        uint capacity = BitOperations.RoundUpToPowerOf2((uint)index + 1);
        return (int)Math.Min(capacity, (uint)MAX_INDEXED_VARIABLE_COUNT);
    }

    public object RemoveIndexedVariable(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        object[] lookup = indexedVariables;
        if (index < lookup.Length)
        {
            object v = lookup[index];
            lookup[index] = UNSET;
            return v;
        }
        else
        {
            return UNSET;
        }
    }

    public bool IsIndexedVariableSet(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        object[] lookup = indexedVariables;
        return index < lookup.Length && lookup[index] != UNSET;
    }
}
