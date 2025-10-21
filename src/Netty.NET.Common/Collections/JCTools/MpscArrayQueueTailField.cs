using System.Threading;

namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueTailField<T> : MpscArrayQueueL1Pad<T>
    where T : class
{
    long producerIndex;

    protected MpscArrayQueueTailField(int capacity)
        : base(capacity)
    {
    }

    protected long ProducerIndex => Volatile.Read(ref this.producerIndex);

    protected bool TrySetProducerIndex(long expect, long newValue) => Interlocked.CompareExchange(ref this.producerIndex, newValue, expect) == expect;
}