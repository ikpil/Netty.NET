using System;
using Netty.NET.Common.Internal;

namespace Netty.NET.Common.Concurrent;

/**
 * This implementation is based on MpscAtomicUnpaddedArrayQueue from JCTools.
 */
public sealed class MpscAtomicIntegerArrayQueue : AtomicIntegerArray, IMpscIntQueue
{
    private AtomicLong PRODUCER_INDEX => producerIndex;
    private AtomicLong PRODUCER_LIMIT => producerLimit;
    private AtomicLong CONSUMER_INDEX => consumerIndex;

    private readonly int mask;
    private readonly int emptyValue;
    private readonly AtomicLong producerIndex;
    private readonly AtomicLong producerLimit;
    private readonly AtomicLong consumerIndex;

    public MpscAtomicIntegerArrayQueue(int capacity, int emptyValue)
    {
        super(MathUtil.safeFindNextPositivePowerOfTwo(capacity));
        if (emptyValue != 0)
        {
            this.emptyValue = emptyValue;
            int end = capacity - 1;
            for (int i = 0; i < end; i++)
            {
                lazySet(i, emptyValue);
            }

            getAndSet(end, emptyValue); // 'getAndSet' acts as a full barrier, giving us initialization safety.
        }
        else
        {
            this.emptyValue = 0;
        }

        mask = length() - 1;
        
        producerIndex = new AtomicLong();
        producerLimit = new AtomicLong();
        consumerIndex = new AtomicLong();
    }

    public bool offer(int value)
    {
        if (value == emptyValue)
        {
            throw new ArgumentException("Cannot offer the \"empty\" value: " + emptyValue);
        }

        // use a cached view on consumer index (potentially updated in loop)
        int mask = this.mask;
        long producerLimit = this.producerLimit.read();
        long pIndex;
        do
        {
            pIndex = producerIndex.read();
            if (pIndex >= producerLimit)
            {
                long cIndex = consumerIndex.read();
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
                    PRODUCER_LIMIT.lazySet(this, producerLimit);
                }
            }
        } while (!PRODUCER_INDEX.compareAndSet(pIndex, pIndex + 1));

        /*
         * NOTE: the new producer index value is made visible BEFORE the element in the array. If we relied on
         * the index visibility to poll() we would need to handle the case where the element is not visible.
         */
        // Won CAS, move on to storing
        int offset = (int)(pIndex & mask);
        lazySet(offset, value);
        // AWESOME :)
        return true;
    }

    public int poll()
    {
        long cIndex = consumerIndex.read();
        int offset = (int)(cIndex & mask);
        // If we can't see the next available element we can't poll
        int value = get(offset);
        if (emptyValue == value)
        {
            /*
             * NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
             * winning the CAS on offer but before storing the element in the queue. Other producers may go on
             * to fill up the queue after this element.
             */
            if (cIndex != producerIndex.read())
            {
                do
                {
                    value = get(offset);
                } while (emptyValue == value);
            }
            else
            {
                return emptyValue;
            }
        }

        lazySet(offset, emptyValue);
        CONSUMER_INDEX.lazySet(this, cIndex + 1);
        return value;
    }

    public int drain(int limit, Action<int> consumer)
    {
        ObjectUtil.requireNonNull(consumer, "consumer");
        ObjectUtil.checkPositiveOrZero(limit, "limit");
        if (limit == 0)
        {
            return 0;
        }

        int mask = this.mask;
        long cIndex = consumerIndex; // Note: could be weakened to plain-load.
        for (int i = 0; i < limit; i++)
        {
            long index = cIndex + i;
            int offset = (int)(index & mask);
            int value = get(offset);
            if (emptyValue == value)
            {
                return i;
            }

            lazySet(offset, emptyValue); // Note: could be weakened to plain-store.
            // ordered store -> atomic and ordered for size()
            CONSUMER_INDEX.lazySet(this, index + 1);
            consumer.Invoke(value);
        }

        return limit;
    }

    public int fill(int limit, Func<int> supplier)
    {
        ObjectUtil.requireNonNull(supplier, "supplier");
        ObjectUtil.checkPositiveOrZero(limit, "limit");
        if (limit == 0)
        {
            return 0;
        }

        int mask = this.mask;
        long capacity = mask + 1;
        long producerLimit = this.producerLimit.read();
        long pIndex;
        int actualLimit;
        do
        {
            pIndex = producerIndex.read();
            long available = producerLimit - pIndex;
            if (available <= 0)
            {
                long cIndex = consumerIndex.read();
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
                    PRODUCER_LIMIT.lazySet(this, producerLimit);
                }
            }

            actualLimit = Math.Min((int)available, limit);
        } while (!PRODUCER_INDEX.compareAndSet(pIndex, pIndex + actualLimit));

        // right, now we claimed a few slots and can fill them with goodness
        for (int i = 0; i < actualLimit; i++)
        {
            // Won CAS, move on to storing
            int offset = (int)(pIndex + i & mask);
            lazySet(offset, supplier.Invoke());
        }

        return actualLimit;
    }

    public bool isEmpty()
    {
        // Load consumer index before producer index, so our check is conservative.
        long cIndex = consumerIndex.read();
        long pIndex = producerIndex.read();
        return cIndex >= pIndex;
    }

    public int size()
    {
        // Loop until we get a consistent read of both the consumer and producer indices.
        long after = consumerIndex.read();
        long size;
        for (;;)
        {
            long before = after;
            long pIndex = producerIndex.read();
            after = consumerIndex.read();
            if (before == after)
            {
                size = pIndex - after;
                break;
            }
        }

        return size < 0 ? 0 : size > int.MaxValue ? int.MaxValue : (int)size;
    }
}