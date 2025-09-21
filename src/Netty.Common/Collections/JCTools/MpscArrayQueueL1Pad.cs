namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueL1Pad<T> : ConcurrentCircularArrayQueue<T>
    where T : class
{
#pragma warning disable 169 // padded reference
    long p10, p11, p12, p13, p14, p15, p16;
    long p30, p31, p32, p33, p34, p35, p36, p37;
#pragma warning restore 169

    protected MpscArrayQueueL1Pad(int capacity)
        : base(capacity)
    {
    }
}
