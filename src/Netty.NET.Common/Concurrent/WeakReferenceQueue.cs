using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Netty.NET.Common.Concurrent;

public class WeakReferenceQueue<T> where T : class
{
    private readonly object _lock;
    private readonly ConcurrentQueue<T> _queue;

    public WeakReferenceQueue()
    {
        _lock = new object();
        _queue = new();
    }


    public void Enqueue(T obj)
    {
        lock (_lock)
        {
            _queue.Enqueue(obj);
        }
    }

    public T poll()
    {
        lock (_lock)
        {
            return poll0();
        }
    }

    private T poll0()
    {
        return _queue.TryDequeue(out var e)
            ? e
            : null;
    }

    public T remove()
    {
        lock (_lock)
        {
            return poll0();
        }
    }

    private T remove0(long timeoutMillis)
    {
        T r = poll0();
        if (r != null)
            return r;

        long start = SystemTimer.nanoTime();
        for (;;)
        {
            Monitor.Wait(_lock, TimeSpan.FromMilliseconds(timeoutMillis));
            r = poll0();
            if (r != null)
                return r;

            long end = SystemTimer.nanoTime();
            timeoutMillis -= (end - start) / 1000_000;
            if (timeoutMillis <= 0)
                return null;
            start = end;
        }
    }

    public T remove(long timeoutMillis)
    {
        if (timeoutMillis < 0)
            throw new ArgumentException("Negative timeout value");

        if (timeoutMillis == 0)
            return remove();

        lock (_lock)
        {
            return remove0(timeoutMillis);
        }
    }
}