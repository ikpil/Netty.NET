using System.Threading;

namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueHeadCacheField<T> : MpscArrayQueueMidPad<T>
    where T : class
{
    long headCache;

    protected MpscArrayQueueHeadCacheField(int capacity)
        : base(capacity)
    {
    }

    protected long ConsumerIndexCache
    {
        get { return Volatile.Read(ref this.headCache); }
        set { Volatile.Write(ref this.headCache, value); }
    }
}
