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
using System.Threading;
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
    private static readonly ThreadLocal<InternalThreadLocalMap> slowThreadLocalMap = new ThreadLocal<InternalThreadLocalMap>();
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
    private int futureListenerStackDepth;
    private int localChannelReaderStackDepth;
    private IDictionary<Type, bool> handlerSharableCache;
    private IDictionary<Type, TypeParameterMatcher> typeParameterMatcherGetCache;
    private IDictionary<Type, IDictionary<string, TypeParameterMatcher>> typeParameterMatcherFindCache;

    // string-related thread-locals
    private StringBuilder stringBuilder;
    private IDictionary<Charset, CharsetEncoder> charsetEncoderCache;
    private IDictionary<Charset, CharsetDecoder> charsetDecoderCache;

    // List-related thread-locals
    private List<object> arrayList;

    private BitSet cleanerFlags;

    /** @deprecated These padding fields will be removed in the future. */
    public long rp1, rp2, rp3, rp4, rp5, rp6, rp7, rp8;

    static {
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

    public static InternalThreadLocalMap getIfSet() {
        Thread thread = Thread.currentThread();
        if (thread instanceof FastThreadLocalThread) {
            return ((FastThreadLocalThread) thread).threadLocalMap();
        }
        return slowThreadLocalMap.get();
    }

    public static InternalThreadLocalMap get() {
        Thread thread = Thread.currentThread();
        if (thread instanceof FastThreadLocalThread) {
            return fastGet((FastThreadLocalThread) thread);
        } else {
            return slowGet();
        }
    }

    private static InternalThreadLocalMap fastGet(FastThreadLocalThread thread) {
        InternalThreadLocalMap threadLocalMap = thread.threadLocalMap();
        if (threadLocalMap == null) {
            thread.setThreadLocalMap(threadLocalMap = new InternalThreadLocalMap());
        }
        return threadLocalMap;
    }

    private static InternalThreadLocalMap slowGet() {
        InternalThreadLocalMap ret = slowThreadLocalMap.get();
        if (ret == null) {
            ret = new InternalThreadLocalMap();
            slowThreadLocalMap.set(ret);
        }
        return ret;
    }

    public static void remove() {
        Thread thread = Thread.currentThread();
        if (thread instanceof FastThreadLocalThread) {
            ((FastThreadLocalThread) thread).setThreadLocalMap(null);
        } else {
            slowThreadLocalMap.remove();
        }
    }

    public static void destroy() {
        slowThreadLocalMap.remove();
    }

    public static int nextVariableIndex() {
        int index = nextIndex.getAndIncrement();
        if (index >= ARRAY_LIST_CAPACITY_MAX_SIZE || index < 0) {
            nextIndex.set(ARRAY_LIST_CAPACITY_MAX_SIZE);
            throw new InvalidOperationException("too many thread-local indexed variables");
        }
        return index;
    }

    public static int lastVariableIndex() {
        return nextIndex.get() - 1;
    }

    private InternalThreadLocalMap() {
        indexedVariables = newIndexedVariableTable();
    }

    private static object[] newIndexedVariableTable() {
        object[] array = new object[INDEXED_VARIABLE_TABLE_INITIAL_SIZE];
        Arrays.fill(array, UNSET);
        return array;
    }

    public int size() {
        int count = 0;

        if (futureListenerStackDepth != 0) {
            count ++;
        }
        if (localChannelReaderStackDepth != 0) {
            count ++;
        }
        if (handlerSharableCache != null) {
            count ++;
        }
        if (typeParameterMatcherGetCache != null) {
            count ++;
        }
        if (typeParameterMatcherFindCache != null) {
            count ++;
        }
        if (stringBuilder != null) {
            count ++;
        }
        if (charsetEncoderCache != null) {
            count ++;
        }
        if (charsetDecoderCache != null) {
            count ++;
        }
        if (arrayList != null) {
            count ++;
        }

        object v = indexedVariable(VARIABLES_TO_REMOVE_INDEX);
        if (v != null && v != InternalThreadLocalMap.UNSET) {
            @SuppressWarnings("unchecked")
            ISet<FastThreadLocal<?>> variablesToRemove = (ISet<FastThreadLocal<?>>) v;
            count += variablesToRemove.size();
        }

        return count;
    }

    public StringBuilder stringBuilder() {
        StringBuilder sb = stringBuilder;
        if (sb == null) {
            return stringBuilder = new StringBuilder(STRING_BUILDER_INITIAL_SIZE);
        }
        if (sb.capacity() > STRING_BUILDER_MAX_SIZE) {
            sb.setLength(STRING_BUILDER_INITIAL_SIZE);
            sb.trimToSize();
        }
        sb.setLength(0);
        return sb;
    }

    public IDictionary<Charset, CharsetEncoder> charsetEncoderCache() {
        IDictionary<Charset, CharsetEncoder> cache = charsetEncoderCache;
        if (cache == null) {
            charsetEncoderCache = cache = new IdentityHashMap<>();
        }
        return cache;
    }

    public IDictionary<Charset, CharsetDecoder> charsetDecoderCache() {
        IDictionary<Charset, CharsetDecoder> cache = charsetDecoderCache;
        if (cache == null) {
            charsetDecoderCache = cache = new IdentityHashMap<>();
        }
        return cache;
    }

    public <E> List<E> arrayList() {
        return arrayList(DEFAULT_ARRAY_LIST_INITIAL_CAPACITY);
    }

    @SuppressWarnings("unchecked")
    public <E> List<E> arrayList(int minCapacity) {
        List<E> list = (List<E>) arrayList;
        if (list == null) {
            arrayList = new List<>(minCapacity);
            return (List<E>) arrayList;
        }
        list.clear();
        list.ensureCapacity(minCapacity);
        return list;
    }

    public int futureListenerStackDepth() {
        return futureListenerStackDepth;
    }

    public void setFutureListenerStackDepth(int futureListenerStackDepth) {
        this.futureListenerStackDepth = futureListenerStackDepth;
    }

    /**
     * @deprecated Use {@link java.util.concurrent.ThreadLocalRandom#current()} instead.
     */
    @Deprecated
    public ThreadLocalRandom random() {
        return new ThreadLocalRandom();
    }

    public IDictionary<Type, TypeParameterMatcher> typeParameterMatcherGetCache() {
        IDictionary<Type, TypeParameterMatcher> cache = typeParameterMatcherGetCache;
        if (cache == null) {
            typeParameterMatcherGetCache = cache = new IdentityHashMap<>();
        }
        return cache;
    }

    public IDictionary<Type, IDictionary<string, TypeParameterMatcher>> typeParameterMatcherFindCache() {
        IDictionary<Type, IDictionary<string, TypeParameterMatcher>> cache = typeParameterMatcherFindCache;
        if (cache == null) {
            typeParameterMatcherFindCache = cache = new IdentityHashMap<>();
        }
        return cache;
    }

    @Deprecated
    public IntegerHolder counterHashCode() {
        return new IntegerHolder();
    }

    @Deprecated
    public void setCounterHashCode(IntegerHolder counterHashCode) {
        // No-op.
    }

    public IDictionary<Type, bool> handlerSharableCache() {
        IDictionary<Type, bool> cache = handlerSharableCache;
        if (cache == null) {
            // Start with small capacity to keep memory overhead as low as possible.
            handlerSharableCache = cache = new WeakHashMap<>(HANDLER_SHARABLE_CACHE_INITIAL_CAPACITY);
        }
        return cache;
    }

    public int localChannelReaderStackDepth() {
        return localChannelReaderStackDepth;
    }

    public void setLocalChannelReaderStackDepth(int localChannelReaderStackDepth) {
        this.localChannelReaderStackDepth = localChannelReaderStackDepth;
    }

    public object indexedVariable(int index) {
        object[] lookup = indexedVariables;
        return index < lookup.length? lookup[index] : UNSET;
    }

    /**
     * @return {@code true} if and only if a new thread-local variable has been created
     */
    public bool setIndexedVariable(int index, object value) {
        return getAndSetIndexedVariable(index, value) == UNSET;
    }

    /**
     * @return {@link InternalThreadLocalMap#UNSET} if and only if a new thread-local variable has been created.
     */
    public object getAndSetIndexedVariable(int index, object value) {
        object[] lookup = indexedVariables;
        if (index < lookup.length) {
            object oldValue = lookup[index];
            lookup[index] = value;
            return oldValue;
        }
        expandIndexedVariableTableAndSet(index, value);
        return UNSET;
    }

    private void expandIndexedVariableTableAndSet(int index, object value) {
        object[] oldArray = indexedVariables;
        final int oldCapacity = oldArray.length;
        int newCapacity;
        if (index < ARRAY_LIST_CAPACITY_EXPAND_THRESHOLD) {
            newCapacity = index;
            newCapacity |= newCapacity >>>  1;
            newCapacity |= newCapacity >>>  2;
            newCapacity |= newCapacity >>>  4;
            newCapacity |= newCapacity >>>  8;
            newCapacity |= newCapacity >>> 16;
            newCapacity ++;
        } else {
            newCapacity = ARRAY_LIST_CAPACITY_MAX_SIZE;
        }

        object[] newArray = Arrays.copyOf(oldArray, newCapacity);
        Arrays.fill(newArray, oldCapacity, newArray.length, UNSET);
        newArray[index] = value;
        indexedVariables = newArray;
    }

    public object removeIndexedVariable(int index) {
        object[] lookup = indexedVariables;
        if (index < lookup.length) {
            object v = lookup[index];
            lookup[index] = UNSET;
            return v;
        } else {
            return UNSET;
        }
    }

    public bool isIndexedVariableSet(int index) {
        object[] lookup = indexedVariables;
        return index < lookup.length && lookup[index] != UNSET;
    }

    @Deprecated
    public bool isCleanerFlagSet(int index) {
        return cleanerFlags != null && cleanerFlags.get(index);
    }

    @Deprecated
    public void setCleanerFlag(int index) {
        if (cleanerFlags == null) {
            cleanerFlags = new BitSet();
        }
        cleanerFlags.set(index);
    }
}
