namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueL2Pad<T> : MpscArrayQueueHeadCacheField<T>
    where T : class
{
#pragma warning disable 169 // padded reference
    long p20, p21, p22, p23, p24, p25, p26;
    long p30, p31, p32, p33, p34, p35, p36, p37;
#pragma warning restore 169

    protected MpscArrayQueueL2Pad(int capacity)
        : base(capacity)
    {
    }
}