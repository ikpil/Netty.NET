using System;
using System.Collections.Generic;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public class BlockingMessageQueue<T> : IQueue<T>
{
    private readonly object _lock = new object();
    private readonly Queue<T> _queue;
    private readonly int _boundedCapacity;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queue.Count;
            }
        }
    }

    public BlockingMessageQueue(int boundedCapacity)
    {
        _queue = new Queue<T>();
        _boundedCapacity = boundedCapacity;
    }


    public bool isEmpty()
    {
        lock (_lock)
        {
            return 0 >= _queue.Count;
        }
    }

    public bool tryRemove(T item)
    {
        throw new NotImplementedException();
    }

    public bool tryEnqueue(T item)
    {
        lock (_lock)
        {
            if (_boundedCapacity > 0 && _queue.Count >= _boundedCapacity)
            {
                return false;
            }

            _queue.Enqueue(item);
            return true;
        }
    }

    public bool tryDequeue(out T item)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out item);
        }
    }

    public bool tryPeek(out T item)
    {
        lock (_lock)
        {
            return _queue.TryPeek(out item);
        }
    }

    public void clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    public int drain(IConsumer<T> consumer, int limit)
    {
        lock (_lock)
        {
            int i = 0;
            for (i = 0; i < limit && _queue.TryDequeue(out var item); ++i)
            {
                consumer.accept(item);
            }

            return i;
        }
    }
}