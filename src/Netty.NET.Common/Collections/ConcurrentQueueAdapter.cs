using System;
using System.Collections.Concurrent;
using Netty.NET.Common.Functional;

namespace Netty.NET.Common.Collections;

// CLR adaptation for Netty's unbounded JCTools queue. ConcurrentQueue supplies
// concurrent publication and FIFO without Java Unsafe/VarHandle dependencies.
// Its segment sizes and multi-consumer support differ from the upstream queue.
internal sealed class ConcurrentQueueAdapter<T> : IQueue<T>
{
    private readonly ConcurrentQueue<T> queue = new();
    public int Count => queue.Count;
    public bool isEmpty() => queue.IsEmpty;
    public bool tryEnqueue(T item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        queue.Enqueue(item);
        return true;
    }
    public bool tryDequeue(out T item) => queue.TryDequeue(out item);
    public bool tryPeek(out T item) => queue.TryPeek(out item);
    public bool tryRemove(T item) => throw new NotSupportedException();
    public void clear()
    {
        while (queue.TryDequeue(out _)) { }
    }
    public int drain(IConsumer<T> consumer, int limit)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        if (limit < 0) throw new ArgumentOutOfRangeException(nameof(limit));
        int drained = 0;
        while (drained < limit && queue.TryDequeue(out T item))
        {
            consumer.accept(item);
            ++drained;
        }
        return drained;
    }
}
