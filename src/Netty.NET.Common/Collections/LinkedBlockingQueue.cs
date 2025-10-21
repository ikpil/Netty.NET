using System;
using System.Collections.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public class LinkedBlockingQueue<T> : IQueue<T>, IBlockingQueue<T>
{
    private readonly BlockingCollection<T> _queue;
    public int Count => _queue.Count;

    public LinkedBlockingQueue(int boundedCapacity)
        : this(new ConcurrentQueue<T>(), boundedCapacity)
    {
    }

    public LinkedBlockingQueue(IProducerConsumerCollection<T> collection, int boundedCapacity)
    {
        _queue = new BlockingCollection<T>(collection, boundedCapacity);
    }

    public bool isEmpty()
    {
        return 0 >= Count;
    }

    public bool tryRemove(T item)
    {
        throw new NotImplementedException();
    }

    public bool tryEnqueue(T item)
    {
        return _queue.TryAdd(item);
    }

    public bool tryDequeue(out T item)
    {
        return tryTake(out item);
    }

    public T take()
    {
        return _queue.Take();
    }

    public bool tryTake(out T item)
    {
        return _queue.TryTake(out item);
    }

    public bool tryTake(out T item, TimeSpan timeout)
    {
        return _queue.TryTake(out item, timeout);
    }

    public bool tryPeek(out T item)
    {
        return tryTake(out item);
    }

    public void clear()
    {
        while (_queue.TryTake(out var _))
        {
            // do nothing
        }
    }

    public int drain(IConsumer<T> consumer, int limit)
    {
        int i = 0;
        for (i = 0; i < limit && _queue.TryTake(out var item); ++i)
        {
            consumer.accept(item);
        }

        return i;
    }
}