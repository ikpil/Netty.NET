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
using System.Text;
using Netty.NET.Common.Concurrent;
using Netty.NET.Common.Internal.Logging;

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

    private static readonly AtomicInteger nextIndex = new AtomicInteger();

    // Internal use only.
    public static readonly int VARIABLES_TO_REMOVE_INDEX = NextVariableIndex();

    private static readonly int DEFAULT_ARRAY_LIST_INITIAL_CAPACITY = 8;

    private static readonly int ARRAY_LIST_CAPACITY_EXPAND_THRESHOLD = 1 << 30;

    // Reference: https://hg.openjdk.java.net/jdk8/jdk8/jdk/file/tip/src/share/classes/java/util/ArrayList.java#l229
    private const int ARRAY_LIST_CAPACITY_MAX_SIZE = int.MaxValue - 8;

    private static readonly int HANDLER_SHARABLE_CACHE_INITIAL_CAPACITY = 4;
    private static readonly int INDEXED_VARIABLE_TABLE_INITIAL_SIZE = 32;

    private static readonly int STRING_BUILDER_INITIAL_SIZE;
    private static readonly int STRING_BUILDER_MAX_SIZE;

    private static readonly IInternalLogger logger;

    /** Internal use only. */
    public static readonly object UNSET = new object();

    /** Used by {@link FastThreadLocal} */
    private object[] indexedVariables;

    // Core thread-locals
    private int _futureListenerStackDepth;
    private int _localChannelReaderStackDepth;
    private Dictionary<Type, bool> _handlerSharableCache;
    private Dictionary<Type, TypeParameterMatcher> _typeParameterMatcherGetCache;
    private Dictionary<Type, IDictionary<(Type Superclass, string Name), TypeParameterMatcher>> _typeParameterMatcherFindCache;

    // String-related thread-locals
    private StringBuilder _stringBuilder;

    // ArrayList-related thread-locals
    private System.Collections.IList _arrayList;

    /** @deprecated These padding fields will be removed in the future. */
    public long rp1, rp2, rp3, rp4, rp5, rp6, rp7, rp8;

    static InternalThreadLocalMap()
    {
        STRING_BUILDER_INITIAL_SIZE =
            SystemPropertyUtil.GetInt("io.netty.threadLocalMap.stringBuilder.initialSize", 1024);
        STRING_BUILDER_MAX_SIZE =
            SystemPropertyUtil.GetInt("io.netty.threadLocalMap.stringBuilder.maxSize", 1024 * 4);

        // Ensure the InternalLogger is initialized as last field in this class as InternalThreadLocalMap might be used
        // by the InternalLogger itself. For this its important that all the other static fields are correctly
        // initialized.
        //
        // See https://github.com/netty/netty/issues/12931.
        logger = InternalLoggerFactory.GetInstance(typeof(InternalThreadLocalMap));
        logger.Debug("-Dio.netty.threadLocalMap.stringBuilder.initialSize: {}", STRING_BUILDER_INITIAL_SIZE);
        logger.Debug("-Dio.netty.threadLocalMap.stringBuilder.maxSize: {}", STRING_BUILDER_MAX_SIZE);
    }

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
        _slowThreadLocalMap = null;
    }

    public static int NextVariableIndex()
    {
        int index = nextIndex.GetAndIncrement();
        if (index >= ARRAY_LIST_CAPACITY_MAX_SIZE || index < 0)
        {
            nextIndex.Set(ARRAY_LIST_CAPACITY_MAX_SIZE);
            throw new InvalidOperationException("too many thread-local indexed variables");
        }

        return index;
    }

    public static int LastVariableIndex()
    {
        return nextIndex.Get() - 1;
    }


    private static object[] NewIndexedVariableTable()
    {
        object[] array = new object[INDEXED_VARIABLE_TABLE_INITIAL_SIZE];
        Arrays.Fill(array, UNSET);
        return array;
    }

    public int Size()
    {
        int count = 0;

        if (_futureListenerStackDepth != 0)
        {
            count++;
        }

        if (_localChannelReaderStackDepth != 0)
        {
            count++;
        }

        if (_handlerSharableCache != null)
        {
            count++;
        }

        if (_typeParameterMatcherGetCache != null)
        {
            count++;
        }

        if (_typeParameterMatcherFindCache != null)
        {
            count++;
        }

        if (_stringBuilder != null)
        {
            count++;
        }

        if (_arrayList != null)
        {
            count++;
        }

        object v = IndexedVariable(VARIABLES_TO_REMOVE_INDEX);
        if (v != null && v != UNSET)
        {
            //@SuppressWarnings("unchecked")
            System.Collections.ICollection variablesToRemove = (System.Collections.ICollection)v;
            count += variablesToRemove.Count;
        }

        return count;
    }

    public StringBuilder StringBuilder()
    {
        StringBuilder sb = _stringBuilder;
        if (sb == null)
        {
            return _stringBuilder = new StringBuilder(STRING_BUILDER_INITIAL_SIZE);
        }

        if (sb.Capacity > STRING_BUILDER_MAX_SIZE)
        {
            sb.Clear();
            sb.Capacity = STRING_BUILDER_INITIAL_SIZE;
        }

        sb.Length = 0;
        return sb;
    }

    public List<E> ArrayList<E>()
    {
        return ArrayList<E>(DEFAULT_ARRAY_LIST_INITIAL_CAPACITY);
    }

    //@SuppressWarnings("unchecked")
    public List<E> ArrayList<E>(int minCapacity)
    {
        // CLR generic lists cannot share storage across different element types.
        // Clear the old list before replacing it so cached objects are released.
        if (_arrayList is not List<E> list)
        {
            _arrayList?.Clear();
            _arrayList = new List<E>(minCapacity);
            return (List<E>)_arrayList;
        }

        list.Clear();
        list.EnsureCapacity(minCapacity);
        return list;
    }

    public int FutureListenerStackDepth()
    {
        return _futureListenerStackDepth;
    }

    public IntegerHolder CounterHashCode() => new IntegerHolder();

    public void SetCounterHashCode(IntegerHolder counterHashCode)
    {
        // No-op.
    }

    public void SetFutureListenerStackDepth(int futureListenerStackDepth)
    {
        _futureListenerStackDepth = futureListenerStackDepth;
    }

    public IDictionary<Type, TypeParameterMatcher> TypeParameterMatcherGetCache()
    {
        var cache = _typeParameterMatcherGetCache;
        if (cache == null)
        {
            _typeParameterMatcherGetCache = cache = new Dictionary<Type, TypeParameterMatcher>();
        }

        return cache;
    }

    public IDictionary<Type, IDictionary<(Type Superclass, string Name), TypeParameterMatcher>> TypeParameterMatcherFindCache()
    {
        var cache = _typeParameterMatcherFindCache;
        if (cache == null)
        {
            _typeParameterMatcherFindCache = cache = new Dictionary<Type, IDictionary<(Type Superclass, string Name), TypeParameterMatcher>>();
        }

        return cache;
    }

    public IDictionary<Type, bool> HandlerSharableCache()
    {
        var cache = _handlerSharableCache;
        if (cache == null)
        {
            // Start with small capacity to keep memory overhead as low as possible.
            _handlerSharableCache = cache = new Dictionary<Type, bool>(HANDLER_SHARABLE_CACHE_INITIAL_CAPACITY);
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
        int newCapacity;
        if (index < ARRAY_LIST_CAPACITY_EXPAND_THRESHOLD)
        {
            newCapacity = index;
            newCapacity |= newCapacity >>> 1;
            newCapacity |= newCapacity >>> 2;
            newCapacity |= newCapacity >>> 4;
            newCapacity |= newCapacity >>> 8;
            newCapacity |= newCapacity >>> 16;
            newCapacity++;
        }
        else
        {
            newCapacity = ARRAY_LIST_CAPACITY_MAX_SIZE;
        }

        object[] newArray = Arrays.CopyOf(oldArray, newCapacity);
        Arrays.Fill(newArray, oldCapacity, newArray.Length, UNSET);
        newArray[index] = value;
        indexedVariables = newArray;
    }

    public object RemoveIndexedVariable(int index)
    {
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
        object[] lookup = indexedVariables;
        return index < lookup.Length && lookup[index] != UNSET;
    }
}
