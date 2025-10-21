using System.Threading;

namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueConsumerField<T> : MpscArrayQueueL2Pad<T>
    where T : class
{
    long consumerIndex;

    protected MpscArrayQueueConsumerField(int capacity)
        : base(capacity)
    {
    }

    protected long ConsumerIndex
    {
        get { return Volatile.Read(ref this.consumerIndex); }
        set { Volatile.Write(ref this.consumerIndex, value); } // todo: revisit: UNSAFE.putOrderedLong -- StoreStore fence
    }
}