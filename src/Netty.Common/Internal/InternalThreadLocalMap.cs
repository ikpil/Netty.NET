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
internal sealed class InternalThreadLocalMap
{
    [ThreadStatic] private static InternalThreadLocalMap _slowThreadLocalMap;

    private static readonly AtomicInteger nextIndex = new AtomicInteger();

    // Internal use only.
    public static readonly int VARIABLES_TO_REMOVE_INDEX = nextVariableIndex();

    private static readonly int DEFAULT_ARRAY_LIST_INITIAL_CAPACITY = 8;

    private static readonly int ARRAY_LIST_CAPACITY_EXPAND_THRESHOLD = 1 << 30;

    // Reference: https://hg.openjdk.java.net/jdk8/jdk8/jdk/file/tip/src/share/classes/java/util/List.java#l229
    private static readonly int ARRAY_LIST_CAPACITY_MAX_SIZE = int.MaxValue - 8;

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
    private Dictionary<Type, IDictionary<string, TypeParameterMatcher>> _typeParameterMatcherFindCache;

    // string-related thread-locals
    private StringBuilder _stringBuilder;
    private Dictionary<Encoding, Encoder> _charsetEncoderCache;
    private Dictionary<Encoding, Decoder> _charsetDecoderCache;

    // List-related thread-locals
    private System.Collections.IList _arrayList;

    /** @deprecated These padding fields will be removed in the future. */
    public long rp1, rp2, rp3, rp4, rp5, rp6, rp7, rp8;

    static InternalThreadLocalMap()
    {
        STRING_BUILDER_INITIAL_SIZE =
            SystemPropertyUtil.getInt("io.netty.threadLocalMap.stringBuilder.initialSize", 1024);
        STRING_BUILDER_MAX_SIZE =
            SystemPropertyUtil.getInt("io.netty.threadLocalMap.stringBuilder.maxSize", 1024 * 4);

        // Ensure the InternalLogger is initialized as last field in this class as InternalThreadLocalMap might be used
        // by the InternalLogger itself. For this its important that all the other static fields are correctly
        // initialized.
        //
        // See https://github.com/netty/netty/issues/12931.
        logger = InternalLoggerFactory.getInstance(typeof(InternalThreadLocalMap));
        logger.debug("-Dio.netty.threadLocalMap.stringBuilder.initialSize: {}", STRING_BUILDER_INITIAL_SIZE);
        logger.debug("-Dio.netty.threadLocalMap.stringBuilder.maxSize: {}", STRING_BUILDER_MAX_SIZE);
    }

    private InternalThreadLocalMap()
    {
        indexedVariables = newIndexedVariableTable();
    }

    public static InternalThreadLocalMap getIfSet()
    {
        return _slowThreadLocalMap;
    }

    public static InternalThreadLocalMap get()
    {
        return slowGet();
    }

    private static InternalThreadLocalMap slowGet()
    {
        InternalThreadLocalMap ret = _slowThreadLocalMap;
        if (ret == null)
        {
            ret = new InternalThreadLocalMap();
            _slowThreadLocalMap = ret;
        }

        return ret;
    }

    public static void remove()
    {
        _slowThreadLocalMap = null;
    }

    public static void destroy()
    {
        _slowThreadLocalMap = null;
    }

    public static int nextVariableIndex()
    {
        int index = nextIndex.getAndIncrement();
        if (index >= ARRAY_LIST_CAPACITY_MAX_SIZE || index < 0)
        {
            nextIndex.set(ARRAY_LIST_CAPACITY_MAX_SIZE);
            throw new InvalidOperationException("too many thread-local indexed variables");
        }

        return index;
    }

    public static int lastVariableIndex()
    {
        return nextIndex.get() - 1;
    }


    private static object[] newIndexedVariableTable()
    {
        object[] array = new object[INDEXED_VARIABLE_TABLE_INITIAL_SIZE];
        Arrays.fill(array, UNSET);
        return array;
    }

    public int size()
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

        if (_charsetEncoderCache != null)
        {
            count++;
        }

        if (_charsetDecoderCache != null)
        {
            count++;
        }

        if (_arrayList != null)
        {
            count++;
        }

        object v = indexedVariable(VARIABLES_TO_REMOVE_INDEX);
        if (v != null && v != UNSET)
        {
            //@SuppressWarnings("unchecked")
            System.Collections.ICollection variablesToRemove = (System.Collections.ICollection)v;
            count += variablesToRemove.Count;
        }

        return count;
    }

    public StringBuilder stringBuilder()
    {
        StringBuilder sb = _stringBuilder;
        if (sb == null)
        {
            return _stringBuilder = new StringBuilder(STRING_BUILDER_INITIAL_SIZE);
        }

        if (sb.Capacity > STRING_BUILDER_MAX_SIZE)
        {
            sb.Length = STRING_BUILDER_INITIAL_SIZE;
            //sb.trimToSize();
        }

        sb.Length = 0;
        return sb;
    }

    public Dictionary<Encoding, Encoder> charsetEncoderCache()
    {
        var cache = _charsetEncoderCache;
        if (cache == null)
        {
            _charsetEncoderCache = cache = new Dictionary<Encoding, Encoder>();
        }

        return cache;
    }

    public Dictionary<Encoding, Decoder> charsetDecoderCache()
    {
        var cache = _charsetDecoderCache;
        if (cache == null)
        {
            _charsetDecoderCache = cache = new Dictionary<Encoding, Decoder>();
        }

        return cache;
    }

    public List<E> arrayList<E>()
    {
        return arrayList<E>(DEFAULT_ARRAY_LIST_INITIAL_CAPACITY);
    }

    //@SuppressWarnings("unchecked")
    public List<E> arrayList<E>(int minCapacity)
    {
        List<E> list = (List<E>)_arrayList;
        if (list == null)
        {
            _arrayList = new List<E>(minCapacity);
            return (List<E>)_arrayList;
        }

        list.Clear();
        list.EnsureCapacity(minCapacity);
        return list;
    }

    public int futureListenerStackDepth()
    {
        return _futureListenerStackDepth;
    }

    public void setFutureListenerStackDepth(int futureListenerStackDepth)
    {
        _futureListenerStackDepth = futureListenerStackDepth;
    }

    public IDictionary<Type, TypeParameterMatcher> typeParameterMatcherGetCache()
    {
        var cache = _typeParameterMatcherGetCache;
        if (cache == null)
        {
            _typeParameterMatcherGetCache = cache = new Dictionary<Type, TypeParameterMatcher>();
        }

        return cache;
    }

    public IDictionary<Type, IDictionary<string, TypeParameterMatcher>> typeParameterMatcherFindCache()
    {
        var cache = _typeParameterMatcherFindCache;
        if (cache == null)
        {
            _typeParameterMatcherFindCache = cache = new Dictionary<Type, IDictionary<string, TypeParameterMatcher>>();
        }

        return cache;
    }

    public IDictionary<Type, bool> handlerSharableCache()
    {
        var cache = _handlerSharableCache;
        if (cache == null)
        {
            // Start with small capacity to keep memory overhead as low as possible.
            _handlerSharableCache = cache = new Dictionary<Type, bool>(HANDLER_SHARABLE_CACHE_INITIAL_CAPACITY);
        }

        return cache;
    }

    public int localChannelReaderStackDepth()
    {
        return _localChannelReaderStackDepth;
    }

    public void setLocalChannelReaderStackDepth(int localChannelReaderStackDepth)
    {
        _localChannelReaderStackDepth = localChannelReaderStackDepth;
    }

    public object indexedVariable(int index)
    {
        object[] lookup = indexedVariables;
        return index < lookup.Length ? lookup[index] : UNSET;
    }

    /**
     * @return {@code true} if and only if a new thread-local variable has been created
     */
    public bool setIndexedVariable(int index, object value)
    {
        return getAndSetIndexedVariable(index, value) == UNSET;
    }

    /**
     * @return {@link InternalThreadLocalMap#UNSET} if and only if a new thread-local variable has been created.
     */
    public object getAndSetIndexedVariable(int index, object value)
    {
        object[] lookup = indexedVariables;
        if (index < lookup.Length)
        {
            object oldValue = lookup[index];
            lookup[index] = value;
            return oldValue;
        }

        expandIndexedVariableTableAndSet(index, value);
        return UNSET;
    }

    private void expandIndexedVariableTableAndSet(int index, object value)
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

        object[] newArray = Arrays.copyOf(oldArray, newCapacity);
        Arrays.fill(newArray, oldCapacity, newArray.Length, UNSET);
        newArray[index] = value;
        indexedVariables = newArray;
    }

    public object removeIndexedVariable(int index)
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

    public bool isIndexedVariableSet(int index)
    {
        object[] lookup = indexedVariables;
        return index < lookup.Length && lookup[index] != UNSET;
    }
}