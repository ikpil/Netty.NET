/*
 * Copyright 2019 The Netty Project
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
using System.Threading;
using static Netty.NET.Common.Internal.ObjectUtil;

namespace Netty.NET.Common.Internal;

public class AtomicIntegerFieldUpdater<T>
{
    public int get(T instance)
    {
        throw new NotImplementedException();
    }

    public void set(T instance, int v)
    {
        throw new NotImplementedException();
    }

    public int getAndAdd(T instance, int v)
    {
        throw new NotImplementedException();
    }

    public void lazySet(T instance, int v)
    {
        throw new NotImplementedException();
    }

    public bool compareAndSet(T instance, int v1, int v2)
    {
        throw new NotImplementedException();
    }
}

/**
 * Common logic for {@link IReferenceCounted} implementations
 */
public abstract class ReferenceCountUpdater<T> where T : IReferenceCounted
{
    /*
     * Implementation notes:
     *
     * For the updated int field:
     *   Even => "real" refcount is (refCnt >>> 1)
     *   Odd  => "real" refcount is 0
     *
     * (x & y) appears to be surprisingly expensive relative to (x == y). Thus this class uses
     * a fast-path in some places for most common low values when checking for live (even) refcounts,
     * for example: if (rawCnt == 2 || rawCnt == 4 || (rawCnt & 1) == 0) { ...
     */

    protected ReferenceCountUpdater()
    {
    }

    public static long getUnsafeOffset<T>(string fieldName) where T : IReferenceCounted
    {
        try
        {
            if (PlatformDependent.hasUnsafe())
            {
                var clz = typeof(T);
                return PlatformDependent.objectFieldOffset(clz.GetField(fieldName));
            }
        }
        catch (Exception ignore)
        {
            // fall-back
        }

        return -1;
    }

    protected abstract AtomicIntegerFieldUpdater<T> updater();

    protected abstract long unsafeOffset();

    public int initialValue()
    {
        return 2;
    }

    public void setInitialValue(T instance)
    {
        long offset = unsafeOffset();
        if (offset == -1)
        {
            updater().set(instance, initialValue());
        }
        else
        {
            PlatformDependent.safeConstructPutInt(instance, offset, initialValue());
        }
    }

    private static int realRefCnt(int rawCnt)
    {
        return rawCnt != 2 && rawCnt != 4 && (rawCnt & 1) != 0 ? 0 : rawCnt >>> 1;
    }

    /**
     * Like {@link #realRefCnt(int)} but throws if refCnt == 0
     */
    private static int toLiveRealRefCnt(int rawCnt, int decrement)
    {
        if (rawCnt == 2 || rawCnt == 4 || (rawCnt & 1) == 0)
        {
            return rawCnt >>> 1;
        }

        // odd rawCnt => already deallocated
        throw new IllegalReferenceCountException(0, -decrement);
    }

    private int nonVolatileRawCnt(T instance)
    {
        // TODO: Once we compile against later versions of Java we can replace the Unsafe usage here by varhandles.
        long offset = unsafeOffset();
        return offset != -1 ? PlatformDependent.getInt(instance, offset) : updater().get(instance);
    }

    public int refCnt(T instance)
    {
        return realRefCnt(updater().get(instance));
    }

    public bool isLiveNonVolatile(T instance)
    {
        long offset = unsafeOffset();
        int rawCnt = offset != -1 ? PlatformDependent.getInt(instance, offset) : updater().get(instance);

        // The "real" ref count is > 0 if the rawCnt is even.
        return rawCnt == 2 || rawCnt == 4 || rawCnt == 6 || rawCnt == 8 || (rawCnt & 1) == 0;
    }

    /**
     * An unsafe operation that sets the reference count directly
     */
    public void setRefCnt(T instance, int refCnt)
    {
        updater().set(instance, refCnt > 0 ? refCnt << 1 : 1); // overflow OK here
    }

    /**
     * Resets the reference count to 1
     */
    public void resetRefCnt(T instance)
    {
        // no need of a volatile set, it should happen in a quiescent state
        updater().lazySet(instance, initialValue());
    }

    public T retain(T instance)
    {
        return retain0(instance, 1, 2);
    }

    public T retain(T instance, int increment)
    {
        // all changes to the raw count are 2x the "real" change - overflow is OK
        int rawIncrement = checkPositive(increment, "increment") << 1;
        return retain0(instance, increment, rawIncrement);
    }

    // rawIncrement == increment << 1
    private T retain0(T instance, int increment, int rawIncrement)
    {
        int oldRef = updater().getAndAdd(instance, rawIncrement);
        if (oldRef != 2 && oldRef != 4 && (oldRef & 1) != 0)
        {
            throw new IllegalReferenceCountException(0, increment);
        }

        // don't pass 0!
        if ((oldRef <= 0 && oldRef + rawIncrement >= 0)
            || (oldRef >= 0 && oldRef + rawIncrement < oldRef))
        {
            // overflow case
            updater().getAndAdd(instance, -rawIncrement);
            throw new IllegalReferenceCountException(realRefCnt(oldRef), increment);
        }

        return instance;
    }

    public bool release(T instance)
    {
        int rawCnt = nonVolatileRawCnt(instance);
        return rawCnt == 2
            ? tryFinalRelease0(instance, 2) || retryRelease0(instance, 1)
            : nonFinalRelease0(instance, 1, rawCnt, toLiveRealRefCnt(rawCnt, 1));
    }

    public bool release(T instance, int decrement)
    {
        int rawCnt = nonVolatileRawCnt(instance);
        int realCnt = toLiveRealRefCnt(rawCnt, checkPositive(decrement, "decrement"));
        return decrement == realCnt
            ? tryFinalRelease0(instance, rawCnt) || retryRelease0(instance, decrement)
            : nonFinalRelease0(instance, decrement, rawCnt, realCnt);
    }

    private bool tryFinalRelease0(T instance, int expectRawCnt)
    {
        return updater().compareAndSet(instance, expectRawCnt, 1); // any odd number will work
    }

    private bool nonFinalRelease0(T instance, int decrement, int rawCnt, int realCnt)
    {
        if (decrement < realCnt
            // all changes to the raw count are 2x the "real" change - overflow is OK
            && updater().compareAndSet(instance, rawCnt, rawCnt - (decrement << 1)))
        {
            return false;
        }

        return retryRelease0(instance, decrement);
    }

    private bool retryRelease0(T instance, int decrement)
    {
        for (;;)
        {
            int rawCnt = updater().get(instance), realCnt = toLiveRealRefCnt(rawCnt, decrement);
            if (decrement == realCnt)
            {
                if (tryFinalRelease0(instance, rawCnt))
                {
                    return true;
                }
            }
            else if (decrement < realCnt)
            {
                // all changes to the raw count are 2x the "real" change
                if (updater().compareAndSet(instance, rawCnt, rawCnt - (decrement << 1)))
                {
                    return false;
                }
            }
            else
            {
                throw new IllegalReferenceCountException(realCnt, -decrement);
            }

            Thread.Yield(); // this benefits throughput under high contention
        }
    }
}