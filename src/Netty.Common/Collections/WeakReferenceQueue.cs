using System;
using System.Collections.Concurrent;

namespace Netty.NET.Common.Collections;

public class WeakReferenceQueue<T> where T : class
{
    private readonly ConcurrentQueue<T> _queue;

    public WeakReferenceQueue()
    {
        _queue = new();
    }


    public void Enqueue(T obj)
    {
        _queue.Enqueue(obj);
    }

    public T poll()
    {
        return _queue.TryDequeue(out var e)
            ? e
            : null;
    }
}