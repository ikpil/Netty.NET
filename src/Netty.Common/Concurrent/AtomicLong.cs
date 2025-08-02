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

    public long incrementAndGet()
    {
        return Interlocked.Increment(ref _location);
    }

    public long getAndIncrement()
    {
        var next = Interlocked.Increment(ref _location);
        return next - 1;
    }

    public long decrementAndGet()
    {
        return Interlocked.Decrement(ref _location);
    }

    public long get()
    {
        return _location;
    }

    public long read()
    {
        return Volatile.Read(ref _location);
    }

    public long set(long exchange)
    {
        return Interlocked.Exchange(ref _location, exchange);
    }

    public long decrease(long value)
    {
        return Interlocked.Add(ref _location, -value);
    }

    public bool compareAndSet(long expectedValue, long newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public long addAndGet(long value)
    {
        return Interlocked.Add(ref _location, value);
    }
}