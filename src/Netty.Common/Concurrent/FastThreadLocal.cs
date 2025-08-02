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
using System.Linq;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * A special variant of {@link ThreadLocal} that yields higher access performance when accessed from a
 * {@link FastThreadLocalThread}.
 * <p>
 * Internally, a {@link FastThreadLocal} uses a constant index in an array, instead of using hash code and hash table,
 * to look for a variable.  Although seemingly very subtle, it yields slight performance advantage over using a hash
 * table, and it is useful when accessed frequently.
 * </p><p>
 * To take advantage of this thread-local variable, your thread must be a {@link FastThreadLocalThread} or its subtype.
 * By default, all threads created by {@link DefaultThreadFactory} are {@link FastThreadLocalThread} due to this reason.
 * </p><p>
 * Note that the fast path is only possible on threads that extend {@link FastThreadLocalThread}, because it requires
 * a special field to store the necessary state.  An access by any other kind of thread falls back to a regular
 * {@link ThreadLocal}.
 * </p>
 *
 * @param <V> the type of the thread-local variable
 * @see ThreadLocal
 */
public class FastThreadLocal<V> where V : class
{
    private readonly int index;

    public FastThreadLocal()
    {
        index = InternalThreadLocalMap.nextVariableIndex();
    }

    /**
     * Removes all {@link FastThreadLocal} variables bound to the current thread.  This operation is useful when you
     * are in a container environment, and you don't want to leave the thread local variables in the threads you do not
     * manage.
     */
    public static void removeAll()
    {
        InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.getIfSet();
        if (threadLocalMap == null)
        {
            return;
        }

        try
        {
            object v = threadLocalMap.indexedVariable(InternalThreadLocalMap.VARIABLES_TO_REMOVE_INDEX);
            if (v != null && v != InternalThreadLocalMap.UNSET)
            {
                //@SuppressWarnings("unchecked")
                HashSet<FastThreadLocal<V>> variablesToRemove = (HashSet<FastThreadLocal<V>>)v;
                FastThreadLocal<V>[] variablesToRemoveArray = variablesToRemove.ToArray();
                foreach (FastThreadLocal<V> tlv in variablesToRemoveArray)
                {
                    tlv.remove(threadLocalMap);
                }
            }
        }
        finally
        {
            InternalThreadLocalMap.remove();
        }
    }

    /**
     * Returns the number of thread local variables bound to the current thread.
     */
    public static int size()
    {
        InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.getIfSet();
        if (threadLocalMap == null)
        {
            return 0;
        }
        else
        {
            return threadLocalMap.size();
        }
    }

    /**
     * Destroys the data structure that keeps all {@link FastThreadLocal} variables accessed from
     * non-{@link FastThreadLocalThread}s.  This operation is useful when you are in a container environment, and you
     * do not want to leave the thread local variables in the threads you do not manage.  Call this method when your
     * application is being unloaded from the container.
     */
    public static void destroy()
    {
        InternalThreadLocalMap.destroy();
    }

    //@SuppressWarnings("unchecked")
    private static void addToVariablesToRemove(InternalThreadLocalMap threadLocalMap, FastThreadLocal<V> variable)
    {
        object v = threadLocalMap.indexedVariable(InternalThreadLocalMap.VARIABLES_TO_REMOVE_INDEX);
        HashSet<FastThreadLocal<V>> variablesToRemove;
        if (v == InternalThreadLocalMap.UNSET || v == null)
        {
            variablesToRemove = new HashSet<FastThreadLocal<V>>();
            threadLocalMap.setIndexedVariable(InternalThreadLocalMap.VARIABLES_TO_REMOVE_INDEX, variablesToRemove);
        }
        else
        {
            variablesToRemove = (HashSet<FastThreadLocal<V>>)v;
        }

        variablesToRemove.Add(variable);
    }

    private static void removeFromVariablesToRemove(InternalThreadLocalMap threadLocalMap, FastThreadLocal<V> variable)
    {
        object v = threadLocalMap.indexedVariable(InternalThreadLocalMap.VARIABLES_TO_REMOVE_INDEX);

        if (v == InternalThreadLocalMap.UNSET || v == null)
        {
            return;
        }

        //@SuppressWarnings("unchecked")
        HashSet<FastThreadLocal<V>> variablesToRemove = (HashSet<FastThreadLocal<V>>)v;
        variablesToRemove.Remove(variable);
    }


    /**
     * Returns the current value for the current thread
     */
    //@SuppressWarnings("unchecked")
    public V get()
    {
        InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.get();
        object v = threadLocalMap.indexedVariable(index);
        if (v != InternalThreadLocalMap.UNSET)
        {
            return (V)v;
        }

        return initialize(threadLocalMap);
    }

    /**
     * Returns the current value for the current thread if it exists, {@code null} otherwise.
     */
    //@SuppressWarnings("unchecked")
    public V getIfExists()
    {
        InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.getIfSet();
        if (threadLocalMap != null)
        {
            object v = threadLocalMap.indexedVariable(index);
            if (v != InternalThreadLocalMap.UNSET)
            {
                return (V)v;
            }
        }

        return default;
    }

    /**
     * Returns the current value for the specified thread local map.
     * The specified thread local map must be for the current thread.
     */
    //@SuppressWarnings("unchecked")
    public V get(InternalThreadLocalMap threadLocalMap)
    {
        object v = threadLocalMap.indexedVariable(index);
        if (v != InternalThreadLocalMap.UNSET)
        {
            return (V)v;
        }

        return initialize(threadLocalMap);
    }

    private V initialize(InternalThreadLocalMap threadLocalMap)
    {
        V v = null;
        try
        {
            v = initialValue();
            if (v == InternalThreadLocalMap.UNSET)
            {
                throw new ArgumentException("InternalThreadLocalMap.UNSET can not be initial value.");
            }
        }
        catch (Exception e)
        {
            PlatformDependent.throwException(e);
        }

        threadLocalMap.setIndexedVariable(index, v);
        addToVariablesToRemove(threadLocalMap, this);
        return v;
    }

    /**
     * Set the value for the current thread.
     */
    public void set(V value)
    {
        getAndSet(value);
    }

    /**
     * Set the value for the specified thread local map. The specified thread local map must be for the current thread.
     */
    public void set(InternalThreadLocalMap threadLocalMap, V value)
    {
        getAndSet(threadLocalMap, value);
    }

    /**
     * Set the value for the current thread and returns the old value.
     */
    public V getAndSet(V value)
    {
        if (value != InternalThreadLocalMap.UNSET)
        {
            InternalThreadLocalMap threadLocalMap = InternalThreadLocalMap.get();
            return setKnownNotUnset(threadLocalMap, value);
        }

        return removeAndGet(InternalThreadLocalMap.getIfSet());
    }

    /**
     * Set the value for the specified thread local map. The specified thread local map must be for the current thread.
     */
    public V getAndSet(InternalThreadLocalMap threadLocalMap, V value)
    {
        if (value != InternalThreadLocalMap.UNSET)
        {
            return setKnownNotUnset(threadLocalMap, value);
        }

        return removeAndGet(threadLocalMap);
    }

    /**
     * @see InternalThreadLocalMap#setIndexedVariable(int, object).
     */
    //@SuppressWarnings("unchecked")
    private V setKnownNotUnset(InternalThreadLocalMap threadLocalMap, V value)
    {
        V old = (V)threadLocalMap.getAndSetIndexedVariable(index, value);
        if (old == InternalThreadLocalMap.UNSET)
        {
            addToVariablesToRemove(threadLocalMap, this);
            return null;
        }

        return old;
    }

    /**
     * Returns {@code true} if and only if this thread-local variable is set.
     */
    public bool isSet()
    {
        return isSet(InternalThreadLocalMap.getIfSet());
    }

    /**
     * Returns {@code true} if and only if this thread-local variable is set.
     * The specified thread local map must be for the current thread.
     */
    public bool isSet(InternalThreadLocalMap threadLocalMap)
    {
        return threadLocalMap != null && threadLocalMap.isIndexedVariableSet(index);
    }

    /**
     * Sets the value to uninitialized for the specified thread local map and returns the old value.
     * After this, any subsequent call to get() will trigger a new call to initialValue().
     */
    public void remove()
    {
        remove(InternalThreadLocalMap.getIfSet());
    }

    /**
     * Sets the value to uninitialized for the specified thread local map.
     * After this, any subsequent call to get() will trigger a new call to initialValue().
     * The specified thread local map must be for the current thread.
     */
    //@SuppressWarnings("unchecked")
    public void remove(InternalThreadLocalMap threadLocalMap)
    {
        removeAndGet(threadLocalMap);
    }

    /**
     * Sets the value to uninitialized for the specified thread local map.
     * After this, any subsequent call to get() will trigger a new call to initialValue().
     * The specified thread local map must be for the current thread.
     */
    //@SuppressWarnings("unchecked")
    private V removeAndGet(InternalThreadLocalMap threadLocalMap)
    {
        if (threadLocalMap == null)
        {
            return null;
        }

        object v = threadLocalMap.removeIndexedVariable(index);
        if (v != InternalThreadLocalMap.UNSET)
        {
            removeFromVariablesToRemove(threadLocalMap, this);
            try
            {
                onRemoval((V)v);
            }
            catch (Exception e)
            {
                PlatformDependent.throwException(e);
            }

            return (V)v;
        }

        return null;
    }

    /**
     * Returns the initial value for this thread-local variable.
     */
    protected V initialValue()
    {
        return null;
    }

    /**
     * Invoked when this thread local variable is removed by {@link #remove()}. Be aware that {@link #remove()}
     * is not guaranteed to be called when the `Thread` completes which means you can not depend on this for
     * cleanup of the resources in the case of `Thread` completion.
     */
    //@SuppressWarnings("UnusedParameters")
    protected void onRemoval(V value)
    {
    }
}