using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicInteger
{
    private volatile int _location;

    public AtomicInteger() : this(0)
    {
    }

    public AtomicInteger(int location)
    {
        _location = location;
    }

    public int IncrementAndGet()
    {
        return Interlocked.Increment(ref _location);
    }

    public int GetAndIncrement()
    {
        var next = Interlocked.Increment(ref _location);
        return next - 1;
    }

    public int DecrementAndGet()
    {
        return Interlocked.Decrement(ref _location);
    }

    public int Get()
    {
        return _location;
    }

    public int Set(int exchange)
    {
        return Interlocked.Exchange(ref _location, exchange);
    }

    public int Decrease(int value)
    {
        return Interlocked.Add(ref _location, -value);
    }

    public bool CompareAndSet(int expectedValue, int newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public int AddAndGet(int value)
    {
        return Interlocked.Add(ref _location, value);
    }
}