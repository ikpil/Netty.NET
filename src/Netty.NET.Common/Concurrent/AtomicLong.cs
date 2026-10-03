using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicLong
{
    private long _location;

    public AtomicLong() : this(0)
    {
    }

    public AtomicLong(int location)
    {
        _location = location;
    }

    public long IncrementAndGet()
    {
        return Interlocked.Increment(ref _location);
    }

    public long GetAndIncrement()
    {
        var next = Interlocked.Increment(ref _location);
        return next - 1;
    }

    public long DecrementAndGet()
    {
        return Interlocked.Decrement(ref _location);
    }

    public long Get()
    {
        return Volatile.Read(ref _location);
    }

    public long Set(long exchange)
    {
        return Interlocked.Exchange(ref _location, exchange);
    }

    public long Decrease(long value)
    {
        return Interlocked.Add(ref _location, -value);
    }

    public bool CompareAndSet(long expectedValue, long newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public long AddAndGet(long value)
    {
        return Interlocked.Add(ref _location, value);
    }
}