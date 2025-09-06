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


    public bool IsEmpty()
    {
        lock (_lock)
        {
            return 0 >= _queue.Count;
        }
    }

    public bool TryRemove(T item)
    {
        throw new NotImplementedException();
    }

    public bool TryEnqueue(T item)
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

    public bool TryDequeue(out T item)
    {
        lock (_lock)
        {
            return _queue.TryDequeue(out item);
        }
    }

    public bool TryPeek(out T item)
    {
        lock (_lock)
        {
            return _queue.TryPeek(out item);
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queue.Clear();
        }
    }

    public int Drain(IConsumer<T> consumer, int limit)
    {
        lock (_lock)
        {
            for (int i = 0; i < limit && _queue.TryDequeue(out var item); ++i)
            {
                consumer.accept(item);
            }
        }
    }
}