using System;

namespace Netty.NET.Common.Concurrent;

public class WeakReferenceQueueElement<T> where T : class
{
    private readonly WeakReferenceQueue<T> _queue;
    private readonly WeakReference<T> _weak;

    ~WeakReferenceQueueElement()
    {
        if (_weak.TryGetTarget(out var target))
        {
            _queue.Enqueue(target);
        }
    }
    
    public WeakReferenceQueueElement(WeakReferenceQueue<T> queue, T obj)
    {
        _queue = queue;
        _weak = new WeakReference<T>(obj);
    }

    public void clear()
    {
        _weak.SetTarget(null);
    }
}