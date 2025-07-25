﻿using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class AtomicInteger
{
    private int _location;

    public AtomicInteger() : this(0)
    {
    }

    public AtomicInteger(int location)
    {
        _location = location;
    }

    public int incrementAndGet()
    {
        return Interlocked.Increment(ref _location);
    }

    public int getAndIncrement()
    {
        var next = Interlocked.Increment(ref _location);
        return next - 1;
    }

    public int decrementAndGet()
    {
        return Interlocked.Decrement(ref _location);
    }

    public int read()
    {
        return Volatile.Read(ref _location);
    }

    public int set(int exchange)
    {
        return Interlocked.Exchange(ref _location, exchange);
    }

    public int decrease(int value)
    {
        return Interlocked.Add(ref _location, -value);
    }

    public bool compareAndSet(int expectedValue, int newValue)
    {
        var original = Interlocked.CompareExchange(ref _location, newValue, expectedValue);
        return original == expectedValue;
    }

    public int add(int value)
    {
        return Interlocked.Add(ref _location, value);
    }
}