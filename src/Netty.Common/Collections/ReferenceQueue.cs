using System;
using System.Collections.Concurrent;

namespace Netty.NET.Common.Collections;

public class ReferenceQueueElement<T> where T : class 
{
    private readonly ReferenceQueue<T> _queue;
    private readonly WeakReference<T> _weak;

    public ReferenceQueueElement(ReferenceQueue<T> queue, T obj)
    {
        _queue = queue;
        _weak = new WeakReference<T>(obj);
    }

    ~ReferenceQueueElement()
    {
        if (_weak.TryGetTarget(out var target))
        {
            _queue.Enqueue(target);
        }        
    }
}

public class ReferenceQueue<T> where T : class
{
    private readonly ConcurrentQueue<T> _queue;

    public ReferenceQueue()
    {
        _queue = new();
    }

    public ReferenceQueueElement<T> Add(T obj)
    {
        return new ReferenceQueueElement<T>(this, obj);
    }

    public void Enqueue(T obj)
    {
        _queue.Enqueue(obj);
    }

    public void Dequeue(T obj)
    {
        
    }
}