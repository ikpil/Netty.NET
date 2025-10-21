using System;

namespace Netty.NET.Common.Concurrent;

public class WeakReferenceQueueEntry<T> where T : class
{
    private readonly WeakReferenceQueue<T> _queue;
    private readonly WeakReference<T> _weak;

    ~WeakReferenceQueueEntry()
    {
        if (_weak.TryGetTarget(out var target))
        {
            _queue.Enqueue(target);
        }
    }
    
    public WeakReferenceQueueEntry(T obj, WeakReferenceQueue<T> queue)
    {
        _weak = new WeakReference<T>(obj);
        _queue = queue;
    }

    public virtual void clear()
    {
        _weak.SetTarget(null);
    }
}