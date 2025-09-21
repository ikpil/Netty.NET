using System.Diagnostics.Contracts;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections.JCTools;

/// <summary>
/// Forked from <a href="https://github.com/JCTools/JCTools">JCTools</a>.
/// A Multi-Producer-Single-Consumer queue based on a <see cref="ConcurrentCircularArrayQueue{T}"/>. This implies
/// that any thread may call the Enqueue methods, but only a single thread may call poll/peek for correctness to
/// maintained.
/// <para>
/// This implementation follows patterns documented on the package level for False Sharing protection.
/// </para>
/// <para>
/// This implementation is using the <a href="http://sourceforge.net/projects/mc-fastflow/">Fast Flow</a>
/// method for polling from the queue (with minor change to correctly publish the index) and an extension of
/// the Leslie Lamport concurrent queue algorithm (originated by Martin Thompson) on the producer side.
/// </para>
/// </summary>
/// <typeparam name="T">The type of each item in the queue.</typeparam>
sealed class MpscArrayQueue<T> : MpscArrayQueueConsumerField<T>
    where T : class
{
#pragma warning disable 169 // padded reference
    long p40, p41, p42, p43, p44, p45, p46;
    long p30, p31, p32, p33, p34, p35, p36, p37;
#pragma warning restore 169

    public MpscArrayQueue(int capacity)
        : base(capacity)
    {
    }

    /// <summary>
    /// Lock free Enqueue operation, using a single compare-and-swap. As the class name suggests, access is
    /// permitted to many threads concurrently.
    /// </summary>
    /// <param name="e">The item to enqueue.</param>
    /// <returns><c>true</c> if the item was added successfully, otherwise <c>false</c>.</returns>
    /// <seealso cref="IQueue{T}.TryEnqueue"/>
    public override bool TryEnqueue(T e)
    {
        Contract.Requires(e != null);

        // use a cached view on consumer index (potentially updated in loop)
        long mask = this.Mask;
        long capacity = mask + 1;
        long consumerIndexCache = this.ConsumerIndexCache; // LoadLoad
        long currentProducerIndex;
        do
        {
            currentProducerIndex = this.ProducerIndex; // LoadLoad
            long wrapPoint = currentProducerIndex - capacity;
            if (consumerIndexCache <= wrapPoint)
            {
                long currHead = this.ConsumerIndex; // LoadLoad
                if (currHead <= wrapPoint)
                {
                    return false; // FULL :(
                }
                else
                {
                    // update shared cached value of the consumerIndex
                    this.ConsumerIndexCache = currHead; // StoreLoad
                    // update on stack copy, we might need this value again if we lose the CAS.
                    consumerIndexCache = currHead;
                }
            }
        } while (!this.TrySetProducerIndex(currentProducerIndex, currentProducerIndex + 1));

        // NOTE: the new producer index value is made visible BEFORE the element in the array. If we relied on
        // the index visibility to poll() we would need to handle the case where the element is not visible.

        // Won CAS, move on to storing
        long offset = RefArrayAccessUtil.CalcElementOffset(currentProducerIndex, mask);
        this.SoElement(offset, e); // StoreStore
        return true; // AWESOME :)
    }

    /// <summary>
    /// A wait-free alternative to <see cref="TryEnqueue"/>, which fails on compare-and-swap failure.
    /// </summary>
    /// <param name="e">The item to enqueue.</param>
    /// <returns><c>1</c> if next element cannot be filled, <c>-1</c> if CAS failed, and <c>0</c> if successful.</returns>
    public int WeakEnqueue(T e)
    {
        Contract.Requires(e != null);

        long mask = this.Mask;
        long capacity = mask + 1;
        long currentTail = this.ProducerIndex; // LoadLoad
        long consumerIndexCache = this.ConsumerIndexCache; // LoadLoad
        long wrapPoint = currentTail - capacity;
        if (consumerIndexCache <= wrapPoint)
        {
            long currHead = this.ConsumerIndex; // LoadLoad
            if (currHead <= wrapPoint)
            {
                return 1; // FULL :(
            }
            else
            {
                this.ConsumerIndexCache = currHead; // StoreLoad
            }
        }

        // look Ma, no loop!
        if (!this.TrySetProducerIndex(currentTail, currentTail + 1))
        {
            return -1; // CAS FAIL :(
        }

        // Won CAS, move on to storing
        long offset = RefArrayAccessUtil.CalcElementOffset(currentTail, mask);
        this.SoElement(offset, e);
        return 0; // AWESOME :)
    }

    /// <summary>
    /// Lock free poll using ordered loads/stores. As class name suggests, access is limited to a single thread.
    /// </summary>
    /// <param name="item">The dequeued item.</param>
    /// <returns><c>true</c> if an item was retrieved, otherwise <c>false</c>.</returns>
    /// <seealso cref="IQueue{T}.TryDequeue"/>
    public override bool TryDequeue(out T item)
    {
        long consumerIndex = this.ConsumerIndex; // LoadLoad
        long offset = this.CalcElementOffset(consumerIndex);
        // Copy field to avoid re-reading after volatile load
        T[] buffer = this.Buffer;

        // If we can't see the next available element we can't poll
        T e = RefArrayAccessUtil.LvElement(buffer, offset); // LoadLoad
        if (null == e)
        {
            // NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
            // winning the CAS on offer but before storing the element in the queue. Other producers may go on
            // to fill up the queue after this element.

            if (consumerIndex != this.ProducerIndex)
            {
                do
                {
                    e = RefArrayAccessUtil.LvElement(buffer, offset);
                } while (e == null);
            }
            else
            {
                item = default(T);
                return false;
            }
        }

        RefArrayAccessUtil.SpElement(buffer, offset, default(T));
        this.ConsumerIndex = consumerIndex + 1; // StoreStore
        item = e;
        return true;
    }

    /// <summary>
    /// Lock free peek using ordered loads. As class name suggests access is limited to a single thread.
    /// </summary>
    /// <param name="item">The peeked item.</param>
    /// <returns><c>true</c> if an item was retrieved, otherwise <c>false</c>.</returns>
    /// <seealso cref="IQueue{T}.TryPeek"/>
    public override bool TryPeek(out T item)
    {
        // Copy field to avoid re-reading after volatile load
        T[] buffer = this.Buffer;

        long consumerIndex = this.ConsumerIndex; // LoadLoad
        long offset = this.CalcElementOffset(consumerIndex);
        T e = RefArrayAccessUtil.LvElement(buffer, offset);
        if (null == e)
        {
            // NOTE: Queue may not actually be empty in the case of a producer (P1) being interrupted after
            // winning the CAS on offer but before storing the element in the queue. Other producers may go on
            // to fill up the queue after this element.

            if (consumerIndex != this.ProducerIndex)
            {
                do
                {
                    e = RefArrayAccessUtil.LvElement(buffer, offset);
                } while (e == null);
            }
            else
            {
                item = default(T);
                return false;
            }
        }

        item = e;

        return true;
    }

    public override int Drain(IConsumer<T> c, int limit)
    {
        T item;
        int i = 0;
        for (; i < limit && TryDequeue(out item); i++)
        {
            c.accept(item);
        }

        return i;
    }

    /// <summary>
    /// Returns the number of items in this <see cref="MpscArrayQueue{T}"/>.
    /// </summary>
    public override int Count
    {
        get
        {
            // It is possible for a thread to be interrupted or reschedule between the read of the producer and
            // consumer indices, therefore protection is required to ensure size is within valid range. In the
            // event of concurrent polls/offers to this method the size is OVER estimated as we read consumer
            // index BEFORE the producer index.

            long after = this.ConsumerIndex;
            while (true)
            {
                long before = after;
                long currentProducerIndex = this.ProducerIndex;
                after = this.ConsumerIndex;
                if (before == after)
                {
                    return (int)(currentProducerIndex - after);
                }
            }
        }
    }

    public override bool IsEmpty
    {
        get
        {
            // Order matters!
            // Loading consumer before producer allows for producer increments after consumer index is read.
            // This ensures the correctness of this method at least for the consumer thread. Other threads POV is
            // not really
            // something we can fix here.
            return this.ConsumerIndex == this.ProducerIndex;
        }
    }
}