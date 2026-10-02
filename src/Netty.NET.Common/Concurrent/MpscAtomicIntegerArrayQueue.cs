/*
 * Copyright 2025 The Netty Project
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
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * This implementation is based on MpscAtomicUnpaddedArrayQueue from JCTools.
 */
public sealed class MpscAtomicIntegerArrayQueue : IMpscIntQueue
{
    private readonly int[] elements;
    private readonly int mask;
    private readonly int emptyValue;
    private long producerIndex;
    private long producerLimit;
    private long consumerIndex;

    public MpscAtomicIntegerArrayQueue(int capacity, int emptyValue)
    {
        elements = new int[MathUtil.safeFindNextPositivePowerOfTwo(capacity)];
        if (emptyValue != 0)
        {
            this.emptyValue = emptyValue;
            int end = elements.Length - 1;
            for (int i = 0; i < end; i++)
            {
                Volatile.Write(ref elements[i], emptyValue);
            }
            Interlocked.Exchange(ref elements[end], emptyValue); // 'getAndSet' acts as a full barrier, giving us initialization safety.
        }
        else
        {
            this.emptyValue = 0;
        }
        mask = elements.Length - 1;
    }

    public bool offer(int value)
    {
        if (value == emptyValue)
        {
            throw new ArgumentException("Cannot offer the \"empty\" value: " + emptyValue);
        }
        // use a cached view on consumer index (potentially updated in loop)
        int mask = this.mask;
        long producerLimit = Volatile.Read(ref this.producerLimit);
        long pIndex;
        do
        {
            pIndex = Volatile.Read(ref producerIndex);
            if (pIndex >= producerLimit)
            {
                long cIndex = Volatile.Read(ref consumerIndex);
                producerLimit = cIndex + mask + 1;
                if (pIndex >= producerLimit)
                {
                    // FULL :(
                    return false;
                }
                else
                {
                    // update producer limit to the next index that we must recheck the consumer index
                    // this is racy, but the race is benign
                    Volatile.Write(ref this.producerLimit, producerLimit);
                }
            }
        } while (Interlocked.CompareExchange(ref producerIndex, pIndex + 1, pIndex) != pIndex);
        /*
         * NOTE: the new producer index value is made visible BEFORE the element in the array. If we relied on
         * the index visibility to poll() we would need to handle the case where the element is not visible.
         */
        // Won CAS, move on to storing
        int offset = (int)(pIndex & mask);
        Volatile.Write(ref elements[offset], value);
        // AWESOME :)
        return true;
    }

    public int poll()
    {
        long cIndex = Volatile.Read(ref consumerIndex);
        int offset = (int)(cIndex & mask);
        // If we can't see the next available element we can't poll
        int value = Volatile.Read(ref elements[offset]);
        if (emptyValue == value)
        {
            /*
             * NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
             * winning the CAS on offer but before storing the element in the queue. Other producers may go on
             * to fill up the queue after this element.
             */
            if (cIndex != Volatile.Read(ref producerIndex))
            {
                do
                {
                    Thread.SpinWait(1);
                    value = Volatile.Read(ref elements[offset]);
                } while (emptyValue == value);
            }
            else
            {
                return emptyValue;
            }
        }
        Volatile.Write(ref elements[offset], emptyValue);
        Volatile.Write(ref consumerIndex, cIndex + 1);
        return value;
    }

    public int drain(int limit, Action<int> consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        ObjectUtil.checkPositiveOrZero(limit, "limit");
        if (limit == 0)
        {
            return 0;
        }
        int mask = this.mask;
        long cIndex = Volatile.Read(ref consumerIndex); // Note: could be weakened to plain-load.
        for (int i = 0; i < limit; i++)
        {
            long index = cIndex + i;
            int offset = (int)(index & mask);
            int value = Volatile.Read(ref elements[offset]);
            if (emptyValue == value)
            {
                return i;
            }
            Volatile.Write(ref elements[offset], emptyValue); // Note: could be weakened to plain-store.
            // ordered store -> atomic and ordered for size()
            Volatile.Write(ref consumerIndex, index + 1);
            consumer(value);
        }
        return limit;
    }

    public int fill(int limit, Func<int> supplier)
    {
        ArgumentNullException.ThrowIfNull(supplier);
        ObjectUtil.checkPositiveOrZero(limit, "limit");
        if (limit == 0)
        {
            return 0;
        }
        int mask = this.mask;
        long capacity = mask + 1;
        long producerLimit = Volatile.Read(ref this.producerLimit);
        long pIndex;
        int actualLimit;
        do
        {
            pIndex = Volatile.Read(ref producerIndex);
            long available = producerLimit - pIndex;
            if (available <= 0)
            {
                long cIndex = Volatile.Read(ref consumerIndex);
                producerLimit = cIndex + capacity;
                available = producerLimit - pIndex;
                if (available <= 0)
                {
                    // FULL :(
                    return 0;
                }
                else
                {
                    // update producer limit to the next index that we must recheck the consumer index
                    Volatile.Write(ref this.producerLimit, producerLimit);
                }
            }
            actualLimit = Math.Min((int)available, limit);
        } while (Interlocked.CompareExchange(ref producerIndex, pIndex + actualLimit, pIndex) != pIndex);
        // right, now we claimed a few slots and can fill them with goodness
        for (int i = 0; i < actualLimit; i++)
        {
            // Won CAS, move on to storing
            int offset = (int)(pIndex + i & mask);
            Volatile.Write(ref elements[offset], supplier());
        }
        return actualLimit;
    }

    public int weakPeekReduce(int limit, int initial, Func<int, int, int> op)
    {
        ArgumentNullException.ThrowIfNull(op);
        ObjectUtil.checkPositiveOrZero(limit, "limit");
        if (limit == 0)
        {
            return 0;
        }
        int result = initial;

        int mask = this.mask;
        long cIndex = Volatile.Read(ref consumerIndex); // Note: could be weakened to plain-load.
        for (int i = 0; i < limit; i++)
        {
            long index = cIndex + i;
            int offset = (int)(index & mask);
            int value = Volatile.Read(ref elements[offset]);
            if (emptyValue == value)
            {
                return result;
            }
            // Do not remove the element or advance the consumer index.
            result = op(result, value);
        }
        return result;
    }

    public bool isEmpty()
    {
        // Load consumer index before producer index, so our check is conservative.
        long cIndex = Volatile.Read(ref consumerIndex);
        long pIndex = Volatile.Read(ref producerIndex);
        return cIndex >= pIndex;
    }

    public int size()
    {
        // Loop until we get a consistent read of both the consumer and producer indices.
        long after = Volatile.Read(ref consumerIndex);
        long size;
        for (;;)
        {
            long before = after;
            long pIndex = Volatile.Read(ref producerIndex);
            after = Volatile.Read(ref consumerIndex);
            if (before == after)
            {
                size = pIndex - after;
                break;
            }
        }
        return size < 0 ? 0 : size > int.MaxValue ? int.MaxValue : (int) size;
    }
}
