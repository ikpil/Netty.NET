using System;
using System.Collections.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

public class LinkedBlockingQueue<T> : IQueue<T>
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

    public bool IsEmpty()
    {
        return 0 >= Count;
    }

    public bool TryRemove(T item)
    {
        throw new NotImplementedException();
    }

    public bool TryEnqueue(T item)
    {
        return _queue.TryAdd(item);
    }

    public bool TryDequeue(out T item)
    {
        return _queue.TryTake(out item);
    }

    public bool TryPeek(out T item)
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        while (_queue.TryTake(out var _))
        {
            // do nothing
        }
    }

    public int Drain(IConsumer<T> consumer, int limit)
    {
        int i = 0;
        for (i = 0; i < limit && _queue.TryTake(out var item); ++i)
        {
            consumer.accept(item);
        }

        return i;
    }
}