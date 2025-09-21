namespace Netty.NET.Common.Collections.JCTools;

abstract class MpscArrayQueueMidPad<T> : MpscArrayQueueTailField<T>
    where T : class
{
#pragma warning disable 169 // padded reference
    long p20, p21, p22, p23, p24, p25, p26;
    long p30, p31, p32, p33, p34, p35, p36, p37;
#pragma warning restore 169

    protected MpscArrayQueueMidPad(int capacity)
        : base(capacity)
    {
    }
}
